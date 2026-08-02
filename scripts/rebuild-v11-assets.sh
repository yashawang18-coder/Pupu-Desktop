#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
BUILD_ID="$(date +%Y%m%d%H%M%S)-$$"
WORK="/tmp/pupu-v11-$BUILD_ID"
SOURCE="$ROOT/AssetSources/v11"
OUT="$ROOT/Pupu.Desktop/Assets"
KEY_HELPER="/root/.codex/skills/.system/imagegen/scripts/remove_chroma_key.py"
PYTHON_BIN="${CODEX_PRIMARY_RUNTIME_PYTHON:-python3}"
CELL=256
VISIBLE=212
BOTTOM=20

command -v convert >/dev/null
command -v identify >/dev/null
test -f "$KEY_HELPER"
mkdir -p "$WORK" "$OUT"

verify_png() {
  local path="$1" attempt
  for attempt in {1..20}; do
    if identify "$path" >/dev/null 2>&1; then
      return 0
    fi
    sleep 0.1
  done
  identify "$path"
}

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

build_row() {
  local atlas="$1" output_row="$2" source_name="$3" source_columns="$4" source_row="$5" visible="$6"
  shift 6
  local frames=("$@")
  test "${#frames[@]}" -eq 8
  local row_dir="$WORK/output/$atlas/row-$output_row"
  mkdir -p "$row_dir"
  local max_w=1 max_h=1 frame input width height
  for frame in "${frames[@]}"; do
    input="$(cell_path "$source_name" "$source_columns" "$source_row" "$frame")"
    read -r width height < <(identify -format '%w %h\n' "$input")
    (( width > max_w )) && max_w="$width"
    (( height > max_h )) && max_h="$height"
  done
  local percent
  percent="$(awk -v visible="$visible" -v w="$max_w" -v h="$max_h" \
    'BEGIN { a=visible/w; b=visible/h; s=(a<b?a:b); printf "%.4f", s*100 }')"
  local outputs=() index=0 normalized
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
  sync -f "$WORK/output/$atlas/row-$(printf '%02d' "$output_row").png"
  verify_png "$WORK/output/$atlas/row-$(printf '%02d' "$output_row").png"
}

finish_atlas() {
  local atlas="$1" rows="$2" output="$3"
  local inputs=() row
  for ((row=0; row<rows; row++)); do
    inputs+=("$WORK/output/$atlas/row-$(printf '%02d' "$row").png")
  done
  local staged="$WORK/final-$atlas.png"
  for input in "${inputs[@]}"; do
    verify_png "$input"
  done
  convert "${inputs[@]}" -append \
    -depth 8 -strip -define png:color-type=6 \
    -define png:compression-level=6 "$staged"
  test "$(identify -format '%wx%h' "$staged")" = "$((CELL * 8))x$((CELL * rows))"
  verify_png "$staged"
  convert "$staged" -alpha extract -format '%[fx:mean]\n' info: >/dev/null
  cp "$staged" "$output.staged.png"
  sync -f "$output.staged.png"
  verify_png "$output.staged.png"
  mv "$output.staged.png" "$output"
  sync -f "$output"
}

key_and_split life "$SOURCE/pupu-life-equipment-v11-chroma.png" 8 4
key_and_split diagonal_harness "$SOURCE/pupu-diagonal-harness-v11-chroma.png" 8 4
key_and_split diagonal_free "$SOURCE/pupu-diagonal-free-v11-chroma.png" 8 4
key_and_split motion_extra "$SOURCE/pupu-motion-supplement-v11-chroma.png" 8 3
key_and_split magic "$SOURCE/pupu-magic-v11-chroma.png" 8 4

# The generated petrification row contains one extra near-identical stone
# phase. Select five progressive cat phases plus the three distinct polished
# coin highlights so no nominal cell ever contains two subjects.
mkdir -p "$WORK/magic_petrify/split"
magic_alpha="$WORK/magic/keyed.png"
petrify_crops=(
  "175x200+45+485"
  "174x200+214+485"
  "170x200+390+485"
  "170x200+557+485"
  "168x200+728+485"
  "155x200+1060+485"
  "150x200+1235+485"
  "160x200+1420+485"
)
for index in "${!petrify_crops[@]}"; do
  convert "$magic_alpha" \
    -crop "${petrify_crops[$index]}" +repage -trim +repage \
    -depth 8 -strip -define png:color-type=6 \
    "$WORK/magic_petrify/split/cell-$(printf '%03d' "$index")-trim.png"
done

mkdir -p "$WORK/output"/{life_equipment,motion,specials}

for row in {0..3}; do
  build_row life_equipment "$row" life 8 "$row" "$VISIBLE" 0 1 2 3 4 5 6 7
  if (( row == 2 )); then
    # The rear-left source has two unusually tight tail poses. Keep the leg
    # contact phases but reuse adjacent full-body phases so the apparent
    # footprint stays within the 1.25x movement gate.
    build_row motion "$row" diagonal_harness 8 "$row" "$VISIBLE" 0 1 2 5 4 5 6 1
    build_row motion "$((row + 4))" diagonal_free 8 "$row" "$VISIBLE" 0 1 2 5 4 5 6 1
  else
    build_row motion "$row" diagonal_harness 8 "$row" "$VISIBLE" 0 1 2 3 4 5 6 7
    build_row motion "$((row + 4))" diagonal_free 8 "$row" "$VISIBLE" 0 1 2 3 4 5 6 7
  fi
done

for row in {0..2}; do
  build_row motion "$((row + 8))" motion_extra 8 "$row" "$VISIBLE" 0 1 2 3 4 5 6 7
done

# Preserve the already approved V10 low-prone gaze row exactly; only magic is
# upgraded in V11.
convert "$ROOT/AssetSources/legacy-formal-v12/pupu-specials-youthful-v10.png" \
  -crop 2048x256+0+0 +repage \
  -depth 8 -strip -define png:color-type=6 \
  "$WORK/output/specials/row-00.png"
build_row specials 1 magic 8 0 "$VISIBLE" 0 1 2 3 4 5 6 7
build_row specials 2 magic 8 1 "$VISIBLE" 0 1 2 3 4 5 6 7
build_row specials 3 magic_petrify 8 0 "$VISIBLE" 0 1 2 3 4 5 6 7
build_row specials 4 magic 8 3 "$VISIBLE" 0 1 2 3 4 5 6 7

# The Apparate flash is intentionally soft, but its dressed-cat silhouette
# still needs a crisp readable edge at desktop size. Sharpen only that flash
# cell, then rebuild the row from its normalized 256px frames.
apparition="$WORK/output/specials/row-2/frame-05.png"
convert "$apparition" -unsharp 0x1+1+0.02 \
  "$WORK/output/specials/row-2/frame-05-sharp.png"
mv "$WORK/output/specials/row-2/frame-05-sharp.png" "$apparition"
specials_row_two=()
for frame in {0..7}; do
  specials_row_two+=("$WORK/output/specials/row-2/frame-$(printf '%02d' "$frame").png")
done
convert "${specials_row_two[@]}" +append \
  -depth 8 -strip -define png:color-type=6 \
  "$WORK/output/specials/row-02.png"
verify_png "$WORK/output/specials/row-02.png"

finish_atlas life_equipment 4 "$OUT/pupu-life-equipment-youthful-v11.png"
finish_atlas motion 11 "$OUT/pupu-motion-youthful-v11.png"
finish_atlas specials 5 "$OUT/pupu-specials-youthful-v11.png"

identify \
  "$OUT/pupu-life-equipment-youthful-v11.png" \
  "$OUT/pupu-motion-youthful-v11.png" \
  "$OUT/pupu-specials-youthful-v11.png"
echo "V11 asset work retained at: $WORK"
