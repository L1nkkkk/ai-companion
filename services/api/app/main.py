"""T00 process liveness only; business routes are implemented in later tasks."""

from typing import Literal

from fastapi import FastAPI
from pydantic import BaseModel, ConfigDict

app = FastAPI(title="AI Companion foundation", version="0.0.0")


class Health(BaseModel):
    model_config = ConfigDict(extra="forbid")
    status: Literal["ok", "degraded", "unavailable"]


@app.get("/health/live", response_model=Health, operation_id="liveness")
async def liveness() -> Health:
    return Health(status="ok")
