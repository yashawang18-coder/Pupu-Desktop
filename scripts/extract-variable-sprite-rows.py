#!/usr/bin/env python3
"""Extract sprite rows whose generated frame counts differ by row."""

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
    parser.add_argument(
        "--row-columns",
        required=True,
        help="Comma-separated frame count for each visual row, for example 7,7,8.",
    )
    return parser.parse_args()


def main() -> None:
    args = parse_args()
    row_columns = [int(value) for value in args.row_columns.split(",")]
    if not row_columns or any(value < 1 for value in row_columns):
        raise ValueError("row-columns must contain positive integers")

    args.output.mkdir(parents=True, exist_ok=True)
    rgba = np.asarray(Image.open(args.input).convert("RGBA"))
    alpha = rgba[..., 3]
    labels, _ = ndimage.label(
        alpha > 12, structure=np.ones((3, 3), dtype=np.uint8)
    )

    components: list[dict[str, object]] = []
    for label_id, slices in enumerate(ndimage.find_objects(labels), start=1):
        if slices is None:
            continue
        selected = labels[slices] == label_id
        area = int(selected.sum())
        if area < 500:
            continue
        ys, xs = np.where(labels == label_id)
        components.append(
            {
                "id": label_id,
                "area": area,
                "cx": float(xs.mean()),
                "cy": float(ys.mean()),
            }
        )

    expected_total = sum(row_columns)
    if len(components) != expected_total:
        raise RuntimeError(
            f"detected {len(components)} complete sprites, expected {expected_total}"
        )

    by_y = sorted(components, key=lambda component: float(component["cy"]))
    rows: list[list[dict[str, object]]] = []
    cursor = 0
    for expected_columns in row_columns:
        row = by_y[cursor : cursor + expected_columns]
        cursor += expected_columns
        rows.append(sorted(row, key=lambda component: float(component["cx"])))

    stride = max(row_columns)
    for row_index, row in enumerate(rows):
        for column, component in enumerate(row):
            label_id = int(component["id"])
            selected = labels == label_id
            selected = ndimage.binary_dilation(selected, iterations=1)
            selected &= alpha > 0
            ys, xs = np.where(selected)
            x0 = max(0, int(xs.min()) - 2)
            y0 = max(0, int(ys.min()) - 2)
            x1 = min(rgba.shape[1], int(xs.max()) + 3)
            y1 = min(rgba.shape[0], int(ys.max()) + 3)
            crop = rgba[y0:y1, x0:x1].copy()
            crop[~selected[y0:y1, x0:x1]] = 0
            index = row_index * stride + column
            Image.fromarray(crop, mode="RGBA").save(
                args.output / f"cell-{index:03d}-trim.png", optimize=True
            )

    print(
        f"Extracted {len(rows)} variable rows with {expected_total} complete sprites "
        f"from {args.input.name}."
    )


if __name__ == "__main__":
    main()
