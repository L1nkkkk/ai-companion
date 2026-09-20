"""Evidence isolation and immutable package checks; no application is launched."""

import csv
import importlib.util
import json
from pathlib import Path

import pytest

SPEC = importlib.util.spec_from_file_location(
    "export_qa", Path(__file__).with_name("run_export_qa.py")
)
qa = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(qa)


def package_fixture(tmp_path):
    package = tmp_path / "portable"
    package.mkdir()
    rows = []
    for name in ("python/python.exe", "NeuroSaki.exe", "launch_desktop.py"):
        path = package / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(b"nonexecutable controlled test fixture")
        rows.append({"path": name, "bytes": path.stat().st_size, "sha256": qa.sha256(path)})
    (package / "package-manifest.json").write_text(
        json.dumps(
            {
                "format": 1,
                "mode": "fixture",
                "source_sha": "a" * 40,
                "source_dirty": False,
                "files": rows,
            }
        ),
        encoding="utf-8",
    )
    return package


def prepare(package, evidence):
    return qa.prepare_case(
        package,
        evidence,
        "generation",
        "cancel",
        seconds=240,
        delay=15,
        width=1280,
        height=800,
        expected_sha="a" * 40,
    )


def test_prepare_never_executes_files_and_preserves_evidence_on_duplicate(tmp_path):
    package = package_fixture(tmp_path)
    evidence = tmp_path / "case"
    case = prepare(package, evidence)
    original = (evidence / "case.json").read_bytes()
    assert case["scenario"] == "slow_generation"
    assert not (evidence / "runtime").exists()
    with pytest.raises(FileExistsError):
        prepare(package, evidence)
    assert (evidence / "case.json").read_bytes() == original


def test_changed_binary_is_rejected_before_creating_evidence(tmp_path):
    package = package_fixture(tmp_path)
    (package / "NeuroSaki.exe").write_bytes(b"modified fixture")
    with pytest.raises(ValueError, match="does not match"):
        prepare(package, tmp_path / "case")
    assert not (tmp_path / "case").exists()


def test_untracked_runtime_data_and_wrong_source_are_rejected(tmp_path):
    package = package_fixture(tmp_path)
    with pytest.raises(ValueError, match="source SHA"):
        qa.verify_package(package, "b" * 40)
    (package / "config.json").write_text("{}", encoding="utf-8")
    with pytest.raises(ValueError, match="extra or missing"):
        prepare(package, tmp_path / "case")
    assert not (tmp_path / "case").exists()


def test_evidence_cannot_be_written_inside_deliverable(tmp_path):
    package = package_fixture(tmp_path)
    with pytest.raises(ValueError, match="outside"):
        prepare(package, package / "evidence")
    assert not (package / "evidence").exists()


ANALYSIS_SPEC = importlib.util.spec_from_file_location(
    "export_analysis", Path(__file__).with_name("analyze_export_qa.py")
)
analysis = importlib.util.module_from_spec(ANALYSIS_SPEC)
ANALYSIS_SPEC.loader.exec_module(analysis)


def write_csv(path, rows):
    with path.open("w", encoding="utf-8", newline="") as output:
        writer = csv.DictWriter(output, fieldnames=list(rows[0]))
        writer.writeheader()
        writer.writerows(rows)


def evidence_fixture(tmp_path, outcome="cancel"):
    export = tmp_path / "exported-conversation.json"
    qa.write_json(
        tmp_path / "case.json",
        {
            "phase": "playback",
            "expected_outcome": outcome,
            "minimum_dialog_hold_seconds": 5,
            "expected_export_path": str(export),
        },
    )
    qa.write_json(
        tmp_path / "run-result.json",
        {"launcher_exit_code": 0, "runtime_config_exists": False, "package_unchanged": True},
    )
    qa.write_json(
        tmp_path / "player-result.json",
        {
            "stopwatchFrequency": 1000,
            "frames": 100,
            "durationSeconds": 10,
            "errors": 0,
            "failures": [],
            "scriptedChecksCompleted": True,
        },
    )
    qa.write_json(
        tmp_path / "export-ready.json", {"expected_phase": "playback", "phase": "Speaking"}
    )
    stages = [("Stopping", 1000), ("Stopped", 1001), ("Capturing", 1002), ("DialogOpen", 2000)]
    stages += [("Saving", 8000), ("Saved", 8001)] if outcome == "save" else [("Cancelled", 8000)]
    stages += [("Finished", 8002)]
    events = [
        {
            "stage": stage,
            "stage_monotonic_ticks": tick,
            "observed_native_qpc_ticks": tick * 10,
            "qpc_frequency": 10000,
            "trigger_phase": "Speaking",
            "conversation_id": "conversation",
            "request_id": "request",
            "generation": 1,
            "current_phase": "Ready",
            "played_samples": 16,
            "total_samples": 192000,
            "mouth": 0,
        }
        for stage, tick in stages
    ]
    write_csv(tmp_path / "export-events.csv", events)
    write_csv(
        tmp_path / "frame-times.csv",
        [{"seconds": f"{i / 10:.1f}", "frame_ms": 100} for i in range(1, 101)],
    )
    history_path = tmp_path / "userdata/history/conversations.v1.json"
    history_path.parent.mkdir(parents=True)
    conversation = {
        "id": "conversation",
        "records": [
            {
                "role": 1,
                "requestId": "request",
                "generation": 1,
                "deliveryState": 4,
                "playedSamples": 16,
                "totalSamples": 192000,
                "text": "test fixture",
            }
        ],
    }
    qa.write_json(history_path, {"conversations": [conversation]})
    if outcome == "save":
        qa.write_json(export, {"schemaVersion": 1, "conversation": conversation})
    return events


def test_analyzer_accepts_complete_cancel_evidence_without_claiming_manual_qa(tmp_path):
    evidence_fixture(tmp_path)
    result = analysis.analyze(tmp_path)
    assert result["status"] == "automated_checks_passed"
    assert result["dialog_open_seconds"] == 6
    assert "manual" in result["scope"]


def test_analyzer_rejects_old_export_blocking_frame(tmp_path):
    evidence_fixture(tmp_path)
    frames = analysis.read_csv(tmp_path / "frame-times.csv")
    frames[40]["frame_ms"] = 17728.3486
    write_csv(tmp_path / "frame-times.csv", frames)
    result = analysis.analyze(tmp_path)
    assert result["status"] == "failed"
    assert any("one second" in failure for failure in result["failures"])


def test_analyzer_marks_missed_generation_or_playback_phase_invalid(tmp_path):
    events = evidence_fixture(tmp_path)
    events[0]["trigger_phase"] = "Ready"
    write_csv(tmp_path / "export-events.csv", events)
    result = analysis.analyze(tmp_path)
    assert result["status"] == "invalid_attempt"


def test_analyzer_rejects_stale_generated_export_after_terminal_history(tmp_path):
    evidence_fixture(tmp_path, "save")
    export = tmp_path / "exported-conversation.json"
    snapshot = analysis.read_json(export)
    snapshot["conversation"]["records"][0]["deliveryState"] = 1
    qa.write_json(export, snapshot)
    result = analysis.analyze(tmp_path)
    assert result["status"] == "failed"
    assert "Saved export differs from the final durable conversation" in result["failures"]
