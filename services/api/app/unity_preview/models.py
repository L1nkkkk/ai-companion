"""Strict wire models; these deliberately do not modify root contracts/."""

from typing import Annotated, Literal
from uuid import UUID

from pydantic import BaseModel, ConfigDict, Field, field_validator, model_validator

PROTOCOL = "unity-preview/1"
PREFIX = "/preview/unity/v1"
LIMITS = {
    "max_input_characters": 2000,
    "max_reply_characters": 400,
    "max_history_messages": 6,
    "max_context_characters": 12000,
    "max_request_bytes": 65536,
    "max_event_bytes": 65536,
    "max_output_wav_bytes": 8388608,
    "max_output_seconds": 120,
    "max_capture_wav_bytes": 1048576,
    "max_capture_seconds": 30,
}


def uuid_string(value: str, version: int | None = None) -> str:
    parsed = UUID(value)
    if str(parsed) != value or parsed.int == 0 or (version and parsed.version != version):
        raise ValueError("A canonical non-zero UUID is required")
    return value


class StrictModel(BaseModel):
    model_config = ConfigDict(extra="forbid", strict=True)


class HistoryItem(StrictModel):
    role: Literal["user", "assistant"]
    text: Annotated[str, Field(min_length=1, max_length=2000)]

    @field_validator("text")
    @classmethod
    def non_blank(cls, value: str) -> str:
        if not value.strip():
            raise ValueError("Text must not be blank")
        return value


class TurnRequest(StrictModel):
    protocol: Literal["unity-preview/1"]
    request_id: str
    generation: Annotated[int, Field(ge=0, le=4294967295)]
    conversation_id: str
    character_id: Literal["mao"]
    text: Annotated[str, Field(min_length=1, max_length=2000)]
    history: Annotated[list[HistoryItem], Field(max_length=6)]
    generate_audio: bool
    voice_id: Literal["fixture-tone"] | None

    @field_validator("request_id")
    @classmethod
    def valid_request_id(cls, value: str) -> str:
        return uuid_string(value, 4)

    @field_validator("conversation_id")
    @classmethod
    def valid_conversation_id(cls, value: str) -> str:
        return uuid_string(value)

    @model_validator(mode="after")
    def text_limits(self):
        if not self.text.strip():
            raise ValueError("Text must not be blank")
        if len(self.text) + sum(len(item.text) for item in self.history) > 12000:
            raise ValueError("Context exceeds 12000 Unicode characters")
        return self


class CancelRequest(StrictModel):
    generation: Annotated[int, Field(ge=0, le=4294967295)]


class RuntimeConfiguration(StrictModel):
    protocol: Literal["unity-preview/1"]
    base_url: Literal["http://127.0.0.1:8000"]
    token: Annotated[str, Field(min_length=32, max_length=256, pattern=r"^[A-Za-z0-9_-]+$")]
