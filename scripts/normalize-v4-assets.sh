#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
SOURCE="$ROOT/AssetSources"
WORK="$ROOT/.asset-work/v4"
ASSETS="$ROOT/Pupu.Desktop/Assets"

normalize_grid() {
  local input="$1" source_columns="$2" rows="$3" output="$4" name="$5"
  local x_boundaries_text="$6" y_boundaries_text="$7"
  local directory="$WORK/$name"
  mkdir -p "$directory/cells" "$directory/frames"
  find "$directory" -type f -name '*.png' -delete

  local source_width source_height
  read -r source_width source_height < <(identify -format '%w %h\n' "$input")

  local -a x_boundaries y_boundaries
  read -ra x_boundaries <<< "$x_boundaries_text"
  read -ra y_boundaries <<< "$y_boundaries_text"
  [[ "${#x_boundaries[@]}" -eq $((source_columns + 1)) ]] || exit 2
  [[ "${#y_boundaries[@]}" -eq $((rows + 1)) ]] || exit 2

  local row column x0 x1 y0 y1 width height index
  for ((row = 0; row < rows; row++)); do
    y0=$((${y_boundaries[$row]} + 3))
    y1=$((${y_boundaries[$((row + 1))]} - 3))
    height=$((y1 - y0))
    for ((column = 0; column < source_columns; column++)); do
      x0=$((${x_boundaries[$column]} + 3))
      x1=$((${x_boundaries[$((column + 1))]} - 3))
      width=$((x1 - x0))
      index=$((row * source_columns + column))
      convert "$input" -crop "${width}x${height}+${x0}+${y0}" +repage \
        -bordercolor none -border 1 -trim +repage -resize '160x160>' \
        -gravity center -background none -extent 192x192 \
        "$directory/cells/frame-$(printf '%03d' "$index").png"
    done
  done

  local -a map
  case "$source_columns" in
    8) map=(0 1 2 3 4 5 6 7) ;;
    7) map=(0 1 2 3 3 4 5 6) ;;
    6) map=(0 1 2 2 3 4 5 5) ;;
    *) echo "Unsupported source column count: $source_columns" >&2; exit 2 ;;
  esac

  local output_column source_column source_index output_index
  local -a row_files frame_files
  local row_file
  for ((row = 0; row < rows; row++)); do
    frame_files=()
    for ((output_column = 0; output_column < 8; output_column++)); do
      source_column="${map[$output_column]}"
      source_index=$((row * source_columns + source_column))
      output_index=$((row * 8 + output_column))
      cp "$directory/cells/frame-$(printf '%03d' "$source_index").png" \
         "$directory/frames/frame-$(printf '%03d' "$output_index").png"
      frame_files+=("$directory/frames/frame-$(printf '%03d' "$output_index").png")
    done
    row_file="$directory/row-$(printf '%02d' "$row").png"
    convert "${frame_files[@]}" +append "$row_file"
    row_files+=("$row_file")
  done
  convert "${row_files[@]}" -append +repage "$output"
}

normalize_grid \
  "$SOURCE/pupu-routines-youthful-source.png" 7 8 \
  "$ASSETS/pupu-routines-youthful-v4.png" routines \
  "0 179 356 535 717 896 1076 1254" \
  "0 146 285 438 579 731 884 1036 1254"

normalize_grid \
  "$SOURCE/pupu-walk-modes-youthful-source.png" 6 8 \
  "$ASSETS/pupu-walk-modes-youthful-v4.png" walk-modes \
  "0 209 418 627 836 1045 1254" \
  "0 171 332 489 644 809 955 1101 1254"

echo "Generated Pupu 0.7.0 routine and walk-mode atlases in $ASSETS"
