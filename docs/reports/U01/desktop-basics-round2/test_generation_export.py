"""Offline tests only: no socket, process, native dialog or real QA evidence."""

import asyncio
import importlib.util
import json
from pathlib import Path
from uuid import uuid4

import pytest
import uvicorn
from app.unity_preview.models import TurnRequest

SPEC = importlib.util.spec_from_file_location(
    "generation_export", Path(__file__).with_name("run-generation-export.py")
)
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)


def configure(tmp_path, monkeypatch, mode="fixture"):
    path = tmp_path / "test-config.json"
    path.write_text(
        json.dumps(
            {
                "protocol": "unity-preview/1",
                "base_url": "http://127.0.0.1:8000",
                "token": "offline_fixture_not_a_real_secret_0123456789",
            }
        ),
        encoding="utf-8",
    )
    monkeypatch.setenv("U01_PREVIEW_CONFIG", str(path))
    monkeypatch.setenv("U01_PREVIEW_MODE", mode)
    monkeypatch.setenv("U01_PREVIEW_SCENARIO", "normal")


def test_frozen_factory_supports_exact_injection_without_server(tmp_path, monkeypatch):
    configure(tmp_path, monkeypatch)
    captured = []
    monkeypatch.setattr(uvicorn, "run", lambda app, **kwargs: captured.append((app, kwargs)))
    exec(MODULE.BACKEND_CODE, {})
    app, options = captured[0]
    runtime = app.state.unity_preview
    assert runtime.settings.scenario == "slow_generation"
    assert runtime.provider.delay == runtime.speech.delay == 8.0
    assert runtime.settings.llm_timeout == 45 and runtime.settings.tts_timeout == 30
    assert options == dict(host="127.0.0.1", port=8000, workers=1, access_log=False)

    async def accepted_then_cancel():
        request = TurnRequest(
            protocol="unity-preview/1",
            request_id=str(uuid4()),
            generation=1,
            conversation_id=str(uuid4()),
            character_id="mao",
            text="Offline fixture test",
            history=[],
            generate_audio=True,
            voice_id="fixture-tone",
        )
        operation = await runtime.begin(request.request_id, request.generation)
        events = runtime.events(request, operation)
        accepted = json.loads(await anext(events))
        assert accepted["type"] == "turn.accepted" and runtime.provider.llm_calls == 0
        await runtime.cancel(request.request_id, request.generation)
        remaining = [json.loads(event) async for event in events]
        assert all(event["type"] not in {"text.delta", "audio.ready"} for event in remaining)
        assert not runtime.audio
        await runtime.shutdown()

    asyncio.run(accepted_then_cancel())


def test_injection_refuses_cloud_mode_before_server(tmp_path, monkeypatch):
    configure(tmp_path, monkeypatch, "cloud")
    captured = []
    monkeypatch.setattr(uvicorn, "run", lambda *args, **kwargs: captured.append(args))
    with pytest.raises(ValueError, match="Fixture-only"):
        exec(MODULE.BACKEND_CODE, {})
    assert captured == []


def test_prepare_records_both_delays_and_preserves_existing_attempt(tmp_path):
    package = tmp_path / "package"
    package.mkdir()
    files = []
    for name in ["python/python.exe", "NeuroSaki.exe", "launch_desktop.py"]:
        path = package / name
        path.parent.mkdir(exist_ok=True, parents=True)
        path.write_bytes(b"offline nonexecutable fixture")
        files.append(dict(path=name, bytes=path.stat().st_size, sha256=MODULE.qa.sha256(path)))
    MODULE.qa.write_json(
        package / "package-manifest.json",
        dict(format=1, mode="fixture", source_sha="a" * 40, source_dirty=False, files=files),
    )
    evidence = tmp_path / "case"
    arguments = dict(seconds=240, delay=-1, width=1280, height=800, expected_sha="a" * 40)
    case = MODULE.prepare(package, evidence, "cancel", **arguments)
    assert case["phase"] == "generation" and case["scenario"] == "slow_generation"
    injection = case["fixture_latency_injection"]
    assert injection["accepted_to_text_minimum_seconds_if_not_cancelled"] == 11
    assert injection["fixture_speech_delay_seconds_if_reached"] == 8
    original = (evidence / "case.json").read_bytes()
    with pytest.raises(FileExistsError):
        MODULE.prepare(package, evidence, "save", **arguments)
    assert (evidence / "case.json").read_bytes() == original
    assert not (evidence / "runtime").exists()
