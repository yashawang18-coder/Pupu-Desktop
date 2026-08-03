#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"

# The Python auditor reads the formal manifest instead of repeating generated
# filenames here, so a V15 rebuild cannot leave this script validating V13.
python3 "$ROOT/scripts/audit-asset-quality.py"

echo "Verified 13 HD atlases, 616 atlas cells, 110 independent frames, full PNG decode, V17 front-facing five-state coin, 20px margins, despilled edges, stable anchors, and closed 8-direction pursuit gait."
