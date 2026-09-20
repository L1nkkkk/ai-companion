import json
import struct
from dataclasses import replace
from pathlib import Path
from uuid import UUID

import pytest
from mocks.protocol import AudioFrameHeader, AudioProtocolError, decode_frame, encode_frame

STREAM = UUID("00112233-4455-4677-8899-aabbccddeeff")
TURN = UUID("fedcba98-7654-4321-9abc-0123456789ab")


def input_header(**kwargs):
    return replace(AudioFrameHeader(1, 1, 0, 16000, STREAM), **kwargs)


def test_authoritative_contract_header_is_byte_exact():
    root = Path(__file__).resolve().parents[2]
    expected = json.loads((root / "contracts/examples/audio-header.json").read_text("utf-8"))
    header = AudioFrameHeader(1, 1, 0, 16000, expected["stream_id"])
    actual = encode_frame(header, bytes(3200))
    assert actual[:64].hex() == expected["header_hex"]
    assert len(actual) == 3264
    assert decode_frame(actual).header.stream_id == UUID(expected["stream_id"])


def test_uuid_order_is_network_order_independently_of_scalar_endianness():
    h = AudioFrameHeader(2, 0x01020304, 0x11223344, 24000, STREAM, TURN, 3, 9)
    frame = encode_frame(h, b"\x00\x80\xff\x7f")
    assert frame[8:16] == bytes.fromhex("0403020144332211")
    assert frame[24:40] == bytes.fromhex("00112233445546778899aabbccddeeff")
    assert frame[24:40] != STREAM.bytes_le
    assert frame[40:56] == bytes.fromhex("fedcba98765443219abc0123456789ab")
    assert struct.unpack("<hh", decode_frame(frame).payload) == (-32768, 32767)


@pytest.mark.parametrize("size", [0, 2, 3198, 3200, 9600])
def test_valid_even_payload_boundaries(size):
    frame = decode_frame(encode_frame(input_header(), bytes(size)))
    assert frame.sample_count == size // 2


@pytest.mark.parametrize(
    "field,value",
    [
        ("kind", 0),
        ("kind", 3),
        ("version", 2),
        ("channels", 2),
        ("flags", 1),
        ("session_epoch", 0),
        ("session_epoch", -1),
        ("session_epoch", 2**32),
        ("frame_seq", -1),
        ("frame_seq", 2**32),
        ("frame_seq", True),
        ("segment_index", -1),
        ("sample_rate", 24000),
        ("offset_samples", 2**32),
        ("turn_id", TURN),
        ("segment_index", 1),
        ("stream_id", UUID(int=0)),
        ("stream_id", "not-a-uuid"),
        ("channels", 1.0),
    ],
)
def test_encoder_rejects_invalid_fields(field, value):
    with pytest.raises(AudioProtocolError):
        encode_frame(input_header(**{field: value}), b"\0\0")


@pytest.mark.parametrize(
    "payload",
    [b"\0", bytes(9602), "not bytes"],
    ids=["odd-payload", "oversized-payload", "nonbytes"],
)
def test_encoder_rejects_invalid_payload(payload):
    with pytest.raises(AudioProtocolError):
        encode_frame(input_header(), payload)


@pytest.mark.parametrize(
    "offset,value",
    [
        (0, 0),
        (4, 2),
        (5, 3),
        (6, 2),
        (7, 1),
        (8, 0),
        (16, 1),
        (20, 3),
    ],
)
def test_decoder_independently_rejects_corrupt_wire_fields(offset, value):
    wire = bytearray(encode_frame(input_header(), b"\0\0"))
    wire[offset] = value
    with pytest.raises(AudioProtocolError):
        decode_frame(bytes(wire))


@pytest.mark.parametrize("cut", [0, 4, 63, 64, 65])
def test_truncation_is_not_decoded_as_audio(cut):
    wire = encode_frame(input_header(), b"\0\0")
    with pytest.raises(AudioProtocolError):
        decode_frame(wire[:cut])


def test_extra_bytes_oversize_output_nil_turn_and_uint_overflow():
    wire = encode_frame(input_header(), b"\0\0")
    for bad in (wire + b"\0\0", bytes(9665)):
        with pytest.raises(AudioProtocolError):
            decode_frame(bad)
    with pytest.raises(AudioProtocolError):
        encode_frame(AudioFrameHeader(2, 1, 0, 24000, STREAM), b"\0\0")
    with pytest.raises(AudioProtocolError):
        encode_frame(input_header(offset_samples=2**32 - 1), b"\0\0")


def test_maximum_scalars_are_preserved_without_signed_conversion():
    h = AudioFrameHeader(2, 2**32 - 1, 2**32 - 1, 24000, STREAM, TURN, 2**32 - 1, 2**32 - 2)
    result = decode_frame(encode_frame(h, b"\0\0")).header
    assert result.session_epoch == result.frame_seq == result.segment_index == 2**32 - 1
