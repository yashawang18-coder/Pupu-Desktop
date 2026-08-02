#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
BUILD_ID="$(date +%Y%m%d%H%M%S)-$$"
WORK="$ROOT/.asset-work/v9/build-$BUILD_ID"
SOURCE="$ROOT/AssetSources/v9"
OUT="$ROOT/Pupu.Desktop/Assets"
KEY_HELPER="/root/.codex/skills/.system/imagegen/scripts/remove_chroma_key.py"
PYTHON_BIN="${CODEX_PRIMARY_RUNTIME_PYTHON:-python3}"
CELL=256
VISIBLE=212
TOUCH_VISIBLE=216
WALK_VISIBLE=208
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

key_only() {
  local name="$1" input="$2"
  local keyed="$WORK/$name/keyed.png"
  mkdir -p "$WORK/$name/split"
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
}

split_legacy_grid() {
  local name="$1" input="$2" columns="$3" rows="$4"
  local split="$WORK/$name/split"
  mkdir -p "$split"
  local row column index output
  for ((row=0; row<rows; row++)); do
    for ((column=0; column<columns; column++)); do
      index=$((row * columns + column))
      output="$split/cell-$(printf '%03d' "$index")-trim.png"
      convert "$input" \
        -crop "${CELL}x${CELL}+$((column * CELL))+$((row * CELL))" \
        +repage -trim +repage \
        -depth 8 -strip -define png:color-type=6 "$output"
    done
  done
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

mirror_row() {
  local atlas="$1" source_row="$2" output_row="$3"
  local input="$WORK/output/$atlas/row-$(printf '%02d' "$source_row").png"
  local row_dir="$WORK/output/$atlas/mirror-$output_row"
  mkdir -p "$row_dir"
  local frames=() column output
  for column in {0..7}; do
    output="$row_dir/frame-$(printf '%02d' "$column").png"
    convert "$input" \
      -crop "${CELL}x${CELL}+$((column * CELL))+0" +repage \
      -flop -depth 8 -strip -define png:color-type=6 "$output"
    frames+=("$output")
  done
  convert "${frames[@]}" +append \
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

key_only touch "$SOURCE/pupu-touch-v9-chroma.png"
"$PYTHON_BIN" "$ROOT/scripts/extract-variable-sprite-rows.py" \
  --input "$WORK/touch/keyed.png" \
  --output "$WORK/touch/split" \
  --row-columns "7,7,7,8,8,8"
key_and_split core-actions "$SOURCE/pupu-core-actions-v9-chroma.png" 7 3
key_and_split walk-left "$SOURCE/pupu-walk-left-v9-chroma.png" 5 2
key_and_split walk-rear "$SOURCE/pupu-walk-rear-v9-chroma.png" 5 2
key_and_split walk-front "$SOURCE/pupu-walk-front-v9-chroma.png" 5 2
split_legacy_grid core-legacy "$SOURCE/pupu-core-youthful-v6-legacy.png" 8 6

mkdir -p "$WORK/output"/{core,touch,walkModes,directions}

# Core keeps the unrelated roll, tail-chase and stretch rows. The requested
# prone breathing, wand play and sleepy-only yawn rows are replaced by V9.
build_row core 0 core-actions 7 0 "$VISIBLE" 0 1 2 3 4 5 6 5
build_row core 1 core-legacy 8 1 "$VISIBLE" 0 1 2 3 4 5 6 7
build_row core 2 core-legacy 8 2 "$VISIBLE" 0 1 2 3 4 5 6 7
build_row core 3 core-actions 7 1 "$VISIBLE" 0 1 2 3 4 5 6 5
build_row core 4 core-actions 7 2 "$VISIBLE" 0 1 2 3 4 5 6 6
build_row core 5 core-legacy 8 5 "$VISIBLE" 0 1 2 3 4 5 6 7

for row in {0..5}; do
  # Touch poses vary from upright to long side-lying. Fit each complete pose
  # independently into the same 216px stage so a single wide frame does not
  # shrink every other frame in the row.
  if (( row < 3 )); then
    build_row_fit_each touch "$row" touch 8 "$row" "$TOUCH_VISIBLE" 0 1 2 3 4 5 6 6
  else
    build_row_fit_each touch "$row" touch 8 "$row" "$TOUCH_VISIBLE" 0 1 2 3 4 5 6 7
  fi
done

# Every gait row is an exact closed half-stride:
# contact -> swing -> pass -> opposite swing -> opposite contact -> return.
gait=(0 1 2 3 4 3 2 1)
build_row walkModes 0 walk-left 5 0 "$WALK_VISIBLE" "${gait[@]}"
mirror_row walkModes 0 1
build_row walkModes 2 walk-rear 5 0 "$WALK_VISIBLE" "${gait[@]}"
build_row walkModes 3 walk-front 5 0 "$WALK_VISIBLE" "${gait[@]}"
build_row walkModes 4 walk-left 5 1 "$WALK_VISIBLE" "${gait[@]}"
mirror_row walkModes 4 5
build_row walkModes 6 walk-rear 5 1 "$WALK_VISIBLE" "${gait[@]}"
build_row walkModes 7 walk-front 5 1 "$WALK_VISIBLE" "${gait[@]}"

for row in {0..3}; do
  cp "$WORK/output/walkModes/row-$(printf '%02d' "$((row + 4))").png" \
     "$WORK/output/directions/row-$(printf '%02d' "$row").png"
done

finish_atlas core 6 "$OUT/pupu-core-youthful-v9.png"
finish_atlas touch 6 "$OUT/pupu-touch-youthful-v9.png"
finish_atlas walkModes 8 "$OUT/pupu-walk-modes-youthful-v9.png"
finish_atlas directions 4 "$OUT/pupu-directions-youthful-v9.png"

identify \
  "$OUT/pupu-core-youthful-v9.png" \
  "$OUT/pupu-touch-youthful-v9.png" \
  "$OUT/pupu-walk-modes-youthful-v9.png" \
  "$OUT/pupu-directions-youthful-v9.png"
echo "V9 asset work retained at: $WORK"
