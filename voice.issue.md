# Unity AR "Ask Avento" voice issue (current state)

## Symptom
- User taps painting in Unity AR -> caption opens.
- User taps **Ask Avento** button.
- Tessa session appears to start, but no audible assistant voice is heard.

## What is confirmed working
- Unity interaction path works:
  - `AventoObjectInteract.TryShowCaption()` fires.
  - `AventoObjectInteract.AskTessaFromCaption()` fires.
  - Unity emits object-interact payload with `speechMode:"tessa"`.
- Native iOS bridge path works:
  - `UnityArSessionPlugin` logs `unityArObjectInteract len=...`.
  - Stop control logs `unityArStopTessa len=...`.
- App JS bridge receives and parses events:
  - `[vr] UnityArInteract: raw event received`
  - `[vr] UnityArInteract: parsed event`
- Tessa start flow reaches success logs:
  - `ensure native tessa ready` -> `native tessa ready ok`
  - `ensureVoiceLiveGuideReady` -> `ensureVoiceLiveGuideReady ok`
  - `startTessa scene kickoff ok`

## What still fails
- Despite successful start logs, no audible response is produced after **Ask Avento**.

## Added mitigations so far
- Added detailed end-to-end diagnostics in Unity, native iOS plugin, and app JS.
- Added Unity in-AR voice bar with Stop button (sends `unityArStopTessa`).
- Added post-connect fallback message injection from Unity AR bridge (`sendMessage(kickoff)`).
- Added delayed fallback send (220ms) to avoid potential stale `loading=true` gate in LiveGuide hook.

## Current hypothesis
- Voice connect is successful, but first assistant output may be dropped/suppressed in runtime state after connect.
- Secondary possibility: `sendMessage` fallback is being skipped by transient loading/mode timing and not forcing playback reliably.

## Next verification point
- Check for this log after latest patch:
  - `[vr] UnityArInteract: post-connect kickoff inject send`
- If present and still silent, patch `useGeminiLiveGuideSession.sendMessage` to allow forced programmatic send for Unity AR kickoff even while `loading` is true.

## Root cause identified
- Unity AR and native bridge were working correctly (raw/parsed event, connect, and kickoff send logs all present).
- The first Unity AR kickoff message could be dropped in `avento-app` due to a race in Live Guide state:
  - `sendMessage` returned early when `loading === true`.
  - Right after `ensureVoiceLiveGuideReady` resolves, React state can still hold stale `loading=true`, so the post-connect kickoff injection became a silent no-op.

## Fix applied (avento-app)
- Updated `hooks/useGeminiLiveGuideSession.ts`:
  - `sendMessage` now accepts optional `options?: { force?: boolean }`.
  - When `force: true`, message send is allowed even if `loading` is true.
  - Loading timeout/state is only started/cleared by this call when it was not already loading, to avoid breaking an in-flight turn.
- Updated `components/vr/UnityArTessaBridge.tsx`:
  - Unity AR post-connect fallback now calls:
    - `sendMessage(kickoff, { force: true })`

## Expected behavior after patch
- On first object tap with `speechMode: "tessa"`:
  - `ensureVoiceLiveGuideReady ok`
  - `post-connect kickoff inject send`
  - Tessa should now produce audible assistant voice instead of staying silent.

## Verification checklist
- Confirm logs still include:
  - `[vr] UnityArInteract: post-connect kickoff inject send`
- Confirm audible Tessa reply starts immediately after this log.
- If still silent after this patch, next likely area is native output audio routing/session policy (speaker route), not Unity event delivery.
