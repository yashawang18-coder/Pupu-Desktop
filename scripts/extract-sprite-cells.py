#!/usr/bin/env python3
"""Extract complete sprites without cutting subjects at nominal grid lines."""

from __future__ import annotations

import argparse
from pathlib import Path

import numpy as np
from PIL import Image
from scipy import ndimage


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--input", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--columns", required=True, type=int)
    parser.add_argument("--rows", required=True, type=int)
    return parser.parse_args()


def resolve_row_centers(components: list[dict[str, object]], rows: int) -> list[float]:
    anchors = sorted(float(component["cy"]) for component in components if int(component["area"]) >= 1000)
    if len(anchors) < rows:
        raise RuntimeError(f"only {len(anchors)} row anchors for {rows} rows")
    gaps = sorted(
        ((anchors[index + 1] - anchors[index], index) for index in range(len(anchors) - 1)),
        reverse=True,
    )
    cuts = sorted(index for _, index in gaps[: rows - 1])
    groups: list[list[float]] = []
    start = 0
    for cut in cuts:
        groups.append(anchors[start : cut + 1])
        start = cut + 1
    groups.append(anchors[start:])
    if len(groups) != rows or any(not group for group in groups):
        raise RuntimeError("could not resolve visual sprite rows")
    return [float(np.median(group)) for group in groups]


def main() -> None:
    args = parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    rgba = np.asarray(Image.open(args.input).convert("RGBA"))
    alpha = rgba[..., 3]
    labels, component_count = ndimage.label(alpha > 12, structure=np.ones((3, 3), dtype=np.uint8))

    components: list[dict[str, object]] = []
    for label_id, slices in enumerate(ndimage.find_objects(labels), start=1):
        if slices is None:
            continue
        area = int((labels[slices] == label_id).sum())
        if area < 30:
            continue
        ys, xs = np.where(labels == label_id)
        components.append(
            {"id": label_id, "area": area, "cx": float(xs.mean()), "cy": float(ys.mean())}
        )

    centers_y = resolve_row_centers(components, args.rows)
    centers_x = [(column + 0.5) * rgba.shape[1] / args.columns for column in range(args.columns)]
    assigned: list[list[list[int]]] = [[[] for _ in range(args.columns)] for _ in range(args.rows)]
    areas = np.zeros((args.rows, args.columns), dtype=np.int64)

    for component in components:
        row = min(range(args.rows), key=lambda index: abs(float(component["cy"]) - centers_y[index]))
        column = min(range(args.columns), key=lambda index: abs(float(component["cx"]) - centers_x[index]))
        assigned[row][column].append(int(component["id"]))
        areas[row, column] += int(component["area"])

    # Long generated rows occasionally omit one in-between phase. Fill only
    # an empty nominal slot from its nearest complete neighbor; the atlas row
    # builder later adds a deliberate forward/back phase for frame eight.
    for row in range(args.rows):
        available = [column for column in range(args.columns) if areas[row, column] >= 500]
        if not available:
            raise RuntimeError(f"row {row} contains no complete sprite")
        for column in range(args.columns):
            if areas[row, column] >= 500:
                continue
            nearest = min(available, key=lambda candidate: abs(candidate - column))
            assigned[row][column] = list(assigned[row][nearest])
            areas[row, column] = areas[row, nearest]

    for row in range(args.rows):
        for column in range(args.columns):
            selected = np.isin(labels, assigned[row][column])
            selected = ndimage.binary_dilation(selected, iterations=1)
            selected &= alpha > 0
            ys, xs = np.where(selected)
            if not len(xs):
                raise RuntimeError(f"empty extracted cell {row}:{column}")
            x0 = max(0, int(xs.min()) - 2)
            y0 = max(0, int(ys.min()) - 2)
            x1 = min(rgba.shape[1], int(xs.max()) + 3)
            y1 = min(rgba.shape[0], int(ys.max()) + 3)
            crop = rgba[y0:y1, x0:x1].copy()
            crop_mask = selected[y0:y1, x0:x1]
            crop[~crop_mask] = 0
            index = row * args.columns + column
            Image.fromarray(crop, mode="RGBA").save(
                args.output / f"cell-{index:03d}-trim.png", optimize=True
            )

    print(
        f"Extracted {args.columns}x{args.rows} complete cells from {args.input.name}; "
        f"detected {component_count} alpha components."
    )


if __name__ == "__main__":
    main()
