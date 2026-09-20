"""Launch one final-package evidence case using that package's bundled Python.

Run with <package>/python/python.exe -B this-script.py --package <package>
--evidence <new-directory> --expected-source-sha <40-hex-sha> --mode <mode>.
--prepare-only verifies and writes the command without starting any process.
This wrapper does not operate windows, change system DPI, or record system audio.
"""

from __future__ import annotations

import argparse
import datetime
import importlib.util
import math
import re
import sys
from pathlib import Path

# Importing the immutable package launcher must not create __pycache__ inside it.
sys.dont_write_bytecode = True
MODES = {"manual", "audiovisual", "fixture", "generation", "late-audio", "close"}


def load_module(name: str, path: Path):
    spec = importlib.util.spec_from_file_location(name, path)
    if spec is None or spec.loader is None:
        raise ValueError("Required QA module is unavailable")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def repository_root() -> Path:
    # Resolve from this report's location, never from the shell's working directory.
    for parent in Path(__file__).resolve().parents:
        if (parent / "tools/unity/qa/run_export_qa.py").is_file():
            return parent
    raise ValueError("Keep this report tool in its repository checkout")


qa = load_module(
    "round2_package_verification", repository_root() / "tools/unity/qa/run_export_qa.py"
)


def utc_now() -> str:
    return datetime.datetime.now(datetime.UTC).isoformat()


def prepare_case(
    package: Path,
    evidence: Path,
    mode: str,
    *,
    seconds: float,
    width: int,
    height: int,
    expected_sha: str,
    monitor: int | None = None,
    user_data: Path | None = None,
) -> dict:
    if mode not in MODES:
        raise ValueError("Unknown evidence mode")
    if not re.fullmatch(r"[0-9a-fA-F]{40}", expected_sha):
        raise ValueError("An explicit complete source SHA is required")
    if not math.isfinite(seconds) or not 1 <= seconds <= 1800:
        raise ValueError("Use a duration between 1 and 1800 seconds")
    if width < 640 or height < 480 or (monitor is not None and monitor < 1):
        raise ValueError("Use a 640x480 or larger window and a one-based monitor number")
    if mode == "audiovisual" and (
        seconds < 65 or width > 2048 or height > 1440 or width % 2 or height % 2
    ):
        raise ValueError("AV needs 65+ seconds and even dimensions within 2048x1440")
    package, evidence = package.resolve(), evidence.resolve()
    if evidence == package or evidence.is_relative_to(package):
        raise ValueError("Evidence must be outside the immutable package")
    if evidence.exists():
        raise FileExistsError("Every attempt requires a new evidence directory")
    if user_data is not None and not user_data.is_absolute():
        raise ValueError("Reused user data requires an explicit absolute directory")
    history = user_data.resolve() if user_data is not None else evidence / "userdata"
    if history == package or history.is_relative_to(package):
        raise ValueError("User data must be outside the immutable package")
    if history == Path(history.anchor) or (history.exists() and not history.is_dir()):
        raise ValueError("Use a dedicated user-data directory")
    identity = qa.verify_package(package, expected_sha.lower())
    scenario = {"generation": "slow_generation", "late-audio": "slow_download"}.get(mode, "normal")
    player = [
        str(package / "NeuroSaki.exe"),
        "-screen-fullscreen",
        "0",
        "-screen-width",
        str(width),
        "-screen-height",
        str(height),
        "-userDataPath",
        str(history),
        "-evidenceDirectory",
        str(evidence),
        "-smokeSeconds",
        str(seconds),
        "-logFile",
        str(evidence / "Player.log"),
    ]
    if monitor is not None:
        # Unity's official Player flag is one-based; this does not change OS scaling.
        player += ["-monitor", str(monitor)]
    if mode != "manual":
        player += ["-desktopTest", "fixture" if mode == "close" else mode]
    if mode == "audiovisual":
        player += ["-evidenceSourceSha", identity["source_sha"]]
    if mode == "close":
        # Verified against DesktopEvidenceRunner.Exercise in the frozen product.
        player += ["-quitDuringPlayback", "true"]
    case = {
        "format": 1,
        "created_utc": utc_now(),
        "purpose": "Final portable Player evidence; manual UI and independent review required",
        "package_directory": str(package),
        "package": identity,
        "wrapper_sha256": qa.sha256(Path(__file__)),
        "mode": mode,
        "scenario": scenario,
        "seconds": seconds,
        "width": width,
        "height": height,
        "monitor": monitor,
        "user_data_directory": str(history),
        "user_data_explicit": user_data is not None,
        "user_data_existed_before": history.exists(),
        "player_command": player,
        "scope": (
            "Manual mode never submits automatically. AV enables only the Player's own rendered "
            "frame evidence; separate explicit PID-scoped audio recording is required. Monitor "
            "selection does not prove Windows DPI. Close triggers Application.Quit after actual "
            "audio output; it is not a physical Alt+F4 test. Explicit user data is used in place, "
            "never copied, cleared or read by this wrapper."
        ),
    }
    evidence.mkdir(parents=True, exist_ok=False)
    qa.write_json(evidence / "case.json", case)
    return case


def backend_command(package: Path, scenario: str) -> list[str] | None:
    if scenario == "normal":
        return None
    if scenario not in {"slow_generation", "slow_download"}:
        raise ValueError("Unsupported controlled fixture scenario")
    # The production launcher resets scenarios to normal. Only this explicit QA child
    # entry point overrides it, still using the unchanged bundled backend/runtime.
    return [
        str(package / "python/python.exe"),
        "-B",
        "-c",
        f"import os,runpy; os.environ['U01_PREVIEW_SCENARIO']={scenario!r}; "
        "runpy.run_module('app.unity_preview',run_name='__main__')",
    ]


def run_case(case: dict, evidence: Path) -> int:
    if (evidence / "run-result.json").exists():
        raise FileExistsError("Do not run the same evidence case twice")
    package = Path(case["package_directory"])
    interpreter = package / "python/python.exe"
    result = {
        "format": 1,
        "started_utc": utc_now(),
        "launcher_exit_code": None,
        "player_exit_code": None,
        "backend_exit_code": None,
        "process_started": False,
    }
    observed = []
    launcher = original_owner = None
    wrapper_code = 1
    try:
        if Path(sys.executable).resolve() not in {
            interpreter.resolve(),
            (package / "python/pythonw.exe").resolve(),
        }:
            result["error_code"] = "bundled_python_required"
            raise ValueError("Use this exact package's Python interpreter")
        before = qa.verify_package(package, case["package"]["source_sha"])
        if before != case["package"]:
            raise ValueError("Package identity changed after case preparation")
        result["package_before_run"] = before
        launcher = load_module("round2_portable_launcher", package / "launch_desktop.py")
        original_owner = launcher.OwnedProcesses

        class ObservedProcesses(original_owner):
            # Observation only: process creation, environment, job supervision and cleanup
            # remain the bundled launcher's exact implementation. Do not log environments.
            def start(self, command, **kwargs):
                child = super().start(command, **kwargs)
                role = (
                    "player"
                    if Path(command[0]).resolve() == package / "NeuroSaki.exe"
                    else "backend"
                )
                observed.append((role, child))
                return child

        launcher.OwnedProcesses = ObservedProcesses
        result["launcher_exit_code"] = launcher.run_desktop(
            package,
            evidence / "runtime",
            backend_command=backend_command(package, case["scenario"]),
            player_command=case["player_command"],
        )
        wrapper_code = result["launcher_exit_code"]
    except KeyboardInterrupt:
        result["error_code"] = "interrupted"
        wrapper_code = 130
    except Exception as error:
        # Exception text, private history, runtime config and environment are never logged.
        result.setdefault("error_code", getattr(error, "code", "player_evidence_launch_failed"))
        result["error_type"] = type(error).__name__
    finally:
        if launcher is not None and original_owner is not None:
            launcher.OwnedProcesses = original_owner
        for role, child in observed:
            result[f"{role}_pid"] = child.pid
            # OwnedProcesses already reaped/closed its handles; use its cached return code.
            result[f"{role}_exit_code"] = child.returncode
        result["process_started"] = bool(observed)
        result["finished_utc"] = utc_now()
        result["runtime_config_exists"] = (evidence / "runtime/config.json").exists()
        result["pending_config_files"] = len(list((evidence / "runtime").glob("config-*.pending")))
        try:
            after = qa.verify_package(package, case["package"]["source_sha"])
            result["package_after_run"] = after
            result["package_unchanged"] = after == case["package"]
        except Exception:
            result["package_unchanged"] = False
        if (
            not result["package_unchanged"]
            or result["runtime_config_exists"]
            or result["pending_config_files"]
        ):
            wrapper_code = 1
        result["wrapper_exit_code"] = wrapper_code
        result["acceptance_status"] = "requires independent frame/history/DPI/audio-video review"
        qa.write_json(evidence / "run-result.json", result)
    return wrapper_code


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--package", required=True, type=Path)
    parser.add_argument("--evidence", required=True, type=Path)
    parser.add_argument("--mode", required=True, choices=sorted(MODES))
    parser.add_argument("--seconds", type=float, default=120)
    parser.add_argument("--width", type=int, default=1280)
    parser.add_argument("--height", type=int, default=800)
    parser.add_argument("--monitor", type=int)
    parser.add_argument("--expected-source-sha", required=True)
    parser.add_argument("--user-data", type=Path)
    parser.add_argument("--prepare-only", action="store_true")
    arguments = parser.parse_args()
    case = prepare_case(
        arguments.package,
        arguments.evidence,
        arguments.mode,
        seconds=arguments.seconds,
        width=arguments.width,
        height=arguments.height,
        expected_sha=arguments.expected_source_sha,
        monitor=arguments.monitor,
        user_data=arguments.user_data,
    )
    if arguments.prepare_only:
        print("Evidence command prepared; no process was started.")
        return 0
    return run_case(case, arguments.evidence.resolve())


if __name__ == "__main__":
    raise SystemExit(main())
