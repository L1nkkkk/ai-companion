"""Offline-only QPC alignment of actual Unity frames and process-scoped WASAPI.

Does not capture, operate windows, record devices, invent frames, or shift audio to
match visible mouth motion. The MP4 is a lossy viewing derivative; originals stay authoritative.
"""

from __future__ import annotations

import argparse
import array
import csv
import hashlib
import json
import math
import re
import struct
import subprocess
from pathlib import Path

from analyze_process_loopback import read_float_wav

TICKS = 10_000_000


def sha(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def read_json(path: Path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def qpc100ns(tick: int, frequency: int) -> int:
    if tick <= 0 or frequency <= 0:
        raise ValueError("Native QPC must be positive.")
    return (tick * TICKS + frequency // 2) // frequency


def nearest(values, fraction):
    ordered = sorted(values)
    return ordered[max(0, math.ceil(len(ordered) * fraction) - 1)] if ordered else None


def load_frames(directory: Path):
    source = read_json(directory / "frame-source.json")
    end = read_json(directory / "capture-end.json")
    if (
        source.get("capture_scope") != "unity_player_backbuffer_only"
        or source.get("fixture_only") is not True
    ):
        raise ValueError("Only explicitly scoped fixture Player backbuffer frames are permitted.")
    if not re.fullmatch(r"[0-9a-fA-F]{40}", source.get("source_commit", "")):
        raise ValueError("Missing exact source commit.")
    if end.get("completed") is not True or end.get("readback_errors") != 0:
        raise ValueError(
            "Frame capture did not complete successfully; retain incomplete evidence separately."
        )
    with (directory / "frames.csv").open(encoding="utf-8-sig", newline="") as f:
        rows = list(csv.DictReader(f))
    captured = []
    freq = int(source["qpc_frequency"])
    for index, row in enumerate(rows):
        if int(row["frame_index"]) != index or int(row["qpc_frequency"]) != freq:
            raise ValueError("Inconsistent frame identity or clock.")
        if row["status"] not in ("captured", "dropped"):
            raise ValueError("Frame capture contains an error.")
        row["time"] = qpc100ns(int(row["capture_qpc_ticks"]), freq)
        if index and row["time"] <= rows[index - 1]["time"]:
            raise ValueError("Frame QPC moved backwards.")
        if row["status"] == "dropped":
            continue
        if int(row["readback_done_qpc_ticks"]) < int(row["capture_qpc_ticks"]) or int(
            row["write_done_qpc_ticks"]
        ) < int(row["readback_done_qpc_ticks"]):
            raise ValueError("Invalid readback/write timing.")
        name = row["file"]
        if not re.fullmatch(r"[A-Za-z0-9_-]+\.(png|bmp)", name):
            raise ValueError("Frame paths must be bounded local image basenames.")
        path = (directory / name).resolve()
        if path.parent != directory.resolve() or not path.is_file():
            raise ValueError("Frame file escaped the capture directory or is missing.")
        row["path"] = path
        captured.append(row)
    if len(captured) < 2 or len(captured) != end["captured"]:
        raise ValueError("Frame count differs from capture summary.")
    if len(rows) - len(captured) != end["dropped"]:
        raise ValueError("Dropped-frame count differs from capture summary.")
    dimensions = {(int(row["width"]), int(row["height"])) for row in captured}
    if len(dimensions) != 1:
        raise ValueError("Window resized during capture; a new fixed-size capture is required.")
    width, height = dimensions.pop()
    if not (16 <= width <= 1920 and 16 <= height <= 1080 and width % 2 == height % 2 == 0):
        raise ValueError("H264 evidence needs bounded even dimensions.")
    return source, end, rows, captured, (width, height)


def prepare(frames: Path, wav: Path, output: Path) -> dict:
    if output.exists():
        raise ValueError("Output directory must be new.")
    source, end, all_frames, captured, size = load_frames(frames)
    summary = read_json(Path(str(wav) + ".json"))
    if (
        summary.get("capture_mode") != "include_target_process_tree_only"
        or summary.get("target_pid") != source["target_pid"]
        or summary.get("expected_image_verified") is not True
        or summary.get("fixture_only_acknowledged") is not True
        or summary.get("microphone_used") is not False
        or summary.get("system_loopback_used") is not False
    ):
        raise ValueError("Video and process-only audio scope/identity do not match.")
    if summary.get("timestamp_errors") != 0 or summary.get("discontinuities") != 0:
        raise ValueError("Audio contains discontinuities or unreliable timestamps.")
    raw, rate, channels = read_float_wav(wav)
    with Path(str(wav) + ".packets.csv").open(encoding="utf-8-sig", newline="") as f:
        packets = list(csv.DictReader(f))
    offset = 0
    last_end = None
    for packet in packets:
        count = int(packet["frames"])
        packet["time"] = int(packet["audio_qpc_100ns"])
        packet["end"] = packet["time"] + (count * TICKS + rate // 2) // rate
        packet["offset"] = offset
        offset += count * channels * 4
        if (
            count <= 0
            or int(packet["flags"]) & 5
            or (last_end is not None and abs(packet["time"] - last_end) > 20000)
        ):
            raise ValueError("Audio packet clock is invalid or has a gap over 2ms.")
        last_end = packet["end"]
    if not packets or offset != len(raw) or offset // 8 != summary["frames"]:
        raise ValueError("Audio WAV and packet manifest differ.")
    finish = min(
        qpc100ns(int(end["end_qpc_ticks"]), int(source["qpc_frequency"])), packets[-1]["end"]
    )
    selected = [r for r in captured if r["time"] >= packets[0]["time"] and r["time"] < finish]
    if len(selected) < 2:
        raise ValueError("No meaningful shared video/audio coverage.")
    start = selected[0]["time"]
    if finish - start > 120 * TICKS:
        raise ValueError("Evidence exceeds 120 seconds.")
    intervals = [b["time"] - a["time"] for a, b in zip(selected, selected[1:])]
    if finish - selected[-1]["time"] > TICKS:
        raise ValueError("Capture end requires holding an unobserved final frame over one second.")
    output.mkdir()
    with (output / "frames.tsv").open("x", encoding="utf-8", newline="\n") as f:
        for index, frame in enumerate(selected):
            following = selected[index + 1]["time"] if index + 1 < len(selected) else finish
            f.write(f"{frame['time'] - start}\t{following - frame['time']}\t{frame['path']}\n")
    pcm_offset, chunks, quantized = 0, 0, 0
    with (
        (output / "audio.pcm").open("xb") as pcm,
        (output / "audio.tsv").open("x", encoding="ascii", newline="\n") as timeline,
    ):
        for packet in packets:
            count = int(packet["frames"])
            low = max(0, -((-(start - packet["time"]) * rate) // TICKS))
            high = min(count, -((-(finish - packet["time"]) * rate) // TICKS))
            if low >= high:
                continue
            begin = packet["offset"] + low * 8
            data = raw[begin : packet["offset"] + high * 8]
            samples = array.array("h")
            for (value,) in struct.iter_unpack("<f", data):
                if not math.isfinite(value):
                    raise ValueError("Invalid float audio.")
                quantized += value != 0 and abs(value) < 1 / 65536
                samples.append(max(-32768, min(32767, round(value * 32768))))
            block = samples.tobytes()
            time = packet["time"] + (low * TICKS + rate // 2) // rate - start
            duration = ((high - low) * TICKS + rate // 2) // rate
            timeline.write(f"{time}\t{duration}\t{pcm_offset}\t{len(block)}\n")
            pcm.write(block)
            pcm_offset += len(block)
            chunks += 1
    hashes = {r["file"]: sha(r["path"]) for r in captured}
    report = {
        "source_commit": source["source_commit"],
        "target_pid": source["target_pid"],
        "scope": "actual_Unity_Player_backbuffer_and_same_target_process_WASAPI_only",
        "capture_timestamp_boundary": source.get(
            "timestamp_boundary", "EndOfFrame capture request; not physical display presentation"
        ),
        "time_basis": "independent native Win32 QPC; actual per-frame variable presentation times; no lip-sync offset correction",
        "start_qpc_100ns": start,
        "end_qpc_100ns": finish,
        "duration_seconds": (finish - start) / TICKS,
        "width": size[0],
        "height": size[1],
        "nominal_fps": int(source["target_fps"]),
        "source_frame_count": len(captured),
        "selected_frame_count": len(selected),
        "frames_outside_shared_audio_coverage": len(captured) - len(selected),
        "declared_dropped_frames": end["dropped"],
        "actual_interval_p50_ms": nearest(intervals, 0.5) / 10000,
        "actual_interval_p95_ms": nearest(intervals, 0.95) / 10000,
        "maximum_frame_hold_ms": max(intervals + [finish - selected[-1]["time"]]) / 10000,
        "effective_capture_fps": (len(selected) - 1) * TICKS / (selected[-1]["time"] - start),
        "readback_p95_ms": nearest(
            [
                (int(r["readback_done_qpc_ticks"]) - int(r["capture_qpc_ticks"]))
                * 1000
                / int(r["qpc_frequency"])
                for r in captured
            ],
            0.95,
        ),
        "readback_write_max_ms": max(
            (int(r["write_done_qpc_ticks"]) - int(r["capture_qpc_ticks"]))
            * 1000
            / int(r["qpc_frequency"])
            for r in captured
        ),
        "audio_chunks": chunks,
        "audio_samples_quantized_below_half_pcm16_lsb": quantized,
        "viewing_derivative": "H264 YUV420 BT601 plus AAC from PCM16; raw float WAV/PNG/QPC evidence authoritative",
        "limitations": [
            "Frame held until next actual capture; missed render frames are not interpolated.",
            "Unity backbuffer observation is not DWM/physical display timing.",
            "AAC codec delay and output timestamps must be inspected before declaring the derivative synchronized.",
            "Audio is clipped only to common coverage; pre-capture sound is never invented.",
        ],
        "input_sha256": {
            "frames.csv": sha(frames / "frames.csv"),
            "frame-source.json": sha(frames / "frame-source.json"),
            "capture-end.json": sha(frames / "capture-end.json"),
            "audio.wav": sha(wav),
            "audio.wav.json": sha(Path(str(wav) + ".json")),
            "audio.wav.packets.csv": sha(Path(str(wav) + ".packets.csv")),
        },
        "frame_sha256": hashes,
    }
    (output / "alignment.json").write_text(
        json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8"
    )
    return report


def signal_regions(pcm: Path, chunks: list[tuple[int, int, int, int]], threshold=100):
    """Codec audit only: merge sub-10ms sine zero crossings, retain actual timestamped edges."""
    raw = pcm.read_bytes()
    regions = []
    first = last = None
    for pts, duration, offset, length in chunks:
        for frame, (left, right) in enumerate(
            struct.iter_unpack("<hh", raw[offset : offset + length])
        ):
            if max(abs(left), abs(right)) <= threshold:
                continue
            timestamp = pts + (frame * TICKS + 24000) // 48000
            if last is not None and timestamp - last > 100000:
                regions.append([first, last])
                first = None
            if first is None:
                first = timestamp
            last = timestamp
    if first is not None:
        regions.append([first, last])
    return regions


def verify_encoded(directory: Path) -> dict:
    """Read the encoder's independent MP4 demux/decode output and reject timing changes."""
    expected = [
        line.split("\t")
        for line in (directory / "frames.tsv").read_text(encoding="utf-8").splitlines()
    ]
    with (directory / "decoded/decoded-timeline.csv").open(newline="") as f:
        actual = list(csv.DictReader(f))
    video = [r for r in actual if r["stream"] == "video"]
    audio = [r for r in actual if r["stream"] == "audio"]
    if len(video) != len(expected) or not audio:
        raise ValueError("Encoder dropped image samples or audio.")
    deltas = [abs(int(row["time_100ns"]) - int(source[0])) for source, row in zip(expected, video)]
    durations = [
        abs(int(row["duration_100ns"]) - int(source[1])) for source, row in zip(expected, video)
    ]
    if max(deltas) > 10 or max(durations) > 10:
        raise ValueError(
            "Encoded video timing differs from native-QPC input by over one microsecond."
        )
    raw_chunks = [
        tuple(map(int, line.split("\t")))
        for line in (directory / "audio.tsv").read_text().splitlines()
    ]
    decoded_chunks = []
    offset = 0
    for row in audio:
        length = int(row["bytes"])
        decoded_chunks.append((int(row["time_100ns"]), int(row["duration_100ns"]), offset, length))
        offset += length
    if offset != (directory / "decoded/decoded-audio.pcm").stat().st_size:
        raise ValueError("Decoded audio length mismatch.")
    original = signal_regions(directory / "audio.pcm", raw_chunks)
    decoded = signal_regions(directory / "decoded/decoded-audio.pcm", decoded_chunks)
    if len(original) != len(decoded):
        raise ValueError(
            "AAC changed the number of >10ms-separated signal regions; manual codec audit required."
        )
    edges = [
        {
            "region": i,
            "source_start_100ns": a[0],
            "decoded_start_100ns": b[0],
            "source_last_100ns": a[1],
            "decoded_last_100ns": b[1],
            "start_shift_ms": (b[0] - a[0]) / 10000,
            "last_shift_ms": (b[1] - a[1]) / 10000,
        }
        for i, (a, b) in enumerate(zip(original, decoded))
    ]
    maximum = max(
        [abs(r[k]) for r in edges for k in ["start_shift_ms", "last_shift_ms"]], default=0
    )
    if maximum > 30:
        raise ValueError(
            "AAC signal edge shifted over 30ms; derivative cannot be presented as synchronized."
        )
    report = {
        "video_frames": len(video),
        "maximum_video_pts_delta_100ns": max(deltas),
        "maximum_video_duration_delta_100ns": max(durations),
        "audio_input_start_100ns": raw_chunks[0][0],
        "audio_decoded_start_100ns": decoded_chunks[0][0],
        "audio_input_end_100ns": raw_chunks[-1][0] + raw_chunks[-1][1],
        "audio_decoded_end_100ns": decoded_chunks[-1][0] + decoded_chunks[-1][1],
        "audio_edge_audit_threshold_pcm16": 100,
        "audio_edge_audit_join_quiet_ms": 10,
        "maximum_codec_edge_shift_ms": maximum,
        "audio_signal_regions": edges,
        "note": "Decoded viewing derivative audit; raw float samples remain the source for stop latency. No audio time correction was applied.",
    }
    (directory / "encoding-verification.json").write_text(
        json.dumps(report, indent=2) + "\n", encoding="utf-8"
    )
    return report


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--frames", type=Path, required=True)
    p.add_argument("--wav", type=Path, required=True)
    p.add_argument("--output", type=Path, required=True)
    p.add_argument("--encoder", type=Path)
    args = p.parse_args()
    report = prepare(args.frames.resolve(), args.wav.resolve(), args.output.resolve())
    if args.encoder:
        folder = args.output.resolve()
        mp4 = folder / "synchronized-evidence.mp4"
        run = subprocess.run(
            [
                str(args.encoder.resolve()),
                "--encode",
                str(folder / "frames.tsv"),
                str(folder / "audio.pcm"),
                str(folder / "audio.tsv"),
                str(report["width"]),
                str(report["height"]),
                str(report["nominal_fps"]),
                str(mp4),
                "--offline-only",
            ],
            capture_output=True,
            text=True,
            timeout=600,
        )
        (folder / "encode.log").write_text(run.stdout + run.stderr, encoding="utf-8")
        run.check_returncode()
        inspect = subprocess.run(
            [str(args.encoder.resolve()), "--inspect", str(mp4), str(folder / "decoded")],
            capture_output=True,
            text=True,
            timeout=120,
        )
        (folder / "inspect.log").write_text(inspect.stdout + inspect.stderr, encoding="utf-8")
        inspect.check_returncode()
        report["mp4_sha256"] = sha(mp4)
        report["encoder_sha256"] = sha(args.encoder)
        report["encoding_verification"] = verify_encoded(folder)
        (folder / "alignment.json").write_text(
            json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8"
        )
    print(
        json.dumps(
            {
                k: report[k]
                for k in [
                    "selected_frame_count",
                    "duration_seconds",
                    "effective_capture_fps",
                    "maximum_frame_hold_ms",
                ]
            }
        )
    )


if __name__ == "__main__":
    main()
