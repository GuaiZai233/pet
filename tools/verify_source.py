from __future__ import annotations

import hashlib
import json
import re
from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "src" / "GuaiMiao"
ASSETS = SOURCE / "Assets"
EXPECTED_CODEX_HASH = "33eebf4e12a49ef0c50d3052f9ed718fa7e7b5a59995a0851c29de17ad7cfab4"


def hash_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def main() -> None:
    errors: list[str] = []
    checks: dict[str, object] = {}

    with Image.open(ASSETS / "spritesheet.png") as atlas_source:
        atlas = atlas_source.convert("RGBA")
    checks["mainAtlasSize"] = list(atlas.size)
    if atlas.size != (1536, 2288):
        errors.append(f"main atlas size is {atlas.size}")
    for column in range(6):
        cell = atlas.crop((column * 192, 8 * 208, (column + 1) * 192, 9 * 208))
        if cell.getchannel("A").getbbox() is None:
            errors.append(f"review frame {column} is empty")
    for column in (6, 7):
        cell = atlas.crop((column * 192, 8 * 208, (column + 1) * 192, 9 * 208))
        if cell.getchannel("A").getbbox() is not None:
            errors.append(f"unused review cell {column} is not transparent")

    with Image.open(ASSETS / "paw-glass.png") as paw_source:
        paw = paw_source.convert("RGBA")
    checks["pawAtlasSize"] = list(paw.size)
    if paw.size != (1536, 208):
        errors.append(f"paw atlas size is {paw.size}")
    for column in range(8):
        cell = paw.crop((column * 192, 0, (column + 1) * 192, 208))
        if cell.getchannel("A").getbbox() is None:
            errors.append(f"paw frame {column} is empty")

    with (ASSETS / "animations.json").open("r", encoding="utf-8") as stream:
        animation = json.load(stream)
    expected_frames = {
        "idle": 6, "running-right": 8, "running-left": 8, "waving": 4,
        "jumping": 5, "failed": 8, "waiting": 6, "running": 6,
        "review": 6, "paw-glass": 8,
    }
    checks["animationStates"] = len(animation["states"])
    for name, count in expected_frames.items():
        actual = animation["states"].get(name, {}).get("frames")
        if actual != count:
            errors.append(f"{name} frame count is {actual}, expected {count}")

    codex_hash = hash_file(ASSETS / "codex-spritesheet.webp")
    checks["codexAtlasSha256"] = codex_hash
    if codex_hash != EXPECTED_CODEX_HASH:
        errors.append("embedded Codex atlas hash mismatch")

    source_text = "\n".join(path.read_text("utf-8") for path in SOURCE.rglob("*.cs"))
    if EXPECTED_CODEX_HASH.upper() not in source_text:
        errors.append("AppInfo Codex atlas hash is not synchronized")
    allowed_urls = [
        "https://api.github.com/repos/GuaiZai233/pet/releases/latest",
        "https://github.com/GuaiZai233/pet",
        "https://github.com/GuaiZai233/pet/releases/download/",
    ]
    urls = sorted(set(re.findall(r"https?://[^\"\s]+", source_text)))
    checks["urls"] = urls
    if urls != allowed_urls:
        errors.append(f"unexpected URL set: {urls}")
    if "此形象由Codex生成，仅供个人使用！" not in source_text:
        errors.append("exact About text is missing")
    required_behaviors = [
        'MouseEnter', 'MouseLeave', 'PointerEntered', 'PointerExited',
        '"立即思考"', '"自动跑动（已开启）"', '"立即跑动（测试）"', 'AutoRunEnabled',
        '"开机启动（已开启）"', 'ExpectedCommand', 'key.Flush()',
        '_animator.Play("review"', 'PetInteractionPolicy.HoverPounceLoops', 'HoverInteractionGate',
        'DragStarted', 'DragMoved', 'DragDirectionTracker', 'IsPointerWithinWindowBounds',
        'LatestReleaseApiUrl', 'GitHubUpdateService', '"检查更新"', 'DownloadAsync',
        'SHA256.HashDataAsync', 'LaunchInstaller',
        'ScheduleAutoRun(runSoon: true)', 'settings-migrated schema=3', 'run-start source=',
        '"running-left"', '"running-right"', 'AutomaticDelayMinSeconds = 45',
        'ShouldUpgrade(incoming, installed, sameBinary)', 'FilesHaveSameSha256',
    ]
    checks["desktopBehaviors"] = required_behaviors
    for behavior in required_behaviors:
        if behavior not in source_text:
            errors.append(f"missing desktop behavior marker: {behavior}")

    project_text = (SOURCE / "GuaiMiao.csproj").read_text("utf-8")
    required_project_settings = [
        "<TargetFramework>net8.0-windows10.0.19041.0</TargetFramework>",
        "<RuntimeIdentifier>win-x64</RuntimeIdentifier>",
        "<PublishSingleFile>true</PublishSingleFile>",
        "<SelfContained>true</SelfContained>",
        "<PublishTrimmed>false</PublishTrimmed>",
    ]
    for setting in required_project_settings:
        if setting not in project_text:
            errors.append(f"missing project setting: {setting}")

    report = {
        "ok": not errors,
        "checks": checks,
        "errors": errors,
    }
    report_path = ROOT / "qa" / "source-verification.json"
    report_path.parent.mkdir(parents=True, exist_ok=True)
    report_path.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps(report, ensure_ascii=False, indent=2))
    raise SystemExit(0 if not errors else 1)


if __name__ == "__main__":
    main()
