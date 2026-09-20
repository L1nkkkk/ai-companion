"""Verify pinned SDK bytes and exercise the official Windows x64 Core/model API.

This is a native asset probe, not a Unity import, rendering, or Player test.
It uses only Python's standard library and never makes network requests.
"""

import argparse
import ctypes as ct
import hashlib
import json
import platform
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
PTR = ct.c_void_p
UINT = ct.c_uint32
INT = ct.c_int
FLOATS = ct.POINTER(ct.c_float)


def sha256(path: Path) -> str:
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def verify_file(path: Path, expected: str) -> str:
    actual = sha256(path)
    if actual != expected.lower():
        raise ValueError(f"Hash mismatch; refusing to load {path.name}")
    return actual


def aligned_buffer(size: int, alignment: int):
    storage = ct.create_string_buffer(size + alignment - 1)
    address = (ct.addressof(storage) + alignment - 1) & ~(alignment - 1)
    return storage, address


def inspect(project: Path, manifest: dict) -> dict:
    if platform.system() != "Windows" or ct.sizeof(PTR) != 8:
        raise RuntimeError("The pinned probe requires Windows and 64-bit Python")
    core_asset = manifest["core"]["file"]
    dll_path = project / core_asset["path"]
    core_hash = verify_file(dll_path, core_asset["sha256"])
    model_manifest = manifest["model"]
    model_json_path = model_manifest["model3Json"]
    model_file = next(
        row for row in model_manifest["filesIncludingMeta"] if row["path"] == model_json_path
    )
    verify_file(project / model_json_path, model_file["sha256"])
    model_json = json.loads((project / model_json_path).read_text(encoding="utf-8"))
    moc_path = Path(model_json_path).parent / model_json["FileReferences"]["Moc"]
    moc_file = next(
        row for row in model_manifest["filesIncludingMeta"] if row["path"] == moc_path.as_posix()
    )
    moc_hash = verify_file(project / moc_path, moc_file["sha256"])
    library = ct.CDLL(str(dll_path.resolve()))

    def api(name, result, arguments):
        function = getattr(library, name)
        function.restype = result
        function.argtypes = arguments
        return function

    version = api("csmGetVersion", UINT, [])()
    if version != manifest["core"]["versionUInt32"]:
        raise RuntimeError(f"Unexpected Core version {version:#010x}")
    data = (project / moc_path).read_bytes()
    # Keep both backing buffers alive for every call into Core.
    moc_storage, moc_address = aligned_buffer(len(data), 64)
    ct.memmove(moc_address, data, len(data))
    consistency = api("csmHasMocConsistency", INT, [PTR, UINT])(moc_address, len(data))
    if consistency != 1:
        raise RuntimeError("Official Core rejected model consistency")
    moc = api("csmReviveMocInPlace", PTR, [PTR, UINT])(moc_address, len(data))
    if not moc:
        raise RuntimeError("Core failed to revive the model")
    model_size = api("csmGetSizeofModel", UINT, [PTR])(moc)
    model_storage, model_address = aligned_buffer(model_size, 16)
    model = api("csmInitializeModelInPlace", PTR, [PTR, PTR, UINT])(moc, model_address, model_size)
    if not model:
        raise RuntimeError("Core failed to initialize the model")
    count = api("csmGetParameterCount", INT, [PTR])(model)
    identifiers = api("csmGetParameterIds", ct.POINTER(ct.c_char_p), [PTR])(model)
    minimums = api("csmGetParameterMinimumValues", FLOATS, [PTR])(model)
    maximums = api("csmGetParameterMaximumValues", FLOATS, [PTR])(model)
    defaults = api("csmGetParameterDefaultValues", FLOATS, [PTR])(model)
    values = api("csmGetParameterValues", FLOATS, [PTR])(model)
    parameters = [
        {
            "id": identifiers[index].decode("utf-8"),
            "min": minimums[index],
            "max": maximums[index],
            "default": defaults[index],
        }
        for index in range(count)
    ]
    indices = {row["id"]: index for index, row in enumerate(parameters)}
    drawable_count = api("csmGetDrawableCount", INT, [PTR])(model)
    masks = api("csmGetDrawableMaskCounts", ct.POINTER(INT), [PTR])(model)
    flags = api("csmGetDrawableConstantFlags", ct.POINTER(ct.c_ubyte), [PTR])(model)
    vertex_counts = api("csmGetDrawableVertexCounts", ct.POINTER(INT), [PTR])(model)
    positions = api("csmGetDrawableVertexPositions", ct.POINTER(FLOATS), [PTR])(model)
    update = api("csmUpdateModel", None, [PTR])

    def snapshot():
        return [
            tuple(positions[index][axis] for axis in range(vertex_counts[index] * 2))
            for index in range(drawable_count)
        ]

    update(model)
    baseline = snapshot()
    deformations = []
    for parameter_id, target in (
        ("ParamA", 1.0),
        ("ParamEyeLOpen", 0.0),
        ("ParamEyeROpen", 0.0),
        ("ParamBreath", 1.0),
    ):
        if parameter_id not in indices:
            raise RuntimeError(f"Missing required model parameter {parameter_id}")
        for index in range(count):
            values[index] = defaults[index]
        values[indices[parameter_id]] = target
        update(model)
        changed = sum(before != after for before, after in zip(baseline, snapshot()))
        if changed == 0:
            raise RuntimeError(f"No geometry changed when exercising {parameter_id}")
        deformations.append(
            {"parameter": parameter_id, "value": target, "changedDrawableCount": changed}
        )
    # This explicitly separates successful native evaluation from GPU rendering.
    return {
        "status": "passed",
        "testedAtUtc": datetime.now(timezone.utc).isoformat(),
        "scope": "Official Core/model public API and deformations; no Unity/Player rendering",
        "python": platform.python_version(),
        "platform": platform.platform(),
        "coreVersion": version,
        "coreVersionHex": f"0x{version:08x}",
        "coreVersionFormatted": f"{version >> 24:02d}.{(version >> 16) & 255:02d}.{version & 65535:04d}",
        "coreSha256": core_hash,
        "mocSha256": moc_hash,
        "mocConsistency": consistency,
        "parameterCount": count,
        "drawableCount": drawable_count,
        "maskedDrawableCount": sum(masks[index] > 0 for index in range(drawable_count)),
        "invertedMaskDrawableCount": sum(bool(flags[index] & 8) for index in range(drawable_count)),
        "deformationProbes": deformations,
        "parameters": parameters,
        "notVerified": ["Unity compilation", "URP material or mask appearance", "Windows Player"],
    }


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--project", type=Path, default=ROOT / "apps" / "unity")
    parser.add_argument(
        "--manifest", type=Path, default=ROOT / "assets" / "manifest" / "unity-foundation.json"
    )
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    manifest = json.loads(args.manifest.read_text(encoding="utf-8"))
    try:
        report = inspect(args.project.resolve(), manifest)
    except (OSError, ValueError, RuntimeError, KeyError, StopIteration) as error:
        report = {"status": "failed", "error": str(error)}
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
        raise SystemExit(str(error)) from error
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({key: value for key, value in report.items() if key != "parameters"}))


if __name__ == "__main__":
    main()
