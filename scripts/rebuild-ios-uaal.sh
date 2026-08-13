#!/usr/bin/env bash
# Avento UaaL rebuild — iOS and/or Android into avento-app.
#
# Bare run (default):
#   build PlacedContent AssetBundles (iOS+Android)
#   rebuild iOS (export → UnityFramework → integrate)
#   AND Android (export Google project → integrate unityLibrary)
#   (upload hint skipped)
#
# Usage:
#   ./scripts/rebuild-ios-uaal.sh
#   ./scripts/rebuild-ios-uaal.sh --ios-only
#   ./scripts/rebuild-ios-uaal.sh --android-only
#   ./scripts/rebuild-ios-uaal.sh -i|--interactive
#   ./scripts/rebuild-ios-uaal.sh --full
#   ./scripts/rebuild-ios-uaal.sh --skip-bundle --skip-upload-hint
#
# Env:
#   UNITY PROJECT AVENTO_APP
#   OUT_IOS=$PROJECT/Builds/iOS_UaaL
#   OUT_ANDROID=$PROJECT/Builds/Android_UaaL
#   (OUT= still aliases OUT_IOS for compatibility)
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PROJECT="${PROJECT:-$ROOT}"
UNITY="${UNITY:-/Applications/Unity/Hub/Editor/6000.5.6f1/Unity.app/Contents/MacOS/Unity}"
# Prefer sibling avento-app next to this repo (survives folder moves); override with AVENTO_APP=.
if [[ -z "${AVENTO_APP:-}" ]]; then
  if [[ -d "$PROJECT/../avento-app" ]]; then
    AVENTO_APP="$(cd "$PROJECT/../avento-app" && pwd)"
  else
    AVENTO_APP="/Users/andreyorlov/Projects/atlyx-project/avento-app"
  fi
fi
OUT_IOS="${OUT_IOS:-${OUT:-$PROJECT/Builds/iOS_UaaL}}"
OUT_ANDROID="${OUT_ANDROID:-$PROJECT/Builds/Android_UaaL}"
LOG_DIR="${LOG_DIR:-$PROJECT/Builds}"
BUNDLE_IOS="$PROJECT/AssetBundles/iOS/placedcontent"
BUNDLE_ANDROID="$PROJECT/AssetBundles/Android/placedcontent"
RESOURCES_PREFAB="$PROJECT/Assets/Resources/PlacedContent.prefab"
ROOT_PREFAB="$PROJECT/Assets/PlacedContent.prefab"
MIN_BUNDLE_BYTES=$((64 * 1024))
UNITY_LOCKFILE="$PROJECT/Temp/UnityLockfile"

# Day-to-day defaults: build content packs + refresh players (both platforms).
SKIP_BUNDLE=0
SKIP_UPLOAD_HINT=1
SKIP_EXPORT=0
SKIP_FW=0
SKIP_INTEGRATE=0
NO_OPEN=0
DO_IOS=1
DO_ANDROID=1

die() { echo "ERROR: $*" >&2; exit 1; }

# Fail early if Editor/batchmode already owns this project (common after a move/reopen).
ensure_unity_project_free() {
  local unity_pids project_pids
  unity_pids="$(pgrep -f 'Unity.app/Contents/MacOS/Unity' 2>/dev/null || true)"
  project_pids="$(pgrep -f -- "-projectPath[= ]$PROJECT" 2>/dev/null || true)"

  if [[ -n "$project_pids" ]]; then
    die "Unity already running for this project (pids: $project_pids). Close the Editor / other batchmode, then re-run."
  fi

  if [[ -f "$UNITY_LOCKFILE" ]]; then
    if [[ -n "$unity_pids" ]]; then
      die "Unity lockfile present ($UNITY_LOCKFILE) while Editor is running. Close Unity on this project (or quit Editor), then re-run."
    fi
    echo "==> Removing stale Unity lockfile: $UNITY_LOCKFILE"
    rm -f "$UNITY_LOCKFILE"
  fi
}

flag_label() {
  if [[ "$1" -eq 0 ]]; then echo "ON "; else echo "off"; fi
}

platform_label() {
  if [[ "$1" -eq 1 ]]; then echo "ON "; else echo "off"; fi
}

print_plan() {
  echo ""
  echo "Plan (toggle with number, then Enter to start):"
  echo "  [1] Build AssetBundles (iOS+Android)  $(flag_label "$SKIP_BUNDLE")"
  echo "  [2] Upload hint (reveal in Finder)    $(flag_label "$SKIP_UPLOAD_HINT")"
  echo "  [3] Export UaaL projects              $(flag_label "$SKIP_EXPORT")"
  echo "  [4] xcodebuild UnityFramework (iOS)   $(flag_label "$SKIP_FW")"
  echo "  [5] Integrate into avento-app         $(flag_label "$SKIP_INTEGRATE")"
  echo "  [6] Open IDE at end                   $(flag_label "$NO_OPEN")"
  echo "  [7] iOS platform                      $(platform_label "$DO_IOS")"
  echo "  [8] Android platform                  $(platform_label "$DO_ANDROID")"
  echo ""
  echo "  [d] Defaults   (build bundle + UaaL; skip upload; both platforms)"
  echo "  [f] Full       (all steps ON)"
  echo "  [Enter] Start"
  echo "  [q] Quit"
}

apply_defaults() {
  SKIP_BUNDLE=0
  SKIP_UPLOAD_HINT=1
  SKIP_EXPORT=0
  SKIP_FW=0
  SKIP_INTEGRATE=0
  NO_OPEN=0
  DO_IOS=1
  DO_ANDROID=1
}

apply_full() {
  SKIP_BUNDLE=0
  SKIP_UPLOAD_HINT=0
  SKIP_EXPORT=0
  SKIP_FW=0
  SKIP_INTEGRATE=0
  NO_OPEN=0
  DO_IOS=1
  DO_ANDROID=1
}

flip01() {
  if [[ "$1" -eq 0 ]]; then echo 1; else echo 0; fi
}

prompt_interactive_flags() {
  apply_defaults
  echo "=============================================="
  echo " Avento UaaL rebuild — interactive"
  echo " Default: build bundles + UaaL; skip upload; iOS + Android"
  echo "=============================================="

  while true; do
    print_plan
    printf "> "
    if ! IFS= read -r choice; then
      break
    fi
    case "$choice" in
      ""|s|S|start|START) break ;;
      1) SKIP_BUNDLE=$(flip01 "$SKIP_BUNDLE") ;;
      2) SKIP_UPLOAD_HINT=$(flip01 "$SKIP_UPLOAD_HINT") ;;
      3) SKIP_EXPORT=$(flip01 "$SKIP_EXPORT") ;;
      4) SKIP_FW=$(flip01 "$SKIP_FW") ;;
      5) SKIP_INTEGRATE=$(flip01 "$SKIP_INTEGRATE") ;;
      6) NO_OPEN=$(flip01 "$NO_OPEN") ;;
      7) DO_IOS=$(flip01 "$DO_IOS") ;;
      8) DO_ANDROID=$(flip01 "$DO_ANDROID") ;;
      d|D) apply_defaults ;;
      f|F) apply_full ;;
      q|Q) echo "Aborted."; exit 0 ;;
      *) echo "Unknown: $choice" ;;
    esac
  done
}

WANT_INTERACTIVE=0
ARGS_GIVEN=0

for arg in "$@"; do
  case "$arg" in
    -i|--interactive) WANT_INTERACTIVE=1 ;;
    -h|--help)
      sed -n '2,28p' "$0"
      exit 0
      ;;
    --skip-bundle|--skip-export|--skip-upload-hint|--skip-framework|--skip-fw|--skip-integrate|--no-open|--full|--all|--defaults|--default|--ios-only|--android-only|--skip-ios|--skip-android)
      ARGS_GIVEN=1
      ;;
    *)
      echo "Unknown arg: $arg (try --help)"
      exit 1
      ;;
  esac
done

if [[ "$WANT_INTERACTIVE" -eq 1 ]]; then
  [[ -t 0 ]] || die "--interactive requires a TTY"
  prompt_interactive_flags
elif [[ "$ARGS_GIVEN" -eq 0 ]]; then
  apply_defaults
  echo "Using defaults: build PlacedContent bundles + UaaL (iOS + Android) → avento-app"
  echo "Tip: --skip-bundle / --ios-only / --android-only / -i / --full"
else
  SKIP_BUNDLE=0
  SKIP_UPLOAD_HINT=0
  SKIP_EXPORT=0
  SKIP_FW=0
  SKIP_INTEGRATE=0
  NO_OPEN=0
  DO_IOS=1
  DO_ANDROID=1
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
      --ios-only) DO_IOS=1; DO_ANDROID=0 ;;
      --android-only) DO_IOS=0; DO_ANDROID=1 ;;
      --skip-ios) DO_IOS=0 ;;
      --skip-android) DO_ANDROID=0 ;;
    esac
  done
fi

if [[ "$DO_IOS" -eq 0 && "$DO_ANDROID" -eq 0 ]]; then
  die "Nothing to do: both iOS and Android disabled"
fi

mkdir -p "$LOG_DIR" "$(dirname "$OUT_IOS")" "$(dirname "$OUT_ANDROID")"

need_unity() {
  [[ -x "$UNITY" ]] || die "Unity not found at $UNITY (set UNITY=...)"
  [[ -d "$PROJECT/Assets" ]] || die "PROJECT looks wrong (no Assets/): $PROJECT"
  [[ -d "$AVENTO_APP" ]] || die "AVENTO_APP not found: $AVENTO_APP (set AVENTO_APP=...)"
  ensure_unity_project_free
}

run_unity() {
  local target="$1"
  local method="$2"
  local log="$3"
  shift 3
  echo ""
  echo "==> Unity -buildTarget $target -executeMethod $method"
  echo "    project: $PROJECT"
  echo "    log: $log"
  ensure_unity_project_free
  set +e
  "$UNITY" \
    -batchmode \
    -nographics \
    -quit \
    -projectPath "$PROJECT" \
    -buildTarget "$target" \
    -logFile "$log" \
    -executeMethod "$method" \
    "$@"
  local rc=$?
  set -e
  if [[ $rc -ne 0 ]]; then
    echo "ERROR: Unity exited with code $rc — check $log" >&2
    if grep -qi 'another Unity instance is running' "$log" 2>/dev/null; then
      echo "HINT: Close Unity Editor (and Unity Hub project open on this folder), then re-run." >&2
    fi
    tail -n 40 "$log" || true
    exit "$rc"
  fi
  if grep -qi 'another Unity instance is running' "$log" 2>/dev/null; then
    die "Unity aborted: project already open in another instance — see $log"
  fi
}

echo "=============================================="
echo " Avento UaaL rebuild"
echo "=============================================="
echo "PROJECT:     $PROJECT"
echo "UNITY:       $UNITY"
echo "AVENTO_APP:  $AVENTO_APP"
echo "OUT_IOS:     $OUT_IOS"
echo "OUT_ANDROID: $OUT_ANDROID"
echo "Platforms:   ios=$DO_IOS android=$DO_ANDROID"
echo "Flags:       skip-bundle=$SKIP_BUNDLE skip-upload-hint=$SKIP_UPLOAD_HINT skip-export=$SKIP_EXPORT skip-fw=$SKIP_FW skip-integrate=$SKIP_INTEGRATE no-open=$NO_OPEN"
echo ""

# ---------------------------------------------------------------------------
# 1) AssetBundles
# ---------------------------------------------------------------------------
if [[ "$SKIP_BUNDLE" -eq 0 ]]; then
  need_unity
  if [[ -f "$RESOURCES_PREFAB" ]]; then
    echo "==> PlacedContent source: $RESOURCES_PREFAB (Resources — used by simulator + bundle)"
  elif [[ -f "$ROOT_PREFAB" ]]; then
    echo "==> PlacedContent source: $ROOT_PREFAB"
  else
    die "Missing PlacedContent prefab. Expected:\n  $RESOURCES_PREFAB\nor\n  $ROOT_PREFAB"
  fi
  if [[ "$DO_IOS" -eq 1 ]]; then
    run_unity iOS \
      "UnityEngine.XR.Templates.AR.Editor.PlacedContentBundleBuilder.BuildForIosBatch" \
      "$LOG_DIR/placedcontent-ios-bundle.log"
  fi
  if [[ "$DO_ANDROID" -eq 1 ]]; then
    run_unity Android \
      "UnityEngine.XR.Templates.AR.Editor.PlacedContentBundleBuilder.BuildForAndroidBatch" \
      "$LOG_DIR/placedcontent-android-bundle.log"
  fi
else
  echo "==> Skipping AssetBundle build (--skip-bundle)"
fi

NEED_BUNDLE_CHECK=0
if [[ "$SKIP_BUNDLE" -eq 0 || "$SKIP_UPLOAD_HINT" -eq 0 ]]; then
  NEED_BUNDLE_CHECK=1
fi

check_bundle() {
  local path="$1"
  local label="$2"
  [[ -f "$path" ]] || die "Missing $path — build the $label AssetBundle first"
  local size
  size=$(stat -f%z "$path" 2>/dev/null || stat -c%s "$path")
  if (( size < MIN_BUNDLE_BYTES )); then
    die "$label bundle too small ($size bytes). Expected real placedcontent, not the catalog file."
  fi
  echo "==> $label AssetBundle OK: $path ($(python3 -c "print(f'{$size/1024/1024:.1f} MB')"))"
}

if [[ "$NEED_BUNDLE_CHECK" -eq 1 ]]; then
  if [[ "$DO_IOS" -eq 1 ]]; then
    check_bundle "$BUNDLE_IOS" "iOS"
  fi
  if [[ "$DO_ANDROID" -eq 1 ]]; then
    check_bundle "$BUNDLE_ANDROID" "Android"
  fi
else
  [[ -f "$BUNDLE_IOS" ]] && echo "==> iOS AssetBundle present (not required): $BUNDLE_IOS"
  [[ -f "$BUNDLE_ANDROID" ]] && echo "==> Android AssetBundle present (not required): $BUNDLE_ANDROID"
fi

# ---------------------------------------------------------------------------
# 2) Upload hints
# ---------------------------------------------------------------------------
if [[ "$SKIP_UPLOAD_HINT" -eq 0 ]]; then
  echo ""
  echo "==> Upload in avento-web → Unity Scene:"
  if [[ "$DO_IOS" -eq 1 ]]; then
    echo "    iOS file:     $BUNDLE_IOS"
    command -v open >/dev/null 2>&1 && open -R "$BUNDLE_IOS" || true
  fi
  if [[ "$DO_ANDROID" -eq 1 ]]; then
    echo "    Android file: $BUNDLE_ANDROID"
    command -v open >/dev/null 2>&1 && open -R "$BUNDLE_ANDROID" || true
  fi
else
  echo "==> Skipping upload hint (--skip-upload-hint)"
fi

# ---------------------------------------------------------------------------
# 3) Export UaaL
# ---------------------------------------------------------------------------
if [[ "$SKIP_EXPORT" -eq 0 ]]; then
  need_unity
  if [[ "$DO_IOS" -eq 1 ]]; then
    if [[ -d "$OUT_IOS" ]]; then
      echo "==> Removing previous iOS export: $OUT_IOS"
      rm -rf "$OUT_IOS"
    fi
    run_unity iOS \
      "UnityEngine.XR.Templates.AR.Editor.AventoUaalIosExporter.ExportIosLibraryBatch" \
      "$LOG_DIR/uaal-ios-export.log" \
      "-aventoUaalOut=$OUT_IOS"
    [[ -d "$OUT_IOS/Data" ]] || die "iOS export missing Data/ — see $LOG_DIR/uaal-ios-export.log"
    [[ -f "$OUT_IOS/Data/boot.config" ]] || die "iOS export missing Data/boot.config"
    echo "==> iOS UaaL export OK: $OUT_IOS"
  fi
  if [[ "$DO_ANDROID" -eq 1 ]]; then
    if [[ -d "$OUT_ANDROID" ]]; then
      echo "==> Removing previous Android export: $OUT_ANDROID"
      rm -rf "$OUT_ANDROID"
    fi
    run_unity Android \
      "UnityEngine.XR.Templates.AR.Editor.AventoUaalAndroidExporter.ExportAndroidLibraryBatch" \
      "$LOG_DIR/uaal-android-export.log" \
      "-aventoUaalOut=$OUT_ANDROID"
    if [[ ! -f "$OUT_ANDROID/unityLibrary/build.gradle" ]]; then
      # Some Unity versions nest the project one level deeper
      found="$(find "$OUT_ANDROID" -type f -path '*/unityLibrary/build.gradle' 2>/dev/null | head -n 1 || true)"
      [[ -n "$found" ]] || die "Android export missing unityLibrary/ — see $LOG_DIR/uaal-android-export.log"
    fi
    echo "==> Android UaaL export OK: $OUT_ANDROID"
  fi
else
  echo "==> Skipping UaaL export (--skip-export)"
  if [[ "$DO_IOS" -eq 1 && ( "$SKIP_FW" -eq 0 || "$SKIP_INTEGRATE" -eq 0 ) ]]; then
    [[ -d "$OUT_IOS/Data" ]] || die "No iOS export at $OUT_IOS (run without --skip-export)"
  fi
  if [[ "$DO_ANDROID" -eq 1 && "$SKIP_INTEGRATE" -eq 0 ]]; then
    [[ -f "$OUT_ANDROID/unityLibrary/build.gradle" ]] || \
      find "$OUT_ANDROID" -type f -path '*/unityLibrary/build.gradle' 2>/dev/null | grep -q . || \
      die "No Android export at $OUT_ANDROID (run without --skip-export)"
  fi
fi

# ---------------------------------------------------------------------------
# 4) Build UnityFramework (iOS only)
# ---------------------------------------------------------------------------
if [[ "$DO_IOS" -eq 1 ]]; then
  if [[ "$SKIP_FW" -eq 0 ]]; then
    XCODEPROJ="$OUT_IOS/Unity-iPhone.xcodeproj"
    [[ -d "$XCODEPROJ" ]] || die "Missing $XCODEPROJ"

    FW_LOG="$LOG_DIR/unityframework-ios.log"
    DEST="${IOS_DESTINATION:-generic/platform=iOS}"
    echo ""
    echo "==> xcodebuild UnityFramework ($DEST)"
    echo "    log: $FW_LOG"

    set +e
    xcodebuild \
      -project "$XCODEPROJ" \
      -scheme UnityFramework \
      -configuration Release \
      -destination "$DEST" \
      -derivedDataPath "$OUT_IOS/DerivedData" \
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
        -derivedDataPath "$OUT_IOS/DerivedData" \
        build \
        CODE_SIGNING_ALLOWED=NO \
        CODE_SIGNING_REQUIRED=NO \
        CODE_SIGN_IDENTITY="" \
        >>"$FW_LOG" 2>&1
      XC_RC=$?
      set -e
    fi

    FW_BUILT="$(find "$OUT_IOS/DerivedData/Build/Products" -type d -name 'UnityFramework.framework' 2>/dev/null | head -n 1 || true)"
    if [[ -z "$FW_BUILT" ]]; then
      FW_BUILT="$(find "$HOME/Library/Developer/Xcode/DerivedData" -path "*Unity-iPhone*" -type d -name 'UnityFramework.framework' 2>/dev/null \
        | while read -r d; do echo "$(stat -f '%m' "$d/UnityFramework" 2>/dev/null || echo 0) $d"; done \
        | sort -rn | head -n 1 | awk '{ $1=""; sub(/^ /,""); print }' || true)"
    fi

    [[ -n "$FW_BUILT" && -d "$FW_BUILT" ]] || die "UnityFramework.framework not found after xcodebuild. See $FW_LOG"

    STAGE="$OUT_IOS/build/Release-iphoneos"
    mkdir -p "$STAGE"
    rm -rf "$STAGE/UnityFramework.framework"
    cp -R "$FW_BUILT" "$STAGE/UnityFramework.framework"
    echo "==> Staged framework: $STAGE/UnityFramework.framework"
    stat -f '%Sm %z %N' "$STAGE/UnityFramework.framework/UnityFramework"
  else
    echo "==> Skipping UnityFramework build (--skip-fw)"
  fi
fi

# ---------------------------------------------------------------------------
# 5) Integrate into avento-app
# ---------------------------------------------------------------------------
if [[ "$SKIP_INTEGRATE" -eq 0 ]]; then
  if [[ "$DO_IOS" -eq 1 ]]; then
    INTEGRATE_IOS="$AVENTO_APP/scripts/integrate-unity-ios.sh"
    [[ -x "$INTEGRATE_IOS" ]] || die "Missing $INTEGRATE_IOS"
    echo ""
    echo "==> Integrating iOS UaaL into avento-app"
    "$INTEGRATE_IOS" "$OUT_IOS"
  fi
  if [[ "$DO_ANDROID" -eq 1 ]]; then
    INTEGRATE_ANDROID="$AVENTO_APP/scripts/integrate-unity-android.sh"
    [[ -x "$INTEGRATE_ANDROID" ]] || die "Missing $INTEGRATE_ANDROID"
    echo ""
    echo "==> Integrating Android UaaL into avento-app"
    "$INTEGRATE_ANDROID" "$OUT_ANDROID"
  fi
else
  echo "==> Skipping integrate (--skip-integrate)"
fi

# ---------------------------------------------------------------------------
# 6) Open IDEs
# ---------------------------------------------------------------------------
APP_XCODE="$AVENTO_APP/ios/App/App.xcodeproj"
APP_ANDROID="$AVENTO_APP/android"
echo ""
echo "=============================================="
echo " Done"
echo "=============================================="
echo "Content: default batch packs Assets/Resources/PlacedContent.prefab → AssetBundles/<platform>/placedcontent"
echo "         (any local name is fine; after admin upload the app caches by MinIO GUID)."
echo "         Simulator uses Resources.Load; device/UaaL prefers AssetBundle then Resources)."
echo ""
[[ "$DO_IOS" -eq 1 ]] && echo "iOS export:     $OUT_IOS"
[[ "$DO_ANDROID" -eq 1 ]] && echo "Android export: $OUT_ANDROID"
echo "App iOS:        $APP_XCODE"
echo "App Android:    $APP_ANDROID"
echo ""
echo "Next: Xcode → device (iOS); Android Studio → ARCore device (Android)."
if [[ "$SKIP_BUNDLE" -eq 0 ]]; then
  echo ""
  echo "Upload built bundles in avento-web (Unity Scene) — MinIO stores GUID keys:"
  if [[ "$DO_IOS" -eq 1 ]]; then
    if [[ -f "$BUNDLE_IOS" ]]; then
      echo "  iOS:     $BUNDLE_IOS"
    else
      echo "  iOS:     $PROJECT/AssetBundles/iOS/<name>  (pick the large UnityFS file, not the tiny 'iOS' catalog)"
    fi
  fi
  if [[ "$DO_ANDROID" -eq 1 ]]; then
    if [[ -f "$BUNDLE_ANDROID" ]]; then
      echo "  Android: $BUNDLE_ANDROID"
    else
      echo "  Android: $PROJECT/AssetBundles/Android/<name>"
    fi
  fi
fi

if [[ "$NO_OPEN" -eq 0 ]]; then
  if [[ "$DO_IOS" -eq 1 && -d "$APP_XCODE" ]] && command -v open >/dev/null 2>&1; then
    open "$APP_XCODE"
  fi
  if [[ "$DO_ANDROID" -eq 1 && -d "$APP_ANDROID" ]] && command -v open >/dev/null 2>&1; then
    open -a "Android Studio" "$APP_ANDROID" 2>/dev/null || open "$APP_ANDROID" || true
  fi
fi
