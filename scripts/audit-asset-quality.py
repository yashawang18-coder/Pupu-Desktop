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
MAX_EDGE_GREEN_RATE = 0.003
# V10/V13 seasonal and magic props intentionally contain saturated green
# clothing/light effects. They remain on the legacy detector; only the
# V12/V15/V17/V18/V19 chroma-derived silhouette set uses the strict despill gate.
MAX_LEGACY_GREEN_RATE = 0.04
MAX_LOOP_CLOSURE_RATIO = 2.0
MAX_LOOP_CLOSURE_DISTANCE = 0.20
STRICT_EDGE_VERSIONS = ("-v12.png", "-v15.png", "-v17.png", "-v18.png", "-v19.png")
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


def load_png(path: Path) -> np.ndarray:
    data = path.read_bytes()
    iend = b"\x00\x00\x00\x00IEND\xaeB\x60\x82"
    if not data.startswith(b"\x89PNG\r\n\x1a\n") or not data.endswith(iend):
        raise ValueError(f"{path} is truncated or has no terminal PNG IEND chunk")
    with Image.open(path) as image:
        image.load()
        return np.asarray(image.convert("RGBA"))


def strict_edge_file(path: str) -> bool:
    return any(version in path for version in STRICT_EDGE_VERSIONS)


def green_edge_rate(cell: np.ndarray, strict: bool) -> float:
    alpha = cell[:, :, 3]
    visible = alpha > 8
    edge = visible & ~ndimage.binary_erosion(visible, iterations=3)
    rgb = cell[:, :, :3].astype(np.int16)
    dominance = rgb[:, :, 1] - np.maximum(rgb[:, :, 0], rgb[:, :, 2])
    threshold = 6 if strict else 18
    minimum_green = 45 if strict else 90
    green_edge = edge & (dominance > threshold) & (rgb[:, :, 1] > minimum_green)
    return float(green_edge.sum() / max(1, edge.sum()))


def frame_distance(first: np.ndarray, second: np.ndarray) -> float:
    first_alpha = first[..., 3:4].astype(np.float32) / 255.0
    second_alpha = second[..., 3:4].astype(np.float32) / 255.0
    first_premultiplied = np.concatenate(
        (first[..., :3].astype(np.float32) * first_alpha, first[..., 3:4]), axis=2
    )
    second_premultiplied = np.concatenate(
        (second[..., :3].astype(np.float32) * second_alpha, second[..., 3:4]), axis=2
    )
    union = (first_alpha[..., 0] > 0.03) | (second_alpha[..., 0] > 0.03)
    if not union.any():
        return 0.0
    return float(np.abs(first_premultiplied - second_premultiplied)[union].mean() / 255.0)


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
    strict_edge_green_rates: list[float] = []
    loop_closure_ratios: list[float] = []
    total = 0
    independent_total = 0
    loaded_images: dict[Path, np.ndarray] = {}

    def load_cached(path: Path) -> np.ndarray:
        if path not in loaded_images:
            loaded_images[path] = load_png(path)
        return loaded_images[path]

    for atlas_id, atlas in manifest["atlases"].items():
        image = load_cached(assets / atlas["file"])
        strict_edges = strict_edge_file(atlas["file"])
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
                edge_green_rate = green_edge_rate(cell, strict_edges)
                edge_green_rates.append(edge_green_rate)
                if strict_edges:
                    strict_edge_green_rates.append(edge_green_rate)
                green_limit = MAX_EDGE_GREEN_RATE if strict_edges else MAX_LEGACY_GREEN_RATE
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

    def action_frame(group: dict, frame_index: int) -> np.ndarray:
        source = group.get("source") or {}
        if source.get("type", "atlasRow") == "atlasRow":
            atlas = manifest["atlases"][source["atlas"]]
            sheet = load_cached(assets / atlas["file"])
            row = int(source.get("row", 0))
            return sheet[row * CELL : (row + 1) * CELL,
                         frame_index * CELL : (frame_index + 1) * CELL]
        frame_width = int(source.get("frameWidth") or CELL)
        frame_height = int(source.get("frameHeight") or CELL)
        sheet = load_cached(assets / source["file"])
        if source.get("vertical"):
            return sheet[frame_index * frame_height : (frame_index + 1) * frame_height,
                         :frame_width]
        return sheet[:frame_height,
                     frame_index * frame_width : (frame_index + 1) * frame_width]

    for group_id, group in manifest.get("actionGroups", {}).items():
        loop_mode = group.get("loopMode", "loop")
        if loop_mode not in {"loop", "pingPong"}:
            continue
        sequences = [list(group.get("frames") or [])]
        directions = group.get("directions") or {}
        if directions:
            sequences = [list(value.get("frames") or []) for value in directions.values()]
        for sequence in sequences:
            if len(sequence) < 2:
                continue
            if loop_mode == "pingPong" and len(sequence) > 2:
                sequence = sequence + list(reversed(sequence[1:-1]))
            frames = [action_frame(group, frame) for frame in sequence]
            distances = [
                frame_distance(first, second)
                for first, second in zip(frames, frames[1:] + frames[:1])
            ]
            internal = distances[:-1] or distances
            median_internal = float(np.median(internal))
            ratio = distances[-1] / max(0.001, median_internal)
            loop_closure_ratios.append(ratio)
            if (
                distances[-1] > MAX_LOOP_CLOSURE_DISTANCE
                and ratio > MAX_LOOP_CLOSURE_RATIO
            ):
                failures.append(
                    f"action group {group_id} loop closes with a visible jump "
                    f"({distances[-1]:.3f}, {ratio:.2f}x median transition)"
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
        image = load_cached(path)
        strict_edges = strict_edge_file(source_file)
        if image.shape[0] != frame_height or image.shape[1] % frame_width:
            failures.append(f"independent action source has invalid grid: {source_file}")
            continue
        for frame_index in range(image.shape[1] // frame_width):
            independent_total += 1
            cell = image[:, frame_index * frame_width : (frame_index + 1) * frame_width]
            alpha = cell[:, :, 3]
            visible = alpha > 16
            edge_green_rate = green_edge_rate(cell, strict_edges)
            edge_green_rates.append(edge_green_rate)
            if strict_edges:
                strict_edge_green_rates.append(edge_green_rate)
            green_limit = MAX_EDGE_GREEN_RATE if strict_edges else MAX_LEGACY_GREEN_RATE
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
            if frame_count != 64:
                failures.append(
                    f"{source_file} must contain 8 directions x 8 gait phases"
                )
            else:
                for direction in range(8):
                    lower_body_hashes: set[str] = set()
                    for phase in range(8):
                        index = direction * 8 + phase
                        frame = image[
                            :,
                            index * frame_width : (index + 1) * frame_width,
                        ]
                        lower = frame[int(frame_height * 0.52) :, :, :]
                        lower_body_hashes.add(hashlib.sha256(lower.tobytes()).hexdigest())
                    # The source provides two photographed foot swaps. V19
                    # must expose at least four distinct lower-body rasters
                    # across eight clean display phases; all eight complete
                    # frames are also covered by the adjacent-frame gate.
                    if len(lower_body_hashes) < 4:
                        failures.append(
                            f"{source_file} direction {direction} has frozen feet"
                        )

    gaze_coin = manifest["atlases"].get("gazeCoin")
    if gaze_coin:
        coin_image = load_cached(assets / gaze_coin["file"])
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
    maximum_strict_edge_green_rate = max(strict_edge_green_rates, default=0.0)
    maximum_loop_closure_ratio = max(loop_closure_ratios, default=0.0)
    print(
        f"Audited {total} atlas cells and {independent_total} independent frames: effective subject >= "
        f"{minimum_short}x{minimum_long}px; minimum focus {minimum_focus:.1f}; "
        f"movement size drift <= {maximum_size_ratio:.3f}x; "
        f"centroid step <= {maximum_centroid_step:.2f}px; "
        f"V12/V15/V17/V18/V19 green edge <= {maximum_strict_edge_green_rate:.2%}; "
        f"(legacy reference maximum {maximum_edge_green_rate:.2%}); "
        f"loop closure <= {maximum_loop_closure_ratio:.2f}x median; "
        "pursuit gait 8 directions x 8 display phases."
    )
    if warnings:
        print("\n".join(warnings))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
