# Avento AR Objects Guide (Unity Scene)

This file describes all core AR objects we create in `avento-ar`, aligned with `readme.avento.md` (`unity_scene` pipeline: build AssetBundle in Unity -> upload in avento-web -> run in avento-app).

## 1) How objects are delivered

- Objects are authored inside `PlacedContent` (or another selected prefab) in Unity.
- They are shipped as platform AssetBundles:
  - `AssetBundles/iOS/...`
  - `AssetBundles/Android/...`
- In avento-web VR item (`mode: unity_scene`) you upload both bundles.
- In avento-app, Unity loads the bundle and `TapToPlaceOnAnchor` places the prefab into AR.

---

## 2) Object types we created

## 2.1 Video Sprite (including "walking man")

**What it is**
- A transparent video billboard (quad + `VideoPlayer` + `PlayVideoOnPlace`).
- Used for character-like content such as the walking man, bouquet, etc.

**Key script / shader / material**
- `Assets/MobileARTemplateAssets/Scripts/PlayVideoOnPlace.cs`
- Shader `AR/VideoSpriteTransparent`
- Material `Assets/MobileARTemplateAssets/Materials/VideoPlane.mat` (use this — not an image/painting material)

**Main capabilities**
- Transparent background (alpha from video layout — see below).
- Auto fit by video aspect.
- Face camera billboard mode.
- Optional movement toward user camera (walking effect):
  - `m_MoveTowardCamera`
  - `m_ApproachSpeedMetersPerSecond`
  - `m_StopDistanceMeters`
  - optional walk bob.

### How transparency works on device (important)

Unity `VideoPlayer` on **iOS/Android often drops HEVC-with-alpha**. QuickTime can look perfect while the phone shows a **black box / black border** around the subject.

**Device path we use: side-by-side (SBS) H.264**

| Half of frame | Content |
|---|---|
| Left | RGB color |
| Right | Grayscale alpha matte (white = opaque, black = transparent) |

- `PlayVideoOnPlace.m_AlphaLayout` = **`SideBySide`**
- Shader samples left for color and right for alpha
- Regular **H.264** — no HEVC alpha track required
- Playback uses an **ARGB32 RenderTexture** into `_BaseMap`

**Authoring (DaVinci → Unity)**
1. In **DaVinci Resolve**, export the clip as **Apple ProRes 4444** (with alpha). That `.mov` is the **alpha source**.
   - Do **not** use Resolve “HEVC with Alpha” / multilayer HEVC as the Unity source — `ffmpeg` often extracts an **empty mask** (SBS right half all black).
2. Drop the ProRes `.mov` into the demo folder and assign it to `m_VideoClip` (or keep `*_sbs.mp4` beside it).
3. Use material **`VideoPlane`** / shader **`AR/VideoSpriteTransparent`**.
4. Build the **iOS or Android** AssetBundle. The builder **auto-converts** ProRes → `*_sbs.mp4` (H.264 side-by-side). ProRes itself is **not** shipped in the device bundle.

**Automatic SBS convert on bundle build**

- Use **`AR Test → Build AssetBundle from selected prefab (iOS/Android)`**  
  with the demo `PlacedContent.prefab` selected in Project (not `Build PlacedContent AssetBundle…` — that packs the default Resources prefab only).
- Or run **`AR Test → Convert selected prefab videos to side-by-side`** to create `*_sbs.mp4` without building a bundle.
- Implementation: `VideoSideBySideConverter` (also called from `PlacedContentBundleBuilder` on iOS/Android)
- Requires **`ffmpeg`** (`brew install ffmpeg`; Unity uses `/opt/homebrew/bin/ffmpeg` when PATH is empty)
- Writes `{sourceName}_sbs.mp4` next to the ProRes source
- Sets `m_VideoClip` → SBS and `m_AlphaLayout` → `SideBySide`
- Fails if the source is Resolve multilayer HEVC or the SBS matte is empty
- Console / dialog: `Side-by-side for device bundle: converted=…`

**Do not rely on**
- Resolve **HEVC Main 10 multilayer** (`lhvC`) as the SBS source — QT OK, but SBS convert loses the mask
- Shipping **ProRes** inside the AssetBundle — iOS cannot play it; only use ProRes as the **pre-SBS** source
- `AR/ImageSpriteTransparent` for video — that shader is for still PNG billboards

**Optional editor-only fallbacks** (Simulator / macOS)
- `m_EditorFallbackClip` — QT RLE / Animation alpha
- `m_EditorOpaqueFallbackClip` — opaque H.264 if no alpha fallback
- Device bundles strip these so only the SBS (or device) clip is packed

**How to use**
1. In DaVinci, export **ProRes 4444 (with alpha)** and put the `.mov` in the demo folder.
2. Create/select a quad; add `VideoPlayer`, `MeshRenderer`, `PlayVideoOnPlace`.
3. Assign `VideoPlane` material and the ProRes clip to `m_VideoClip`.
4. Keep `AspectFitMode = FitHeight` for full-body (or `FitWidth` for tall portraits).
5. Enable `Face camera` and (optionally) `Move toward camera`.
6. Build iOS/Android bundles (SBS convert runs automatically from ProRes) and upload to avento-web.

---

## 2.2 Image Sprite

**What it is**
- A static image billboard (supports PNG alpha) for posters, signs, paintings, etc.

**Key script**
- `Assets/MobileARTemplateAssets/Scripts/PlayImageOnPlace.cs`

**Main capabilities**
- Uses material texture first, then fallback Texture/Sprite fields.
- Aspect-fit scaling to avoid stretching.
- Adjustable opacity.
- Optional face-camera behavior.

**How to use**
1. Create/select quad in the content prefab.
2. Add `PlayImageOnPlace`.
3. Put image in material `_BaseMap` (recommended), or assign `m_Texture`/`m_Sprite`.
4. Keep `m_FitQuadToAspect = true`.
5. Tune `m_Opacity` and `m_FaceCamera`.
6. Rebuild and upload both platform bundles.

---

## 2.3 Skybox / 360 Panorama Object

**What it is**
- Immersive 360 environment rendered on an inverted dome.
- Supports still panorama image or 360 video.

**Key script**
- `Assets/MobileARTemplateAssets/Scripts/PanoramaSkyboxViewer.cs`

**Main capabilities**
- Content modes:
  - `StillImage` (equirectangular PNG)
  - `Video` (360 MP4)
- Look controls:
  - touch drag
  - device orientation
  - both
- Opacity blend with AR camera feed.
- Hides plane coaching UI when active.

**How to use**
1. Add `PanoramaSkyboxViewer` to a scene/prefab object.
2. Choose `ContentMode`:
   - Still: assign `m_PanoramaTexture`
   - Video: assign `m_VideoClip`
3. Set `m_DomeMaterialTemplate` (shader `AR/EquirectangularDome`).
4. Configure look mode and opacity.
5. Build and upload bundles.

---

## 2.4 Interactable Object (tap / proximity speech)

**What it is**
- Any object that can react when user taps it or comes close.
- Sends event to native app for TTS and/or Tessa conversation.

**Key scripts**
- `Assets/MobileARTemplateAssets/Scripts/AventoObjectInteract.cs`
- `Assets/MobileARTemplateAssets/Scripts/AventoInteractionDirector.cs`
- `Assets/MobileARTemplateAssets/Scripts/AventoInteractJson.cs`

**Main capabilities**
- Trigger mode: `Tap`, `Proximity`, `Both`.
- Per-object prompt text + optional localized prompts.
- Speech mode:
  - `tts`
  - `tessa`
  - `tts_then_tessa`
  - `caption` — show title + prompt as an on-screen text panel (no TTS / Tessa)
- Fire-once / cooldown / line-of-sight / facing checks.
- Single-speaker lock (prevents multiple objects from talking at once).

**How to use**
1. Add collider on the target object.
2. Add `AventoObjectInteract`.
3. Fill:
   - `objectId`
   - `displayName`
   - `prompt` (and `promptByLanguage` if needed)
   - `speechMode`
   - `triggerMode`
4. For walk-up behavior, set proximity radius/exit values.
5. Build/upload bundles.

**Caption (image / video sprite tap)**
Do **not** add a description field on `PlayImageOnPlace` / `PlayVideoOnPlace`. Put the museum label on `AventoObjectInteract`:
- `m_SpeechMode`: `Caption`
- `m_TriggerMode`: `Tap`
- `m_DisplayName`: painting / clip title
- `m_Prompt`: description body (`promptByLanguage` for `ru` / `uk` / …)
- `m_PauseVideoWhenCaptionShown`: `true` (pauses `PlayVideoOnPlace` while the panel is open)

Tap the sprite to open the panel, tap again (sprite, panel, or empty space) to close.

The panel includes **Ask Avento**: that sends `speechMode: tessa` to avento-app so Live Guide talks about this painting. Needs a device/UaaL session (the Editor only logs the event).

---

## 2.5 Scene auto-start interaction (scene-level Tessa)

**What it is**
- Optional scene greeting when content is placed/opened.

**Key script**
- `Assets/MobileARTemplateAssets/Scripts/AventoSceneTessa.cs`

**Main capabilities**
- Fires `scene_start` event once after placement.
- Works with offer flag `autoStartTessa`.
- Uses scene-level title/prompt fallback.

**How to use**
1. Add `AventoSceneTessa` on placed content root (or host-level object).
2. Set prompt/title if you need custom scene greeting.
3. Enable `autoStartTessa` in offer settings (app/web side).

---

## 2.6 Placement object (all content entry point)

**What it is**
- AR placement controller that instantiates your content prefab on planes.

**Key script**
- `Assets/MobileARTemplateAssets/Scripts/TapToPlaceOnAnchor.cs`

**Main capabilities**
- Plane raycast + anchor placement.
- Multi-place or replace-old behavior.
- Automatic placement mode (no tap).
- Interactable-first tap handling (tap object before plane placement).
- Refreshes child video/image sprites after instantiate.

**How to use**
1. Ensure scene has ARRaycast/Anchor/Plane managers.
2. Set content prefab (or allow bundle loader to provide it).
3. Configure:
   - `m_ReplaceExistingPlacement`
   - `m_ContentScale`
   - `m_HeadingDegrees`
   - automatic placement if desired.

---

## 3) "Walking man" recommended setup

Use this for a character that appears as a transparent video and can approach user:

1. In `PlacedContent`, create `walking_man` object (quad).
2. Material = `VideoPlane` (`AR/VideoSpriteTransparent`).
3. Add `PlayVideoOnPlace` with:
   - Alpha **source** clip (or `*_sbs.mp4` after first iOS/Android bundle build)
   - After device bundle build: `m_AlphaLayout = SideBySide` (set automatically)
   - `Face camera = true`
   - `AspectFitMode = FitHeight`
   - `MoveTowardCamera = true`
4. Add collider + `AventoObjectInteract` for tap/proximity dialog:
   - `triggerMode = Both`
   - `speechMode = tessa` (or `tts_then_tessa`)
5. Build iOS and Android bundles (SBS convert runs automatically).
6. Upload both in avento-web Unity Scene item.

---

## 4) Typical authoring workflow

1. Edit `PlacedContent` prefab and add needed objects/scripts.
2. Build AssetBundle for iOS and Android.
3. Upload both to offer (`unity_scene`) in avento-web.
4. Open offer in avento-app and test:
   - place content
   - video plays
   - image visible
   - skybox/panorama works
   - tap/proximity interaction works.

---

## 5) Ready samples for `PlacedContent` (copy presets)

Use these as quick presets in the Inspector.

## 5.1 Sample A - Video Sprite (static billboard)

**GameObject**
- Name: `video_sprite_01`
- Components: `MeshRenderer`, `VideoPlayer`, `PlayVideoOnPlace`

**PlayVideoOnPlace preset**
- `m_VideoClip`: alpha source clip (or `*_sbs.mp4`)
- `m_AlphaLayout`: `SideBySide` after device bundle build (auto)
- `m_TexturePropertyName`: `_BaseMap`
- Material: `VideoPlane`
- `m_FitQuadToAspect`: `true`
- `m_AspectFitMode`: `FitHeight`
- `m_Opacity`: `1`
- `m_FaceCamera`: `true`
- `m_MoveTowardCamera`: `false`
- `m_PlayAudio`: `false`

**Transform start**
- Position: `0, 0, 0`
- Rotation: `0, 0, 0`
- Scale: `1.2, 1.8, 1`

**Use when**
- You want video to stay in place and always face user.

## 5.2 Sample B - Video Sprite (walking man behavior)

**GameObject**
- Name: `walking_man`
- Components: `MeshRenderer`, `VideoPlayer`, `PlayVideoOnPlace`, `BoxCollider`, `AventoObjectInteract`

**PlayVideoOnPlace preset (walking)**
- `m_VideoClip`: alpha source or `*_sbs.mp4` (bundle build converts automatically)
- `m_AlphaLayout`: `SideBySide` on device packs
- Material: `VideoPlane`
- `m_TexturePropertyName`: `_BaseMap`
- `m_FitQuadToAspect`: `true`
- `m_AspectFitMode`: `FitHeight`
- `m_FaceCamera`: `true`
- `m_MoveTowardCamera`: `true`
- `m_ApproachSpeedMetersPerSecond`: `0.35`
- `m_StopDistanceMeters`: `1.2`
- `m_MoveOnlyWhilePlaying`: `true`
- `m_WalkBobAmplitudeMeters`: `0.02`
- `m_WalkBobFrequency`: `2.0`
- `m_PlayAudio`: `false`

**AventoObjectInteract preset (walking)**
- `m_ObjectId`: `walking_man`
- `m_DisplayName`: `Walking Man`
- `m_SpeechMode`: `Tessa` (or `TtsThenTessa`)
- `m_TriggerMode`: `Both`
- `m_ProximityRadiusMeters`: `2.2`
- `m_ProximityExitMeters`: `3.0`
- `m_FireOnce`: `true`
- `m_CooldownSeconds`: `30`
- `m_RequireLineOfSight`: `false`
- `m_RequireFacingUser`: `false`

**Transform start**
- Position: `0, 0, 0`
- Rotation: `0, 0, 0`
- Scale: `1.0, 1.8, 1`

**Use when**
- You want a character that approaches user and can start dialog by proximity/tap.

## 5.3 Sample C - Image Sprite (face toward camera)

**GameObject**
- Name: `image_sprite_01`
- Components: `MeshRenderer`, `PlayImageOnPlace`

**PlayImageOnPlace preset**
- Material `_BaseMap`: assign your PNG/JPG (preferred)
- `m_Texture` / `m_Sprite`: optional fallback
- `m_TexturePropertyName`: `_BaseMap`
- `m_FlipVertical`: `false`
- `m_FitQuadToAspect`: `true`
- `m_Opacity`: `1`
- `m_FaceCamera`: `true`  <- face toward user camera

**Transform start**
- Position: `0, 0, 0`
- Rotation: `0, 0, 0`
- Scale: `1.4, 1.0, 1`

**Use when**
- You need poster/sign style content that always turns toward user.

## 5.4 Optional - add interaction to any sprite

For video or image sprite, you can add `AventoObjectInteract` + collider:
- Tap **text description** (recommended for paintings / posters):
  - `m_TriggerMode`: `Tap`
  - `m_SpeechMode`: `Caption`
  - `m_DisplayName` + `m_Prompt`: title and description
  - `m_FireOnce`: `false`
- Tap spoken line (app TTS):
  - `m_TriggerMode`: `Tap`
  - `m_SpeechMode`: `Tts`
- Walk-up info panel:
  - `m_TriggerMode`: `Proximity`
  - `m_ProximityRadiusMeters`: `1.8`
  - `m_ProximityExitMeters`: `2.4`

---

## 6) Full sample from `OrigPlacedContent` copy

Use this as the standard "all objects in one prefab" setup.

## 6.1 Copy source prefab

1. In `Assets/Resources`, copy `OrigPlacedContent.prefab`.
2. Rename copy to `PlacedContent_AllFeatures.prefab`.
3. Open `PlacedContent_AllFeatures.prefab` and add the sample objects below.
4. Use this prefab for bundle builds (selected prefab build), or replace default `PlacedContent`.

## 6.2 Naming rule (feature in object name is required)

Use this pattern:
- `feature_<type>_<behavior>_<extra>`

Examples:
- `feature_video_static_facecam`
- `feature_video_walking_move_proximity_tessa`
- `feature_image_static_facecam_tap_tts`
- `feature_panorama_360_image_device_look`
- `feature_panorama_360_video_touchdrag`

## 6.3 Scene object set (all features)

Create these child objects under `PlacedContent_AllFeatures`:

1. `feature_video_static_facecam`
2. `feature_video_walking_move_proximity_tessa`
3. `feature_image_static_facecam_tap_tts`
4. `feature_panorama_360_image_device_look`
5. `feature_panorama_360_video_touchdrag`

This gives one prefab containing: video sprite, walking video sprite, image sprite, and both 360 modes.

## 6.4 Preset: `feature_video_static_facecam`

**Components**
- `MeshRenderer`
- `VideoPlayer`
- `PlayVideoOnPlace`

**Inspector preset**
- `m_VideoClip`: assign alpha source (or `*_sbs.mp4`)
- `m_AlphaLayout`: `SideBySide` after device bundle build
- Material: `VideoPlane`
- `m_FitQuadToAspect`: `true`
- `m_AspectFitMode`: `FitHeight`
- `m_FaceCamera`: `true`
- `m_MoveTowardCamera`: `false`
- `m_Opacity`: `1`
- `m_PlayAudio`: `false`

## 6.5 Preset: `feature_video_walking_move_proximity_tessa`

**Components**
- `MeshRenderer`
- `VideoPlayer`
- `PlayVideoOnPlace`
- `BoxCollider`
- `AventoObjectInteract`

**PlayVideoOnPlace preset**
- `m_VideoClip`: alpha source or `*_sbs.mp4`
- `m_AlphaLayout`: `SideBySide` after iOS/Android bundle build
- Material: `VideoPlane`
- `m_FitQuadToAspect`: `true`
- `m_AspectFitMode`: `FitHeight`
- `m_FaceCamera`: `true`
- `m_MoveTowardCamera`: `true`
- `m_ApproachSpeedMetersPerSecond`: `0.35`
- `m_StopDistanceMeters`: `1.2`
- `m_MoveOnlyWhilePlaying`: `true`
- `m_WalkBobAmplitudeMeters`: `0.02`
- `m_WalkBobFrequency`: `2.0`

**AventoObjectInteract preset**
- `m_ObjectId`: `feature_video_walking_move_proximity_tessa`
- `m_DisplayName`: `Walking Man`
- `m_SpeechMode`: `Tessa`
- `m_TriggerMode`: `Both`
- `m_ProximityRadiusMeters`: `2.2`
- `m_ProximityExitMeters`: `3.0`
- `m_FireOnce`: `true`

## 6.6 Preset: `feature_image_static_facecam_tap_tts`

**Components**
- `MeshRenderer`
- `PlayImageOnPlace`
- `BoxCollider`
- `AventoObjectInteract`

**PlayImageOnPlace preset**
- Material `_BaseMap`: assign PNG/JPG
- `m_FitQuadToAspect`: `true`
- `m_FaceCamera`: `true`
- `m_Opacity`: `1`

**AventoObjectInteract preset**
- `m_ObjectId`: `feature_image_static_facecam_tap_tts`
- `m_DisplayName`: `Info Panel`
- `m_SpeechMode`: `Caption` (on-screen description) or `Tts` (spoken)
- `m_TriggerMode`: `Tap`
- `m_FireOnce`: `false`
- `m_CooldownSeconds`: `8`
- `m_Prompt`: exhibit description text

## 6.7 Preset: `feature_panorama_360_image_device_look`

**Components**
- `PanoramaSkyboxViewer`

**PanoramaSkyboxViewer preset**
- `m_ContentMode`: `StillImage`
- `m_PanoramaTexture`: assign 360 equirectangular image
- `m_LookControlMode`: `DeviceOrientation`
- `m_Opacity`: `0.95`
- `m_HideArCameraBackground`: `true`
- `m_HideSurfaceCoachingWhenActive`: `true`
- `m_DomeRadius`: `50`

## 6.8 Preset: `feature_panorama_360_video_touchdrag`

**Components**
- `PanoramaSkyboxViewer`

**PanoramaSkyboxViewer preset**
- `m_ContentMode`: `Video`
- `m_VideoClip`: assign 360 MP4
- `m_AutoPlay`: `true` (or `false` to require tap)
- `m_LookControlMode`: `TouchDrag`
- `m_Opacity`: `0.95`
- `m_HideArCameraBackground`: `true`
- `m_HideSurfaceCoachingWhenActive`: `true`
- `m_DomeRadius`: `50`

## 6.9 Build and publish this sample prefab

1. Select `PlacedContent_AllFeatures.prefab` in Project.
2. Build bundle:
   - `AR Test -> Build AssetBundle from selected prefab (iOS)`
   - `AR Test -> Build AssetBundle from selected prefab (Android)`
3. In avento-web Unity Scene item:
   - upload iOS and Android files
   - set `unityAssetName = PlacedContent_AllFeatures` (if required by your bundle content)
4. Open in avento-app and verify every object by name and behavior.

## 6.10 Add it into Unity scene (`PlacedContent` in Hierarchy)

If you want to see/test this directly in the Unity scene:

1. Open your AR scene (for example `Assets/Scenes/SampleScene.unity` or your test scene).
2. In Project, drag `PlacedContent_AllFeatures.prefab` into Hierarchy.
3. Rename scene instance to:
   - `PlacedContent` (recommended for default flow), or
   - keep `PlacedContent_AllFeatures` and set `TapToPlaceOnAnchor.m_ContentPrefabName` to the same name.
4. Reset Transform of the root:
   - Position: `0, 0, 0`
   - Rotation: `0, 0, 0`
   - Scale: `1, 1, 1`
5. Ensure `TapToPlaceOnAnchor` exists in scene and references AR managers.
6. Press Play and verify:
   - plane detected
   - placement works
   - feature objects are present and named correctly.

Important:
- Scene instance is for editor/runtime preview.
- Final mobile content is still what you ship in AssetBundles from the prefab.
