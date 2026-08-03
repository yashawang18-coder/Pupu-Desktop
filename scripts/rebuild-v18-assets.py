#!/usr/bin/env python3
"""Rebuild the V18 Pupu runtime atlases from the approved identity sheets.

V18 replaces every row named by the owner while preserving unrelated, already
approved rows.  Generated 4x4 sheets are keyed to transparency, normalized on a
shared body coordinate system and expanded to eight-frame atlas rows.  Snack
and laser pursuit are deliberately separate 8-direction strips; every
direction has four distinct gait phases.
"""

from __future__ import annotations

import importlib.util
import json
from pathlib import Path

import numpy as np
from PIL import Image

from repair_runtime_assets import repair_runtime_assets


ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / "Pupu.Desktop" / "Assets"
SOURCES = ROOT / "AssetSources" / "v18"
CELL = 256


def load_v15_helpers():
    helper_path = ROOT / "scripts" / "rebuild-v15-assets.py"
    spec = importlib.util.spec_from_file_location("pupu_v15_helpers", helper_path)
    if spec is None or spec.loader is None:
        raise RuntimeError(f"Unable to load asset helpers: {helper_path}")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


H = load_v15_helpers()


def split_grid(path: Path) -> list[list[Image.Image]]:
    image = Image.open(path).convert("RGB")
    keyed = H.key_cell(image)
    alpha = np.asarray(keyed.getchannel("A"))

    def cuts(occupancy: np.ndarray, size: int) -> list[int]:
        smoothed = np.convolve(
            occupancy.astype(np.float64), np.ones(7, dtype=np.float64), mode="same"
        )
        result = [0]
        radius = max(24, round(size / 18))
        for index in range(1, 4):
            target = round(index * size / 4)
            low = max(result[-1] + 8, target - radius)
            high = min(size - 8, target + radius)
            candidates = np.arange(low, high + 1)
            values = smoothed[candidates]
            minimum = values.min()
            best = candidates[values == minimum]
            result.append(int(best[np.argmin(np.abs(best - target))]))
        result.append(size)
        return result

    y_cuts = cuts((alpha > 8).sum(axis=1), image.height)
    x_cuts = cuts((alpha > 8).sum(axis=0), image.width)
    rows: list[list[Image.Image]] = []
    for row in range(4):
        row_frames: list[Image.Image] = []
        y0, y1 = y_cuts[row], y_cuts[row + 1]
        for column in range(4):
            x0, x1 = x_cuts[column], x_cuts[column + 1]
            cell = np.asarray(keyed.crop((x0, y0, x1, y1))).copy()
            cell[..., 3] = H.keep_subject(cell[..., 3])
            cell[cell[..., 3] == 0, :3] = 0
            row_frames.append(Image.fromarray(H.depollute_edges(cell), "RGBA"))
        rows.append(row_frames)
    return rows


def normalize_four(row: list[Image.Image], *, max_width: int = 216,
                   max_height: int = 216) -> list[Image.Image]:
    return H.normalize_row(row, max_width=max_width, max_height=max_height)


def expand_row(row: list[Image.Image], *, max_width: int = 216,
               max_height: int = 216) -> list[Image.Image]:
    frames = normalize_four(row, max_width=max_width, max_height=max_height)
    order = (0, 1, 2, 3, 2, 1, 0, 1)
    return [frames[index].copy() for index in order]


def align_row_centroids(frames: list[Image.Image]) -> list[Image.Image]:
    """Align the visible body centre without violating the 20px safe area."""
    measurements: list[tuple[float, tuple[int, int, int, int]]] = []
    for frame in frames:
        alpha = np.asarray(frame.getchannel("A"))
        ys, xs = np.nonzero(alpha > 8)
        if len(xs) == 0:
            raise ValueError("Cannot align an empty frame")
        measurements.append(
            (
                float(xs.mean()),
                (int(xs.min()), int(ys.min()), int(xs.max() + 1), int(ys.max() + 1)),
            )
        )
    target = float(np.median([value for value, _ in measurements]))
    result: list[Image.Image] = []
    for frame, (centre, box) in zip(frames, measurements):
        requested = int(round(target - centre))
        minimum = 20 - box[0]
        maximum = (CELL - 20) - box[2]
        dx = max(minimum, min(maximum, requested))
        canvas = Image.new("RGBA", (CELL, CELL), (0, 0, 0, 0))
        canvas.alpha_composite(frame, (dx, 0))
        result.append(canvas)
    return result


def offset_phase(frame: Image.Image, dx: int, dy: int) -> Image.Image:
    """Create a clean body-bob phase without cross-fade ghosts."""
    canvas = Image.new("RGBA", (CELL, CELL), (0, 0, 0, 0))
    canvas.alpha_composite(frame, (dx, dy))
    return canvas


def build_pursuit_strip(sheet: list[list[Image.Image]]) -> list[list[Image.Image]]:
    source = [frame for row in sheet for frame in row]
    if len(source) != 16:
        raise ValueError(f"Expected 16 pursuit keys, got {len(source)}")
    normalized = H.normalize_row(source, max_width=210, max_height=208)
    output: list[Image.Image] = []
    for direction in range(8):
        first = normalized[direction * 2]
        second = normalized[direction * 2 + 1]
        # A -> body lift -> B -> body settle.  The two generated keys carry the
        # real foot swap; clean 1-2px bobs supply the intermediate phases.  Do
        # not cross-fade full bodies here: that creates visible double-image
        # ghosts on fur, eyes and tail.
        output.extend(
            (
                first.copy(),
                offset_phase(first, 1, -2),
                second.copy(),
                offset_phase(second, -1, 1),
            )
        )
    return [output]


def load_atlas(atlas_id: str, rows: int) -> list[list[Image.Image]]:
    manifest = json.loads((ASSETS / "pupu-assets.json").read_text(encoding="utf-8"))
    filename = manifest["atlases"][atlas_id]["file"]
    return H.clean_runtime_atlas(ASSETS / filename, rows, 8)


def main() -> None:
    idle = split_grid(SOURCES / "pupu-idle-v18-chroma.png")
    play = split_grid(SOURCES / "pupu-play-v18-chroma.png")
    sleep = split_grid(SOURCES / "pupu-sleep-v18-chroma.png")
    groom = split_grid(SOURCES / "pupu-groom-v18-chroma.png")
    interaction = split_grid(SOURCES / "pupu-interaction-v18-chroma.png")
    diagonal = split_grid(SOURCES / "pupu-diagonal-gait-v18-chroma.png")
    litter = split_grid(SOURCES / "pupu-litter-v18-chroma.png")
    snack_chase = split_grid(SOURCES / "pupu-snack-chase-v18-chroma.png")
    laser_chase = split_grid(SOURCES / "pupu-laser-chase-v18-chroma.png")

    core_rows = load_atlas("core", 6)
    core_rows[0] = expand_row(idle[1])              # prone breathing
    core_rows[1] = expand_row(play[0])              # side roll
    core_rows[2] = expand_row(play[1])              # tail chase
    core_rows[3] = expand_row(play[3])              # wand play
    core_rows[4] = expand_row(play[2])              # blink/yawn
    core_file = "pupu-core-youthful-v18.png"
    H.save_atlas(core_rows, ASSETS / core_file)

    routines_rows = load_atlas("routines", 8)
    routines_rows[0] = expand_row(idle[0])          # side breathing
    routines_rows[1] = expand_row(idle[2])          # low prone observe
    routines_rows[2] = expand_row(groom[2])         # paw nibble
    routines_rows[4] = expand_row(interaction[1])   # freeze-dried rush/eat
    routines_file = "pupu-routines-youthful-v18.png"
    H.save_atlas(routines_rows, ASSETS / routines_file)

    activity_rows = load_atlas("activity", 8)
    activity_rows[0] = expand_row(interaction[2])   # laser pounce
    activity_rows[2] = expand_row(sleep[0])         # curled sleep
    activity_rows[3] = expand_row(sleep[1])         # belly-up sleep
    activity_rows[4] = expand_row(sleep[2])         # side-stretch sleep
    activity_rows[5] = expand_row(sleep[3])         # prone transition sleep
    activity_rows[6] = expand_row(idle[3])          # sploot
    activity_file = "pupu-activity-youthful-v18.png"
    H.save_atlas(activity_rows, ASSETS / activity_file)

    equipment_rows = load_atlas("lifeEquipment", 3)
    equipment_rows[0] = H.normalize_row(groom[0] + groom[1])
    equipment_rows[1] = expand_row(interaction[3], max_width=216, max_height=204)
    equipment_rows[2] = expand_row(interaction[0], max_width=212, max_height=214)
    equipment_file = "pupu-life-equipment-youthful-v18.png"
    H.save_atlas(equipment_rows, ASSETS / equipment_file)

    life_rows = load_atlas("life", 8)
    life_rows[6] = expand_row(sleep[0])
    life_rows[7] = expand_row(interaction[0], max_width=212, max_height=214)
    life_file = "pupu-life-youthful-v18.png"
    H.save_atlas(life_rows, ASSETS / life_file)

    litter_rows = load_atlas("litter", 4)
    for index in range(4):
        litter_rows[index] = expand_row(litter[index], max_width=216, max_height=208)
    litter_file = "pupu-litter-youthful-v18.png"
    H.save_atlas(litter_rows, ASSETS / litter_file)

    motion_rows = load_atlas("motion", 10)
    motion_rows[0] = align_row_centroids(
        expand_row(diagonal[2], max_width=202, max_height=212))
    motion_rows[1] = align_row_centroids(
        expand_row(diagonal[3], max_width=202, max_height=212))
    motion_rows[4] = align_row_centroids(
        expand_row(diagonal[0], max_width=202, max_height=212))
    motion_rows[5] = align_row_centroids(
        expand_row(diagonal[1], max_width=202, max_height=212))
    motion_rows[9] = expand_row(idle[0])
    motion_file = "pupu-motion-youthful-v18.png"
    H.save_atlas(motion_rows, ASSETS / motion_file)

    snack_file = "Actions/pupu-snack-chase-gait-8dir-youthful-v18.png"
    laser_file = "Actions/pupu-laser-chase-gait-8dir-youthful-v18.png"
    H.save_atlas(build_pursuit_strip(snack_chase), ASSETS / snack_file)
    H.save_atlas(build_pursuit_strip(laser_chase), ASSETS / laser_file)

    manifest_path = ASSETS / "pupu-assets.json"
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    manifest["version"] = "1.11.1-v18-identity-unified-interactions"
    replacements = {
        "core": core_file,
        "life": life_file,
        "routines": routines_file,
        "activity": activity_file,
        "lifeEquipment": equipment_file,
        "motion": motion_file,
        "litter": litter_file,
    }
    for atlas_id, filename in replacements.items():
        manifest["atlases"][atlas_id]["file"] = filename

    chase_files = {
        "laser-chase-8": laser_file,
        "snack-chase-8": snack_file,
    }
    direction_names = (
        "left", "upLeft", "up", "upRight",
        "right", "downRight", "down", "downLeft",
    )
    for group_id, filename in chase_files.items():
        group = manifest["actionGroups"][group_id]
        group["source"] = {
            "type": "spriteStrip",
            "file": filename,
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
            name: {
                "frames": [
                    index * 4,
                    index * 4 + 1,
                    index * 4 + 2,
                    index * 4 + 3,
                    index * 4 + 2,
                    index * 4 + 1,
                ]
            }
            for index, name in enumerate(direction_names)
        }
        group["triggerConditions"] = [
            condition
            for condition in group["triggerConditions"]
            if not condition.startswith("每个方向包含四个")
            and not condition.startswith("每方向四个独立源相位")
        ]
        group["triggerConditions"].append(
            "每方向四个独立源相位按 0-1-2-3-2-1 往返播放；窗口位移严格随换帧推进"
        )

    manifest["actionGroups"]["fur-groom-daily"] = {
        "groupId": "fur-groom-daily",
        "behaviorId": "self.groom",
        "source": {
            "type": "atlasRow",
            "atlas": "lifeEquipment",
            "row": 0,
        },
        "frameCount": 8,
        "frameDurationMs": 860,
        "frameDurationsMs": [980, 760, 820, 720, 900, 760, 860, 1120],
        "frames": list(range(8)),
        "loopMode": "pingPong",
        "fallback": "prone-idle",
        "behaviorTags": ["self_care", "groom", "quiet"],
        "triggerConditions": [
            "自主舔毛经 BehaviorArbitrator 选中",
            "往返播放避免末帧直接跳回首帧",
            "原地动作不移动窗口",
        ],
    }

    manifest_path.write_text(
        json.dumps(manifest, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )

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

    repaired_files, repaired_pixels = repair_runtime_assets()

    print("V18 assets rebuilt")
    print(f"Runtime manifest references {len(referenced)} image files")
    print(f"Laser and snack pursuit use separate sources: {laser_file} / {snack_file}")
    print(
        f"Runtime despill repaired {repaired_files} files / "
        f"{repaired_pixels} boundary pixels"
    )


if __name__ == "__main__":
    main()
