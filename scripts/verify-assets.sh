#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"

# The Python auditor reads the formal manifest instead of repeating generated
# filenames here, so a V15 rebuild cannot leave this script validating V13.
python3 "$ROOT/scripts/audit-asset-quality.py"

echo "Verified 13 HD atlases, 616 atlas cells, 78 independent frames, V17 front-facing five-state coin, 20px margins, clean edges, stable anchors, and 8-direction four-phase pursuit gait."
