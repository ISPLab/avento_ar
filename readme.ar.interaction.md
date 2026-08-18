# Avento × Unity AR — Object interaction → speech (plan)

Implementation plan: tap or walk-up (proximity) on authored AR objects / walking characters — or **auto-start Tessa when the Unity scene opens** — emit an event into **avento-app**, then speak the prompt in the **user’s app language** with **Google Cloud TTS** (`/api/tts` + `public/voices/voices.json`), and/or open **Tessa** (Live Guide) so the traveler can talk about the scene or object.

Sibling docs: [`readme.ar.md`](readme.ar.md) (UaaL pipeline), [`readme.notap.md`](readme.notap.md) (iOS tap forwarding).

**Status:** v1 implemented (player + app bridge). Rebuild UaaL after pulling C# / `.mm` changes (`./scripts/rebuild-ios-uaal.sh`). Author `AventoObjectInteract` on prefab colliders; enable **Start Tessa when AR scene opens** on the offer VR item.

---

## How to use (authoring cookbook)

Use this when you want a statue, sign, video billboard, or walking character in `PlacedContent` to speak (TTS) and/or start a live Tessa conversation.

### Prerequisites

| Need | Where |
|------|--------|
| Content prefab | `Assets/Resources/PlacedContent.prefab` (default AssetBundle root) |
| Script | `AventoObjectInteract` on the **same GameObject** (or parent) that has a **Collider** |
| Player bridge | Already in UaaL — rebuild only if you changed C# / native notify code |
| App | `UnityArTessaBridge` listens for `unityArObjectInteract` |

Unity **never** plays TTS or Gemini itself. It only fires the event; **avento-app** speaks / opens Live Guide.

### Step-by-step — add interaction to an object

1. Open **`Assets/Resources/PlacedContent.prefab`** in Unity (not only a scene instance — the AssetBundle is built from this prefab).
2. Select the child you want (e.g. video billboard, cube, NPC root). Give it a stable name (`walking_man`, `statue_01`).
3. Add a **Collider** if missing (`BoxCollider` is enough). Size it so the traveler can tap the visible mesh / billboard.
4. Add component **`AventoObjectInteract`** (Add Component → search).
5. Fill the Inspector (see §4.1). Minimum useful set:

| Field | Example | Why |
|-------|---------|-----|
| `objectId` | `walking_man` | Stable id in logs / future CMS |
| `displayName` | `Ivan Sokolov` | Title shown to the app / kickoff |
| `prompt` | Short TTS line **or** full character script | What the app speaks / sends to Tessa |
| `speechMode` | `tts` / `tessa` / `tts_then_tessa` | See §2 |
| `triggerMode` | `Tap`, `Proximity`, or `Both` | How the event fires |
| `proximityRadiusMeters` | `1.8`–`2.2` | Walk-up distance to AR camera |
| `fireOnce` | on for first meeting | Avoid spam while standing near |

6. Save the prefab (**Apply** overrides if you edited from a scene).
7. Rebuild the platform AssetBundle and ship it to the offer (see **Ship content** below).
8. On device: open the Unity Scene offer → place / auto-place → tap or walk toward the object → check Xcode / logcat for `[AventoObjectInteract]` and app `[UnityArInteract]`.

### Which `speechMode` to pick

| Want | Set `speechMode` | Put in `prompt` |
|------|------------------|-----------------|
| One authored spoken line, no conversation | `tts` | Short spoken line only (keep short — Google TTS) |
| Live character / guide (roleplay, Q&A) | `tessa` | Full character / system script (long OK) |
| Short spoken line, then live chat | `tts_then_tessa` | Short line for TTS; app then opens Tessa with a kickoff |

**Long character prompts:** if `prompt` is longer than ~80 characters, avento-app uses it as the Live Guide kickoff **as-is** (plus “Speak in {lang}. Stay fully in character.”). Do **not** use `tts` for multi-page role scripts — TTS would try to read the whole thing aloud.

### Example A — walking character → live Ivan (Tessa)

Reference already on `PlacedContent`: child **`walking_man`** (video billboard + `PlayVideoOnPlace`).

| Setting | Value |
|---------|--------|
| GameObject name | `walking_man` |
| Collider | `BoxCollider` on the billboard |
| `objectId` | `walking_man` |
| `displayName` | `Ivan Sokolov` |
| `speechMode` | **`tessa`** |
| `triggerMode` | **`Both`** (proximity when he walks up, or tap) |
| `proximityRadiusMeters` | `2.2` |
| `proximityExitMeters` | `3` |
| `fireOnce` | `true` |
| `ssmlGenderHint` | `Male` (optional; mainly for TTS modes) |
| `prompt` | Full Ivan Sokolov character script (personality, first meeting lines, historical rules, …) |

Expected on device:

1. Scene places → man walks toward the camera.
2. When distance ≤ ~2.2 m (or traveler taps him) → Unity emits `unityArObjectInteract` with `trigger: proximity` or `tap`, `speechMode: tessa`.
3. App starts Live Guide and sends the Ivan script as the first turn → he greets (“Good day! …”) and stays in 1817 for conversation.
4. Exit AR ends that Tessa session.

### Example B — static exhibit → short TTS, then Tessa

| Setting | Value |
|---------|--------|
| `objectId` | `statue_01` |
| `displayName` | `Bronze horse` |
| `speechMode` | `tts_then_tessa` |
| `triggerMode` | `Tap` |
| `prompt` | `This bronze horse was cast in 1891…` (one short paragraph) |

Optional: add `promptByLanguage` rows for `ru`, `uk`, etc., so TTS matches app language.

### Example C — scene greets as Tessa on open

No per-object component required for the greeting:

1. In **avento-web** offer VR item: enable **Start Tessa when AR scene opens**.
2. Optional notes in `autoStartTessaPrompt`.
3. Or author `AventoSceneTessa` on the prefab (§4.5).

Later tap / proximity on objects injects into the **same** Live session (`sendMessage`), it does not connect twice.

### Ship content (required after prefab edits)

Interaction lives **inside the AssetBundle**. Editing the prefab in the Editor is not enough for the phone.

```bash
# From avento-ar (or Unity menu):
# AR Test → Build PlacedContent AssetBundle (iOS)
# AR Test → Build PlacedContent AssetBundle (Android)
#
# Or batch via rebuild script (also rebuilds UaaL if needed):
./scripts/rebuild-ios-uaal.sh
```

Then upload the real bundle files (not the tiny catalog):

- `AssetBundles/iOS/placedcontent`
- `AssetBundles/Android/placedcontent`

…to the offer’s Unity Scene URLs in avento-web. Bump / refresh content so the app redownloads (cache key / `contentUpdatedAt`).

If you only changed **C# / native bridge** (not content), rebuild UaaL into avento-app. If you only changed **prompt / collider on the prefab**, rebuild **AssetBundles** only.

### Verify it works

| Check | Good sign |
|-------|-----------|
| Prefab Inspector | Collider + `AventoObjectInteract` on the same object |
| Unity Console (Editor play) | `[AventoObjectInteract] proximity walking_man` (or `tap`) |
| Device log | `onUnityObjectInteract` / `[UnityArInteract] event` with `objectId` |
| `speechMode=tessa` | Live Guide starts; character stays in role |
| `speechMode=tts` | Google TTS line; no Live Guide |
| No collider | Tap never fires — Physics raycast misses |
| Old bundle on device | Phone still has previous prefab — force redownload / new upload |

### Prompt tips for characters (`speechMode: tessa`)

- Write as **system / role instructions**: who they are, first meeting speech, topics, what they must not know.
- Include the **exact first spoken greeting** in the prompt (“say naturally: …”).
- Keep answers conversational in the instructions (no encyclopedia lectures).
- Historical / fictional NPCs: say they are fictional and must not invent documented “facts.”
- Prefer English in `prompt` as the default; add `promptByLanguage` when you need a localized TTS line. For Tessa, the model follows app language even if the script is English.

---

## 1. Goal

| Trigger in Unity | What the traveler hears |
|------------------|-------------------------|
| **Scene start** (optional) | Tessa connects as soon as the Unity scene is ready and greets / guides about this AR scene |
| **Tap** an interactable (statue, sign, hotspot) | Scripted line via Google TTS, then optional Tessa conversation about that object |
| **Proximity / hit** — walking character (or any NPC) comes within radius of the user (AR camera) | Same: character “starts speaking” the prompt; optional Tessa follow-up |

Speech **language** = app language (`localStorage app_language` / `getUserAppLanguage`), not device locale alone.

Speech **voice** = Google Cloud TTS catalog in [`avento-app/public/voices/voices.json`](../avento-app/public/voices/voices.json), resolved the same way as Settings → Pronunciation (`resolveGoogleVoiceName` in `lib/tts-translate-target.ts`).

---

## 2. Two speech modes (do not mix APIs)

| Mode | When | Engine | UI |
|------|------|--------|----|
| **A. Scripted TTS** (default for NPC walk-up) | Object/character delivers an authored line | Google Cloud TTS → `POST /api/tts` → MP3 | Stay in AR; optional native/Unity caption |
| **B. Tessa Live Guide** | Traveler talks *with* Tessa about the object | Gemini Live (`connectLive` + `sendMessage`) | `LiveGuideVoiceBar` / `AiSheetTessaTab` |

Gemini Live voices are **not** the Google TTS catalog (different product). Object/NPC lines **must** use Google TTS + `voices.json`. Tessa is a separate conversational session.

Per-object authoring field `speechMode`:

- `tts` — play prompt with Google TTS only (walk-up character)
- `tessa` — skip TTS; open Live Guide and send the prompt as the first model turn
- `tts_then_tessa` — TTS line, then start Tessa so the user can continue (tap on a landmark)

Default: **walk-up / proximity → `tts`**. **Tap on exhibit → `tts_then_tessa`**. **Scene start → `tessa`** (Live Guide, not Google TTS).

---

## 3. End-to-end flow

```
Unity scene ready  AND/OR  interactable (tap raycast  OR  proximity to XR camera)
        ↓
AventoSceneTessa / AventoObjectInteract  → JSON payload
        ↓
AventoUnityNative.NotifyObjectInteract(json)     ← new (mirror Ready/Ended/Error)
        ↓
iOS AventoUnity_OnObjectInteract  /  Android UnityArNativeBridge.onUnityObjectInteract
        ↓
Capacitor UnityArSession  →  event "unityArObjectInteract"
        ↓
avento-app UnityArTessaBridge
        ├─ language = getUserAppLanguage()
        ├─ voice    = resolveGoogleVoiceName(voices.json, googleBcp47, selectedVoice)
        ├─ Mode A: playAppTts({ text, language, provider: 'google', selectedVoice })
        └─ Mode B/C: setAiSheetMode('LIVE_GUIDE') → ensureVoiceLiveGuideReady()
                     → sendMessage(prompt)  → LiveGuideVoiceBar
```

Unity **does not** synthesize audio. It only fires the event. Playback and Tessa live in avento-app (WebView / native audio session). UaaL is fullscreen, so TTS must play **while Unity stays up** (background audio). Tessa UI may be a thin overlay or background voice (see §8).

---

## 4. Unity authoring — `AventoObjectInteract`

New MonoBehaviour on any collider in the AssetBundle prefab (statue, plaque, walking NPC). Ships with **content**; player rebuild needed once for the script + native notify.

### 4.1 Inspector

| Field | Type | Notes |
|-------|------|--------|
| `objectId` | string | Stable id (`statue_01`, `guide_npc`). App/CMS key. |
| `displayName` | string | Fallback title if CMS has none |
| `prompt` | string (multiline) | Fallback English (or default) line / Tessa kickoff |
| `promptByLanguage` | list `{ lang, text }` | Optional `en`, `ru`, `uk`, … — app prefers user language |
| `speechMode` | enum | `tts` / `tessa` / `tts_then_tessa` |
| `triggerMode` | enum | `Tap` / `Proximity` / `Both` |
| `proximityRadiusMeters` | float | Default `1.8` — “near the user” |
| `proximityExitMeters` | float | Hysteresis (e.g. radius + `0.6`) so it does not stutter |
| `fireOnce` | bool | Typical for exhibits |
| `cooldownSeconds` | float | For repeating NPCs (default `30`) |
| `requireLineOfSight` | bool | Optional ray camera → NPC (ignore walls) |
| `voiceNameOverride` | string | Optional Google catalog name (`ru-RU-Standard-C`, `Aoede`, …). Empty → app picks from settings + language |
| `ssmlGenderHint` | enum | `Unspecified` / `Female` / `Male` — used only if no `voiceName` |

Walking characters: same component on the NPC root. Movement (Animator / NavMesh / patrol) is content; interaction only cares about **distance to AR camera**.

### 4.2 Tap

Reuse the UaaL tap path (`readme.notap.md`): native catcher → `AventoTapReceiver.OnNativeTap` → `TapToPlaceOnAnchor.InjectTap`.

Change **order** in `InjectTap` / a new `AventoInteractionRaycaster`:

1. Ignore Exit chrome (`AventoUnityHost.IsInExitChromeImgui`).
2. Physics raycast from screen point (world camera).
3. If hit `AventoObjectInteract` with `Tap` or `Both` → `NotifyObjectInteract`, **do not place**.
4. Else existing plane place.

Collider required (mesh or box). UI / video screens that already consume taps should sit on a layer the interact raycast includes.

### 4.3 Proximity / “hit” (walking character)

Each frame (or 5–10 Hz):

```
d = distance(interactable.transform, Camera.main / XR Origin camera)
enter when d <= proximityRadiusMeters  (and triggerMode is Proximity or Both)
exit  when d >= proximityExitMeters
```

On **enter** (rising edge): fire the same `NotifyObjectInteract` as tap, with `"trigger": "proximity"`.

Guards:

- Cooldown / `fireOnce`
- Do not re-enter until **exit** (hysteresis)
- Skip if another interactable is already speaking (`AventoInteractionDirector` singleton)
- Optional: only when NPC is roughly facing the user (`dot(forward, toCamera) > 0.3`)

“Hit” here means **enter proximity collider / distance threshold**, not a combat hit. Implement as:

- `OnTriggerEnter` on a sphere trigger parented to the NPC (simple), **or**
- Distance check to the user camera (more reliable in AR; camera is the user)

Prefer **distance to camera** as source of truth; trigger collider is optional visualization.

### 4.4 Event JSON (Unity → native)

```json
{
  "type": "object_interact",
  "trigger": "tap",
  "objectId": "guide_npc",
  "title": "Courtyard guide",
  "prompt": "Welcome — this bronze horse was cast in 1891…",
  "promptByLanguage": { "en": "…", "ru": "…" },
  "speechMode": "tts",
  "voiceName": "",
  "ssmlGender": "FEMALE"
}
```

`trigger`: `tap` | `proximity` | `scene_start`. Empty `voiceName` → app resolves from catalog + user language.

---

## 4.5 Auto-speak with Tessa on Unity scene start

Optional. When enabled, opening a `unity_scene` starts a **Tessa Live Guide** session without waiting for a tap or NPC walk-up. The Launch / Open scene tap **is** a user gesture, so iOS audio unlock is easier than proximity.

### Where the flag lives (both)

| Layer | Field | Who sets it |
|-------|--------|-------------|
| **avento-web / offer VR item** | `autoStartTessa?: boolean` (plus optional `autoStartTessaPrompt`) | Partner / admin — no Unity rebuild |
| **`OpenFromNative` JSON** | `autoStartTessa`, `autoStartTessaPrompt` | App copies from VR settings (+ user language) |
| **Unity host / scene** | `AventoSceneTessa` on the placed prefab **or** host default | Content author: scene-specific welcome prompt |

App flag is source of truth for **on/off**. Unity may attach a richer scene prompt (`prompt` / `promptByLanguage`). If the offer flag is on and Unity never fires `scene_start`, the app still starts Tessa after `unityArReady` (fallback). If Unity fires `scene_start` first, the app uses that payload and does **not** double-start.

### Timing (do not greet on a black/loading screen)

**Scene Tessa session:** Opening a Unity scene with auto-start uses **only** `autoStartTessaPrompt` as the Live Guide voice kickoff **and** injects that script into the Gemini system instruction (so Tessa does not fall back to the default companion greeting). Closing / dismissing the Unity scene **always ends** that Tessa session (`unityArSessionEnded` + JS fallback) so it does not keep talking as a normal companion afterward.

**iOS + Android:** Before Unity opens, `ensureNativeTessaReadyForUnityAr` arms native audio — Live Activities on iOS, Live Guide FGS/notification + audio focus on Android — so Tessa can keep speaking under fullscreen UaaL. Mic permission must already be granted (onboarding).

Legacy / optional Unity-side kickoff (still supported as backup if prestart failed):

Start Tessa only after **all** of:

1. Native Unity VC/Activity is presented  
2. `AventoUnityNative.NotifyReady` (or `unityArReady`)  
3. Content placed (tap-to-place **or** `automaticScenePlacement`) — host sets `sceneReadyForTessa`  
4. Short delay (e.g. **400–800 ms**) so tracking/planes settle  

Then either Unity sends:

```json
{
  "type": "object_interact",
  "trigger": "scene_start",
  "objectId": "scene",
  "title": "Offer / scene title",
  "prompt": "Welcome to this AR courtyard…",
  "promptByLanguage": { "en": "…", "ru": "…" },
  "speechMode": "tessa"
}
```

…or the app, if `autoStartTessa === true` and no `scene_start` arrived within ~2 s after ready+placed, starts Tessa itself with offer title + `autoStartTessaPrompt`.

`speechMode` for scene start is **`tessa`** (Gemini Live). Do not play Google TTS and Tessa at the same time on start.

### Kickoff prompt (app-built)

```
The traveler just opened an AR Unity scene: "{sceneTitle}".
Speak in {userLanguage}. Greet them as Tessa, briefly say what they can look at or tap,
and invite them to ask questions. Stay in a live voice conversation.
Scene notes: {autoStartTessaPrompt or Unity prompt}
```

### Interaction with object tap / proximity

If Tessa is **already live** from scene start:

- Object **TTS** (`speechMode: tts`) — pause Tessa mic or let Tessa wait; play the NPC/object line; then resume (or `sendMessage` a one-line context so Tessa knows what played)
- Object **Tessa** (`tessa` / `tts_then_tessa`) — do **not** `connectLive` again; `sendMessage` the object kickoff into the existing session
- Exit AR → `closeLiveSession` (same as leaving Live Guide)

One Live Guide session per Unity visit. Scene start owns connect; later interacts only send turns.

### Offer / `openScene` fields

```ts
// VrOfferSettings + UnityArSessionOpenOptions
autoStartTessa?: boolean;
autoStartTessaPrompt?: string;
```

Admin UI (VR Experience section): checkbox **“Start Tessa when AR scene opens”** + optional welcome notes. Default **off** so existing offers stay silent until enabled.

---

## 5. Native / Capacitor bridge (extend existing)

Mirror `NotifyReady` / `NotifySessionEnded` / `NotifyError`. **Do not** invent a second plugin.

| Layer | Change |
|-------|--------|
| `AventoUnityNative.cs` | `NotifyObjectInteract(string json)` → iOS `AventoUnity_OnObjectInteract` / Android `onUnityObjectInteract` |
| `Assets/Plugins/iOS/AventoUnityNativeBridge.mm` | `AventoUnity_OnObjectInteract` → `NSNotification` `AventoUnityOnObjectInteract` |
| `avento-app/ios/.../AventoUnityNativeBridge.h/.mm` | `setObjectInteractHandler:` + observer |
| `UnityArEmbeddedHost.mm` | Forward handler → plugin `notifyListeners("unityArObjectInteract", …)` |
| `UnityArNativeBridge.kt` | `onUnityObjectInteract` → `dispatchUnityEvent("unityArObjectInteract", json)` |
| `lib/vr/native/unityArSession.ts` | `addListener('unityArObjectInteract', …)` |

After C# / `.mm` change: rebuild UaaL (`./scripts/rebuild-ios-uaal.sh`) so UnityFramework contains the new export.

Pass **user language**, optional `selectedVoice`, and **`autoStartTessa` / `autoStartTessaPrompt`** into `OpenFromNative` JSON so the host can fire `scene_start` at the right time. **TTS still runs in the app** (Unity has no Google TTS key). Tessa still runs in the app (Gemini Live).

---

## 6. avento-app — language + Google voice

Reuse Pronunciation / analysis TTS. Do not add a second catalog.

### 6.1 Language

```ts
const language = getUserAppLanguage(i18n.language || 'en'); // e.g. "ru"
const googleLang = translateTargetToBcp47ForGoogle(language); // "ru-RU"
```

`lib/app-language.ts` + `SHORT_TO_GOOGLE` in `lib/tts-translate-target.ts` (`en-US`, `ru-RU`, `uk-UA`, `fr-FR`, `cmn-CN`, …).

Prompt text:

1. `promptByLanguage[language]` if present  
2. else `promptByLanguage.en`  
3. else `prompt`  
4. else CMS/offer copy for `objectId`

If the Unity string is English and the user language is not `en`, **Tessa mode** can still speak the right language (model follows Live Guide `language`). **TTS mode** should prefer a localized `promptByLanguage` (or a later CMS string). Do not send English text into Google TTS with `ru-RU` — it will pronounce English with a Russian voice.

### 6.2 Voice (`voices.json`)

Catalog: `GET /voices/voices.json` via `loadGoogleVoicesCatalog()`.

Resolve:

```ts
const catalog = await loadGoogleVoicesCatalog();
const voiceName =
  payload.voiceName?.trim() ||
  resolveGoogleVoiceName(catalog, googleLang, selectedVoiceFromSettings);
```

`resolveGoogleVoiceName`:

- Use Settings `selectedVoice` **only if** that catalog entry’s `languageCodes` match `googleLang`
- Else `pickPreferredGoogleVoice` → prefer `*-Standard-C`, then any `Standard`, then first match

Settings: `AIContext` `ttsProvider` + `selectedVoice` (Menu → Pronunciation). AR scripted lines:

- `ttsProvider === 'google'` (default) → `/api/tts` with `languageCode` + `voiceName`
- `ttsProvider === 'browser'` → `playAppTts` browser path (fallback; AR should still prefer Google)

Playback: existing `playAppTts({ text, language, provider: 'google', selectedVoice: voiceName })` in `lib/tts-play.ts` → `POST /api/tts` (`app/api/tts/route.ts` → Google `text:synthesize`).

Unity `voiceNameOverride` must be a real `name` from `voices.json` (e.g. `ru-RU-Wavenet-C`, `en-US` Chirp3 names like `Aoede`). Invalid names → ignore and resolve from catalog.

### 6.3 Tessa follow-up (same prompt, conversational)

Template: `RouteFollowTessaBridge` + `AiSheetLiveGuide.sendDiscoveryPrompt`.

```ts
setPlaceContext({ placeName: title, language, /* lat/lng from offer if any */ });
setAiSheetMode('LIVE_GUIDE');
primeGeminiLiveAudioForUserGesture(); // may already have a gesture from Unity tap; proximity has none
await ensureVoiceLiveGuideReady();    // connectLive(true)
await sendMessage(tessaKickoffPrompt);
```

Kickoff prompt (app-built, not raw exhibit copy):

```
The traveler is in AR looking at "{title}" (id: {objectId}).
Speak in {userLanguage}. Briefly introduce this object, then ask if they want to know more.
Object notes: {prompt}
```

**Exception — long character scripts:** if `{prompt}` is longer than ~80 characters (e.g. `walking_man` / Ivan Sokolov), `buildUnityArTessaKickoff` in avento-app does **not** wrap it as a brief exhibit intro. It sends:

```
Speak in {userLanguage}. Stay fully in character.

{prompt}
```

Use that for `speechMode: tessa` NPCs. Short exhibit copy still uses the brief-intro template above.

`LiveGuideVoiceBar` / `AiSheetTessaTab` stay the Tessa chrome. Do not call those components from Unity; drive Live Guide context.

**Proximity has no user gesture** → iOS audio may block until a tap. Mitigations: pre-unlock audio when opening Unity Scene; or first proximity line waits until next tap; or native AVAudioSession already active from AR.

**Scene-start Tessa** uses the Launch tap as the gesture: call `primeGeminiLiveAudioForUserGesture()` in `UnitySceneViewer` / `openUnityArFromVrSettings` **before** presenting UaaL, then `connectLive(true)` after ready+placed. If connect fails (backgrounded), retry on first Unity tap.

---

## 7. App bridge component

New `components/vr/UnityArTessaBridge.tsx` (or `UnityArInteractBridge.tsx`), mounted wherever Live Guide is (same tree as `RouteFollowTessaBridge`).

Responsibilities:

1. Subscribe to `unityArObjectInteract` (and `unityArReady`) while a Unity session is open (and keep listening after `openScene` returns — today `UnitySceneViewer` awaits dismiss; **must add a listener that lives for the session**, not only after exit).
2. If VR `autoStartTessa`: prime audio on Launch, then on ready+placed **or** `trigger: scene_start` → one `connectLive` + scene kickoff `sendMessage` (dedupe so Unity + app fallback do not double-greet).
3. Deduplicate object events (`objectId` + cooldown).
4. Pick prompt + language + voice (§6).
5. `speechMode`:
   - `tts` → `playAppTts` (stop previous with `stopAppTts`); if Tessa already live, pause/resume or inject context
   - `tessa` → Live Guide connect + `sendMessage` (skip connect if session already open from scene start)
   - `tts_then_tessa` → TTS `onended` then Tessa
6. Optional: `UnitySendMessage` back (`AventoObjectInteract`, `OnSpeechStarted` / `OnSpeechEnded`) so the NPC can lip-sync / talk animation.

`UnitySceneViewer` today listens only until the native `openScene` promise settles (session end). Interact events happen **during** the session → register the listener in the plugin **before** present Unity, on a long-lived provider, not inside the blocking `openScene` await.

---

## 8. UX while Unity is fullscreen

UaaL covers the WebView. Choices:

| Option | Use |
|--------|-----|
| **1. Audio-only in AR** (v1) | Google TTS (and Tessa background voice) play; no sheet. Exit AR to see transcript. |
| **2. Native overlay** | Small “Tessa / Stop / Mic” bar above Unity (like Exit chrome), forwarding to JS. |
| **3. Minimize Unity** | Dismiss or shrink UaaL, show `AiSheetTessaTab` + `LiveGuideVoiceBar`. |

**v1:** option 1 for TTS + proximity; option 2 if Tessa is running (scene start or `speechMode` includes Tessa — mic must stay reachable). Scene-start Tessa implies a VoiceBar overlay from the first second. Do not auto-dismiss the scene on tap — that kills the exhibit.

---

## 9. Implementation phases

### Phase 0 — Session-lived JS listener

- Extend `UnityArSession` event union with `unityArObjectInteract`.
- Register listener in a provider that outlives `openScene`’s promise.
- Log payload in debug (`vrLog`). No speech yet.

### Phase 1 — Unity tap interact

- `AventoObjectInteract` + raycast-before-place.
- Native notify + iOS/Android handlers.
- Rebuild UaaL.
- QA: tap statue → JS event; tap plane → still places.

### Phase 2 — Google TTS in user language

- Bridge calls `playAppTts` with `getUserAppLanguage` + `resolveGoogleVoiceName(voices.json)`.
- Honor Settings voice when language matches; else Standard-C for that BCP-47.
- `promptByLanguage` / fallback rules.
- QA: app language `ru` → `ru-RU` + Russian catalog voice; switch to `en` → `en-US`.

### Phase 3 — Proximity / walking character

- Distance to AR camera + hysteresis + cooldown + single speaker lock.
- `trigger: "proximity"` in JSON.
- QA: walk toward NPC (or move phone toward placed character) → line plays once; walk away and back after cooldown → optional repeat.

### Phase 4 — Tessa follow-up + auto-start on scene open

- `speechMode` `tessa` / `tts_then_tessa`.
- Offer flag `autoStartTessa` + optional `autoStartTessaPrompt`; pass through `openScene` / `OpenFromNative`.
- On Launch: `primeGeminiLiveAudioForUserGesture()`. After ready+placed (or Unity `scene_start`): `connectLive` + scene kickoff. Dedupe vs object events.
- `UnityArTessaBridge` → `setAiSheetMode('LIVE_GUIDE')` + `ensureVoiceLiveGuideReady` + `sendMessage`.
- Overlay or background `LiveGuideVoiceBar` (§8) whenever Tessa is live in AR.
- Proximity + Tessa: document audio-session / gesture limitation.

### Phase 5 — Polish

- CMS prompts per `objectId` (avento-web) so copy updates without rebundling.
- Speech start/end callbacks for NPC animator.
- Optional caption in Unity (`OpenFromNative.language`).

---

## 10. File map (planned)

### avento-ar

```
Assets/MobileARTemplateAssets/Scripts/
  AventoObjectInteract.cs          # tap + proximity, payload
  AventoSceneTessa.cs              # optional scene_start after placed
  AventoInteractionDirector.cs     # one speaker, cooldown
  AventoUnityNative.cs             # + NotifyObjectInteract
  AventoUnityHost.cs               # OpenFromNative autoStartTessa; sceneReadyForTessa
  TapToPlaceOnAnchor.cs            # raycast interactables first
  AventoTapReceiver.cs             # unchanged entry; owner routes to interact
Assets/Plugins/iOS/AventoUnityNativeBridge.mm  # + OnObjectInteract
```

### avento-app

```
lib/vr/native/unityArSession.ts           # + unityArObjectInteract; autoStartTessa
lib/offer-vr.ts                           # + autoStartTessa / autoStartTessaPrompt
components/vr/UnityArTessaBridge.tsx      # new (scene start + object events)
lib/tts-play.ts                           # reuse playAppTts
lib/tts-translate-target.ts               # voices.json + resolveGoogleVoiceName
public/voices/voices.json                 # Google catalog (no duplicate)
context/AIContext.tsx                     # ttsProvider / selectedVoice
components/AiSheet/AiSheetTessaTab.tsx    # Tessa chrome (unchanged API)
components/LiveGuideVoiceBar.tsx          # mic/stop (unchanged API)
ios/.../AventoUnityNativeBridge.*         # + object-interact handler
android/.../UnityArNativeBridge.kt        # + onUnityObjectInteract
```

### avento-web

```
src/lib/offerVr.ts                         # + autoStartTessa fields
src/components/admin/sections/VrExperienceSection.tsx  # checkbox + prompt
```

---

## 11. QA checklist

| Case | Expect |
|------|--------|
| Tap interactable | Event `trigger=tap`; no new placement |
| Tap empty plane | Place as today |
| Proximity enter | Event `trigger=proximity`; TTS once (or Tessa if `speechMode=tessa`) |
| Stay inside radius | No repeat until exit + cooldown |
| Two NPCs close | Only one speaks (`AventoInteractionDirector`) |
| `walking_man` / Ivan (`speechMode=tessa`) | Walk-up or tap → Live Guide in character; first greeting from prompt |
| App language `ru` | `languageCode=ru-RU`, Russian `voices.json` name, Russian prompt if authored |
| Settings voice matches language | That `selectedVoice` is sent to `/api/tts` |
| Settings voice is `en-US` but UI is `ru` | Ignore settings voice; pick Russian catalog voice |
| `voiceNameOverride` on component | Used if it exists in catalog |
| `speechMode=tts` | Google TTS only; Tessa not opened |
| `speechMode=tts_then_tessa` | Line then Live Guide + VoiceBar |
| Offer `autoStartTessa` on | After ready+placed, Tessa greets in user language (once) |
| Offer `autoStartTessa` off | Silent until tap / proximity |
| Scene start + later object tap | Same Live session; object sends a new turn, no second connect |
| Scene start + Unity `scene_start` | Single greet (app fallback does not double-fire) |
| Exit AR mid-TTS / Tessa | `stopAppTts` + `closeLiveSession`; ignore further interact events |
| iOS UaaL tap | Interact still works via native catcher (`readme.notap.md`) |

---

## 12. Out of scope (v1)

- Gemini Live as the NPC voice (wrong catalog)
- On-device Unity TTS
- Lip-sync visemes beyond start/stop animator flags
- Multiplayer / two users in one scene
- Changing `voices.json` from Unity Editor (catalog stays in avento-app)

---

## 13. Decision summary

1. **Triggers:** scene start (optional Tessa), tap (raycast before place), and proximity (distance to AR camera / trigger enter).  
2. **Scripted speech:** Google Cloud TTS + `public/voices/voices.json`, language from `getUserAppLanguage`, voice via `resolveGoogleVoiceName`.  
3. **Conversation:** Tessa Live Guide via `connectLive` / `sendMessage` / `LiveGuideVoiceBar` — including **auto-start when the Unity scene is ready** (`autoStartTessa`).  
4. **Bridge:** one new native event `unityArObjectInteract` (`tap` / `proximity` / `scene_start`), same path as session lifecycle.  
5. **v1 UX:** stay in AR for TTS; overlay or background voice if Tessa starts (required if auto-start is on).  
6. **One session per visit:** scene start connects; later object events only `sendMessage`.
