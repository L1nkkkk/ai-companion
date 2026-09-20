"""Recovery, resource limits and blocked transports for the offline fixture."""

import asyncio
import time
from uuid import uuid4

import pytest
from fastapi.testclient import TestClient

from mocks.protocol import AudioFrameHeader, encode_frame
from mocks.server import NewSession, Session, Turn, create_app
from mocks.test_server import (
    bootstrap,
    channels,
    command,
    inspect,
    receive,
    url,
)


@pytest.fixture
def client():
    with TestClient(create_app()) as test_client:
        yield test_client


def test_reconnect_restarts_client_seq_but_keeps_session_idempotency_window(client):
    session = bootstrap(client)
    with client.websocket_connect(url(session, "control")) as control:
        receive(control, "session.snapshot")
        control.send_json(command(session, "session.mute", {"muted": True}, seq=7))
        receive(control, "command.ack")
    state = client.app.state.sessions[session["session_id"]]
    assert len(state.idempotency) == 1
    ticket = client.post(
        f"/mock/sessions/{session['session_id']}/tickets",
        json={"device_id": session["device_id"], "channel": "control"},
    ).json()["ticket"]
    with client.websocket_connect(url(session, "control", ticket)) as control:
        snapshot, _ = receive(control, "session.snapshot")
        control.send_json(
            command(
                session,
                "session.resync",
                {"last_epoch": 1, "last_seq": 0},
                seq=1,
                session_epoch=snapshot["session_epoch"],
            )
        )
        assert receive(control, "command.ack")[0]["payload"]["status"] == "accepted"
        assert len(state.idempotency) == 2


def test_input_lease_expiry_clears_listening_state_and_stops_upload(client):
    session = bootstrap(client)
    with channels(client, session) as (control, audio):
        stream = str(uuid4())
        control.send_json(
            command(
                session,
                "input.audio.start",
                {"stream_id": stream, "sample_rate": 16000, "channels": 1, "codec": "pcm_s16le"},
            )
        )
        receive(control, "command.ack")
        state = client.app.state.sessions[session["session_id"]]
        state.devices[session["device_id"]].expires["input"] = time.monotonic() - 1
        snapshot, _ = receive(control, "session.snapshot")
        assert snapshot["payload"]["input_device_id"] is None
        assert snapshot["payload"]["state"] == "idle"
        audio.send_bytes(encode_frame(AudioFrameHeader(1, 1, 0, 16000, stream), b"\0\0" * 1600))
        assert receive(control, "error")[0]["payload"]["code"] == "lease_conflict"
        assert inspect(client, session)["stats"]["input_samples"] == 0


def test_epoch_exhaustion_is_controlled_for_takeover_and_reconnect(client):
    session = bootstrap(client, epoch=0xFFFFFFFE)
    endpoint = f"/mock/sessions/{session['session_id']}/devices"
    assert client.post(endpoint, json={"roles": ["speaker"], "takeover": True}).status_code == 201
    rejected = client.post(endpoint, json={"roles": ["speaker"], "takeover": True})
    assert rejected.status_code == 409
    state = inspect(client, session)
    assert state["session_epoch"] == 0xFFFFFFFF
    assert state["snapshot"]["state"] == "ended"

    reconnect_session = bootstrap(client, epoch=0xFFFFFFFE)
    for expected_state in ["idle", "idle", "ended"]:
        ticket = client.post(
            f"/mock/sessions/{reconnect_session['session_id']}/tickets",
            json={"device_id": reconnect_session["device_id"], "channel": "control"},
        ).json()["ticket"]
        with client.websocket_connect(url(reconnect_session, "control", ticket)) as control:
            snapshot, _ = receive(control, "session.snapshot")
            assert snapshot["session_epoch"] <= 0xFFFFFFFF
            assert snapshot["payload"]["state"] == expected_state


class CapturingSocket:
    def __init__(self):
        self.events = []
        self.closed = False

    async def send_json(self, event):
        self.events.append(event)

    async def close(self, **_):
        self.closed = True


class BlockedControl(CapturingSocket):
    async def send_json(self, _):
        await asyncio.Event().wait()


class BlockedAudio(CapturingSocket):
    def __init__(self):
        super().__init__()
        self.sending = asyncio.Event()

    async def send_bytes(self, _):
        self.sending.set()
        await asyncio.Event().wait()


def test_slow_observer_cannot_hold_the_session_lock_forever():
    async def scenario():
        session = Session(NewSession())
        main = session.add_device(["controller", "input", "speaker"])
        observer = session.add_device(["observer"])
        capture, blocked = CapturingSocket(), BlockedControl()
        session.connections["control"] = {main: capture, observer: blocked}
        turn = Turn(str(uuid4()), 1)
        session.active = turn

        async def broadcast():
            async with session.lock:
                await session.event("session.snapshot", session.snapshot())

        async def cancel():
            async with session.lock:
                await session.cancel("user_stop")

        sending = asyncio.create_task(broadcast())
        await asyncio.sleep(0)
        await asyncio.wait_for(cancel(), 0.7)
        await sending
        assert blocked.closed and observer not in session.connections["control"]
        assert turn.status == "cancelled"
        assert [e["type"] for e in capture.events] == ["session.snapshot", "turn.cancelled"]

    asyncio.run(scenario())


def test_slow_audio_transport_is_bounded_and_cannot_prevent_cancel():
    async def scenario():
        session = Session(NewSession())
        main = session.add_device(["controller", "input", "speaker"])
        capture, audio = CapturingSocket(), BlockedAudio()
        session.connections["control"][main] = capture
        session.connections["audio"][main] = audio
        async with session.lock:
            turn = await session.start_turn("slow transport", "user")
        await asyncio.wait_for(audio.sending.wait(), 0.7)

        async def cancel():
            async with session.lock:
                await session.cancel("user_stop")

        await asyncio.wait_for(cancel(), 0.7)
        assert turn.status == "cancelled"
        assert audio.closed and main not in session.connections["audio"]
        assert session.stats["output_frames"] == 0
        await asyncio.gather(*list(session.tasks))

    asyncio.run(scenario())
