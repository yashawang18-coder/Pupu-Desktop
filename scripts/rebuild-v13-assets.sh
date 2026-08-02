#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SOURCE="$ROOT/AssetSources/v13"
ASSETS="$ROOT/Pupu.Desktop/Assets"
ACTIONS="$ASSETS/Actions"
PYTHON_BIN="${CODEX_PRIMARY_RUNTIME_PYTHON:-python3}"
KEY_HELPER="/root/.codex/skills/.system/imagegen/scripts/remove_chroma_key.py"
WORK="$(mktemp -d /tmp/pupu-v13-assets.XXXXXX)"
CELL=256
VISIBLE=212
BOTTOM=20

command -v convert >/dev/null
command -v identify >/dev/null
test -f "$KEY_HELPER"
mkdir -p "$ACTIONS"

cleanup() {
  rm -rf "$WORK"
}
trap cleanup EXIT

key_sheet() {
  local name="$1" input="$2" key="$3"
  mkdir -p "$WORK/$name"
  "$PYTHON_BIN" "$KEY_HELPER" \
    --input "$input" \
    --out "$WORK/$name/keyed.png" \
    --key-color "$key" \
    --soft-matte \
    --transparent-threshold 12 \
    --opaque-threshold 220 \
    --despill \
    --edge-contract 1 \
    --edge-feather 0.25 \
    --force
}

split_grid() {
  local name="$1" columns="$2" rows="$3"
  mkdir -p "$WORK/$name/cells"
  convert "$WORK/$name/keyed.png" \
    -crop "${columns}x${rows}@" +repage \
    "$WORK/$name/cells/cell-%03d.png"
}

split_magic_grid() {
  local name="magic"
  local width
  width="$(identify -format '%w' "$WORK/$name/keyed.png")"
  test "$width" -eq 1693
  mkdir -p "$WORK/$name/cells"

  local row start_y end_y height frame left right cell_index
  local -a bounds
  for row in 0 1 2 3; do
    case "$row" in
      0)
        start_y=0
        end_y=250
        bounds=(0 199 416 644 870 1102 1323 1514 1693)
        ;;
      1)
        start_y=250
        end_y=480
        bounds=(0 212 435 675 908 1120 1327 1693)
        ;;
      2)
        start_y=480
        end_y=700
        bounds=(0 204 413 640 866 1080 1293 1493 1693)
        ;;
      *)
        start_y=700
        end_y=929
        bounds=(0 221 436 605 730 918 1188 1434 1693)
        ;;
    esac
    height=$((end_y - start_y))
    if [ "$row" -eq 1 ]; then
      for frame in {0..6}; do
        left="${bounds[$frame]}"
        right="${bounds[$((frame + 1))]}"
        cell_index=$((row * 8 + frame))
        convert "$WORK/$name/keyed.png" \
          -crop "$((right - left))x${height}+${left}+${start_y}" +repage \
          "$WORK/$name/cells/cell-$(printf '%03d' "$cell_index").png"
      done
      cp "$WORK/$name/cells/cell-014.png" "$WORK/$name/cells/cell-015.png"
      continue
    fi
    for frame in {0..7}; do
      left="${bounds[$frame]}"
      right="${bounds[$((frame + 1))]}"
      cell_index=$((row * 8 + frame))
      convert "$WORK/$name/keyed.png" \
        -crop "$((right - left))x${height}+${left}+${start_y}" +repage \
        "$WORK/$name/cells/cell-$(printf '%03d' "$cell_index").png"
    done
  done
}

normalize_group() {
  local name="$1" start="$2" count="$3" visible="$4" gravity="$5"
  local max_w=1 max_h=1 index input width height percent
  for ((index=start; index<start+count; index++)); do
    input="$WORK/$name/cells/cell-$(printf '%03d' "$index").png"
    read -r width height < <(convert "$input" -alpha extract -trim -format '%w %h\n' info:)
    (( width > max_w )) && max_w="$width"
    (( height > max_h )) && max_h="$height"
  done
  percent="$(awk -v visible="$visible" -v w="$max_w" -v h="$max_h" \
    'BEGIN { a=visible/w; b=visible/h; s=(a<b?a:b); printf "%.4f", s*100 }')"
  for ((index=start; index<start+count; index++)); do
    input="$WORK/$name/cells/cell-$(printf '%03d' "$index").png"
    convert -size "${CELL}x${CELL}" xc:none \
      \( "$input" -trim +repage -filter Lanczos -resize "${percent}%" \) \
      -gravity "$gravity" \
      -geometry "$([ "$gravity" = south ] && printf '+0+%s' "$BOTTOM" || printf '+0+0')" \
      -compose over -composite \
      -depth 8 -strip -define png:color-type=6 \
      "$WORK/$name/cells/normalized-$(printf '%03d' "$index").png"
  done
}

normalize_rows() {
  local name="$1" columns="$2" rows="$3" visible="$4" gravity="$5" row
  for ((row=0; row<rows; row++)); do
    normalize_group "$name" "$((row * columns))" "$columns" "$visible" "$gravity"
  done
}

normalize_gaze_overlays() {
  local name="gaze" max_w=1 max_h=1 index input width height percent
  for index in {0..15}; do
    input="$WORK/$name/cells/cell-$(printf '%03d' "$index").png"
    read -r width height < <(convert "$input" -alpha extract -trim -format '%w %h\n' info:)
    (( width > max_w )) && max_w="$width"
    (( height > max_h )) && max_h="$height"
  done
  percent="$(awk -v visible=104 -v w="$max_w" -v h="$max_h" \
    'BEGIN { a=visible/w; b=visible/h; s=(a<b?a:b); printf "%.4f", s*100 }')"
  for index in {0..15}; do
    input="$WORK/$name/cells/cell-$(printf '%03d' "$index").png"
    convert -size "${CELL}x${CELL}" xc:none \
      \( "$input" -trim +repage -filter Lanczos -resize "${percent}%" \) \
      -gravity northwest -geometry '+22+72' -compose over -composite \
      -depth 8 -strip -define png:color-type=6 \
      "$WORK/$name/cells/normalized-$(printf '%03d' "$index").png"
  done
}

build_grid() {
  local name="$1" columns="$2" rows="$3" output="$4" row column index
  local staged="$output.staged.png"
  local row_files=()
  for ((row=0; row<rows; row++)); do
    local cells=()
    for ((column=0; column<columns; column++)); do
      index=$((row * columns + column))
      cells+=("$WORK/$name/cells/normalized-$(printf '%03d' "$index").png")
    done
    convert "${cells[@]}" +append "$WORK/$name/row-$(printf '%02d' "$row").png"
    row_files+=("$WORK/$name/row-$(printf '%02d' "$row").png")
  done
  convert "${row_files[@]}" -append \
    -depth 8 -strip -define png:color-type=6 \
    -define png:compression-level=6 "$staged"
  identify "$staged" >/dev/null
  mv "$staged" "$output"
}

build_horizontal() {
  local name="$1" count="$2" output="$3" index
  local staged="$output.staged.png"
  local cells=()
  for ((index=0; index<count; index++)); do
    cells+=("$WORK/$name/cells/normalized-$(printf '%03d' "$index").png")
  done
  convert "${cells[@]}" +append \
    -depth 8 -strip -define png:color-type=6 \
    -define png:compression-level=6 "$staged"
  identify "$staged" >/dev/null
  mv "$staged" "$output"
}

atlas_row() {
  local input="$1" row="$2" output="$3"
  convert "$input" -crop "2048x256+0+$((row * CELL))" +repage "$output"
}

compose_atlas() {
  local output="$1"
  local staged="$output.staged.png"
  shift
  convert "$@" -append \
    -depth 8 -strip -define png:color-type=6 \
    -define png:compression-level=6 "$staged"
  identify "$staged" >/dev/null
  mv "$staged" "$output"
}

key_sheet magic "$SOURCE/pupu-magic-v13-chroma.png" "#00ff00"
split_magic_grid
normalize_rows magic 8 4 "$VISIBLE" south
build_grid magic 8 4 "$WORK/magic-grid.png"

key_sheet coin "$SOURCE/pupu-coin-states-v13-chroma.png" "#00ff00"
split_grid coin 5 1
normalize_group coin 0 5 196 center

key_sheet gaze "$SOURCE/pupu-gaze-overlays-v13-chroma.png" "#ff00ff"
split_grid gaze 4 4
normalize_gaze_overlays
build_horizontal gaze 16 "$ACTIONS/pupu-gaze-overlays-youthful-v13.png"

key_sheet chase "$SOURCE/pupu-anchor-chase-16dir-v13-chroma.png" "#00ff00"
split_grid chase 4 4
normalize_group chase 0 16 "$VISIBLE" south
build_horizontal chase 16 "$ACTIONS/pupu-anchor-chase-16dir-youthful-v13.png"

key_sheet harness "$SOURCE/pupu-walk-harness-16dir-v13-chroma.png" "#00ff00"
split_grid harness 4 4
normalize_group harness 0 16 "$VISIBLE" south
build_horizontal harness 16 "$ACTIONS/pupu-walk-harness-16dir-youthful-v13.png"

key_sheet litter "$SOURCE/pupu-litter-v13-chroma.png" "#00ff00"
split_grid litter 8 4
normalize_rows litter 8 4 "$VISIBLE" south
build_grid litter 8 4 "$ASSETS/pupu-litter-youthful-v13.png"

key_sheet interactions "$SOURCE/pupu-interactions-v13-chroma.png" "#00ff00"
split_grid interactions 8 4
normalize_rows interactions 8 4 "$VISIBLE" south
build_grid interactions 8 4 "$WORK/interactions-grid.png"

key_sheet social "$SOURCE/pupu-social-care-v13-chroma.png" "#00ff00"
split_grid social 8 4
normalize_rows social 8 4 "$VISIBLE" south
build_grid social 8 4 "$WORK/social-grid.png"

for row in {0..4}; do
  atlas_row "$ASSETS/pupu-specials-youthful-v13.png" "$row" \
    "$WORK/specials-old-$row.png"
done
for row in {0..3}; do
  atlas_row "$WORK/magic-grid.png" "$row" "$WORK/magic-$row.png"
done
compose_atlas "$ASSETS/pupu-specials-youthful-v13.png" \
  "$WORK/specials-old-0.png" \
  "$WORK/magic-0.png" "$WORK/magic-1.png" \
  "$WORK/magic-2.png" "$WORK/magic-3.png"

for row in {0..7}; do
  atlas_row "$ASSETS/pupu-activity-youthful-v13.png" "$row" \
    "$WORK/activity-old-$row.png"
done
atlas_row "$WORK/interactions-grid.png" 0 "$WORK/interaction-laser-chase.png"
atlas_row "$WORK/interactions-grid.png" 1 "$WORK/interaction-laser-paw.png"
compose_atlas "$ASSETS/pupu-activity-youthful-v13.png" \
  "$WORK/interaction-laser-chase.png" "$WORK/interaction-laser-paw.png" \
  "$WORK/activity-old-2.png" "$WORK/activity-old-3.png" \
  "$WORK/activity-old-4.png" "$WORK/activity-old-5.png" \
  "$WORK/activity-old-6.png" "$WORK/activity-old-6.png"

for row in {0..5}; do
  atlas_row "$ASSETS/pupu-touch-youthful-v13.png" "$row" \
    "$WORK/touch-old-$row.png"
done
atlas_row "$WORK/interactions-grid.png" 2 "$WORK/touch-needs-space.png"
atlas_row "$WORK/interactions-grid.png" 3 "$WORK/touch-trust.png"
atlas_row "$WORK/social-grid.png" 3 "$WORK/touch-over-rua.png"
compose_atlas "$ASSETS/pupu-touch-youthful-v13.png" \
  "$WORK/touch-old-0.png" "$WORK/touch-old-1.png" "$WORK/touch-old-2.png" \
  "$WORK/touch-needs-space.png" "$WORK/touch-over-rua.png" "$WORK/touch-trust.png"

for row in {0..7}; do
  atlas_row "$ROOT/AssetSources/legacy-formal-v12/pupu-life-youthful-v6.png" "$row" \
    "$WORK/life-old-$row.png"
done
atlas_row "$WORK/social-grid.png" 0 "$WORK/social-groom.png"
atlas_row "$WORK/social-grid.png" 1 "$WORK/social-attention.png"
compose_atlas "$ASSETS/pupu-life-youthful-v13.png" \
  "$WORK/life-old-0.png" "$WORK/life-old-1.png" "$WORK/life-old-2.png" \
  "$WORK/social-groom.png" "$WORK/social-attention.png" \
  "$WORK/life-old-5.png" "$WORK/life-old-6.png" "$WORK/life-old-7.png"

for row in {0..2}; do
  atlas_row "$ASSETS/pupu-life-equipment-youthful-v13.png" "$row" \
    "$WORK/equipment-old-$row.png"
done
atlas_row "$WORK/social-grid.png" 2 "$WORK/social-ask-walk.png"
compose_atlas "$ASSETS/pupu-life-equipment-youthful-v13.png" \
  "$WORK/equipment-old-0.png" "$WORK/equipment-old-1.png" "$WORK/social-ask-walk.png"

convert "$ROOT/AssetSources/legacy-formal-v12/pupu-motion-youthful-v12.png" \
  -crop 2048x2560+0+0 +repage \
  -depth 8 -strip -define png:color-type=6 \
  "$WORK/pupu-motion-youthful-v13.png"
mv "$WORK/pupu-motion-youthful-v13.png" "$ASSETS/pupu-motion-youthful-v13.png"

for row in {0..2}; do
  atlas_row "$ASSETS/pupu-gaze-coin-youthful-v13.png" "$row" \
    "$WORK/gaze-coin-old-$row.png"
done
for frame in {0..4}; do
  cp "$WORK/coin/cells/normalized-$(printf '%03d' "$frame").png" \
    "$WORK/coin-atlas-$frame.png"
done
cp "$WORK/coin-atlas-4.png" "$WORK/coin-atlas-5.png"
cp "$WORK/coin-atlas-0.png" "$WORK/coin-atlas-6.png"
cp "$WORK/coin-atlas-4.png" "$WORK/coin-atlas-7.png"
convert "$WORK"/coin-atlas-{0..7}.png +append "$WORK/coin-row.png"
compose_atlas "$ASSETS/pupu-gaze-coin-youthful-v13.png" \
  "$WORK/gaze-coin-old-0.png" "$WORK/gaze-coin-old-1.png" "$WORK/coin-row.png"

identify \
  "$ASSETS/pupu-specials-youthful-v13.png" \
  "$ASSETS/pupu-activity-youthful-v13.png" \
  "$ASSETS/pupu-touch-youthful-v13.png" \
  "$ASSETS/pupu-life-youthful-v13.png" \
  "$ASSETS/pupu-life-equipment-youthful-v13.png" \
  "$ASSETS/pupu-motion-youthful-v13.png" \
  "$ASSETS/pupu-gaze-coin-youthful-v13.png" \
  "$ASSETS/pupu-litter-youthful-v13.png" \
  "$ACTIONS"/*.png
