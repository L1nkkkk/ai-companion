"""Controlled process and dependency tests; these do not pretend to execute Unity."""

import importlib.util
import json
import os
import re
import socket
import subprocess
import sys
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
