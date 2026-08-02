#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
BUILD_ID="$(date +%Y%m%d%H%M%S)-$$"
WORK="/tmp/pupu-v12-$BUILD_ID"
SOURCE="$ROOT/AssetSources/v12"
LEGACY="$ROOT/AssetSources/legacy-formal-v12"
OUT="$ROOT/Pupu.Desktop/Assets"
KEY_HELPER="/root/.codex/skills/.system/imagegen/scripts/remove_chroma_key.py"
PYTHON_BIN="${CODEX_PRIMARY_RUNTIME_PYTHON:-python3}"
CELL=256
VISIBLE=212
TOUCH_VISIBLE=216
MOVEMENT_VISIBLE=202
BOTTOM=20

command -v convert >/dev/null
command -v identify >/dev/null
test -f "$KEY_HELPER"
mkdir -p "$WORK" "$OUT"

key_and_split() {
  local name="$1" input="$2" columns="$3" rows="$4"
  local keyed="$WORK/$name/keyed.png"
  local split="$WORK/$name/split"
  mkdir -p "$split"
  "$PYTHON_BIN" "$KEY_HELPER" \
    --input "$input" \
    --out "$keyed" \
    --auto-key border \
    --soft-matte \
    --transparent-threshold 12 \
    --opaque-threshold 220 \
    --despill \
    --edge-contract 1 \
    --edge-feather 0.25 \
    --force
  "$PYTHON_BIN" "$ROOT/scripts/extract-sprite-cells.py" \
    --input "$keyed" \
    --output "$split" \
    --columns "$columns" \
    --rows "$rows"
}

cell_path() {
  local source_name="$1" source_columns="$2" source_row="$3" source_frame="$4"
  local index=$((source_row * source_columns + source_frame))
  printf '%s/%s/split/cell-%03d-trim.png' "$WORK" "$source_name" "$index"
}

row_percent() {
  local source_name="$1" source_columns="$2" source_row="$3" visible="$4"
  shift 4
  local max_w=1 max_h=1 frame input width height
  for frame in "$@"; do
    input="$(cell_path "$source_name" "$source_columns" "$source_row" "$frame")"
    read -r width height < <(identify -format '%w %h\n' "$input")
    (( width > max_w )) && max_w="$width"
    (( height > max_h )) && max_h="$height"
  done
  awk -v visible="$visible" -v w="$max_w" -v h="$max_h" \
    'BEGIN { a=visible/w; b=visible/h; s=(a<b?a:b); printf "%.4f", s*100 }'
}

build_row_with_percent() {
  local atlas="$1" output_row="$2" source_name="$3" source_columns="$4" source_row="$5" percent="$6"
  shift 6
  local frames=("$@")
  test "${#frames[@]}" -eq 8
  local row_dir="$WORK/output/$atlas/row-$output_row"
  mkdir -p "$row_dir"
  local outputs=() index=0 frame input normalized
  for frame in "${frames[@]}"; do
    input="$(cell_path "$source_name" "$source_columns" "$source_row" "$frame")"
    normalized="$row_dir/frame-$(printf '%02d' "$index").png"
    convert -size "${CELL}x${CELL}" xc:none \
      \( "$input" -filter Lanczos -resize "${percent}%" \) \
      -gravity south -geometry "+0+${BOTTOM}" -compose over -composite \
      -depth 8 -strip -define png:color-type=6 "$normalized"
    outputs+=("$normalized")
    index=$((index + 1))
  done
  convert "${outputs[@]}" +append \
    -depth 8 -strip -define png:color-type=6 \
    "$WORK/output/$atlas/row-$(printf '%02d' "$output_row").png"
}

build_row() {
  local atlas="$1" output_row="$2" source_name="$3" source_columns="$4" source_row="$5" visible="$6"
  shift 6
  local frames=("$@")
  local percent
  percent="$(row_percent "$source_name" "$source_columns" "$source_row" "$visible" "${frames[@]}")"
  build_row_with_percent \
    "$atlas" "$output_row" "$source_name" "$source_columns" "$source_row" "$percent" \
    "${frames[@]}"
}

build_row_fit_each() {
  local atlas="$1" output_row="$2" source_name="$3" source_columns="$4" source_row="$5" visible="$6"
  shift 6
  local frames=("$@")
  test "${#frames[@]}" -eq 8
  local row_dir="$WORK/output/$atlas/row-$output_row"
  mkdir -p "$row_dir"
  local outputs=() index=0 frame input normalized
  for frame in "${frames[@]}"; do
    input="$(cell_path "$source_name" "$source_columns" "$source_row" "$frame")"
    normalized="$row_dir/frame-$(printf '%02d' "$index").png"
    convert -size "${CELL}x${CELL}" xc:none \
      \( "$input" -filter Lanczos -resize "${visible}x${visible}" \) \
      -gravity south -geometry "+0+${BOTTOM}" -compose over -composite \
      -depth 8 -strip -define png:color-type=6 "$normalized"
    outputs+=("$normalized")
    index=$((index + 1))
  done
  convert "${outputs[@]}" +append \
    -depth 8 -strip -define png:color-type=6 \
    "$WORK/output/$atlas/row-$(printf '%02d' "$output_row").png"
}

rescale_built_cell() {
  local atlas="$1" output_row="$2" output_frame="$3" percent="$4"
  local row_dir="$WORK/output/$atlas/row-$output_row"
  local cell="$row_dir/frame-$(printf '%02d' "$output_frame").png"
  local staged="$cell.staged.png"
  convert -size "${CELL}x${CELL}" xc:none \
    \( "$cell" -trim +repage -filter Lanczos -resize "${percent}%" \) \
    -gravity south -geometry "+0+${BOTTOM}" -compose over -composite \
    -depth 8 -strip -define png:color-type=6 "$staged"
  mv "$staged" "$cell"
  convert "$row_dir"/frame-*.png +append \
    -depth 8 -strip -define png:color-type=6 \
    "$WORK/output/$atlas/row-$(printf '%02d' "$output_row").png"
}

copy_legacy_row() {
  local atlas="$1" output_row="$2" input="$3" source_row="$4"
  mkdir -p "$WORK/output/$atlas"
  convert "$input" \
    -crop "$((CELL * 8))x${CELL}+0+$((source_row * CELL))" +repage \
    -depth 8 -strip -define png:color-type=6 \
    "$WORK/output/$atlas/row-$(printf '%02d' "$output_row").png"
}

finish_atlas() {
  local atlas="$1" rows="$2" output="$3"
  local inputs=() row
  for ((row=0; row<rows; row++)); do
    inputs+=("$WORK/output/$atlas/row-$(printf '%02d' "$row").png")
  done
  local staged="$output.staged.png"
  convert "${inputs[@]}" -append \
    -depth 8 -strip -define png:color-type=6 \
    -define png:compression-level=6 "$staged"
  test "$(identify -format '%wx%h' "$staged")" = "$((CELL * 8))x$((CELL * rows))"
  identify "$staged" >/dev/null
  mv "$staged" "$output"
  sync -f "$output"
}

key_and_split daily "$SOURCE/pupu-daily-actions-v12-chroma.png" 8 4
key_and_split care "$SOURCE/pupu-care-actions-v12-chroma.png" 8 4
key_and_split social "$SOURCE/pupu-social-actions-v12-chroma.png" 8 4
key_and_split free_motion "$SOURCE/pupu-free-motion-v12-chroma.png" 8 4
key_and_split gaze_coin "$SOURCE/pupu-gaze-coin-v12-chroma.png" 8 3
key_and_split litter "$SOURCE/pupu-litter-v12-chroma.png" 8 4

mkdir -p "$WORK/output"/{core,life,directions,touch,routines,walk_modes,life_equipment,motion,gaze_coin,litter}

legacy_core="$LEGACY/pupu-core-youthful-v9.png"
legacy_life="$LEGACY/pupu-life-youthful-v6.png"
legacy_directions="$LEGACY/pupu-directions-youthful-v9.png"
legacy_touch="$LEGACY/pupu-touch-youthful-v9.png"
legacy_routines="$LEGACY/pupu-routines-youthful-v6.png"
legacy_walk="$LEGACY/pupu-walk-modes-youthful-v9.png"
legacy_equipment="$LEGACY/pupu-life-equipment-youthful-v11.png"
legacy_motion="$LEGACY/pupu-motion-youthful-v11.png"

# Core: retire the V6-derived roll and tail-chase rows.
copy_legacy_row core 0 "$legacy_core" 0
build_row core 1 daily 8 2 "$VISIBLE" 0 1 2 3 4 5 6 7
build_row core 2 daily 8 3 "$VISIBLE" 0 1 2 3 4 5 6 7
for row in 3 4 5; do copy_legacy_row core "$row" "$legacy_core" "$row"; done

# Routines: replace prone, paw grooming and both hungry-cat food sequences.
copy_legacy_row routines 0 "$legacy_routines" 0
build_row routines 1 daily 8 0 "$VISIBLE" 0 1 2 3 4 5 6 7
build_row routines 2 daily 8 1 "$VISIBLE" 0 1 2 3 4 5 6 7
copy_legacy_row routines 3 "$legacy_routines" 3
build_row routines 4 care 8 1 "$VISIBLE" 0 1 2 3 4 5 6 7
build_row routines 5 care 8 2 "$VISIBLE" 0 1 2 3 4 5 6 7
copy_legacy_row routines 6 "$legacy_routines" 6
copy_legacy_row routines 7 "$legacy_routines" 7

# Life: replace litter inspection, owner grooming and mischief.
copy_legacy_row life 0 "$legacy_life" 0
build_row life 1 litter 8 0 "$VISIBLE" 0 1 2 3 4 5 6 7
copy_legacy_row life 2 "$legacy_life" 2
build_row life 3 care 8 3 "$VISIBLE" 0 1 2 3 4 5 6 7
copy_legacy_row life 4 "$legacy_life" 4
build_row life 5 free_motion 8 3 "$VISIBLE" 0 1 2 3 4 5 6 7
copy_legacy_row life 6 "$legacy_life" 6
copy_legacy_row life 7 "$legacy_life" 7

# Touch: the new trust approach is normalized per cell; runtime supplies the
# small 1.00 -> 1.06 visual zoom so the sprite itself never jumps in size.
for row in 0 1 2; do copy_legacy_row touch "$row" "$legacy_touch" "$row"; done
build_row touch 3 social 8 3 "$TOUCH_VISIBLE" 0 1 2 3 4 5 6 7
copy_legacy_row touch 4 "$legacy_touch" 4
build_row_fit_each touch 5 social 8 2 "$TOUCH_VISIBLE" 0 1 2 3 4 5 6 7

# Movement rows 0-2 share one scale across front-left, front-right and rear.
movement_percent="$(
  max_w=1; max_h=1
  for row in 0 1 2; do
    for frame in {0..7}; do
      input="$(cell_path free_motion 8 "$row" "$frame")"
      read -r width height < <(identify -format '%w %h\n' "$input")
      (( width > max_w )) && max_w="$width"
      (( height > max_h )) && max_h="$height"
    done
  done
  awk -v visible="$MOVEMENT_VISIBLE" -v w="$max_w" -v h="$max_h" \
    'BEGIN { a=visible/w; b=visible/h; s=(a<b?a:b); printf "%.4f", s*100 }'
)"

for row in {0..5}; do copy_legacy_row walk_modes "$row" "$legacy_walk" "$row"; done
build_row_with_percent walk_modes 6 free_motion 8 2 "$movement_percent" 0 1 2 3 4 5 6 7
copy_legacy_row walk_modes 7 "$legacy_walk" 7

copy_legacy_row directions 0 "$legacy_directions" 0
copy_legacy_row directions 1 "$legacy_directions" 1
cp "$WORK/output/walk_modes/row-06.png" "$WORK/output/directions/row-02.png"
copy_legacy_row directions 3 "$legacy_directions" 3

for row in 0 1 2 3; do copy_legacy_row motion "$row" "$legacy_motion" "$row"; done
build_row_with_percent motion 4 free_motion 8 0 "$movement_percent" 0 1 2 3 4 5 6 7
# The first front-left pose presents a slightly wider silhouette than the
# stepping poses. A 2% optical correction keeps the loop under the 1.25x
# apparent-size gate without changing the shared direction scale.
rescale_built_cell motion 4 0 98
build_row_with_percent motion 5 free_motion 8 1 "$movement_percent" 0 1 2 3 4 5 6 7
for row in 6 7 8 9 10; do copy_legacy_row motion "$row" "$legacy_motion" "$row"; done

# The legacy first row bundled an obsolete all-in-one toilet sequence.  V12
# carries litter work in its own four-row atlas, so the formal equipment atlas
# keeps only grooming, the blue bed and the peacock-blue leash.
build_row life_equipment 0 care 8 0 "$VISIBLE" 0 1 2 3 4 5 6 7
copy_legacy_row life_equipment 1 "$legacy_equipment" 2
copy_legacy_row life_equipment 2 "$legacy_equipment" 3

build_row gaze_coin 0 gaze_coin 8 0 "$VISIBLE" 0 1 2 3 4 5 6 7
build_row gaze_coin 1 gaze_coin 8 1 "$VISIBLE" 0 1 2 3 4 5 6 7
build_row gaze_coin 2 gaze_coin 8 2 196 0 1 2 3 4 5 6 7

for row in {0..3}; do
  build_row litter "$row" litter 8 "$row" "$VISIBLE" 0 1 2 3 4 5 6 7
done

finish_atlas core 6 "$OUT/pupu-core-youthful-v12.png"
finish_atlas life 8 "$OUT/pupu-life-youthful-v12.png"
finish_atlas directions 4 "$OUT/pupu-directions-youthful-v12.png"
finish_atlas touch 6 "$OUT/pupu-touch-youthful-v12.png"
finish_atlas routines 8 "$OUT/pupu-routines-youthful-v12.png"
finish_atlas walk_modes 8 "$OUT/pupu-walk-modes-youthful-v12.png"
finish_atlas life_equipment 3 "$OUT/pupu-life-equipment-youthful-v12.png"
finish_atlas motion 11 "$OUT/pupu-motion-youthful-v12.png"
finish_atlas gaze_coin 3 "$OUT/pupu-gaze-coin-youthful-v12.png"
finish_atlas litter 4 "$OUT/pupu-litter-youthful-v12.png"

identify \
  "$OUT/pupu-core-youthful-v12.png" \
  "$OUT/pupu-life-youthful-v12.png" \
  "$OUT/pupu-directions-youthful-v12.png" \
  "$OUT/pupu-touch-youthful-v12.png" \
  "$OUT/pupu-routines-youthful-v12.png" \
  "$OUT/pupu-walk-modes-youthful-v12.png" \
  "$OUT/pupu-life-equipment-youthful-v12.png" \
  "$OUT/pupu-motion-youthful-v12.png" \
  "$OUT/pupu-gaze-coin-youthful-v12.png" \
  "$OUT/pupu-litter-youthful-v12.png"
echo "V12 asset work retained at: $WORK"
