"""Controlled process and dependency tests; these do not pretend to execute Unity."""

import ctypes
import importlib.util
import json
import os
import re
import socket
import subprocess
import sys
import time
from ctypes import wintypes
from pathlib import Path

import pytest

REPOSITORY = Path(__file__).resolve().parents[4]


def module(name):
    spec = importlib.util.spec_from_file_location(name, REPOSITORY / "tools/unity" / (name + ".py"))
    loaded = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(loaded)
    return loaded


launch = module("launch_desktop")
package = module("package_desktop")


def free_port():
    with socket.socket() as connection:
        connection.bind(("127.0.0.1", 0))
        return connection.getsockname()[1]


def test_runtime_config_is_private_rotated_url_safe_and_no_secret_is_logged(tmp_path):
    launch.private_directory(tmp_path)
    path, first = launch.write_private_config(tmp_path)
    _, second = launch.write_private_config(tmp_path)
    assert first != second
    assert re.fullmatch(r"[A-Za-z0-9_-]{43}", second)
    assert json.loads(path.read_text())["token"] == second
    launch.log_event(tmp_path, "test_event")
    assert first not in (tmp_path / "launcher.log").read_text()
    assert second not in (tmp_path / "launcher.log").read_text()
    assert list(tmp_path.glob("*.pending")) == []


def test_os_lock_prevents_duplicate_start_and_releases(tmp_path):
    with launch.startup_lock(tmp_path):
        with pytest.raises(launch.LaunchError) as error:
            with launch.startup_lock(tmp_path):
                pytest.fail("Two launchers acquired the same lock")
        assert error.value.code == "already_running"
    with launch.startup_lock(tmp_path):
        pass


def test_busy_port_is_reported_without_ending_owner():
    with socket.socket() as listener:
        listener.bind(("127.0.0.1", 0))
        listener.listen(1)
        port = listener.getsockname()[1]
        with pytest.raises(launch.LaunchError) as error:
            launch.ensure_port_free(port)
        assert error.value.code == "port_in_use"
        with socket.create_connection(("127.0.0.1", port), timeout=1):
            pass


def test_missing_player_is_a_reviewable_error(tmp_path):
    with pytest.raises(launch.LaunchError) as error:
        launch.run_desktop(tmp_path, tmp_path / "runtime")
    assert error.value.code == "incomplete_package"


def test_controlled_player_exit_stops_only_owned_backend_and_removes_token(tmp_path):
    runtime = tmp_path / "runtime"
    port = free_port()
    backend_script = tmp_path / "controlled_backend.py"
    backend_script.write_text(
        """import http.server, json, os
from pathlib import Path
Path("backend.pid").write_text(str(os.getpid()))
config = json.loads(Path(os.environ["U01_PREVIEW_CONFIG"]).read_text())
class Handler(http.server.BaseHTTPRequestHandler):
    def do_GET(self):
        if self.headers.get("Authorization") != "Bearer " + config["token"]:
            self.send_error(401); return
        data = b'{"protocol":"unity-preview/1","chat_mode":"fixture","chat":{"available":true}}'
        self.send_response(200)
        self.send_header("Content-Length", str(len(data)))
        self.end_headers(); self.wfile.write(data)
    def log_message(self, *args): pass
http.server.HTTPServer(("127.0.0.1", PORT), Handler).serve_forever()
""".replace("PORT", str(port)),
        encoding="utf-8",
    )
    unrelated = subprocess.Popen(
        [sys.executable, "-c", "import time; time.sleep(15)"], creationflags=launch.hidden_flags()
    )
    try:
        result = launch.run_desktop(
            tmp_path,
            runtime,
            backend_command=[sys.executable, "-B", str(backend_script)],
            player_command=[sys.executable, "-c", "import time; time.sleep(0.2)"],
            port=port,
        )
        assert result == 0
        assert unrelated.poll() is None
        assert not (runtime / "config.json").exists()
        assert "owned_processes_stopped" in (runtime / "launcher.log").read_text()
        launch.ensure_port_free(port)
    finally:
        launch.stop_owned(unrelated)


def test_backend_startup_failure_cleans_token_and_lock(tmp_path):
    runtime = tmp_path / "runtime"
    with pytest.raises(launch.LaunchError) as error:
        launch.run_desktop(
            tmp_path,
            runtime,
            backend_command=[sys.executable, "-c", "raise SystemExit(7)"],
            player_command=[sys.executable, "-c", "raise SystemExit(0)"],
            port=free_port(),
            readiness_timeout=2,
        )
    assert error.value.code == "backend_exit"
    assert not (runtime / "config.json").exists()
    with launch.startup_lock(runtime):
        pass


def test_dependency_closure_excludes_development_packages_and_retains_runtime_licenses(tmp_path):
    site = REPOSITORY / ".venv/Lib/site-packages"
    if not site.is_dir():
        pytest.skip("Portable Windows packager test needs the frozen Windows environment")
    dependencies = package.runtime_dependencies(site)
    names = {distribution.metadata["Name"].lower() for distribution in dependencies}
    assert {
        "fastapi",
        "uvicorn",
        "pydantic",
        "pydantic_core",
        "starlette",
        "anyio",
        "click",
        "h11",
    } <= names
    assert not {"pytest", "httpx", "ruff", "packaging", "jsonschema"} & names
    for distribution in dependencies:
        package.copy_distribution(distribution, site, tmp_path)
    assert list(tmp_path.glob("fastapi-*.dist-info/licenses/LICENSE"))
    assert list(tmp_path.glob("uvicorn-*.dist-info/licenses/LICENSE.md"))
    assert list(tmp_path.glob("pydantic_core/*.pyd"))
    assert not list(tmp_path.rglob("__pycache__"))


def test_packager_excludes_cache_and_runtime_configuration(tmp_path):
    source = tmp_path / "source"
    source.mkdir()
    for name in ("config.json", "launcher.lock", "launcher.log", "stale.pyc", "keep.txt"):
        (source / name).write_text("fixture")
    (source / "__pycache__").mkdir()
    (source / "__pycache__/cache.pyc").write_bytes(b"x")
    destination = tmp_path / "destination"
    package.copy_tree(source, destination)
    assert [path.name for path in destination.iterdir()] == ["keep.txt"]


@pytest.mark.skipif(os.name != "nt", reason="Windows protected DACL verification")
def test_token_file_inherits_only_current_user_acl(tmp_path):
    launch.private_directory(tmp_path)
    config, _ = launch.write_private_config(tmp_path)
    acl = tmp_path / "acl-evidence.txt"
    result = subprocess.run(
        [
            str(Path(os.environ["SystemRoot"]) / "System32/icacls.exe"),
            str(config),
            "/save",
            str(acl),
            "/Q",
        ],
        capture_output=True,
        creationflags=launch.hidden_flags(),
    )
    assert result.returncode == 0
    sddl = acl.read_text(encoding="utf-16-le")
    assert sddl.count("(A;") == 1
    assert "S-1-" in sddl
    assert ";;;WD" not in sddl and ";;;BU" not in sddl and ";;;AU" not in sddl


@pytest.mark.skipif(os.name != "nt", reason="Windows native job lifetime")
@pytest.mark.parametrize("child_count", [1, 2])
def test_forced_launcher_exit_reaps_owned_tree_without_ending_other_processes(
    tmp_path, child_count
):
    """Use real dummy processes and retained handles, never Unity or a PID-wide kill."""
    port = free_port()
    dummy = tmp_path / "dummy.py"
    dummy.write_text(
        """import os, socket, subprocess, sys, time
from pathlib import Path
name = sys.argv[1]
listener = None
if name == "backend":
    listener = socket.socket()
    listener.bind(("127.0.0.1", int(sys.argv[2])))
    listener.listen(1)
    subprocess.Popen([sys.executable, __file__, "grandchild"], creationflags=0x08000000)
Path(name + ".pid").write_text(str(os.getpid()))
time.sleep(60)
""",
        encoding="utf-8",
    )
    helper = tmp_path / "launcher_helper.py"
    helper.write_text(
        f"""import os, sys, time
from pathlib import Path
sys.path.insert(0, {str(REPOSITORY / "tools/unity")!r})
from launch_desktop import OwnedProcesses
with OwnedProcesses() as owned:
    owned.start([sys.executable, {str(dummy)!r}, "backend", {str(port)!r}], cwd=Path.cwd(), env=dict(os.environ))
    if {child_count} == 2:
        owned.start([sys.executable, {str(dummy)!r}, "player"], cwd=Path.cwd(), env=dict(os.environ))
    Path("launcher.pid").write_text(str(os.getpid()))
    time.sleep(60)
""",
        encoding="utf-8",
    )
    # Use the base interpreter directly, avoiding the venv redirector as the
    # process under test. TerminateProcess must target the launcher, not its wrapper.
    interpreter = getattr(sys, "_base_executable", sys.executable)
    launcher = subprocess.Popen(
        [interpreter, str(helper)], cwd=tmp_path, creationflags=launch.hidden_flags()
    )
    unrelated = subprocess.Popen(
        [interpreter, "-c", "import time; time.sleep(60)"], creationflags=launch.hidden_flags()
    )
    kernel = ctypes.WinDLL("kernel32", use_last_error=True)
    kernel.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
    kernel.OpenProcess.restype = wintypes.HANDLE
    kernel.WaitForSingleObject.argtypes = [wintypes.HANDLE, wintypes.DWORD]
    kernel.WaitForSingleObject.restype = wintypes.DWORD
    kernel.TerminateProcess.argtypes = [wintypes.HANDLE, wintypes.UINT]
    kernel.TerminateProcess.restype = wintypes.BOOL
    kernel.CloseHandle.argtypes = [wintypes.HANDLE]
    kernel.CloseHandle.restype = wintypes.BOOL
    handles = []
    names = ["backend", "grandchild"] + (["player"] if child_count == 2 else [])
    try:
        deadline = time.monotonic() + 10
        while not all((tmp_path / (name + ".pid")).is_file() for name in names + ["launcher"]):
            assert launcher.poll() is None, (
                "Controlled launcher exited before creating its children"
            )
            assert time.monotonic() < deadline, "Controlled children did not become ready"
            time.sleep(0.025)
        assert int((tmp_path / "launcher.pid").read_text()) == launcher.pid
        for name in names:
            pid = int((tmp_path / (name + ".pid")).read_text())
            handle = kernel.OpenProcess(0x00100000 | 0x0001, False, pid)  # SYNCHRONIZE | TERMINATE
            assert handle
            handles.append(handle)
            assert kernel.WaitForSingleObject(handle, 0) == 258
        with socket.create_connection(("127.0.0.1", port), timeout=1):
            pass
        launcher.kill()  # No Python finally runs in the tested launcher.
        launcher.wait(timeout=5)
        for handle in handles:
            assert kernel.WaitForSingleObject(handle, 5000) == 0, (
                "Owned process survived launcher termination"
            )
        assert unrelated.poll() is None
        launch.ensure_port_free(port)
    finally:
        launch.stop_owned(launcher)
        launch.stop_owned(unrelated)
        for handle in handles:
            if kernel.WaitForSingleObject(handle, 0) == 258:
                kernel.TerminateProcess(handle, 1)
                kernel.WaitForSingleObject(handle, 5000)
            kernel.CloseHandle(handle)


@pytest.mark.skipif(os.name != "nt", reason="Windows fail-closed job setup")
@pytest.mark.parametrize("failed_function", ["UpdateProcThreadAttribute", "CreateProcessW"])
def test_job_attribute_or_process_creation_failure_does_not_run_unsupervised(
    tmp_path, monkeypatch, failed_function
):
    marker = tmp_path / "must-not-run.txt"
    command = [sys.executable, "-c", f"from pathlib import Path; Path({str(marker)!r}).touch()"]
    with launch.OwnedProcesses() as owned:
        monkeypatch.setattr(owned.kernel, failed_function, lambda *arguments: 0)
        with pytest.raises(launch.LaunchError) as error:
            owned.start(command, cwd=tmp_path, env=dict(os.environ))
        assert error.value.code == "process_supervision_unavailable"
        assert owned.children == []
    assert not marker.exists()


@pytest.mark.skipif(os.name != "nt", reason="Windows fail-closed job limits")
def test_job_limit_configuration_failure_closes_job_before_any_child(tmp_path, monkeypatch):
    kernel = ctypes.WinDLL("kernel32", use_last_error=True)
    close = kernel.CloseHandle
    close.argtypes = [wintypes.HANDLE]
    close.restype = wintypes.BOOL
    closed = []

    def track_close(handle):
        closed.append(handle)
        return close(handle)

    monkeypatch.setattr(kernel, "CloseHandle", track_close)
    monkeypatch.setattr(kernel, "SetInformationJobObject", lambda *arguments: 0)
    monkeypatch.setattr(launch.ctypes, "WinDLL", lambda *arguments, **keywords: kernel)
    with pytest.raises(launch.LaunchError) as error:
        launch.OwnedProcesses()
    assert error.value.code == "process_supervision_unavailable"
    assert len(closed) == 1
