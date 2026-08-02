#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
BUILD_ID="$(date +%Y%m%d%H%M%S)-$$"
WORK="$ROOT/.asset-work/v10/build-$BUILD_ID"
SOURCE="$ROOT/AssetSources/v10"
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
  convert "$staged" -alpha extract -format '%[fx:mean]\n' info: >/dev/null
  mv "$staged" "$output"
  sync -f "$output"
}

key_and_split gaze "$SOURCE/pupu-gaze-prone-v10-chroma.png" 8 1
key_and_split magic "$SOURCE/pupu-magic-v10-chroma.png" 8 4
key_and_split seasonal "$SOURCE/pupu-seasonal-v10-chroma.png" 8 4

mkdir -p "$WORK/output"/{specials,seasonal}

# Every row shares one scale and one baseline. This is deliberately stricter
# than fitting each frame independently, because eye direction, props and
# outfit animation must not make Pupu's body pulse in size.
build_row specials 0 gaze 8 0 "$VISIBLE" 0 1 2 3 4 5 6 7
build_row specials 1 magic 8 0 "$VISIBLE" 0 1 2 3 4 5 6 7
build_row specials 2 magic 8 1 "$VISIBLE" 0 1 2 3 4 5 6 7
build_row specials 3 magic 8 2 "$VISIBLE" 0 1 2 3 4 5 6 7
build_row specials 4 magic 8 3 "$VISIBLE" 0 1 2 3 4 5 6 7

for row in {0..3}; do
  build_row seasonal "$row" seasonal 8 "$row" "$VISIBLE" 0 1 2 3 4 5 6 7
done

finish_atlas specials 5 "$OUT/pupu-specials-youthful-v10.png"
finish_atlas seasonal 4 "$OUT/pupu-seasonal-youthful-v10.png"

identify \
  "$OUT/pupu-specials-youthful-v10.png" \
  "$OUT/pupu-seasonal-youthful-v10.png"
echo "V10 special asset work retained at: $WORK"
