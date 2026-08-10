#!/usr/bin/env bash
# Batch-export avento-ar as Unity-as-a-Library (iOS).
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
UNITY="${UNITY:-/Applications/Unity/Hub/Editor/6000.5.6f1/Unity.app/Contents/MacOS/Unity}"
PROJECT="${PROJECT:-$ROOT}"
OUT="${OUT:-$PROJECT/Builds/iOS_UaaL}"
LOG="${LOG:-$PROJECT/Builds/uaal-ios-export.log}"

mkdir -p "$(dirname "$OUT")" "$(dirname "$LOG")"

if pgrep -f 'Unity.app/Contents/MacOS/Unity' >/dev/null 2>&1 && [[ -f "$PROJECT/Temp/UnityLockfile" ]]; then
  echo "ERROR: Unity already has this project open. Close the Editor, then re-run." >&2
  exit 1
fi

echo "Unity:   $UNITY"
echo "Project: $PROJECT"
echo "Out:     $OUT"
echo "Log:     $LOG"

"$UNITY" \
  -batchmode \
  -nographics \
  -quit \
  -projectPath "$PROJECT" \
  -buildTarget iOS \
  -logFile "$LOG" \
  -executeMethod UnityEngine.XR.Templates.AR.Editor.AventoUaalIosExporter.ExportIosLibraryBatch \
  "-aventoUaalOut=$OUT"

echo "Export finished. See $LOG"
ls -la "$OUT" | head -30
