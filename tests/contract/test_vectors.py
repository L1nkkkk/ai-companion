import json
import shutil
import struct
import subprocess
from dataclasses import asdict
from pathlib import Path
from uuid import UUID

import pytest
from mocks.protocol import AudioFrameHeader, decode_frame, encode_frame

FOLDER = Path(__file__).parent / "vectors"
VECTORS = json.loads((FOLDER / "audio-vectors.json").read_text("utf-8"))["vectors"]


@pytest.mark.parametrize("vector", VECTORS, ids=[v["name"] for v in VECTORS])
def test_python_matches_literal_wire_and_pcm_vectors(vector):
    raw = bytes.fromhex(vector["frame_hex"])
    frame = decode_frame(raw)
    actual = {k: str(v) if isinstance(v, UUID) else v for k, v in asdict(frame.header).items()}
    assert actual == vector["header"]
    assert frame.payload.hex() == vector["payload_hex"]
    assert list(struct.unpack(f"<{frame.sample_count}h", frame.payload)) == vector["pcm_s16"]
    assert encode_frame(AudioFrameHeader(**vector["header"]), frame.payload) == raw


def test_independent_node_serialization_and_malformed_frames():
    node = shutil.which("node")
    assert node, "Node is required by the frozen repository checks"
    result = subprocess.run(
        [node, str(FOLDER / "verify-node.mjs")],
        capture_output=True,
        text=True,
        timeout=30,
        check=True,
    )
    evidence = json.loads(result.stdout)
    assert evidence["status"] == "passed"
    assert evidence["vectors"] == len(VECTORS)
    assert evidence["malformedFramesRejected"] == 10


@pytest.mark.skipif(
    shutil.which("pwsh") is None, reason="Optional independent C# check requires PowerShell 7"
)
def test_independent_csharp_serialization_and_malformed_frames():
    result = subprocess.run(
        [shutil.which("pwsh"), "-NoProfile", "-File", str(FOLDER / "Verify-Aic1.ps1")],
        capture_output=True,
        text=True,
        timeout=60,
        check=True,
    )
    evidence = json.loads(result.stdout)
    assert evidence["status"] == "passed"
    assert evidence["vectors"] == len(VECTORS)
    assert evidence["malformedFramesRejected"] == 10
