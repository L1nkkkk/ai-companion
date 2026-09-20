"""Measure instrumented stop commands against explicitly scoped float32 WASAPI capture.

This does not capture audio. It refuses Mono Stopwatch-only clocks and incomplete windows.
"""

from __future__ import annotations

import argparse
import bisect
import csv
import json
import math
import struct
from pathlib import Path


def read_float_wav(path: Path) -> tuple[bytes, int, int]:
    raw = path.read_bytes()
    if len(raw) < 44 or raw[:4] != b"RIFF" or raw[8:12] != b"WAVE":
        raise ValueError("Expected a complete RIFF/WAVE capture.")
    if struct.unpack_from("<I", raw, 4)[0] != len(raw) - 8:
        raise ValueError("Capture WAV is incomplete; wait until recording has stopped.")
    cursor, fmt, data = 12, None, None
    while cursor < len(raw):
        if cursor + 8 > len(raw):
            raise ValueError("Incomplete WAV chunk.")
        tag, size = struct.unpack_from("<4sI", raw, cursor)
        start, end = cursor + 8, cursor + 8 + size
        if end + (size & 1) > len(raw):
            raise ValueError("Truncated WAV chunk.")
        if tag == b"fmt ":
            if fmt is not None or size < 16:
                raise ValueError("Invalid capture format chunk.")
            fmt = struct.unpack_from("<HHIIHH", raw, start)
        elif tag == b"data":
            if data is not None:
                raise ValueError("Duplicate capture data chunk.")
            data = raw[start:end]
        cursor = end + (size & 1)
    if fmt != (3, 2, 48000, 384000, 8, 32) or data is None or len(data) % 8:
        raise ValueError(
            "Tail analysis requires this tool's 48 kHz stereo float32 capture; PCM16 dither must not be treated as signal."
        )
    return data, 48000, 2


def analyze(wav_path: Path, stops_path: Path, *, threshold: float = 0.0) -> dict:
    if not math.isfinite(threshold) or not 0 <= threshold < 1:
        raise ValueError("Invalid amplitude threshold.")
    summary = json.loads(Path(str(wav_path) + ".json").read_text(encoding="utf-8-sig"))
    if (
        summary.get("capture_mode") != "include_target_process_tree_only"
        or not summary.get("expected_image_verified")
        or not summary.get("fixture_only_acknowledged")
        or summary.get("system_loopback_used") is not False
        or summary.get("microphone_used") is not False
    ):
        raise ValueError("Missing verified process-only fixture capture provenance.")
    pcm, rate, channels = read_float_wav(wav_path)
    with Path(str(wav_path) + ".packets.csv").open(newline="", encoding="utf-8-sig") as handle:
        packets = list(csv.DictReader(handle))
    with stops_path.open(newline="", encoding="utf-8-sig") as handle:
        reader = csv.DictReader(handle)
        if not {"stop_qpc_ticks", "qpc_frequency"}.issubset(reader.fieldnames or []):
            raise ValueError(
                "Player stop log lacks Win32 QPC anchors. Mono Stopwatch ticks can have a different origin and cannot be compared directly."
            )
        stops = list(reader)
    if not packets or not stops:
        raise ValueError("Need actual captured packets and stop commands.")

    offsets, starts, ends, flags = [], [], [], []
    frames_total = 0
    gap_indexes = set()
    for index, packet in enumerate(packets):
        frames = int(packet["frames"])
        timestamp = int(packet["audio_qpc_100ns"])
        if frames <= 0 or timestamp <= 0:
            raise ValueError("Invalid capture packet size or timestamp.")
        start = timestamp / 10_000_000
        end = start + frames / rate
        if starts and start < starts[-1]:
            raise ValueError("Capture timestamps moved backwards.")
        if ends and abs(start - ends[-1]) > 0.002:
            gap_indexes.add(index)
        offsets.append(frames_total * channels * 4)
        starts.append(start)
        ends.append(end)
        flags.append(int(packet["flags"]))
        frames_total += frames
    if frames_total * channels * 4 != len(pcm) or frames_total != int(summary["frames"]):
        raise ValueError("WAV, packet log and capture summary disagree on the actual frame count.")

    samples = []
    valid = []
    for stop in stops:
        frequency = int(stop["qpc_frequency"])
        command_ticks = int(stop["stop_qpc_ticks"])
        if frequency <= 0 or command_ticks <= 0:
            raise ValueError("Invalid native stop clock.")
        command = command_ticks / frequency
        begin, finish = command - 0.25, command + 0.5
        item = {
            "sample": int(stop["sample"]),
            "request_id": stop.get("request_id"),
            "stop_qpc_ticks": command_ticks,
            "qpc_frequency": frequency,
            "status": "invalid",
            "tail_ms": None,
        }
        if begin < starts[0] or finish > ends[-1]:
            item["reason"] = "capture_does_not_cover_250ms_before_and_500ms_after_stop"
            samples.append(item)
            continue
        first = max(0, bisect.bisect_right(ends, begin))
        last = min(len(packets), bisect.bisect_left(starts, finish))
        if any(flags[index] & 0x5 or index in gap_indexes for index in range(first, last)):
            item["reason"] = "discontinuity_or_timestamp_error_in_measurement_window"
            samples.append(item)
            continue
        last_signal = None
        peak = 0.0
        for index in range(first, last):
            packet_frames = int(packets[index]["frames"])
            low = max(0, math.ceil((begin - starts[index]) * rate))
            high = min(packet_frames, math.ceil((finish - starts[index]) * rate))
            for frame in range(low, high):
                left, right = struct.unpack_from("<ff", pcm, offsets[index] + frame * 8)
                if not math.isfinite(left) or not math.isfinite(right):
                    raise ValueError("Non-finite capture samples.")
                value = max(abs(left), abs(right))
                peak = max(peak, value)
                if value > threshold:
                    last_signal = starts[index] + frame / rate
        if last_signal is None:
            item["reason"] = "no_target_signal_near_stop"
        elif finish - last_signal < 0.05:
            item["reason"] = "no_50ms_quiet_tail_within_observation_window"
        else:
            delta = (last_signal - command) * 1000
            item.update(
                status="valid",
                reason=None,
                signed_last_signal_ms=delta,
                tail_ms=max(0.0, delta),
                window_peak=peak,
                last_signal_qpc_100ns=round(last_signal * 10_000_000),
            )
            valid.append(item["tail_ms"])
        samples.append(item)
    ordered = sorted(valid)

    def percentile(fraction: float):
        return ordered[max(0, math.ceil(len(ordered) * fraction) - 1)] if ordered else None

    return {
        "method": "Instrumented Player stop command Win32 QPC to last process-loopback sample above the declared amplitude threshold; not human finger latency or acoustic speaker output.",
        "capture_pid": summary["target_pid"],
        "capture_codec": "float32/stereo/48000",
        "threshold_full_scale": threshold,
        "pre_window_ms": 250,
        "post_window_ms": 500,
        "required_quiet_tail_ms": 50,
        "requested_samples": len(stops),
        "valid_samples": len(valid),
        "invalid_samples": len(stops) - len(valid),
        "p50_ms": percentile(0.5),
        "p95_ms": percentile(0.95),
        "ua05_count_and_p95_subcheck": len(valid) >= 20 and percentile(0.95) <= 200,
        "samples": samples,
    }


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--wav", type=Path, required=True)
    parser.add_argument("--stops", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--amplitude-threshold", type=float, default=0.0)
    args = parser.parse_args()
    result = analyze(args.wav, args.stops, threshold=args.amplitude_threshold)
    with args.output.open("x", encoding="utf-8") as handle:
        json.dump(result, handle, ensure_ascii=False, indent=2)
        handle.write("\n")
    print(
        json.dumps(
            {
                name: result[name]
                for name in (
                    "requested_samples",
                    "valid_samples",
                    "invalid_samples",
                    "p95_ms",
                    "ua05_count_and_p95_subcheck",
                )
            }
        )
    )


if __name__ == "__main__":
    main()
