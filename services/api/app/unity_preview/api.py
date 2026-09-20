"""FastAPI adapter for the isolated, authenticated local preview protocol."""

import asyncio
import ipaddress
import json
import secrets
import struct
from contextlib import asynccontextmanager

from fastapi import APIRouter, FastAPI, Request
from fastapi.responses import JSONResponse, Response, StreamingResponse
from pydantic import ValidationError

from .audio import validate_wav
from .models import PREFIX, CancelRequest, TurnRequest, uuid_string
from .runtime import (
    OperationCancelled,
    PreviewFailure,
    PreviewRuntime,
    PreviewSettings,
)


def error_response(error: PreviewFailure, request_id: str | None = None):
    return JSONResponse(
        error.payload(request_id), status_code=error.status, headers={"Cache-Control": "no-store"}
    )


async def limited_body(request: Request, limit: int) -> bytes:
    body = bytearray()
    async for chunk in request.stream():
        if len(body) + len(chunk) > limit:
            raise PreviewFailure("invalid_request", "请求内容超过大小上限。", 422)
        body.extend(chunk)
    return bytes(body)


def unique_object(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError("Duplicate JSON key")
        result[key] = value
    return result


async def read_json(request: Request, model):
    if request.headers.get("content-type", "").split(";")[0].strip() != "application/json":
        raise PreviewFailure("invalid_request", "请求必须使用 application/json。", 422)
    body = await limited_body(request, 65536)
    try:
        value = json.loads(body.decode("utf-8"), object_pairs_hook=unique_object)
        if isinstance(value, dict) and isinstance(value.get("request_id"), str):
            try:
                request.state.preview_request_id = uuid_string(value["request_id"], 4)
            except ValueError:
                pass
        return model.model_validate(value)
    except (ValueError, ValidationError, UnicodeDecodeError, RecursionError):
        raise PreviewFailure(
            "invalid_request", "请求字段、身份或容量无效，请检查预览配置。", 422
        ) from None


def register_preview(app: FastAPI, settings: PreviewSettings | None):
    runtime = PreviewRuntime(settings) if settings else None
    app.state.unity_preview = runtime
    router = APIRouter(prefix=PREFIX)

    @app.middleware("http")
    async def preview_auth(request: Request, call_next):
        if not request.url.path.startswith(PREFIX):
            return await call_next(request)
        local = False
        try:
            local = bool(request.client and ipaddress.ip_address(request.client.host).is_loopback)
        except ValueError:
            pass
        host_valid = request.url.hostname in ("127.0.0.1", "localhost", "::1")
        # Unity is not a browser. Reject Origin-bearing browser requests entirely.
        authorization = request.headers.get("authorization", "")
        expected = f"Bearer {settings.token}" if settings else ""
        if (
            not local
            or not host_valid
            or "origin" in request.headers
            or not settings
            or not secrets.compare_digest(authorization.encode(), expected.encode())
        ):
            return error_response(
                PreviewFailure("unauthorized", "本机访问验证失败，请重新启动桌面预览。", 401)
            )
        response = await call_next(request)
        response.headers["Cache-Control"] = "no-store"
        response.headers["X-Content-Type-Options"] = "nosniff"
        return response

    @router.get("/capabilities")
    async def capabilities():
        return runtime.capabilities()

    @router.post("/turns")
    async def turns(request: Request):
        submission = None
        try:
            submission = await read_json(request, TurnRequest)
            operation = await runtime.begin(submission.request_id, submission.generation)
        except PreviewFailure as error:
            return error_response(
                error,
                submission.request_id
                if submission
                else getattr(request.state, "preview_request_id", None),
            )
        return StreamingResponse(
            runtime.events(submission, operation),
            media_type="application/x-ndjson",
            headers={"Cache-Control": "no-store"},
        )

    @router.post("/requests/{request_id}/cancel")
    async def cancel(request_id: str, request: Request):
        parsed_id = None
        try:
            parsed_id = uuid_string(request_id, 4)
            body = await read_json(request, CancelRequest)
            await runtime.cancel(parsed_id, body.generation)
            return {"request_id": parsed_id, "cancelled": True}
        except ValueError:
            return error_response(PreviewFailure("invalid_request", "请求身份无效。", 422))
        except PreviewFailure as error:
            return error_response(error, parsed_id)

    @router.get("/turns/{turn_id}/audio.wav")
    async def audio(turn_id: str):
        try:
            uuid_string(turn_id)
            data = await runtime.get_audio(turn_id)
            if runtime.settings.scenario == "slow_download":

                async def delayed_body():
                    # Headers are already sent. Never emit audio retained before a cancellation.
                    await asyncio.sleep(3)
                    try:
                        current = await runtime.get_audio(turn_id)
                    except PreviewFailure:
                        return  # Cancelled/expired after headers: terminate with no stale WAV body.
                    yield current

                return StreamingResponse(
                    delayed_body(), media_type="audio/wav", headers={"Cache-Control": "no-store"}
                )
            return Response(data, media_type="audio/wav", headers={"Cache-Control": "no-store"})
        except ValueError:
            return error_response(PreviewFailure("invalid_request", "音频身份无效。", 422))
        except PreviewFailure as error:
            return error_response(error)

    @router.post("/transcriptions")
    async def transcriptions(request: Request):
        request_id = None
        operation = None
        disconnect_watcher = None
        try:
            request_id = uuid_string(request.headers.get("x-request-id", ""), 4)
            raw_generation = request.headers.get("x-generation", "")
            if not raw_generation.isascii() or not raw_generation.isdecimal():
                raise ValueError("Invalid generation")
            generation = int(raw_generation)
            CancelRequest(generation=generation)
            if request.headers.get("content-type", "").split(";")[0].strip() != "audio/wav":
                raise PreviewFailure("invalid_audio", "录音必须使用 audio/wav。", 422)
            data = await limited_body(request, 1048576)
            try:
                info = validate_wav(data, 16000, 1048576, 30)
            except ValueError:
                raise PreviewFailure(
                    "invalid_audio", "录音须为非空 16 kHz 单声道 PCM16 WAV，最长 30 秒。", 422
                ) from None
            if not any(abs(value[0]) >= 128 for value in struct.iter_unpack("<h", info.pcm)):
                raise PreviewFailure("no_speech", "未检测到声音，请检查麦克风后重新录音。", 422)
            operation = await runtime.begin(request_id, generation)

            async def watch_disconnect():
                while True:
                    message = await request.receive()
                    if message["type"] == "http.disconnect":
                        runtime.stop(operation)
                        return

            disconnect_watcher = asyncio.create_task(watch_disconnect())
            text = await runtime.work(
                operation, runtime.provider.transcribe(info.pcm), runtime.settings.asr_timeout
            )
            return {
                "request_id": request_id,
                "generation": generation,
                "text": text,
                "mode": runtime.settings.mode,
            }
        except (ValueError, ValidationError):
            return error_response(
                PreviewFailure("invalid_request", "录音请求身份无效。", 422), request_id
            )
        except OperationCancelled:
            return error_response(PreviewFailure("cancelled", "转写已取消。", 409), request_id)
        except PreviewFailure as error:
            return error_response(error, request_id)
        except asyncio.CancelledError:
            if operation:
                runtime.stop(operation)
            raise
        except Exception:
            return error_response(
                PreviewFailure("provider_unavailable", "转写服务暂不可用。", 503, True), request_id
            )
        finally:
            if disconnect_watcher:
                disconnect_watcher.cancel()
                await asyncio.gather(disconnect_watcher, return_exceptions=True)
            if operation:
                await runtime.finish(operation)

    app.include_router(router)
    return runtime


def create_preview_app(settings: PreviewSettings) -> FastAPI:
    @asynccontextmanager
    async def lifespan(app):
        yield
        await app.state.unity_preview.shutdown()

    app = FastAPI(
        title="U01 local desktop preview",
        version="1",
        lifespan=lifespan,
        docs_url=None,
        redoc_url=None,
        openapi_url=None,
    )
    register_preview(app, settings)

    @app.get("/health/live")
    async def live():
        return {"status": "ok"}

    return app


def create_app_from_environment() -> FastAPI:
    return create_preview_app(PreviewSettings.from_environment())
