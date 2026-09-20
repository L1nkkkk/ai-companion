"""Bounded offline R1 reference consumer, never an actual audio playback device.

``take_ready`` simulates draining a queue; it is not a speaker callback or evidence
of real playback. Unknown-stream limits apply to total wire bytes across streams.
Call ``tick`` from the host timer even while no network message is arriving.
"""

import time
from collections import OrderedDict, deque
from dataclasses import dataclass
from uuid import UUID

from .protocol import AudioFrame, AudioProtocolError, decode_frame, validate_event

UNKNOWN_LIMIT_BYTES = 64 * 1024
WAIT_SECONDS = 0.5
PLAYOUT_LIMIT_BYTES = 24000 * 2 * 2
MAX_INPUT_SAMPLES = 16000 * 30


@dataclass
class StreamState:
    stream_id: str
    turn_id: str | None
    segment_index: int
    next_seq: int = 0
    total_samples: int = 0
    end_samples: int | None = None
    end_last_seq: int | None = None
    end_deadline: float | None = None
    complete: bool = False


class PcmReceiver:
    """Test-only state machine for one session and one input or output direction."""

    def __init__(self, session_id, session_epoch=1, *, kind=2, input_device_id=None, clock=None):
        if (
            kind not in (1, 2)
            or type(session_epoch) is not int
            or not 1 <= session_epoch <= 0xFFFFFFFF
        ):
            raise ValueError("Invalid receiver direction or epoch")
        self.session_id = str(UUID(str(session_id)))
        self.epoch = session_epoch
        self.kind = kind
        self.input_device_id = str(UUID(str(input_device_id))) if input_device_id else None
        self.clock = clock or time.monotonic
        self.active_turn = None
        self.cancelled_turns = set()
        self.terminal_turns = set()
        self.stopped_streams = set()
        self.streams = {}
        self.unknown = {}
        self.ready = deque()
        self.text_deltas = deque(maxlen=128)
        self.avatar_states = deque(maxlen=128)
        self.errors = deque(maxlen=128)
        self.resync_requested = False
        self.last_control_seq = 0
        self.seen_events = OrderedDict()

    @property
    def unknown_bytes(self):
        return sum(
            64 + frame.payload_bytes for items in self.unknown.values() for _, frame in items
        )

    @property
    def queued_bytes(self):
        return sum(frame.payload_bytes for frame in self.ready)

    def _fail(self, stream_id, code):
        self.stopped_streams.add(stream_id)
        self.unknown.pop(stream_id, None)
        self.streams.pop(stream_id, None)
        self.ready = deque(f for f in self.ready if str(f.header.stream_id) != stream_id)
        self.errors.append({"code": code, "stream_id": stream_id})
        self.resync_requested = True
        return code

    def tick(self):
        """Expire cross-channel waits using the injected monotonic host clock."""
        now = self.clock()
        for stream_id, items in list(self.unknown.items()):
            if now - items[0][0] >= WAIT_SECONDS:
                self._fail(stream_id, "unknown_stream_timeout")
        for stream_id, state in list(self.streams.items()):
            if not state.complete and state.end_deadline is not None and now >= state.end_deadline:
                self._fail(stream_id, "incomplete_stream")

    def set_epoch(self, epoch):
        if type(epoch) is not int or not self.epoch < epoch <= 0xFFFFFFFF:
            raise ValueError("Epoch changes must advance uint32")
        self.epoch = epoch
        self.active_turn = None
        self.cancelled_turns.clear()
        self.terminal_turns.clear()
        self.stopped_streams.clear()
        self.streams.clear()
        self.unknown.clear()
        self.ready.clear()
        self.text_deltas.clear()
        self.avatar_states.clear()
        self.seen_events.clear()
        self.last_control_seq = 0
        self.resync_requested = False

    def cancel(self, turn_id):
        """Local-first: remove all queued work before any server acknowledgement."""
        turn = str(UUID(str(turn_id)))
        self.cancelled_turns.add(turn)
        self.terminal_turns.add(turn)
        for stream_id, state in list(self.streams.items()):
            if state.turn_id == turn:
                self.streams.pop(stream_id)
                self.stopped_streams.add(stream_id)
        for stream_id, items in list(self.unknown.items()):
            if any(str(frame.header.turn_id) == turn for _, frame in items):
                self.unknown.pop(stream_id)
                self.stopped_streams.add(stream_id)
        self.ready = deque(f for f in self.ready if str(f.header.turn_id) != turn)
        self.text_deltas = deque((e for e in self.text_deltas if e["turn_id"] != turn), maxlen=128)
        self.avatar_states = deque(
            (e for e in self.avatar_states if e["turn_id"] != turn), maxlen=128
        )
        if self.active_turn == turn:
            self.active_turn = None

    def accept_input(self, stream_id, *, device_id, session_epoch):
        """Test admission after input.audio.accepted and an already-checked lease."""
        stream = str(UUID(str(stream_id)))
        if self.kind != 1 or session_epoch != self.epoch or str(device_id) != self.input_device_id:
            return "input_not_authorized"
        if stream in self.streams or stream in self.stopped_streams:
            return "stream_already_used"
        if any(not state.complete for state in self.streams.values()):
            return "input_already_active"
        if len(self.streams) >= 64:
            return "stream_capacity"
        self.streams[stream] = StreamState(stream, None, 0)
        return "accepted"

    def receive_audio(self, data: bytes, *, device_id=None):
        self.tick()
        frame = decode_frame(data)
        h = frame.header
        stream, turn = str(h.stream_id), str(h.turn_id)
        if h.kind != self.kind:
            return "wrong_direction"
        if h.session_epoch != self.epoch:
            return "stale_epoch"
        if self.resync_requested:
            return "resync_required"
        if self.kind == 1 and (
            self.input_device_id is None or str(device_id) != self.input_device_id
        ):
            return "input_not_authorized"
        if turn in self.terminal_turns:
            return "terminal_turn"
        if stream in self.stopped_streams:
            return "stopped_stream"
        if stream not in self.streams:
            if self.kind == 1:
                return "input_not_accepted"
            if self.unknown_bytes + len(data) > UNKNOWN_LIMIT_BYTES:
                return self._fail(stream, "unknown_buffer_limit")
            self.unknown.setdefault(stream, []).append((self.clock(), frame))
            return "waiting_metadata"
        return self._accept_frame(frame)

    def _accept_frame(self, frame: AudioFrame):
        h = frame.header
        stream = str(h.stream_id)
        state = self.streams[stream]
        if self.kind == 2 and state.turn_id != self.active_turn:
            return self._fail(stream, "inactive_turn")
        if self.kind == 2 and (
            str(h.turn_id) != state.turn_id or h.segment_index != state.segment_index
        ):
            return self._fail(stream, "metadata_mismatch")
        if state.complete:
            return self._fail(stream, "frame_after_end")
        if h.frame_seq != state.next_seq or h.offset_samples != state.total_samples:
            return self._fail(stream, "stream_discontinuity")
        total = state.total_samples + frame.sample_count
        if self.kind == 1 and total > MAX_INPUT_SAMPLES:
            return self._fail(stream, "input_duration_limit")
        if state.end_samples is not None and total > state.end_samples:
            return self._fail(stream, "end_total_mismatch")
        if self.kind == 2 and (
            self.queued_bytes + frame.payload_bytes > PLAYOUT_LIMIT_BYTES or len(self.ready) >= 1024
        ):
            # Reject the stream as a whole, rather than silently skipping one frame.
            return self._fail(stream, "playout_backpressure")
        state.next_seq += 1
        state.total_samples = total
        if self.kind == 2:
            self.ready.append(frame)
        return self._check_end(state) or "accepted"

    def _check_end(self, state):
        if state.end_samples is None or state.total_samples != state.end_samples:
            return
        if self.kind == 1:
            actual_last = state.next_seq - 1 if state.next_seq else None
            if actual_last != state.end_last_seq:
                return self._fail(state.stream_id, "end_sequence_mismatch")
        state.complete = True
        state.end_deadline = None

    def end_stream(self, stream_id, total_samples, *, last_frame_seq=None):
        self.tick()
        stream = str(UUID(str(stream_id)))
        if type(total_samples) is not int or not 0 <= total_samples <= 0xFFFFFFFF:
            raise AudioProtocolError("invalid_field", "Invalid total_samples")
        if last_frame_seq is not None and (
            type(last_frame_seq) is not int or not 0 <= last_frame_seq <= 0xFFFFFFFF
        ):
            raise AudioProtocolError("invalid_field", "Invalid last_frame_seq")
        if stream not in self.streams:
            return "unknown_end"
        state = self.streams[stream]
        if state.end_samples is not None:
            if (state.end_samples, state.end_last_seq) == (total_samples, last_frame_seq):
                return "duplicate_end"
            return self._fail(stream, "conflicting_end")
        if state.total_samples > total_samples:
            return self._fail(stream, "end_total_mismatch")
        if self.kind == 1 and ((total_samples == 0) != (last_frame_seq is None)):
            return self._fail(stream, "end_sequence_mismatch")
        state.end_samples = total_samples
        state.end_last_seq = last_frame_seq
        state.end_deadline = self.clock() + WAIT_SECONDS
        failure = self._check_end(state)
        if failure:
            return failure
        return "complete" if state.complete else "waiting_end_frames"

    def take_ready(self):
        """Return matched PCM to a test sink; no claim of real speaker playback."""
        self.tick()
        frames = list(self.ready)
        self.ready.clear()
        return frames

    def receive_control(self, event: dict):
        self.tick()
        validate_event(event, "server")
        if event["session_id"] != self.session_id:
            return "wrong_session"
        if event["session_epoch"] != self.epoch:
            if event["type"] == "session.snapshot" and event["session_epoch"] > self.epoch:
                self.set_epoch(event["session_epoch"])
            else:
                return "stale_epoch"
        now = self.clock()
        while self.seen_events and next(iter(self.seen_events.values())) <= now - 600:
            self.seen_events.popitem(last=False)
        if event["event_id"] in self.seen_events:
            return "duplicate_event"
        if event["seq"] <= self.last_control_seq:
            return "stale_control_seq"
        self.last_control_seq = event["seq"]
        self.seen_events[event["event_id"]] = now
        while len(self.seen_events) > 1000:
            self.seen_events.popitem(last=False)
        kind, turn, payload = event["type"], event["turn_id"], event["payload"]
        if kind == "session.snapshot":
            if payload["active_turn_id"] in self.terminal_turns:
                return "terminal_turn"
            active = payload["active_turn_id"]
            if self.active_turn is not None and self.active_turn != active:
                self.cancel(self.active_turn)
            # A resync snapshot is authoritative, including in the same epoch.
            # Never recover by replaying pre-resync pending PCM.
            for stream_id, state in list(self.streams.items()):
                if self.resync_requested or state.turn_id != active:
                    self.streams.pop(stream_id)
                    self.stopped_streams.add(stream_id)
            for stream_id, items in list(self.unknown.items()):
                if self.resync_requested or any(str(f.header.turn_id) != active for _, f in items):
                    self.unknown.pop(stream_id)
                    self.stopped_streams.add(stream_id)
            if self.resync_requested:
                self.ready.clear()
                self.text_deltas.clear()
                self.avatar_states.clear()
            else:
                self.ready = deque(f for f in self.ready if str(f.header.turn_id) == active)
                self.text_deltas = deque(
                    (e for e in self.text_deltas if e["turn_id"] == active), maxlen=128
                )
                self.avatar_states = deque(
                    (e for e in self.avatar_states if e["turn_id"] == active), maxlen=128
                )
            self.active_turn = active
            self.resync_requested = False
            return "snapshot"
        if self.resync_requested and kind not in ("turn.cancelled", "turn.failed"):
            return "resync_required"
        if kind in ("turn.cancelled", "turn.failed"):
            self.cancel(turn)
            return "cancelled"
        if turn in self.terminal_turns:
            return "terminal_turn"
        if kind == "turn.started":
            if self.active_turn is not None and self.active_turn != turn:
                return "active_turn_conflict"
            self.active_turn = turn
            return "turn_started"
        if turn is not None and turn != self.active_turn:
            return "unknown_turn"
        if kind == "assistant.segment":
            stream = payload["stream_id"]
            if stream in self.stopped_streams:
                return "stopped_stream"
            if stream in self.streams:
                return self._fail(stream, "duplicate_segment")
            if len(self.streams) >= 64:
                return self._fail(stream, "stream_capacity")
            self.streams[stream] = StreamState(stream, turn, payload["segment_index"])
            waiting = self.unknown.pop(stream, [])
            for _, frame in waiting:
                status = self._accept_frame(frame)
                if status != "accepted":
                    return status
            return "segment_registered"
        if kind == "assistant.audio.end":
            stream = payload["stream_id"]
            state = self.streams.get(stream)
            if state is None:
                return "unknown_end"
            if state.turn_id != turn or state.segment_index != payload["segment_index"]:
                return self._fail(stream, "metadata_mismatch")
            return self.end_stream(stream, payload["total_samples"])
        if kind == "assistant.text.delta":
            self.text_deltas.append(event)
        elif kind == "avatar.state":
            self.avatar_states.append(event)
        elif kind == "turn.completed":
            self.cancel(turn)
            return "completed"
        return "accepted"
