# Content Delivery — AssetBundle Build & Upload

## Overview

AR content is packaged as **Unity AssetBundles** — self-contained archives containing a prefab and all its dependencies (meshes, textures, materials, shaders, video clips, etc.). Each bundle is built per-platform (iOS / Android), uploaded to MinIO via **avento-web**, and downloaded at runtime by **avento-app**.

## Naming Convention

Bundle filenames follow the pattern:

```
{content}.{platform}.bundle
```

Examples:
- `art-galary.ios.bundle`
- `art-galary.android.bundle`
- `portal.ios.bundle`
- `portal.android.bundle`

This naming is **synced** between:

| Component | File / Function | Role |
|-----------|----------------|------|
| **avento-ar** (Unity) | `PlacedContentBundleBuilder.OutputBundleFileName()` | Generates the local output file |
| **avento-web** | `generateUnityBundleFileName()` in `src/lib/vr-upload.ts` | Generates the MinIO object key (fallback) |
| **avento-web** | `sanitizeUnityBundleFileName()` in `src/app/api/upload/route.ts` | Preserves original filename if it already matches the pattern |

When uploading a bundle built by Unity, avento-web **does not rename** the file — it uses the original filename as the MinIO key directly.

## How the Content Name Is Derived

The content base name (e.g. `art-galary`) comes from the **parent folder** of the prefab when the prefab itself is named `PlacedContent.prefab`:

```
Assets/Scenes/demos/art-galary/PlacedContent.prefab  →  art-galary
Assets/Scenes/demos/portal/PlacedContent.prefab      →  portal
```

If the prefab has a unique name (not `PlacedContent`), that name is used instead.

The default `Assets/Resources/PlacedContent.prefab` always maps to `placedcontent`.

Logic: `PlacedContentBundleBuilder.UniqueBundleNameForPrefab()`.

## Building in Unity Editor

### Menu: Default PlacedContent

**AR Test > Build PlacedContent AssetBundle (iOS / Android / active platform)**

Builds `Assets/Resources/PlacedContent.prefab` as `placedcontent` (no platform suffix, legacy).

### Menu: Selected Prefab (recommended)

**AR Test > Build AssetBundle from selected prefab (iOS / Android / active platform)**

1. Select the demo prefab in the **Project** window (e.g. `Assets/Scenes/demos/art-galary/PlacedContent.prefab`) or its instance in the **Hierarchy**.
2. The builder derives the content name from the folder, appends the platform suffix and `.bundle` extension.
3. Output lands in `AssetBundles/`, e.g. `AssetBundles/art-galary.ios.bundle`.

If the selected Hierarchy instance has **unapplied prefab overrides**, a warning dialog appears — the bundle packs the on-disk prefab, not unsaved scene edits.

### Batch / CI

```bash
Unity -batchmode -executeMethod \
  UnityEngine.XR.Templates.AR.Editor.PlacedContentBundleBuilder.BuildForIosBatch

Unity -batchmode -executeMethod \
  UnityEngine.XR.Templates.AR.Editor.PlacedContentBundleBuilder.BuildForAndroidBatch
```

## Upload Flow (avento-web)

1. Admin opens VR Experience settings, selects **Unity Scene** mode.
2. Uploads the iOS bundle file → stored in MinIO as `art-galary.ios.bundle`.
3. Uploads the Android bundle file → stored in MinIO as `art-galary.android.bundle`.
4. The `unityIosBundleUrl` / `unityAndroidBundleUrl` fields in the offer settings are set to the MinIO key.
5. `unityAssetName` is auto-extracted from the filename (e.g. `art-galary`) via `unityPrefabNameFromFileName()`.

## Runtime Download (avento-app)

1. `UnitySceneViewer.tsx` reads `unityIosBundleUrl` or `unityAndroidBundleUrl` from the offer.
2. The native plugin (Swift / Kotlin) downloads the bundle from MinIO via `/api/download?file=...`.
3. The bundle is cached locally as `unity-{hash}.unity3d` (hash of the download URL + content revision).
4. Unity (`AventoUnityHost.cs`) loads the bundle from the local cache path and instantiates the prefab.

## Key Files

| File | Description |
|------|-------------|
| `avento-ar/.../Editor/PlacedContentBundleBuilder.cs` | Unity Editor build script |
| `avento-web/src/lib/vr-upload.ts` | Bundle naming, validation, MIME/size checks |
| `avento-web/src/app/api/upload/route.ts` | Upload API route (MinIO storage) |
| `avento-app/.../UnityArSessionPlugin.swift` | iOS native bundle download & cache |
| `avento-app/.../UnityArSessionPlugin.kt` | Android native bundle download & cache |
| `avento-ar/.../AventoUnityHost.cs` | Unity runtime — loads bundle, instantiates prefab |
