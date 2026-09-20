"""Local desktop preview. The authenticated R1 service is still separate work."""

from typing import Literal

from fastapi import FastAPI
from pydantic import BaseModel, ConfigDict
from starlette.middleware.trustedhost import TrustedHostMiddleware

from app.prototype.routes import router

app = FastAPI(title="AI Companion desktop preview", version="0.1.0")
app.add_middleware(TrustedHostMiddleware, allowed_hosts=["localhost", "127.0.0.1", "[::1]"])
app.include_router(router)


class Health(BaseModel):
    model_config = ConfigDict(extra="forbid")
    status: Literal["ok", "degraded", "unavailable"]


@app.get("/health/live", response_model=Health, operation_id="liveness")
async def liveness() -> Health:
    return Health(status="ok")
