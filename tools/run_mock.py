"""Start the explicitly offline T04 fixture service on loopback only."""

import argparse
import sys
from pathlib import Path

import uvicorn

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tests"))


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--port", type=int, default=8765)
    args = parser.parse_args()
    if not 1 <= args.port <= 65535:
        parser.error("port must be in 1..65535")
    print("T04 MOCK ONLY: no cloud, no real microphone, no real live platform.", flush=True)
    uvicorn.run(
        "mocks.server:app",
        host="127.0.0.1",
        port=args.port,
        ws="websockets-sansio",
        ws_max_size=65536,
        ws_max_queue=16,
        proxy_headers=False,
        access_log=False,
        # Uvicorn's WebSocket handshake INFO messages include the ticket query string.
        log_level="warning",
        workers=1,
    )


if __name__ == "__main__":
    main()
