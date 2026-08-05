#!/usr/bin/env python3
"""Remove chroma-key colour spill from packaged Pupu sprite boundaries.

The operation is deliberately conservative: it never changes alpha, geometry,
frame placement, or opaque interior pixels.  Only the outer three-pixel alpha
boundary of versioned runtime assets is recoloured from a nearby clean subject
pixel.  The green-screen source sheets under AssetSources are not modified.
"""

from __future__ import annotations

import gc
import json
from pathlib import Path

import numpy as np
from PIL import Image
from scipy import ndimage


ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / "Pupu.Desktop" / "Assets"
STRICT_VERSIONS = ("-v12.png", "-v15.png", "-v17.png", "-v18.png", "-v19.png")


def is_repair_target(path: Path) -> bool:
    return any(version in path.name for version in STRICT_VERSIONS)


def _repair_edge_spill_cell(image: Image.Image) -> tuple[Image.Image, int]:
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


def repair_edge_spill(image: Image.Image) -> tuple[Image.Image, int]:
    """Repair one sprite at a time so wide strips never allocate huge EDT maps."""
    source = image.convert("RGBA")
    if source.width <= 256 and source.height <= 256:
        return _repair_edge_spill_cell(source)
    if source.width % 256 or source.height % 256:
        return _repair_edge_spill_cell(source)
    output = Image.new("RGBA", source.size, (0, 0, 0, 0))
    changed = 0
    for y in range(0, source.height, 256):
        for x in range(0, source.width, 256):
            repaired, count = _repair_edge_spill_cell(
                source.crop((x, y, x + 256, y + 256))
            )
            output.paste(repaired, (x, y))
            changed += count
    return output, changed


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
        temporary = path.with_name(f"{path.name}.repairing")
        repaired.save(temporary, format="PNG")
        temporary.replace(path)
        repaired.close()
        gc.collect()
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
