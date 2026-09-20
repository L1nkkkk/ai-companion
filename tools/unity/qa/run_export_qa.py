"""Run one isolated manual export case with the exact bundled portable runtime.

This does not operate the UI, change DPI, delay fixture responses or auto-repeat
turns. Only the integration owner runs it; --prepare-only never starts a process.
"""

from __future__ import annotations

import argparse
import datetime
import hashlib
import importlib.util
import json
import sys
from pathlib import Path, PurePosixPath


def sha256(path: Path) -> str:
    with path.open("rb") as source:
        return hashlib.file_digest(source, "sha256").hexdigest()


def verify_package(package: Path, expected_sha: str | None = None) -> dict:
    manifest_path = package / "package-manifest.json"
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    if manifest.get("format") != 1 or manifest.get("mode") != "fixture":
        raise ValueError("The case requires an identified fixture portable package")
    if manifest.get("source_dirty") is not False:
        raise ValueError("Acceptance cases require a package built from frozen clean source")
    if expected_sha is not None and manifest.get("source_sha") != expected_sha:
        raise ValueError("Portable source SHA does not match the requested candidate")
    paths = set()
    for item in manifest["files"]:
        relative = PurePosixPath(item["path"])
        if (
            relative.is_absolute()
            or ".." in relative.parts
            or ":" in str(relative)
            or "\\" in str(relative)
        ):
            raise ValueError("Unsafe package manifest path")
        key = str(relative)
        if key.casefold() in paths:
            raise ValueError("Duplicate portable manifest path")
        paths.add(key.casefold())
        path = package / key
        if (
            path.is_symlink()
            or not path.is_file()
            or path.stat().st_size != item["bytes"]
            or sha256(path) != item["sha256"]
        ):
            raise ValueError("Portable file does not match its manifest")
    actual = {
        path.relative_to(package).as_posix().casefold()
        for path in package.rglob("*")
        if path.is_file()
    }
    if actual != paths | {"package-manifest.json"}:
        raise ValueError("Portable package contains extra or missing files")
    for relative in ("python/python.exe", "launch_desktop.py", "NeuroSaki.exe"):
        if relative.casefold() not in paths:
            raise ValueError("Portable launch file is not covered by its manifest")
    return {
        "source_sha": manifest["source_sha"],
        "source_dirty": manifest["source_dirty"],
        "manifest_sha256": sha256(manifest_path),
        "launcher_sha256": sha256(package / "launch_desktop.py"),
        "file_count": len(actual),
    }


def prepare_case(
    package: Path,
    evidence: Path,
    phase: str,
    outcome: str,
    *,
    seconds: float,
    delay: float,
    width: int,
    height: int,
    expected_sha: str | None = None,
) -> dict:
    package, evidence = package.resolve(), evidence.resolve()
    if phase not in {"idle", "generation", "playback"} or outcome not in {"save", "cancel", "quit"}:
        raise ValueError("Unknown manual export phase or outcome")
    if not 30 <= seconds <= 1800 or not (delay == -1 or 3 <= delay <= seconds - 15):
        raise ValueError(
            "Use delay=-1 for manual F9, or leave at least 15 seconds after a 3+ second delay"
        )
    if width < 640 or height < 480:
        raise ValueError("The requested window is too small")
    if evidence == package or evidence.is_relative_to(package):
        raise ValueError("Evidence must be outside the immutable portable package")
    if evidence.exists():
        raise FileExistsError("Use a new evidence directory for every attempt")
    identity = verify_package(package, expected_sha)
    scenario = "slow_generation" if phase == "generation" else "normal"
    player = [
        str(package / "NeuroSaki.exe"),
        "-screen-fullscreen",
        "0",
        "-screen-width",
        str(width),
        "-screen-height",
        str(height),
        "-userDataPath",
        str(evidence / "userdata"),
        "-evidenceDirectory",
        str(evidence),
        "-desktopTest",
        "export-stage",
        "-exportPhase",
        phase,
        "-exportDelay",
        str(delay),
        "-smokeSeconds",
        str(seconds),
        "-logFile",
        str(evidence / "Player.log"),
    ]
    case = {
        "format": 1,
        "created_utc": datetime.datetime.now(datetime.UTC).isoformat(),
        "purpose": "Manual native export acceptance; no automatic UI or DPI changes",
        "package_directory": str(package),
        "package": identity,
        "phase": phase,
        "expected_outcome": outcome,
        "scenario": scenario,
        "seconds": seconds,
        "preparation_delay_seconds": delay,
        "expected_export_path": str(evidence / "exported-conversation.json"),
        "minimum_dialog_hold_seconds": 5,
        "player_command": player,
        "validity_rule": "export-ready.json is preparation only; actual ExportProgress.TriggerPhase/TriggerOperation must prove the clicked phase. A missed 3-second generation window is invalid, not a pass.",
    }
    evidence.mkdir(parents=True, exist_ok=False)
    write_json(evidence / "case.json", case)
    (evidence / "manual-steps.txt").write_text(
        "Only the integration owner operates this case.\n"
        "Open History and locate this conversation's Export button before preparation; delay=-1 waits for your F9.\n"
        "When export-ready.json appears, click Export in the actual requested phase.\n"
        "Keep the native window open for at least 5 seconds. Observe avatar updates and main-window controls.\n"
        f"Requested phase: {phase}; expected result: {outcome}.\n"
        f"For Save, choose exactly: {evidence / 'exported-conversation.json'}\n"
        "For Cancel, cancel the native window and verify the target file remains absent.\n"
        "For Quit, leave the native window open and close the main Player normally.\n"
        "Retain every invalid attempt in its own directory; create a new case to retry.\n",
        encoding="utf-8",
    )
    return case


def write_json(path: Path, value: dict) -> None:
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def run_case(case: dict, evidence: Path) -> int:
    package = Path(case["package_directory"])
    interpreter = package / "python/python.exe"
    if Path(sys.executable).resolve() not in {
        interpreter.resolve(),
        (package / "python/pythonw.exe").resolve(),
    }:
        raise ValueError("Run this wrapper using this package's bundled Python")
    spec = importlib.util.spec_from_file_location(
        "portable_export_launcher", package / "launch_desktop.py"
    )
    launcher = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(launcher)
    backend = None
    if case["scenario"] == "slow_generation":
        # The production launcher deliberately resets scenarios to normal. This
        # explicit QA-only child override uses the unchanged bundled backend.
        backend = [
            str(interpreter),
            "-B",
            "-c",
            "import os,runpy; os.environ['U01_PREVIEW_SCENARIO']='slow_generation'; runpy.run_module('app.unity_preview',run_name='__main__')",
        ]
    result = {
        "started_utc": datetime.datetime.now(datetime.UTC).isoformat(),
        "launcher_exit_code": None,
    }
    try:
        result["launcher_exit_code"] = launcher.run_desktop(
            package,
            evidence / "runtime",
            backend_command=backend,
            player_command=case["player_command"],
        )
        return result["launcher_exit_code"]
    except Exception as error:
        # No exception text, environment, runtime token or private history is logged.
        result["error_code"] = getattr(error, "code", "export_qa_launcher_failed")
        raise
    finally:
        result["finished_utc"] = datetime.datetime.now(datetime.UTC).isoformat()
        result["runtime_config_exists"] = (evidence / "runtime/config.json").exists()
        export = Path(case["expected_export_path"])
        result["export_file_exists"] = export.is_file()
        if export.is_file():
            result["export_bytes"] = export.stat().st_size
            result["export_sha256"] = sha256(export)
        try:
            identity = verify_package(package, case["package"]["source_sha"])
            result["package_after_run"] = identity
            result["package_unchanged"] = identity == case["package"]
        except Exception:
            result["package_unchanged"] = False
        result["acceptance_status"] = "requires independent ExportProgress/frame/history review"
        write_json(evidence / "run-result.json", result)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--package", required=True, type=Path)
    parser.add_argument("--evidence", required=True, type=Path)
    parser.add_argument("--phase", required=True, choices=["idle", "generation", "playback"])
    parser.add_argument("--outcome", required=True, choices=["save", "cancel", "quit"])
    parser.add_argument("--seconds", type=float, default=240)
    parser.add_argument("--delay", type=float, default=15)
    parser.add_argument("--width", type=int, default=1280)
    parser.add_argument("--height", type=int, default=800)
    parser.add_argument("--expected-source-sha")
    parser.add_argument("--prepare-only", action="store_true")
    arguments = parser.parse_args()
    case = prepare_case(
        arguments.package,
        arguments.evidence,
        arguments.phase,
        arguments.outcome,
        seconds=arguments.seconds,
        delay=arguments.delay,
        width=arguments.width,
        height=arguments.height,
        expected_sha=arguments.expected_source_sha,
    )
    if arguments.prepare_only:
        print("Manual case prepared; no process was started.")
        return 0
    return run_case(case, arguments.evidence.resolve())


if __name__ == "__main__":
    raise SystemExit(main())
