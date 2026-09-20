import copy
import json
from pathlib import Path
from uuid import NAMESPACE_URL, uuid5

import pytest

ROOT = Path(__file__).resolve().parents[2]
SESSION = "11111111-1111-4111-8111-111111111111"
TURN = "33333333-3333-4333-8333-333333333333"
STREAM = "44444444-4444-4444-8444-444444444444"
DEVICE = "22222222-2222-4222-8222-222222222222"


class ManualClock:
    def __init__(self):
        self.value = 0.0

    def __call__(self):
        return self.value

    def advance(self, seconds):
        self.value += seconds


class Events:
    def __init__(self):
        self.seq = 0
        self.samples = {
            e["type"]: e
            for e in json.loads((ROOT / "contracts/examples/server-valid.json").read_text("utf-8"))
        }

    def __call__(self, kind, *, turn=TURN, epoch=1, **payload):
        self.seq += 1
        result = copy.deepcopy(self.samples[kind])
        result.update(
            seq=self.seq,
            session_epoch=epoch,
            event_id=str(uuid5(NAMESPACE_URL, f"T04:test:{self.seq}:{kind}:{epoch}")),
        )
        if result["turn_id"] is not None:
            result["turn_id"] = turn
        result["payload"].update(payload)
        return result


@pytest.fixture
def clock():
    return ManualClock()


@pytest.fixture
def events():
    return Events()
