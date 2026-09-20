"""Detect toolchain drift and accidental edits to the frozen contract baseline."""

import hashlib
import json
import platform
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def main() -> None:
    errors: list[str] = []
    pins = json.loads((ROOT / "toolchain.json").read_text(encoding="utf-8"))
    node = subprocess.check_output(["node", "--version"], text=True).strip().removeprefix("v")
    if node != pins["node"]:
        errors.append(f"Node {node} != pinned {pins['node']}")
    if platform.python_version() != pins["python"]:
        errors.append(f"Python {platform.python_version()} != pinned {pins['python']}")
    root_package = json.loads((ROOT / "package.json").read_text(encoding="utf-8"))
    if root_package["packageManager"] != "pnpm@" + pins["pnpm"]:
        errors.append("packageManager and toolchain pins disagree")
    for package in [
        ROOT / "package.json",
        *ROOT.glob("apps/*/package.json"),
        *ROOT.glob("packages/*/package.json"),
    ]:
        metadata = json.loads(package.read_text(encoding="utf-8"))
        if metadata.get("private") is not True:
            errors.append(f"Workspace package must remain private: {package.relative_to(ROOT)}")
        for group in ("dependencies", "devDependencies"):
            for name, version in metadata.get(group, {}).items():
                if version.startswith(("^", "~", ">", "<", "*")) or version == "latest":
                    errors.append(
                        f"Direct dependency is not pinned: {name} in {package.relative_to(ROOT)}"
                    )
    frozen = json.loads((ROOT / "contracts/baseline.json").read_text(encoding="utf-8"))
    for relative, expected in frozen["sha256"].items():
        path = ROOT / relative
        if not path.is_file():
            errors.append(f"Frozen contract file is missing: {relative}")
            continue
        actual = hashlib.sha256(path.read_bytes().replace(b"\r\n", b"\n")).hexdigest()
        if actual != expected:
            errors.append(
                f"Frozen contract changed: {relative}; A0 must record an ADR and update the baseline"
            )
    if (ROOT / "docs/blueprint/contracts").exists():
        errors.append("There must be only one authoritative contracts directory")
    for required in (
        "pnpm-lock.yaml",
        "uv.lock",
        "AGENTS.md",
        "apps/mobile/ios/Podfile",
        "apps/mobile/android/gradlew",
    ):
        if not (ROOT / required).is_file():
            errors.append(f"Missing foundation file: {required}")
    if errors:
        raise SystemExit("\n".join(errors))
    print(f"Pinned runtimes and {len(frozen['sha256'])} frozen contract files checked.")


if __name__ == "__main__":
    main()
