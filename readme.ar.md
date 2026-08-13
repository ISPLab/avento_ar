# Avento × Unity AR — Full Guide

End-to-end documentation for **Unity Scene** AR (`unity_scene`): author content in Unity, ship AssetBundles via **avento-web**, run **Unity as a Library (UaaL)** inside **avento-app** on iOS (ARKit) and Android (ARCore).

| Sibling doc | Focus |
|-------------|--------|
| [`readme.avento.md`](readme.avento.md) | Original implementation plan + decision log |
| [`readme.ar.android.md`](readme.ar.android.md) | Android UaaL checklist / build notes |
| [`readme.notap.md`](readme.notap.md) | iOS tap / UaaL input debugging |

---

## 1. Big picture

```
┌──────────────────┐     AssetBundle URL      ┌─────────────────────────────┐
│  Unity (avento-ar) │ ───────────────────────► │ avento-web (admin)          │
│  author prefab   │   upload per platform    │ offer.sections_data.vr      │
│  build bundles   │                          │ mode: unity_scene           │
│  export UaaL     │                          └──────────────┬──────────────┘
└────────┬─────────┘                                         │
         │ integrate player                                  │ publish offer
         ▼                                                   ▼
┌──────────────────────────────────────────────────────────────────────────┐
│ avento-app (Capacitor)                                                   │
│  Offer UI → UnitySceneViewer → UnityArSession plugin                     │
│    1. Download/cache platform AssetBundle                                │
│    2. Present UaaL (iOS UnityFramework / Android unityLibrary)           │
│    3. UnitySendMessage → AventoUnityHost.OpenFromNative(json)            │
│    4. Load prefab → AR planes → tap-to-place → Exit → native callback    │
└──────────────────────────────────────────────────────────────────────────┘
```

**Two artifacts, two jobs**

| Artifact | Changes when… | Rebuild with |
|----------|---------------|--------------|
| **Content** AssetBundle | New/changed prefab, materials, video | Unity menu → upload in avento-web |
| **Player** UaaL (Framework / unityLibrary) | C# host, AR scripts, native bridge | `./scripts/rebuild-ios-uaal.sh` |

Do **not** mix platforms: an iOS bundle will not load on Android (and vice versa).

---

## 2. Repos & roles

| Path | Role |
|------|------|
| `/Users/andreyorlov/Projects/atlyx-project/avento-web` | Unity 6000.5.x project: content, AssetBundles, UaaL export |
| `/Users/andreyorlov/Projects/atlyx-project/avento-web` | Admin: VR mode, uploads, `sections_data.vr` |
| `/Users/andreyorlov/Projects/atlyx-project/avento-app` | Capacitor app: plugin, download/cache, UaaL host |

**Unity version:** `6000.5.6f1` (override with `UNITY=…` for scripts).

---

## 3. Working in Unity (avento-ar)

### 3.1 Runtime pieces (shared iOS + Android)

| Script | Job |
|--------|-----|
| `AventoUnityHost` | UaaL entry: `OpenFromNative` / `DismissFromNative`; loads bundle; Exit |
| `AventoUnityHostBootstrap` | Creates host GO early (`BeforeSceneLoad`) |
| `PlacedContentBundleLoader` | Loads AssetBundle from absolute path / cache |
| `TapToPlaceOnAnchor` | AR Foundation planes + multi-place; accepts native tap inject |
| `AventoTapReceiver` | Receives `OnNativeTap` from iOS UIKit forwarder |
| `AventoUnityNative` | Callbacks → iOS `DllImport` / Android `UnityArNativeBridge` |

### 3.2 Default content contract

| Field | Default / notes |
|-------|-----------------|
| Authoring prefab | `Assets/Resources/PlacedContent.prefab` (or any selected prefab) |
| Local build output | `AssetBundles/iOS/<lowercased-prefab>` · `AssetBundles/Android/…` |
| MinIO / admin URL | GUID key (e.g. `dfecb028-….bundle`) — **runtime identity** |
| Device cache | `unity-<guid>.unity3d` (+ optional content revision) |
| Asset name | Optional prefab root; blank → load first GameObject in the bundle |

`unityBundleFileName` is deprecated and ignored for caching.

### 3.3 Author a scene / prefab

1. Put placeable content under a root prefab (default `PlacedContent`, or any name).
2. Include materials, shaders, video clips as dependencies (AssetBundle packs them).
3. Keep the player scene able to host AR Foundation + `AventoUnityHost` (Mobile AR template entry scene).

### 3.4 Build content AssetBundles

**Menus**

- **AR Test → Build PlacedContent AssetBundle (iOS / Android / active platform)**  
  Packs the default PlacedContent prefab → local file `placedcontent`.
- **AR Test → Build AssetBundle from selected prefab (iOS / Android / active)**  
  Select a `.prefab` in Project → packs it; **local file name = lowercased prefab name**.  
  Dialog shows suggested optional `unityAssetName` (prefab name). After upload, MinIO GUID is what the app uses.

**Batch (CI / script)**

```text
UnityEngine.XR.Templates.AR.Editor.PlacedContentBundleBuilder.BuildForIosBatch
UnityEngine.XR.Templates.AR.Editor.PlacedContentBundleBuilder.BuildForAndroidBatch
```

**Rules**

- Build **separately** for iOS and Android.
- Upload the big UnityFS file (~tens of MB), **not** the tiny `AssetBundles/iOS/iOS` catalog (~KB).
- Never upload an Android bundle into the iOS admin field (or reverse).
- Local filename does **not** matter after upload — storage is a MinIO GUID.
### 3.5 Export Unity as a Library (player)

**Menus**

| Platform | Menu |
|----------|------|
| iOS | **AR Test → UaaL → Prepare iOS Player Settings** then **Export iOS Library Project** |
| Android | **AR Test → UaaL → Prepare Android Player Settings** then **Export Android Library Project** |

**Outputs**

| Platform | Folder | What matters |
|----------|--------|----------------|
| iOS | `Builds/iOS_UaaL/` | Xcode project → build `UnityFramework` + `Data/` |
| Android | `Builds/Android_UaaL/` | Gradle project → `unityLibrary/` (+ `shared/`) |

Prefer the rebuild script (next section) over manual steps day-to-day.

---

## 4. Compile / rebuild scripts

### 4.1 Main script: `scripts/rebuild-ios-uaal.sh`

Despite the name, it rebuilds **iOS and/or Android** players into avento-app.

```bash
cd /Users/andreyorlov/Projects/atlyx-project/avento-ar
# Close Unity Editor first (batchmode needs the project unlocked).

./scripts/rebuild-ios-uaal.sh
```

**Default (bare run)** = `--skip-bundle --skip-upload-hint`:

1. **Skip** content AssetBundle build (content is uploaded separately).
2. **Skip** Finder upload hint.
3. **iOS:** export UaaL → `xcodebuild` UnityFramework → `integrate-unity-ios.sh`
4. **Android:** export Google project → `integrate-unity-android.sh`
5. Open Xcode / Android Studio (unless `--no-open`)

**Useful flags**

| Flag | Meaning |
|------|---------|
| `--ios-only` | Only iOS player |
| `--android-only` | Only Android player |
| `--skip-ios` / `--skip-android` | Disable one platform |
| `-i` / `--interactive` | Toggle steps in a menu |
| `--full` | Also build AssetBundles + upload hints |
| `--skip-bundle` | Don’t build default AssetBundles |
| `--skip-upload-hint` | Don’t reveal bundle in Finder |
| `--skip-export` | Reuse existing `Builds/*_UaaL` |
| `--skip-fw` | Skip iOS `xcodebuild` UnityFramework |
| `--skip-integrate` | Don’t copy into avento-app |
| `--no-open` | Don’t open IDEs |
| `--defaults` | Same as bare run |

**Env overrides**

```bash
UNITY=/Applications/Unity/Hub/Editor/6000.5.6f1/Unity.app/Contents/MacOS/Unity
PROJECT=/Users/andreyorlov/Projects/atlyx-project/avento-ar
AVENTO_APP=/Users/andreyorlov/Projects/atlyx-project/avento-app
OUT_IOS=$PROJECT/Builds/iOS_UaaL
OUT_ANDROID=$PROJECT/Builds/Android_UaaL
```

### 4.2 Integrate scripts (avento-app)

| Script | Input | Output |
|--------|-------|--------|
| `avento-app/scripts/integrate-unity-ios.sh` | `Builds/iOS_UaaL` | `ios/App/UnityUaaL/` (Framework + Data), `AVENTO_UNITY_EMBEDDED` |
| `avento-app/scripts/integrate-unity-android.sh` | `Builds/Android_UaaL` | `android/unityLibrary/` + `android/shared/`, `ENABLE_EMBEDDED` |

Android integrate also:

- Copies all `unity.*` keys into `android/gradle.properties`
- Replaces Unity’s flat `arcore_client.aar` with Maven `com.google.ar:core:1.54.0` (avoids clash with SceneView)

### 4.3 Typical workflows

**A — Changed only Unity C# / AR player (no new content)**

```bash
./scripts/rebuild-ios-uaal.sh          # both
# or
./scripts/rebuild-ios-uaal.sh --ios-only
./scripts/rebuild-ios-uaal.sh --android-only --no-open
```

**B — New content for an offer (no player rebuild)**

1. Unity → build iOS + Android AssetBundles (selected prefab or PlacedContent).
2. avento-web → upload both files on the offer.
3. Run app; it downloads the new URL (cache key includes URL hash).

**C — First-time / full pipeline**

```bash
./scripts/rebuild-ios-uaal.sh --full
```

Then upload revealed bundles in admin, and run on devices.

---

## 5. avento-web — admin & data model

### 5.1 Where it lives

| File | Role |
|------|------|
| `src/lib/offerVr.ts` | Mode `unity_scene`, fields, sanitize, platform URL helpers |
| `src/lib/vr-upload.ts` | `vrKind: 'unity_bundle'` (octet-stream / no extension, large size) |
| `src/components/admin/sections/VrExperienceSection.tsx` | UI: iOS + Android uploads, optional asset name |

Stored in offer JSON: `sections_data.vr` (no DB migration). Version stays `OFFER_VR_VERSION = 1` (additive fields).

### 5.2 VR item settings

```ts
mode: 'unity_scene'
unityIosBundleUrl?: string      // MinIO GUID / HTTPS
unityAndroidBundleUrl?: string
unityAssetName?: string         // optional prefab root; blank → first GameObject
// unityBundleFileName?: string // deprecated — ignored for device cache
// plus shared VR fields: title, previewImageUrl, lat/lng, scale, …
```

### 5.3 Admin steps

1. Edit offer → **VR Experience** → add item → mode **Unity Scene**.
2. Upload **iOS AssetBundle** = `AssetBundles/iOS/…` file from Unity (any local name → MinIO GUID).
3. Upload **Android AssetBundle** = `AssetBundles/Android/…` file.
4. Optional: set **Asset name** = prefab root if the bundle has multiple roots; leave blank to auto-load the first prefab.
5. Save / publish.

Validation: at least one of iOS/Android URL must be present. Platform-specific open uses **only** that platform’s URL (no cross-fallback).
### 5.4 Upload API

`POST /api/upload?purpose=vr&vrKind=unity_bundle` — accepts large binary / no extension.

---

## 6. avento-app — how Unity Scene runs

### 6.1 JS / React layer

| File | Role |
|------|------|
| `lib/offer-vr.ts` | Same types + `getUnityBundleUrlForPlatform()` |
| `lib/vr/capabilities.ts` | Strategy `unity_native` for `unity_scene` |
| `lib/vr/native/unityArSession.ts` | Capacitor plugin wrapper |
| `components/vr/viewers/UnitySceneViewer.tsx` | Offer CTA → `openUnityArFromVrSettings` |

Flow:

1. User opens offer VR item with `mode === 'unity_scene'`.
2. App resolves URL: iOS → `unityIosBundleUrl`, Android → `unityAndroidBundleUrl`.
3. Calls `UnityArSession.openScene({ bundleUrl, assetName?, scale, title, forceRedownload? })`.
4. Listens for `unityArProgress` / `unityArSessionEnded` / `unityArError`.

### 6.2 Capacitor plugin contract

```ts
UnityArSession.isAvailable()
  → { available, unityEmbedded?, reason? }

UnityArSession.openScene({
  bundleUrl, assetName?, title?, scale?, contentRevision?, forceRedownload?, …
})

UnityArSession.dismiss()
```

Events: `unityArProgress`, `unityArSessionEnded`, `unityArError` (+ Android may emit `unityArReady`).

### 6.3 Native open JSON → Unity

After download, native caches as `unity-<minio-guid>.unity3d` and sends (same on both platforms):

```json
{
  "bundlePath": "/…/Caches/unity-ar-bundles/unity-dfecb028-….unity3d",
  "assetName": "",
  "bundleFileName": "unity-dfecb028-….unity3d",
  "scale": 1.0,
  "title": "Offer title"
}
```

Empty `assetName` → Unity loads the first GameObject in the bundle.

Via `UnitySendMessage("AventoUnityHost", "OpenFromNative", json)`.

### 6.4 iOS host

| Piece | Path |
|-------|------|
| Plugin | `ios/App/App/UnityArSessionPlugin.swift` |
| Embedded host | `UnityArEmbeddedHost.mm` / `.h` |
| Native → JS bridge | `AventoUnityNativeBridge.*` |
| Vendor | `ios/App/UnityUaaL/` (`UnityFramework.framework` + `Data/`) |
| Flag | `AVENTO_UNITY_EMBEDDED` (xcconfig / preprocessor) |

Behavior:

1. Download/cache under Library Caches `unity-ar-bundles/`.
2. If embedded → present Unity VC; else placeholder VC.
3. Touch catcher overlay forwards taps → `AventoTapReceiver` / `InjectTap` (UaaL Input System often blind — see `readme.notap.md`).
4. Exit → `DismissFromNative` → session ended callback.

**After integrate:** Xcode → Clean → Run on **physical iPhone** (ARKit). Confirm `unityEmbedded === true`.

### 6.5 Android host

| Piece | Path |
|-------|------|
| Plugin | `android/.../UnityArSessionPlugin.kt` |
| Bridge | `UnityArNativeBridge.kt` (must stay `club.avento.app.UnityArNativeBridge`) |
| Host | `UnityArEmbeddedHost.kt` |
| Player Activity | `src/unityEmbedded/.../UnityArPlayerActivity.kt` extends `UnityPlayerGameActivity` |
| Placeholder | `UnityArPlaceholderActivity.kt` |
| Vendor | `android/unityLibrary/` + `android/shared/` |
| Flag | `BuildConfig.AVENTO_UNITY_EMBEDDED` when `unityLibrary/ENABLE_EMBEDDED` exists |

Behavior:

1. Cache: `cacheDir/unity-ar-bundles/<name>-<sha256prefix>.unity3d`; reject tiny downloads.
2. If embedded → `UnityArPlayerActivity`; else placeholder.
3. Retries `OpenFromNative` until host ready; Exit button / back → `DismissFromNative`.
4. When Unity linked: `minSdk` bumped to **26**; ARCore meta-data forced `optional` via `tools:replace`.

**After integrate:** Android Studio → Sync → Run on **ARCore** device. Confirm `unityEmbedded === true`.

Disable without deleting: `rm android/unityLibrary/ENABLE_EMBEDDED`.

---

## 7. End-to-end checklist

### Partner / content

- [ ] Prefab authored in Unity  
- [ ] iOS AssetBundle built and uploaded  
- [ ] Android AssetBundle built and uploaded  
- [ ] `unityAssetName` matches prefab if not `PlacedContent`  
- [ ] Offer published  

### Dev / player

- [ ] Unity Editor closed  
- [ ] `./scripts/rebuild-ios-uaal.sh` (or `--ios-only` / `--android-only`) succeeded  
- [ ] iOS: Framework + Data paired from **same** export  
- [ ] Android: `unityLibrary` + `shared` + `ENABLE_EMBEDDED`  
- [ ] Device build: `isAvailable().unityEmbedded === true`  

### Device QA

| Case | Expect |
|------|--------|
| Cold open | Download → AR → planes → place |
| Second open | Cache hit |
| Wrong platform file | Clear error, no crash |
| Tiny / HTML upload | Rejected |
| Exit / Back | Returns to offer WebView |
| Missing platform URL | Error before Unity |

---

## 8. File map (quick reference)

### avento-ar (Unity)

```
Assets/PlacedContent.prefab
Assets/MobileARTemplateAssets/Scripts/
  AventoUnityHost.cs
  AventoUnityHostBootstrap.cs
  PlacedContentBundleLoader.cs
  TapToPlaceOnAnchor.cs
  AventoTapReceiver.cs
  AventoUnityNative.cs
  Editor/PlacedContentBundleBuilder.cs
  Editor/AventoUaalIosExporter.cs
  Editor/AventoUaalAndroidExporter.cs
Assets/Plugins/iOS/AventoUnityNativeBridge.mm
scripts/rebuild-ios-uaal.sh
scripts/check-unity-ar-versions.sh
AssetBundles/iOS|Android/<bundle>
Builds/iOS_UaaL|Android_UaaL/
```

### avento-web

```
src/lib/offerVr.ts
src/lib/vr-upload.ts
src/components/admin/sections/VrExperienceSection.tsx
```

### avento-app

```
lib/offer-vr.ts
lib/vr/native/unityArSession.ts
lib/vr/capabilities.ts
components/vr/viewers/UnitySceneViewer.tsx
scripts/integrate-unity-ios.sh
scripts/integrate-unity-android.sh
ios/App/App/UnityArSessionPlugin.swift
ios/App/App/UnityArEmbeddedHost.mm
ios/App/UnityUaaL/
android/app/src/main/java/club/avento/app/UnityArSessionPlugin.kt
android/app/src/main/java/club/avento/app/UnityArEmbeddedHost.kt
android/app/src/main/java/club/avento/app/UnityArNativeBridge.kt
android/app/src/unityEmbedded/.../UnityArPlayerActivity.kt
android/unityLibrary/          # gitignored, from integrate
android/shared/                # gitignored, from integrate
```

---

## 9. Troubleshooting

### 9.0 Version / identity check

Device paths like `unity-dfecb028-….unity3d` (or legacy `placedcontent-<hash>.unity3d`) are the **admin download**, not your local `AssetBundles/iOS/<name>`. Cache identity is the **MinIO GUID** from the URL (+ optional content revision).

```bash
# Local bundle vs embedded UaaL in avento-app
./scripts/check-unity-ar-versions.sh --sha256

# Compare against the URL/file the phone actually loads
./scripts/check-unity-ar-versions.sh --sha256 --url 'https://…/dfecb028-….bundle'
./scripts/check-unity-ar-versions.sh --sha256 --bundle /path/to/downloaded.bundle
```

Unity menu: **AR Test → Check Unity AR versions (bundle vs UaaL)**.

| Symptom | Likely cause | Fix |
|---------|--------------|-----|
| Bundle load fails, path OK | Wrong file (catalog / other platform) | Upload real UnityFS content pack (~20MB+), matching OS |
| iOS Metal UnityFS rejected, engines look equal | Stale admin URL/cache, or bundle not re-uploaded after rebuild | Re-upload iOS pack (new GUID URL), Try again (force redownload), or reinstall; run version-check script |
| iOS crash in IL2CPP metadata | Data / Framework mismatch | Re-export + rebuild Framework + re-integrate **together** |
| iOS: planes OK, taps=0 | UaaL Input System blind | Ensure touch catcher window path (`readme.notap.md`); rebuild player |
| Android: placeholder only | No `ENABLE_EMBEDDED` / no unityLibrary | Run integrate / rebuild `--android-only` |
| Android: Duplicate `com.google.ar.core` | Unity aar + SceneView Maven | integrate script must replace `arcore_client` with Maven 1.54.0 |
| Android: `unity.androidSdkPath` missing | Incomplete gradle.properties merge | Re-run `integrate-unity-android.sh` |
| `OpenFromNative` no effect | Host not ready | Bootstrap + native retry (already in hosts); check logs |
| JSON path with `\/` | Escaped slashes | Hosts unescape; prefer `NSJSONWritingWithoutEscapingSlashes` on iOS |

---

## 10. Status (summary)

| Area | iOS | Android |
|------|-----|---------|
| Admin upload fields | ✅ | ✅ |
| App JS + plugin API | ✅ | ✅ |
| Download / cache | ✅ | ✅ |
| UaaL embed | ✅ | ✅ (assembleDebug verified) |
| Tap-to-place in UaaL | ✅ (with native forward) | ⬜ confirm on device |
| Multi-content prefabs | ✅ | ✅ |
| Rebuild script | ✅ | ✅ (same script) |

---

## 11. One-page cheat sheet

```bash
# Content (per offer)
# Unity: AR Test → Build AssetBundle … (iOS) + (Android)
# avento-web: Unity Scene → upload both → optional Asset name if multi-root

# Player (dev) — close Unity Editor first
cd /Users/andreyorlov/Projects/atlyx-project/avento-ar
./scripts/rebuild-ios-uaal.sh                 # iOS + Android into avento-app
./scripts/rebuild-ios-uaal.sh --ios-only
./scripts/rebuild-ios-uaal.sh --android-only

# Run
# iOS: Xcode → device
# Android: Android Studio → ARCore device
# Offer → Unity Scene → place → Exit
```
