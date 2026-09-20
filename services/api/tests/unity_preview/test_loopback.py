"""Real TCP tests: transport disconnects must release the single active operation."""

import io
import json
import socket
import struct
import threading
import time
import wave
from uuid import uuid4

import httpx
import pytest
import uvicorn
from app.unity_preview import PreviewSettings, create_preview_app
from app.unity_preview.models import PREFIX

TOKEN = "test_tcp_only_not_a_real_access_token_0123456789"


def wait_until(predicate, seconds=3):
    deadline = time.monotonic() + seconds
    while time.monotonic() < deadline:
        if predicate():
            return
        time.sleep(0.01)
    assert predicate(), "Timed out waiting for server operation lifecycle"


@pytest.fixture
def live_server(request):
    settings = getattr(request, "param", {"fixture_delay": 2})
    app = create_preview_app(PreviewSettings(token=TOKEN, **settings))
    listener = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    listener.bind(("127.0.0.1", 0))
    port = listener.getsockname()[1]
    server = uvicorn.Server(
        uvicorn.Config(app, host="127.0.0.1", port=port, access_log=False, log_level="critical")
    )
    thread = threading.Thread(target=lambda: server.run(sockets=[listener]), daemon=True)
    thread.start()
    wait_until(lambda: server.started)
    try:
        yield f"http://127.0.0.1:{port}", port, app.state.unity_preview
    finally:
        server.should_exit = True
        thread.join(timeout=5)
        listener.close()
        assert not thread.is_alive()


def request_body():
    return dict(
        protocol="unity-preview/1",
        request_id=str(uuid4()),
        generation=1,
        conversation_id=str(uuid4()),
        character_id="mao",
        text="演示测试",
        history=[],
        generate_audio=True,
        voice_id="fixture-tone",
    )


def test_tcp_stream_disconnect_cancels_inflight_generation(live_server):
    url, _, runtime = live_server
    with httpx.Client(trust_env=False, headers={"Authorization": "Bearer " + TOKEN}) as client:
        with client.stream("POST", url + PREFIX + "/turns", json=request_body()) as response:
            assert response.status_code == 200
            assert json.loads(next(response.iter_lines()))["type"] == "turn.accepted"
            wait_until(lambda: runtime.active is not None and runtime.active.work is not None)
            operation = runtime.active
        wait_until(lambda: runtime.active is None)
        assert operation.cancelled
        assert not runtime.audio


def test_tcp_cancel_interrupts_stream_and_drops_audio(live_server):
    url, _, runtime = live_server
    body = request_body()
    with httpx.Client(trust_env=False, headers={"Authorization": "Bearer " + TOKEN}) as client:
        with client.stream("POST", url + PREFIX + "/turns", json=body) as response:
            lines = response.iter_lines()
            assert json.loads(next(lines))["type"] == "turn.accepted"
            result = client.post(
                url + PREFIX + "/requests/" + body["request_id"] + "/cancel", json={"generation": 2}
            )
            assert result.status_code == 200
            assert json.loads(next(lines))["type"] == "turn.cancelled"
            assert list(lines) == []
        wait_until(lambda: runtime.active is None)
        assert not runtime.audio


def test_tcp_asr_disconnect_releases_provider_without_persisting_recording(live_server):
    _, port, runtime = live_server
    audio = io.BytesIO()
    with wave.open(audio, "wb") as wav:
        wav.setnchannels(1)
        wav.setsampwidth(2)
        wav.setframerate(16000)
        wav.writeframes(struct.pack("<1600h", *([2000, -2000] * 800)))
    body = audio.getvalue()
    headers = (
        f"POST {PREFIX}/transcriptions HTTP/1.1\r\nHost: 127.0.0.1:{port}\r\n"
        f"Authorization: Bearer {TOKEN}\r\nContent-Type: audio/wav\r\n"
        f"X-Request-Id: {uuid4()}\r\nX-Generation: 1\r\nContent-Length: {len(body)}\r\n\r\n"
    ).encode()
    connection = socket.create_connection(("127.0.0.1", port), timeout=3)
    try:
        connection.sendall(headers + body)
        wait_until(lambda: runtime.active is not None and runtime.active.work is not None)
        operation = runtime.active
    finally:
        connection.close()
    wait_until(lambda: runtime.active is None)
    assert operation.cancelled
    assert not runtime.audio


@pytest.mark.parametrize(
    "live_server", [{"fixture_delay": 0, "scenario": "slow_generation"}], indirect=True
)
def test_slow_generation_is_accepted_before_wait_and_cancel_releases_without_reply(live_server):
    url, _, runtime = live_server
    body = request_body()
    with httpx.Client(trust_env=False, headers={"Authorization": "Bearer " + TOKEN}) as client:
        with client.stream("POST", url + PREFIX + "/turns", json=body) as response:
            lines = response.iter_lines()
            assert json.loads(next(lines))["type"] == "turn.accepted"
            assert runtime.provider.llm_calls == 0
            before = time.monotonic()
            result = client.post(
                url + PREFIX + "/requests/" + body["request_id"] + "/cancel", json={"generation": 2}
            )
            assert result.status_code == 200
            assert json.loads(next(lines))["type"] == "turn.cancelled"
            assert time.monotonic() - before < 1
            assert list(lines) == []
        assert runtime.provider.llm_calls == 0
        assert runtime.speech.calls == 0


@pytest.mark.parametrize(
    "live_server", [{"fixture_delay": 0, "scenario": "slow_download"}], indirect=True
)
@pytest.mark.parametrize("cancel_after_headers", [False, True])
def test_slow_download_sends_headers_then_waits_and_rechecks_cancel(
    live_server, cancel_after_headers
):
    url, _, runtime = live_server
    body = request_body()
    with httpx.Client(trust_env=False, headers={"Authorization": "Bearer " + TOKEN}) as client:
        generated = client.post(url + PREFIX + "/turns", json=body)
        events = [json.loads(line) for line in generated.iter_lines()]
        assert events[-1]["type"] == "generation.completed"
        path = next(event["payload"]["path"] for event in events if event["type"] == "audio.ready")
        before = time.monotonic()
        with client.stream("GET", url + path) as response:
            assert response.status_code == 200
            assert time.monotonic() - before < 1
            if cancel_after_headers:
                cancelled = client.post(
                    url + PREFIX + "/requests/" + body["request_id"] + "/cancel",
                    json={"generation": 2},
                )
                assert cancelled.status_code == 200
            data = response.read()
            assert time.monotonic() - before >= 2.8
            assert time.monotonic() - before < 5
        if cancel_after_headers:
            assert data == b""
            assert client.get(url + path).status_code == 410
            assert not runtime.audio
        else:
            assert data == runtime.speech.wav
