"""Bounded, explicitly synthetic live comments for offline adapter development."""

import time
from collections import OrderedDict
from datetime import UTC, datetime
from uuid import uuid4


class MockLive:
    platform = "mock"
    capabilities = {
        "ordinary_comments": True,
        "command_comments": False,
        "viewer_identifiers": True,
        "gifts": False,
        "real_platform": False,
    }

    def __init__(self):
        self.connection_id = str(uuid4())
        self.queue: OrderedDict[str, tuple[float, dict]] = OrderedDict()
        self.seen: OrderedDict[str, float] = OrderedDict()
        self.dropped = 0
        self.selected: str | None = None
        self.auto_reply = False

    def expire(self):
        now = time.monotonic()
        for key, (at, _) in list(self.queue.items()):
            if now - at >= 30:
                self.queue.pop(key)
                self.dropped += 1
        while self.seen and (now - next(iter(self.seen.values())) >= 600 or len(self.seen) > 1000):
            self.seen.popitem(last=False)

    def add(self, text: str, event_id: str | None = None) -> tuple[dict, bool]:
        self.expire()
        key = event_id or str(uuid4())
        timestamp = datetime.now(UTC).isoformat().replace("+00:00", "Z")
        event = {
            "platform": "mock",
            "connection_id": self.connection_id,
            "platform_event_id": key,
            "received_at": timestamp,
            "occurred_at": timestamp,
            "type": "comment",
            "viewer_id": "mock-viewer",
            "display_name": "MOCK 观众",
            "text": text,
            "metadata": {"mock": True, "source": "test-only injection"},
        }
        if key in self.seen:
            return event, False
        self.seen[key] = time.monotonic()
        if len(self.queue) == 50:
            self.queue.popitem(last=False)
            self.dropped += 1
        self.queue[key] = (time.monotonic(), event)
        return event, True

    def select(self, event_id: str) -> dict | None:
        self.expire()
        selected = self.queue.pop(event_id, None)
        if selected:
            self.selected = event_id
            return selected[1]
        return None

    def snapshot(self) -> dict:
        self.expire()
        return {
            "size": len(self.queue),
            "dropped_total": self.dropped,
            "selected_event_id": self.selected,
        }
