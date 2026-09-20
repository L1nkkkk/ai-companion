"""Real ASGI WebSocket conversations; no canned fixture list is used as a timeline."""

import time
from contextlib import contextmanager
from datetime import UTC, datetime
from uuid import uuid4

import pytest
from fastapi.testclient import TestClient
from starlette.websockets import WebSocketDisconnect

from mocks.protocol import AudioFrameHeader, decode_frame, encode_frame, validate_event
from mocks.server import MOCK_TEXT, TOTAL_SAMPLES, create_app


@pytest.fixture
def client():
    with TestClient(create_app()) as test_client:
        yield test_client


def bootstrap(client, **options):
    response = client.post("/mock/sessions", json=options)
    assert response.status_code == 201, response.text
    return response.json()


def url(session, channel, ticket=None):
    return session[f"{channel}_path"] + "?ticket=" + (ticket or session[f"{channel}_ticket"])


def command(session, kind, payload, seq=1, **overrides):
    return {
        "schema_version": "1.0",
        "type": kind,
        "event_id": str(uuid4()),
        "session_id": session["session_id"],
        "session_epoch": session["session_epoch"],
        "device_id": session["device_id"],
        "client_seq": seq,
        "timestamp": datetime.now(UTC).isoformat().replace("+00:00", "Z"),
        "payload": payload,
        **overrides,
    }


def receive(socket, kind):
    observed = []
    for _ in range(40):
        event = socket.receive_json()
        validate_event(event, "server")
        observed.append(event)
        if event["type"] == kind:
            return event, observed
        assert event["type"] != "error", event
    pytest.fail(f"Expected {kind}; got {[e['type'] for e in observed]}")


def inspect(client, session):
    return client.get(f"/mock/sessions/{session['session_id']}").json()


@contextmanager
def channels(client, session):
    with client.websocket_connect(url(session, "control")) as control:
        snapshot, _ = receive(control, "session.snapshot")
        assert snapshot["session_epoch"] == session["session_epoch"]
        with client.websocket_connect(url(session, "audio")) as audio:
            yield control, audio


def test_mock_identity_rest_boundaries_and_single_use_channel_tickets(client):
    assert client.get("/health/live").json()["mode"] == "mock"
    assert client.post("/v1/sessions", json={}).status_code == 501
    assert client.get("/mock/assets/character-placeholder.svg").status_code == 200
    session = bootstrap(client)
    with pytest.raises(WebSocketDisconnect):
        with client.websocket_connect(url(session, "audio", session["control_ticket"])):
            pass
    with client.websocket_connect(url(session, "control")) as control:
        receive(control, "session.snapshot")
        with pytest.raises(WebSocketDisconnect):
            with client.websocket_connect(url(session, "control")):
                pass
    assert inspect(client, session)["session_epoch"] == 1


def test_full_generation_waits_for_completed_playback_and_event_id_is_idempotent(client):
    session = bootstrap(client)
    with channels(client, session) as (control, audio):
        submitted = command(session, "input.text.submit", {"text": "你好", "source": "user"})
        control.send_json(submitted)
        started, _ = receive(control, "turn.started")
        generated, timeline = receive(control, "turn.generation.completed")
        assert "assistant.segment" in [e["type"] for e in timeline]
        frames = [decode_frame(audio.receive_bytes()) for _ in range(3)]
        assert [f.header.frame_seq for f in frames] == [0, 1, 2]
        assert [f.header.offset_samples for f in frames] == [0, 2400, 4800]
        assert sum(f.sample_count for f in frames) == TOTAL_SAMPLES
        assert all(str(f.header.turn_id) == started["turn_id"] for f in frames)
        state = inspect(client, session)
        assert state["snapshot"]["active_turn_id"] == generated["turn_id"]
        assert state["turns"][0]["spoken_text"] == ""
        progress = {
            "turn_id": started["turn_id"],
            "stream_id": str(frames[0].header.stream_id),
            "segment_index": 0,
            "played_samples": TOTAL_SAMPLES,
            "status": "playing",
        }
        control.send_json(command(session, "playback.progress", progress, seq=2))
        receive(control, "command.ack")
        assert inspect(client, session)["snapshot"]["active_turn_id"] is not None
        control.send_json(
            command(session, "playback.progress", dict(progress, status="completed"), seq=3)
        )
        completed, _ = receive(control, "turn.completed")
        assert completed["payload"]["spoken_text"] == MOCK_TEXT
        receive(control, "command.ack")
        control.send_json(submitted)
        ack, _ = receive(control, "command.ack")
        assert ack["payload"]["status"] == "duplicate"
        assert ack["payload"]["result_turn_id"] == started["turn_id"]
        assert len(inspect(client, session)["turns"]) == 1
        control.send_json(dict(submitted, payload={"text": "不同意图", "source": "user"}))
        error, _ = receive(control, "error")
        assert error["payload"]["code"] == "idempotency_conflict"


@pytest.mark.parametrize("phase", ["generating", "before_audio", "playing"])
def test_cancel_is_processed_during_delay_and_terminal_prevents_late_output(client, phase):
    session = bootstrap(client)
    settings = {"generation_delay_ms": 0, "frame_interval_ms": 200}
    if phase == "generating":
        settings["generation_delay_ms"] = 1000
    elif phase == "before_audio":
        settings["audio_delay_ms"] = 1000
    assert (
        client.post(f"/mock/sessions/{session['session_id']}/faults", json=settings).status_code
        == 200
    )
    with channels(client, session) as (control, audio):
        control.send_json(
            command(session, "input.text.submit", {"text": "取消测试", "source": "user"})
        )
        started, _ = receive(control, "turn.started")
        if phase == "before_audio":
            receive(control, "assistant.segment")
        elif phase == "playing":
            audio.receive_bytes()
        before = time.monotonic()
        control.send_json(
            command(
                session,
                "turn.cancel",
                {"turn_id": started["turn_id"], "reason": "user_stop"},
                seq=2,
            )
        )
        cancelled, _ = receive(control, "turn.cancelled")
        assert time.monotonic() - before < 0.7
        assert cancelled["payload"]["spoken_text"] == ""
        frozen_frames = inspect(client, session)["stats"]["output_frames"]
        time.sleep(1.1)
        state = inspect(client, session)
        assert state["stats"]["output_frames"] == frozen_frames
        assert state["turns"][0]["status"] == "cancelled"
        control.send_json(
            command(session, "session.resync", {"last_epoch": 1, "last_seq": 0}, seq=3)
        )
        _, subsequent = receive(control, "session.snapshot")
        assert not any(e["type"].startswith(("assistant.", "avatar.")) for e in subsequent)


def test_schema_identity_old_epoch_and_observer_authority_rejected(client):
    session = bootstrap(client)
    device = client.post(
        f"/mock/sessions/{session['session_id']}/devices", json={"roles": ["observer"]}
    ).json()["device_id"]
    assert (
        client.post(
            f"/mock/sessions/{session['session_id']}/tickets",
            json={"device_id": device, "channel": "audio"},
        ).status_code
        == 403
    )
    ticket = client.post(
        f"/mock/sessions/{session['session_id']}/tickets",
        json={"device_id": device, "channel": "control"},
    ).json()["ticket"]
    with (
        channels(client, session) as (control, _),
        client.websocket_connect(url(session, "control", ticket)) as observer,
    ):
        receive(observer, "session.snapshot")
        observer.send_json(
            command(
                session, "input.text.submit", {"text": "无权限", "source": "user"}, device_id=device
            )
        )
        assert receive(observer, "error")[0]["payload"]["code"] == "forbidden"
        observer.send_json(
            command(
                session, "session.resync", {"last_epoch": 1, "last_seq": 0}, device_id=str(uuid4())
            )
        )
        assert receive(observer, "error")[0]["payload"]["code"] == "forbidden"
        control.send_json(
            command(session, "session.resync", {"last_epoch": 1, "last_seq": 0}, session_epoch=2)
        )
        assert receive(control, "error")[0]["payload"]["code"] == "stale_epoch"
        control.send_json({"type": "made.up"})
        assert receive(control, "error")[0]["payload"]["code"] == "invalid_event"
        control.send_text('{"schema_version":"1.0","schema_version":"1.0"}')
        assert receive(control, "error")[0]["payload"]["code"] == "invalid_event"
        control.send_text("x" * 65537)
        assert receive(control, "error")[0]["payload"]["code"] == "invalid_event"


def test_input_requires_acceptance_end_can_arrive_before_final_frame_and_mute_blocks_upload(client):
    session = bootstrap(client)
    with channels(client, session) as (control, audio):
        stream = str(uuid4())
        frame = encode_frame(AudioFrameHeader(1, 1, 0, 16000, stream), b"\0\0" * 1600)
        audio.send_bytes(frame)
        assert receive(control, "error")[0]["payload"]["code"] == "stream_unknown"
        control.send_json(
            command(
                session,
                "input.audio.start",
                {"stream_id": stream, "sample_rate": 16000, "channels": 1, "codec": "pcm_s16le"},
            )
        )
        receive(control, "input.audio.accepted")
        receive(control, "command.ack")
        control.send_json(
            command(
                session,
                "input.audio.end",
                {
                    "stream_id": stream,
                    "reason": "user_stop",
                    "last_frame_seq": 0,
                    "total_samples": 1600,
                },
                seq=2,
            )
        )
        receive(control, "command.ack")
        audio.send_bytes(frame)
        transcript, _ = receive(control, "input.transcript")
        assert "MOCK ASR" in transcript["payload"]["text"]
        started, _ = receive(control, "turn.started")
        control.send_json(
            command(
                session,
                "turn.cancel",
                {"turn_id": started["turn_id"], "reason": "user_stop"},
                seq=3,
            )
        )
        receive(control, "turn.cancelled")
        receive(control, "command.ack")
        control.send_json(command(session, "session.mute", {"muted": True}, seq=4))
        receive(control, "command.ack")
        before = inspect(client, session)["stats"]["input_samples"]
        audio.send_bytes(frame)
        assert receive(control, "error")[0]["payload"]["code"] == "invalid_state"
        assert inspect(client, session)["stats"]["input_samples"] == before


def test_input_end_timeout_is_an_explicit_failure(client):
    session = bootstrap(client)
    with channels(client, session) as (control, _):
        stream = str(uuid4())
        control.send_json(
            command(
                session,
                "input.audio.start",
                {"stream_id": stream, "sample_rate": 16000, "channels": 1, "codec": "pcm_s16le"},
            )
        )
        receive(control, "command.ack")
        control.send_json(
            command(
                session,
                "input.audio.end",
                {
                    "stream_id": stream,
                    "reason": "silence",
                    "last_frame_seq": 0,
                    "total_samples": 1600,
                },
                seq=2,
            )
        )
        receive(control, "command.ack")
        error, _ = receive(control, "error")
        assert error["payload"]["code"] == "invalid_audio"
        assert inspect(client, session)["turns"] == []


def test_cross_channel_delay_duplicate_control_and_stale_audio_are_explicit_faults(client):
    session = bootstrap(client, epoch=2)
    response = client.post(
        f"/mock/sessions/{session['session_id']}/faults",
        json={
            "control_delay_ms": 200,
            "audio_delay_ms": 10,
            "duplicate_events": True,
            "stale_epoch": True,
            "duplicate_audio_frames": True,
        },
    )
    assert response.status_code == 200
    with channels(client, session) as (control, audio):
        control.send_json(command(session, "input.text.submit", {"text": "故障", "source": "user"}))
        first = audio.receive_bytes()
        assert audio.receive_bytes() == first
        assert decode_frame(first).header.session_epoch == 1
        _, events = receive(control, "turn.generation.completed")
        deltas = [e for e in events if e["type"] == "assistant.text.delta"]
        assert len(deltas) == 2 and deltas[0] == deltas[1]


def test_disconnect_cancels_then_primary_reconnect_advances_epoch_and_never_replays_audio(client):
    session = bootstrap(client)
    client.post(
        f"/mock/sessions/{session['session_id']}/faults", json={"disconnect_after_frames": 1}
    )
    with channels(client, session) as (control, audio):
        control.send_json(command(session, "input.text.submit", {"text": "断线", "source": "user"}))
        audio.receive_bytes()
        cancelled, _ = receive(control, "turn.cancelled")
        assert cancelled["payload"]["reason"] == "disconnected"
        with pytest.raises(WebSocketDisconnect):
            control.receive_json()
    ticket = client.post(
        f"/mock/sessions/{session['session_id']}/tickets",
        json={"device_id": session["device_id"], "channel": "control"},
    ).json()["ticket"]
    with client.websocket_connect(url(session, "control", ticket)) as control:
        snapshot, _ = receive(control, "session.snapshot")
        assert snapshot["session_epoch"] == 2
        assert snapshot["payload"]["active_turn_id"] is None
        assert inspect(client, session)["stats"]["output_frames"] == 1


def test_observer_reconnect_does_not_advance_epoch_and_takeover_is_explicit(client):
    session = bootstrap(client)
    assert (
        client.post(
            f"/mock/sessions/{session['session_id']}/devices", json={"roles": ["speaker"]}
        ).status_code
        == 409
    )
    with channels(client, session):
        observer = client.post(
            f"/mock/sessions/{session['session_id']}/devices", json={"roles": ["observer"]}
        ).json()["device_id"]
        for _ in range(2):
            ticket = client.post(
                f"/mock/sessions/{session['session_id']}/tickets",
                json={"device_id": observer, "channel": "control"},
            ).json()["ticket"]
            with client.websocket_connect(url(session, "control", ticket)) as control:
                assert receive(control, "session.snapshot")[0]["session_epoch"] == 1
        taken = client.post(
            f"/mock/sessions/{session['session_id']}/devices",
            json={"roles": ["speaker"], "takeover": True},
        ).json()
        assert taken["session_epoch"] == 2
        assert inspect(client, session)["snapshot"]["speaker_device_id"] == taken["device_id"]


def test_live_mock_is_bounded_deduplicated_and_isolated_from_personal_mode(client):
    personal = bootstrap(client)
    assert (
        client.post(
            f"/mock/sessions/{personal['session_id']}/live", json={"text": "comment"}
        ).status_code
        == 409
    )
    session = bootstrap(client, mode="broadcast")
    endpoint = f"/mock/sessions/{session['session_id']}/live"
    first = client.post(endpoint, json={"text": "你好", "event_id": "stable-id"}).json()
    duplicate = client.post(endpoint, json={"text": "你好", "event_id": "stable-id"}).json()
    assert first["event"]["platform"] == "mock" and first["event"]["metadata"]["mock"]
    assert not duplicate["accepted"]
    for i in range(51):
        client.post(endpoint, json={"text": str(i), "event_id": str(i)})
    queue = inspect(client, session)["live"]
    assert queue["size"] == 50 and queue["dropped_total"] == 2
    with channels(client, session) as (control, _):
        control.send_json(command(session, "live.select", {"live_event_id": "50"}))
        started, _ = receive(control, "turn.started")
        assert started["payload"]["source"] == "live"


def test_expired_ticket_and_speaker_lease_are_not_silently_accepted(client):
    session = bootstrap(client)
    state = client.app.state.sessions[session["session_id"]]
    token = session["audio_ticket"]
    device, channel, _ = state.tickets[token]
    state.tickets[token] = (device, channel, time.monotonic() - 1)
    with pytest.raises(WebSocketDisconnect):
        with client.websocket_connect(url(session, "audio")):
            pass
    with client.websocket_connect(url(session, "control")) as control:
        receive(control, "session.snapshot")
        state.devices[session["device_id"]].expires["speaker"] = time.monotonic() - 1
        control.send_json(
            command(session, "input.text.submit", {"text": "expired", "source": "user"})
        )
        assert receive(control, "error")[0]["payload"]["code"] in {
            "lease_expired",
            "lease_conflict",
        }
