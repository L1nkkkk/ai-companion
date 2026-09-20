"""Single-process cancellation, deduplication and bounded ephemeral audio storage."""

import asyncio
import json
import os
import time
from collections import OrderedDict
from dataclasses import dataclass, field
from pathlib import Path
from typing import Awaitable, Callable, TypeVar
from uuid import uuid4

from .models import LIMITS, PREFIX, PROTOCOL, RuntimeConfiguration, TurnRequest
from .providers import FixtureProvider, FixtureSpeechSynthesizer, SpeechSynthesizer

SCENARIOS = {
    "normal",
    "tts_failure",
    "partial_stream",
    "seq_duplicate",
    "seq_gap",
    "wrong_ids",
    "late_audio",
    "corrupt_audio",
    "truncated_audio",
    "expired_audio",
    "budget_exceeded",
    "provider_timeout",
    "slow_generation",
    "slow_download",
}


class PreviewFailure(Exception):
    def __init__(self, code: str, message: str, status: int, retryable: bool = False):
        self.code, self.message, self.status, self.retryable = code, message, status, retryable

    def payload(self, request_id: str | None = None) -> dict:
        return dict(
            code=self.code, message=self.message, retryable=self.retryable, request_id=request_id
        )


class OperationCancelled(Exception):
    pass


@dataclass(frozen=True)
class PreviewSettings:
    token: str
    mode: str = "fixture"
    scenario: str = "normal"
    fixture_delay: float = 0.05
    dedup_seconds: float = 300
    audio_seconds: float = 300
    max_entries: int = 512
    max_audio_bytes: int = 32 * 1024 * 1024
    llm_timeout: float = 45
    tts_timeout: float = 30
    asr_timeout: float = 30
    turn_timeout: float = 90
    clock: Callable[[], float] = time.monotonic

    def __post_init__(self):
        RuntimeConfiguration(protocol=PROTOCOL, base_url="http://127.0.0.1:8000", token=self.token)
        if self.mode not in ("fixture", "cloud") or self.scenario not in SCENARIOS:
            raise ValueError("Invalid preview mode or scenario")
        if self.mode != "fixture" and self.scenario != "normal":
            raise ValueError("Fault scenarios are only available in fixture mode")

    @classmethod
    def from_environment(cls):
        path = os.environ.get("U01_PREVIEW_CONFIG")
        if not path:
            raise ValueError("U01_PREVIEW_CONFIG must point to the private runtime configuration")
        # Do not include invalid content or the path/token in exception messages.
        try:
            configuration = RuntimeConfiguration.model_validate_json(Path(path).read_bytes())
        except Exception:
            raise ValueError("The private preview runtime configuration is invalid") from None
        return cls(
            token=configuration.token,
            mode=os.environ.get("U01_PREVIEW_MODE", "fixture"),
            scenario=os.environ.get("U01_PREVIEW_SCENARIO", "normal"),
        )


@dataclass
class Operation:
    request_id: str
    generation: int
    turn_id: str
    started: float
    cancelled: bool = False
    work: asyncio.Task | None = None
    cancellation: asyncio.Event = field(default_factory=asyncio.Event)


@dataclass
class AudioResource:
    request_id: str
    expires: float
    data: bytes


T = TypeVar("T")


class PreviewRuntime:
    def __init__(self, settings: PreviewSettings, speech: SpeechSynthesizer | None = None):
        self.settings = settings
        self.lock = asyncio.Lock()
        self.seen: dict[str, float] = {}
        self.cancelled: dict[str, float] = {}
        self.audio: OrderedDict[str, AudioResource] = OrderedDict()
        self.active: Operation | None = None
        self.provider = FixtureProvider(settings.fixture_delay)
        self.speech = speech or FixtureSpeechSynthesizer(settings.fixture_delay)

    def capabilities(self) -> dict:
        available = self.settings.mode == "fixture"
        return {
            "protocol": PROTOCOL,
            **{f"{kind}_mode": self.settings.mode for kind in ("chat", "tts", "asr")},
            **{
                kind: {"configured": available, "available": available}
                for kind in ("chat", "tts", "asr")
            },
            "character_ids": ["mao"],
            "voices": [{"id": "fixture-tone", "display_name": "演示测试音频", "mode": "fixture"}]
            if available
            else [],
            "limits": dict(LIMITS),
        }

    def prune(self):
        now = self.settings.clock()
        self.seen = {key: expiry for key, expiry in self.seen.items() if expiry > now}
        self.cancelled = {key: expiry for key, expiry in self.cancelled.items() if expiry > now}
        for key in list(self.audio):
            if self.audio[key].expires <= now:
                del self.audio[key]

    def stop(self, operation: Operation):
        operation.cancelled = True
        operation.cancellation.set()
        if operation.work and not operation.work.done():
            operation.work.cancel()
        for key in list(self.audio):
            if self.audio[key].request_id == operation.request_id:
                del self.audio[key]

    async def begin(self, request_id: str, generation: int) -> Operation:
        async with self.lock:
            self.prune()
            if request_id in self.cancelled:
                raise PreviewFailure("request_cancelled", "请求已取消，请重新点击发送。", 409)
            if request_id in self.seen:
                raise PreviewFailure("duplicate_request", "请求已提交，不会重复生成。", 409)
            if (
                len(self.seen) >= self.settings.max_entries
                or len(self.cancelled) >= self.settings.max_entries
            ):
                raise PreviewFailure("rate_limited", "请求登记已满，请稍后重新发送。", 429, True)
            if self.settings.mode != "fixture":
                raise PreviewFailure(
                    "provider_unavailable", "云服务尚未配置；请配置后台或使用演示模式。", 503
                )
            if self.settings.scenario == "budget_exceeded":
                raise PreviewFailure(
                    "budget_exceeded", "【演示故障】预算已耗尽，请检查后台预算。", 429
                )
            if self.active:
                self.stop(self.active)
            now = self.settings.clock()
            operation = Operation(request_id, generation, str(uuid4()), now)
            self.seen[request_id] = now + self.settings.dedup_seconds
            self.active = operation
            return operation

    async def cancel(self, request_id: str, generation: int):
        async with self.lock:
            self.prune()
            if self.active and self.active.request_id == request_id:
                self.stop(self.active)
            for key in list(self.audio):
                if self.audio[key].request_id == request_id:
                    del self.audio[key]
            if request_id not in self.cancelled:
                if len(self.cancelled) >= self.settings.max_entries:
                    raise PreviewFailure("rate_limited", "取消登记已满，请稍后重试。", 429, True)
                self.cancelled[request_id] = self.settings.clock() + self.settings.dedup_seconds

    async def finish(self, operation: Operation, disconnected: bool = False):
        async with self.lock:
            if disconnected:
                self.stop(operation)
            if self.active is operation:
                self.active = None

    async def work(self, operation: Operation, awaitable: Awaitable[T], timeout: float) -> T:
        task = asyncio.create_task(awaitable)
        cancelled = asyncio.create_task(operation.cancellation.wait())
        operation.work = task
        if operation.cancelled:
            task.cancel()
        remaining = self.settings.turn_timeout - (self.settings.clock() - operation.started)
        try:
            done, _ = await asyncio.wait(
                (task, cancelled),
                timeout=min(timeout, max(0, remaining)),
                return_when=asyncio.FIRST_COMPLETED,
            )
            if operation.cancelled:
                raise OperationCancelled
            if task not in done:
                raise PreviewFailure(
                    "provider_timeout", "服务处理超时，请重新点击发送。", 504, True
                )
            result = task.result()
        finally:
            cancelled.cancel()
            if not task.done():
                task.cancel()
                # Providers may ignore cancellation. Observe a late failure but never publish it.
                task.add_done_callback(
                    lambda finished: None if finished.cancelled() else finished.exception()
                )
            if operation.work is task:
                operation.work = None
        if operation.cancelled:
            raise OperationCancelled
        return result

    def store_audio(self, operation: Operation, data: bytes):
        if operation.cancelled:
            raise OperationCancelled
        self.prune()
        if len(data) > self.settings.max_audio_bytes:
            raise PreviewFailure("invalid_audio", "测试音频超过存储上限。", 422)
        while (
            self.audio
            and sum(len(item.data) for item in self.audio.values()) + len(data)
            > self.settings.max_audio_bytes
        ):
            self.audio.popitem(last=False)
        self.audio[operation.turn_id] = AudioResource(
            operation.request_id, self.settings.clock() + self.settings.audio_seconds, data
        )

    async def get_audio(self, turn_id: str) -> bytes:
        async with self.lock:
            self.prune()
            resource = self.audio.get(turn_id)
            if resource is None:
                raise PreviewFailure("resource_expired", "音频已过期或已取消，请重新发送。", 410)
            return resource.data

    async def events(self, request: TurnRequest, operation: Operation):
        from .audio import validate_wav

        sequence = 0
        completed = False

        def event(kind: str, payload: dict) -> bytes:
            nonlocal sequence
            if operation.cancelled and kind not in ("turn.accepted", "turn.cancelled", "error"):
                raise OperationCancelled
            value = dict(
                protocol=PROTOCOL,
                request_id=request.request_id,
                generation=request.generation,
                conversation_id=request.conversation_id,
                turn_id=operation.turn_id,
                seq=sequence,
                type=kind,
                payload=payload,
            )
            sequence += 1
            return (json.dumps(value, ensure_ascii=False, separators=(",", ":")) + "\n").encode()

        try:
            yield event("turn.accepted", {"mode": self.settings.mode})
            if operation.cancelled:
                raise OperationCancelled
            if self.settings.scenario == "slow_generation":
                await self.work(operation, asyncio.sleep(3), self.settings.llm_timeout)
            if self.settings.scenario == "provider_timeout":
                raise PreviewFailure("provider_timeout", "【演示故障】服务响应超时。", 504, True)
            reply = await self.work(
                operation, self.provider.reply(request.text), self.settings.llm_timeout
            )
            if not reply or len(reply) > 400:
                raise PreviewFailure("invalid_reply", "回复长度无效，未生成音频。", 502)
            delta = event("text.delta", {"text": reply})
            yield delta
            if self.settings.scenario == "partial_stream":
                return
            if self.settings.scenario == "seq_duplicate":
                yield delta
            elif self.settings.scenario == "seq_gap":
                sequence += 1
            elif self.settings.scenario == "wrong_ids":
                wrong = json.loads(event("text.completed", {"text": reply, "emotion": "neutral"}))
                wrong["request_id"] = str(uuid4())
                yield (json.dumps(wrong, ensure_ascii=False) + "\n").encode()
                return
            yield event("text.completed", {"text": reply, "emotion": "happy"})
            if request.generate_audio:
                if self.settings.scenario == "tts_failure":
                    raise PreviewFailure(
                        "provider_unavailable", "【演示故障】语音生成失败，文字已保留。", 503, True
                    )
                if self.settings.scenario == "late_audio":
                    await self.work(operation, asyncio.sleep(3), self.settings.tts_timeout)
                data = await self.work(
                    operation,
                    self.speech.synthesize(reply, request.voice_id),
                    self.settings.tts_timeout,
                )
                try:
                    info = validate_wav(data, 24000, 8 * 1024 * 1024, 120)
                except ValueError:
                    raise PreviewFailure("invalid_audio", "生成音频格式无效。", 422) from None
                if self.settings.scenario == "corrupt_audio":
                    data = b"NOTWAV" + data[6:]
                elif self.settings.scenario == "truncated_audio":
                    data = data[:-1]
                self.store_audio(operation, data)
                if self.settings.scenario == "expired_audio":
                    del self.audio[operation.turn_id]
                yield event(
                    "audio.ready",
                    dict(
                        path=f"{PREFIX}/turns/{operation.turn_id}/audio.wav",
                        sample_rate=24000,
                        channels=1,
                        codec="pcm_s16le",
                        total_samples=info.total_samples,
                    ),
                )
            else:
                yield event("audio.skipped", {"reason": "user_disabled"})
            if operation.cancelled:
                raise OperationCancelled
            yield event("generation.completed", {})
            completed = True
        except OperationCancelled:
            yield event("turn.cancelled", {})
            completed = True
        except PreviewFailure as error:
            payload = error.payload()
            payload.pop("request_id")
            yield event("error", payload)
            completed = True
        except Exception:
            yield event(
                "error",
                dict(
                    code="provider_unavailable",
                    message="服务暂不可用，请检查后台。",
                    retryable=True,
                ),
            )
            completed = True
        finally:
            await self.finish(operation, disconnected=not completed)

    async def shutdown(self):
        if self.active:
            self.stop(self.active)
            if self.active.work:
                await asyncio.gather(self.active.work, return_exceptions=True)
        self.active = None
        self.audio.clear()
