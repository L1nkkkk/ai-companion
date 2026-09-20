"""Exercise a fresh T04 server over real loopback HTTP and both WebSockets."""

import argparse
import json
import os
import socket
import subprocess
import sys
import time
from datetime import UTC, datetime
from pathlib import Path
from uuid import uuid4

import httpx
from websockets.sync.client import connect

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tests"))
from mocks.protocol import decode_frame, validate_event  # noqa: E402


def command(session: dict, kind: str, payload: dict, seq: int) -> dict:
    return {
        "schema_version": "1.0",
        "type": kind,
        "event_id": str(uuid4()),
        "session_id": session["session_id"],
        "session_epoch": session["session_epoch"],
        "device_id": session["device_id"],
        "client_seq": seq,
        "timestamp": datetime.now(UTC).isoformat().replace("+00:00", "Z"),
        "payload": payload,
    }


def receive_until(ws, kind: str) -> list[dict]:
    events = []
    deadline = time.monotonic() + 8
    while len(events) < 100 and time.monotonic() < deadline:
        event = json.loads(ws.recv(timeout=max(0.01, deadline - time.monotonic())))
        validate_event(event, "server")
        events.append(event)
        if event["type"] == "error":
            raise AssertionError(f"Unexpected mock error: {event['payload']['code']}")
        if event["type"] == kind:
            return events
    raise AssertionError(f"No {kind} within bounded event window")


def post(client: httpx.Client, path: str, body: dict) -> dict:
    response = client.post(path, json=body)
    response.raise_for_status()
    return response.json()


def exercise(base: str) -> dict:
    with httpx.Client(base_url=base, timeout=5, trust_env=False) as client:
        health = client.get("/health/live").raise_for_status().json()
        assert health["mode"] == "mock"
        session = post(client, "/mock/sessions", {"mode": "personal"})
        ws_base = base.replace("http:", "ws:")
        control_url = ws_base + session["control_path"] + "?ticket=" + session["control_ticket"]
        audio_url = ws_base + session["audio_path"] + "?ticket=" + session["audio_ticket"]
        with connect(control_url, proxy=None, open_timeout=5) as control:
            receive_until(control, "session.snapshot")
            with connect(audio_url, proxy=None, open_timeout=5) as audio:
                submit = command(
                    session, "input.text.submit", {"text": "离线你好", "source": "user"}, 1
                )
                control.send(json.dumps(submit, ensure_ascii=False))
                events = receive_until(control, "turn.generation.completed")
                segment = next(e for e in events if e["type"] == "assistant.segment")
                end = next(e for e in events if e["type"] == "assistant.audio.end")
                assert all(e["type"] != "turn.completed" for e in events)
                assert len({e["seq"] for e in events}) == len(events)
                samples = 0
                frames = 0
                while samples < end["payload"]["total_samples"]:
                    frame = decode_frame(audio.recv(timeout=5))
                    assert frame.header.kind == 2
                    assert str(frame.header.stream_id) == segment["payload"]["stream_id"]
                    assert str(frame.header.turn_id) == segment["turn_id"]
                    assert frame.header.session_epoch == session["session_epoch"]
                    assert frame.header.frame_seq == frames
                    assert frame.header.offset_samples == samples
                    samples += frame.sample_count
                    frames += 1
                assert samples == end["payload"]["total_samples"] > 0
                progress = command(
                    session,
                    "playback.progress",
                    {
                        "turn_id": segment["turn_id"],
                        "stream_id": segment["payload"]["stream_id"],
                        "segment_index": segment["payload"]["segment_index"],
                        "played_samples": samples,
                        "status": "completed",
                    },
                    2,
                )
                control.send(json.dumps(progress))
                receive_until(control, "turn.completed")
                # A retry must not allocate a second turn. The event ID remains identical.
                control.send(json.dumps(submit, ensure_ascii=False))
                for _ in range(4):
                    ack = receive_until(control, "command.ack")[-1]
                    if ack["payload"]["related_event_id"] == submit["event_id"]:
                        break
                else:
                    raise AssertionError("No acknowledgement for the repeated command")
                assert ack["payload"]["status"] == "duplicate"
                assert ack["payload"]["result_turn_id"] == segment["turn_id"]

        cancelled = post(client, "/mock/sessions", {"mode": "personal"})
        post(
            client,
            f"/mock/sessions/{cancelled['session_id']}/faults",
            {"generation_delay_ms": 1000},
        )
        url = ws_base + cancelled["control_path"] + "?ticket=" + cancelled["control_ticket"]
        with connect(url, proxy=None, open_timeout=5) as control:
            receive_until(control, "session.snapshot")
            control.send(
                json.dumps(
                    command(
                        cancelled, "input.text.submit", {"text": "取消测试", "source": "user"}, 1
                    )
                )
            )
            started = receive_until(control, "turn.started")[-1]
            start = time.perf_counter()
            control.send(
                json.dumps(
                    command(
                        cancelled,
                        "turn.cancel",
                        {"turn_id": started["turn_id"], "reason": "user_stop"},
                        2,
                    )
                )
            )
            received = receive_until(control, "turn.cancelled")
            cancel_ms = (time.perf_counter() - start) * 1000
            assert all(e["type"] != "assistant.segment" for e in received)
            assert cancel_ms < 1000, "Cancellation was blocked behind injected generation delay"

        broadcast = post(client, "/mock/sessions", {"mode": "broadcast"})
        path = f"/mock/sessions/{broadcast['session_id']}"
        url = ws_base + broadcast["control_path"] + "?ticket=" + broadcast["control_ticket"]
        with connect(url, proxy=None, open_timeout=5) as control:
            receive_until(control, "session.snapshot")
            control.send(json.dumps(command(broadcast, "live.auto_reply", {"enabled": False}, 7)))
            receive_until(control, "command.ack")
            comment = {"text": "MOCK 本机测试弹幕", "event_id": "tcp-smoke-comment"}
            first = post(client, path + "/live", comment)
            receive_until(control, "live.queue")
            repeated = post(client, path + "/live", comment)
            receive_until(control, "live.queue")
            assert first["event"]["platform"] == "mock"
            assert first["accepted"] is True and repeated["accepted"] is False
            assert repeated["queue"]["size"] == 1
            post(client, path + "/disconnect", {})
        ticket = post(
            client, path + "/tickets", {"device_id": broadcast["device_id"], "channel": "control"}
        )
        url = ws_base + broadcast["control_path"] + "?ticket=" + ticket["ticket"]
        with connect(url, proxy=None, open_timeout=5) as control:
            recovered = receive_until(control, "session.snapshot")[-1]
            assert recovered["session_epoch"] == broadcast["session_epoch"] + 1
            assert recovered["payload"]["active_turn_id"] is None
            broadcast["session_epoch"] = recovered["session_epoch"]
            control.send(json.dumps(command(broadcast, "live.auto_reply", {"enabled": False}, 1)))
            receive_until(control, "command.ack")
        return {
            "status": "passed",
            "mode": "mock",
            "transport": "real loopback TCP HTTP + separate control/audio WebSockets",
            "health": health,
            "control_event_types": [e["type"] for e in events],
            "audio_frames": frames,
            "audio_samples": samples,
            "audio_payload_bytes": samples * 2,
            "duplicate_command": "same turn, duplicate acknowledgement",
            "cancel_with_generation_delay_ms": 1000,
            "cancel_ack_elapsed_ms": round(cancel_ms, 3),
            "live_duplicate_queue_size": repeated["queue"]["size"],
            "injected_disconnect_new_epoch": recovered["session_epoch"],
            "new_connection_client_seq_one": "accepted",
            "playback_report": "synthetic test consumer acknowledgement; no physical audio playback",
        }


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--report", type=Path, default=ROOT / ".tmp/t04-smoke.json")
    args = parser.parse_args()
    started = datetime.now(UTC).isoformat()
    with socket.socket() as reserved:
        reserved.bind(("127.0.0.1", 0))
        port = reserved.getsockname()[1]
    log_path = ROOT / ".tmp" / f"t04-smoke-{uuid4().hex}.log"
    log_path.parent.mkdir(parents=True, exist_ok=True)
    with log_path.open("w+", encoding="utf-8") as log:
        process = subprocess.Popen(
            [sys.executable, str(ROOT / "tools/run_mock.py"), "--port", str(port)],
            cwd=ROOT,
            stdout=log,
            stderr=subprocess.STDOUT,
            creationflags=subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0,
        )
        try:
            deadline = time.monotonic() + 12
            base = f"http://127.0.0.1:{port}"
            with httpx.Client(timeout=0.5, trust_env=False) as probe:
                while True:
                    if process.poll() is not None:
                        log.seek(0)
                        raise RuntimeError("Mock failed to start: " + log.read())
                    try:
                        if probe.get(base + "/health/live").status_code == 200:
                            break
                    except httpx.HTTPError:
                        pass
                    if time.monotonic() >= deadline:
                        raise TimeoutError("Mock startup exceeded 12 seconds")
                    time.sleep(0.05)
            result = exercise(base)
            result["started_at"] = started
            result["finished_at"] = datetime.now(UTC).isoformat()
        finally:
            if process.poll() is None:
                if os.name == "nt":
                    # Windows venv launchers may have an interpreter child. Kill only our PID tree.
                    subprocess.run(
                        ["taskkill", "/PID", str(process.pid), "/T", "/F"],
                        stdout=subprocess.DEVNULL,
                        stderr=subprocess.DEVNULL,
                        timeout=10,
                        check=False,
                    )
                else:
                    process.terminate()
                try:
                    process.wait(timeout=5)
                except subprocess.TimeoutExpired:
                    process.kill()
                    process.wait(timeout=5)
    assert "?ticket=" not in log_path.read_text("utf-8"), "Server log exposed a channel ticket"
    result["server_log_ticket_values"] = "absent"
    args.report.parent.mkdir(parents=True, exist_ok=True)
    args.report.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", "utf-8")
    print(json.dumps(result, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
