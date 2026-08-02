#!/usr/bin/env python3
"""Build the V17 five-state coin from a transparent, front-facing master."""

from __future__ import annotations

import json
from pathlib import Path

import numpy as np
from PIL import Image, ImageEnhance, ImageOps


ROOT = Path(__file__).resolve().parents[1]
CELL = 256
SAFE_MARGIN = 20
BASE = ROOT / "AssetSources" / "v16" / "base" / "pupu-gaze-coin-youthful-v15.png"
FRONT_MASTER = ROOT / "AssetSources" / "v17" / "pupu-coin-front-master-v17.png"
OUTPUT = ROOT / "Pupu.Desktop" / "Assets" / "pupu-gaze-coin-youthful-v17.png"
MANIFEST = ROOT / "Pupu.Desktop" / "Assets" / "pupu-assets.json"


def coin_circle_mask() -> np.ndarray:
    yy, xx = np.mgrid[:CELL, :CELL]
    radius = np.sqrt((xx - 128.0) ** 2 + (yy - 128.0) ** 2)
    return np.clip((120.0 - radius) / 2.5, 0.0, 1.0)


def clear_outside_coin(frame: Image.Image) -> Image.Image:
    rgba = np.asarray(frame.convert("RGBA")).copy()
    alpha = np.round(coin_circle_mask() * 255.0).astype(np.uint8)
    rgba[..., 3] = np.minimum(rgba[..., 3], alpha)
    rgba[rgba[..., 3] == 0, :3] = 0
    return Image.fromarray(rgba, "RGBA")


def fit_front_master() -> Image.Image:
    source = Image.open(FRONT_MASTER).convert("RGBA")
    alpha_bbox = source.getchannel("A").getbbox()
    if alpha_bbox is None:
        raise ValueError(f"Coin front master is empty: {FRONT_MASTER}")

    cropped = source.crop(alpha_bbox)
    maximum = CELL - SAFE_MARGIN * 2
    scale = min(maximum / cropped.width, maximum / cropped.height)
    resized = cropped.resize(
        (max(1, round(cropped.width * scale)), max(1, round(cropped.height * scale))),
        Image.Resampling.LANCZOS,
    )
    frame = Image.new("RGBA", (CELL, CELL), (0, 0, 0, 0))
    frame.alpha_composite(
        resized,
        ((CELL - resized.width) // 2, (CELL - resized.height) // 2),
    )
    return frame


def faded(frame: Image.Image, brightness: float = 0.82) -> Image.Image:
    alpha = frame.getchannel("A")
    gray = ImageOps.grayscale(frame.convert("RGB"))
    silver = ImageOps.colorize(gray, black="#34383d", white="#f3f5f4")
    silver = ImageEnhance.Brightness(silver).enhance(brightness)
    silver.putalpha(alpha)
    return silver


def unhappy(frame: Image.Image, brightness: float = 0.9) -> Image.Image:
    alpha = frame.getchannel("A")
    cool = ImageEnhance.Color(frame.convert("RGB")).enhance(0.72)
    cool = ImageEnhance.Brightness(cool).enhance(brightness)
    cool.putalpha(alpha)
    return cool


def main() -> None:
    source = Image.open(BASE).convert("RGBA")
    expected = (CELL * 8, CELL * 3)
    if source.size != expected:
        raise ValueError(f"V15 base must be {expected[0]}x{expected[1]}, got {source.size}")

    front = fit_front_master()
    legacy_coin_frames = [
        source.crop((column * CELL, 2 * CELL, (column + 1) * CELL, 3 * CELL))
        for column in range(8)
    ]
    back = clear_outside_coin(legacy_coin_frames[4])
    states = [
        front,
        faded(front),
        unhappy(front),
        faded(unhappy(front), brightness=0.72),
        back,
        back,
        front,
        back,
    ]

    output = source.copy()
    for column, frame in enumerate(states):
        output.paste((0, 0, 0, 0), (column * CELL, 2 * CELL, (column + 1) * CELL, 3 * CELL))
        output.alpha_composite(frame, (column * CELL, 2 * CELL))

    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    output.save(OUTPUT, format="PNG", optimize=True)

    manifest = json.loads(MANIFEST.read_text(encoding="utf-8"))
    manifest["version"] = "1.11.0-v17-front-facing-silver-coin"
    manifest["atlases"]["gazeCoin"]["file"] = OUTPUT.name
    manifest["atlases"]["gazeCoin"]["rowActions"][2] = (
        "V17 正视亮银边四态银币与猫爪背面"
    )
    MANIFEST.write_text(
        json.dumps(manifest, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    print(f"V17 coin rebuilt: {OUTPUT}")


if __name__ == "__main__":
    main()
