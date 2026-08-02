#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
BUILD_ID="$(date +%Y%m%d%H%M%S)-$$"
WORK="$ROOT/.asset-work/v8/build-$BUILD_ID"
SOURCE="$ROOT/AssetSources/v8/pupu-activity-v8-chroma.png"
SPLOOT_SOURCE="$ROOT/AssetSources/v8/pupu-sploot-v8-chroma.png"
OUT="$ROOT/Pupu.Desktop/Assets/pupu-activity-youthful-v8.png"
KEY_HELPER="/root/.codex/skills/.system/imagegen/scripts/remove_chroma_key.py"
PYTHON_BIN="${CODEX_PRIMARY_RUNTIME_PYTHON:-python3}"
CELL=256
SOURCE_COLUMNS=7
ROWS=8
VISIBLE=212
BOTTOM=20

command -v convert >/dev/null
command -v identify >/dev/null
test -f "$SOURCE"
test -f "$KEY_HELPER"
mkdir -p "$WORK/split" "$WORK/frames" "$WORK/rows"

"$PYTHON_BIN" "$KEY_HELPER" \
  --input "$SOURCE" \
  --out "$WORK/keyed.png" \
  --auto-key border \
  --soft-matte \
  --transparent-threshold 12 \
  --opaque-threshold 220 \
  --despill \
  --edge-contract 1 \
  --edge-feather 0.25 \
  --force

"$PYTHON_BIN" "$ROOT/scripts/extract-sprite-cells.py" \
  --input "$WORK/keyed.png" \
  --output "$WORK/split" \
  --columns "$SOURCE_COLUMNS" \
  --rows "$ROWS"

for ((row = 0; row < ROWS; row++)); do
  max_w=1
  max_h=1
  for ((column = 0; column < SOURCE_COLUMNS; column++)); do
    index=$((row * SOURCE_COLUMNS + column))
    path="$WORK/split/cell-$(printf '%03d' "$index")-trim.png"
    read -r width height < <(identify -format '%w %h\n' "$path")
    (( width > max_w )) && max_w="$width"
    (( height > max_h )) && max_h="$height"
  done
  percent="$(awk -v visible="$VISIBLE" -v w="$max_w" -v h="$max_h" \
    'BEGIN { a=visible/w; b=visible/h; s=(a<b?a:b); printf "%.4f", s*100 }')"
  row_frames=()
  for ((column = 0; column < SOURCE_COLUMNS; column++)); do
    index=$((row * SOURCE_COLUMNS + column))
    input="$WORK/split/cell-$(printf '%03d' "$index")-trim.png"
    frame="$WORK/frames/frame-$(printf '%02d-%02d' "$row" "$column").png"
    convert -size "${CELL}x${CELL}" xc:none \
      \( "$input" -filter Lanczos -resize "${percent}%" \) \
      -gravity south -geometry "+0+${BOTTOM}" -compose over -composite \
      -depth 8 -strip -define png:color-type=6 "$frame"
    row_frames+=("$frame")
  done

  # The image generator produced seven clean semantic keyframes per row.
  # Preserve them all and duplicate the settled seventh frame as frame 8;
  # runtime ping-pong sequences then hold briefly before reversing smoothly.
  held="$WORK/frames/frame-$(printf '%02d-07' "$row").png"
  cp "${row_frames[6]}" "$held"
  row_frames+=("$held")
  convert "${row_frames[@]}" +append \
    -depth 8 -strip -define png:color-type=6 \
    "$WORK/rows/row-$(printf '%02d' "$row").png"
done

convert "$WORK/rows"/row-*.png -append \
  -depth 8 -strip -define png:color-type=6 \
  -define png:compression-level=6 "$WORK/final.png"

if [[ -f "$SPLOOT_SOURCE" ]]; then
  "$PYTHON_BIN" "$KEY_HELPER" \
    --input "$SPLOOT_SOURCE" \
    --out "$WORK/sploot-keyed.png" \
    --auto-key border \
    --soft-matte \
    --transparent-threshold 12 \
    --opaque-threshold 220 \
    --despill \
    --edge-contract 1 \
    --edge-feather 0.25 \
    --force
  convert "$WORK/sploot-keyed.png" -trim +repage "$WORK/sploot-trim.png"
  read -r sploot_w sploot_h < <(identify -format '%w %h\n' "$WORK/sploot-trim.png")
  sploot_percent="$(awk -v visible="$VISIBLE" -v w="$sploot_w" -v h="$sploot_h" \
    'BEGIN { a=visible/w; b=visible/h; s=(a<b?a:b); printf "%.4f", s*100 }')"
  convert "$WORK/sploot-trim.png" -filter Lanczos -resize "${sploot_percent}%" "$WORK/sploot-base.png"
  sploot_frames=()
  breath_scales=(100 99.5 99 99.5 100 100.5 100 99.5)
  for column in {0..7}; do
    frame="$WORK/frames/sploot-$(printf '%02d' "$column").png"
    convert -size "${CELL}x${CELL}" xc:none \
      \( "$WORK/sploot-base.png" -filter Lanczos -resize "100x${breath_scales[$column]}%" \) \
      -gravity south -geometry "+0+${BOTTOM}" -compose over -composite \
      -depth 8 -strip -define png:color-type=6 "$frame"
    sploot_frames+=("$frame")
  done
  convert "${sploot_frames[@]}" +append \
    -depth 8 -strip -define png:color-type=6 "$WORK/sploot-row.png"
  convert "$WORK/final.png" -crop "2048x1536+0+0" +repage "$WORK/top-six.png"
  convert "$WORK/final.png" -crop "2048x256+0+1792" +repage "$WORK/window-row.png"
  convert "$WORK/top-six.png" "$WORK/sploot-row.png" "$WORK/window-row.png" -append \
    -depth 8 -strip -define png:color-type=6 \
    -define png:compression-level=6 "$WORK/final-with-sploot.png"
  mv "$WORK/final-with-sploot.png" "$WORK/final.png"
fi

test "$(identify -format '%wx%h' "$WORK/final.png")" = "2048x2048"
convert "$WORK/final.png" -alpha extract -format '%[fx:mean]\n' info: >/dev/null
cp "$WORK/final.png" "$OUT.staged.png"
identify "$OUT.staged.png" >/dev/null
mv "$OUT.staged.png" "$OUT"
sync -f "$OUT"
identify "$OUT"
echo "v8 activity asset work retained at: $WORK"
