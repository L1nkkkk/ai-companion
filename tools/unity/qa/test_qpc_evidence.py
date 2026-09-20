import csv
import importlib.util
import json
import math
import shutil
import struct
import tempfile
import unittest
from pathlib import Path

SPEC = importlib.util.spec_from_file_location(
    "mux_qpc_evidence", Path(__file__).with_name("mux_qpc_evidence.py")
)
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)


def make_fixture(root):
    """Synthetic encoder calibration only, never product or recording evidence."""
    root.mkdir(exist_ok=True)
    frames = root / "frames"
    frames.mkdir()
    base = 600_000_000_000
    times = [
        0.05,
        0.11,
        0.23,
        0.3,
        0.45,
        0.6,
        0.68,
        0.75,
        0.83,
        0.95,
        1.0,
        1.09,
        1.2,
        1.4,
        1.6,
        1.8,
        1.95,
    ]
    rows = []
    for index, time in enumerate(times):
        shade = 0 if time < 1 else 255
        data = bytes([shade, shade, shade]) * 64 * 64
        bitmap = (
            b"BM"
            + struct.pack("<IHHI", 54 + len(data), 0, 0, 54)
            + struct.pack("<IiiHHIIiiII", 40, 64, 64, 1, 24, 0, len(data), 0, 0, 0, 0)
            + data
        )
        name = f"frame-{index:06d}.bmp"
        (frames / name).write_bytes(bitmap)
        tick = base + round(time * 10000000)
        rows.append(
            dict(
                frame_index=index,
                file=name,
                capture_qpc_ticks=tick,
                qpc_frequency=10000000,
                readback_done_qpc_ticks=tick + 5000,
                write_done_qpc_ticks=tick + 20000,
                width=64,
                height=64,
                request_id="synthetic-calibration",
                generation=1,
                turn_id="synthetic",
                mouth=shade / 255,
                volume=1,
                phase="SyntheticCalibration",
                status="captured",
            )
        )
    with (frames / "frames.csv").open("w", newline="") as f:
        writer = csv.DictWriter(f, fieldnames=rows[0])
        writer.writeheader()
        writer.writerows(rows)
    (frames / "frame-source.json").write_text(
        json.dumps(
            dict(
                capture_scope="unity_player_backbuffer_only",
                target_pid=123,
                fixture_only=True,
                qpc_frequency=10000000,
                target_fps=15,
                source_commit="0" * 40,
                timestamp_boundary="SYNTHETIC calibration; no Player or capture",
            )
        )
    )
    (frames / "capture-end.json").write_text(
        json.dumps(
            dict(
                end_qpc_ticks=base + 20000000,
                completed=True,
                captured=len(rows),
                dropped=0,
                readback_errors=0,
            )
        )
    )
    pcm = bytearray()
    for i in range(96000):
        value = 0.4 * math.sin(i * 2 * math.pi * 440 / 48000) if 48000 <= i < 72000 else 0
        pcm.extend(struct.pack("<ff", value, value))
    fmt = struct.pack("<HHIIHHH", 3, 2, 48000, 384000, 8, 32, 0)
    body = (
        b"WAVEfmt "
        + struct.pack("<I", len(fmt))
        + fmt
        + b"fact"
        + struct.pack("<II", 4, 96000)
        + b"data"
        + struct.pack("<I", len(pcm))
        + pcm
    )
    wav = root / "audio.wav"
    wav.write_bytes(b"RIFF" + struct.pack("<I", len(body)) + body)
    Path(str(wav) + ".json").write_text(
        json.dumps(
            dict(
                capture_mode="include_target_process_tree_only",
                target_pid=123,
                expected_image_verified=True,
                fixture_only_acknowledged=True,
                microphone_used=False,
                system_loopback_used=False,
                timestamp_errors=0,
                discontinuities=0,
                frames=96000,
            )
        )
    )
    with Path(str(wav) + ".packets.csv").open("w", newline="") as f:
        writer = csv.DictWriter(f, fieldnames=["frames", "audio_qpc_100ns", "flags"])
        writer.writeheader()
        for i in range(200):
            writer.writerow(dict(frames=480, audio_qpc_100ns=base + i * 100000, flags=0))
    return frames, wav


class QpcEvidenceTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.frames, self.wav = make_fixture(self.root)
        self.out = self.root / "prepared"

    def tearDown(self):
        self.temp.cleanup()

    def test_actual_vfr_and_audio_crop_are_preserved(self):
        r = MODULE.prepare(self.frames, self.wav, self.out)
        self.assertEqual(r["selected_frame_count"], 17)
        self.assertEqual(r["duration_seconds"], 1.95)
        pts = [int(x.split("\t")[0]) for x in (self.out / "frames.tsv").read_text().splitlines()]
        self.assertEqual(pts[:3], [0, 600000, 1800000])
        self.assertEqual(pts[10], 9500000)
        self.assertEqual((self.out / "audio.pcm").stat().st_size, round(1.95 * 48000) * 4)

    def test_target_mismatch_rejected(self):
        p = Path(str(self.wav) + ".json")
        obj = json.loads(p.read_text())
        obj["target_pid"] = 124
        p.write_text(json.dumps(obj))
        with self.assertRaisesRegex(ValueError, "identity"):
            MODULE.prepare(self.frames, self.wav, self.out)

    def test_non_scoped_audio_rejected(self):
        p = Path(str(self.wav) + ".json")
        obj = json.loads(p.read_text())
        obj["system_loopback_used"] = True
        p.write_text(json.dumps(obj))
        with self.assertRaisesRegex(ValueError, "scope"):
            MODULE.prepare(self.frames, self.wav, self.out)

    def test_absolute_frame_path_rejected(self):
        p = self.frames / "frames.csv"
        p.write_text(p.read_text().replace("frame-000000.bmp", "C:/private.bmp"))
        with self.assertRaisesRegex(ValueError, "basenames"):
            MODULE.prepare(self.frames, self.wav, self.out)

    def test_missing_finalization_rejected(self):
        p = self.frames / "capture-end.json"
        obj = json.loads(p.read_text())
        obj["completed"] = False
        p.write_text(json.dumps(obj))
        with self.assertRaisesRegex(ValueError, "complete"):
            MODULE.prepare(self.frames, self.wav, self.out)

    def test_timestamp_error_rejected(self):
        p = Path(str(self.wav) + ".json")
        obj = json.loads(p.read_text())
        obj["timestamp_errors"] = 1
        p.write_text(json.dumps(obj))
        with self.assertRaisesRegex(ValueError, "timestamps"):
            MODULE.prepare(self.frames, self.wav, self.out)

    def test_packet_gap_outside_shared_coverage_retained_but_not_encoded(self):
        path = Path(str(self.wav) + ".packets.csv")
        text = path.read_text()
        text = text.replace("600000000000", "599999890000")
        path.write_text(text)
        result = MODULE.prepare(self.frames, self.wav, self.out)
        self.assertEqual(len(result["audio_packet_gaps_outside_shared_coverage"]), 1)
        self.assertEqual(result["audio_packet_gaps_outside_shared_coverage"][0]["gap_ms"], 11)

    def test_packet_gap_inside_shared_coverage_rejected(self):
        path = Path(str(self.wav) + ".packets.csv")
        path.write_text(path.read_text().replace("600005000000", "600005030000"))
        with self.assertRaisesRegex(ValueError, "gap over 2ms overlaps"):
            MODULE.prepare(self.frames, self.wav, self.out)

    def test_packet_gap_intersecting_shared_start_is_rejected(self):
        path = Path(str(self.wav) + ".packets.csv")
        path.write_text(path.read_text().replace("600000500000", "600000530000"))
        with self.assertRaisesRegex(ValueError, "gap over 2ms overlaps"):
            MODULE.prepare(self.frames, self.wav, self.out)

    def test_encoded_timestamps_independently_verified(self):
        MODULE.prepare(self.frames, self.wav, self.out)
        folder = self.out / "decoded"
        folder.mkdir()

        def box(kind, body):
            return struct.pack(">I4s", len(body) + 8, kind) + body

        tracks = b""
        for kind, scale in [(b"vide", 15000), (b"soun", 48000)]:
            tracks += box(
                b"trak",
                box(
                    b"mdia",
                    box(b"mdhd", b"\0" * 12 + struct.pack(">I", scale))
                    + box(b"hdlr", b"\0" * 8 + kind),
                ),
            )
        (self.out / "synchronized-evidence.mp4").write_bytes(box(b"moov", tracks))
        shutil.copyfile(self.out / "audio.pcm", folder / "decoded-audio.pcm")
        with (folder / "decoded-timeline.csv").open("w", newline="") as f:
            writer = csv.writer(f)
            writer.writerow(["stream", "time_100ns", "duration_100ns", "bytes", "flags"])
            for line in (self.out / "frames.tsv").read_text().splitlines():
                pts, duration, path = line.split("\t")
                writer.writerow(["video", pts, duration, 100, 0])
            for line in (self.out / "audio.tsv").read_text().splitlines():
                pts, duration, offset, length = line.split("\t")
                writer.writerow(["audio", pts, duration, length, 0])
        report = MODULE.verify_encoded(self.out)
        self.assertEqual(report["maximum_codec_edge_shift_ms"], 0)
        self.assertEqual(report["mp4_track_timescales"], {"vide": 15000, "soun": 48000})
        self.assertEqual(report["video_quantization_bound_100ns"], 667)
        p = folder / "decoded-timeline.csv"
        original = p.read_text()
        p.write_text(original.replace("video,0,", "video,501,", 1))
        self.assertEqual(MODULE.verify_encoded(self.out)["maximum_video_pts_delta_100ns"], 501)
        p.write_text(original.replace("video,0,", "video,10000,", 1))
        with self.assertRaisesRegex(ValueError, "timing differs"):
            MODULE.verify_encoded(self.out)


if __name__ == "__main__":
    unittest.main()
