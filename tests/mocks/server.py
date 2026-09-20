"""T04 offline R1 fixture. No accounts, providers, database or production REST."""

import asyncio
import json
import struct
import time
from collections import OrderedDict, deque
from contextlib import asynccontextmanager, suppress
from dataclasses import dataclass, field
from datetime import UTC, datetime, timedelta
from pathlib import Path
from typing import Literal
from uuid import UUID, uuid4

from fastapi import FastAPI, HTTPException, WebSocket, WebSocketDisconnect
from fastapi.staticfiles import StaticFiles
from pydantic import BaseModel, ConfigDict, Field

from .live import MockLive
from .protocol import (
    AudioFrameHeader,
    AudioProtocolError,
    decode_control,
    decode_frame,
    encode_frame,
    validate_event,
)

MOCK_TEXT = "[MOCK] 这是离线测试回应，不是真实 AI。"
TOTAL_SAMPLES = 7200
SEND_TIMEOUT_SECONDS = 0.1
# Integer-only square-wave fixture: 300 ms, 500 Hz, mono PCM16LE at 24 kHz.
PCM = b"".join(struct.pack("<h", 1200 if i % 48 < 24 else -1200) for i in range(TOTAL_SAMPLES))
Role = Literal["controller", "input", "speaker", "observer"]


def utc() -> str:
    return datetime.now(UTC).isoformat().replace("+00:00", "Z")


async def bounded_close(socket, code=1012, reason="MOCK channel unavailable"):
    with suppress(TimeoutError, WebSocketDisconnect, RuntimeError, OSError):
        await asyncio.wait_for(socket.close(code=code, reason=reason), SEND_TIMEOUT_SECONDS)


class Settings(BaseModel):
    model_config = ConfigDict(extra="forbid")


class NewSession(Settings):
    mode: Literal["personal", "broadcast"] = "personal"
    epoch: int = Field(default=1, ge=1, le=0xFFFFFFFE)


class NewDevice(Settings):
    roles: list[Role] = Field(default_factory=lambda: ["observer"], min_length=1, max_length=4)
    takeover: bool = False


class NewTicket(Settings):
    device_id: UUID
    channel: Literal["control", "audio"]


class Faults(Settings):
    generation_delay_ms: int = Field(default=20, ge=0, le=5000)
    control_delay_ms: int = Field(default=0, ge=0, le=5000)
    audio_delay_ms: int = Field(default=0, ge=0, le=5000)
    frame_interval_ms: int = Field(default=5, ge=0, le=1000)
    duplicate_events: bool = False
    stale_epoch: bool = False
    duplicate_audio_frames: bool = False
    disconnect_after_frames: int | None = Field(default=None, ge=1, le=3)


class NewComment(Settings):
    text: str = Field(min_length=1, max_length=4000)
    event_id: str | None = Field(default=None, min_length=1, max_length=256)


@dataclass
class Device:
    roles: set[str]
    expires: dict[str, float] = field(default_factory=dict)
    last_client_seq: int = 0
    rate: deque = field(default_factory=deque)


@dataclass
class Turn:
    id: str
    epoch: int
    stream: str = field(default_factory=lambda: str(uuid4()))
    status: str = "generating"
    generated: bool = False
    playback_completed: bool = False
    played: int = 0
    sent: int = 0
    spoken_text: str = ""


@dataclass
class InputStream:
    id: str
    device: str
    epoch: int
    samples: int = 0
    frames: int = 0
    ending: bool = False
    changed: asyncio.Event = field(default_factory=asyncio.Event)


class CommandError(Exception):
    def __init__(self, code: str, message: str):
        self.code, self.message = code, message


class Session:
    def __init__(self, request: NewSession):
        self.id = str(uuid4())
        self.epoch, self.mode = request.epoch, request.mode
        self.seq = 0
        self.state = "idle"
        self.muted = False
        self.active: Turn | None = None
        self.turns: OrderedDict[str, Turn] = OrderedDict()
        self.devices: dict[str, Device] = {}
        self.holders: dict[str, str | None] = {"input": None, "speaker": None}
        self.connections: dict[str, dict[str, WebSocket]] = {"control": {}, "audio": {}}
        self.tickets: dict[str, tuple[str, str, float]] = {}
        self.idempotency: OrderedDict[str, tuple[float, str, dict]] = OrderedDict()
        self.inputs: dict[str, InputStream] = {}
        self.used_streams: set[str] = set()
        self.recover = False
        self.faults = Faults()
        self.live = MockLive()
        self.lock = asyncio.Lock()
        self.tasks: set[asyncio.Task] = set()
        self.stats = {"input_samples": 0, "output_frames": 0, "rejected": 0}

    def task(self, coroutine):
        task = asyncio.create_task(coroutine)
        self.tasks.add(task)
        task.add_done_callback(self.tasks.discard)
        return task

    def add_device(self, roles: list[str]) -> str:
        device_id = str(uuid4())
        device = Device(set(roles))
        for role in device.roles & {"input", "speaker"}:
            self.holders[role] = device_id
            device.expires[role] = time.monotonic() + 30
        self.devices[device_id] = device
        return device_id

    def ticket(self, device: str, channel: str) -> str:
        now = time.monotonic()
        self.tickets = {k: v for k, v in self.tickets.items() if v[2] > now}
        if len(self.tickets) >= 128:
            raise HTTPException(429, "MOCK ticket limit")
        token = str(uuid4())
        self.tickets[token] = (device, channel, now + 30)
        return token

    def role(self, device_id: str, role: str):
        device = self.devices[device_id]
        if role not in device.roles:
            raise CommandError("forbidden", f"MOCK command requires {role}")
        if role in self.holders:
            if self.holders[role] != device_id:
                raise CommandError("lease_conflict", "Device does not own this lease")
            if device.expires.get(role, 0) <= time.monotonic():
                raise CommandError("lease_expired", "MOCK lease expired; attach a device again")

    def snapshot(self) -> dict:
        return {
            "state": self.state,
            "mode": self.mode,
            "muted": self.muted,
            "active_turn_id": self.active.id if self.active else None,
            "input_device_id": self.holders["input"],
            "speaker_device_id": self.holders["speaker"],
        }

    async def event(
        self,
        kind: str,
        payload: dict,
        turn: Turn | None = None,
        target: str | None = None,
        duplicate: bool = False,
    ) -> dict:
        self.seq += 1
        event = {
            "schema_version": "1.0",
            "type": kind,
            "event_id": str(uuid4()),
            "session_id": self.id,
            "session_epoch": self.epoch,
            "seq": self.seq,
            "timestamp": utc(),
            "turn_id": turn.id if turn else None,
            "payload": payload,
        }
        validate_event(event, "server")

        async def deliver(device, socket):
            try:
                await asyncio.wait_for(socket.send_json(event), SEND_TIMEOUT_SECONDS)
                if duplicate:
                    await asyncio.wait_for(socket.send_json(event), SEND_TIMEOUT_SECONDS)
                return False
            except (TimeoutError, WebSocketDisconnect, RuntimeError, OSError):
                if self.connections["control"].get(device) is socket:
                    self.connections["control"].pop(device)
                await bounded_close(socket)
                return "controller" in self.devices[device].roles

        lost = await asyncio.gather(
            *(
                deliver(device, socket)
                for device, socket in list(self.connections["control"].items())
                if target is None or device == target
            )
        )
        if any(lost) and self.state != "ended":
            self.recover = True
            self.inputs.clear()
            await self.cancel("disconnected")
        return event

    async def advance_epoch(self) -> bool:
        if self.epoch == 0xFFFFFFFF:
            await self.cancel("failure")
            self.inputs.clear()
            self.holders = {"input": None, "speaker": None}
            self.state = "ended"
            self.recover = False
            return False
        self.epoch += 1
        self.seq = 0
        return True

    async def error(self, device: str, code: str, message: str, related: str | None = None):
        self.stats["rejected"] += 1
        await self.event(
            "error",
            {
                "code": code,
                "message": message[:500],
                "retryable": False,
                "related_event_id": related,
            },
            target=device,
        )

    async def cancel(self, reason: str):
        turn = self.active
        if turn is None:
            return
        turn.status = "cancelled"
        self.active = None
        self.state = "muted" if self.muted else "idle"
        await self.event(
            "turn.cancelled", {"reason": reason, "spoken_text": turn.spoken_text}, turn
        )

    async def start_turn(self, text: str, source: str) -> Turn:
        if self.active or self.inputs:
            raise CommandError("invalid_state", "MOCK supports one active turn or input stream")
        speaker = self.holders["speaker"]
        if speaker is None:
            raise CommandError("lease_conflict", "No speaker is attached")
        self.role(speaker, "speaker")
        turn = Turn(str(uuid4()), self.epoch)
        self.active = turn
        self.turns[turn.id] = turn
        while len(self.turns) > 100:
            self.turns.popitem(last=False)
        self.state = "thinking"
        await self.event("turn.started", {"source": source, "input_text": text}, turn)
        self.task(self.generate(turn, self.faults.model_copy()))
        return turn

    def current(self, turn: Turn) -> bool:
        return (
            self.active is turn
            and turn.epoch == self.epoch
            and turn.status not in {"cancelled", "completed", "failed"}
        )

    async def control_output(self, turn: Turn, faults: Faults):
        await asyncio.sleep(faults.control_delay_ms / 1000)
        async with self.lock:
            if not self.current(turn):
                return
            await self.event(
                "assistant.text.delta", {"text": MOCK_TEXT}, turn, duplicate=faults.duplicate_events
            )
            await self.event(
                "assistant.segment",
                {
                    "segment_index": 0,
                    "stream_id": turn.stream,
                    "text": MOCK_TEXT,
                    "emotion": "neutral",
                    "gesture": "none",
                    "sample_rate": 24000,
                    "channels": 1,
                    "codec": "pcm_s16le",
                },
                turn,
            )
            await self.event(
                "avatar.state", {"emotion": "neutral", "gesture": "none", "segment_index": 0}, turn
            )

    async def generate(self, turn: Turn, faults: Faults):
        await asyncio.sleep(faults.generation_delay_ms / 1000)
        # Segment is logically produced first. Independent delayed delivery models transport skew.
        delivery = self.task(self.control_output(turn, faults))
        await asyncio.sleep(faults.audio_delay_ms / 1000)
        if not faults.control_delay_ms:
            await delivery
        for seq in range(3):
            async with self.lock:
                if not self.current(turn):
                    return
                speaker = self.holders["speaker"]
                try:
                    self.role(speaker, "speaker")
                except (CommandError, KeyError):
                    await self.cancel("disconnected")
                    return
                socket = self.connections["audio"].get(speaker)
                if socket is None:
                    await self.cancel("disconnected")
                    return
                epoch = turn.epoch - 1 if faults.stale_epoch else turn.epoch
                frame = encode_frame(
                    AudioFrameHeader(2, epoch, seq, 24000, turn.stream, turn.id, 0, seq * 2400),
                    PCM[seq * 4800 : (seq + 1) * 4800],
                )
                try:
                    await asyncio.wait_for(socket.send_bytes(frame), SEND_TIMEOUT_SECONDS)
                    if faults.duplicate_audio_frames:
                        await asyncio.wait_for(socket.send_bytes(frame), SEND_TIMEOUT_SECONDS)
                except (TimeoutError, WebSocketDisconnect, RuntimeError, OSError):
                    self.connections["audio"].pop(speaker, None)
                    self.recover = True
                    await bounded_close(socket)
                    await self.cancel("disconnected")
                    return
                turn.sent += 2400
                self.stats["output_frames"] += 1
                if faults.disconnect_after_frames == seq + 1:
                    await self.cancel("disconnected")
                    self.recover = True
                    await bounded_close(socket, reason="MOCK injected disconnect")
                    control = self.connections["control"].get(speaker)
                    if control:
                        await bounded_close(control, reason="MOCK injected disconnect")
                    return
            await asyncio.sleep(faults.frame_interval_ms / 1000)
        await delivery
        async with self.lock:
            if not self.current(turn):
                return
            await self.event(
                "assistant.audio.end",
                {"stream_id": turn.stream, "segment_index": 0, "total_samples": TOTAL_SAMPLES},
                turn,
            )
            turn.generated = True
            turn.status = "playing"
            self.state = "speaking"
            await self.event("turn.generation.completed", {"finish_reason": "stop"}, turn)
            if turn.playback_completed:
                await self.complete(turn)

    async def complete(self, turn: Turn):
        turn.status, turn.spoken_text = "completed", MOCK_TEXT
        self.active = None
        self.state = "muted" if self.muted else "idle"
        await self.event(
            "turn.completed", {"spoken_text": MOCK_TEXT, "completion_reason": "played"}, turn
        )

    async def finish_input(self, stream: InputStream, payload: dict):
        deadline = time.monotonic() + 0.5
        while True:
            async with self.lock:
                if self.inputs.get(stream.id) is not stream or stream.epoch != self.epoch:
                    return
                expected = payload["total_samples"]
                last = stream.frames - 1 if stream.frames else None
                if stream.samples == expected and last == payload["last_frame_seq"]:
                    self.inputs.pop(stream.id)
                    self.state = "idle"
                    if expected:
                        await self.event(
                            "input.transcript",
                            {
                                "stream_id": stream.id,
                                "text": "[MOCK ASR] 固定测试转写",
                                "is_final": True,
                            },
                        )
                        try:
                            await self.start_turn("[MOCK ASR] 固定测试转写", "user")
                        except CommandError as exc:
                            await self.error(stream.device, exc.code, exc.message)
                    return
                if stream.samples > expected or time.monotonic() >= deadline:
                    self.inputs.pop(stream.id)
                    self.state = "idle"
                    await self.error(
                        stream.device, "invalid_audio", "Input incomplete or end mismatch"
                    )
                    return
                stream.changed.clear()
            with suppress(TimeoutError):
                await asyncio.wait_for(
                    stream.changed.wait(), max(0.001, deadline - time.monotonic())
                )

    async def command(self, device_id: str, event: dict):
        device = self.devices[device_id]
        now = time.monotonic()
        while device.rate and now - device.rate[0] >= 1:
            device.rate.popleft()
        if len(device.rate) >= 100:
            raise CommandError("rate_limited", "MOCK maximum 100 control messages per second")
        device.rate.append(now)
        if event["session_id"] != self.id or event["device_id"] != device_id:
            raise CommandError("forbidden", "Ticket identity does not match event")
        if event["session_epoch"] != self.epoch:
            raise CommandError("stale_epoch", "Request a current snapshot")
        kind, payload = event["type"], event["payload"]
        if kind == "playback.progress":
            self.role(device_id, "speaker")
        elif kind.startswith("input.audio"):
            self.role(device_id, "input")
        elif kind not in {"lease.renew", "session.resync"}:
            self.role(device_id, "controller")
        while self.idempotency and (
            now - next(iter(self.idempotency.values()))[0] >= 600 or len(self.idempotency) >= 1000
        ):
            self.idempotency.popitem(last=False)
        fingerprint = json.dumps(
            {k: event[k] for k in ("type", "payload", "device_id", "session_epoch")}, sort_keys=True
        )
        cached = self.idempotency.get(event["event_id"])
        if cached:
            if cached[1] != fingerprint:
                raise CommandError("idempotency_conflict", "Same event_id has different intent")
            ack = dict(cached[2], status="duplicate")
            await self.event("command.ack", ack, target=device_id)
            return
        if event["client_seq"] <= device.last_client_seq:
            raise CommandError("invalid_event", "client_seq must increase for this device")
        if self.state == "ended" and kind not in {"session.resync", "session.end"}:
            raise CommandError("session_ended", "MOCK session has ended")
        result = None
        expires = None
        if kind == "input.text.submit":
            result = (await self.start_turn(payload["text"], payload["source"])).id
        elif kind == "turn.cancel":
            turn = self.turns.get(payload["turn_id"])
            if turn is None:
                raise CommandError("invalid_state", "Unknown turn")
            if self.active is turn:
                await self.cancel(payload["reason"])
            result = turn.id
        elif kind == "session.mute":
            self.muted = payload["muted"]
            if self.muted:
                self.inputs.clear()
            if not self.active:
                self.state = "muted" if self.muted else "idle"
            await self.event("session.snapshot", self.snapshot())
        elif kind == "session.end":
            await self.cancel("session_end")
            self.inputs.clear()
            self.live.auto_reply = False
            self.state = "ended"
            self.holders = {"input": None, "speaker": None}
            await self.event("session.snapshot", self.snapshot())
        elif kind == "session.resync":
            await self.event("session.snapshot", self.snapshot(), target=device_id)
        elif kind == "lease.renew":
            for role in payload["roles"]:
                self.role(device_id, role)
            for role in payload["roles"]:
                device.expires[role] = now + 30
            expires = (datetime.now(UTC) + timedelta(seconds=30)).isoformat().replace("+00:00", "Z")
        elif kind == "playback.progress":
            turn = self.turns.get(payload["turn_id"])
            if not turn or not self.current(turn) or payload["stream_id"] != turn.stream:
                raise CommandError("invalid_state", "No matching active playback")
            played = payload["played_samples"]
            if payload["segment_index"] != 0 or not turn.played <= played <= turn.sent:
                raise CommandError("invalid_audio", "Playback progress is outside sent samples")
            if payload["status"] == "completed" and played != TOTAL_SAMPLES:
                raise CommandError("invalid_audio", "Completed must cover all fixture samples")
            turn.played = played
            turn.playback_completed = payload["status"] == "completed"
            result = turn.id
            if payload["status"] == "stopped":
                await self.cancel("user_stop")
            elif payload["status"] == "completed" and turn.generated:
                await self.complete(turn)
        elif kind == "input.audio.start":
            stream_id = payload["stream_id"]
            if self.muted or self.inputs or self.active or stream_id in self.used_streams:
                raise CommandError("invalid_state", "Muted, busy or reused input stream")
            if len(self.used_streams) >= 1000:
                raise CommandError("rate_limited", "MOCK session input stream limit")
            self.used_streams.add(stream_id)
            self.inputs[stream_id] = InputStream(stream_id, device_id, self.epoch)
            self.state = "listening"
            await self.event("input.audio.accepted", {"stream_id": stream_id}, target=device_id)
        elif kind == "input.audio.end":
            stream = self.inputs.get(payload["stream_id"])
            if not stream or stream.device != device_id or stream.ending:
                raise CommandError("stream_unknown", "No matching accepted input stream")
            if payload["total_samples"] > 480000:
                raise CommandError("invalid_audio", "Input exceeds 30 seconds")
            stream.ending = True
            self.task(self.finish_input(stream, payload))
        elif kind in {"live.select", "live.auto_reply"}:
            if self.mode != "broadcast":
                raise CommandError("forbidden", "Live controls require broadcast mode")
            if kind == "live.auto_reply":
                self.live.auto_reply = payload["enabled"]
            else:
                if self.active or self.inputs:
                    raise CommandError("invalid_state", "Current turn must finish or be cancelled")
                selected = self.live.queue.get(payload["live_event_id"])
                self.live.expire()
                if not selected or payload["live_event_id"] not in self.live.queue:
                    raise CommandError("invalid_state", "Live comment missing or expired")
                result = (await self.start_turn(selected[1]["text"], "live")).id
                self.live.select(payload["live_event_id"])
            await self.event("live.queue", self.live.snapshot())
        device.last_client_seq = event["client_seq"]
        ack = {
            "related_event_id": event["event_id"],
            "status": "accepted",
            "result_turn_id": result,
            "lease_expires_at": expires,
        }
        self.idempotency[event["event_id"]] = (now, fingerprint, ack)
        await self.event("command.ack", ack, target=device_id)

    async def input_audio(self, device: str, data: bytes):
        self.role(device, "input")
        if self.muted or self.state == "ended":
            raise CommandError("invalid_state", "Microphone uploads are disabled")
        frame = decode_frame(data)
        header = frame.header
        if header.kind != 1:
            raise CommandError("invalid_audio", "Clients may only upload input frames")
        if header.session_epoch != self.epoch:
            raise CommandError("stale_epoch", "Input frame epoch is obsolete")
        stream = self.inputs.get(str(header.stream_id))
        if not stream or stream.device != device:
            raise CommandError("stream_unknown", "Input requires an accepted stream")
        if header.frame_seq != stream.frames or header.offset_samples != stream.samples:
            self.inputs.pop(stream.id)
            self.state = "idle"
            raise CommandError("invalid_audio", "Input sequence or offset discontinuity")
        if stream.samples + frame.sample_count > 480000:
            self.inputs.pop(stream.id)
            raise CommandError("invalid_audio", "Input exceeds 30 seconds")
        stream.frames += 1
        stream.samples += frame.sample_count
        self.stats["input_samples"] += frame.sample_count
        stream.changed.set()

    async def watchdog(self):
        while self.state != "ended":
            await asyncio.sleep(0.1)
            async with self.lock:
                for role, device in list(self.holders.items()):
                    if device and self.devices[device].expires.get(role, 0) <= time.monotonic():
                        self.holders[role] = None
                        if role == "speaker":
                            await self.cancel("disconnected")
                            self.live.auto_reply = False
                        else:
                            self.inputs.clear()
                            if self.state == "listening":
                                self.state = "muted" if self.muted else "idle"
                        await self.event("session.snapshot", self.snapshot())


def create_app() -> FastAPI:
    sessions: dict[str, Session] = {}

    @asynccontextmanager
    async def lifespan(_):
        yield
        tasks = [task for session in sessions.values() for task in session.tasks]
        for task in tasks:
            task.cancel()
        await asyncio.gather(*tasks, return_exceptions=True)

    app = FastAPI(title="MOCK — T04 offline R1 fixture", lifespan=lifespan)
    app.state.sessions = sessions

    def get_session(session_id: str) -> Session:
        if session_id not in sessions:
            raise HTTPException(404, "MOCK session does not exist; use /mock/sessions")
        return sessions[session_id]

    @app.get("/health/live")
    async def health():
        return {"status": "ok", "mode": "mock", "scope": "T04 offline R1 fixtures"}

    @app.post("/mock/sessions", status_code=201)
    async def new_session(request: NewSession):
        if len(sessions) >= 32:
            raise HTTPException(429, "MOCK limit of 32 sessions; restart fixture to reset")
        session = Session(request)
        sessions[session.id] = session
        device = session.add_device(["controller", "input", "speaker", "observer"])
        session.task(session.watchdog())
        return {
            "mode": "mock",
            "session_id": session.id,
            "device_id": device,
            "session_epoch": session.epoch,
            "control_ticket": session.ticket(device, "control"),
            "audio_ticket": session.ticket(device, "audio"),
            "control_path": f"/v1/sessions/{session.id}/control",
            "audio_path": f"/v1/sessions/{session.id}/audio",
        }

    @app.get("/mock/sessions/{session_id}")
    async def inspect(session_id: str):
        session = get_session(session_id)
        async with session.lock:
            return {
                "mode": "mock",
                "session_id": session.id,
                "session_epoch": session.epoch,
                "snapshot": session.snapshot(),
                "stats": session.stats,
                "turns": [
                    {
                        "turn_id": t.id,
                        "status": t.status,
                        "generated": t.generated,
                        "played_samples": t.played,
                        "spoken_text": t.spoken_text,
                        "stream_id": t.stream,
                    }
                    for t in session.turns.values()
                ],
                "live": {
                    "platform": "mock",
                    "capabilities": session.live.capabilities,
                    **session.live.snapshot(),
                },
            }

    @app.post("/mock/sessions/{session_id}/devices", status_code=201)
    async def add_device(session_id: str, request: NewDevice):
        session = get_session(session_id)
        async with session.lock:
            if session.state == "ended" or len(session.devices) >= 16:
                raise HTTPException(409, "MOCK ended session or device limit")
            conflicts = [r for r in request.roles if r in session.holders and session.holders[r]]
            if conflicts and not request.takeover:
                raise HTTPException(409, "Explicit takeover=true required")
            if conflicts:
                await session.cancel("device_takeover")
                session.inputs.clear()
                if not await session.advance_epoch():
                    await session.event("session.snapshot", session.snapshot())
                    raise HTTPException(409, "MOCK epoch exhausted; create a new session")
                for role in conflicts:
                    session.devices[session.holders[role]].roles.discard(role)
            device = session.add_device(request.roles)
            await session.event("session.snapshot", session.snapshot())
            return {"mode": "mock", "device_id": device, "session_epoch": session.epoch}

    @app.post("/mock/sessions/{session_id}/tickets")
    async def ticket(session_id: str, request: NewTicket):
        session = get_session(session_id)
        device = str(request.device_id)
        if device not in session.devices:
            raise HTTPException(403, "Unknown mock device")
        if request.channel == "audio" and not session.devices[device].roles & {"input", "speaker"}:
            raise HTTPException(403, "Observers cannot connect to audio")
        return {
            "mode": "mock",
            "ticket": session.ticket(device, request.channel),
            "ttl_seconds": 30,
        }

    @app.post("/mock/sessions/{session_id}/faults")
    async def faults(session_id: str, request: Faults):
        session = get_session(session_id)
        if request.stale_epoch and session.epoch <= 1:
            raise HTTPException(422, "stale_epoch requires a session created with epoch >= 2")
        session.faults = request
        return {"mode": "mock", **request.model_dump()}

    @app.post("/mock/sessions/{session_id}/live")
    async def live(session_id: str, request: NewComment):
        session = get_session(session_id)
        async with session.lock:
            if session.mode != "broadcast" or session.state == "ended":
                raise HTTPException(409, "Live fixture requires an active broadcast session")
            event, accepted = session.live.add(request.text, request.event_id)
            if accepted and session.live.auto_reply and not session.active and not session.inputs:
                try:
                    await session.start_turn(request.text, "live")
                    session.live.select(event["platform_event_id"])
                except CommandError:
                    # Keep queued until explicitly selected; no fake successful reply.
                    pass
            await session.event("live.queue", session.live.snapshot())
            return {
                "mode": "mock",
                "accepted": accepted,
                "event": event,
                "queue": session.live.snapshot(),
            }

    @app.post("/mock/sessions/{session_id}/disconnect")
    async def disconnect(session_id: str):
        session = get_session(session_id)
        async with session.lock:
            await session.cancel("disconnected")
            session.inputs.clear()
            session.recover = True
            for channel in session.connections.values():
                for socket in list(channel.values()):
                    await bounded_close(socket, reason="MOCK injected disconnect")
            return {"mode": "mock", "recovery": "fresh control connection advances epoch"}

    async def connect(socket: WebSocket, session_id: str, channel: str):
        session = sessions.get(session_id)
        token = socket.query_params.get("ticket")
        grant = session.tickets.get(token) if session else None
        if not grant or grant[1] != channel or grant[2] <= time.monotonic():
            await socket.close(code=1008, reason="MOCK valid single-use channel ticket required")
            return
        device = grant[0]
        session.tickets.pop(token)
        async with session.lock:
            if device in session.connections[channel]:
                await socket.close(code=1008, reason="MOCK device channel already connected")
                return
            await socket.accept()
            session.connections[channel][device] = socket
            if channel == "control":
                session.devices[device].last_client_seq = 0
                if session.recover and "controller" in session.devices[device].roles:
                    await session.cancel("disconnected")
                    await session.advance_epoch()
                    session.inputs.clear()
                    session.recover = False
                    if session.state != "ended":
                        session.state = "muted" if session.muted else "idle"
                await session.event("session.snapshot", session.snapshot(), target=device)
        try:
            while True:
                message = await socket.receive()
                if message["type"] == "websocket.disconnect":
                    break
                async with session.lock:
                    event = None
                    try:
                        if channel == "control":
                            if message.get("text") is None:
                                raise CommandError(
                                    "invalid_event", "Control channel requires JSON text"
                                )
                            event = decode_control(message["text"], "client")
                            await session.command(device, event)
                        else:
                            if message.get("bytes") is None:
                                raise CommandError(
                                    "invalid_audio", "Audio channel requires binary PCM"
                                )
                            await session.input_audio(device, message["bytes"])
                    except AudioProtocolError as exc:
                        await session.error(
                            device,
                            "invalid_event" if channel == "control" else "invalid_audio",
                            str(exc),
                        )
                    except CommandError as exc:
                        await session.error(
                            device, exc.code, exc.message, event["event_id"] if event else None
                        )
        except (WebSocketDisconnect, RuntimeError):
            pass
        finally:
            async with session.lock:
                was_current = session.connections[channel].get(device) is socket
                if was_current:
                    session.connections[channel].pop(device, None)
                executing = (
                    channel == "control"
                    and "controller" in session.devices[device].roles
                    or channel == "audio"
                    and session.holders["speaker"] == device
                )
                if was_current and executing and session.state != "ended":
                    session.recover = True
                    await session.cancel("disconnected")
                    session.inputs.clear()

    @app.websocket("/v1/sessions/{session_id}/control")
    async def control(socket: WebSocket, session_id: str):
        await connect(socket, session_id, "control")

    @app.websocket("/v1/sessions/{session_id}/audio")
    async def audio(socket: WebSocket, session_id: str):
        await connect(socket, session_id, "audio")

    @app.api_route("/v1/{path:path}", methods=["GET", "POST", "PUT", "DELETE", "PATCH"])
    async def unsupported(path: str):
        raise HTTPException(
            501,
            {
                "mode": "mock",
                "code": "not_implemented",
                "message": "T04 implements WebSocket fixtures, not production REST",
            },
        )

    app.mount(
        "/mock/assets", StaticFiles(directory=Path(__file__).parent / "assets"), name="assets"
    )
    return app


app = create_app()
