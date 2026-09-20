"""Read-only checks for one actual export-stage case; never replaces manual UI review."""

from __future__ import annotations

import argparse
import csv
import hashlib
import json
import math
from pathlib import Path


def digest(path: Path) -> str:
    with path.open("rb") as source:
        return hashlib.file_digest(source, "sha256").hexdigest()


def read_json(path: Path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def read_csv(path: Path):
    with path.open(encoding="utf-8-sig", newline="") as source:
        return list(csv.DictReader(source))


def analyze(evidence: Path) -> dict:
    evidence = evidence.resolve()
    required = [
        "case.json",
        "run-result.json",
        "player-result.json",
        "export-ready.json",
        "export-events.csv",
        "frame-times.csv",
        "userdata/history/conversations.v1.json",
    ]
    missing = [name for name in required if not (evidence / name).is_file()]
    if missing:
        return {"status": "incomplete_evidence", "missing": missing, "failures": []}
    case, run, player, ready = [read_json(evidence / name) for name in required[:4]]
    events = read_csv(evidence / "export-events.csv")
    frames = read_csv(evidence / "frame-times.csv")
    history = read_json(evidence / "userdata/history/conversations.v1.json")
    invalid, failures = [], []
    stages = [row["stage"] for row in events]

    def require(condition, description):
        if not condition:
            failures.append(description)

    if stages.count("Stopping") != 1 or stages.count("DialogOpen") != 1:
        invalid.append(
            "The case must contain exactly one attempted and opened native export dialog"
        )
    expected_phase = {"idle": "Ready", "generation": "Thinking", "playback": "Speaking"}[
        case["phase"]
    ]
    trigger = next((row for row in events if row["stage"] == "Stopping"), None)
    if (
        trigger is None
        or trigger["trigger_phase"] != expected_phase
        or ready.get("expected_phase") != case["phase"]
    ):
        invalid.append("Actual click did not occur in the requested phase; repeat with a new case")
    frequency = int(player.get("stopwatchFrequency", 0))
    if frequency <= 0:
        invalid.append(
            "Missing declared Stopwatch frequency; native QPC origin cannot substitute for it"
        )
    outcome = case["expected_outcome"]
    terminal = "Saved" if outcome == "save" else "Cancelled"
    require("Failed" not in stages, "Export reported a product failure")
    require(
        "Finished" in stages and terminal in stages,
        "Native export did not finish with the requested outcome",
    )
    if "Stopped" in stages:
        stopped = next(row for row in events if row["stage"] == "Stopped")
        require(float(stopped["mouth"]) == 0, "Mouth was not closed before native selection")
        require(
            "Stopping" in stages
            and "DialogOpen" in stages
            and stages.index("Stopping") < stages.index("Stopped") < stages.index("DialogOpen"),
            "Native window opened before local stop completed",
        )
    else:
        failures.append("Missing local-stop completion event")
    dialog_seconds = None
    if "DialogOpen" in stages and terminal in stages and frequency > 0:
        opened = next(row for row in events if row["stage"] == "DialogOpen")
        # Saving is emitted on the worker as soon as native file selection returns.
        closed = next(
            row for row in events if row["stage"] == ("Saving" if "Saving" in stages else terminal)
        )
        dialog_seconds = (
            int(closed["stage_monotonic_ticks"]) - int(opened["stage_monotonic_ticks"])
        ) / frequency
        if dialog_seconds < case["minimum_dialog_hold_seconds"]:
            invalid.append("Native window was held for less than the declared minimum interval")
    frame_ms = [float(row["frame_ms"]) for row in frames]
    require(
        bool(frame_ms) and all(math.isfinite(value) and 0 <= value < 1000 for value in frame_ms),
        "A frame reached one second, or frame data is absent/invalid",
    )
    require(len(frames) == player.get("frames"), "Frame count does not match final Player result")
    if frames:
        require(
            abs(float(frames[-1]["seconds"]) - player["durationSeconds"]) < 0.2,
            "Frame record does not cover the run end",
        )
        for previous, current in zip(frames, frames[1:]):
            interval = float(current["seconds"]) - float(previous["seconds"])
            require(
                interval > 0 and abs(interval * 1000 - float(current["frame_ms"])) < 5,
                "Frame timeline has missing samples or inconsistent intervals",
            )
            if (
                failures
                and failures[-1] == "Frame timeline has missing samples or inconsistent intervals"
            ):
                break
    require(
        player.get("errors") == 0 and not player.get("failures"),
        "Player recorded runtime errors or failed checks",
    )
    require(
        player.get("scriptedChecksCompleted") is True,
        "Player did not complete its export-stage checks",
    )
    require(
        run.get("launcher_exit_code") == 0 and not run.get("runtime_config_exists"),
        "Launcher did not exit cleanly and remove its runtime configuration",
    )
    require(run.get("package_unchanged") is True, "Portable files changed during the case")
    export_path = Path(case["expected_export_path"])
    snapshot = read_json(export_path) if export_path.is_file() else None
    if outcome == "save":
        require(snapshot is not None, "Save case has no selected export file")
        if snapshot is not None:
            require(
                trigger is not None
                and snapshot.get("schemaVersion") == 1
                and snapshot["conversation"]["id"] == trigger["conversation_id"],
                "Saved snapshot belongs to a different conversation",
            )
    else:
        require(snapshot is None, "Cancel/quit case unexpectedly wrote an export file")
    target = None
    if trigger is not None:
        conversations = [
            item for item in history["conversations"] if item["id"] == trigger["conversation_id"]
        ]
        require(len(conversations) == 1, "Stopped conversation is absent from durable history")
        if conversations:
            records = conversations[0]["records"]
            matches = [
                row
                for row in records
                if row["role"] == 1
                and row["requestId"] == trigger["request_id"]
                and str(row["generation"]) == trigger["generation"]
            ]
            require(
                len(matches) == 1, "Cannot identify the assistant record for the export trigger"
            )
            if matches:
                target = matches[0]
                played, total = target["playedSamples"], target["totalSamples"]
                require(
                    target["deliveryState"] == (3 if case["phase"] == "idle" else 4),
                    "Assistant durable delivery state is not the expected Played/Interrupted terminal",
                )
                if case["phase"] == "idle":
                    require(
                        played == total and total > 0,
                        "Idle case did not retain a complete audio record",
                    )
                elif case["phase"] == "generation":
                    require(played == total == 0, "Generation case already had audio progress")
                else:
                    require(
                        0 < played < total,
                        "Playback case did not preserve an actual interrupted output position",
                    )
                if snapshot is not None:
                    require(
                        snapshot["conversation"]["records"] == records,
                        "Saved export differs from the final durable conversation",
                    )
        first = int(trigger["stage_monotonic_ticks"])
        if (evidence / "events.csv").is_file():
            late = [
                row
                for row in read_csv(evidence / "events.csv")
                if row["event"] == "playback-started"
                and int(row["monotonic_ticks"]) > first
                and row["request_id"] == trigger["request_id"]
                and row["generation"] == trigger["generation"]
            ]
            require(not late, "Old operation started playback after export stopped it")
    return {
        "status": "invalid_attempt"
        if invalid
        else "failed"
        if failures
        else "automated_checks_passed",
        "scope": "Native stage, whole-run frame continuity, durable terminal/export and package cleanup checks; manual avatar/input/window observations still required",
        "phase": case["phase"],
        "outcome": outcome,
        "invalid_reasons": invalid,
        "failures": failures,
        "dialog_open_seconds": dialog_seconds,
        "maximum_frame_ms": max(frame_ms, default=None),
        "target_delivery_state": target["deliveryState"] if target else None,
        "files_sha256": {name: digest(evidence / name) for name in required},
        "export_sha256": digest(export_path) if snapshot is not None else None,
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--evidence", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    arguments = parser.parse_args()
    if arguments.output.exists():
        raise FileExistsError("Use a new analysis output; do not overwrite prior results")
    result = analyze(arguments.evidence)
    arguments.output.write_text(
        json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8"
    )
    print(result["status"])
    return 0 if result["status"] == "automated_checks_passed" else 1


if __name__ == "__main__":
    raise SystemExit(main())
