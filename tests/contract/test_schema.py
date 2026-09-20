import copy
import json
from pathlib import Path

import pytest
from mocks.protocol import AudioProtocolError, decode_control, event_validator, validate_event

ROOT = Path(__file__).resolve().parents[2]
VALID = {
    direction: json.loads((ROOT / f"contracts/examples/{direction}-valid.json").read_text("utf-8"))
    for direction in ("client", "server")
}
INVALID = json.loads((ROOT / "contracts/examples/invalid-events.json").read_text("utf-8"))


@pytest.mark.parametrize("direction", ["client", "server"])
def test_frozen_schemas_validate_themselves_and_cover_each_event(direction):
    validator = event_validator(direction)
    assert {e["type"] for e in VALID[direction]} == set(
        validator.schema["properties"]["type"]["enum"]
    )


@pytest.mark.parametrize(
    "direction,event",
    [(d, e) for d in VALID for e in VALID[d]],
    ids=[f"{d}:{e['type']}" for d in VALID for e in VALID[d]],
)
def test_all_frozen_positive_events(direction, event):
    validate_event(event, direction)
    assert decode_control(json.dumps(event, ensure_ascii=False), direction) == event


@pytest.mark.parametrize("case", INVALID, ids=[item["reason"] for item in INVALID])
def test_all_frozen_negative_events(case):
    with pytest.raises(AudioProtocolError):
        validate_event(case["event"], case["direction"])


@pytest.mark.parametrize("direction", ["client", "server"])
@pytest.mark.parametrize(
    "field,value",
    [
        ("event_id", "bad uuid"),
        ("session_id", "bad uuid"),
        ("timestamp", "2026-09-20T08:00:00"),
        ("schema_version", "unity-preview/1"),
        ("session_epoch", 0),
        ("session_epoch", 2**32),
        ("session_epoch", True),
        ("unknown", "not negotiated"),
        ("type", "future.event"),
    ],
)
def test_extra_negative_boundaries(direction, field, value):
    event = copy.deepcopy(VALID[direction][0])
    event[field] = value
    with pytest.raises(AudioProtocolError):
        validate_event(event, direction)


@pytest.mark.parametrize(
    "bad",
    ['{"type":"a","type":"b"}', '{"x":NaN}', "[]", b"\xff", " " * 65537],
    ids=["duplicate-key", "nan", "array", "invalid-utf8", "oversized"],
)
def test_strict_control_wire_rejections(bad):
    with pytest.raises(AudioProtocolError):
        decode_control(bad, "client")


def test_size_limit_counts_utf8_bytes_including_whitespace():
    payload = json.dumps(VALID["server"][0], ensure_ascii=False)
    assert decode_control(payload + " " * (65536 - len(payload.encode())), "server")
    with pytest.raises(AudioProtocolError):
        decode_control(payload + " " * (65537 - len(payload.encode())), "server")


def test_each_required_envelope_field_is_required():
    for direction, examples in VALID.items():
        for name in event_validator(direction).schema["required"]:
            event = copy.deepcopy(examples[0])
            del event[name]
            with pytest.raises(AudioProtocolError):
                validate_event(event, direction)


def test_deeply_nested_json_is_a_controlled_rejection():
    with pytest.raises(AudioProtocolError):
        decode_control("[" * 2000 + "0" + "]" * 2000, "client")
