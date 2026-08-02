#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
BUILD_ID="$(date +%Y%m%d%H%M%S)-$$"
WORK="$ROOT/.asset-work/v6/build-$BUILD_ID"
SOURCE="$ROOT/AssetSources/v6"
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

prepare_source() {
  local name="$1" input="$2" cols="$3" rows="$4"
  local keyed="$WORK/$name/keyed.png"
  local split="$WORK/$name/split"
  mkdir -p "$split"

  "$PYTHON_BIN" "$KEY_HELPER" \
    --input "$input" \
    --out "$keyed" \
    --key-color '#00ff00' \
    --soft-matte \
    --transparent-threshold 24 \
    --opaque-threshold 188 \
    --despill \
    --edge-contract 1 \
    --edge-feather 0.3 \
    --force

  "$PYTHON_BIN" "$ROOT/scripts/extract-sprite-cells.py" \
    --input "$keyed" \
    --output "$split" \
    --columns "$cols" \
    --rows "$rows"
}

cell_path() {
  local source_name="$1" source_cols="$2" source_row="$3" source_frame="$4"
  local index=$((source_row * source_cols + source_frame))
  printf '%s/%s/split/cell-%03d-trim.png' "$WORK" "$source_name" "$index"
}

build_row() {
  local output_atlas="$1" output_row="$2" source_name="$3" source_cols="$4" source_row="$5"
  shift 5
  local frames=("$@")
  test "${#frames[@]}" -eq 8

  local row_dir="$WORK/output/$output_atlas/row-$output_row"
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
    convert -size "${CELL}x${CELL}" xc:none \
      \( "$path" -filter Lanczos -resize "${percent}%" \) \
      -gravity south -geometry "+0+${BOTTOM}" -compose over -composite \
      -depth 8 -strip -define png:color-type=6 "$normalized"
    outputs+=("$normalized")
    i=$((i + 1))
  done

  local row_output="$WORK/output/$output_atlas/row-$(printf '%02d' "$output_row").png"
  local row_temporary="$row_output.tmp.png"
  convert "${outputs[@]}" +append -depth 8 -strip \
    -define png:color-type=6 -define png:compression-level=6 "$row_temporary"
  test "$(identify -format '%wx%h' "$row_temporary")" = "$((CELL * 8))x$CELL"
  convert "$row_temporary" -alpha extract -format '%[fx:mean]\n' info: >/dev/null
  sync -f "$row_temporary"
  identify "$row_temporary" >/dev/null
  mv "$row_temporary" "$row_output"
  sync -f "$row_output"
  identify "$row_output" >/dev/null
}

finish_atlas() {
  local name="$1" rows="$2" output="$3"
  local inputs=() row
  for ((row=0; row<rows; row++)); do
    inputs+=("$WORK/output/$name/row-$(printf '%02d' "$row").png")
  done
  local built="$WORK/output/$name/final.png"
  local staged="$output.v6-staged.png"
  convert "${inputs[@]}" -append -depth 8 -strip \
    -define png:color-type=6 -define png:compression-level=6 "$built"
  test "$(identify -format '%wx%h' "$built")" = "$((CELL * 8))x$((CELL * rows))"
  convert "$built" -alpha extract -format '%[fx:mean]\n' info: >/dev/null
  sync -f "$built"
  identify "$built" >/dev/null
  cp "$built" "$staged"
  sync -f "$staged"
  test "$(identify -format '%wx%h' "$staged")" = "$((CELL * 8))x$((CELL * rows))"
  convert "$staged" -alpha extract -format '%[fx:mean]\n' info: >/dev/null
  mv "$staged" "$output"
  sync -f "$output"
  identify "$output" >/dev/null
}

prepare_source core "$SOURCE/pupu-core-v6-chroma.png" 8 6
prepare_source touch "$SOURCE/pupu-touch-v6-chroma.png" 8 6
prepare_source life "$SOURCE/pupu-life-v6-chroma.png" 7 8
prepare_source directions "$SOURCE/pupu-directions-v6-chroma.png" 7 4
prepare_source routines "$SOURCE/pupu-routines-v6-chroma.png" 8 8
prepare_source walk "$SOURCE/pupu-walk-modes-v6-chroma.png" 8 9
prepare_source rear "$SOURCE/pupu-rear-v6-chroma.png" 8 2

mkdir -p "$WORK/output"/{core,touch,life,directions,routines,walkModes}

for row in {0..5}; do
  if (( row == 2 )); then
    # The generated final chase frame included a wand tip. Return to frame 0
    # to close the tail-chase loop without leaking a prop across action rows.
    build_row core "$row" core 8 "$row" 0 1 2 3 4 5 6 0
  else
    build_row core "$row" core 8 "$row" 0 1 2 3 4 5 6 7
  fi
done
for row in {0..5}; do build_row touch "$row" touch 8 "$row" 0 1 2 3 4 5 6 7; done
for row in {0..7}; do build_row life "$row" life 7 "$row" 0 1 2 3 4 5 6 5; done
for row in {0..3}; do build_row directions "$row" directions 7 "$row" 0 1 2 3 4 5 6 5; done
for row in {0..5}; do build_row routines "$row" routines 8 "$row" 0 1 2 3 4 5 6 7; done
build_row routines 6 rear 8 0 0 1 2 3 4 5 6 7
build_row routines 7 rear 8 1 0 1 2 3 4 5 6 7

# The generator produced two harnessed rear-view rows. Keep the clearer
# body-visible row and omit the redundant high-tail variant.
walk_rows=(0 1 3 4 5 6 7 8)
for output_row in {0..7}; do
  build_row walkModes "$output_row" walk 8 "${walk_rows[$output_row]}" 0 1 2 3 4 5 6 7
done

finish_atlas core 6 "$OUT/pupu-core-youthful-v6.png"
finish_atlas life 8 "$OUT/pupu-life-youthful-v6.png"
finish_atlas directions 4 "$OUT/pupu-directions-youthful-v6.png"
finish_atlas touch 6 "$OUT/pupu-touch-youthful-v6.png"
finish_atlas routines 8 "$OUT/pupu-routines-youthful-v6.png"
finish_atlas walkModes 8 "$OUT/pupu-walk-modes-youthful-v6.png"

identify "$OUT"/pupu-*-youthful-v6.png
echo "v6 asset work retained at: $WORK"
