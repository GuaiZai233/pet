from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path

from PIL import Image, ImageDraw


CELL_WIDTH = 192
CELL_HEIGHT = 208
PAW_DURATIONS = [160, 140, 140, 150, 300, 160, 180, 160]


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest().upper()


def checkerboard(size: tuple[int, int]) -> Image.Image:
    image = Image.new("RGBA", size, (244, 246, 248, 255))
    draw = ImageDraw.Draw(image)
    step = 16
    for y in range(0, size[1], step):
        for x in range(0, size[0], step):
            if (x // step + y // step) % 2:
                draw.rectangle((x, y, x + step - 1, y + step - 1), fill=(225, 230, 235, 255))
    return image


def build_icon(idle: Image.Image, destination: Path) -> None:
    bbox = idle.getchannel("A").getbbox()
    if bbox is None:
        raise ValueError("idle frame is empty")
    subject = idle.crop(bbox)
    canvas = Image.new("RGBA", (256, 256), (0, 0, 0, 0))
    subject.thumbnail((230, 230), Image.Resampling.LANCZOS)
    canvas.alpha_composite(subject, ((256 - subject.width) // 2, (256 - subject.height) // 2))
    canvas.save(destination, format="ICO", sizes=[(16, 16), (20, 20), (24, 24), (32, 32),
                                                   (40, 40), (48, 48), (64, 64),
                                                   (128, 128), (256, 256)])


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--source-webp", type=Path, required=True)
    paw_source = parser.add_mutually_exclusive_group(required=True)
    paw_source.add_argument("--paw-strip", type=Path)
    paw_source.add_argument("--paw-frames-dir", type=Path)
    parser.add_argument("--assets-dir", type=Path, required=True)
    parser.add_argument("--qa-dir", type=Path, required=True)
    args = parser.parse_args()

    args.assets_dir.mkdir(parents=True, exist_ok=True)
    args.qa_dir.mkdir(parents=True, exist_ok=True)

    main_atlas = Image.open(args.source_webp).convert("RGBA")
    if main_atlas.size != (1536, 2288):
        raise ValueError(f"unexpected main atlas size: {main_atlas.size}")
    main_png = args.assets_dir / "spritesheet.png"
    main_atlas.save(main_png, format="PNG", optimize=True)

    if args.paw_frames_dir is not None:
        paw = Image.new("RGBA", (CELL_WIDTH * 8, CELL_HEIGHT), (0, 0, 0, 0))
        for index in range(8):
            frame_path = args.paw_frames_dir / f"{index:02d}.png"
            frame = Image.open(frame_path).convert("RGBA")
            if frame.size != (CELL_WIDTH, CELL_HEIGHT):
                raise ValueError(f"unexpected paw frame size at {frame_path}: {frame.size}")
            paw.alpha_composite(frame, (index * CELL_WIDTH, 0))
    else:
        paw = Image.open(args.paw_strip).convert("RGBA")
    if paw.size != (CELL_WIDTH * 8, CELL_HEIGHT):
        raise ValueError(f"unexpected paw strip size: {paw.size}")
    paw_png = args.assets_dir / "paw-glass.png"
    paw.save(paw_png, format="PNG", optimize=True)

    frames: list[Image.Image] = []
    frame_report: list[dict[str, object]] = []
    for index in range(8):
        frame = paw.crop((index * CELL_WIDTH, 0, (index + 1) * CELL_WIDTH, CELL_HEIGHT))
        alpha = frame.getchannel("A")
        bbox = alpha.getbbox()
        if bbox is None:
            raise ValueError(f"paw frame {index} is empty")
        coverage = sum(1 for value in alpha.getdata() if value > 0)
        frame_report.append({"frame": index, "bbox": list(bbox), "visiblePixels": coverage})
        preview = checkerboard(frame.size)
        preview.alpha_composite(frame)
        frames.append(preview.convert("P", palette=Image.Palette.ADAPTIVE, colors=255))

    preview_path = args.qa_dir / "paw-glass-preview.gif"
    frames[0].save(preview_path, save_all=True, append_images=frames[1:], loop=0,
                   duration=PAW_DURATIONS, disposal=2)

    icon_path = args.assets_dir / "GuaiMiao.ico"
    build_icon(main_atlas.crop((0, 0, CELL_WIDTH, CELL_HEIGHT)), icon_path)

    report = {
        "ok": True,
        "cell": [CELL_WIDTH, CELL_HEIGHT],
        "mainAtlas": {"size": list(main_atlas.size), "sha256": sha256(main_png)},
        "pawStrip": {"size": list(paw.size), "sha256": sha256(paw_png), "frames": frame_report},
        "icon": {"sha256": sha256(icon_path)},
        "preview": {"sha256": sha256(preview_path), "durationsMs": PAW_DURATIONS},
    }
    (args.qa_dir / "asset-report.json").write_text(
        json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8"
    )
    print(json.dumps(report, ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
