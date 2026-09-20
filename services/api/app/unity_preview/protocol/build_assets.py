"""Reproduce the preview schema and public deterministic interoperability examples."""

import json
from pathlib import Path

from app.unity_preview.models import CancelRequest, RuntimeConfiguration, TurnRequest
from app.unity_preview.providers import FIXTURE_REPLY, FIXTURE_TRANSCRIPTION

ROOT = Path(__file__).parent
REQUEST = "11111111-1111-4111-8111-111111111111"
CONVERSATION = "22222222-2222-4222-8222-222222222222"
TURN = "33333333-3333-4333-8333-333333333333"


def obj(properties):
    return {
        "type": "object",
        "properties": properties,
        "required": list(properties),
        "additionalProperties": False,
    }


def enum(*values):
    return {"enum": list(values)}


def integer(minimum, maximum):
    return {"type": "integer", "minimum": minimum, "maximum": maximum}


def write(name, value):
    (ROOT / name).write_text(
        json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8"
    )


def export():
    uuid = {
        "type": "string",
        "format": "uuid",
        "pattern": r"^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$",
    }
    string = {"type": "string", "minLength": 1, "maxLength": 400}
    error = obj(
        {
            "code": {"type": "string", "minLength": 1, "maxLength": 64},
            "message": string,
            "retryable": {"type": "boolean"},
        }
    )
    payloads = {
        "turn.accepted": obj({"mode": enum("fixture", "cloud")}),
        "text.delta": obj({"text": string}),
        "text.completed": obj(
            {"text": string, "emotion": enum("neutral", "happy", "sad", "surprised", "thinking")}
        ),
        "audio.ready": obj(
            {
                "path": {
                    "type": "string",
                    "pattern": r"^/preview/unity/v1/turns/[0-9a-f-]{36}/audio\.wav$",
                },
                "sample_rate": enum(24000),
                "channels": enum(1),
                "codec": enum("pcm_s16le"),
                "total_samples": integer(1, 2880000),
            }
        ),
        "audio.skipped": obj({"reason": enum("user_disabled")}),
        "generation.completed": obj({}),
        "turn.cancelled": obj({}),
        "error": error,
    }
    schemas = {
        "turn-request.schema.json": TurnRequest.model_json_schema(),
        "cancel-request.schema.json": CancelRequest.model_json_schema(),
        "runtime-config.schema.json": RuntimeConfiguration.model_json_schema(),
        "event.schema.json": {
            "oneOf": [
                obj(
                    {
                        "protocol": enum("unity-preview/1"),
                        "request_id": uuid,
                        "generation": integer(0, 4294967295),
                        "conversation_id": uuid,
                        "turn_id": uuid,
                        "seq": integer(0, 4294967295),
                        "type": enum(kind),
                        "payload": payload,
                    }
                )
                for kind, payload in payloads.items()
            ]
        },
        "error.schema.json": obj(
            {**error["properties"], "request_id": {"anyOf": [uuid, {"type": "null"}]}}
        ),
        "cancel-result.schema.json": obj({"request_id": uuid, "cancelled": enum(True)}),
        "transcription.schema.json": obj(
            {
                "request_id": uuid,
                "generation": integer(0, 4294967295),
                "text": {"type": "string", "minLength": 1, "maxLength": 2000},
                "mode": enum("fixture", "cloud"),
            }
        ),
    }
    for name, schema in schemas.items():
        schema["$schema"] = "https://json-schema.org/draft/2020-12/schema"
        schema["$id"] = "urn:neurosaki:unity-preview:1:" + name
        if name == "turn-request.schema.json":
            schema["properties"]["request_id"].update(uuid)
            schema["properties"]["request_id"]["pattern"] = (
                r"^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$"
            )
            schema["properties"]["conversation_id"].update(uuid)
        write(name, schema)
    from app.unity_preview.models import LIMITS

    modes = {f"{kind}_mode": enum("fixture", "cloud") for kind in ("chat", "tts", "asr")}
    modes["tts_mode"] = enum("fixture", "cloud", "system")
    capability = obj({"configured": {"type": "boolean"}, "available": {"type": "boolean"}})
    capabilities_schema = obj(
        {
            "protocol": enum("unity-preview/1"),
            **modes,
            **{kind: capability for kind in ("chat", "tts", "asr")},
            "character_ids": {
                "type": "array",
                "minItems": 1,
                "maxItems": 128,
                "uniqueItems": True,
                "items": {"type": "string", "minLength": 1, "maxLength": 64},
            },
            "voices": {
                "type": "array",
                "maxItems": 128,
                "items": obj(
                    {
                        "id": {"type": "string", "minLength": 1, "maxLength": 64},
                        "display_name": {"type": "string", "minLength": 1, "maxLength": 128},
                        "mode": enum("fixture", "cloud", "system"),
                    }
                ),
            },
            "limits": obj({name: enum(value) for name, value in LIMITS.items()}),
        }
    )
    capabilities_schema["$schema"] = "https://json-schema.org/draft/2020-12/schema"
    write("capabilities.schema.json", capabilities_schema)
    write(
        "sample-request.json",
        dict(
            protocol="unity-preview/1",
            request_id=REQUEST,
            generation=1,
            conversation_id=CONVERSATION,
            character_id="mao",
            text="你好，测试声音。",
            history=[],
            generate_audio=True,
            voice_id="fixture-tone",
        ),
    )

    def event(seq, kind, payload):
        return dict(
            protocol="unity-preview/1",
            request_id=REQUEST,
            generation=1,
            conversation_id=CONVERSATION,
            turn_id=TURN,
            seq=seq,
            type=kind,
            payload=payload,
        )

    accepted = event(0, "turn.accepted", {"mode": "fixture"})
    delta = event(1, "text.delta", {"text": FIXTURE_REPLY})
    complete = event(2, "text.completed", {"text": FIXTURE_REPLY, "emotion": "happy"})
    ready = event(
        3,
        "audio.ready",
        dict(
            path=f"/preview/unity/v1/turns/{TURN}/audio.wav",
            sample_rate=24000,
            channels=1,
            codec="pcm_s16le",
            total_samples=192000,
        ),
    )
    done = event(4, "generation.completed", {})
    normal = [accepted, delta, complete, ready, done]
    samples = {
        "normal": normal,
        "no_audio": [
            accepted,
            delta,
            complete,
            event(3, "audio.skipped", {"reason": "user_disabled"}),
            done,
        ],
        "partial_stream": [accepted, delta],
        "seq_duplicate": [accepted, delta, delta, complete, ready, done],
        "seq_gap": [accepted, delta, *[{**item, "seq": item["seq"] + 1} for item in normal[2:]]],
        "wrong_ids": [
            accepted,
            delta,
            {**complete, "request_id": "44444444-4444-4444-8444-444444444444"},
        ],
        "cancelled": [accepted, event(1, "turn.cancelled", {})],
        "tts_failure": [
            accepted,
            delta,
            complete,
            event(
                3,
                "error",
                dict(
                    code="provider_unavailable",
                    message="【演示故障】语音生成失败，文字已保留。",
                    retryable=True,
                ),
            ),
        ],
        "provider_timeout": [
            accepted,
            event(
                1,
                "error",
                dict(code="provider_timeout", message="【演示故障】服务响应超时。", retryable=True),
            ),
        ],
        **{
            name: normal
            for name in (
                "late_audio",
                "corrupt_audio",
                "truncated_audio",
                "expired_audio",
                "slow_generation",
                "slow_download",
            )
        },
    }
    for name, items in samples.items():
        (ROOT / (name + ".ndjson")).write_text(
            "".join(
                json.dumps(item, ensure_ascii=False, separators=(",", ":")) + "\n" for item in items
            ),
            encoding="utf-8",
        )
    errors = {
        "cloud_missing": (
            503,
            "provider_unavailable",
            "云服务尚未配置；请配置后台或使用演示模式。",
            False,
        ),
        "cancel_before_submit": (409, "request_cancelled", "请求已取消，请重新点击发送。", False),
        "budget_exceeded": (
            429,
            "budget_exceeded",
            "【演示故障】预算已耗尽，请检查后台预算。",
            False,
        ),
        "empty_recording": (
            422,
            "invalid_audio",
            "录音须为非空 16 kHz 单声道 PCM16 WAV，最长 30 秒。",
            False,
        ),
        "expired_resource": (410, "resource_expired", "音频已过期或已取消，请重新发送。", False),
    }
    for name, (status, code, message, retryable) in errors.items():
        write(
            name + ".json",
            dict(
                code=code,
                message=message,
                retryable=retryable,
                request_id=None if status == 410 else REQUEST,
            ),
        )
    write(
        "asr_success.json",
        dict(request_id=REQUEST, generation=1, text=FIXTURE_TRANSCRIPTION, mode="fixture"),
    )
    write(
        "manifest.json",
        {
            "protocol": "unity-preview/1",
            "audio": "../fixtures/demo-tone.wav",
            "audio_total_samples": 192000,
            "audio_seconds": 8,
            "negative_streams": ["partial_stream", "seq_gap", "wrong_ids"],
            "duplicate_policy": "Identical repeated sequence is discarded; conflicting sequence fails.",
            "http_errors": {name: values[0] for name, values in errors.items()},
            "scenario_environment": "U01_PREVIEW_SCENARIO",
            "late_audio_delay_seconds": 3,
            "slow_generation_delay_seconds": 3,
            "slow_download_body_delay_seconds": 3,
            "delay_scenario_source": "Explicit fixture scheduler; no cloud/provider latency claim.",
        },
    )


if __name__ == "__main__":
    export()
