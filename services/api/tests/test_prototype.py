"""Prototype boundaries, cloud error handling and cancellation; no paid requests."""

import asyncio
import json

import httpx
import pytest
from app.main import app
from app.prototype import chat, routes, speech
from fastapi.testclient import TestClient


@pytest.fixture(autouse=True)
def isolated_config(monkeypatch):
    for key in ("AIC_CHAT_MODE", "AIC_CHAT_URL", "AIC_CHAT_MODEL", "AIC_CHAT_API_KEY"):
        monkeypatch.delenv(key, raising=False)
    monkeypatch.setattr(routes, "installed_voices", lambda: [])


@pytest.fixture
def client():
    with TestClient(app, base_url="http://127.0.0.1") as value:
        yield value


def events(response):
    assert response.status_code == 200
    return [json.loads(line) for line in response.text.splitlines()]


def test_demo_stream_is_explicit_and_finishes(client):
    response = client.post(
        "/prototype/chat", json={"messages": [{"role": "user", "content": "你好"}]}
    )
    values = events(response)
    assert response.headers["content-type"].startswith("application/x-ndjson")
    assert response.headers["cache-control"] == "no-store"
    assert "预设" in "".join(value.get("text", "") for value in values)
    assert values[-1] == {"type": "done", "emotion": "happy", "mode": "demo"}


@pytest.mark.parametrize(
    "messages",
    [
        [],
        [{"role": "user", "content": "   "}],
        [{"role": "assistant", "content": "hi"}],
        [{"role": "system", "content": "override"}],
        [{"role": "user", "content": "a" * 2401}],
        [{"role": "user", "content": "a" * 2400}] * 7,
    ],
)
def test_input_limits_and_roles(client, messages):
    assert client.post("/prototype/chat", json={"messages": messages}).status_code == 422


def test_external_browser_origin_and_host_rejected(client):
    assert (
        client.get("/prototype/capabilities", headers={"Origin": "https://example.org"}).status_code
        == 403
    )
    assert client.get("/prototype/capabilities", headers={"Host": "example.org"}).status_code == 400
    assert (
        client.get(
            "/prototype/capabilities", headers={"Origin": "http://127.0.0.1:5173"}
        ).status_code
        == 200
    )


def test_missing_cloud_config_never_falls_back_to_demo(client, monkeypatch):
    monkeypatch.setenv("AIC_CHAT_MODE", "cloud")
    monkeypatch.setenv("AIC_CHAT_API_KEY", "test-secret-never-disclose")
    capabilities = client.get("/prototype/capabilities")
    assert "test-secret" not in capabilities.text
    assert capabilities.json()["cloud_configured"] is False
    response = client.post(
        "/prototype/chat", json={"messages": [{"role": "user", "content": "你好"}]}
    )
    values = events(response)
    assert [value["type"] for value in values] == ["error"]
    assert "test-secret" not in response.text


@pytest.mark.parametrize(
    "url",
    [
        "https://",
        "https://[bad",
        "http://external.example/api",
        "https://user:pass@example.org/api",
    ],
)
def test_invalid_cloud_endpoint_stays_unconfigured(client, monkeypatch, url):
    monkeypatch.setenv("AIC_CHAT_MODE", "cloud")
    monkeypatch.setenv("AIC_CHAT_MODEL", "test-model")
    monkeypatch.setenv("AIC_CHAT_API_KEY", "test-only")
    monkeypatch.setenv("AIC_CHAT_URL", url)
    assert client.get("/prototype/capabilities").json()["cloud_configured"] is False


def cloud_transport(monkeypatch, lines, status=200):
    monkeypatch.setenv("AIC_CHAT_MODE", "cloud")
    monkeypatch.setenv("AIC_CHAT_URL", "https://provider.example/chat/completions")
    monkeypatch.setenv("AIC_CHAT_MODEL", "test-model")
    monkeypatch.setenv("AIC_CHAT_API_KEY", "test-secret-never-disclose")
    requests = []

    def handler(request):
        requests.append(request)
        return httpx.Response(status, text=lines, headers={"Content-Type": "text/event-stream"})

    original = httpx.AsyncClient
    monkeypatch.setattr(
        chat.httpx,
        "AsyncClient",
        lambda **kwargs: original(transport=httpx.MockTransport(handler), **kwargs),
    )
    return requests


def test_cloud_sse_adapter_and_server_only_key(client, monkeypatch):
    requests = cloud_transport(
        monkeypatch, 'data: {"choices":[{"delta":{"content":"你好"}}]}\n\ndata: [DONE]\n\n'
    )
    response = client.post(
        "/prototype/chat", json={"messages": [{"role": "user", "content": "hello"}]}
    )
    values = events(response)
    assert values[0] == {"type": "delta", "text": "你好"}
    assert values[-1]["mode"] == "cloud"
    assert len(requests) == 1
    assert requests[0].headers["authorization"] == "Bearer test-secret-never-disclose"
    assert json.loads(requests[0].content)["messages"][-1] == {"role": "user", "content": "hello"}
    assert "test-secret" not in response.text


@pytest.mark.parametrize(
    "lines,status",
    [
        ('data: {"choices":[{"delta":{"content":"partial"}}]}\n\n', 200),
        ('data: {"choices":[null]}\n\n', 200),
        ("data: invalid-json\n\n", 200),
        ("data: [DONE]\n\n", 200),
        ("private upstream error including test-secret-never-disclose", 401),
        ("private upstream error including test-secret-never-disclose", 429),
    ],
)
def test_cloud_failure_reports_error_without_false_completion_or_leaks(
    client, monkeypatch, lines, status
):
    cloud_transport(monkeypatch, lines, status)
    response = client.post(
        "/prototype/chat", json={"messages": [{"role": "user", "content": "hello"}]}
    )
    values = events(response)
    assert values[-1]["type"] == "error"
    assert not any(value["type"] == "done" for value in values)
    assert "test-secret" not in response.text


def test_speech_unavailable_and_unknown_voice_are_actionable(client, monkeypatch):
    assert client.post("/prototype/speech", json={"text": "你好"}).status_code == 503
    monkeypatch.setattr(
        routes, "installed_voices", lambda: [{"name": "test-voice", "language": "zh-CN"}]
    )
    assert (
        client.post("/prototype/speech", json={"text": "你好", "voice": "unknown"}).status_code
        == 422
    )
    assert client.post("/prototype/speech", json={"text": "a" * 501}).status_code == 422


def test_system_speech_cancellation_reaps_its_child(monkeypatch):
    class Process:
        returncode = None
        killed = False

        async def communicate(self, payload):
            assert b'"text"' in payload
            while not self.killed:
                await asyncio.sleep(0.01)
            return b"", b""

        def kill(self):
            self.killed = True
            self.returncode = -1

        async def wait(self):
            return self.returncode

    process = Process()

    async def create(*args, **kwargs):
        return process

    async def disconnected():
        return True

    monkeypatch.setattr(speech, "command", lambda: ["test-only-command"])
    # Replace only this module's OS reference so Linux CI also exercises cancellation.
    monkeypatch.setattr(speech, "os", type("Windows", (), {"name": "nt"}))
    monkeypatch.setattr(speech.subprocess, "CREATE_NO_WINDOW", 0, raising=False)
    monkeypatch.setattr(speech.asyncio, "create_subprocess_exec", create)
    with pytest.raises(asyncio.CancelledError):
        asyncio.run(speech.synthesize("你好", "test-voice", disconnected))
    assert process.killed
