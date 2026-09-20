"""Replaceable provider boundary. The packaged fixture never performs network I/O."""

import asyncio
from pathlib import Path
from typing import Protocol

from .audio import WavInfo, validate_wav

FIXTURE_REPLY = "【演示回复】你好，我是咲。当前正在播放随包测试音频，用于验证声音、口型与停止功能。"
FIXTURE_TRANSCRIPTION = "【演示转写】这是一条固定测试文本，请编辑后再发送。"


class SpeechSynthesizer(Protocol):
    async def synthesize(self, text: str, voice_id: str | None) -> bytes: ...


class FixtureSpeechSynthesizer:
    """Audio is a test signal, not synthesized speech or a TTS quality claim."""

    def __init__(self, delay: float = 0.05):
        self.delay = delay
        self.wav = Path(__file__).with_name("fixtures").joinpath("demo-tone.wav").read_bytes()
        self.info: WavInfo = validate_wav(self.wav, 24000, 8 * 1024 * 1024, 120)
        self.calls = 0

    async def synthesize(self, text: str, voice_id: str | None) -> bytes:
        self.calls += 1
        await asyncio.sleep(self.delay)
        return self.wav


class FixtureProvider:
    def __init__(self, delay: float = 0.05):
        self.delay = delay
        self.llm_calls = 0
        self.asr_calls = 0

    async def reply(self, text: str) -> str:
        self.llm_calls += 1
        await asyncio.sleep(self.delay)
        return FIXTURE_REPLY

    async def transcribe(self, pcm: bytes) -> str:
        self.asr_calls += 1
        await asyncio.sleep(self.delay)
        return FIXTURE_TRANSCRIPTION
