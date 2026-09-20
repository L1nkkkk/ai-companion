"""Explicit fixture-latency injection for manual generation-stage export QA.

The portable package and acceptance analyzer stay unchanged. Use the package's
Python with -B. No window is operated, and --prepare-only starts no process.
"""

from __future__ import annotations

import argparse
import datetime
import importlib.util
import re
import sys
from pathlib import Path

sys.dont_write_bytecode = True
ROOT = next(
    parent
    for parent in Path(__file__).resolve().parents
    if (parent / "tools/unity/qa/run_export_qa.py").is_file()
)


def load(name: str, path: Path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


qa = load("generation_export_qa", ROOT / "tools/unity/qa/run_export_qa.py")

# This uses only public constructors already present in frozen 3110a56. The
# settings field affects both reply and speech fixture providers; document both.
BACKEND_CODE = (
    "from dataclasses import replace; import uvicorn; "
    "from app.unity_preview import PreviewSettings, create_preview_app; "
    "settings=PreviewSettings.from_environment()\n"
    "if settings.mode != 'fixture': raise ValueError('Fixture-only QA required')\n"
    "settings=replace(settings, scenario='slow_generation', fixture_delay=8.0); "
    "uvicorn.run(create_preview_app(settings), host='127.0.0.1', port=8000, "
    "workers=1, access_log=False)"
)


def prepare(package, evidence, outcome, *, seconds, delay, width, height, expected_sha):
    if not re.fullmatch(r"[0-9a-f]{40}", expected_sha):
        raise ValueError("An explicit full lowercase source SHA is required")
    case = qa.prepare_case(
        package,
        evidence,
        "generation",
        outcome,
        seconds=seconds,
        delay=delay,
        width=width,
        height=height,
        expected_sha=expected_sha,
    )
    case["wrapper_sha256"] = qa.sha256(Path(__file__))
    case["fixture_latency_injection"] = {
        "api": "dataclasses.replace(PreviewSettings.from_environment(), scenario='slow_generation', fixture_delay=8.0); create_preview_app(settings)",
        "fixture_reply_delay_seconds": 8.0,
        "existing_slow_generation_delay_seconds": 3.0,
        "accepted_to_text_minimum_seconds_if_not_cancelled": 11.0,
        "fixture_speech_delay_seconds_if_reached": 8.0,
        "package_files_modified": False,
        "scope": "Explicit QA latency injection through frozen Settings API, not default production timing. Real accepted generation, cancellation, persistence and native dialog paths remain active. No acceptance threshold or analyzer is changed.",
    }
    case["backend_command"] = [
        str(Path(case["package_directory"]) / "python/python.exe"),
        "-B",
        "-c",
        BACKEND_CODE,
    ]
    case["validity_rule"] = (
        "Actual ExportProgress.TriggerPhase/TriggerOperation must prove accepted generation "
        "at the click. A missed injected 11-second window remains an invalid attempt, "
        "never a pass. Preserve the earlier default-timing invalid attempt."
    )
    qa.write_json(evidence / "case.json", case)
    return case


def run(case, evidence):
    if (evidence / "run-result.json").exists():
        raise FileExistsError("Do not overwrite an existing run result")
    package = Path(case["package_directory"])
    result = {
        "started_utc": datetime.datetime.now(datetime.UTC).isoformat(),
        "launcher_exit_code": None,
        "fixture_latency_injection": case["fixture_latency_injection"],
    }
    code = 1
    try:
        if Path(sys.executable).resolve() not in {
            (package / "python/python.exe").resolve(),
            (package / "python/pythonw.exe").resolve(),
        }:
            raise ValueError("Use this exact package's Python interpreter")
        if qa.verify_package(package, case["package"]["source_sha"]) != case["package"]:
            raise ValueError("Package changed after preparation")
        launcher = load("generation_export_launcher", package / "launch_desktop.py")
        code = result["launcher_exit_code"] = launcher.run_desktop(
            package,
            evidence / "runtime",
            backend_command=case["backend_command"],
            player_command=case["player_command"],
        )
    except KeyboardInterrupt:
        result["error_code"] = "interrupted"
        code = 130
    except Exception as error:
        result["error_code"] = getattr(error, "code", "generation_export_launch_failed")
        result["error_type"] = type(error).__name__
    finally:
        result["finished_utc"] = datetime.datetime.now(datetime.UTC).isoformat()
        result["runtime_config_exists"] = (evidence / "runtime/config.json").exists()
        export = Path(case["expected_export_path"])
        result["export_file_exists"] = export.is_file()
        if export.is_file():
            result["export_bytes"] = export.stat().st_size
            result["export_sha256"] = qa.sha256(export)
        try:
            identity = qa.verify_package(package, case["package"]["source_sha"])
            result["package_after_run"] = identity
            result["package_unchanged"] = identity == case["package"]
        except Exception:
            result["package_unchanged"] = False
        if not result["package_unchanged"] or result["runtime_config_exists"]:
            code = 1
        result["wrapper_exit_code"] = code
        result["acceptance_status"] = "requires unchanged analyzer and independent manual review"
        qa.write_json(evidence / "run-result.json", result)
    return code


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--package", type=Path, required=True)
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--outcome", choices=["save", "cancel", "quit"], required=True)
    parser.add_argument("--expected-source-sha", required=True)
    parser.add_argument("--seconds", type=float, default=240)
    parser.add_argument("--delay", type=float, default=-1)
    parser.add_argument("--width", type=int, default=1280)
    parser.add_argument("--height", type=int, default=800)
    parser.add_argument("--prepare-only", action="store_true")
    args = parser.parse_args()
    evidence = args.evidence.resolve()
    case = prepare(
        args.package,
        evidence,
        args.outcome,
        seconds=args.seconds,
        delay=args.delay,
        width=args.width,
        height=args.height,
        expected_sha=args.expected_source_sha,
    )
    if args.prepare_only:
        print("Explicit fixture latency case prepared; no process started.")
        return 0
    return run(case, evidence)


if __name__ == "__main__":
    raise SystemExit(main())
