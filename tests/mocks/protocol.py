"""Frozen R1 AIC1 test codec. This is not the U01 WAV preview protocol."""

import json
import re
import struct
from dataclasses import dataclass
from datetime import datetime
from functools import lru_cache
from pathlib import Path
from uuid import UUID

from jsonschema import Draft202012Validator, FormatChecker

HEADER_SIZE = 64
MAX_PAYLOAD_BYTES = 9600
MAX_CONTROL_BYTES = 65536
UINT32_MAX = 0xFFFFFFFF
NIL_UUID = UUID(int=0)
_HEADER = struct.Struct("<4sBBBBIIII16s16sII")


class AudioProtocolError(ValueError):
    """A deterministic protocol rejection; code is for mock diagnostics only."""

    def __init__(self, code: str, message: str):
        self.code = code
        super().__init__(message)


@dataclass(frozen=True)
class AudioFrameHeader:
    kind: int
    session_epoch: int
    frame_seq: int
    sample_rate: int
    stream_id: UUID | str
    turn_id: UUID | str | None = None
    segment_index: int = 0
    offset_samples: int = 0
    channels: int = 1
    flags: int = 0
    version: int = 1


@dataclass(frozen=True)
class AudioFrame:
    header: AudioFrameHeader
    payload: bytes

    @property
    def payload_bytes(self) -> int:
        return len(self.payload)

    @property
    def sample_count(self) -> int:
        return len(self.payload) // 2


def _uint(value: int, name: str, maximum: int = UINT32_MAX) -> int:
    if type(value) is not int or not 0 <= value <= maximum:
        raise AudioProtocolError("invalid_field", f"{name} is outside its integer range")
    return value


def _uuid(value: UUID | str | None, name: str, *, allow_nil: bool = False) -> UUID:
    try:
        result = value if isinstance(value, UUID) else UUID(str(value))
    except (ValueError, TypeError, AttributeError) as exc:
        raise AudioProtocolError("invalid_uuid", f"Invalid {name}") from exc
    if not allow_nil and result.int == 0:
        raise AudioProtocolError("invalid_uuid", f"{name} cannot be nil")
    return result


def _validate(header: AudioFrameHeader, payload: bytes) -> tuple[UUID, UUID]:
    for name in ("session_epoch", "frame_seq", "sample_rate", "segment_index", "offset_samples"):
        _uint(getattr(header, name), name)
    for name in ("kind", "channels", "flags", "version"):
        _uint(getattr(header, name), name, 255)
    if header.version != 1 or header.kind not in (1, 2):
        raise AudioProtocolError("invalid_field", "Unsupported AIC1 version or kind")
    if header.channels != 1 or header.flags != 0 or header.session_epoch == 0:
        raise AudioProtocolError("invalid_field", "R1 requires mono, zero flags and positive epoch")
    if header.sample_rate != (16000 if header.kind == 1 else 24000):
        raise AudioProtocolError(
            "invalid_sample_rate", "Sample rate disagrees with frame direction"
        )
    if not isinstance(payload, bytes):
        raise AudioProtocolError("invalid_length", "PCM payload must be bytes")
    if len(payload) > MAX_PAYLOAD_BYTES or len(payload) % 2:
        raise AudioProtocolError(
            "invalid_length", "PCM payload must be even and at most 9600 bytes"
        )
    if header.offset_samples + len(payload) // 2 > UINT32_MAX:
        raise AudioProtocolError("invalid_field", "Sample offset would overflow uint32")
    stream = _uuid(header.stream_id, "stream_id")
    turn = _uuid(NIL_UUID if header.turn_id is None else header.turn_id, "turn_id", allow_nil=True)
    if header.kind == 1 and (turn.int != 0 or header.segment_index != 0):
        raise AudioProtocolError("invalid_field", "Input frames require nil turn and segment zero")
    if header.kind == 2 and turn.int == 0:
        raise AudioProtocolError("invalid_uuid", "Output frames require a non-nil turn")
    return stream, turn


def encode_frame(header: AudioFrameHeader, payload: bytes) -> bytes:
    """Encode little-endian scalar fields and RFC 4122 network-order UUID bytes."""
    stream, turn = _validate(header, payload)
    return (
        _HEADER.pack(
            b"AIC1",
            header.version,
            header.kind,
            header.channels,
            header.flags,
            header.session_epoch,
            header.frame_seq,
            header.sample_rate,
            len(payload),
            stream.bytes,
            turn.bytes,
            header.segment_index,
            header.offset_samples,
        )
        + payload
    )


def decode_frame(data: bytes) -> AudioFrame:
    """Reject malformed messages before exposing any PCM to a consumer."""
    if (
        not isinstance(data, bytes)
        or not HEADER_SIZE <= len(data) <= HEADER_SIZE + MAX_PAYLOAD_BYTES
    ):
        raise AudioProtocolError("invalid_length", "Frame must contain a bounded 64-byte header")
    magic, version, kind, channels, flags, epoch, seq, rate, size, stream, turn, segment, offset = (
        _HEADER.unpack_from(data)
    )
    if magic != b"AIC1":
        raise AudioProtocolError("invalid_magic", "Not an AIC1 frame")
    if size != len(data) - HEADER_SIZE:
        raise AudioProtocolError("invalid_length", "Declared and actual PCM length differ")
    header = AudioFrameHeader(
        kind,
        epoch,
        seq,
        rate,
        UUID(bytes=stream),
        UUID(bytes=turn),
        segment,
        offset,
        channels,
        flags,
        version,
    )
    payload = data[HEADER_SIZE:]
    _validate(header, payload)
    return AudioFrame(header, payload)


@lru_cache(maxsize=2)
def event_validator(direction: str) -> Draft202012Validator:
    if direction not in ("client", "server"):
        raise ValueError("direction must be client or server")
    path = Path(__file__).resolve().parents[2] / "contracts" / f"{direction}-event.schema.json"
    schema = json.loads(path.read_text("utf-8"))
    Draft202012Validator.check_schema(schema)
    checker = FormatChecker()

    @checker.checks("date-time")
    def zoned_rfc3339(value):
        # jsonschema's date-time checker is optional without extra dependencies.
        # Keep this offline harness strict in every frozen environment.
        if not isinstance(value, str):
            return True
        if not re.fullmatch(
            r"\d{4}-\d{2}-\d{2}[Tt]\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:[Zz]|[+-]\d{2}:\d{2})",
            value,
        ):
            return False
        try:
            return datetime.fromisoformat(value.upper()).utcoffset() is not None
        except ValueError:
            return False

    return Draft202012Validator(schema, format_checker=checker)


def validate_event(event: dict, direction: str) -> None:
    """Validate frozen schemas, including UUID/date-time formats and encoded size."""
    try:
        size = len(json.dumps(event, ensure_ascii=False, allow_nan=False).encode("utf-8"))
    except (ValueError, TypeError, RecursionError) as exc:
        raise AudioProtocolError("invalid_control", "Not a JSON event") from exc
    if size > MAX_CONTROL_BYTES:
        raise AudioProtocolError("invalid_control", "Control message exceeds 64 KiB")
    issues = sorted(event_validator(direction).iter_errors(event), key=lambda e: str(e.path))
    if issues:
        raise AudioProtocolError("invalid_control", issues[0].message)


def decode_control(data: str | bytes, direction: str) -> dict:
    """Strict JSON text parsing; reject duplicate keys and non-finite numbers."""

    def object_pairs(pairs):
        result = {}
        for key, value in pairs:
            if key in result:
                raise ValueError("Duplicate JSON key")
            result[key] = value
        return result

    def reject_constant(_):
        raise ValueError("Non-finite JSON number")

    try:
        raw = data.encode("utf-8") if isinstance(data, str) else data
        if not isinstance(raw, bytes) or len(raw) > MAX_CONTROL_BYTES:
            raise ValueError("Control frame exceeds 64 KiB")
        event = json.loads(
            raw.decode("utf-8"), object_pairs_hook=object_pairs, parse_constant=reject_constant
        )
    except (ValueError, TypeError, UnicodeError, RecursionError) as exc:
        raise AudioProtocolError("invalid_control", str(exc)) from exc
    validate_event(event, direction)
    return event
