#!/usr/bin/env python3
from __future__ import annotations

import json
import hashlib
import sys
from pathlib import Path

import numpy as np
from PIL import Image
from scipy import ndimage

CELL = 256
MIN_SHORT_SIDE = 64
MIN_LONG_SIDE = 96
MIN_LAPLACIAN_VARIANCE = 70.0
MIN_TRANSPARENT_MARGIN = 20
MAX_MOVEMENT_SIZE_RATIO = 1.25
MAX_MOVEMENT_CENTROID_STEP = 10.0
MAX_EDGE_GREEN_RATE = 0.01
MAX_LEGACY_GREEN_RATE = 0.02
MOVEMENT_ROWS: dict[str, set[int] | None] = {
    "directions": None,
    "walkModes": None,
    "motion": set(range(8)),
}


def sharpness(rgb: np.ndarray, alpha: np.ndarray) -> float:
    gray = rgb.astype(np.float32).mean(axis=2)
    laplacian = (
        -4 * gray[1:-1, 1:-1]
        + gray[:-2, 1:-1]
        + gray[2:, 1:-1]
        + gray[1:-1, :-2]
        + gray[1:-1, 2:]
    )
    opaque = alpha[1:-1, 1:-1] > 128
    return float(laplacian[opaque].var()) if opaque.any() else 0.0


def main() -> int:
    root = Path(__file__).resolve().parents[1]
    assets = root / "Pupu.Desktop" / "Assets"
    manifest = json.loads((assets / "pupu-assets.json").read_text(encoding="utf-8"))
    failures: list[str] = []
    warnings: list[str] = []
    measurements: list[tuple[int, int, float]] = []
    focus_measurements: list[float] = []
    movement_size_ratios: list[float] = []
    movement_centroid_steps: list[float] = []
    edge_green_rates: list[float] = []
    v15_edge_green_rates: list[float] = []
    total = 0
    independent_total = 0
    for atlas_id, atlas in manifest["atlases"].items():
        image = np.asarray(Image.open(assets / atlas["file"]).convert("RGBA"))
        for row in range(atlas["rows"]):
            row_bounds: list[tuple[int, int]] = []
            row_centroids: list[tuple[float, float]] = []
            for column in range(atlas["columns"]):
                total += 1
                cell = image[
                    row * CELL : (row + 1) * CELL,
                    column * CELL : (column + 1) * CELL,
                ]
                alpha = cell[:, :, 3]
                visible = alpha > 16
                edge = visible & ~ndimage.binary_erosion(visible, iterations=3)
                rgb16 = cell[:, :, :3].astype(np.int16)
                green_dominance = rgb16[:, :, 1] - np.maximum(
                    rgb16[:, :, 0], rgb16[:, :, 2]
                )
                green_edge = edge & (green_dominance > 18) & (rgb16[:, :, 1] > 90)
                edge_green_rate = float(green_edge.sum() / max(1, edge.sum()))
                edge_green_rates.append(edge_green_rate)
                if any(version in atlas["file"] for version in ("-v15.png", "-v16.png", "-v17.png")):
                    v15_edge_green_rates.append(edge_green_rate)
                green_limit = (
                    MAX_EDGE_GREEN_RATE
                    if any(version in atlas["file"] for version in ("-v15.png", "-v16.png", "-v17.png"))
                    else MAX_LEGACY_GREEN_RATE
                )
                if edge_green_rate > green_limit:
                    failures.append(
                        f"{atlas_id} {row}:{column} green edge rate is "
                        f"{edge_green_rate:.2%}"
                    )
                ys, xs = np.where(alpha > 32)
                if not len(xs):
                    failures.append(f"{atlas_id} {row}:{column} empty")
                    continue
                width = int(xs.max() - xs.min() + 1)
                height = int(ys.max() - ys.min() + 1)
                left = int(xs.min())
                right = CELL - int(xs.max()) - 1
                top = int(ys.min())
                bottom = CELL - int(ys.max()) - 1
                focus = sharpness(cell[:, :, :3], alpha)
                row_bounds.append((width, height))
                row_centroids.append((float(xs.mean()), float(ys.mean())))
                is_intentional_coin_edge = (
                    atlas_id == "gazeCoin" and row == 2 and column in {2, 3, 6}
                )
                focus_measurements.append(focus)
                if not is_intentional_coin_edge:
                    measurements.append((min(width, height), max(width, height), focus))
                if (
                    not is_intentional_coin_edge
                    and (
                        min(width, height) < MIN_SHORT_SIDE
                        or max(width, height) < MIN_LONG_SIDE
                    )
                ):
                    failures.append(
                        f"{atlas_id} {row}:{column} effective subject is only {width}x{height}px"
                    )
                if focus < MIN_LAPLACIAN_VARIANCE:
                    failures.append(
                        f"{atlas_id} {row}:{column} is too soft ({focus:.1f})"
                    )
                if min(left, right, top, bottom) < MIN_TRANSPARENT_MARGIN:
                    failures.append(
                        f"{atlas_id} {row}:{column} transparent margin is "
                        f"{left}/{top}/{right}/{bottom}px"
                    )
            audited_rows = MOVEMENT_ROWS.get(atlas_id)
            is_movement_row = atlas_id in MOVEMENT_ROWS and (
                audited_rows is None or row in audited_rows
            )
            if is_movement_row and len(row_bounds) == atlas["columns"]:
                areas = [width * height for width, height in row_bounds]
                size_ratio = max(areas) / min(areas)
                movement_size_ratios.append(size_ratio)
                if size_ratio > MAX_MOVEMENT_SIZE_RATIO:
                    failures.append(
                        f"{atlas_id} row {row} apparent size jumps by {size_ratio:.3f}x"
                    )
                loop_centroids = row_centroids + row_centroids[:1]
                centroid_step = max(
                    float(np.hypot(x2 - x1, y2 - y1))
                    for (x1, y1), (x2, y2) in zip(
                        loop_centroids, loop_centroids[1:]
                    )
                )
                movement_centroid_steps.append(centroid_step)
                if centroid_step > MAX_MOVEMENT_CENTROID_STEP:
                    failures.append(
                        f"{atlas_id} row {row} centroid jumps by {centroid_step:.2f}px"
                    )
    schema = int(manifest.get("schemaVersion", 1))
    if schema not in (1, 2):
        failures.append(f"unsupported schemaVersion {schema}")
    required_coin_states = {
        "normalColor",
        "normalFaded",
        "unhappyColor",
        "unhappyFaded",
        "back",
    }
    coin_states = manifest.get("coinStates")
    if coin_states is not None:
        missing_coin_states = required_coin_states - set(coin_states)
        if missing_coin_states:
            failures.append(
                "coinStates missing " + ", ".join(sorted(missing_coin_states))
            )
    for group_id, group in manifest.get("actionGroups", {}).items():
        source = group.get("source") or {}
        if not group.get("behaviorId"):
            failures.append(f"action group {group_id} has no behaviorId")
        if int(group.get("frameCount") or len(group.get("frames") or []) or 1) < 1:
            failures.append(f"action group {group_id} has invalid frameCount")
        if int(group.get("frameDurationMs") or 600) < 40:
            failures.append(f"action group {group_id} frame duration is too short")
        if group.get("loopMode", "loop") not in {"once", "loop", "pingPong", "hold"}:
            failures.append(f"action group {group_id} has invalid loopMode")
        if not group.get("triggerConditions"):
            failures.append(f"action group {group_id} has no triggerConditions")
        if source.get("type", "atlasRow") == "atlasRow":
            atlas = manifest["atlases"].get(source.get("atlas"))
            row = int(source.get("row", 0))
            if atlas is None or row < 0 or row >= int(atlas["rows"]):
                failures.append(f"action group {group_id} has invalid atlas row")
        elif not source.get("file") and not group.get("fallback"):
            failures.append(
                f"action group {group_id} has neither source file nor fallback"
            )
    independent_sources: dict[str, tuple[int, int]] = {}
    for group in manifest.get("actionGroups", {}).values():
        source = group.get("source") or {}
        source_file = source.get("file")
        if source_file:
            independent_sources[source_file] = (
                int(source.get("frameWidth") or CELL),
                int(source.get("frameHeight") or CELL),
            )
    for source_file, (frame_width, frame_height) in independent_sources.items():
        path = assets / source_file
        if not path.exists():
            failures.append(f"independent action source is missing: {source_file}")
            continue
        image = np.asarray(Image.open(path).convert("RGBA"))
        if image.shape[0] != frame_height or image.shape[1] % frame_width:
            failures.append(f"independent action source has invalid grid: {source_file}")
            continue
        for frame_index in range(image.shape[1] // frame_width):
            independent_total += 1
            cell = image[:, frame_index * frame_width : (frame_index + 1) * frame_width]
            alpha = cell[:, :, 3]
            visible = alpha > 16
            edge = visible & ~ndimage.binary_erosion(visible, iterations=3)
            rgb16 = cell[:, :, :3].astype(np.int16)
            green_dominance = rgb16[:, :, 1] - np.maximum(
                rgb16[:, :, 0], rgb16[:, :, 2]
            )
            green_edge = edge & (green_dominance > 18) & (rgb16[:, :, 1] > 90)
            edge_green_rate = float(green_edge.sum() / max(1, edge.sum()))
            edge_green_rates.append(edge_green_rate)
            if "-v15.png" in source_file:
                v15_edge_green_rates.append(edge_green_rate)
            green_limit = (
                MAX_EDGE_GREEN_RATE
                if "-v15.png" in source_file
                else MAX_LEGACY_GREEN_RATE
            )
            if edge_green_rate > green_limit:
                failures.append(
                    f"{source_file} frame {frame_index} green edge rate is "
                    f"{edge_green_rate:.2%}"
                )
            ys, xs = np.where(alpha > 32)
            if not len(xs):
                failures.append(f"{source_file} frame {frame_index} empty")
                continue
            left = int(xs.min())
            right = frame_width - int(xs.max()) - 1
            top = int(ys.min())
            bottom = frame_height - int(ys.max()) - 1
            if min(left, right, top, bottom) < MIN_TRANSPARENT_MARGIN:
                failures.append(
                    f"{source_file} frame {frame_index} transparent margin is "
                    f"{left}/{top}/{right}/{bottom}px"
                )
            focus = sharpness(cell[:, :, :3], alpha)
            if focus < MIN_LAPLACIAN_VARIANCE:
                failures.append(
                    f"{source_file} frame {frame_index} is too soft ({focus:.1f})"
                )
        if "chase-gait-8dir" in source_file:
            frame_count = image.shape[1] // frame_width
            if frame_count != 32:
                failures.append(
                    f"{source_file} must contain 8 directions x 4 gait phases"
                )
            else:
                for direction in range(8):
                    lower_body_hashes: set[str] = set()
                    for phase in range(4):
                        index = direction * 4 + phase
                        frame = image[
                            :,
                            index * frame_width : (index + 1) * frame_width,
                        ]
                        lower = frame[int(frame_height * 0.52) :, :, :]
                        lower_body_hashes.add(hashlib.sha256(lower.tobytes()).hexdigest())
                    if len(lower_body_hashes) < 3:
                        failures.append(
                            f"{source_file} direction {direction} has frozen feet"
                        )

    gaze_coin = manifest["atlases"].get("gazeCoin")
    if gaze_coin:
        coin_image = np.asarray(Image.open(assets / gaze_coin["file"]).convert("RGBA"))
        for state_name in ("normalColor", "normalFaded", "unhappyColor", "unhappyFaded"):
            state = (coin_states or {}).get(state_name, {})
            for frame in state.get("frames", []):
                cell = coin_image[2 * CELL : 3 * CELL, frame * CELL : (frame + 1) * CELL]
                corners = (
                    cell[:24, :24, 3],
                    cell[:24, -24:, 3],
                    cell[-24:, :24, 3],
                    cell[-24:, -24:, 3],
                )
                if any(int(corner.max(initial=0)) > 0 for corner in corners):
                    failures.append(
                        f"gazeCoin front state {state_name} retains an opaque corner"
                    )
    for issue in manifest.get("qualityRequirements", {}).get("knownIssues", []):
        warnings.append(f"KNOWN ASSET ISSUE: {issue}")

    if failures:
        print("\n".join(failures), file=sys.stderr)
        return 2
    minimum_short = min(value[0] for value in measurements)
    minimum_long = min(value[1] for value in measurements)
    minimum_focus = min(focus_measurements)
    maximum_size_ratio = max(movement_size_ratios)
    maximum_centroid_step = max(movement_centroid_steps)
    maximum_edge_green_rate = max(edge_green_rates)
    maximum_v15_edge_green_rate = max(v15_edge_green_rates, default=0.0)
    print(
        f"Audited {total} atlas cells and {independent_total} independent frames: effective subject >= "
        f"{minimum_short}x{minimum_long}px; minimum focus {minimum_focus:.1f}; "
        f"movement size drift <= {maximum_size_ratio:.3f}x; "
        f"centroid step <= {maximum_centroid_step:.2f}px; "
        f"V15 green edge <= {maximum_v15_edge_green_rate:.2%} "
        f"(legacy reference maximum {maximum_edge_green_rate:.2%}); "
        "pursuit gait 8 directions x 4 phases."
    )
    if warnings:
        print("\n".join(warnings))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
