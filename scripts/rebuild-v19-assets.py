#!/usr/bin/env python3
"""Build the V19 identity-unified runtime asset pack.

V19 deliberately stops using an older atlas as a silent base. Every runtime
cat atlas is rebuilt from the approved V18 identity sheets or the V19
magic/seasonal masters. Four generated key poses are expanded to eight clean
display phases without duplicated cells or double-exposure crossfades.
"""

from __future__ import annotations

import importlib.util
import json
from pathlib import Path

import numpy as np
from PIL import Image, ImageEnhance, ImageFilter


ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / "Pupu.Desktop" / "Assets"
V18 = ROOT / "AssetSources" / "v18"
V19 = ROOT / "AssetSources" / "v19"
CELL = 256


def load_module(name: str, path: Path):
    spec = importlib.util.spec_from_file_location(name, path)
    if spec is None or spec.loader is None:
        raise RuntimeError(f"Unable to load helper: {path}")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


H = load_module("pupu_v15_helpers", ROOT / "scripts" / "rebuild-v15-assets.py")
V18_HELPERS = load_module("pupu_v18_helpers", ROOT / "scripts" / "rebuild-v18-assets.py")
V17_COIN = load_module("pupu_v17_coin", ROOT / "scripts" / "rebuild-v17-coin.py")


def split(path: Path) -> list[list[Image.Image]]:
    return V18_HELPERS.split_grid(path)


def micro_phase(
    frame: Image.Image,
    *,
    scale: float,
    dx: int,
    dy: int,
) -> Image.Image:
    """Create a clean second phase without blending two complete cat bodies."""
    box = H.alpha_bbox(frame)
    subject = frame.crop(box)
    size = (
        max(1, round(subject.width * scale)),
        max(1, round(subject.height * scale)),
    )
    subject = subject.resize(size, Image.Resampling.LANCZOS)
    canvas = Image.new("RGBA", (CELL, CELL), (0, 0, 0, 0))
    x = round((CELL - subject.width) / 2) + dx
    y = H.BASELINE - subject.height + dy
    x = max(20, min(CELL - 20 - subject.width, x))
    y = max(20, min(CELL - 20 - subject.height, y))
    canvas.alpha_composite(subject, (x, y))
    return Image.fromarray(H.depollute_edges(np.asarray(canvas).copy()), "RGBA")


def eight_phase(
    keys: list[Image.Image],
    *,
    max_width: int = 216,
    max_height: int = 216,
    close_loop: bool = True,
) -> list[Image.Image]:
    normalized = H.normalize_row(keys[:4], max_width=max_width, max_height=max_height)
    # The second phase of each key is a restrained breathing/weight shift.
    # Cross-fading two complete furry bodies looks like a double exposure and
    # is explicitly avoided.  One-shot rows settle downward at the end; loops
    # use alternating up/down micro motion so frame 7 returns naturally to 0.
    variants = (
        (0.996, 0, -1),
        (0.992, 1, 0),
        (0.995, -1, -1),
        ((0.997 if close_loop else 0.990), 0, (0 if close_loop else 1)),
    )
    result: list[Image.Image] = []
    for key, (scale, dx, dy) in zip(normalized, variants):
        result.extend((key, micro_phase(key, scale=scale, dx=dx, dy=dy)))
    return result


def eight_phase_from_two(
    first: Image.Image,
    second: Image.Image,
    *,
    max_width: int = 208,
    max_height: int = 210,
) -> list[Image.Image]:
    a, b = H.normalize_row(
        [first, second], max_width=max_width, max_height=max_height
    )
    # Three restrained weight shifts around each photographed gait key create
    # a clean eight-phase cadence. The final two phases return toward A so the
    # strip closes without a B -> A snap at the loop boundary.
    return [
        a,
        micro_phase(a, scale=0.997, dx=1, dy=-1),
        micro_phase(a, scale=0.993, dx=0, dy=1),
        b,
        micro_phase(b, scale=0.997, dx=-1, dy=-1),
        micro_phase(b, scale=0.993, dx=0, dy=1),
        micro_phase(a, scale=0.995, dx=-1, dy=0),
        micro_phase(a, scale=0.999, dx=0, dy=0),
    ]


def mirror(row: list[Image.Image]) -> list[Image.Image]:
    return [frame.transpose(Image.Transpose.FLIP_LEFT_RIGHT) for frame in row]


def align_movement_centroids(frames: list[Image.Image]) -> list[Image.Image]:
    """Balance pose centroids in both axes while preserving the safe margin."""
    measurements: list[tuple[float, float, tuple[int, int, int, int]]] = []
    for frame in frames:
        alpha = np.asarray(frame.getchannel("A"))
        ys, xs = np.nonzero(alpha > 8)
        if len(xs) == 0:
            raise ValueError("Cannot align an empty movement frame")
        measurements.append((
            float(xs.mean()),
            float(ys.mean()),
            (int(xs.min()), int(ys.min()), int(xs.max() + 1), int(ys.max() + 1)),
        ))
    target_x = (min(value[0] for value in measurements) + max(value[0] for value in measurements)) / 2
    target_y = (min(value[1] for value in measurements) + max(value[1] for value in measurements)) / 2
    result: list[Image.Image] = []
    for frame, (centre_x, centre_y, box) in zip(frames, measurements):
        dx = int(round(target_x - centre_x))
        dy = int(round(target_y - centre_y))
        dx = max(20 - box[0], min((CELL - 20) - box[2], dx))
        dy = max(20 - box[1], min((CELL - 20) - box[3], dy))
        canvas = Image.new("RGBA", (CELL, CELL), (0, 0, 0, 0))
        canvas.alpha_composite(frame, (dx, dy))
        result.append(canvas)
    return result


def flattened(sheet: list[list[Image.Image]]) -> list[Image.Image]:
    return [frame for row in sheet for frame in row]


def chase_direction_rows(sheet: list[list[Image.Image]]) -> list[list[Image.Image]]:
    keys = flattened(sheet)
    if len(keys) != 16:
        raise ValueError(f"Expected sixteen pursuit keys, found {len(keys)}")
    return [eight_phase_from_two(keys[index * 2], keys[index * 2 + 1]) for index in range(8)]


def strip_eight_phase(direction_rows: list[list[Image.Image]]) -> list[list[Image.Image]]:
    return [[frame for row in direction_rows for frame in row]]


def build_coin_row(source_row: list[Image.Image]) -> list[Image.Image]:
    normal_source = source_row[0]
    unhappy_source = source_row[2] if len(source_row) > 2 else source_row[0]
    back_source = source_row[4] if len(source_row) > 4 else source_row[-1]

    def bright(frame: Image.Image) -> Image.Image:
        image = ImageEnhance.Color(frame.convert("RGBA")).enhance(1.30)
        image = ImageEnhance.Contrast(image).enhance(1.10)
        image = ImageEnhance.Brightness(image).enhance(1.13)
        rgba = np.asarray(image).copy()
        alpha = rgba[..., 3]
        yy, xx = np.mgrid[:CELL, :CELL]
        glint = ((xx - yy > -18) & (xx - yy < -5) & (xx > 65) & (xx < 195) & (alpha > 0))
        rgba[..., :3][glint] = np.clip(
            rgba[..., :3][glint].astype(np.int16) + 34, 0, 255
        ).astype(np.uint8)
        return Image.fromarray(rgba, "RGBA")

    def faded(frame: Image.Image, seed: int) -> Image.Image:
        rgba = np.asarray(frame.convert("RGBA")).copy()
        alpha = rgba[..., 3]
        rgb = rgba[..., :3].astype(np.float32)
        luminance = rgb.mean(axis=2, keepdims=True)
        sepia = np.concatenate((luminance * 1.03, luminance * 0.94, luminance * 0.78), axis=2)
        rgb = rgb * 0.30 + sepia * 0.70
        yy, xx = np.mgrid[:CELL, :CELL]
        mask = alpha > 32
        rust = np.zeros((CELL, CELL), dtype=np.float32)
        centres = ((79 + seed, 74), (173, 92 + seed), (109, 184), (187 - seed, 167))
        for cx, cy in centres:
            rust += np.exp(-((xx - cx) ** 2 + (yy - cy) ** 2) / 155.0)
        rust = np.clip(rust, 0, 0.70)[..., None] * mask[..., None]
        rust_colour = np.array([116.0, 72.0, 39.0], dtype=np.float32)
        rgb = rgb * (1.0 - rust) + rust_colour * rust
        rgba[..., :3] = np.clip(rgb * 0.88, 0, 255).astype(np.uint8)
        rgba[alpha == 0, :3] = 0
        return Image.fromarray(rgba, "RGBA").filter(ImageFilter.UnsharpMask(1.2, 110, 2))

    normal = bright(normal_source)
    unhappy = bright(unhappy_source)
    back = bright(back_source)
    def edge(frame: Image.Image, width: int) -> Image.Image:
        subject = frame.resize((width, CELL), Image.Resampling.LANCZOS)
        canvas = Image.new("RGBA", (CELL, CELL), (0, 0, 0, 0))
        canvas.alpha_composite(subject, ((CELL - width) // 2, 0))
        return canvas

    return [
        normal,
        faded(normal_source, 0),
        unhappy,
        faded(unhappy_source, 9),
        back,
        edge(normal, 84),
        edge(back, 84),
        back.copy(),
    ]


def source_map() -> dict[str, list[list[Image.Image]]]:
    return {
        "idle": split(V18 / "pupu-idle-v18-chroma.png"),
        "play": split(V18 / "pupu-play-v18-chroma.png"),
        "sleep": split(V18 / "pupu-sleep-v18-chroma.png"),
        "groom": split(V18 / "pupu-groom-v18-chroma.png"),
        "interaction": split(V18 / "pupu-interaction-v18-chroma.png"),
        "diagonal": split(V18 / "pupu-diagonal-gait-v18-chroma.png"),
        "litter": split(V18 / "pupu-litter-v18-chroma.png"),
        "snack": split(V18 / "pupu-snack-chase-v18-chroma.png"),
        "laser": split(V18 / "pupu-laser-chase-v18-chroma.png"),
        "magic": split(V19 / "pupu-magic-v19-chroma.png"),
        "seasonal": split(V19 / "pupu-seasonal-v19-chroma.png"),
    }


def save(rows: list[list[Image.Image]], filename: str) -> str:
    H.save_atlas(rows, ASSETS / filename)
    return filename


def main() -> None:
    s = source_map()
    snack_dirs = [align_movement_centroids(row) for row in chase_direction_rows(s["snack"])]
    laser_dirs = [align_movement_centroids(row) for row in chase_direction_rows(s["laser"])]

    files: dict[str, str] = {}
    files["core"] = save([
        eight_phase(s["idle"][1]),
        eight_phase(s["play"][0], close_loop=False),
        eight_phase(s["play"][1], close_loop=False),
        eight_phase(s["play"][3]),
        eight_phase(s["play"][2], close_loop=False),
        eight_phase(s["sleep"][3], close_loop=False),
    ], "pupu-core-youthful-v19.png")

    files["life"] = save([
        eight_phase(s["interaction"][1], close_loop=False),
        eight_phase(s["litter"][0], max_height=208, close_loop=False),
        eight_phase(s["idle"][2], close_loop=False),
        eight_phase(s["groom"][1]),
        eight_phase(s["interaction"][0], close_loop=False),
        eight_phase(s["play"][3], close_loop=False),
        eight_phase(s["sleep"][0]),
        eight_phase(s["interaction"][0], close_loop=False),
    ], "pupu-life-youthful-v19.png")

    files["directions"] = save([
        snack_dirs[0],
        snack_dirs[4],
        snack_dirs[2],
        snack_dirs[6],
    ], "pupu-directions-youthful-v19.png")

    files["touch"] = save([
        eight_phase(s["idle"][2], close_loop=False),
        eight_phase(s["groom"][0]),
        eight_phase(s["interaction"][0], close_loop=False),
        eight_phase(s["play"][2], close_loop=False),
        eight_phase(s["diagonal"][0], close_loop=False),
        eight_phase(s["sleep"][3], close_loop=False),
    ], "pupu-touch-youthful-v19.png")

    files["routines"] = save([
        eight_phase(s["idle"][0]),
        eight_phase(s["idle"][2]),
        eight_phase(s["groom"][2]),
        eight_phase(s["interaction"][1]),
        eight_phase(s["interaction"][1], close_loop=False),
        mirror(eight_phase(s["interaction"][1], close_loop=False)),
        snack_dirs[2],
        eight_phase(s["diagonal"][1], close_loop=False),
    ], "pupu-routines-youthful-v19.png")

    files["walkModes"] = save([
        V18_HELPERS.align_row_centroids(eight_phase(s["diagonal"][2], max_width=202, max_height=212)),
        V18_HELPERS.align_row_centroids(mirror(eight_phase(s["diagonal"][2], max_width=202, max_height=212))),
        V18_HELPERS.align_row_centroids(eight_phase(s["diagonal"][3], max_width=202, max_height=212)),
        V18_HELPERS.align_row_centroids(mirror(eight_phase(s["diagonal"][3], max_width=202, max_height=212))),
        snack_dirs[0],
        snack_dirs[4],
        snack_dirs[2],
        snack_dirs[6],
    ], "pupu-walk-modes-youthful-v19.png")

    files["activity"] = save([
        eight_phase(s["interaction"][2], close_loop=False),
        eight_phase(s["idle"][2]),
        eight_phase(s["sleep"][0]),
        eight_phase(s["sleep"][1]),
        eight_phase(s["sleep"][2]),
        eight_phase(s["sleep"][3], close_loop=False),
        eight_phase(s["idle"][3]),
        eight_phase(s["groom"][3]),
    ], "pupu-activity-youthful-v19.png")

    files["lifeEquipment"] = save([
        H.normalize_row(s["groom"][0] + s["groom"][1]),
        eight_phase(s["interaction"][3], max_height=204),
        eight_phase(s["interaction"][0], max_width=212, max_height=214, close_loop=False),
    ], "pupu-life-equipment-youthful-v19.png")

    files["motion"] = save([
        V18_HELPERS.align_row_centroids(eight_phase(s["diagonal"][2], max_width=202, max_height=212)),
        V18_HELPERS.align_row_centroids(eight_phase(s["diagonal"][3], max_width=202, max_height=212)),
        V18_HELPERS.align_row_centroids(mirror(eight_phase(s["diagonal"][3], max_width=202, max_height=212))),
        V18_HELPERS.align_row_centroids(mirror(eight_phase(s["diagonal"][2], max_width=202, max_height=212))),
        V18_HELPERS.align_row_centroids(eight_phase(s["diagonal"][0], max_width=202, max_height=212)),
        V18_HELPERS.align_row_centroids(eight_phase(s["diagonal"][1], max_width=202, max_height=212)),
        V18_HELPERS.align_row_centroids(mirror(eight_phase(s["diagonal"][1], max_width=202, max_height=212))),
        V18_HELPERS.align_row_centroids(mirror(eight_phase(s["diagonal"][0], max_width=202, max_height=212))),
        V18_HELPERS.align_row_centroids(
            eight_phase(s["magic"][0], max_width=216, max_height=212)
        ),
        eight_phase(s["idle"][0]),
    ], "pupu-motion-youthful-v19.png")

    files["litter"] = save([
        eight_phase(row, max_height=208, close_loop=index == 1)
        for index, row in enumerate(s["litter"])
    ], "pupu-litter-youthful-v19.png")

    files["specials"] = save([
        eight_phase(s["idle"][2]),
        eight_phase(s["magic"][0], max_width=216, max_height=212, close_loop=False),
        eight_phase(s["magic"][1], max_width=214, max_height=212, close_loop=False),
        eight_phase(s["magic"][2], max_width=216, max_height=212, close_loop=False),
        eight_phase(s["magic"][3], max_width=214, max_height=212),
    ], "pupu-specials-youthful-v19.png")

    files["seasonal"] = save([
        eight_phase(row, max_width=216, max_height=212)
        for row in s["seasonal"]
    ], "pupu-seasonal-youthful-v19.png")

    # Rebuild the engraving from stable source masters, then create a true
    # colourful/high-glint state and a distinct sepia/tarnished state.
    coin_front = V17_COIN.fit_front_master()
    coin_base = Image.open(V17_COIN.BASE).convert("RGBA")
    coin_back = V17_COIN.clear_outside_coin(
        coin_base.crop((4 * CELL, 2 * CELL, 5 * CELL, 3 * CELL))
    )
    coin_source = [
        coin_front,
        coin_front,
        V17_COIN.unhappy(coin_front),
        coin_front,
        coin_back,
    ]
    files["gazeCoin"] = save([
        eight_phase(s["idle"][2]),
        mirror(eight_phase(s["idle"][2])),
        build_coin_row(coin_source),
    ], "pupu-gaze-coin-youthful-v19.png")

    snack_strip = save(
        strip_eight_phase(snack_dirs),
        "Actions/pupu-snack-chase-gait-8dir-youthful-v19.png",
    )
    laser_strip = save(
        strip_eight_phase(laser_dirs),
        "Actions/pupu-laser-chase-gait-8dir-youthful-v19.png",
    )
    gaze_strip = save(
        [H.normalize_row(flattened(s["idle"]), max_width=216, max_height=210)],
        "Actions/pupu-gaze-fullbody-youthful-v19.png",
    )
    cage_strip = save(
        [H.normalize_row(flattened(s["sleep"])[:12], max_width=214, max_height=206)],
        "Actions/pupu-cage-rest-youthful-v19.png",
    )
    harness_strip = save(
        [H.normalize_row(flattened(s["diagonal"]), max_width=202, max_height=212)],
        "Actions/pupu-walk-harness-16dir-youthful-v19.png",
    )

    manifest_path = ASSETS / "pupu-assets.json"
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    manifest["version"] = "1.11.2-v19-unified-transition-chat"
    for atlas_id, filename in files.items():
        manifest["atlases"][atlas_id]["file"] = filename

    action_files = {
        "laser-chase-8": laser_strip,
        "snack-chase-8": snack_strip,
        "gaze-fullbody-16": gaze_strip,
        "cage-rest-12": cage_strip,
        "harness-walk-16": harness_strip,
    }
    for group_id, filename in action_files.items():
        manifest["actionGroups"][group_id]["source"]["file"] = filename

    for group_id in ("laser-chase-8", "snack-chase-8"):
        group = manifest["actionGroups"][group_id]
        group["source"]["columns"] = 64
        group["frameCount"] = 64
        group["frames"] = list(range(64))
        group["directions"] = {
            name: {"frames": list(range(index * 8, index * 8 + 8))}
            for index, name in enumerate(
                ("left", "upLeft", "up", "upRight", "right", "downRight", "down", "downLeft")
            )
        }
        group["triggerConditions"] = [
            condition
            for condition in group.get("triggerConditions", [])
            if "每方向四个" not in condition and "0-1-2-3-2-1" not in condition
        ]
        group["triggerConditions"].append(
            "每方向八个无重复显示相位连续播放；窗口位移严格随换帧推进"
        )

    atlas_groups = {
        "magic-accio-broom-intro": ("specials", 1),
        "magic-apparate": ("specials", 2),
        "magic-petrificus-totalus": ("specials", 3),
        "magic-scourgify": ("specials", 4),
        "toilet-relieve": ("litter", 1),
        "annoyed-touch": ("touch", 3),
        "angry-touch": ("touch", 4),
        "trust-touch": ("touch", 5),
        "groom": ("life", 3),
        "attention": ("life", 4),
        "ask-walk": ("lifeEquipment", 2),
        "fur-groom-daily": ("lifeEquipment", 0),
    }
    for group_id, (atlas, row) in atlas_groups.items():
        group = manifest["actionGroups"][group_id]
        group["source"] = {"type": "atlasRow", "atlas": atlas, "row": row}
        group["frames"] = list(range(8))
        group["frameCount"] = 8

    for group_id in (
        "side-lie-idle", "prone-idle", "sploot", "wand-loop",
        "freeze-dried-eating-loop", "fur-groom-daily", "magic-scourgify",
    ):
        group = manifest["actionGroups"][group_id]
        group["frames"] = list(range(8))
        group["frameCount"] = 8
        group["loopMode"] = "loop"
        group["intro"] = {"frames": [0, 1], "next": "loop"}
        group["loop"] = {"frames": [2, 3, 4, 5, 6, 7, 6, 5, 4, 3], "next": "loop"}
        group["exit"] = {"frames": [3, 2, 1, 0]}

    for group_id in (
        "side-lie-idle", "prone-idle", "sploot", "wand-loop",
        "freeze-dried-eating-loop", "cage-rest-12", "fur-groom-daily",
        "magic-scourgify",
    ):
        group = manifest["actionGroups"][group_id]
        base_frames = list(dict.fromkeys(group["frames"]))
        if len(base_frames) > 2:
            group["frames"] = base_frames + base_frames[-2:0:-1]
            group["frameCount"] = len(group["frames"])

    manifest["actionGroups"]["ask-walk"]["behaviorId"] = "social.ask_walk"
    manifest["atlases"]["activity"]["rowActions"][0] = "V19 低伏追逐激光点"
    manifest["atlases"]["activity"]["rowActions"][1] = "V19 低趴视线兼容"
    manifest["atlases"]["activity"]["rowActions"][7] = "V19 安静卧下兼容"
    manifest["atlases"]["specials"]["rowActions"] = [
        "V19 低趴视线兼容",
        "V19 Accio Broom 斗篷召唤与飞行",
        "V19 Apparate 弧光消失与重现",
        "V19 Petrificus 石化渐变与银币",
        "V19 Scourgify 挥爪与清洁光环",
    ]
    manifest["atlases"]["seasonal"]["rowActions"] = [
        "V19 圣诞帽低趴微动",
        "V19 万圣节斗篷帽低趴微动",
        "V19 春节红围巾低趴微动",
        "V19 主人生日帽与轻量彩纸",
    ]
    manifest["qualityRequirements"]["knownIssues"] = []
    manifest["qualityRequirements"]["frameExpansion"] = (
        "4 个生成关键姿态 + 4 个无重影呼吸/重心相位；禁止相邻重复帧"
    )

    manifest_path.write_text(
        json.dumps(manifest, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )

    referenced = {value["file"] for value in manifest["atlases"].values()}
    referenced.update(
        group.get("source", {}).get("file")
        for group in manifest["actionGroups"].values()
        if group.get("source", {}).get("file")
    )
    for path in ASSETS.rglob("*.png"):
        relative = path.relative_to(ASSETS).as_posix()
        if relative == "pupu-icon.png" or relative in referenced:
            continue
        path.unlink()

    repaired_files, repaired_pixels = V18_HELPERS.repair_runtime_assets()

    print("V19 assets rebuilt from identity-unified sources")
    print(f"Runtime manifest references {len(referenced)} PNG files")
    print("No packaged cat atlas is inherited from V10/V13/V15/V17/V18")
    print(f"Boundary despill repaired {repaired_files} files / {repaired_pixels} pixels")


if __name__ == "__main__":
    main()
