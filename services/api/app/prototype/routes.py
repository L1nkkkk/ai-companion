"""Loopback-only desktop preview endpoints. Not the authenticated R1 protocol."""

import asyncio
import json
from typing import Literal
from urllib.parse import urlsplit

from fastapi import APIRouter, Depends, HTTPException, Request, Response
from fastapi.responses import StreamingResponse
from pydantic import BaseModel, ConfigDict, Field, model_validator

from .chat import get_config, stream_reply
from .speech import installed_voices, synthesize


def local_origin(request: Request) -> None:
    origin = request.headers.get("origin")
    try:
        allowed = not origin or urlsplit(origin).hostname in {"127.0.0.1", "localhost", "::1"}
    except ValueError:
        allowed = False
    if not allowed:
        raise HTTPException(403, "此原型只接受本机页面请求。")


router = APIRouter(prefix="/prototype", dependencies=[Depends(local_origin)])
speech_slots = asyncio.Semaphore(2)


class Message(BaseModel):
    model_config = ConfigDict(extra="forbid")
    role: Literal["user", "assistant"]
    content: str = Field(min_length=1, max_length=2400)


class ChatRequest(BaseModel):
    model_config = ConfigDict(extra="forbid")
    messages: list[Message] = Field(min_length=1, max_length=20)
    character_name: str = Field(default="小星", min_length=1, max_length=20)

    @model_validator(mode="after")
    def validate_conversation(self):
        if self.messages[-1].role != "user" or not self.messages[-1].content.strip():
            raise ValueError("The last message must be non-empty user input")
        if sum(len(message.content) for message in self.messages) > 16000:
            raise ValueError("Conversation is too long")
        return self


class SpeechRequest(BaseModel):
    model_config = ConfigDict(extra="forbid")
    text: str = Field(min_length=1, max_length=500)
    voice: str = Field(default="", max_length=100)


@router.get("/capabilities")
async def capabilities():
    config = get_config()
    voices = await asyncio.to_thread(installed_voices)
    return {
        "mode": config.mode if config.mode in {"demo", "cloud"} else "unconfigured",
        "cloud_configured": config.configured,
        "model": config.model if config.mode == "cloud" else "",
        "voices": voices,
        "avatar": "svg-demo",
    }


@router.post("/chat")
async def chat(body: ChatRequest, request: Request):
    async def events():
        async for event in stream_reply(
            [item.model_dump() for item in body.messages], body.character_name
        ):
            if await request.is_disconnected():
                break
            yield json.dumps(event, ensure_ascii=False) + "\n"

    return StreamingResponse(
        events(),
        media_type="application/x-ndjson",
        headers={"Cache-Control": "no-store", "X-Accel-Buffering": "no"},
    )


@router.post("/speech")
async def speech(body: SpeechRequest, request: Request):
    voices = await asyncio.to_thread(installed_voices)
    if not voices:
        raise HTTPException(503, "本机系统语音不可用，可在设置中关闭朗读或使用浏览器语音。")
    if body.voice and body.voice not in {voice["name"] for voice in voices}:
        raise HTTPException(422, "所选音色不可用。")
    selected = body.voice or next(
        (v["name"] for v in voices if v["language"].startswith("zh")), voices[0]["name"]
    )
    try:
        async with asyncio.timeout(1):
            await speech_slots.acquire()
    except TimeoutError as exc:
        raise HTTPException(429, "语音正在处理，请稍后重试。") from exc
    try:
        wave = await synthesize(body.text, selected, request.is_disconnected)
    except (RuntimeError, OSError, ValueError, TimeoutError) as exc:
        raise HTTPException(503, "系统语音暂时不可用，文字回复已保留。") from exc
    finally:
        speech_slots.release()
    return Response(wave, media_type="audio/wav", headers={"Cache-Control": "no-store"})
