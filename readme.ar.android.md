# Avento × Unity AR — Android (UaaL) plan

**Goal:** same E2E as iOS: offer `unity_scene` → download Android AssetBundle → embed Unity as a Library → ARCore planes → tap-to-place → Exit back to Capacitor.

**Sibling docs:** `readme.avento.md` (overall), `readme.notap.md` (iOS tap / UaaL lessons).

**Rebuild (both platforms):**

```bash
./scripts/rebuild-ios-uaal.sh
# default = --skip-bundle --skip-upload-hint → iOS + Android players into avento-app

./scripts/rebuild-ios-uaal.sh --android-only
./scripts/rebuild-ios-uaal.sh --ios-only
```

---

## 0. Status snapshot (2026-08-08)

| Layer | Status |
|-------|--------|
| avento-web: `unityAndroidBundleUrl` upload | ✅ Done |
| avento-app JS: platform URL → `UnityArSession.openScene` | ✅ Done |
| Android Capacitor download/cache (+ URL hash) | ✅ Done |
| Android placeholder UI | ✅ Fallback when not embedded |
| Unity Android AssetBundle build | ✅ Done |
| Unity host / loader / tap-to-place | ✅ Shared C# |
| `AventoUnityNative` → `UnityArNativeBridge` | ✅ Wired |
| `AventoUaalAndroidExporter` (menu + batch) | ✅ Done |
| `integrate-unity-android.sh` + Gradle `:unityLibrary` | ✅ Done |
| `UnityArEmbeddedHost` + `UnityArPlayerActivity` | ✅ Done (needs first export) |
| Rebuild script updates Android | ✅ `rebuild-ios-uaal.sh` |
| First device E2E (export + ARCore place) | ⬜ Device run still needed |
| Local verify: Unity export + integrate + `assembleDebug` | ✅ 2026-08-08 |

**Build check notes (2026-08-08):**
- `./scripts/rebuild-ios-uaal.sh --android-only --skip-bundle --skip-upload-hint --no-open` → export + integrate OK (~5 min).
- Integrate must copy `shared/` + all `unity.*` gradle props; replaces Unity `arcore_client.aar` with Maven `com.google.ar:core:1.54.0` (SceneView clash).
- Host uses `UnityPlayerGameActivity` (Unity 6), `minSdk` 26 when embedded, ARCore meta-data `tools:replace`.
- `:app:assembleDebug` **BUILD SUCCESSFUL** with `AVENTO_UNITY_EMBEDDED=true`.

---

## 1. Target architecture

```
avento-app MainActivity
  UnityArSessionPlugin
    → cache unity-ar-bundles/<name>-<hash>.unity3d
    → UnityArEmbeddedHost.present(…)  if BuildConfig.AVENTO_UNITY_EMBEDDED
    → else UnityArPlaceholderActivity

android/unityLibrary/   ← from Builds/Android_UaaL (integrate script)
  UnityArPlayerActivity : UnityPlayerActivity
    → UnitySendMessage(AventoUnityHost, OpenFromNative, json)
    → AventoUnityNative → UnityArNativeBridge.onUnity*
```

**Open JSON:** same as iOS (`bundlePath`, `assetName`, `bundleFileName`, `scale`, `title`).

---

## 2. First-time / day-to-day commands

### A — Content (partners / per offer)

1. Unity: select prefab → **AR Test → Build AssetBundle from selected prefab (Android)**  
   (or default PlacedContent menu).
2. avento-web → Unity Scene → **Android AssetBundle** upload.  
   Set `unityAssetName` if not `PlacedContent`.

### B — Player rebuild (dev)

Close Unity Editor, then:

```bash
cd /Users/andreyorlov/Projects/atlyx-project/avento-ar
./scripts/rebuild-ios-uaal.sh --android-only
# or both platforms:
./scripts/rebuild-ios-uaal.sh
```

What Android steps do:
1. Export Google Android project → `Builds/Android_UaaL`
2. `avento-app/scripts/integrate-unity-android.sh` → `android/unityLibrary` + `ENABLE_EMBEDDED`
3. Open Android Studio (unless `--no-open`)

Manual menus:
- **AR Test → UaaL → Export Android Library Project**
- `./scripts/integrate-unity-android.sh Builds/Android_UaaL`

### C — Run on device

1. Android Studio → Sync Gradle → Run on **ARCore** phone.  
2. Offer → Unity Scene → expect `unityEmbedded=true` (not placeholder).  
3. Planes → place → Exit.

Disable embed without deleting tree: `rm android/unityLibrary/ENABLE_EMBEDDED`.

---

## 3. Work packages (remaining polish)

| Item | Notes |
|------|--------|
| First Unity Android export | Long batchmode; verify `unityLibrary/build.gradle` + `assets/bin/Data` |
| AGP / NDK merge fixes | Patch unityLibrary if host AGP 8.13 complains |
| Tap overlay | Port iOS catcher if Input System blind on device |
| Cache magic check | Optional UnityFS header like iOS |
| QA matrix | Wrong-platform bundle, tiny file, no ARCore, multi-content name |

---

## 4. Key files

| Repo | Path |
|------|------|
| avento-ar | `Assets/.../Editor/AventoUaalAndroidExporter.cs` |
| avento-ar | `scripts/rebuild-ios-uaal.sh` (iOS **and** Android) |
| avento-app | `scripts/integrate-unity-android.sh` |
| avento-app | `android/settings.gradle` (conditional `:unityLibrary`) |
| avento-app | `android/app/build.gradle` (`AVENTO_UNITY_EMBEDDED`, `src/unityEmbedded`) |
| avento-app | `UnityArNativeBridge.kt`, `UnityArEmbeddedHost.kt`, `UnityArSessionPlugin.kt` |
| avento-app | `src/unityEmbedded/.../UnityArPlayerActivity.kt` |

---

## 5. Risks (from iOS)

1. Do not upload iOS bundles into the Android field.  
2. Always integrate a **fresh** export (Data + native libs together).  
3. UaaL input may need touch forwarding.  
4. Prefer fullscreen Activity (current) over Fragment for v1.  
5. JNI class must stay `club.avento.app.UnityArNativeBridge`.

---

## 6. Done when

- [x] Admin + plugin + bridge scaffolding  
- [x] Export / integrate / rebuild automation  
- [ ] ARCore device: download → place → exit  
- [ ] `isAvailable().unityEmbedded === true` on embedded builds  
