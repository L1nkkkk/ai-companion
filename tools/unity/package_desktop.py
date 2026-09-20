"""Package the existing Unity Player plus a frozen portable Python preview backend."""

from __future__ import annotations

import argparse
import datetime
import hashlib
import importlib.metadata
import json
import os
import shutil
import subprocess
import zipfile
from pathlib import Path

from packaging.markers import default_environment
from packaging.requirements import Requirement
from packaging.utils import canonicalize_name

REPOSITORY = Path(__file__).resolve().parents[2]
DEFAULT_PYTHON = Path(
    "C:/Users/Link/Dev/ai-companion-dev-tools/toolchain/python/cpython-3.12.10-windows-x86_64-none"
)
SKIP_NAMES = {
    "__pycache__",
    ".pytest_cache",
    ".DS_Store",
    "config.json",
    "config.pending",
    "launcher.log",
    "launcher.lock",
}


def excluded(name: str) -> bool:
    return name in SKIP_NAMES or name.endswith((".pyc", ".pyo"))


def copy_tree(source: Path, destination: Path, *, runtime: bool = False) -> None:
    destination.mkdir(parents=True, exist_ok=True)
    for child in source.iterdir():
        if excluded(child.name) or (runtime and child.name == "site-packages"):
            continue
        if child.is_symlink():
            raise ValueError("Package sources must not contain symlinks")
        if child.is_dir():
            copy_tree(child, destination / child.name, runtime=runtime)
        else:
            shutil.copy2(child, destination / child.name)


def runtime_dependencies(site: Path) -> list[importlib.metadata.Distribution]:
    available = {
        canonicalize_name(dist.metadata["Name"]): dist
        for dist in importlib.metadata.distributions(path=[str(site)])
    }
    required: dict[str, set[str]] = {}
    pending = [Requirement("fastapi==0.141.1"), Requirement("uvicorn==0.53.0")]
    environment = default_environment()
    environment.update(
        python_version="3.12",
        python_full_version="3.12.10",
        sys_platform="win32",
        os_name="nt",
        platform_system="Windows",
        platform_machine="AMD64",
        implementation_name="cpython",
        platform_python_implementation="CPython",
    )
    while pending:
        requirement = pending.pop()
        name = canonicalize_name(requirement.name)
        distribution = available.get(name)
        if distribution is None or not requirement.specifier.contains(distribution.version):
            raise ValueError(f"Frozen runtime dependency is missing or incompatible: {name}")
        extras = set(requirement.extras) | {""}
        unseen = extras - required.get(name, set())
        if not unseen:
            continue
        required.setdefault(name, set()).update(extras)
        for item in distribution.requires or []:
            dependency = Requirement(item)
            if dependency.marker is None or any(
                dependency.marker.evaluate({**environment, "extra": extra}) for extra in unseen
            ):
                pending.append(dependency)
    return [available[name] for name in sorted(required)]


def copy_distribution(distribution, site: Path, destination: Path) -> None:
    if not distribution.files:
        raise ValueError("Distribution RECORD is missing")
    for entry in distribution.files:
        relative = Path(entry)
        if (
            relative.is_absolute()
            or ".." in relative.parts
            or any(excluded(part) for part in relative.parts)
        ):
            continue  # Console-script wrappers outside site-packages are not runtime imports.
        source = site / relative
        if not source.is_file():
            raise ValueError(f"Distribution file is missing: {distribution.metadata['Name']}")
        if source.is_symlink():
            raise ValueError("Distribution symlinks are unsupported")
        target = destination / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(source, target)


def digest(path: Path) -> str:
    with path.open("rb") as handle:
        return hashlib.file_digest(handle, "sha256").hexdigest()


def manifest_files(directory: Path) -> list[dict]:
    return [
        {
            "path": path.relative_to(directory).as_posix(),
            "bytes": path.stat().st_size,
            "sha256": digest(path),
        }
        for path in sorted(directory.rglob("*"))
        if path.is_file()
    ]


def package_desktop(
    player: Path, output: Path, python_runtime: Path, site: Path, source_sha: str | None = None
) -> dict:
    player, output, python_runtime, site = (
        path.resolve() for path in (player, output, python_runtime, site)
    )
    if not (player / "NeuroSaki.exe").is_file() or not (player / "NeuroSaki_Data").is_dir():
        raise ValueError("A complete existing Windows NeuroSaki Player is required")
    if output.exists():
        raise ValueError(
            "Package output must be a new directory; no existing directory is overwritten"
        )
    if any(
        output == source or output.is_relative_to(source)
        for source in (player, python_runtime, site)
    ):
        raise ValueError("Package output must not be inside its input directories")
    archive = output.with_suffix(".zip")
    evidence = output.with_name(output.name + "-package-manifest.json")
    if archive.exists() or evidence.exists():
        raise ValueError("Package ZIP or manifest already exists")
    for name in ("python.exe", "pythonw.exe", "python312.dll", "LICENSE.txt"):
        if not (python_runtime / name).is_file():
            raise ValueError("The fixed standalone Python runtime is incomplete")
    version = subprocess.check_output(
        [
            str(python_runtime / "python.exe"),
            "-I",
            "-c",
            "import sys; print('.'.join(map(str,sys.version_info[:3])))",
        ],
        text=True,
    ).strip()
    if version != "3.12.10":
        raise ValueError("The portable runtime must be Python 3.12.10")
    dependencies = runtime_dependencies(site)
    if source_sha is None:
        source_sha = subprocess.check_output(
            ["git", "-C", str(REPOSITORY), "rev-parse", "HEAD"], text=True
        ).strip()
    dirty = bool(
        subprocess.check_output(
            ["git", "-C", str(REPOSITORY), "status", "--porcelain"], text=True
        ).strip()
    )
    copy_tree(player, output)
    copy_tree(python_runtime, output / "python", runtime=True)
    for distribution in dependencies:
        copy_distribution(distribution, site, output / "python/Lib/site-packages")
    module = REPOSITORY / "services/api/app/unity_preview"
    copy_tree(module, output / "backend/app/unity_preview")
    (output / "backend/app/__init__.py").write_text(
        '"""Portable desktop preview package."""\n', encoding="utf-8"
    )
    shutil.copy2(Path(__file__).with_name("launch_desktop.py"), output / "launch_desktop.py")
    (output / "Start-NeuroSaki.cmd").write_text(
        '@echo off\r\nsetlocal\r\ncd /d "%~dp0"\r\nstart "" "%~dp0python\\pythonw.exe" -B "%~dp0launch_desktop.py"\r\n',
        encoding="ascii",
    )
    licenses = output / "licenses"
    licenses.mkdir(exist_ok=True)
    resources = {
        "apps/unity/Assets/Live2D/Cubism/LICENSE.md": "LIVE2D-LICENSE.md",
        "apps/unity/Assets/Live2D/Cubism/NOTICE.md": "LIVE2D-NOTICE.md",
        "apps/unity/Assets/Live2D/Cubism/Plugins/LICENSE.md": "LIVE2D-CORE-LICENSE.md",
        "apps/unity/Assets/Live2D/Cubism/Plugins/RedistributableFiles.txt": "LIVE2D-REDISTRIBUTABLES.txt",
        "apps/unity/Assets/ThirdParty/Fonts/OFL.txt": "FONT-OFL.txt",
        "apps/unity/Assets/TextMesh Pro/Fonts/LiberationSans - OFL.txt": "LIBERATION-SANS-OFL.txt",
        "assets/manifest/unity-foundation-resources.md": "RESOURCE-NOTICES.md",
        "services/api/app/unity_preview/fixtures/SOURCE.md": "TEST-AUDIO-SOURCE.md",
    }
    for source, name in resources.items():
        original = REPOSITORY / source
        if not original.is_file():
            raise ValueError(f"Required resource notice is missing: {name}")
        shutil.copy2(original, licenses / name)
    (output / "README-开始使用.txt").write_text(
        "Neuro-Saki Windows 桌面基础功能预览\n\n"
        "完整解压后双击 Start-NeuroSaki.cmd。无需安装 Python、PowerShell 或 Unity。\n"
        "输入文字后显示演示回复，并播放有静音段的测试音频；这不是 AI/TTS 生成结果。\n"
        "可调音量、停止、查看/导出/删除本机历史。此版本未启用真实云对话和麦克风。\n"
        "关闭窗口会退出本次后台。运行时令牌只在当前用户 LocalAppData/NeuroSaki/preview-runtime 下。\n"
        "如果启动失败，请按提示处理，并查看同目录 launcher.log；不要分享 config.json。\n"
        "保留完整文件夹；不要只复制 exe。若提示 8000 端口占用，请先关闭占用服务。\n"
        "角色、字体、Python 和依赖许可见 licenses、python/LICENSE.txt 及 python/Lib/site-packages/*.dist-info。\n"
        "This content uses sample data owned and copyrighted by Live2D Inc. The sample data are utilized in accordance with terms and conditions set by Live2D Inc. This content itself is created at the author’s sole discretion.\n",
        encoding="utf-8",
    )
    # Verify imports with the copied interpreter and copied site-packages before archiving.
    environment = dict(os.environ)
    environment.pop("PYTHONHOME", None)
    environment["PYTHONPATH"] = str(output / "backend")
    environment["PYTHONNOUSERSITE"] = "1"
    environment["PYTHONDONTWRITEBYTECODE"] = "1"
    subprocess.run(
        [
            str(output / "python/python.exe"),
            "-B",
            "-c",
            "import fastapi, uvicorn; from app.unity_preview import PreviewSettings; print('Portable imports verified')",
        ],
        cwd=output,
        env=environment,
        check=True,
    )
    manifest = {
        "format": 1,
        "created_utc": datetime.datetime.now(datetime.UTC).isoformat(),
        "source_sha": source_sha,
        "source_dirty": dirty,
        "python": version,
        "unity": "2022.3.62f3c1",
        "cubism": "5-r.4.1",
        "mode": "fixture",
        "runtime_dependencies": [
            {"name": dist.metadata["Name"], "version": dist.version} for dist in dependencies
        ],
        "files": manifest_files(output),
        "manifest_self_exclusion": "package-manifest.json excludes itself; outer manifest includes its hash.",
    }
    (output / "package-manifest.json").write_text(
        json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8"
    )
    with zipfile.ZipFile(archive, "x", compression=zipfile.ZIP_DEFLATED, compresslevel=6) as zipped:
        for path in sorted(output.rglob("*")):
            if path.is_file():
                zipped.write(path, Path(output.name) / path.relative_to(output))
    result = {
        **manifest,
        "package_directory": str(output),
        "zip": str(archive),
        "zip_sha256": digest(archive),
        "package_manifest_sha256": digest(output / "package-manifest.json"),
    }
    evidence.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    return {
        "package_directory": str(output),
        "zip": str(archive),
        "manifest": str(evidence),
        "zip_sha256": result["zip_sha256"],
        "dependencies": len(dependencies),
    }


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--player-directory", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--python-runtime", type=Path, default=DEFAULT_PYTHON)
    parser.add_argument(
        "--site-packages", type=Path, default=REPOSITORY / ".venv/Lib/site-packages"
    )
    parser.add_argument("--source-sha")
    arguments = parser.parse_args()
    print(
        json.dumps(
            package_desktop(
                arguments.player_directory,
                arguments.output,
                arguments.python_runtime,
                arguments.site_packages,
                arguments.source_sha,
            ),
            ensure_ascii=False,
        )
    )


if __name__ == "__main__":
    main()
