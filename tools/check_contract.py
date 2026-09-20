"""Run T04's portable offline checks without building historical client scaffolds."""

import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def run(*args: str, timeout: int = 180) -> None:
    print("\n> " + " ".join(args), flush=True)
    subprocess.run(args, cwd=ROOT, check=True, timeout=timeout)


def main() -> None:
    run(sys.executable, "tools/check_foundation.py")
    run(sys.executable, "-m", "ruff", "check", "tests/contract", "tests/mocks")
    run(sys.executable, "-m", "ruff", "format", "--check", "tests/contract", "tests/mocks")
    run(
        sys.executable,
        "-m",
        "pytest",
        "tests/contract",
        "tests/mocks",
        "-o",
        "pythonpath=tests services/api",
        "--junitxml=.tmp/t04-tests.xml",
    )
    run(sys.executable, "tools/smoke_mock.py", "--report", ".tmp/t04-smoke.json", timeout=45)
    print(
        "\nT04 offline checks passed; production consumers and external acceptance stay separate."
    )


if __name__ == "__main__":
    main()
