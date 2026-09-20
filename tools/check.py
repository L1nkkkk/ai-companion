"""Run the same portable foundation checks locally and in CI."""

import os
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def run(*command: str) -> None:
    print("\n> " + " ".join(command), flush=True)
    subprocess.run(command, cwd=ROOT, check=True)


def main() -> None:
    pnpm = "pnpm.cmd" if os.name == "nt" else "pnpm"
    run(sys.executable, "tools/check_foundation.py")
    run(sys.executable, "tools/validate_blueprint.py", "--report", ".tmp/blueprint-report.json")
    run(sys.executable, "-m", "ruff", "check", "services/api", "tools")
    run(sys.executable, "-m", "ruff", "format", "--check", "services/api", "tools")
    run(sys.executable, "-m", "pytest")
    run(sys.executable, "tools/check_contract.py")
    run(pnpm, "typecheck")
    run(pnpm, "build:web")
    run(pnpm, "bundle:mobile")
    print("\nFoundation checks passed. Native builds and device behavior need separate evidence.")


if __name__ == "__main__":
    main()
