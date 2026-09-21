"""Restore pinned official Unity assets, preserving every upstream GUID and meta file."""

import argparse
import gzip
import hashlib
import json
import shutil
import tarfile
import tempfile
import urllib.request
from pathlib import Path, PurePosixPath

ROOT = Path(__file__).resolve().parents[2]
SDK_VERSION = "5-r.4.1"
SDK_FILE = f"CubismSdkForUnity-{SDK_VERSION}.unitypackage"
SDK_URL = f"https://cubism.live2d.com/sdk-unity/bin/{SDK_FILE}"
SDK_SHA256 = "2777b69d4cd02fecd48dc0fe9871700c95943f6d141c18758027bd9aa2ed1de6"


def digest(path: Path) -> str:
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def acquire(url: str, destination: Path, expected: str) -> Path:
    destination.parent.mkdir(parents=True, exist_ok=True)
    if not destination.exists():
        temporary = destination.with_suffix(destination.suffix + ".partial")
        request = urllib.request.Request(
            url, headers={"User-Agent": "AICompanion-AssetRestore/1.0"}
        )
        with urllib.request.urlopen(request, timeout=120) as source, temporary.open("wb") as sink:
            shutil.copyfileobj(source, sink)
        if digest(temporary) != expected:
            raise ValueError(f"Hash mismatch for {url}; refusing to install")
        temporary.replace(destination)
    if digest(destination) != expected:
        raise ValueError(f"Hash mismatch for {destination}; refusing to install")
    return destination


def require_clean_sdk(project: Path) -> None:
    # Never overlay SDK generations: even a same-version import can contain generated
    # or locally modified assets. The caller must choose a clean directory explicitly.
    for target in (project / "Assets/Live2D", project / "Assets/Live2D.meta"):
        if target.exists() or target.is_symlink():
            raise ValueError(
                f"SDK destination already exists: {target}. Restore into a clean project; "
                "back up and remove the old Assets/Live2D and Assets/Live2D.meta first. "
                "This tool never overlays or deletes an existing SDK."
            )


def restore_sdk(archive: Path, project: Path) -> int:
    require_clean_sdk(project)
    if digest(archive) != SDK_SHA256:
        raise ValueError("SDK archive hash mismatch")
    # Unitypackage records are not ordered for pathname lookup. Decompress once so seeks
    # do not repeatedly inflate the complete 200 MB archive for thousands of assets.
    with tempfile.TemporaryFile() as unpacked:
        with gzip.open(archive, "rb") as source:
            shutil.copyfileobj(source, unpacked)
        unpacked.seek(0)
        with tarfile.open(fileobj=unpacked, mode="r:") as package:
            return restore_entries(package, project)


def restore_entries(package: tarfile.TarFile, project: Path) -> int:
    require_clean_sdk(project)
    count = 0
    if package:
        entries = {item.name.removeprefix("./"): item for item in package.getmembers()}
        validated = []
        paths = set()
        for key, member in entries.items():
            if not key.endswith("/pathname"):
                continue
            relative = PurePosixPath(package.extractfile(member).read().decode("utf-8"))
            # Our SDK assembly enables unsafe code locally; do not change all project assemblies.
            if str(relative) in ("Assets/csc.rsp", "Assets/mcs.rsp"):
                continue
            if relative.is_absolute() or ".." in relative.parts or "\\" in str(relative):
                raise ValueError(f"Unsafe package path: {relative}")
            if relative.parts[:2] != ("Assets", "Live2D"):
                raise ValueError(f"Unexpected package root: {relative}")
            target = project.joinpath(*relative.parts).resolve()
            if not target.is_relative_to((project / "Assets" / "Live2D").resolve()):
                raise ValueError(f"Package path escapes SDK directory: {relative}")
            if target in paths:
                raise ValueError(f"Duplicate package path: {relative}")
            paths.add(target)
            guid = key.rsplit("/", 1)[0]
            validated.append((target, guid))
        # Validate all destinations before writing any package payload.
        for target, guid in validated:
            payload = entries.get(guid + "/asset")
            if payload is not None and payload.isfile():
                target.parent.mkdir(parents=True, exist_ok=True)
                target.write_bytes(package.extractfile(payload).read())
            else:
                target.mkdir(parents=True, exist_ok=True)
            metadata = entries.get(guid + "/asset.meta")
            if metadata is not None:
                Path(str(target) + ".meta").write_bytes(package.extractfile(metadata).read())
            count += 1
    return count


def restore_font(cache: Path, project: Path) -> None:
    manifest = json.loads((ROOT / "assets/manifest/unity-foundation.json").read_text("utf-8"))
    font = manifest["font"]
    source = acquire(font["downloadUrl"], cache / font["fileName"], font["sha256"])
    license_path = acquire(
        font["licenseUrl"], cache / "Noto-OFL.txt", font["licenseFile"]["sha256"]
    )
    folder = project / "Assets/ThirdParty/Fonts"
    folder.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(source, folder / font["fileName"])
    shutil.copyfile(license_path, folder / "OFL.txt")
    for path in (folder.parent, folder, folder / font["fileName"], folder / "OFL.txt"):
        relative = path.relative_to(project).as_posix()
        guid = hashlib.sha256(relative.encode()).hexdigest()[:32]
        text = f"fileFormatVersion: 2\nguid: {guid}\n"
        if path.is_dir():
            text += "folderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n"
        elif path.suffix == ".otf":
            text += "TrueTypeFontImporter:\n  externalObjects: {}\n  fontSize: 16\n  includeFontData: 1\n"
        else:
            text += "TextScriptImporter:\n  externalObjects: {}\n"
        Path(str(path) + ".meta").write_text(text, encoding="utf-8", newline="\n")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--cache", type=Path, default=ROOT / ".bootstrap" / "unity")
    parser.add_argument("--project", type=Path, default=ROOT / "apps" / "unity")
    parser.add_argument("--sdk-package", type=Path)
    parser.add_argument("--accept-live2d-terms", action="store_true")
    args = parser.parse_args()
    if not args.accept_live2d_terms:
        parser.error(
            "Read assets/manifest/unity-foundation-resources.md and the linked Live2D terms, "
            "then pass --accept-live2d-terms. Download/use constitutes acceptance."
        )
    project = args.project.resolve()
    require_clean_sdk(project)
    archive = args.sdk_package or acquire(SDK_URL, args.cache / SDK_FILE, SDK_SHA256)
    count = restore_sdk(archive, project)
    restore_font(args.cache, project)
    print(json.dumps({"sdk": SDK_VERSION, "sha256": SDK_SHA256, "assets_restored": count}))


if __name__ == "__main__":
    main()
