#!/usr/bin/env bash
# Avento iOS pipeline:
#   1) Build PlacedContent AssetBundle (iOS)
#   2) Reveal file for avento-web upload (Unity Scene → Upload iOS bundle)
#   3) Export Unity as a Library (iOS)
#   4) xcodebuild UnityFramework
#   5) integrate into avento-app
#   6) Open avento-app Xcode project
#
# Usage:
#   ./scripts/rebuild-ios-uaal.sh
#   ./scripts/rebuild-ios-uaal.sh --skip-bundle      # reuse existing AssetBundles/iOS/placedcontent
#   ./scripts/rebuild-ios-uaal.sh --skip-export      # reuse Builds/iOS_UaaL
#   ./scripts/rebuild-ios-uaal.sh --skip-upload-hint # don't open Finder for upload
#   ./scripts/rebuild-ios-uaal.sh --no-open          # don't open Xcode at the end
#
# Env overrides:
#   UNITY=/Applications/Unity/Hub/Editor/6000.5.6f1/Unity.app/Contents/MacOS/Unity
#   PROJECT=/Users/andreyorlov/AR_TEST
#   AVENTO_APP=/Users/andreyorlov/Projects/atlyx-project/avento-app
#   OUT=$PROJECT/Builds/iOS_UaaL
#   IOS_DEVICE=                              # optional udid for xcodebuild -destination
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PROJECT="${PROJECT:-$ROOT}"
UNITY="${UNITY:-/Applications/Unity/Hub/Editor/6000.5.6f1/Unity.app/Contents/MacOS/Unity}"
AVENTO_APP="${AVENTO_APP:-/Users/andreyorlov/Projects/atlyx-project/avento-app}"
OUT="${OUT:-$PROJECT/Builds/iOS_UaaL}"
LOG_DIR="${LOG_DIR:-$PROJECT/Builds}"
BUNDLE_PATH="$PROJECT/AssetBundles/iOS/placedcontent"
MIN_BUNDLE_BYTES=$((64 * 1024))

SKIP_BUNDLE=0
SKIP_EXPORT=0
SKIP_UPLOAD_HINT=0
SKIP_FW=0
SKIP_INTEGRATE=0
NO_OPEN=0

for arg in "$@"; do
  case "$arg" in
    --skip-bundle) SKIP_BUNDLE=1 ;;
    --skip-export) SKIP_EXPORT=1 ;;
    --skip-upload-hint) SKIP_UPLOAD_HINT=1 ;;
    --skip-framework|--skip-fw) SKIP_FW=1 ;;
    --skip-integrate) SKIP_INTEGRATE=1 ;;
    --no-open) NO_OPEN=1 ;;
    -h|--help)
      sed -n '2,25p' "$0"
      exit 0
      ;;
    *)
      echo "Unknown arg: $arg (try --help)"
      exit 1
      ;;
  esac
done

mkdir -p "$LOG_DIR" "$(dirname "$OUT")"

die() { echo "ERROR: $*" >&2; exit 1; }

need_unity() {
  [[ -x "$UNITY" ]] || die "Unity not found at $UNITY (set UNITY=...)"
}

run_unity() {
  local method="$1"
  local log="$2"
  shift 2
  echo ""
  echo "==> Unity -executeMethod $method"
  echo "    log: $log"
  # Unity may return non-zero on some warnings; check log + artifacts after.
  set +e
  "$UNITY" \
    -batchmode \
    -nographics \
    -quit \
    -projectPath "$PROJECT" \
    -buildTarget iOS \
    -logFile "$log" \
    -executeMethod "$method" \
    "$@"
  local rc=$?
  set -e
  if [[ $rc -ne 0 ]]; then
    echo "WARNING: Unity exited with code $rc — check $log"
    tail -n 40 "$log" || true
  fi
  return 0
}

echo "=============================================="
echo " Avento iOS UaaL rebuild"
echo "=============================================="
echo "PROJECT:    $PROJECT"
echo "UNITY:      $UNITY"
echo "AVENTO_APP: $AVENTO_APP"
echo "OUT:        $OUT"
echo ""

# ---------------------------------------------------------------------------
# 1) AssetBundle (iOS)
# ---------------------------------------------------------------------------
if [[ "$SKIP_BUNDLE" -eq 0 ]]; then
  need_unity
  run_unity \
    "UnityEngine.XR.Templates.AR.Editor.PlacedContentBundleBuilder.BuildForIosBatch" \
    "$LOG_DIR/placedcontent-ios-bundle.log"
else
  echo "==> Skipping AssetBundle build (--skip-bundle)"
fi

[[ -f "$BUNDLE_PATH" ]] || die "Missing $BUNDLE_PATH — build the iOS AssetBundle first"
BUNDLE_SIZE=$(stat -f%z "$BUNDLE_PATH" 2>/dev/null || stat -c%s "$BUNDLE_PATH")
if (( BUNDLE_SIZE < MIN_BUNDLE_BYTES )); then
  die "Bundle too small ($BUNDLE_SIZE bytes). Expected ~20MB placedcontent, not the iOS catalog file."
fi
echo "==> AssetBundle OK: $BUNDLE_PATH ($(python3 -c "print(f'{$BUNDLE_SIZE/1024/1024:.1f} MB')"))"

# ---------------------------------------------------------------------------
# 2) Upload hint (avento-web is manual unless you wire auth later)
# ---------------------------------------------------------------------------
if [[ "$SKIP_UPLOAD_HINT" -eq 0 ]]; then
  echo ""
  echo "==> Upload this file in avento-web:"
  echo "    Admin → offer VR → Unity Scene → Upload iOS bundle"
  echo "    File: $BUNDLE_PATH"
  echo "    (~$(python3 -c "print(f'{$BUNDLE_SIZE/1024/1024:.0f}')") MB, no extension — use “All files” in the picker if needed)"
  if command -v open >/dev/null 2>&1; then
    open -R "$BUNDLE_PATH" || true
  fi
else
  echo "==> Skipping upload hint (--skip-upload-hint)"
fi

# ---------------------------------------------------------------------------
# 3) Export UaaL iOS Xcode project
# ---------------------------------------------------------------------------
if [[ "$SKIP_EXPORT" -eq 0 ]]; then
  need_unity
  # Fresh export folder avoids stale append / mixed Data.
  if [[ -d "$OUT" ]]; then
    echo "==> Removing previous export: $OUT"
    rm -rf "$OUT"
  fi
  run_unity \
    "UnityEngine.XR.Templates.AR.Editor.AventoUaalIosExporter.ExportIosLibraryBatch" \
    "$LOG_DIR/uaal-ios-export.log" \
    "-aventoUaalOut=$OUT"
  [[ -d "$OUT/Data" ]] || die "Export missing Data/ — see $LOG_DIR/uaal-ios-export.log"
  [[ -f "$OUT/Data/boot.config" ]] || die "Export missing Data/boot.config"
  echo "==> UaaL export OK: $OUT"
else
  echo "==> Skipping UaaL export (--skip-export)"
  [[ -d "$OUT/Data" ]] || die "No export at $OUT (run without --skip-export)"
fi

# ---------------------------------------------------------------------------
# 4) Build UnityFramework (device)
# ---------------------------------------------------------------------------
if [[ "$SKIP_FW" -eq 0 ]]; then
  XCODEPROJ="$OUT/Unity-iPhone.xcodeproj"
  [[ -d "$XCODEPROJ" ]] || die "Missing $XCODEPROJ"

  FW_LOG="$LOG_DIR/unityframework-ios.log"
  DEST="${IOS_DESTINATION:-generic/platform=iOS}"
  echo ""
  echo "==> xcodebuild UnityFramework ($DEST)"
  echo "    log: $FW_LOG"

  # Prefer UnityFramework scheme; fall back to building the framework target.
  set +e
  xcodebuild \
    -project "$XCODEPROJ" \
    -scheme UnityFramework \
    -configuration Release \
    -destination "$DEST" \
    -derivedDataPath "$OUT/DerivedData" \
    build \
    CODE_SIGNING_ALLOWED=NO \
    CODE_SIGNING_REQUIRED=NO \
    CODE_SIGN_IDENTITY="" \
    >"$FW_LOG" 2>&1
  XC_RC=$?
  set -e

  if [[ $XC_RC -ne 0 ]]; then
    echo "WARNING: UnityFramework scheme build failed (rc=$XC_RC). Tail of log:"
    tail -n 50 "$FW_LOG" || true
    echo "Trying ReleaseForRunning + UnityFramework target…"
    set +e
    xcodebuild \
      -project "$XCODEPROJ" \
      -target UnityFramework \
      -configuration ReleaseForRunning \
      -destination "$DEST" \
      -derivedDataPath "$OUT/DerivedData" \
      build \
      CODE_SIGNING_ALLOWED=NO \
      CODE_SIGNING_REQUIRED=NO \
      CODE_SIGN_IDENTITY="" \
      >>"$FW_LOG" 2>&1
    XC_RC=$?
    set -e
  fi

  FW_BUILT="$(find "$OUT/DerivedData/Build/Products" -type d -name 'UnityFramework.framework' 2>/dev/null | head -n 1 || true)"
  if [[ -z "$FW_BUILT" ]]; then
    # Also accept frameworks built into system DerivedData from a prior manual Xcode build.
    FW_BUILT="$(find "$HOME/Library/Developer/Xcode/DerivedData" -path "*Unity-iPhone*" -type d -name 'UnityFramework.framework' 2>/dev/null \
      | while read -r d; do echo "$(stat -f '%m' "$d/UnityFramework" 2>/dev/null || echo 0) $d"; done \
      | sort -rn | head -n 1 | awk '{ $1=""; sub(/^ /,""); print }' || true)"
  fi

  [[ -n "$FW_BUILT" && -d "$FW_BUILT" ]] || die "UnityFramework.framework not found after xcodebuild. See $FW_LOG"

  STAGE="$OUT/build/Release-iphoneos"
  mkdir -p "$STAGE"
  rm -rf "$STAGE/UnityFramework.framework"
  cp -R "$FW_BUILT" "$STAGE/UnityFramework.framework"
  echo "==> Staged framework: $STAGE/UnityFramework.framework"
  stat -f '%Sm %z %N' "$STAGE/UnityFramework.framework/UnityFramework"
else
  echo "==> Skipping UnityFramework build (--skip-fw)"
fi

# ---------------------------------------------------------------------------
# 5) Integrate into avento-app
# ---------------------------------------------------------------------------
if [[ "$SKIP_INTEGRATE" -eq 0 ]]; then
  INTEGRATE="$AVENTO_APP/scripts/integrate-unity-ios.sh"
  [[ -x "$INTEGRATE" ]] || die "Missing $INTEGRATE"
  echo ""
  echo "==> Integrating into avento-app"
  "$INTEGRATE" "$OUT"
else
  echo "==> Skipping integrate (--skip-integrate)"
fi

# ---------------------------------------------------------------------------
# 6) Open Xcode
# ---------------------------------------------------------------------------
APP_XCODE="$AVENTO_APP/ios/App/App.xcodeproj"
echo ""
echo "=============================================="
echo " Done"
echo "=============================================="
echo "1. Confirm avento-web has the NEW iOS placedcontent (~20MB) uploaded."
echo "2. In Xcode: Clean Build Folder → Run on iPhone."
echo "3. Open Unity Scene → tap a plane."
echo ""
echo "Bundle:  $BUNDLE_PATH"
echo "Export:  $OUT"
echo "App:     $APP_XCODE"

if [[ "$NO_OPEN" -eq 0 ]]; then
  if [[ -d "$APP_XCODE" ]] && command -v open >/dev/null 2>&1; then
    open "$APP_XCODE"
  fi
fi
