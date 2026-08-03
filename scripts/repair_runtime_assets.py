#!/usr/bin/env python3
"""Remove chroma-key colour spill from packaged Pupu sprite boundaries.

The operation is deliberately conservative: it never changes alpha, geometry,
frame placement, or opaque interior pixels.  Only the outer three-pixel alpha
boundary of versioned runtime assets is recoloured from a nearby clean subject
pixel.  The green-screen source sheets under AssetSources are not modified.
"""

from __future__ import annotations

import io
import json
from pathlib import Path

import numpy as np
from PIL import Image
from scipy import ndimage


ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / "Pupu.Desktop" / "Assets"
STRICT_VERSIONS = ("-v12.png", "-v15.png", "-v17.png", "-v18.png")


def is_repair_target(path: Path) -> bool:
    return any(version in path.name for version in STRICT_VERSIONS)


def repair_edge_spill(image: Image.Image) -> tuple[Image.Image, int]:
    rgba = np.asarray(image.convert("RGBA")).copy()
    alpha = rgba[..., 3]
    visible = alpha > 8
    boundary = visible & ~ndimage.binary_erosion(visible, iterations=3)
    rgb = rgba[..., :3].astype(np.int16)
    dominance = rgb[..., 1] - np.maximum(rgb[..., 0], rgb[..., 2])
    dirty = boundary & (dominance > 6) & (rgb[..., 1] > 45)
    count = int(dirty.sum())
    if count == 0:
        rgba[alpha == 0, :3] = 0
        return Image.fromarray(rgba, "RGBA"), 0

    clean = visible & ~dirty
    _, nearest = ndimage.distance_transform_edt(~clean, return_indices=True)
    replacement = rgba[..., :3][nearest[0][dirty], nearest[1][dirty]].astype(np.int16)
    # A nearby eye or prop may be naturally green.  Keep its hue from being
    # copied back onto the silhouette by capping only residual green dominance.
    replacement[:, 1] = np.minimum(
        replacement[:, 1],
        np.maximum(replacement[:, 0], replacement[:, 2]) + 3,
    )
    rgba[..., :3][dirty] = np.clip(replacement, 0, 255).astype(np.uint8)
    rgba[alpha == 0, :3] = 0
    return Image.fromarray(rgba, "RGBA"), count


def referenced_runtime_files() -> list[Path]:
    manifest = json.loads((ASSETS / "pupu-assets.json").read_text(encoding="utf-8"))
    relative_files = {
        definition["file"] for definition in manifest["atlases"].values()
    }
    relative_files.update(
        group.get("source", {}).get("file")
        for group in manifest.get("actionGroups", {}).values()
        if group.get("source", {}).get("file")
    )
    return sorted(
        ASSETS / relative
        for relative in relative_files
        if relative and is_repair_target(Path(relative))
    )


def repair_runtime_assets() -> tuple[int, int]:
    changed_files = 0
    changed_pixels = 0
    for path in referenced_runtime_files():
        with Image.open(path) as source:
            source.load()
            repaired, pixel_count = repair_edge_spill(source)
        if pixel_count == 0:
            continue
        buffer = io.BytesIO()
        repaired.save(buffer, format="PNG")
        path.write_bytes(buffer.getvalue())
        changed_files += 1
        changed_pixels += pixel_count
        print(f"Despill {path.relative_to(ROOT)}: {pixel_count} boundary pixels")
    return changed_files, changed_pixels


def main() -> None:
    files, pixels = repair_runtime_assets()
    print(
        f"Runtime edge repair complete: {files} files, {pixels} recoloured pixels; "
        "alpha and geometry unchanged."
    )


if __name__ == "__main__":
    main()
