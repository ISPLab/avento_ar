# Avento × Unity AR — Implementation Plan

**Goal:** add VR mode `unity_scene` so partners upload platform AssetBundles in **avento-web**, and **avento-app** (Capacitor) launches a native Unity player via **Unity as a Library (UaaL)** on iOS and Android.

**Decision:** Option A — embed Unity inside the existing Avento native shells (not a separate app, not WebView/`model-viewer`).

**Related repos**

| Repo | Role |
|------|------|
| `AR_TEST` (this project) | Unity content authoring, AssetBundle build, tap-to-place / PlacedContent |
| `avento-web` | Admin upload + `sections_data.vr` JSON |
| `avento-app` | Offer UI + Capacitor plugin → UaaL |

---

## 0. Why not “just upload a Prefab”

- A `.prefab` is **not** self-contained (materials, shaders, video are separate assets).
- Devices cannot load Editor prefabs.
- Cloud artifact = **AssetBundle per platform** (`placedcontent` for iOS + Android), built from Unity (menu already sketched: `AR Test → Build PlacedContent AssetBundle`).

Existing Avento pattern to mirror:

- `modelUrl` (GLB) + `iosModelUrl` (USDZ)
- `androidVideoUrl` + `iosVideoUrl`

→ `unityAndroidBundleUrl` + `unityIosBundleUrl`

---

## 1. Current Avento VR flow (baseline)

```
avento-web VrExperienceSection
  → POST /api/upload?purpose=vr&vrKind=…
  → storage refs in offers.sections_data.vr { version: 1, items: [...] }

avento-app OfferViewSheet
  → OfferVrDetailView (per item)
  → VrOfferModal / VrOfferViewer
  → mode switch: Model3D / ArExperience / Video / 360 / Image
  → native helpers: Scene Viewer, Quick Look, WebXR (no Unity today)
```

**Unity scene must branch before those viewers** and hand off to a Capacitor plugin.

---

## 2. Target architecture (Option A)

```
┌─────────────────────────────────────────────────────────┐
│ avento-app (Capacitor WebView / Next)                   │
│  OfferViewSheet → Launch unity_scene                    │
│       ↓                                                 │
│  Capacitor plugin: UnityArSession                       │
│       ↓ download/cache AssetBundle                      │
│       ↓ present Unity view (full-screen / overlay)      │
└───────────────────────┬─────────────────────────────────┘
                        │ UaaL embed
┌───────────────────────▼─────────────────────────────────┐
│ Unity library (from AR_TEST export)                     │
│  • AR Foundation (ARKit / ARCore)                       │
│  • PlacedContentBundleLoader                            │
│  • TapToPlaceOnAnchor (multi-place)                     │
│  • Exit → callback to Capacitor (close / result JSON)   │
└─────────────────────────────────────────────────────────┘

Cloud CDN / Avento storage
  • placedcontent (iOS bundle)
  • placedcontent (Android bundle)
  • preview image (optional, same as other VR modes)
```

**Important:** one binary per platform. Do not ship a single “universal” bundle.

---

## 3. Data model

Extend shared VR types in:

- `avento-web/src/lib/offerVr.ts`
- `avento-app/lib/offer-vr.ts`

```ts
export type VrOfferMode =
  | 'ar'
  | '360_image'
  | '360_video'
  | '3d_model'
  | 'video'
  | 'image'        // app already has
  | 'unity_scene'; // NEW

// on VrOfferSettings:
unityIosBundleUrl?: string;      // storage ref / HTTPS
unityAndroidBundleUrl?: string;
unityAssetName?: string;         // default "PlacedContent"
unityBundleFileName?: string;    // default "placedcontent"
// reuse: previewImageUrl, title, description, lat/lng, altitude,
// scale, modelHeight, activationRadius, requireUserAtLocation,
// iosEnabled, androidEnabled, instructionText, …
```

**Sanitize / content checks**

- `hasVrOfferContent`: true if either platform bundle URL present (when mode is `unity_scene`).
- `getVrModeMediaUrl` / platform helpers: resolve bundle URL by `ios` | `android`.
- No DB migration: still `sections_data.vr` JSON blob.

**Document version:** keep `OFFER_VR_VERSION = 1`; additive fields only.

---

## 4. Phase plan

### Phase 0 — Content pipeline (AR_TEST) ✅ partially done

| Task | Status / notes |
|------|----------------|
| Prefab `PlacedContent` + multi Instantiate placement | Done in AR_TEST |
| Editor: build AssetBundle (iOS / Android) | `PlacedContentBundleBuilder` |
| Runtime loader (file / StreamingAssets / HTTPS) | `PlacedContentBundleLoader` |
| Document build → upload path for partners | This readme |
| Stabilize bundle naming: `placedcontent` + asset `PlacedContent` | Lock as contract |
| CI optional: build bundles on Unity Cloud / local script | Later |

**Partner workflow**

1. Author scene in Unity (`PlacedContent`).
2. `AR Test → Build PlacedContent AssetBundle (iOS)` and `(Android)`.
3. Upload both files in avento-web admin.
4. Publish offer.

---

### Phase 1 — Admin (avento-web) — no Unity runtime yet

**Files (primary)**

- `src/lib/offerVr.ts` — mode + fields + sanitize
- `src/lib/vr-upload.ts` — `vrKind: 'unity_bundle'`, accept octet-stream / no ext, higher size limit
- `src/components/admin/sections/VrExperienceSection.tsx` — UI like Standard Video (two uploads)
- Upload API already: `/api/upload?purpose=vr&vrKind=…` — verify size limits for large bundles

**UI copy**

- Mode label: **Unity Scene**
- Hint: build AssetBundles in Unity; upload iOS + Android separately; do not upload `.prefab`
- Fields: Preview, iOS bundle, Android bundle, optional Asset name, map pin (reuse LocationMapSection)

**Acceptance**

- [ ] Create offer item `unity_scene`, upload two bundles + preview, save, reload → refs persist
- [ ] `sanitizeVrOfferDocument` round-trips new fields
- [ ] Public/app API returns `sections_data.vr` with bundles

---

### Phase 2 — App data + UI branch (avento-app) — stub launch OK

**Files (primary)**

- `lib/offer-vr.ts` — mirror types / sanitize / platform URL helper
- `lib/vr/capabilities.ts` — strategy `unity_native` for `unity_scene`
- `components/vr/VrOfferViewer.tsx` — branch before model/video viewers
- `components/EventSheet/OfferVrDetailView.tsx` — subtitle / CTA for Unity
- `components/EventSheet/OfferViewSheet.tsx` — no structural change if viewer handles mode

**Behaviour (Phase 2 stub)**

- Show preview + title like other VR items
- Launch button: if plugin missing → message “Unity AR requires the native Avento app build with Unity”
- On web browser: unsupported / preview only (`webFallbackEnabled`)

**Acceptance**

- [ ] Offer sheet lists `unity_scene` items
- [ ] Detail + Launch open viewer path without crashing
- [ ] Analytics: `vr_offer_viewer_open` with `mode: unity_scene`

---

### Phase 3 — Capacitor plugin `UnityArSession`

**Suggested API**

```ts
interface UnityArSessionPlugin {
  isAvailable(): Promise<{ available: boolean; reason?: string }>;

  /** Download (or use cache) then present Unity full-screen. */
  openScene(options: {
    bundleUrl: string;       // HTTPS public URL
    assetName?: string;      // default PlacedContent
    bundleFileName?: string; // cache key, default placedcontent
    title?: string;
    // optional pose / geo hints for later:
    latitude?: number;
    longitude?: number;
    altitude?: number;
    scale?: number;
  }): Promise<{ ok: boolean; error?: string }>;

  dismiss(): Promise<void>;
}

// Events: unityArSessionEnded, unityArError, unityArProgress
```

**Implementation sketch**

| Layer | Work |
|-------|------|
| `avento-app/plugins/unity-ar-session/` (or `@avento/unity-ar`) | TS definitions + web stub |
| iOS | Swift bridge → present `UnityFramework` view controller |
| Android | Kotlin → `UnityPlayer` Activity / Fragment overlay |
| Cache | `File` under app documents; ETag / version query later |

**JS usage from `VrOfferViewer`**

```ts
const url = getUnityBundleUrlForPlatform(vrSettings, platform);
await UnityArSession.openScene({
  bundleUrl: resolveApiUrl(url),
  assetName: vrSettings.unityAssetName ?? 'PlacedContent',
});
```

**Acceptance**

- [ ] Plugin registers on `cap sync`
- [ ] Web stub returns `available: false`
- [ ] Native stub can show a native “Unity placeholder” screen (even before full UaaL)

---

### Phase 4 — Unity as a Library embed

Export **AR_TEST** (or a slimmed `avento-unity-host` project) as UaaL and link into Capacitor projects.

#### 4.1 Unity project prep

- Keep AR Foundation + XR Simulation for Editor
- Strip Editor-only menus from player builds
- Single entry scene: load bundle → enable tap-to-place (existing scripts)
- Native callbacks:
  - `OnUnityReady`
  - `OnExitRequested` (toolbar / Android back)
  - optional `OnPlacementChanged(json)` for future save-to-cloud
- IL2CPP, ARM64; min iOS / Android aligned with avento-app
- **Do not** ship XR Simulation loader in device players (ARKit/ARCore only)

#### 4.2 iOS (UaaL)

1. Unity **Build Settings → iOS → Export** as library / Xcode project (Unity as a Library workflow).
2. Integrate `UnityFramework.framework` into `avento-app/ios/App`.
3. Data bundle / `Data` folder in app resources.
4. Plugin presents Unity VC over Capacitor; on exit, release/pause Unity and return to WKWebView.
5. Camera / ARKit usage descriptions already required for AR — verify `Info.plist`.
6. Bitcode / signing / size: expect large IPA increase; track separately.

#### 4.3 Android (UaaL)

1. Unity export as **Android Library** (AAR / Gradle module).
2. Include in `avento-app/android` via `settings.gradle` + dependency.
3. Launch `UnityPlayerActivity` or embed `UnityPlayer` in a Fragment hosted by MainActivity.
4. ARCore dependency / Play services checks (reuse patterns from existing geospatial AR helpers where possible).
5. ProGuard keep rules for Unity.

#### 4.4 Hand-off contract

Capacitor → Unity (on open):

```json
{
  "bundlePath": "/var/.../placedcontent",
  "assetName": "PlacedContent",
  "scale": 1.0
}
```

Unity → Capacitor (on close):

```json
{
  "reason": "user_exit" | "error",
  "placementsCount": 2
}
```

Wire through `UnitySendMessage` / native plugin callbacks.

**Acceptance**

- [x] Cold start path coded: Capacitor → cache bundle → UnitySendMessage → load → place → Exit (needs UnityFramework link to run on device)
- [x] Second launch uses cache (plugin cache dir unchanged)
- [ ] iOS device E2E with linked UnityFramework
- [ ] Android both work with their respective bundles

### Phase 4 status (M4)

Unity host + iOS plugin branch are in place. Full on-device AR still needs a Unity UaaL export linked into Xcode (`AVENTO_UNITY_EMBEDDED`).

---

### Phase 5 — Hardening & product

| Item | Notes |
|------|--------|
| Progress UI | Download % via plugin events |
| Offline | Cached bundle + requireUserAtLocation still applies |
| Versioning | `?v=` or content-hash in storage key; invalidate cache |
| Size budgets | Warn in admin if bundle > N MB |
| Analytics | download_start/ok/fail, unity_open, unity_exit, place_count |
| Fallback | Missing platform bundle → clear error (like Android GLB required today) |
| Security | Only HTTPS from Avento storage; no arbitrary file:// from web |
| QA matrix | iPhone ARKit devices; Android ARCore-supported devices |

---

## 5. File / ownership map

| Area | Owner repo | Key paths |
|------|------------|-----------|
| Bundle build | AR_TEST | `Assets/.../PlacedContentBundleBuilder.cs`, `AssetBundles/` |
| Bundle load in Unity | AR_TEST | `PlacedContentBundleLoader.cs`, `TapToPlaceOnAnchor.cs` |
| Types + admin UI | avento-web | `offerVr.ts`, `vr-upload.ts`, `VrExperienceSection.tsx` |
| Types + viewers | avento-app | `offer-vr.ts`, `VrOfferViewer.tsx`, `capabilities.ts` |
| Native bridge | avento-app | new Capacitor plugin + `ios/` + `android/` UaaL glue |
| This plan | AR_TEST | `readme.avento.md` |

---

## 6. Suggested milestone order (practical)

1. **M1 — Schema + admin uploads** (Phase 1) — unblocks content ops  
2. **M2 — App types + stub Launch** (Phase 2) — visible in offer sheet  
3. **M3 — Plugin + placeholder native screen** (Phase 3) — wiring proven  
4. **M4 — UaaL iOS vertical slice** (Phase 4.2) — one device path E2E  
5. **M5 — UaaL Android** (Phase 4.3)  
6. **M6 — Cache, progress, analytics, size limits** (Phase 5)

Do **not** wait for full UaaL before M1–M2; partners can start producing bundles early.

---

## 7. Risks & mitigations

| Risk | Mitigation |
|------|------------|
| App size jump (Unity + AR) | On-demand feature module later; strip unused Unity modules; compress bundles |
| UaaL + Capacitor lifecycle bugs | Single Unity instance; pause when backgrounded; explicit dismiss |
| Shader / URP mismatch in bundles | Build bundles with same Unity/URP version as player |
| ProRes / alpha video in bundles | Prefer H.264 + black-key or platform alpha formats already proven in AR_TEST |
| Two platforms to maintain | Automate bundle builds; admin forces both uploads before publish (optional validation) |

---

## 8. Out of scope (for now)

- Editing Unity scenes inside avento-web  
- Uploading raw `.prefab` / `.unity`  
- Running Unity inside the mobile browser  
- Cloud Anchors / multi-user shared world (can reuse placement JSON later)  
- Addressables catalog (AssetBundle pair is enough for v1)

---

## 9. Definition of done (v1)

Partner can:

1. Build iOS + Android AssetBundles from Unity (`PlacedContent`).
2. Upload both in **VR Experience → Unity Scene** on avento-web.
3. Open the offer in **avento-app** (native).
4. Tap Launch → Unity AR session loads the bundle → place content → exit to the offer.

Web admin preview: image only. Browser PWA: unsupported / preview (same as other heavy AR modes).

---

## 10. First test (do this now)

### One-shot iOS rebuild script

From `AR_TEST` (close Unity Editor first — batchmode needs the project unlocked):

```bash
./scripts/rebuild-ios-uaal.sh
```

What it does:
1. Builds `AssetBundles/iOS/placedcontent` (~20MB) and reveals it in Finder  
2. Prints **avento-web → Unity Scene → Upload iOS bundle** reminder  
3. Exports UaaL → `Builds/iOS_UaaL`  
4. `xcodebuild` **UnityFramework**  
5. Runs `avento-app/scripts/integrate-unity-ios.sh`  
6. Opens `avento-app` Xcode project  

Useful flags: `--skip-bundle` · `--skip-export` · `--skip-fw` · `--skip-integrate` · `--skip-upload-hint` · `--no-open`

Then in Xcode: **Clean Build Folder → Run on iPhone**.

Two passes: **A** proves admin → app → native download/cache (no UnityFramework yet). **B** is the real AR session after UaaL is linked.

### A — Placeholder smoke test (no Unity embed)

**1. Build the iOS AssetBundle (Unity / AR_TEST)**

1. Open this project in Unity (`6000.5.x`).
2. Menu **AR Test → Build PlacedContent AssetBundle (iOS)**.
3. Confirm output: `AssetBundles/iOS/placedcontent` (no extension).

**2. Upload in avento-web**

1. Edit an offer → **VR Experience** → add item → mode **Unity Scene**.
2. Upload the iOS file to **iOS AssetBundle** (Android can wait for this first test).
3. Leave asset name `PlacedContent` and bundle file name `placedcontent` unless you changed them.
4. Save / publish the offer so the native app can open it.

**3. Run avento-app on a physical iPhone**

1. From `avento-app`: sync/build as you usually do (`npx cap sync ios`, then Xcode → device).
2. You do **not** need `AVENTO_UNITY_EMBEDDED` or `UnityFramework` for this pass.
3. Open the offer → Unity Scene item → **Open Unity AR**.

**4. What you should see**

1. Fullscreen native screen titled like the VR item (placeholder UI).
2. Status goes from downloading → **Cached bundle ready** or **Bundle saved** (path under app caches).
3. Tap **Close** → back to the offer sheet.
4. Open again → should hit **cache** (no re-download if the same `placedcontent` file is already stored).

If the CTA is missing or greyed: confirm mode is `unity_scene`, iOS bundle URL is set, and you are on the **native** app (not browser PWA).

### B — Real Unity AR (after UaaL link) — **do this now**

Pass **A** is done. Unity Editor is already open on `AR_TEST` — use it for the export (do not start a second batchmode instance).

**1. Export Unity as a Library (in the open Unity Editor)**

1. Wait for scripts to finish compiling (`AventoUnityHost`, `PlacedContentBundleLoader`, UaaL menus).
2. Menu **AR Test → UaaL → Export iOS Library Project** (confirm dialog → exports to `Builds/iOS_UaaL`).
3. Wait for the iOS Xcode project export to finish (can take several minutes; watch the Unity Console).
4. If nothing appears: open **Window → General → Console** and look for `[Avento UaaL]` / errors.

**Alternate (Editor closed):**

```bash
/Users/andreyorlov/AR_TEST/scripts/export-uaal-ios.sh
```

**2. Build UnityFramework once**

```bash
open /Users/andreyorlov/AR_TEST/Builds/iOS_UaaL/Unity-iPhone.xcodeproj
```

1. Select the **UnityFramework** scheme (or build the Unity-iPhone project so the framework is produced).
2. Product → **Build** (generic iOS Device / your phone).
3. Confirm `UnityFramework.framework` exists under the export `build/` folder (or DerivedData).

**3. Integrate into avento-app** — **done**

```bash
cd /Users/andreyorlov/Projects/atlyx-project/avento-app
./scripts/integrate-unity-ios.sh /Users/andreyorlov/AR_TEST/Builds/iOS_UaaL
```

Xcode project is wired: `UnityUaaL/UnityFramework.framework` (Embed & Sign), `UnityUaaL/Data` in Resources, `AVENTO_UNITY_EMBEDDED=1`.

**Next:** open `ios/App/App.xcodeproj`, build & run on iPhone, open Unity Scene offer.

**4. Device check**

1. Open the same Unity Scene offer → **Open Unity Scene**
2. Expect: **AR camera** (not the purple placeholder) → tap a plane to place → **Exit AR** → back to offer
3. `isAvailable` / native path should report `unityEmbedded: true`

**Pass criteria**

| Pass | Success |
|------|---------|
| A | Download/cache + Close works; second open uses cache |
| B | Real AR place + Exit; no placeholder screen |

---

## 11. Implementation log

### 2026-08-08 — Tap places nothing after successful bundle load

UaaL often misses Input System touches / blocks on EventSystem; planes need a wider raycast mask.

**Fix in `TapToPlaceOnAnchor`:** EnhancedTouch + legacy fallbacks, ignore UI blocking, broader plane hits, on-screen HUD (`ready/planes/taps`), magenta debug cube marker on place.

Re-run `./scripts/rebuild-ios-uaal.sh --skip-bundle --skip-upload-hint` (scripts changed, not the AssetBundle).

---

### 2026-08-08 — Tap debug: catcher visible, taps=0

Device: `planes=3`, `fwd=v3`, `ready=True`, yellow `[Native] catcher ON` **never** became `tap #1` → UIKit never got `touchesBegan`. Subview catcher failed; non-key overlay window also failed. Fix: overlay `UIWindow` + **`makeKeyAndVisible`** + keep-front timer + Unity view `userInteractionEnabled=NO` (`catcher KEY`). Notes in `readme.notap.md`. **Rebuild avento-app only.**

---

### 2026-08-08 — `Failed to load AssetBundle` (path OK, load fails)

Common causes after path/`\/` fix:
1. Uploaded **`AssetBundles/iOS/iOS`** catalog (~1.6KB) instead of **`placedcontent`** (~20MB)
2. Uploaded **Android** bundle into the iOS field
3. Truncated/corrupt cache

**Fix:** reject files &lt; 64KB; LoadFromMemory fallback; clearer asset-list errors; delete bad cache on Unity error.

Upload exactly: `AR_TEST/AssetBundles/iOS/placedcontent` (no extension, ~20MB).

---

### 2026-08-07 — EXC_BAD_ACCESS in `MetadataDeserialization`

Cause: **Data/** from a new Unity export paired with an **old** `UnityFramework.framework` → IL2CPP metadata layout mismatch.

Always integrate **matching** pairs (same export build). `integrate-unity-ios.sh` now picks the newest FW from export + DerivedData and warns on age skew.

---

### 2026-08-07 — Bundle path `\/` escape (`File not found`)

iOS `NSJSONSerialization` emits `\/` in paths. Hand-rolled JSON slice kept the backslashes → Unity `File.Exists` failed on a real cached file.

**Fix:** `JsonUtility` + path unescape in `AventoUnityHost`; `NSJSONWritingWithoutEscapingSlashes` in `UnityArEmbeddedHost.mm`.

---

### 2026-08-07 — M4 placement debug (Unity opens, tap places nothing)

**Root causes addressed**
1. `OpenFromNative` often arrived before `AventoUnityHost` existed → silent drop (no prefab)
2. `TapToPlace` StreamingAssets probe set `m_Loading` and blocked absolute-path bundle load from Capacitor
3. `IsPointerOverGameObject(-1)` blocked all iOS taps via EventSystem
4. Scene YAML still had old `m_Content` / `m_ContentAnimator` fields

**Fixes**
- Host bootstrap `BeforeSceneLoad` + `NotifyReady({host,awaitingOpen})`; native retries `OpenFromNative`
- Bundle loader: absolute path cancels in-flight loads
- TapToPlace: UI raycast check, scene template fallback, Input System + Both handlers
- Re-export UaaL + `integrate-unity-ios.sh` required after Unity script changes

---

### 2026-08-07 — M3 Capacitor `UnityArSession` placeholder

**avento-app**
- `lib/vr/native/unityArSession.ts` — JS bridge (`isAvailable` / `openScene` / `dismiss`)
- iOS: `UnityArSessionPlugin.swift` — download/cache + fullscreen placeholder VC
- Android: `UnityArSessionPlugin.kt` + `UnityArPlaceholderActivity` — same flow
- Registered in `ViewController.swift` / `MainActivity.java` / `AndroidManifest.xml` / Xcode `project.pbxproj`
- `UnitySceneViewer` CTA → opens native placeholder and caches the AssetBundle

**Still stub:** `unityEmbedded: false` until Phase 4 UaaL embed.

**Next:** M4 — embed Unity as a Library (iOS vertical slice) and replace placeholder with real AR session.

### 2026-08-07 — M4 iOS UaaL vertical slice (wiring)

**AR_TEST (Unity host)**
- `AventoUnityHost.cs` — `OpenFromNative` / `DismissFromNative` via `UnitySendMessage`; loads absolute-path AssetBundle; wires `TapToPlaceOnAnchor`; Exit AR → native callback
- `AventoUnityNative.cs` — iOS `DllImport` + Android JNI stubs (`OnReady` / `OnSessionEnded` / `OnError`)
- `AventoUnityHostBootstrap.cs` — auto-creates host GO after scene load
- `PlacedContentBundleLoader` — `BeginLoadFromAbsolutePath`, `Configure`, `SetLoadOnAwake`
- Editor: **AR Test → UaaL →** Prepare settings / Export iOS Library / Checklist

**avento-app (iOS)**
- `AventoUnityNativeBridge.mm` — C exports Unity calls into
- `UnityArEmbeddedHost.mm/.h` — real UaaL path behind `AVENTO_UNITY_EMBEDDED`; stub returns `isUnityEmbedded = false` otherwise
- `UnityArSessionPlugin` — download/cache then launches embedded Unity when flag+framework present; else placeholder
- Bridging header + Xcode project entries for the new ObjC++ files

**Still required for device E2E (manual / CI):**
1. Unity menu **AR Test → UaaL → Export iOS Library Project…**
2. Link `UnityFramework.framework` + Data into `avento-app/ios`
3. Define preprocessor `AVENTO_UNITY_EMBEDDED=1`
4. Rebuild app — `isAvailable.unityEmbedded` becomes `true`



Installing
**Next:** M5 — Android UaaL (same host contract via `UnityArNativeBridge`).

Your next steps (in order)
1. In Unity (already open)
Menu: AR Test → UaaL → Export iOS Library Project…
Save to: /Users/andreyorlov/AR_TEST/Builds/iOS_UaaL

2. Build UnityFramework once

open /Users/andreyorlov/AR_TEST/Builds/iOS_UaaL/Unity-iPhone.xcodeproj
Build the UnityFramework target for a device.

3. Integrate into avento-app

cd /Users/andreyorlov/Projects/atlyx-project/avento-app
./scripts/integrate-unity-ios.sh /Users/andreyorlov/AR_TEST/Builds/iOS_UaaL
Then in Xcode: Embed & Sign UnityFramework, add Data, set AVENTO_UNITY_EMBEDDED=1, run on iPhone.

4. Verify
Open Unity Scene → real AR camera (not the purple placeholder) → place → Exit AR.

Tell me when the Unity export finishes (or if the menu is missing after compile) and we can do the Xcode embed together.