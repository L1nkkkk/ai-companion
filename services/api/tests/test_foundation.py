"""Exercise the real HTTP route against the frozen health contract."""

import json
from pathlib import Path

from app.main import app
from fastapi.testclient import TestClient
from jsonschema import Draft202012Validator


def test_liveness_matches_frozen_contract():
    contract_path = Path(__file__).resolve().parents[3] / "contracts/openapi.json"
    contract = json.loads(contract_path.read_text(encoding="utf-8"))
    with TestClient(app) as client:
        response = client.get("/health/live")
    assert response.status_code == 200
    Draft202012Validator(contract["components"]["schemas"]["Health"]).validate(response.json())


def test_foundation_does_not_advertise_unimplemented_business_routes():
    with TestClient(app) as client:
        assert client.post("/v1/sessions", json={}).status_code == 404
        assert client.get("/health/ready").status_code == 404
