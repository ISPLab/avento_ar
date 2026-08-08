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
#       → same as --skip-bundle --skip-upload-hint (rebuild UaaL player into avento-app)
#   ./scripts/rebuild-ios-uaal.sh -i|--interactive
#       → toggle steps; preselected defaults = skip bundle + upload hint
#   ./scripts/rebuild-ios-uaal.sh --full                             # all steps (no skips)
#   ./scripts/rebuild-ios-uaal.sh --skip-bundle --skip-upload-hint   # same as bare run
#   ./scripts/rebuild-ios-uaal.sh --skip-export
#   ./scripts/rebuild-ios-uaal.sh --no-open
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

# Defaults match day-to-day UaaL rebuild (content upload is separate).
SKIP_BUNDLE=1
SKIP_EXPORT=0
SKIP_UPLOAD_HINT=1
SKIP_FW=0
SKIP_INTEGRATE=0
NO_OPEN=0

die() { echo "ERROR: $*" >&2; exit 1; }

flag_label() {
  # $1 = skip flag (1=skipped); invert for "will run"
  if [[ "$1" -eq 0 ]]; then echo "ON "; else echo "off"; fi
}

print_plan() {
  echo ""
  echo "Plan (toggle with number, then Enter to start):"
  echo "  [1] Build PlacedContent AssetBundle   $(flag_label "$SKIP_BUNDLE")"
  echo "  [2] Upload hint (reveal in Finder)    $(flag_label "$SKIP_UPLOAD_HINT")"
  echo "  [3] Export UaaL Xcode project         $(flag_label "$SKIP_EXPORT")"
  echo "  [4] xcodebuild UnityFramework         $(flag_label "$SKIP_FW")"
  echo "  [5] Integrate into avento-app         $(flag_label "$SKIP_INTEGRATE")"
  echo "  [6] Open Xcode at end                 $(flag_label "$NO_OPEN")"
  echo ""
  echo "  [d] Defaults   (skip bundle + upload hint)"
  echo "  [f] Full       (all steps ON)"
  echo "  [Enter] Start"
  echo "  [q] Quit"
}

apply_defaults() {
  SKIP_BUNDLE=1
  SKIP_UPLOAD_HINT=1
  SKIP_EXPORT=0
  SKIP_FW=0
  SKIP_INTEGRATE=0
  NO_OPEN=0
}

apply_full() {
  SKIP_BUNDLE=0
  SKIP_UPLOAD_HINT=0
  SKIP_EXPORT=0
  SKIP_FW=0
  SKIP_INTEGRATE=0
  NO_OPEN=0
}

flip01() {
  if [[ "$1" -eq 0 ]]; then echo 1; else echo 0; fi
}

prompt_interactive_flags() {
  apply_defaults
  echo "=============================================="
  echo " Avento iOS UaaL rebuild — interactive"
  echo " Default: skip AssetBundle + skip upload hint"
  echo "=============================================="

  while true; do
    print_plan
    printf "> "
    # Empty Enter = start
    if ! IFS= read -r choice; then
      break
    fi
    case "$choice" in
      ""|s|S|start|START)
        break
        ;;
      1) SKIP_BUNDLE=$(flip01 "$SKIP_BUNDLE") ;;
      2) SKIP_UPLOAD_HINT=$(flip01 "$SKIP_UPLOAD_HINT") ;;
      3) SKIP_EXPORT=$(flip01 "$SKIP_EXPORT") ;;
      4) SKIP_FW=$(flip01 "$SKIP_FW") ;;
      5) SKIP_INTEGRATE=$(flip01 "$SKIP_INTEGRATE") ;;
      6) NO_OPEN=$(flip01 "$NO_OPEN") ;;
      d|D) apply_defaults ;;
      f|F) apply_full ;;
      q|Q)
        echo "Aborted."
        exit 0
        ;;
      *)
        echo "Unknown: $choice"
        ;;
    esac
  done
}

WANT_INTERACTIVE=0
ARGS_GIVEN=0

# Collect args first so bare run keeps day-to-day UaaL defaults.
for arg in "$@"; do
  case "$arg" in
    -i|--interactive) WANT_INTERACTIVE=1 ;;
    -h|--help)
      sed -n '2,30p' "$0"
      exit 0
      ;;
    --skip-bundle|--skip-export|--skip-upload-hint|--skip-framework|--skip-fw|--skip-integrate|--no-open|--full|--all|--defaults|--default)
      ARGS_GIVEN=1
      ;;
    *)
      echo "Unknown arg: $arg (try --help)"
      exit 1
      ;;
  esac
done

if [[ "$WANT_INTERACTIVE" -eq 1 ]]; then
  if [[ ! -t 0 ]]; then
    die "--interactive requires a TTY"
  fi
  prompt_interactive_flags
elif [[ "$ARGS_GIVEN" -eq 0 ]]; then
  # Bare ./scripts/rebuild-ios-uaal.sh → UaaL player rebuild only (export/fw/integrate).
  apply_defaults
  echo "Using defaults: --skip-bundle --skip-upload-hint (UaaL player → avento-app)"
  echo "Tip: add -i for an interactive step menu, or --full for all steps."
else
  # Explicit CLI: start from "run everything", then apply skip/full/defaults flags.
  SKIP_BUNDLE=0
  SKIP_UPLOAD_HINT=0
  SKIP_EXPORT=0
  SKIP_FW=0
  SKIP_INTEGRATE=0
  NO_OPEN=0
  for arg in "$@"; do
    case "$arg" in
      -i|--interactive) ;;
      --skip-bundle) SKIP_BUNDLE=1 ;;
      --skip-export) SKIP_EXPORT=1 ;;
      --skip-upload-hint) SKIP_UPLOAD_HINT=1 ;;
      --skip-framework|--skip-fw) SKIP_FW=1 ;;
      --skip-integrate) SKIP_INTEGRATE=1 ;;
      --no-open) NO_OPEN=1 ;;
      --full|--all) apply_full ;;
      --defaults|--default) apply_defaults ;;
    esac
  done
fi

mkdir -p "$LOG_DIR" "$(dirname "$OUT")"

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
echo "Flags:      skip-bundle=$SKIP_BUNDLE skip-upload-hint=$SKIP_UPLOAD_HINT skip-export=$SKIP_EXPORT skip-fw=$SKIP_FW skip-integrate=$SKIP_INTEGRATE no-open=$NO_OPEN"
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

NEED_BUNDLE=0
if [[ "$SKIP_BUNDLE" -eq 0 || "$SKIP_UPLOAD_HINT" -eq 0 ]]; then
  NEED_BUNDLE=1
fi

if [[ "$NEED_BUNDLE" -eq 1 ]]; then
  [[ -f "$BUNDLE_PATH" ]] || die "Missing $BUNDLE_PATH — build the iOS AssetBundle first (or use AR Test → Build AssetBundle from selected prefab)"
  BUNDLE_SIZE=$(stat -f%z "$BUNDLE_PATH" 2>/dev/null || stat -c%s "$BUNDLE_PATH")
  if (( BUNDLE_SIZE < MIN_BUNDLE_BYTES )); then
    die "Bundle too small ($BUNDLE_SIZE bytes). Expected ~20MB placedcontent, not the iOS catalog file."
  fi
  echo "==> AssetBundle OK: $BUNDLE_PATH ($(python3 -c "print(f'{$BUNDLE_SIZE/1024/1024:.1f} MB')"))"
else
  BUNDLE_SIZE=0
  if [[ -f "$BUNDLE_PATH" ]]; then
    BUNDLE_SIZE=$(stat -f%z "$BUNDLE_PATH" 2>/dev/null || stat -c%s "$BUNDLE_PATH")
    echo "==> AssetBundle present (not required this run): $BUNDLE_PATH"
  else
    echo "==> No placedcontent check (bundle + upload hint skipped)"
  fi
fi

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
echo "1. Content: build any prefab via AR Test → Build AssetBundle from selected prefab,"
echo "   then upload in avento-web (set unityAssetName if not PlacedContent)."
echo "2. In Xcode: Clean Build Folder → Run on iPhone."
echo "3. Open Unity Scene → tap a plane."
echo ""
echo "Default content bundle: $BUNDLE_PATH"
echo "Export:  $OUT"
echo "App:     $APP_XCODE"

if [[ "$NO_OPEN" -eq 0 ]]; then
  if [[ -d "$APP_XCODE" ]] && command -v open >/dev/null 2>&1; then
    open "$APP_XCODE"
  fi
fi
