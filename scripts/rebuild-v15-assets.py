#!/usr/bin/env python3
"""Build Pupu V15 runtime assets from the approved image-generation sources.

The runtime contract is deliberately stricter than a plain green-screen cut:

* every output cell is 256 x 256 with at least 20 px of transparent padding;
* a row shares one scale, centre line and ground-contact baseline;
* tiny disconnected generation artefacts are discarded;
* semi-transparent edge colours are pulled from clean interior fur pixels;
* the pursuit strip is direction-major, with four visible gait phases per
  direction, so movement can never be implemented as a sliding still image.
"""

from __future__ import annotations

import json
import io
from pathlib import Path

import numpy as np
from PIL import Image
from scipy import ndimage


ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / "Pupu.Desktop" / "Assets"
SOURCES = ROOT / "AssetSources" / "v15"
BASE = SOURCES / "base"
CELL = 256
SAFE = 20
BASELINE = CELL - 24


def chroma_alpha(rgb: np.ndarray) -> np.ndarray:
    """Return a soft alpha matte for pure/gradient green generation sources."""
    pix = rgb.astype(np.float32)
    red, green, blue = pix[..., 0], pix[..., 1], pix[..., 2]
    dominance = green - np.maximum(red, blue)
    # Generated backgrounds vary between neon and slightly shaded green.
    # Clean fur is opaque below 18 dominance, green screen is transparent at 66.
    alpha = np.clip((66.0 - dominance) / 48.0, 0.0, 1.0)
    alpha[(green < 40) | (dominance < 12)] = 1.0
    return (alpha * 255.0).astype(np.uint8)


def keep_subject(alpha: np.ndarray) -> np.ndarray:
    """Keep the main subject and meaningful connected props, not red guide dots."""
    mask = alpha > 22
    labels, count = ndimage.label(mask)
    if count == 0:
        return np.zeros_like(alpha)
    areas = np.bincount(labels.ravel())
    largest = int(areas[1:].max(initial=0))
    keep = np.zeros(count + 1, dtype=bool)
    keep[1:] = areas[1:] >= max(40, int(largest * 0.012))
    kept = keep[labels]
    kept = ndimage.binary_closing(kept, iterations=1)
    kept = ndimage.binary_fill_holes(kept)
    return np.where(kept, alpha, 0).astype(np.uint8)


def depollute_edges(rgba: np.ndarray) -> np.ndarray:
    """Remove green RGB from surviving antialiased boundary pixels."""
    out = rgba.copy()
    alpha = out[..., 3]
    rgb = out[..., :3].astype(np.int16)
    dominance = rgb[..., 1] - np.maximum(rgb[..., 0], rgb[..., 2])
    dirty = (alpha > 0) & (dominance > 12) & (rgb[..., 1] > 80)
    clean = (alpha >= 220) & (dominance <= 12)
    if clean.any() and dirty.any():
        _, indices = ndimage.distance_transform_edt(~clean, return_indices=True)
        out[..., :3][dirty] = out[..., :3][indices[0][dirty], indices[1][dirty]]
    out[alpha == 0, :3] = 0
    return out


def key_cell(image: Image.Image) -> Image.Image:
    rgb = np.asarray(image.convert("RGB"))
    alpha = keep_subject(chroma_alpha(rgb))
    rgba = np.dstack((rgb, alpha)).astype(np.uint8)
    return Image.fromarray(depollute_edges(rgba), "RGBA")


def split_sheet(path: Path, rows: int = 4, columns: int = 8) -> list[list[Image.Image]]:
    image = Image.open(path).convert("RGB")
    output: list[list[Image.Image]] = []
    for row in range(rows):
        y0 = round(row * image.height / rows)
        y1 = round((row + 1) * image.height / rows)
        keyed_row = key_cell(image.crop((0, y0, image.width, y1)))
        alpha = np.asarray(keyed_row.getchannel("A"))
        labels, count = ndimage.label(alpha > 22)
        areas = np.bincount(labels.ravel())
        candidates = sorted(
            range(1, count + 1), key=lambda value: int(areas[value]), reverse=True
        )[:columns]
        # Generated sheets are visually gridded but a long tail can cross the
        # nominal boundary. Segmenting the full row prevents that tail—or a
        # sleeping flank—from being cut in half.
        if len(candidates) == columns:
            frames_with_x: list[tuple[int, Image.Image]] = []
            rgba = np.asarray(keyed_row).copy()
            for label_id in candidates:
                ys, xs = np.nonzero(labels == label_id)
                x0, x1 = max(0, int(xs.min()) - 3), min(image.width, int(xs.max()) + 4)
                yy0, yy1 = max(0, int(ys.min()) - 3), min(y1 - y0, int(ys.max()) + 4)
                isolated = rgba[yy0:yy1, x0:x1].copy()
                local_labels = labels[yy0:yy1, x0:x1]
                isolated[local_labels != label_id, 3] = 0
                isolated[isolated[..., 3] == 0, :3] = 0
                frames_with_x.append((x0, Image.fromarray(isolated, "RGBA")))
            output.append([frame for _, frame in sorted(frames_with_x)])
            continue

        # Conservative fallback for an unexpectedly connected source sheet.
        frames: list[Image.Image] = []
        for column in range(columns):
            x0 = round(column * image.width / columns)
            x1 = round((column + 1) * image.width / columns)
            frames.append(key_cell(image.crop((x0, y0, x1, y1))))
        output.append(frames)
    return output


def alpha_bbox(frame: Image.Image) -> tuple[int, int, int, int]:
    alpha = np.asarray(frame.getchannel("A"))
    ys, xs = np.nonzero(alpha > 8)
    if len(xs) == 0:
        raise ValueError("Generated frame is empty after chroma removal")
    return int(xs.min()), int(ys.min()), int(xs.max() + 1), int(ys.max() + 1)


def normalize_row(
    frames: list[Image.Image],
    *,
    max_width: int = CELL - SAFE * 2,
    max_height: int = CELL - SAFE * 2,
) -> list[Image.Image]:
    bboxes = [alpha_bbox(frame) for frame in frames]
    widths = [box[2] - box[0] for box in bboxes]
    heights = [box[3] - box[1] for box in bboxes]
    scale = min(max_width / max(widths), max_height / max(heights))
    result: list[Image.Image] = []
    for frame, box in zip(frames, bboxes):
        subject = frame.crop(box)
        size = (
            max(1, round(subject.width * scale)),
            max(1, round(subject.height * scale)),
        )
        subject = subject.resize(size, Image.Resampling.LANCZOS)
        canvas = Image.new("RGBA", (CELL, CELL), (0, 0, 0, 0))
        x = round((CELL - subject.width) / 2)
        y = BASELINE - subject.height
        canvas.alpha_composite(subject, (x, y))
        result.append(Image.fromarray(depollute_edges(np.asarray(canvas).copy()), "RGBA"))
    return result


def clean_runtime_cell(cell: Image.Image) -> Image.Image:
    rgba = np.asarray(cell.convert("RGBA")).copy()
    original_alpha = rgba[..., 3]
    keyed = chroma_alpha(rgba[..., :3])
    # Only reduce alpha where the existing visible pixel is demonstrably green.
    rgba[..., 3] = np.minimum(original_alpha, keyed)
    rgba = depollute_edges(rgba)
    return Image.fromarray(rgba, "RGBA")


def clean_runtime_atlas(path: Path, rows: int, columns: int) -> list[list[Image.Image]]:
    image = Image.open(path).convert("RGBA")
    return [
        [
            clean_runtime_cell(
                image.crop(
                    (
                        column * CELL,
                        row * CELL,
                        (column + 1) * CELL,
                        (row + 1) * CELL,
                    )
                )
            )
            for column in range(columns)
        ]
        for row in range(rows)
    ]


def save_atlas(rows: list[list[Image.Image]], path: Path) -> None:
    atlas = Image.new("RGBA", (len(rows[0]) * CELL, len(rows) * CELL), (0, 0, 0, 0))
    for row_index, row in enumerate(rows):
        for column_index, frame in enumerate(row):
            atlas.alpha_composite(frame, (column_index * CELL, row_index * CELL))
    # Pillow's aggressive optimiser has produced a missing IEND on a few very
    # large, sparse RGBA atlases in constrained build sandboxes. A normal PNG
    # save is deterministic and fully decodable, so favour integrity over a
    # small package-size reduction.
    buffer = io.BytesIO()
    atlas.save(buffer, format="PNG")
    path.write_bytes(buffer.getvalue())


def build_coin_atlas() -> str:
    source = BASE / "pupu-gaze-coin-youthful-v13.png"
    rows = clean_runtime_atlas(source, 3, 8)
    yy, xx = np.mgrid[:CELL, :CELL]
    radius = np.sqrt((xx - 128.0) ** 2 + (yy - 128.0) ** 2)
    circle = np.clip((120.0 - radius) / 3.0, 0.0, 1.0)
    # Five front-facing states may be used by old manifests; clear every corner.
    for index in (0, 1, 2, 3, 5, 6, 7):
        rgba = np.asarray(rows[2][index]).copy()
        rgba[..., 3] = np.minimum(
            rgba[..., 3], np.round(circle * 255.0).astype(np.uint8)
        )
        rgba[rgba[..., 3] == 0, :3] = 0
        rows[2][index] = Image.fromarray(rgba, "RGBA")
    filename = "pupu-gaze-coin-youthful-v15.png"
    save_atlas(rows, ASSETS / filename)
    return filename


def main() -> None:
    rest = split_sheet(SOURCES / "pupu-rest-v15-chroma.png")
    sleep = split_sheet(SOURCES / "pupu-sleep-v15-chroma.png")
    routines_source = split_sheet(SOURCES / "pupu-routines-v15-chroma.png")
    prone = split_sheet(SOURCES / "pupu-prone-v15-chroma.png")
    chase = split_sheet(SOURCES / "pupu-chase-v15-chroma.png")

    rest = [normalize_row(row) for row in rest]
    sleep = [normalize_row(row) for row in sleep]
    routines_source = [normalize_row(row) for row in routines_source]
    prone = [normalize_row(row) for row in prone]
    chase = [normalize_row(row) for row in chase]

    # Routines: the three historically broken rows are now independent, complete
    # full-body frames. Generated grooming supplies the paw-cleaning cycle.
    routine_rows = clean_runtime_atlas(
        BASE / "pupu-routines-youthful-v14.png", 8, 8
    )
    routine_rows[0] = rest[0]
    routine_rows[1] = prone[1]
    routine_rows[2] = routines_source[0]
    save_atlas(routine_rows, ASSETS / "pupu-routines-youthful-v15.png")

    # The side-lying fallback is also used directly from motion row 9.
    motion_rows = clean_runtime_atlas(BASE / "pupu-motion-youthful-v13.png", 10, 8)
    motion_rows[9] = rest[0]
    save_atlas(motion_rows, ASSETS / "pupu-motion-youthful-v15.png")

    activity_rows = clean_runtime_atlas(
        BASE / "pupu-activity-youthful-v13.png", 8, 8
    )
    activity_rows[2] = rest[3]
    activity_rows[3] = sleep[0]
    activity_rows[4] = sleep[1]
    activity_rows[5] = sleep[2]
    activity_rows[6] = rest[1]
    save_atlas(activity_rows, ASSETS / "pupu-activity-youthful-v15.png")

    equipment_rows = clean_runtime_atlas(
        BASE / "pupu-life-equipment-youthful-v13.png", 3, 8
    )
    equipment_rows[0] = routines_source[0]
    equipment_rows[1] = rest[2]
    save_atlas(equipment_rows, ASSETS / "pupu-life-equipment-youthful-v15.png")

    litter_rows = clean_runtime_atlas(
        BASE / "pupu-litter-youthful-v13.png", 4, 8
    )
    litter_rows[1] = routines_source[1]
    litter_rows[2] = routines_source[3]
    litter_rows[3] = routines_source[2]
    save_atlas(litter_rows, ASSETS / "pupu-litter-youthful-v15.png")

    # Life and touch keep their acclaimed V14 modelling, but receive a new matte
    # that removes the visible green halo without shrinking whiskers.
    for stem, rows_count in (("life", 8), ("touch", 6)):
        rows = clean_runtime_atlas(
            BASE / f"pupu-{stem}-youthful-v14.png", rows_count, 8
        )
        save_atlas(rows, ASSETS / f"pupu-{stem}-youthful-v15.png")

    # Full-body gaze frames are cleaned in-place structurally, never head overlays.
    gaze_source = BASE / "Actions" / "pupu-gaze-fullbody-youthful-v14.png"
    gaze_rows = clean_runtime_atlas(gaze_source, 1, 16)
    save_atlas(
        gaze_rows,
        ASSETS / "Actions" / "pupu-gaze-fullbody-youthful-v15.png",
    )

    # Cage and placement-object sources came from the same green-screen pass.
    # Remove the green rear plate and boundary spill while preserving cage bars,
    # cat fur and the supplied freeze-dried / laser object silhouettes.
    cage_source = BASE / "Actions" / "pupu-cage-rest-youthful-v14.png"
    cage_rows = clean_runtime_atlas(cage_source, 1, 12)
    save_atlas(
        cage_rows,
        ASSETS / "Actions" / "pupu-cage-rest-youthful-v15.png",
    )
    for old_name, new_name in (
        ("pupu-freeze-dried-target-v14.png", "pupu-freeze-dried-target-v15.png"),
        ("pupu-laser-dot-target-v14.png", "pupu-laser-dot-target-v15.png"),
    ):
        source_path = BASE / "Actions" / old_name
        cleaned = clean_runtime_cell(Image.open(source_path).convert("RGBA"))
        cleaned.save(ASSETS / "Actions" / new_name)

    # Direction-major strip: L phase0..3, UL phase0..3, ... DL phase0..3.
    # Image generation produced especially clean left/rear/front views; construct
    # their right-facing counterparts by lossless mirroring to keep Pupu's scale
    # and stride exactly symmetric instead of accepting near-duplicate directions.
    direction_frames = [
        [chase[phase][0] for phase in range(4)],
        [chase[phase][1] for phase in range(4)],
        [chase[phase][2] for phase in range(4)],
        [
            chase[phase][1].transpose(Image.Transpose.FLIP_LEFT_RIGHT)
            for phase in range(4)
        ],
        [
            chase[phase][0].transpose(Image.Transpose.FLIP_LEFT_RIGHT)
            for phase in range(4)
        ],
        [
            chase[phase][7].transpose(Image.Transpose.FLIP_LEFT_RIGHT)
            for phase in range(4)
        ],
        [chase[phase][6] for phase in range(4)],
        [chase[phase][7] for phase in range(4)],
    ]
    chase_strip = [[frame for direction in direction_frames for frame in direction]]
    chase_path = ASSETS / "Actions" / "pupu-chase-gait-8dir-youthful-v15.png"
    save_atlas(chase_strip, chase_path)

    coin_file = build_coin_atlas()

    manifest_path = ASSETS / "pupu-assets.json"
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    manifest["version"] = "1.10.0-v15-clean-edge-gait-contract"
    manifest["actionGroups"].pop("cursor-gaze-preview", None)
    replacements = {
        "life": "pupu-life-youthful-v15.png",
        "touch": "pupu-touch-youthful-v15.png",
        "routines": "pupu-routines-youthful-v15.png",
        "activity": "pupu-activity-youthful-v15.png",
        "lifeEquipment": "pupu-life-equipment-youthful-v15.png",
        "motion": "pupu-motion-youthful-v15.png",
        "gazeCoin": coin_file,
        "litter": "pupu-litter-youthful-v15.png",
    }
    for atlas_id, filename in replacements.items():
        manifest["atlases"][atlas_id]["file"] = filename

    manifest["actionGroups"]["gaze-fullbody-16"]["source"]["file"] = (
        "Actions/pupu-gaze-fullbody-youthful-v15.png"
    )
    manifest["actionGroups"]["cage-rest-12"]["source"]["file"] = (
        "Actions/pupu-cage-rest-youthful-v15.png"
    )
    manifest["actionGroups"]["freeze-dried-target"]["source"]["file"] = (
        "Actions/pupu-freeze-dried-target-v15.png"
    )
    manifest["actionGroups"]["laser-dot-target"]["source"]["file"] = (
        "Actions/pupu-laser-dot-target-v15.png"
    )
    for group_id in ("laser-chase-8", "snack-chase-8"):
        group = manifest["actionGroups"][group_id]
        group["source"] = {
            "type": "spriteStrip",
            "file": "Actions/pupu-chase-gait-8dir-youthful-v15.png",
            "columns": 32,
            "rows": 1,
            "frameWidth": CELL,
            "frameHeight": CELL,
        }
        group["frameCount"] = 32
        group["frameDurationMs"] = 165
        group["frames"] = list(range(32))
        group["loopMode"] = "loop"
        group["directions"] = {
            name: {"frames": list(range(index * 4, index * 4 + 4))}
            for index, name in enumerate(
                (
                    "left",
                    "upLeft",
                    "up",
                    "upRight",
                    "right",
                    "downRight",
                    "down",
                    "downLeft",
                )
            )
        }
        group["triggerConditions"].append(
            "每个方向包含四个连续脚步相位；窗口位移严格随换帧推进"
        )

    manifest_path.write_text(
        json.dumps(manifest, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )

    # Remove stale outputs from the packaged directory; source history remains in
    # AssetSources and Git, while the runtime ships only manifest-referenced PNGs.
    referenced = {
        value["file"] for value in manifest["atlases"].values()
    } | {
        group["source"]["file"]
        for group in manifest["actionGroups"].values()
        if group.get("source", {}).get("file")
    }
    for path in ASSETS.rglob("*.png"):
        relative = path.relative_to(ASSETS).as_posix()
        if relative == "pupu-icon.png" or relative in referenced:
            continue
        path.unlink()

    print("V15 assets rebuilt")
    print(f"Runtime manifest references {len(referenced)} image files")


if __name__ == "__main__":
    main()
