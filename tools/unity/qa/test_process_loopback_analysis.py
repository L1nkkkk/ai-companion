import csv
import importlib.util
import json
import struct
import tempfile
import unittest
from pathlib import Path

SPEC = importlib.util.spec_from_file_location(
    "analyzer", Path(__file__).with_name("analyze_process_loopback.py")
)
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)


class ProcessLoopbackAnalysisTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.folder = Path(self.directory.name)
        self.wav = self.folder / "capture.wav"
        self.stops = self.folder / "stops.csv"
        self.base = 600_000_000_000
        data = bytearray(96000 * 8)
        for frame in range(45600, 49200):
            struct.pack_into("<ff", data, frame * 8, 0.25, -0.25)
        fmt = struct.pack("<HHIIHHH", 3, 2, 48000, 384000, 8, 32, 0)
        body = (
            b"WAVEfmt "
            + struct.pack("<I", len(fmt))
            + fmt
            + b"fact"
            + struct.pack("<II", 4, 96000)
            + b"data"
            + struct.pack("<I", len(data))
            + data
        )
        self.wav.write_bytes(b"RIFF" + struct.pack("<I", len(body)) + body)
        self.packets = [
            {"packet": i + 1, "frames": 480, "audio_qpc_100ns": self.base + i * 100000, "flags": 0}
            for i in range(200)
        ]
        self.write_packets()
        Path(str(self.wav) + ".json").write_text(
            json.dumps(
                {
                    "capture_mode": "include_target_process_tree_only",
                    "target_pid": 123,
                    "expected_image_verified": True,
                    "fixture_only_acknowledged": True,
                    "system_loopback_used": False,
                    "microphone_used": False,
                    "frames": 96000,
                }
            )
        )
        self.write_stops()

    def tearDown(self):
        self.directory.cleanup()

    def write_packets(self):
        with Path(str(self.wav) + ".packets.csv").open("w", newline="") as handle:
            writer = csv.DictWriter(handle, fieldnames=self.packets[0].keys())
            writer.writeheader()
            writer.writerows(self.packets)

    def write_stops(self, **changes):
        row = {
            "sample": 0,
            "request_id": "synthetic",
            "stop_qpc_ticks": self.base + 10000000,
            "qpc_frequency": 10000000,
        }
        row.update(changes)
        with self.stops.open("w", newline="") as handle:
            writer = csv.DictWriter(handle, fieldnames=row.keys())
            writer.writeheader()
            writer.writerow(row)

    def test_sample_resolution_tail(self):
        result = MODULE.analyze(self.wav, self.stops)
        self.assertEqual(result["valid_samples"], 1)
        self.assertAlmostEqual(result["p95_ms"], 25 - 1000 / 48000, places=4)
        self.assertFalse(result["ua05_count_and_p95_subcheck"])

    def test_incomplete_window_is_not_zero_latency(self):
        self.write_stops(stop_qpc_ticks=self.base + 1)
        result = MODULE.analyze(self.wav, self.stops)
        self.assertEqual(result["valid_samples"], 0)
        self.assertIsNone(result["p95_ms"])

    def test_bad_timestamp_excludes_sample(self):
        self.packets[101]["flags"] = 4
        self.write_packets()
        self.assertEqual(MODULE.analyze(self.wav, self.stops)["valid_samples"], 0)

    def test_clock_origin_cannot_be_guessed(self):
        self.stops.write_text("sample,stop_ticks,clock_frequency\n0,10000000,10000000\n")
        with self.assertRaisesRegex(ValueError, "different origin"):
            MODULE.analyze(self.wav, self.stops)

    def test_wrong_scope_rejected(self):
        path = Path(str(self.wav) + ".json")
        summary = json.loads(path.read_text())
        summary["system_loopback_used"] = True
        path.write_text(json.dumps(summary))
        with self.assertRaisesRegex(ValueError, "provenance"):
            MODULE.analyze(self.wav, self.stops)


if __name__ == "__main__":
    unittest.main()
