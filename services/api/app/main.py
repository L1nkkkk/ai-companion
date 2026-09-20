"""Foundation liveness and optional isolated U01 local preview routes."""

import os
from typing import Literal

from fastapi import FastAPI
from pydantic import BaseModel, ConfigDict

from .unity_preview import PreviewSettings, register_preview

app = FastAPI(title="AI Companion foundation", version="0.0.0")
register_preview(
    app, PreviewSettings.from_environment() if os.environ.get("U01_PREVIEW_CONFIG") else None
)


class Health(BaseModel):
    model_config = ConfigDict(extra="forbid")
    status: Literal["ok", "degraded", "unavailable"]


@app.get("/health/live", response_model=Health, operation_id="liveness")
async def liveness() -> Health:
    return Health(status="ok")
