# MCP playback settings and interference

<!-- mechcue-docs-language -->
[日本語](MCP_PLAYBACK_INTERFERENCE.md) | [English](MCP_PLAYBACK_INTERFERENCE.en.md) | [Documentation](INDEX.en.md)

`mechcue_set_playback_options` changes `applyToCad`, `stopOnInterference` and/or `subdivideInspection` for a guarded document/session, preserving omitted fields. At least one option is required.

```json
{
  "expectedDocument": "C:\\work\\concept.asm",
  "sessionId": "chosen-session-id",
  "applyToCad": true,
  "stopOnInterference": true
}
```

It stops playback and changes choices without moving CAD. The next Play/Seek follows Apply. Edit permission is required; a different document rejects. `mechcue_save_document` persists interference settings with patterns, but Apply is session-only and saving does not enable it on restart. State exposes Apply, interference, clearance, margin and `inspectionRunning`.

## Refine checkbox

**Refine**, right of Auto in both views, defaults on and controls playback/seek inspection. On adds samples from movement/rotation and refines continuous intervals according to policy. Off checks original keys and requested destination poses (including playback updates) without inserting samples, leaving intervening motion unverified. It does not alter keyframes/interpolation or grant continuous proof.

Changing it stops playback. The choice can be saved in the assembly and survives pattern switching; older files default on. Explicit UI/MCP path inspection uses its own chosen policy. MCP uses `subdivideInspection` in settings/state.

## Gap versus interference

Required clearance is a lower limit on shortest surface distance, default zero. Contact has zero gap, but distance alone does not determine interference: contained solids can have positive boundary distance. Native interference is also checked. Collision, possible collision, insufficient gap and incomplete analysis are never clear.

Continuous proof has a separate default 0.01 mm numerical margin and subtracts relative motion bounds from endpoint distance, subdividing when needed.

For saved/verified geometry with exactly zero relative motion and completed clear native checks at both ends, the pair keeps its shape/relative pose and no additional motion margin is subtracted. This prevents endlessly subdividing stationary native-clear zero-gap contacts. It does not apply to moving, dirty, interfering or incomplete pairs, and never creates an allowed contact automatically.

## Time budgets and cancellation

Playback does not add inspection computation time to the next motion interval. Each interval has a two-second budget; exhaustion stops as unverified and restores its start pose. Status/logs report timer stops without a modal dialog.

MCP `check_motion` yields between UI-thread CAD calls. Default budget is ten seconds; `timeBudgetSeconds` accepts 1–3600. `mechcue_stop` cooperatively cancels. Timeout returns `timedOut=true`, `verdict="incomplete"`; intervals beyond `lastCheckedTime` remain unverified. Clear requires the entire uncancelled path to be checked and clear; `allSamplesClear` describes only checked poses.

State/health queries remain possible and conflicting inspections/edits reject. Poll operation status for completion/cancellation. Budgets/cancel are checked between native calls and cannot forcibly interrupt a single Solid Edge API call.

Regression tests check no motion when options change, document guards, omitted options, permissions, static-contact bounds, timeout/restoration, cancellation, unknown intervals and nonmodal playback stops. To verify a fix on hardware, install it, restart Solid Edge and reconnect MCP: replacing DLLs on disk does not update an old running inspection.
