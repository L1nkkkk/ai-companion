"""Generate the original CC0 U01 test signal; contains no voice or recording."""

import math
import struct
from pathlib import Path


def fixture_wav() -> bytes:
    rate = 24000
    samples = []
    for index in range(rate * 8):
        time = index / rate
        value = 0.0
        for start, end, frequency in ((0.5, 2, 220), (3, 5, 330), (6, 7.5, 440)):
            if start <= time < end:
                envelope = min(1.0, (time - start) / 0.03, (end - time) / 0.03)
                value = 0.22 * envelope * math.sin(2 * math.pi * frequency * time)
        samples.append(round(value * 32767))
    pcm = struct.pack(f"<{len(samples)}h", *samples)
    fmt = struct.pack("<HHIIHH", 1, 1, rate, rate * 2, 2, 16)
    # A legal odd-size extra chunk ensures consumers do not assume a 44-byte header.
    chunks = b"fmt " + struct.pack("<I", 16) + fmt + b"JUNK\x03\x00\x00\x00U01\x00"
    chunks += b"data" + struct.pack("<I", len(pcm)) + pcm
    return b"RIFF" + struct.pack("<I", len(chunks) + 4) + b"WAVE" + chunks


if __name__ == "__main__":
    Path(__file__).with_name("demo-tone.wav").write_bytes(fixture_wav())
