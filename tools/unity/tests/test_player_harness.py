"""Windows integration tests for Test-Player.ps1; no Unity/graphics acceptance claim.

Run explicitly with Python 3.12 and PowerShell 7 on PATH. A .NET Framework compiler
builds a controlled executable in ignored .tmp. Synthetic pictures stay there.
Only the test summary is exported with --report; never use it as Player evidence.
"""

import argparse
import ctypes as ct
import hashlib
import json
import os
import shutil
import subprocess
import time
import uuid
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
SOURCE = Path(__file__).with_name("ControlledPlayer.cs")
SCRIPT = ROOT / "tools/unity/Test-Player.ps1"


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def require(condition, message):
    if not condition:
        raise AssertionError(message)


class OwnedProcess:
    """Hold a Windows handle so cleanup cannot target a reused numeric PID."""

    def __init__(self, pid):
        self.kernel = ct.WinDLL("kernel32", use_last_error=True)
        self.kernel.OpenProcess.argtypes = [ct.c_ulong, ct.c_int, ct.c_ulong]
        self.kernel.OpenProcess.restype = ct.c_void_p
        self.kernel.WaitForSingleObject.argtypes = [ct.c_void_p, ct.c_ulong]
        self.kernel.TerminateProcess.argtypes = [ct.c_void_p, ct.c_uint]
        self.kernel.CloseHandle.argtypes = [ct.c_void_p]
        self.handle = self.kernel.OpenProcess(0x100001, False, pid)  # SYNCHRONIZE | TERMINATE
        require(self.handle, f"Could not retain fixture process handle: {ct.get_last_error()}")

    def exited(self):
        result = self.kernel.WaitForSingleObject(self.handle, 0)
        require(result in (0, 258), "Process status query failed")
        return result == 0

    def close(self):
        if not self.exited():
            require(self.kernel.TerminateProcess(self.handle, 137), "Fixture cleanup failed")
            require(self.kernel.WaitForSingleObject(self.handle, 5000) == 0, "Cleanup timed out")
        self.kernel.CloseHandle(self.handle)


def verify_hashes(evidence):
    hashes = json.loads((evidence / "evidence-hashes.json").read_text(encoding="utf-8-sig"))
    expected = {path.name for path in evidence.iterdir() if path.name != "evidence-hashes.json"}
    require({item["file"] for item in hashes} == expected, "Evidence hash inventory differs")
    for item in hashes:
        require(digest(evidence / item["file"]) == item["Hash"].lower(), "Evidence hash mismatch")


def run_case(mode, binary, work, pwsh, sentinel=None):
    evidence = work / mode
    env = dict(os.environ, COMPANION_FIXTURE_MODE=mode)
    command = [
        pwsh,
        "-NoProfile",
        "-File",
        str(SCRIPT),
        "-Player",
        str(binary),
        "-EvidenceDirectory",
        str(evidence),
        "-Seconds",
        "15",
        "-GraceSeconds",
        "1",
    ]
    started = time.monotonic()
    child_handle = None
    with (work / (mode + "-output.txt")).open("w", encoding="utf-8") as output:
        wrapper = subprocess.Popen(
            command,
            env=env,
            stdout=output,
            stderr=subprocess.STDOUT,
            creationflags=subprocess.CREATE_NO_WINDOW,
        )
        try:
            if mode == "hang":
                pid_file = evidence / "fixture.pid"
                deadline = started + 10
                while not pid_file.exists() and time.monotonic() < deadline:
                    require(wrapper.poll() is None, "Wrapper failed before fixture started")
                    time.sleep(0.05)
                require(pid_file.exists(), "Fixture startup deadline exceeded")
                # The file may be observed between creation and completion of WriteAllText.
                while not pid_file.read_text().strip() and time.monotonic() < deadline:
                    time.sleep(0.01)
                child_handle = OwnedProcess(int(pid_file.read_text()))
            code = wrapper.wait(timeout=max(1, 40 - (time.monotonic() - started)))
            wall_seconds = round(time.monotonic() - started, 3)
            runtime_path = evidence / "runtime-manifest.json"
            runtime = json.loads(runtime_path.read_text(encoding="utf-8-sig"))
            require(runtime["processExited"], "Launched process remains alive")
            require(
                runtime["requestedSeconds"] == 15 and runtime["graceSeconds"] == 1,
                "Deadline inputs missing",
            )
            require(runtime["timeoutSeconds"] == 16, "Incorrect external deadline")
            require(runtime["playerSha256"] == digest(binary), "Player hash changed")
            require(
                runtime["files"]
                == [
                    {"path": binary.name, "bytes": binary.stat().st_size, "sha256": digest(binary)}
                ],
                "Player inventory incorrect",
            )
            verify_hashes(evidence)
            if mode == "hang":
                require(code == 124 and runtime["status"] == "timeout", "Hang did not time out")
                require(runtime["timedOut"] and runtime["terminationAttempted"], "No termination")
                require(runtime["elapsedWallSeconds"] >= 15.5, "Deadline fired early")
                require(child_handle.exited(), "Timed-out fixture survived")
                require(sentinel.poll() is None, "Unrelated same-image sentinel was killed")
                require(not (evidence / "player-result.json").exists(), "Fabricated output")
            elif mode == "normal":
                require(code == 0 and runtime["status"] == "passed", "Normal case failed")
                require(
                    not runtime["timedOut"] and not runtime["terminationAttempted"],
                    "Normal process was terminated",
                )
                require(runtime["elapsedWallSeconds"] >= 15, "Normal fixture ended early")
            else:
                require(code == 1 and runtime["status"] == "failed", "Bad output was accepted")
                expected = "Missing runtime evidence" if mode == "missing-image" else "probe failed"
                require(expected in runtime["failureReason"], "Wrong output failure")
            # Portable summary; raw manifests/logs remain in the ignored test directory.
            runtime.pop("player")
            runtime.pop("files")
            return {
                "mode": mode,
                "status": "passed",
                "wrapper_exit_code": code,
                "outer_wall_seconds": wall_seconds,
                "runtime": runtime,
                "runtime_manifest_sha256": digest(runtime_path),
                "all_evidence_hashes_valid": True,
                "retained_log": (evidence / "player.log").read_text(),
                "same_image_sentinel_survived": True if mode == "hang" else None,
            }
        finally:
            if wrapper.poll() is None:
                wrapper.kill()
                wrapper.wait(timeout=5)
            if child_handle:
                child_handle.close()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--report", type=Path, required=True)
    args = parser.parse_args()
    require(os.name == "nt", "This integration fixture requires Windows")
    source_commit = subprocess.check_output(
        ["git", "rev-parse", "HEAD"], cwd=ROOT, text=True
    ).strip()
    source_dirty = bool(
        subprocess.check_output(["git", "status", "--porcelain"], cwd=ROOT, text=True).strip()
    )
    pwsh = shutil.which("pwsh")
    require(pwsh, "PowerShell 7 must be on PATH")
    compiler = Path(os.environ["WINDIR"]) / "Microsoft.NET/Framework64/v4.0.30319/csc.exe"
    require(compiler.is_file(), ".NET Framework x64 C# compiler is required")
    work = ROOT / ".tmp" / ("player-harness-" + uuid.uuid4().hex)
    binaries = work / "bin"
    binaries.mkdir(parents=True)
    binary = binaries / "ControlledPlayer.exe"
    subprocess.run(
        [str(compiler), "/nologo", "/target:exe", "/out:" + str(binary), str(SOURCE)],
        check=True,
        timeout=30,
        creationflags=subprocess.CREATE_NO_WINDOW,
    )
    sentinel_evidence = work / "sentinel"
    sentinel = subprocess.Popen(
        [
            str(binary),
            "-evidenceDirectory",
            str(sentinel_evidence),
            "-smokeSeconds",
            "15",
            "-logFile",
            str(sentinel_evidence / "player.log"),
        ],
        env=dict(os.environ, COMPANION_FIXTURE_MODE="hang"),
        creationflags=subprocess.CREATE_NO_WINDOW,
    )
    try:
        cases = [
            run_case(mode, binary, work, pwsh, sentinel)
            for mode in ("normal", "hang", "missing-image", "invalid-result")
        ]
    finally:
        if sentinel.poll() is None:
            sentinel.kill()
        sentinel.wait(timeout=5)
    report = {
        "scope": "harness_only_not_unity_player",
        "status": "passed",
        "tested_at_utc": datetime.now(timezone.utc).isoformat(),
        "tested_commit": None if source_dirty else source_commit,
        "source_base_commit": source_commit,
        "source_dirty_at_start": source_dirty,
        "script_sha256": digest(SCRIPT),
        "fixture_source_sha256": digest(SOURCE),
        "harness_source_sha256": digest(Path(__file__)),
        "fixture_binary_sha256": digest(binary),
        "requested_seconds": 15,
        "grace_seconds": 1,
        "outer_safety_timeout_seconds": 40,
        "compiler": "Windows .NET Framework64 v4.0.30319 csc.exe",
        "cases": cases,
        "limitations": "Synthetic fixture outputs test process supervision and evidence validation; "
        "no Unity import, graphics, build, UA01 or UA02 acceptance evidence.",
    }
    args.report.parent.mkdir(parents=True, exist_ok=True)
    args.report.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(f"All four controlled-process cases passed. Summary: {args.report}")
    print(f"Raw fixture-only evidence: {work}")


if __name__ == "__main__":
    main()
