# Avento AR

Unity project for **Avento** travel AR scenes. Content is authored here, shipped as platform AssetBundles, and played inside **avento-app** as Unity as a Library (UaaL) on iOS (ARKit) and Android (ARCore).

## How it fits together

```
Unity (this repo)  →  AssetBundles  →  avento-web (upload)  →  avento-app (download + AR)
```

1. Author a `PlacedContent` prefab (video, images, 360, interactables).
2. Build iOS and Android bundles.
3. Upload them on a VR offer (`unity_scene`) in avento-web.
4. The app downloads the matching bundle, embeds Unity, and places the scene on a detected plane.

## What you can put in a scene

| Type | Role |
|------|------|
| **Video sprite** | Transparent video billboard; optional walk-toward-camera (e.g. walking character) |
| **Image sprite** | Posters, paintings, signs (PNG alpha) |
| **360 / skybox** | Still panorama or 360 video on an inverted dome |
| **Interactable** | Tap or walk-up → caption, Google TTS, and/or Tessa (Live Guide) in avento-app |
| **Placement** | Tap-to-place on AR planes, or automatic placement |

Unity does **not** speak audio itself. It sends an event; the mobile app plays TTS and opens Tessa.

## Content delivery

Bundles are named `{content}.{platform}.bundle`, for example:

- `art-galary.ios.bundle`
- `art-galary.android.bundle`

The content name usually comes from the demo folder (`Assets/Scenes/demos/art-galary/PlacedContent.prefab` → `art-galary`).

In Unity: **AR Test → Build AssetBundle from selected prefab (iOS / Android)**. Upload both files on the offer. The app caches the download and loads it at runtime.

## Player rebuild (UaaL)

After C# or native bridge changes, export the Unity library into avento-app:

```bash
./scripts/rebuild-ios-uaal.sh              # iOS + Android
./scripts/rebuild-ios-uaal.sh --ios-only
./scripts/rebuild-ios-uaal.sh --android-only
```

Prefab-only changes (prompts, colliders, media) need a **new AssetBundle**, not a full player rebuild.

## More detail

Internal notes in this repo:

- [readme.avento.ar.md](readme.avento.ar.md) — AR objects and authoring
- [readme.ar.interaction.md](readme.ar.interaction.md) — tap / proximity → speech & Tessa
- [readme.ar.android.md](readme.ar.android.md) — Android UaaL
- [reedme.content_delivery.md](reedme.content_delivery.md) — bundle naming, build, upload
