"""Behavioural coverage for the real preview API, cancellation and bounded resources."""

import asyncio
import io
import json
import struct
import wave
from pathlib import Path
from uuid import uuid4

import pytest
from app.unity_preview import PreviewSettings, create_preview_app
from app.unity_preview.audio import validate_wav
from app.unity_preview.models import PREFIX, TurnRequest
from app.unity_preview.providers import FIXTURE_REPLY
from app.unity_preview.runtime import PreviewFailure, PreviewRuntime
from fastapi.testclient import TestClient
from jsonschema import Draft202012Validator, FormatChecker

TOKEN = "test_only_random_token_not_a_real_secret_0123456789"
ROOT = Path(__file__).resolve().parents[2] / "app/unity_preview"
HEADERS = {"Authorization": "Bearer " + TOKEN}


def submission(**changes):
    result = dict(
        protocol="unity-preview/1",
        request_id=str(uuid4()),
        generation=1,
        conversation_id=str(uuid4()),
        character_id="mao",
        text="你好，测试声音。",
        history=[],
        generate_audio=True,
        voice_id="fixture-tone",
    )
    result.update(changes)
    return result


def application(**settings):
    return create_preview_app(PreviewSettings(token=TOKEN, fixture_delay=0, **settings))


def client_for(app):
    return TestClient(
        app, base_url="http://127.0.0.1:8000", client=("127.0.0.1", 50234), headers=HEADERS
    )


def stream(response):
    assert response.status_code == 200, response.text
    assert response.headers["content-type"].startswith("application/x-ndjson")
    assert response.content.endswith(b"\n")
    return [json.loads(line) for line in response.iter_lines()]


def schema(name, value):
    document = json.loads((ROOT / "protocol" / (name + ".schema.json")).read_text("utf-8"))
    Draft202012Validator(document, format_checker=FormatChecker()).validate(value)


def input_wav(samples=None, rate=16000):
    if samples is None:
        samples = [0, 1000, -1000, 0] * 400
    file = io.BytesIO()
    with wave.open(file, "wb") as writer:
        writer.setnchannels(1)
        writer.setsampwidth(2)
        writer.setframerate(rate)
        writer.writeframes(struct.pack(f"<{len(samples)}h", *samples))
    return file.getvalue()


def test_actual_http_success_matches_wire_and_packaged_audio():
    app = application()
    request = submission()
    with client_for(app) as client:
        capabilities = client.get(PREFIX + "/capabilities")
        schema("capabilities", capabilities.json())
        assert capabilities.json()["chat_mode"] == "fixture"
        response = client.post(PREFIX + "/turns", json=request)
        events = stream(response)
        assert [item["type"] for item in events] == [
            "turn.accepted",
            "text.delta",
            "text.completed",
            "audio.ready",
            "generation.completed",
        ]
        assert [item["seq"] for item in events] == list(range(5))
        for event in events:
            schema("event", event)
            assert event["request_id"] == request["request_id"]
            assert event["conversation_id"] == request["conversation_id"]
            assert event["turn_id"] == events[0]["turn_id"]
        descriptor = events[3]["payload"]
        audio = client.get(descriptor["path"])
        assert audio.status_code == 200
        assert audio.content == (ROOT / "fixtures/demo-tone.wav").read_bytes()
        info = validate_wav(audio.content, 24000, 8388608, 120)
        assert descriptor["total_samples"] == info.total_samples == 192000
        assert b"JUNK" in audio.content[:64]
        assert response.headers["cache-control"] == "no-store"
        assert app.state.unity_preview.provider.llm_calls == 1
        assert app.state.unity_preview.speech.calls == 1


def test_text_only_never_calls_synthesizer():
    app = application()
    with client_for(app) as client:
        events = stream(client.post(PREFIX + "/turns", json=submission(generate_audio=False)))
        assert events[-2]["type"] == "audio.skipped"
        assert events[-2]["payload"] == {"reason": "user_disabled"}
        assert app.state.unity_preview.speech.calls == 0
        assert not app.state.unity_preview.audio


@pytest.mark.parametrize(
    "change",
    [
        {"protocol": "1"},
        {"request_id": str(uuid4()).upper()},
        {"request_id": "bad"},
        {"generation": -1},
        {"generation": 4294967296},
        {"generation": True},
        {"generation": "1"},
        {"conversation_id": ""},
        {"character_id": "unknown"},
        {"text": " "},
        {"text": "好" * 2001},
        {"history": [{"role": "system", "text": "x"}]},
        {"history": [{"role": "user", "text": "x"}] * 7},
        {"generate_audio": "true"},
        {"voice_id": "https://external.invalid/voice"},
        {"system": "override"},
        {"text": "x" * 2000, "history": [{"role": "user", "text": "x" * 2000}] * 6},
    ],
)
def test_rejects_untrusted_or_out_of_bounds_fields_before_provider(change):
    app = application()
    with client_for(app) as client:
        response = client.post(PREFIX + "/turns", json=submission(**change))
        assert response.status_code == 422
        schema("error", response.json())
        assert app.state.unity_preview.provider.llm_calls == 0


def test_rejects_body_overflow_missing_fields_duplicate_keys_and_invalid_utf8():
    app = application()
    with client_for(app) as client:
        for content in (b"x" * 65537, b"{}", b'{"text":"a","text":"b"}', b"\xff"):
            response = client.post(
                PREFIX + "/turns", content=content, headers={"Content-Type": "application/json"}
            )
            assert response.status_code == 422
        assert app.state.unity_preview.provider.llm_calls == 0


@pytest.mark.parametrize(
    "path,method",
    [
        ("/capabilities", "GET"),
        ("/turns", "POST"),
        ("/turns/33333333-3333-4333-8333-333333333333/audio.wav", "GET"),
        ("/requests/11111111-1111-4111-8111-111111111111/cancel", "POST"),
        ("/transcriptions", "POST"),
    ],
)
def test_all_preview_routes_require_token(path, method):
    with client_for(application()) as client:
        response = client.request(method, PREFIX + path, headers={"Authorization": "Bearer wrong"})
        assert response.status_code == 401
        assert TOKEN not in response.text
        schema("error", response.json())


@pytest.mark.parametrize(
    "headers",
    [
        {"Host": "evil.example"},
        {"Origin": "https://evil.example"},
        {"Origin": "http://127.0.0.1:8000"},
    ],
)
def test_rejects_host_rebinding_and_browser_origins(headers):
    with client_for(application()) as client:
        assert client.get(PREFIX + "/capabilities", headers=headers).status_code == 401


def test_rejects_non_loopback_peer_even_with_valid_token():
    app = application()
    with TestClient(app, base_url="http://127.0.0.1:8000", client=("192.0.2.5", 10)) as client:
        assert client.get(PREFIX + "/capabilities", headers=HEADERS).status_code == 401


def test_cloud_unconfigured_is_explicit_failure_without_fixture_provider():
    app = application(mode="cloud")
    with client_for(app) as client:
        capabilities = client.get(PREFIX + "/capabilities").json()
        assert capabilities["chat_mode"] == "cloud"
        assert capabilities["chat"] == {"available": False, "configured": False}
        response = client.post(PREFIX + "/turns", json=submission())
        assert response.status_code == 503
        assert response.json()["code"] == "provider_unavailable"
        assert app.state.unity_preview.provider.llm_calls == 0


def test_duplicate_and_cancel_before_accept_do_not_call_provider():
    app = application()
    with client_for(app) as client:
        request = submission()
        assert client.post(PREFIX + "/turns", json=request).status_code == 200
        duplicate = client.post(PREFIX + "/turns", json=request)
        assert duplicate.status_code == 409
        assert duplicate.json()["code"] == "duplicate_request"
        cancelled = submission()
        path = PREFIX + "/requests/" + cancelled["request_id"] + "/cancel"
        for _ in range(2):
            result = client.post(path, json={"generation": cancelled["generation"]})
            schema("cancel-result", result.json())
        rejected = client.post(PREFIX + "/turns", json=cancelled)
        assert rejected.status_code == 409
        assert rejected.json()["code"] == "request_cancelled"
        assert app.state.unity_preview.provider.llm_calls == 1


def test_cancel_releases_already_completed_audio():
    app = application()
    with client_for(app) as client:
        request = submission()
        events = stream(client.post(PREFIX + "/turns", json=request))
        path = events[3]["payload"]["path"]
        assert client.get(path).status_code == 200
        result = client.post(
            PREFIX + "/requests/" + request["request_id"] + "/cancel", json={"generation": 2}
        )
        assert result.status_code == 200
        assert client.get(path).status_code == 410


def test_resource_expiry_and_oldest_eviction_never_regenerate():
    now = [1000]
    app = application(clock=lambda: now[0], max_audio_bytes=500000)
    with client_for(app) as client:
        first = stream(client.post(PREFIX + "/turns", json=submission()))[3]["payload"]["path"]
        second = stream(client.post(PREFIX + "/turns", json=submission()))[3]["payload"]["path"]
        assert client.get(first).status_code == 410
        assert client.get(second).status_code == 200
        now[0] += 301
        assert client.get(second).status_code == 410
        assert app.state.unity_preview.provider.llm_calls == 2


@pytest.mark.parametrize("fill_cancel", [False, True])
def test_full_registry_refuses_new_request_without_discarding_tombstones(fill_cancel):
    now = [1000]
    app = application(clock=lambda: now[0], max_entries=2)
    with client_for(app) as client:
        ids = []
        for _ in range(2):
            request = submission(generate_audio=False)
            ids.append(request["request_id"])
            if fill_cancel:
                result = client.post(
                    PREFIX + "/requests/" + request["request_id"] + "/cancel",
                    json={"generation": 1},
                )
            else:
                result = client.post(PREFIX + "/turns", json=request)
            assert result.status_code == 200
        assert client.post(PREFIX + "/turns", json=submission()).status_code == 429
        if fill_cancel:
            assert set(app.state.unity_preview.cancelled) == set(ids)
        now[0] += 301
        assert client.post(PREFIX + "/turns", json=submission()).status_code == 200


@pytest.mark.parametrize(
    "scenario",
    [
        "tts_failure",
        "provider_timeout",
        "partial_stream",
        "seq_gap",
        "seq_duplicate",
        "wrong_ids",
        "corrupt_audio",
        "truncated_audio",
        "expired_audio",
    ],
)
def test_fault_scenarios_are_reproducible_and_explicit(scenario):
    app = application(scenario=scenario)
    with client_for(app) as client:
        events = stream(client.post(PREFIX + "/turns", json=submission()))
        if scenario == "tts_failure":
            assert events[-2]["type"] == "text.completed"
            assert events[-1]["type"] == "error"
            assert not any(event["type"] == "audio.skipped" for event in events)
        elif scenario == "provider_timeout":
            assert events[-1]["payload"]["code"] == "provider_timeout"
        elif scenario == "partial_stream":
            assert events[-1]["type"] == "text.delta"
        elif scenario == "seq_gap":
            assert [event["seq"] for event in events] == [0, 1, 3, 4, 5]
        elif scenario == "seq_duplicate":
            assert events[1] == events[2]
        elif scenario == "wrong_ids":
            assert events[-1]["request_id"] != events[0]["request_id"]
        else:
            response = client.get(events[3]["payload"]["path"])
            if scenario == "expired_audio":
                assert response.status_code == 410
            else:
                with pytest.raises(ValueError):
                    validate_wav(response.content, 24000, 8388608, 120)


def test_budget_scenario_is_429_before_generation():
    app = application(scenario="budget_exceeded")
    with client_for(app) as client:
        result = client.post(PREFIX + "/turns", json=submission())
        assert result.status_code == 429
        assert result.json()["code"] == "budget_exceeded"
        assert app.state.unity_preview.provider.llm_calls == 0


def test_fixture_asr_requires_valid_nonempty_non_silent_wav_and_returns_editable_fixture():
    app = application()
    with client_for(app) as client:
        for data, code in [
            (input_wav([]), "invalid_audio"),
            (input_wav([0] * 1600), "no_speech"),
            (b"bad", "invalid_audio"),
            (input_wav(rate=24000), "invalid_audio"),
        ]:
            response = client.post(
                PREFIX + "/transcriptions",
                content=data,
                headers={
                    "Content-Type": "audio/wav",
                    "X-Request-Id": str(uuid4()),
                    "X-Generation": "1",
                },
            )
            assert response.status_code == 422
            assert response.json()["code"] == code
        request_id = str(uuid4())
        response = client.post(
            PREFIX + "/transcriptions",
            content=input_wav(),
            headers={"Content-Type": "audio/wav", "X-Request-Id": request_id, "X-Generation": "2"},
        )
        assert response.status_code == 200
        schema("transcription", response.json())
        assert response.json()["request_id"] == request_id
        assert response.json()["generation"] == 2
        assert response.json()["mode"] == "fixture"
        assert response.json()["text"].startswith("【演示转写】")
        assert app.state.unity_preview.provider.asr_calls == 1


def test_active_request_replacement_and_cancel_drop_late_provider_results():
    async def exercise():
        runtime = PreviewRuntime(PreviewSettings(token=TOKEN))
        started = asyncio.Event()
        release = asyncio.Event()

        async def ignores_cancellation(text):
            started.set()
            try:
                await release.wait()
            except asyncio.CancelledError:
                await release.wait()
            return FIXTURE_REPLY

        runtime.provider.reply = ignores_cancellation
        first = TurnRequest.model_validate(submission())
        old = await runtime.begin(first.request_id, first.generation)
        events = runtime.events(first, old)
        assert json.loads(await anext(events))["type"] == "turn.accepted"
        waiting = asyncio.create_task(anext(events))
        await started.wait()
        second = await runtime.begin(str(uuid4()), 2)
        assert old.cancelled and runtime.active is second
        cancelled = json.loads(await asyncio.wait_for(waiting, 0.5))
        assert cancelled["type"] == "turn.cancelled"
        release.set()
        await events.aclose()
        assert not runtime.audio
        assert runtime.active is second
        await runtime.shutdown()

    asyncio.run(exercise())


def test_cancel_while_audio_is_pending_and_disconnect_release_state():
    async def exercise():
        runtime = PreviewRuntime(PreviewSettings(token=TOKEN, fixture_delay=0))
        ready = asyncio.Event()

        async def slow_audio(text, voice):
            ready.set()
            await asyncio.Event().wait()

        runtime.speech.synthesize = slow_audio
        request = TurnRequest.model_validate(submission())
        operation = await runtime.begin(request.request_id, request.generation)
        events = runtime.events(request, operation)
        for kind in ("turn.accepted", "text.delta", "text.completed"):
            assert json.loads(await anext(events))["type"] == kind
        pending = asyncio.create_task(anext(events))
        await ready.wait()
        await runtime.cancel(request.request_id, 2)
        assert json.loads(await asyncio.wait_for(pending, 0.5))["type"] == "turn.cancelled"
        await events.aclose()
        assert not runtime.audio
        assert runtime.active is None
        # Closing a stream before completion is a disconnect and invalidates output.
        next_request = TurnRequest.model_validate(submission())
        next_operation = await runtime.begin(next_request.request_id, 3)
        next_events = runtime.events(next_request, next_operation)
        await anext(next_events)
        await next_events.aclose()
        assert next_operation.cancelled
        assert runtime.active is None

    asyncio.run(exercise())


def test_real_provider_timeout_ends_stream_without_false_completion():
    app = create_preview_app(PreviewSettings(token=TOKEN, fixture_delay=0.05, llm_timeout=0.001))
    with client_for(app) as client:
        events = stream(client.post(PREFIX + "/turns", json=submission()))
        assert [event["type"] for event in events] == ["turn.accepted", "error"]
        assert events[-1]["payload"]["code"] == "provider_timeout"
        assert app.state.unity_preview.active is None


def test_schema_samples_and_audio_source_are_reproducible():
    from app.unity_preview.fixtures.generate_audio import fixture_wav

    assert fixture_wav() == (ROOT / "fixtures/demo-tone.wav").read_bytes()
    for path in (ROOT / "protocol").glob("*.ndjson"):
        for line in path.read_text("utf-8").splitlines():
            schema("event", json.loads(line))
    schema("turn-request", json.loads((ROOT / "protocol/sample-request.json").read_text("utf-8")))


def test_invalid_cloud_fault_configuration_is_rejected():
    with pytest.raises(ValueError, match="only available in fixture"):
        PreviewSettings(token=TOKEN, mode="cloud", scenario="tts_failure")


def test_wav_rejects_forged_lengths_duplicate_data_and_oversized_duration():
    data = input_wav()
    for invalid in (
        data[:-1],
        data + b"x",
        data[:20] + b"\x03\x00" + data[22:],
        input_wav([1] * (16000 * 30 + 1)),
    ):
        with pytest.raises(ValueError):
            validate_wav(invalid, 16000, 1048576, 30)


def test_cap_limit_keeps_unexpired_tombstone():
    async def exercise():
        runtime = PreviewRuntime(PreviewSettings(token=TOKEN, max_entries=1))
        first = str(uuid4())
        await runtime.cancel(first, 1)
        with pytest.raises(PreviewFailure) as caught:
            await runtime.cancel(str(uuid4()), 1)
        assert caught.value.status == 429
        assert first in runtime.cancelled

    asyncio.run(exercise())
