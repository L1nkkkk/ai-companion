"""Windows system speech; text is passed as JSON stdin, never executable code."""

import asyncio
import base64
import json
import os
import subprocess
import time
from collections.abc import Awaitable, Callable
from functools import lru_cache
from pathlib import Path


def command() -> list[str]:
    executable = (
        Path(os.environ.get("SystemRoot", "C:/Windows"))
        / "System32/WindowsPowerShell/v1.0/powershell.exe"
    )
    return [
        str(executable),
        "-NoProfile",
        "-NonInteractive",
        "-ExecutionPolicy",
        "Bypass",
        "-File",
        str(Path(__file__).with_name("windows_speech.ps1")),
    ]


@lru_cache(maxsize=1)
def installed_voices() -> list[dict[str, str]]:
    if os.name != "nt":
        return []
    try:
        result = subprocess.run(
            command(),
            input='{"mode":"voices"}',
            capture_output=True,
            text=True,
            encoding="utf-8",
            timeout=10,
            creationflags=subprocess.CREATE_NO_WINDOW,
        )
        if result.returncode:
            return []
        voices = json.loads(result.stdout.lstrip("\ufeff"))
        if isinstance(voices, dict):
            voices = [voices]
        return [
            v
            for v in voices
            if isinstance(v.get("name"), str) and isinstance(v.get("language"), str)
        ]
    except (OSError, ValueError, subprocess.TimeoutExpired):
        return []


async def synthesize(text: str, voice: str, disconnected: Callable[[], Awaitable[bool]]) -> bytes:
    if os.name != "nt":
        raise RuntimeError("System speech is unavailable")
    process = await asyncio.create_subprocess_exec(
        *command(),
        stdin=asyncio.subprocess.PIPE,
        stdout=asyncio.subprocess.PIPE,
        stderr=asyncio.subprocess.PIPE,
        creationflags=subprocess.CREATE_NO_WINDOW,
    )
    payload = json.dumps(
        {"mode": "speak", "text": text, "voice": voice}, ensure_ascii=False
    ).encode("utf-8")
    communication = asyncio.create_task(process.communicate(payload))
    deadline = time.monotonic() + 25
    try:
        while not communication.done():
            if await disconnected():
                raise asyncio.CancelledError
            if time.monotonic() > deadline:
                raise TimeoutError("Speech synthesis timed out")
            await asyncio.wait({communication}, timeout=0.1)
        stdout, _ = await communication
        if process.returncode:
            raise RuntimeError("System speech failed")
        wave = base64.b64decode(stdout.strip(), validate=True)
        if not wave.startswith(b"RIFF") or wave[8:12] != b"WAVE":
            raise RuntimeError("System speech returned invalid audio")
        return wave
    finally:
        if process.returncode is None:
            process.kill()
        await process.wait()
        if not communication.done():
            await communication
