#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
WORK="$ROOT/.asset-work/v5/build"
SOURCE="$ROOT/AssetSources/v5"
OUT="$ROOT/Pupu.Desktop/Assets"
KEY_HELPER="/root/.codex/skills/.system/imagegen/scripts/remove_chroma_key.py"
CELL=256
VISIBLE=216
BOTTOM=20

command -v convert >/dev/null
command -v identify >/dev/null
test -f "$KEY_HELPER"
mkdir -p "$WORK" "$OUT"

prepare_source() {
  local name="$1" input="$2" cols="$3" rows="$4"
  local keyed="$WORK/$name/keyed.png"
  local split="$WORK/$name/split"
  rm -rf "$WORK/$name"
  mkdir -p "$split"

  python "$KEY_HELPER" \
    --input "$input" \
    --out "$keyed" \
    --key-color '#00ff00' \
    --soft-matte \
    --transparent-threshold 28 \
    --opaque-threshold 180 \
    --despill \
    --edge-contract 2 \
    --edge-feather 0.6 \
    --force

  convert "$keyed" -crop "${cols}x${rows}@" +repage "$split/cell-%03d.png"
  local file
  for file in "$split"/cell-*.png; do
    # Generated separators vary by a few pixels on non-square source grids.
    # Remove a nine-pixel inner guard band before trimming so no separator,
    # antialias halo, or key-colored fleck survives into a final cell.
    convert "$file" -shave 9x9 -trim +repage "${file%.png}-trim.png"
  done
}

cell_path() {
  local source_name="$1" source_cols="$2" source_row="$3" source_frame="$4"
  local index
  if (( source_row < 0 )); then
    index="$source_frame"
  else
    index=$((source_row * source_cols + source_frame))
  fi
  printf '%s/%s/split/cell-%03d-trim.png' "$WORK" "$source_name" "$index"
}

build_row() {
  local output_atlas="$1" output_row="$2" source_name="$3" source_cols="$4" source_row="$5"
  shift 5
  local frames=("$@")
  test "${#frames[@]}" -eq 8

  local row_dir="$WORK/output/$output_atlas/row-$output_row"
  rm -rf "$row_dir"
  mkdir -p "$row_dir"
  local max_w=1 max_h=1 i=0 frame path dims w h
  for frame in "${frames[@]}"; do
    path="$(cell_path "$source_name" "$source_cols" "$source_row" "$frame")"
    test -f "$path"
    dims="$(identify -format '%w %h' "$path")"
    read -r w h <<<"$dims"
    (( w > max_w )) && max_w="$w"
    (( h > max_h )) && max_h="$h"
  done

  local percent
  percent="$(awk -v visible="$VISIBLE" -v w="$max_w" -v h="$max_h" 'BEGIN { a=visible/w; b=visible/h; s=(a<b?a:b); printf "%.4f", s*100 }')"
  local outputs=()
  for frame in "${frames[@]}"; do
    path="$(cell_path "$source_name" "$source_cols" "$source_row" "$frame")"
    local normalized="$row_dir/frame-$(printf '%02d' "$i").png"
    # Preserve the naturally soft fur contour. A second unsharp pass creates
    # a dark halo around grey fur after chroma removal, especially on bright
    # wallpapers, so the atlas keeps the high-quality Lanczos resample only.
    convert -size "${CELL}x${CELL}" xc:none \
      \( "$path" -filter Lanczos -resize "${percent}%" \) \
      -gravity south -geometry "+0+${BOTTOM}" -compose over -composite \
      -define png:color-type=6 "$normalized"
    outputs+=("$normalized")
    i=$((i + 1))
  done

  local row_output="$WORK/output/$output_atlas/row-$(printf '%02d' "$output_row").png"
  local row_temp="$row_output.tmp.png"
  convert "${outputs[@]}" +append -depth 8 -strip -define png:color-type=6 -define png:compression-level=9 "$row_temp"
  test "$(identify -format '%wx%h' "$row_temp")" = "$((CELL * 8))x$CELL"
  mv "$row_temp" "$row_output"
}

finish_atlas() {
  local name="$1" rows="$2" output="$3"
  local inputs=() r
  for ((r=0; r<rows; r++)); do
    inputs+=("$WORK/output/$name/row-$(printf '%02d' "$r").png")
  done
  local temp="$output.tmp.png"
  convert "${inputs[@]}" -append -depth 8 -strip -define png:color-type=6 -define png:compression-level=9 "$temp"
  test "$(identify -format '%wx%h' "$temp")" = "$((CELL * 8))x$((CELL * rows))"
  mv "$temp" "$output"
  sync -f "$output"
}

prepare_source core "$SOURCE/pupu-core-v5-chroma.png" 8 6
prepare_source touch "$SOURCE/pupu-touch-v5-chroma.png" 8 6
prepare_source life "$SOURCE/pupu-life-v5-chroma.png" 7 7
prepare_source directions "$SOURCE/pupu-directions-v5-chroma.png" 8 4
prepare_source routines "$SOURCE/pupu-routines-v5-chroma.png" 7 8
prepare_source walk "$SOURCE/pupu-walk-modes-v5-chroma.png" 6 8
prepare_source mischief "$SOURCE/pupu-mischief-v5-chroma.png" 4 2

mkdir -p "$WORK/output"/{core,touch,life,directions,routines,walkModes}

for row in {0..5}; do build_row core "$row" core 8 "$row" 0 1 2 3 4 5 6 7; done
for row in {0..5}; do build_row touch "$row" touch 8 "$row" 0 1 2 3 4 5 6 7; done
for row in {0..3}; do build_row directions "$row" directions 8 "$row" 0 1 2 3 4 5 6 7; done
for row in {0..7}; do build_row routines "$row" routines 7 "$row" 0 1 2 3 4 5 6 5; done

# The generator returned six locomotion phases per row. Complete the loop with
# a forward-and-back cadence rather than duplicating a hard jump at the seam.
for row in {0..7}; do build_row walkModes "$row" walk 6 "$row" 0 1 2 3 4 5 4 3; done

# Life source contains seven rows. Row 4 keeps the six hand-free attention
# frames and returns smoothly; the separately generated 4x2 mischief sheet is
# flattened row-major into row 5. Sleep and walk-request remain rows 6 and 7.
for row in {0..3}; do build_row life "$row" life 7 "$row" 0 1 2 3 4 5 6 5; done
build_row life 4 life 7 4 0 1 2 3 4 3 2 1
build_row life 5 mischief 4 -1 0 1 2 3 4 5 6 7
build_row life 6 life 7 5 0 1 2 3 4 5 6 5
# Source frame 0 only contains the harness. Exclude it so every state includes
# a complete pupu body as required by the asset contract.
build_row life 7 life 7 6 1 2 3 4 5 6 5 4

finish_atlas core 6 "$OUT/pupu-core-youthful-v5.png"
finish_atlas life 8 "$OUT/pupu-life-youthful-v5.png"
finish_atlas directions 4 "$OUT/pupu-directions-youthful-v5.png"
finish_atlas touch 6 "$OUT/pupu-touch-youthful-v5.png"
finish_atlas routines 8 "$OUT/pupu-routines-youthful-v5.png"
finish_atlas walkModes 8 "$OUT/pupu-walk-modes-youthful-v5.png"

identify "$OUT"/pupu-*-youthful-v5.png
