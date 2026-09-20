"""Private per-user launcher for the portable U01 Windows desktop preview."""

from __future__ import annotations

import argparse
import contextlib
import ctypes
import datetime
import json
import os
import re
import secrets
import socket
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.request
from ctypes import wintypes
from pathlib import Path


class LaunchError(Exception):
    def __init__(self, code: str, message: str):
        super().__init__(message)
        self.code = code


def hidden_flags() -> int:
    return subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0


class _StartupInfo(ctypes.Structure):
    _fields_ = [
        ("cb", wintypes.DWORD),
        ("lpReserved", wintypes.LPWSTR),
        ("lpDesktop", wintypes.LPWSTR),
        ("lpTitle", wintypes.LPWSTR),
        *[
            (name, wintypes.DWORD)
            for name in (
                "dwX",
                "dwY",
                "dwXSize",
                "dwYSize",
                "dwXCountChars",
                "dwYCountChars",
                "dwFillAttribute",
                "dwFlags",
            )
        ],
        ("wShowWindow", wintypes.WORD),
        ("cbReserved2", wintypes.WORD),
        ("lpReserved2", ctypes.c_void_p),
        ("hStdInput", wintypes.HANDLE),
        ("hStdOutput", wintypes.HANDLE),
        ("hStdError", wintypes.HANDLE),
    ]


class _StartupInfoEx(ctypes.Structure):
    _fields_ = [("StartupInfo", _StartupInfo), ("lpAttributeList", ctypes.c_void_p)]


class _ProcessInfo(ctypes.Structure):
    _fields_ = [
        ("hProcess", wintypes.HANDLE),
        ("hThread", wintypes.HANDLE),
        ("dwProcessId", wintypes.DWORD),
        ("dwThreadId", wintypes.DWORD),
    ]


class _BasicJobLimits(ctypes.Structure):
    _fields_ = [
        ("PerProcessUserTimeLimit", ctypes.c_longlong),
        ("PerJobUserTimeLimit", ctypes.c_longlong),
        ("LimitFlags", wintypes.DWORD),
        ("MinimumWorkingSetSize", ctypes.c_size_t),
        ("MaximumWorkingSetSize", ctypes.c_size_t),
        ("ActiveProcessLimit", wintypes.DWORD),
        ("Affinity", ctypes.c_size_t),
        ("PriorityClass", wintypes.DWORD),
        ("SchedulingClass", wintypes.DWORD),
    ]


class _ExtendedJobLimits(ctypes.Structure):
    _fields_ = [
        ("BasicLimitInformation", _BasicJobLimits),
        ("IoInfo", ctypes.c_ulonglong * 6),
        ("ProcessMemoryLimit", ctypes.c_size_t),
        ("JobMemoryLimit", ctypes.c_size_t),
        ("PeakProcessMemoryUsed", ctypes.c_size_t),
        ("PeakJobMemoryUsed", ctypes.c_size_t),
    ]


def _supervision_error() -> LaunchError:
    return LaunchError(
        "process_supervision_unavailable",
        "无法建立 Windows 子进程托管，已停止启动。请使用 Windows 10 或更新系统并检查运行权限。",
    )


class _WindowsProcess:
    """Retain our own process handle; never look up or terminate a process by PID."""

    def __init__(self, kernel, handle, pid, command):
        self.kernel, self.handle, self.pid, self.args = kernel, handle, pid, command
        self.returncode = None

    def poll(self):
        if self.returncode is None:
            state = self.kernel.WaitForSingleObject(self.handle, 0)
            if state == 0:
                code = wintypes.DWORD()
                if not self.kernel.GetExitCodeProcess(self.handle, ctypes.byref(code)):
                    raise _supervision_error()
                self.returncode = code.value
            elif state != 258:  # WAIT_TIMEOUT
                raise _supervision_error()
        return self.returncode

    def wait(self, timeout=None):
        milliseconds = 0xFFFFFFFF if timeout is None else max(0, int(timeout * 1000))
        state = self.kernel.WaitForSingleObject(self.handle, milliseconds)
        if state == 258:
            raise subprocess.TimeoutExpired(self.args, timeout)
        if state != 0:
            raise _supervision_error()
        return self.poll()

    def terminate(self):
        if self.poll() is None and not self.kernel.TerminateProcess(self.handle, 1):
            if self.poll() is None:
                raise _supervision_error()

    kill = terminate

    def close(self):
        if self.handle:
            self.kernel.CloseHandle(self.handle)
            self.handle = None


class OwnedProcesses:
    """Windows 10+ atomic job assignment, with no unsupervised fallback.

    JOB_LIST associates the child during CreateProcess itself. Unlike creating a
    suspended child and assigning it afterward, even launcher termination in the
    creation interval cannot leave an unassigned child. The unnamed job handle is
    non-inheritable and held only here; Windows closes it on forced launcher exit.
    https://learn.microsoft.com/windows/win32/procthread/job-objects
    https://learn.microsoft.com/windows/win32/api/processthreadsapi/nf-processthreadsapi-updateprocthreadattribute
    """

    def __init__(self):
        self.children = []
        self.job = None
        if os.name != "nt":
            return
        try:
            self.kernel = ctypes.WinDLL("kernel32", use_last_error=True)
            signatures = {
                "CreateJobObjectW": ([ctypes.c_void_p, wintypes.LPCWSTR], wintypes.HANDLE),
                "SetInformationJobObject": (
                    [wintypes.HANDLE, ctypes.c_int, ctypes.c_void_p, wintypes.DWORD],
                    wintypes.BOOL,
                ),
                "CloseHandle": ([wintypes.HANDLE], wintypes.BOOL),
                "CreateFileW": (
                    [
                        wintypes.LPCWSTR,
                        wintypes.DWORD,
                        wintypes.DWORD,
                        ctypes.c_void_p,
                        wintypes.DWORD,
                        wintypes.DWORD,
                        wintypes.HANDLE,
                    ],
                    wintypes.HANDLE,
                ),
                "SetHandleInformation": (
                    [wintypes.HANDLE, wintypes.DWORD, wintypes.DWORD],
                    wintypes.BOOL,
                ),
                "InitializeProcThreadAttributeList": (
                    [
                        ctypes.c_void_p,
                        wintypes.DWORD,
                        wintypes.DWORD,
                        ctypes.POINTER(ctypes.c_size_t),
                    ],
                    wintypes.BOOL,
                ),
                "UpdateProcThreadAttribute": (
                    [
                        ctypes.c_void_p,
                        wintypes.DWORD,
                        ctypes.c_size_t,
                        ctypes.c_void_p,
                        ctypes.c_size_t,
                        ctypes.c_void_p,
                        ctypes.c_void_p,
                    ],
                    wintypes.BOOL,
                ),
                "DeleteProcThreadAttributeList": ([ctypes.c_void_p], None),
                "CreateProcessW": (
                    [
                        wintypes.LPCWSTR,
                        wintypes.LPWSTR,
                        ctypes.c_void_p,
                        ctypes.c_void_p,
                        wintypes.BOOL,
                        wintypes.DWORD,
                        ctypes.c_void_p,
                        wintypes.LPCWSTR,
                        ctypes.POINTER(_StartupInfoEx),
                        ctypes.POINTER(_ProcessInfo),
                    ],
                    wintypes.BOOL,
                ),
                "WaitForSingleObject": ([wintypes.HANDLE, wintypes.DWORD], wintypes.DWORD),
                "GetExitCodeProcess": (
                    [wintypes.HANDLE, ctypes.POINTER(wintypes.DWORD)],
                    wintypes.BOOL,
                ),
                "TerminateProcess": ([wintypes.HANDLE, wintypes.UINT], wintypes.BOOL),
            }
            for name, (arguments, result) in signatures.items():
                function = getattr(self.kernel, name)
                function.argtypes, function.restype = arguments, result
            self.job = self.kernel.CreateJobObjectW(None, None)
            if not self.job:
                raise _supervision_error()
            limits = _ExtendedJobLimits()
            limits.BasicLimitInformation.LimitFlags = 0x00002000  # KILL_ON_JOB_CLOSE
            if not self.kernel.SetInformationJobObject(
                self.job, 9, ctypes.byref(limits), ctypes.sizeof(limits)
            ):
                raise _supervision_error()
        except Exception:
            if self.job:
                self.kernel.CloseHandle(self.job)
                self.job = None
            raise _supervision_error() from None

    def __enter__(self):
        return self

    def start(self, command: list[str], *, cwd: Path, env: dict[str, str]):
        if os.name != "nt":
            child = subprocess.Popen(
                command,
                cwd=cwd,
                env=env,
                stdin=subprocess.DEVNULL,
                stdout=subprocess.DEVNULL,
                stderr=subprocess.DEVNULL,
            )
            self.children.append(child)
            return child
        size = ctypes.c_size_t()
        # The first call intentionally returns ERROR_INSUFFICIENT_BUFFER.
        self.kernel.InitializeProcThreadAttributeList(None, 2, 0, ctypes.byref(size))
        if not size.value:
            raise _supervision_error()
        attributes = ctypes.create_string_buffer(size.value)
        if not self.kernel.InitializeProcThreadAttributeList(attributes, 2, 0, ctypes.byref(size)):
            raise _supervision_error()
        null_handle = None
        try:
            jobs = (wintypes.HANDLE * 1)(self.job)
            if not self.kernel.UpdateProcThreadAttribute(
                attributes, 0, 0x0002000D, jobs, ctypes.sizeof(jobs), None, None
            ):
                raise _supervision_error()
            # Only this NUL handle is inherited. The job handle must never escape.
            null_handle = self.kernel.CreateFileW("NUL", 0xC0000000, 3, None, 3, 0, None)
            if null_handle == ctypes.c_void_p(-1).value:
                null_handle = None
                raise _supervision_error()
            if not self.kernel.SetHandleInformation(null_handle, 1, 1):
                raise _supervision_error()
            inherited = (wintypes.HANDLE * 1)(null_handle)
            if not self.kernel.UpdateProcThreadAttribute(
                attributes, 0, 0x00020002, inherited, ctypes.sizeof(inherited), None, None
            ):
                raise _supervision_error()
            startup = _StartupInfoEx()
            startup.StartupInfo.cb = ctypes.sizeof(startup)
            startup.StartupInfo.dwFlags = 0x00000100  # STARTF_USESTDHANDLES
            startup.StartupInfo.hStdInput = null_handle
            startup.StartupInfo.hStdOutput = null_handle
            startup.StartupInfo.hStdError = null_handle
            startup.lpAttributeList = ctypes.cast(attributes, ctypes.c_void_p)
            information = _ProcessInfo()
            command_line = ctypes.create_unicode_buffer(subprocess.list2cmdline(command))
            environment = ctypes.create_unicode_buffer(
                "\0".join(
                    f"{key}={value}"
                    for key, value in sorted(env.items(), key=lambda item: item[0].upper())
                )
                + "\0"
            )
            flags = (
                0x08000000 | 0x00000400 | 0x00080000
            )  # NO_WINDOW | UNICODE_ENVIRONMENT | EXTENDED_STARTUPINFO_PRESENT
            if not self.kernel.CreateProcessW(
                command[0],
                command_line,
                None,
                None,
                True,
                flags,
                environment,
                str(cwd),
                ctypes.byref(startup),
                ctypes.byref(information),
            ):
                raise _supervision_error()
            self.kernel.CloseHandle(information.hThread)
            child = _WindowsProcess(
                self.kernel, information.hProcess, information.dwProcessId, command
            )
            self.children.append(child)
            return child
        finally:
            self.kernel.DeleteProcThreadAttributeList(attributes)
            if null_handle:
                self.kernel.CloseHandle(null_handle)

    def __exit__(self, *_):
        # Closing the job first also catches descendants or a failed direct-child cleanup.
        if self.job:
            self.kernel.CloseHandle(self.job)
            self.job = None
        for child in self.children:
            try:
                stop_owned(child)
            finally:
                if isinstance(child, _WindowsProcess):
                    child.close()


def private_directory(path: Path) -> None:
    """Set an exact protected DACL before creating any token or private log."""
    path.mkdir(parents=True, exist_ok=True)
    if os.name != "nt":
        path.chmod(0o700)
        return
    system = Path(os.environ.get("SystemRoot", r"C:\Windows")) / "System32"
    result = subprocess.run(
        [str(system / "whoami.exe"), "/user", "/fo", "csv", "/nh"],
        capture_output=True,
        creationflags=hidden_flags(),
        check=True,
    )
    match = re.search(rb"S-1-\d+(?:-\d+)+", result.stdout)
    if not match:
        raise LaunchError("private_permissions", "无法识别当前 Windows 用户。")
    sid = match.group().decode("ascii")
    advapi = ctypes.WinDLL("advapi32", use_last_error=True)
    kernel = ctypes.WinDLL("kernel32", use_last_error=True)
    descriptor = ctypes.c_void_p()
    convert = advapi.ConvertStringSecurityDescriptorToSecurityDescriptorW
    convert.argtypes = [
        ctypes.c_wchar_p,
        ctypes.c_uint32,
        ctypes.POINTER(ctypes.c_void_p),
        ctypes.c_void_p,
    ]
    convert.restype = ctypes.c_int
    set_security = advapi.SetFileSecurityW
    set_security.argtypes = [ctypes.c_wchar_p, ctypes.c_uint32, ctypes.c_void_p]
    set_security.restype = ctypes.c_int
    kernel.LocalFree.argtypes = [ctypes.c_void_p]
    kernel.LocalFree.restype = ctypes.c_void_p
    if not convert(f"D:P(A;OICI;FA;;;{sid})", 1, ctypes.byref(descriptor), None):
        raise LaunchError("private_permissions", "无法准备本机配置权限。")
    try:
        if not set_security(str(path), 0x00000004 | 0x80000000, descriptor):
            raise LaunchError("private_permissions", "无法限制本机配置访问权限。")
    finally:
        kernel.LocalFree(descriptor)


@contextlib.contextmanager
def startup_lock(directory: Path):
    """An OS file lock survives neither crashes nor process termination."""
    with (directory / "launcher.lock").open("a+b") as handle:
        if handle.tell() == 0:
            handle.write(b"0")
            handle.flush()
        handle.seek(0)
        try:
            if os.name == "nt":
                import msvcrt

                msvcrt.locking(handle.fileno(), msvcrt.LK_NBLCK, 1)
            else:
                import fcntl

                fcntl.flock(handle.fileno(), fcntl.LOCK_EX | fcntl.LOCK_NB)
        except OSError:
            raise LaunchError("already_running", "桌面预览已经启动，请查看现有窗口。") from None
        try:
            yield
        finally:
            handle.seek(0)
            if os.name == "nt":
                msvcrt.locking(handle.fileno(), msvcrt.LK_UNLCK, 1)
            else:
                fcntl.flock(handle.fileno(), fcntl.LOCK_UN)


def ensure_port_free(port: int = 8000) -> None:
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as probe:
        if os.name == "nt":
            probe.setsockopt(socket.SOL_SOCKET, socket.SO_EXCLUSIVEADDRUSE, 1)
        try:
            probe.bind(("127.0.0.1", port))
        except OSError:
            raise LaunchError(
                "port_in_use",
                "本机 8000 端口正在使用。请关闭占用该端口的服务后重试；启动器不会结束其他程序。",
            ) from None


def write_private_config(directory: Path) -> tuple[Path, str]:
    token = secrets.token_urlsafe(32)
    path = directory / "config.json"
    descriptor, name = tempfile.mkstemp(prefix="config-", suffix=".pending", dir=directory)
    temporary = Path(name)
    try:
        with os.fdopen(descriptor, "w", encoding="utf-8") as output:
            json.dump(
                {
                    "protocol": "unity-preview/1",
                    "base_url": "http://127.0.0.1:8000",
                    "token": token,
                },
                output,
            )
        if os.name != "nt":
            temporary.chmod(0o600)
        temporary.replace(path)
    finally:
        temporary.unlink(missing_ok=True)
    return path, token


def log_event(directory: Path, event: str) -> None:
    """Only fixed event codes are logged; exceptions, tokens and responses are not."""
    path = directory / "launcher.log"
    if path.exists() and path.stat().st_size > 131072:
        path.replace(directory / "launcher.previous.log")
    with path.open("a", encoding="utf-8") as output:
        output.write(f"{datetime.datetime.now(datetime.UTC).isoformat()} {event}\n")


def stop_owned(process: subprocess.Popen | _WindowsProcess | None) -> None:
    if process is not None and process.poll() is None:
        process.terminate()
        try:
            process.wait(timeout=5)
        except subprocess.TimeoutExpired:
            process.kill()
            process.wait(timeout=5)


def wait_ready(
    process: subprocess.Popen | _WindowsProcess, token: str, port: int, timeout: float
) -> None:
    # Environment proxy settings must not route the local token off this computer.
    class NoRedirect(urllib.request.HTTPRedirectHandler):
        def redirect_request(self, request, fp, code, message, headers, newurl):
            return None

    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}), NoRedirect())
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        if process.poll() is not None:
            raise LaunchError("backend_exit", "本机预览服务未能启动，请检查包是否完整。")
        request = urllib.request.Request(
            f"http://127.0.0.1:{port}/preview/unity/v1/capabilities",
            headers={"Authorization": "Bearer " + token},
        )
        try:
            with opener.open(request, timeout=0.5) as response:
                if response.status != 200:
                    raise ValueError("Unexpected status")
                data = response.read(65537)
                if len(data) > 65536:
                    raise ValueError("Response too large")
                capability = json.loads(data)
                if (
                    capability.get("protocol") == "unity-preview/1"
                    and capability.get("chat_mode") == "fixture"
                    and capability.get("chat", {}).get("available") is True
                ):
                    return
        except (OSError, ValueError, urllib.error.URLError):
            time.sleep(0.05)
    raise LaunchError("backend_timeout", "本机预览服务启动超时，请重新启动。")


def run_desktop(
    package: Path,
    runtime: Path,
    *,
    backend_command: list[str] | None = None,
    player_command: list[str] | None = None,
    port: int = 8000,
    readiness_timeout: float = 15,
) -> int:
    """Command overrides are only for controlled tests; not exposed by the package UI."""
    package, runtime = package.resolve(), runtime.resolve()
    if (
        backend_command is None
        and not (package / "backend/app/unity_preview/__main__.py").is_file()
    ):
        raise LaunchError("incomplete_package", "后台文件缺失，请重新完整解压桌面预览包。")
    if player_command is None and not (package / "NeuroSaki.exe").is_file():
        raise LaunchError("incomplete_package", "桌面程序缺失，请重新完整解压桌面预览包。")
    private_directory(runtime)
    with startup_lock(runtime), OwnedProcesses() as owned:
        ensure_port_free(port)
        config, token = write_private_config(runtime)
        backend = player = None
        try:
            environment = dict(os.environ)
            environment.pop("PYTHONHOME", None)
            environment["PYTHONPATH"] = str(package / "backend")
            environment["PYTHONNOUSERSITE"] = "1"
            environment["PYTHONDONTWRITEBYTECODE"] = "1"
            environment["U01_PREVIEW_CONFIG"] = str(config)
            environment["U01_PREVIEW_MODE"] = "fixture"
            environment["U01_PREVIEW_SCENARIO"] = "normal"
            backend = owned.start(
                backend_command or [sys.executable, "-B", "-m", "app.unity_preview"],
                cwd=package,
                env=environment,
            )
            log_event(runtime, "backend_started")
            wait_ready(backend, token, port, readiness_timeout)
            player = owned.start(
                player_command
                or [
                    str(package / "NeuroSaki.exe"),
                    "-screen-fullscreen",
                    "0",
                    "-screen-width",
                    "1280",
                    "-screen-height",
                    "800",
                ],
                cwd=package,
                env=environment,
            )
            log_event(runtime, "player_started")
            while player.poll() is None:
                if backend.poll() is not None:
                    raise LaunchError("backend_lost", "本机预览服务意外停止，请重新启动桌面预览。")
                time.sleep(0.2)
            code = player.returncode
            if code:
                raise LaunchError("player_exit", "桌面程序意外退出，请重新启动或查看运行报告。")
            log_event(runtime, "player_closed")
            return code
        finally:
            try:
                stop_owned(player)
            finally:
                try:
                    stop_owned(backend)
                finally:
                    config.unlink(missing_ok=True)
                    log_event(runtime, "owned_processes_stopped")


def main() -> int:
    parser = argparse.ArgumentParser(description="Start the portable desktop preview")
    parser.add_argument("--runtime-directory", type=Path)
    arguments = parser.parse_args()
    package = Path(__file__).resolve().parent
    runtime = (
        arguments.runtime_directory
        or Path(os.environ.get("LOCALAPPDATA", str(Path.home()))) / "NeuroSaki/preview-runtime"
    )
    try:
        return run_desktop(package, runtime)
    except Exception as error:
        code = error.code if isinstance(error, LaunchError) else "launcher_failure"
        message = (
            str(error)
            if isinstance(error, LaunchError)
            else "桌面预览启动失败，请检查解压目录和运行权限。"
        )
        try:
            private_directory(runtime)
            log_event(runtime, code)
        except Exception:
            pass
        message += f"\n\n运行记录：{runtime / 'launcher.log'}"
        if os.name == "nt":
            ctypes.windll.user32.MessageBoxW(None, message, "Neuro-Saki 桌面预览", 0x10)
        else:
            print(message, file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
