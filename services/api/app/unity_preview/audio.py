"""Bounded RIFF reader shared by fixture output checks and ASR input validation."""

import struct
from dataclasses import dataclass


@dataclass(frozen=True)
class WavInfo:
    sample_rate: int
    total_samples: int
    pcm: bytes


def validate_wav(data: bytes, sample_rate: int, max_bytes: int, max_seconds: int) -> WavInfo:
    if len(data) > max_bytes or len(data) < 12:
        raise ValueError("WAV size is invalid")
    if data[:4] != b"RIFF" or data[8:12] != b"WAVE":
        raise ValueError("Expected RIFF/WAVE")
    if struct.unpack_from("<I", data, 4)[0] + 8 != len(data):
        raise ValueError("RIFF size does not match file length")
    position = 12
    fmt = None
    pcm = None
    while position < len(data):
        if len(data) - position < 8:
            raise ValueError("Truncated chunk header")
        chunk_id = data[position : position + 4]
        size = struct.unpack_from("<I", data, position + 4)[0]
        start = position + 8
        end = start + size
        position = end + (size % 2)
        if position > len(data):
            raise ValueError("Truncated chunk")
        if chunk_id == b"fmt ":
            if fmt is not None or size < 16:
                raise ValueError("Invalid fmt chunk")
            fmt = struct.unpack_from("<HHIIHH", data, start)
            # PCM fmt may be 16 bytes, or WAVEFORMATEX with zero extension length.
            if size != 16 and (size != 18 or data[start + 16 : end] != b"\x00\x00"):
                raise ValueError("Unsupported PCM format extension")
        elif chunk_id == b"data":
            if pcm is not None:
                raise ValueError("Duplicate data chunk")
            pcm = data[start:end]
    expected = (1, 1, sample_rate, sample_rate * 2, 2, 16)
    if fmt != expected or pcm is None or len(pcm) == 0 or len(pcm) % 2:
        raise ValueError("Expected nonempty mono PCM16 WAV at required sample rate")
    samples = len(pcm) // 2
    if samples > sample_rate * max_seconds:
        raise ValueError("WAV duration exceeds limit")
    return WavInfo(sample_rate, samples, pcm)
