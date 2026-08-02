#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
SOURCE="$ROOT/AssetSources"
WORK="$ROOT/.asset-work"
ASSETS="$ROOT/Pupu.Desktop/Assets"

prepare_directories() {
  local name="$1"
  mkdir -p "$WORK/$name/raw" "$WORK/$name/clean" "$WORK/$name/frames"
  find "$WORK/$name/raw" "$WORK/$name/clean" "$WORK/$name/frames" -type f -name '*.png' -delete
}

detect_and_split() {
  local source="$1" source_columns="$2" rows="$3" name="$4"
  local directory="$WORK/$name"
  local source_width source_height
  read -r source_width source_height < <(identify -format '%w %h\n' "$source")

  local row y0 y1 row_height component_output
  for ((row = 0; row < rows; row++)); do
    y0=$((row * source_height / rows))
    y1=$(((row + 1) * source_height / rows))
    row_height=$((y1 - y0))
    convert "$source" -crop "${source_width}x${row_height}+0+${y0}" +repage "$directory/row.png"
    component_output="$(convert "$directory/row.png" -alpha extract -threshold 5% \
      -define connected-components:verbose=true -connected-components 8 null: 2>&1)"
    mapfile -t centers < <(
      printf '%s\n' "$component_output" \
        | awk '/gray\(255\)$/ { split($3, point, ","); print int(point[1] + 0.5), $4 }' \
        | sort -k2,2nr | head -n "$source_columns" | sort -k1,1n | awk '{ print $1 }'
    )
    [[ "${#centers[@]}" -eq "$source_columns" ]] || {
      echo "Cannot detect $source_columns Pupu figures in $name row $row" >&2
      exit 2
    }

    local column left right previous next width index
    for ((column = 0; column < source_columns; column++)); do
      if ((column == 0)); then left=0; else
        previous="${centers[$((column - 1))]}"
        left=$(((previous + centers[column]) / 2))
      fi
      if ((column == source_columns - 1)); then right="$source_width"; else
        next="${centers[$((column + 1))]}"
        right=$(((centers[column] + next) / 2))
      fi
      width=$((right - left))
      index=$((row * source_columns + column))
      convert "$directory/row.png" -crop "${width}x${row_height}+${left}+0" +repage \
        "$directory/raw/frame-$(printf '%03d' "$index").png"
    done
  done
  rm -f "$directory/row.png"
}

equal_split_without_grid_lines() {
  local source="$1" columns="$2" rows="$3" name="$4"
  local directory="$WORK/$name"
  convert "$source" -crop "${columns}x${rows}@" +repage "$directory/raw/frame-%03d.png"
  local frame temporary
  for frame in "$directory/raw"/frame-*.png; do
    temporary="$directory/shaved.png"
    convert "$frame" -shave 3x3 -bordercolor none -border 3 "$temporary"
    mv "$temporary" "$frame"
  done
}

clean_and_normalize() {
  local source_columns="$1" rows="$2" name="$3" output="$4"
  local directory="$WORK/$name"
  local frame mask
  for frame in "$directory/raw"/frame-*.png; do
    mask="$directory/mask.png"
    convert "$frame" -alpha extract -threshold 5% \
      -define connected-components:keep-top=1 -connected-components 8 -auto-level "$mask"
    convert "$frame" "$mask" -alpha off -compose CopyOpacity -composite \
      "$directory/clean/$(basename "$frame")"
  done
  rm -f "$directory/mask.png"

  local -a map
  if ((source_columns == 8)); then map=(0 1 2 3 4 5 6 7); else map=(0 1 2 2 3 4 5 5); fi
  local row column source_column source_index output_index
  for ((row = 0; row < rows; row++)); do
    for ((column = 0; column < 8; column++)); do
      source_column="${map[$column]}"
      source_index=$((row * source_columns + source_column))
      output_index=$((row * 8 + column))
      convert "$directory/clean/frame-$(printf '%03d' "$source_index").png" \
        -bordercolor none -border 1 -trim +repage -resize '160x160>' \
        -gravity center -background none -extent 192x192 \
        "$directory/frames/frame-$(printf '%03d' "$output_index").png"
    done
  done
  local -a row_files frame_files
  local row_file frame_column frame_number
  for ((row = 0; row < rows; row++)); do
    frame_files=()
    for ((frame_column = 0; frame_column < 8; frame_column++)); do
      frame_number=$((row * 8 + frame_column))
      frame_files+=("$directory/frames/frame-$(printf '%03d' "$frame_number").png")
    done
    row_file="$directory/row-$(printf '%02d' "$row").png"
    convert "${frame_files[@]}" +append "$row_file"
    row_files+=("$row_file")
  done
  convert "${row_files[@]}" -append +repage "$output"
  rm -f "$directory"/row-*.png
}

prepare_directories core
detect_and_split "$SOURCE/pupu-core-youthful-source.png" 6 6 core
clean_and_normalize 6 6 core "$ASSETS/pupu-core-youthful-v3.png"

prepare_directories life
equal_split_without_grid_lines "$SOURCE/pupu-life-youthful-source.png" 8 8 life
clean_and_normalize 8 8 life "$ASSETS/pupu-life-youthful-v3.png"
# The generator placed one transition pose across a cell boundary. Hold the
# previous complete reclining pose for one extra beat instead of shipping a
# squared-off torso.
convert "$ASSETS/pupu-life-youthful-v3.png" \
  \( "$ASSETS/pupu-life-youthful-v3.png" -crop 192x192+192+1152 +repage \) \
  -geometry +384+1152 -composite "$WORK/life-repaired.png"
mv "$WORK/life-repaired.png" "$ASSETS/pupu-life-youthful-v3.png"

prepare_directories directions
detect_and_split "$SOURCE/pupu-directions-youthful-source.png" 8 4 directions
clean_and_normalize 8 4 directions "$ASSETS/pupu-directions-youthful-v3.png"

prepare_directories touch
detect_and_split "$SOURCE/pupu-touch-youthful-source.png" 6 6 touch
clean_and_normalize 6 6 touch "$ASSETS/pupu-touch-youthful-v3.png"

echo "Generated four youthful Pupu atlases in $ASSETS"
