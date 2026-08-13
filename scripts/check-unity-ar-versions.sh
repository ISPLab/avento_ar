#!/usr/bin/env bash
# Compare Unity editor / local AssetBundles / embedded UaaL (and optional uploaded file).
#
# Usage:
#   ./scripts/check-unity-ar-versions.sh
#   ./scripts/check-unity-ar-versions.sh --bundle /path/to/dfecb028-….bundle
#   ./scripts/check-unity-ar-versions.sh --url 'https://…/….bundle'
#   ./scripts/check-unity-ar-versions.sh --sha256
#
# The phone never loads AssetBundles/iOS/<name> directly — it downloads the
# admin URL (MinIO GUID) and caches it as unity-<guid>.unity3d. Rebuilding locally
# without re-upload (new GUID URL) + app reinstall/cache miss will keep failing.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PROJECT="${PROJECT:-$ROOT}"
if [[ -z "${AVENTO_APP:-}" ]]; then
  if [[ -d "$PROJECT/../avento-app" ]]; then
    AVENTO_APP="$(cd "$PROJECT/../avento-app" && pwd)"
  else
    AVENTO_APP="/Users/andreyorlov/Projects/atlyx-project/avento-app"
  fi
fi

COMPARE_PATH=""
COMPARE_URL=""
WANT_SHA=0

while [[ $# -gt 0 ]]; do
  case "$1" in
    --bundle|-b)
      COMPARE_PATH="${2:-}"
      shift 2
      ;;
    --url|-u)
      COMPARE_URL="${2:-}"
      shift 2
      ;;
    --sha256)
      WANT_SHA=1
      shift
      ;;
    -h|--help)
      sed -n '2,14p' "$0" | sed 's/^# \{0,1\}//'
      exit 0
      ;;
    *)
      echo "Unknown arg: $1" >&2
      exit 2
      ;;
  esac
done

# Cache identity helper — MinIO GUID preferred (matches avento-app UnityArSessionPlugin)
minio_guid() {
  python3 -c '
import re, sys
m = re.search(r"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}", sys.argv[1])
print(m.group(0).lower() if m else "")
' "$1"
}

# djb2 64-bit — fallback when URL has no GUID
url_cache_digest() {
  python3 -c '
import sys
value = sys.argv[1]
h = 5381
for b in value.encode("utf-8"):
    h = ((h << 5) + h + b) & 0xFFFFFFFFFFFFFFFF
print(format(h, "x"))
' "$1"
}

inspect_unityfs() {
  local path="$1"
  local label="$2"
  python3 - "$path" "$label" "$WANT_SHA" <<'PY'
import hashlib, os, struct, sys
path, label, want_sha = sys.argv[1], sys.argv[2], sys.argv[3] == "1"
if not os.path.isfile(path):
    print(f"  {label}: MISSING  ({path})")
    sys.exit(0)
size = os.path.getsize(path)
mtime = os.path.getmtime(path)
from datetime import datetime
mtime_s = datetime.fromtimestamp(mtime).strftime("%Y-%m-%d %H:%M:%S")

magic = eng = fmt_str = "?"
fmt_i = -1
comp = "?"
metal = False
try:
    with open(path, "rb") as f:
        raw = f.read(64)
        if raw.startswith(b"UnityFS\x00"):
            magic = "UnityFS"
            fmt_i = struct.unpack_from(">I", raw, 8)[0]
            i = 12
            def cstr(buf, i):
                j = i
                while j < len(buf) and buf[j] != 0:
                    j += 1
                return buf[i:j].decode("ascii", "replace"), j + 1
            fmt_str, i = cstr(raw, i)
            eng, i = cstr(raw, i)
            if i + 20 <= len(raw):
                # fileSize(8) ci(4) ui(4) flags(4)
                flags = struct.unpack_from(">I", raw, i + 16)[0]
                comp = {0: "none", 1: "lzma", 2: "lz4", 3: "lz4hc"}.get(flags & 0x3F, str(flags & 0x3F))
        else:
            magic = raw[:7].decode("ascii", "replace") or "(empty)"
        # Metal marker in first 4MB → iOS-built content
        f.seek(0)
        window = f.read(min(size, 4 * 1024 * 1024))
        metal = b"metal_stdlib" in window
except Exception as ex:
    print(f"  {label}: read-error ({ex})")
    sys.exit(0)

sha = ""
if want_sha:
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1024 * 1024), b""):
            h.update(chunk)
    sha = h.hexdigest()[:16]

plat = "iOS(Metal)" if metal else "Android?/no-Metal"
print(f"  {label}:")
print(f"    path     {path}")
print(f"    size     {size:,} bytes ({size/1024/1024:.2f} MB)")
print(f"    mtime    {mtime_s}")
print(f"    magic    {magic}  format={fmt_i} ({fmt_str})  engine={eng}  compress={comp}")
print(f"    platform {plat}")
if sha:
    print(f"    sha256   {sha}…")
# machine-readable line for later compare
print(f"__META__\t{label}\t{eng}\t{size}\t{int(metal)}\t{sha}")
PY
}

echo "=============================================="
echo " Avento Unity AR — version check"
echo "=============================================="
echo "project:    $PROJECT"
echo "avento-app: $AVENTO_APP"
echo ""

# --- Editor ---
PV="$PROJECT/ProjectSettings/ProjectVersion.txt"
EDITOR_VER="?"
if [[ -f "$PV" ]]; then
  EDITOR_VER="$(sed -n 's/^m_EditorVersion: //p' "$PV" | head -1)"
fi
echo "Unity Editor (ProjectVersion.txt): $EDITOR_VER"
UNITY_BIN="${UNITY:-/Applications/Unity/Hub/Editor/${EDITOR_VER}/Unity.app/Contents/MacOS/Unity}"
if [[ -x "$UNITY_BIN" ]]; then
  echo "Unity binary: present ($UNITY_BIN)"
else
  echo "Unity binary: MISSING at $UNITY_BIN (set UNITY=…)"
fi
echo ""

# --- Local bundles ---
echo "Local AssetBundles (upload any of these; MinIO stores GUID):"
META_TMP="$(mktemp)"
trap 'rm -f "$META_TMP" "${META_TMP}.extra" 2>/dev/null' EXIT

# Prefer default placedcontent; also list other large UnityFS files in the folder.
inspect_unityfs "$PROJECT/AssetBundles/iOS/placedcontent" "local-iOS" | tee -a "$META_TMP"
inspect_unityfs "$PROJECT/AssetBundles/Android/placedcontent" "local-Android" | tee -a "$META_TMP"
for f in "$PROJECT"/AssetBundles/iOS/*; do
  [[ -f "$f" ]] || continue
  base="$(basename "$f")"
  [[ "$base" == "placedcontent" || "$base" == "iOS" || "$base" == *.manifest ]] && continue
  sz=$(wc -c < "$f" | tr -d ' ')
  [[ "$sz" -gt 65536 ]] || continue
  inspect_unityfs "$f" "local-iOS-$base" | tee -a "$META_TMP"
done
for f in "$PROJECT"/AssetBundles/Android/*; do
  [[ -f "$f" ]] || continue
  base="$(basename "$f")"
  [[ "$base" == "placedcontent" || "$base" == "Android" || "$base" == *.manifest ]] && continue
  sz=$(wc -c < "$f" | tr -d ' ')
  [[ "$sz" -gt 65536 ]] || continue
  inspect_unityfs "$f" "local-Android-$base" | tee -a "$META_TMP"
done
echo ""

# --- Embedded UaaL ---
echo "Embedded UaaL in avento-app (what the phone runs):"
UAAL_IOS="$AVENTO_APP/ios/App/UnityUaaL"
UAAL_AND="$AVENTO_APP/android/unityLibrary"
PLAYER_VER="?"
if [[ -d "$UAAL_IOS/Data" ]]; then
  # Prefer globalgamemanagers revision strings
  PLAYER_VER="$(
    rg -a -o '6000\.[0-9]+\.[0-9]+[a-z]?[0-9]*' "$UAAL_IOS/Data/globalgamemanagers" 2>/dev/null \
      | sort | uniq -c | sort -rn | head -1 | awk '{print $2}'
  )"
  [[ -z "$PLAYER_VER" ]] && PLAYER_VER="?"
  FW_MTIME="$(stat -f '%Sm' -t '%Y-%m-%d %H:%M:%S' "$UAAL_IOS/UnityFramework.framework/UnityFramework" 2>/dev/null || echo "?")"
  DATA_MTIME="$(stat -f '%Sm' -t '%Y-%m-%d %H:%M:%S' "$UAAL_IOS/Data/globalgamemanagers" 2>/dev/null || echo "?")"
  EMB="no"
  [[ -f "$UAAL_IOS/ENABLE_EMBEDDED" ]] && EMB="yes"
  echo "  iOS UnityUaaL:"
  echo "    player≈   $PLAYER_VER  (from Data/globalgamemanagers)"
  echo "    Framework $FW_MTIME"
  echo "    Data      $DATA_MTIME"
  echo "    embedded  $EMB"
  # Unique engine tags inside Data (ignore path prefixes from rg)
  MIXED="$(
    rg -a --no-filename -o '6000\.[0-9]+\.[0-9]+[a-z]?[0-9]*' "$UAAL_IOS/Data" 2>/dev/null \
      | sort -u | tr '\n' ' ' | sed 's/[[:space:]]*$//'
  )"
  echo "    Data tags ${MIXED:-?}"
else
  echo "  iOS UnityUaaL: MISSING ($UAAL_IOS) — run ./scripts/rebuild-ios-uaal.sh --ios-only"
fi
if [[ -d "$UAAL_AND" ]]; then
  AND_EMB="no"
  [[ -f "$UAAL_AND/ENABLE_EMBEDDED" ]] && AND_EMB="yes"
  echo "  Android unityLibrary: present (ENABLE_EMBEDDED=$AND_EMB)"
else
  echo "  Android unityLibrary: MISSING — run ./scripts/rebuild-ios-uaal.sh --android-only"
fi
echo ""

# --- Optional compare target (admin download) ---
if [[ -n "$COMPARE_URL" ]]; then
  GUID="$(minio_guid "$COMPARE_URL")"
  DIGEST="$(url_cache_digest "$COMPARE_URL")"
  echo "Admin URL cache key (avento-app):"
  echo "  url       $COMPARE_URL"
  if [[ -n "$GUID" ]]; then
    echo "  cache as  unity-${GUID}.unity3d"
  else
    echo "  cache as  unity-${DIGEST}.unity3d  (no GUID in URL — hash fallback)"
  fi
  echo ""
  TMP_DL="$(mktemp)"
  echo "==> Downloading for header inspect…"
  if curl -fsSL --max-time 120 -o "$TMP_DL" "$COMPARE_URL"; then
    COMPARE_PATH="$TMP_DL"
  else
    echo "  download FAILED"
    rm -f "$TMP_DL"
  fi
fi

if [[ -n "$COMPARE_PATH" ]]; then
  echo "Compared file (admin / device download):"
  inspect_unityfs "$COMPARE_PATH" "compared" | tee -a "${META_TMP}.extra"
  echo ""
fi

# --- Verdict ---
echo "=============================================="
echo " Verdict"
echo "=============================================="

LOCAL_ENG="$(awk -F'\t' '$1=="__META__" && $2=="local-iOS"{print $3; exit}' "$META_TMP" 2>/dev/null || true)"
LOCAL_SIZE="$(awk -F'\t' '$1=="__META__" && $2=="local-iOS"{print $4; exit}' "$META_TMP" 2>/dev/null || true)"
LOCAL_METAL="$(awk -F'\t' '$1=="__META__" && $2=="local-iOS"{print $5; exit}' "$META_TMP" 2>/dev/null || true)"

ok=1
if [[ -z "$LOCAL_ENG" || "$LOCAL_ENG" == "?" ]]; then
  echo "✗ No local iOS AssetBundle — build via AR Test menu or ./scripts/rebuild-ios-uaal.sh --full"
  ok=0
elif [[ "$PLAYER_VER" != "?" && "$LOCAL_ENG" != "$PLAYER_VER" ]]; then
  echo "✗ Engine mismatch: local bundle=$LOCAL_ENG  embedded UaaL≈$PLAYER_VER"
  echo "  Rebuild BOTH with the same Unity ($EDITOR_VER), re-integrate, re-upload."
  ok=0
elif [[ "$LOCAL_METAL" == "0" ]]; then
  echo "✗ Local iOS file has no Metal marker — looks like Android bundle in AssetBundles/iOS/"
  ok=0
else
  echo "✓ Local iOS bundle engine ($LOCAL_ENG) matches embedded UaaL (≈$PLAYER_VER) / editor ($EDITOR_VER)"
fi

if [[ -n "$COMPARE_PATH" && -f "${META_TMP}.extra" ]]; then
  CMP_ENG="$(awk -F'\t' '$1=="__META__" && $2=="compared"{print $3; exit}' "${META_TMP}.extra")"
  CMP_SIZE="$(awk -F'\t' '$1=="__META__" && $2=="compared"{print $4; exit}' "${META_TMP}.extra")"
  CMP_SHA="$(awk -F'\t' '$1=="__META__" && $2=="compared"{print $6; exit}' "${META_TMP}.extra")"
  LOCAL_SHA="$(awk -F'\t' '$1=="__META__" && $2=="local-iOS"{print $6; exit}' "$META_TMP")"
  if [[ "$CMP_SIZE" != "$LOCAL_SIZE" ]]; then
    echo "✗ Compared file SIZE ≠ local iOS ($CMP_SIZE vs $LOCAL_SIZE) — admin still has an older upload"
    ok=0
  elif [[ -n "$CMP_SHA" && -n "$LOCAL_SHA" && "$CMP_SHA" != "$LOCAL_SHA" ]]; then
    echo "✗ Compared file sha256 ≠ local — re-upload AssetBundles/iOS/<name> to avento-web"
    ok=0
  elif [[ "$CMP_ENG" != "$LOCAL_ENG" ]]; then
    echo "✗ Compared file engine ($CMP_ENG) ≠ local ($LOCAL_ENG)"
    ok=0
  else
    echo "✓ Compared file matches local iOS header/size${LOCAL_SHA:+ / sha}"
  fi
fi

echo ""
echo "Important pipeline facts:"
echo "  1. Device loads admin URL (MinIO GUID, e.g. dfecb028-….bundle), NOT local AssetBundles/iOS/<name>."
echo "  2. Cache name is unity-<guid>.unity3d (GUID from URL), not the Unity local filename."
echo "  3. After rebuild: upload NEW iOS file in avento-web → new GUID → cold download (or Try again)."
echo "  4. After UaaL rebuild: Xcode Clean + reinstall on device (Framework+Data must stay paired)."
echo "  5. Asset name is optional — blank loads the first prefab in the bundle."
echo ""
echo "Quick compare of the failing download:"
echo "  ./scripts/check-unity-ar-versions.sh --sha256 --bundle /path/to/dfecb028-….bundle"
echo "  ./scripts/check-unity-ar-versions.sh --sha256 --url 'https://…/dfecb028-….bundle'"
echo ""

if [[ "$ok" -eq 1 ]]; then
  exit 0
fi
exit 1
