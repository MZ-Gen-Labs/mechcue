# Investigation: assembly PMI following motion

<!-- mechcue-docs-language -->
[日本語](PMI_FOLLOW_INVESTIGATION_20261012.md) | [English](PMI_FOLLOW_INVESTIGATION_20261012.en.md) | [Documentation](docs/INDEX.en.md)

**Resolution added 2026-10-12:** immediately undoing/redoing only a newly created dimension made API-created PMI follow motion without save/reopen or movement-time recalculation. This was integrated into CadAutoPmi. The resolution supersedes the investigation's initial unresolved conclusion.

Environment: Solid Edge 2026, installed MechCue 0.3.2-alpha.01, local `output/Mill3Demo_20261012/concept.asm`. This English edition consolidates the chronological experiments and preserves their results; local outputs are not public repository assets.

## Goal and initial observations

The user required motion without adding Dimension.UpdateStatus or AssemblyDocument.UpdateAll to playback. A separately authorized comparison allowed one UpdateStatus immediately after creating a dimension on a copy, not a runtime update loop.

The eight-part three-axis fixture comprised base/column/saddle/table/head/spindle/tool/workpiece with16 s XYZ motion. Generated PMI included overall width700/height900 mm, table X/Y position, tool-tip height and rigid table500×220×60 mm. Manual height PMI had native key887; the API comparison “調査用(API)” used key822/id `probe/user-check` (historical IDs, not permanent assumptions).

Manual Smart Dimension followed440→500 mm between0/2 s without explicit updates. API comparison references highlighted correctly but points/value stayed at creation (486.520997 mm). At t=.393846153846154 s, both references' tool-centre Z was .5018153846153847 m with local tip −.05 m, so true height451.8153846153847 mm. Manual Value/GetRelated matched it; API Value/GetRelated stayed486.520997. Reference retrieval/highlighting is not proof of point/value motion.

Rigid table dimensions should keep their values while annotation placement follows; position/height must change. Restoring horizontal/vertical measurement mode did not repair freezing. Cache/dependency/notification explanations were hypotheses, not established native internals.

## Comparisons that did not solve it

Real vertices, edge keypoints and circular centres; IsKeyPoint/virtual/intersection/tangent flags; reuse of manual references/planes; world/local/related coordinates; measurement axes/orientation/extension directions/annotation positions; initialized creation data and no tags; official typed SDK/marshaling; command begin/end, Activate and command10914/10917 contexts; model-view registration; variable names/exposure; CreateReference2 edge references/TopologyReference; PutMatrix/Move/PutTransform and multiple-component motion; test assembly constraint offsets; DoIdle; and user-confirmed property acceptance all failed to establish following.

Both manual/API dimensions existed in native variables, so simple missing variable registration was ruled out. Preselected native edges still needed interactive selection and created no new PMI. CreateReference2 with TopologyReference failed E_FAIL; real-plane creation data E_NOINTERFACE; manual Duplicate E_NOTIMPL; AddDistanceBetweenObjects failed in PMI settings and was not a successful substitute. Environment had DelayCompute=false, ScreenUpdating=true, Interactive=true and activated parts.

The UI automation comparison was limited by an owner-identity error even when paths appeared identical. These limits did not justify declaring either a Siemens defect or merely bad creation arguments.

## Reopen workaround and one-time UpdateStatus

After save/reopen, existing API dimensions followed without runtime updates:

| Time | Table X | Table Y | Tool height | Rigid table size |
|---|---:|---:|---:|---|
| 0 s | 100 | 190 | 440 | 500×220×60 |
| 2 s | 100 | 190 | 500 | Same |
| 8 s | 200 | 250 | 500 | Same |
| 4 s | 0 | 130 | 500 | Same |
| Return0 | 100 | 190 | 440 | Same |

All lengths are mm. New dimensions created afterwards still froze; existing-data workaround and corrected creation are separate claims. Save alone failed. In an authorized copy, immediate UpdateStatus once versus none both retained451.8153846153847 mm after a60 mm tool move and return. This did not support a playback update loop. Temporary dimensions/copy were cleaned up and original model/chart state restored.

## Interim code and model state

The initial changes fixed reopening a saved target without saving/closing another dirty active model (`ConceptMigration.cs`), added `motionFollowVerified=false` and accurate guidance, and separated immediate versus after-reopen checks in Test-AssemblyPmiNative.py. They initially did not fix PMI creation. Builds/self-test/Python syntax checks passed, but the whole native suite was not rerun at that intermediate point. No installed update, version change or release occurred.

The original eight generated dimensions, one manual dimension and comparison API dimension were retained; other temporary PMI was removed and copied windows closed. Time .393846153846154 s/Apply=true and original dirty state were restored, including the other pre-existing SimpleGantry document. Comparison axis orientation was restored but freezing remained until the later resolution. Historical images/code/results remain under the local `output/Mill3Demo_20261012/` investigation folders, excluded from Git.

## Resolution: rebuild the new dimension

The failure was isolated to moving the **second** reference. First-reference motion worked. Both real-vertex and circular-edge dimensions froze. On copied fixtures with PutMatrix and no UpdateStatus/UpdateAll:

| Creation path | Initial | Moved | Returned |
|---|---:|---:|---:|
| Vertex–vertex, creation only | 640 | 640 (frozen) | 640 |
| Vertex–vertex, immediate Undo/Redo | 640 | 700 | 640 |
| Vertex–circle, creation only | 440 | 440 (frozen) | 440 |
| Vertex–circle, immediate Undo/Redo | 440 | 500 | 440 |
| Old overall-Z with moved second reference | 900 | 900 (frozen) | 900 |
| Fixed overall-Z | 900 | 960 | 900 |
| Manual comparison | 440 | 500 | 440 |

After Undo/Redo, −30/+60/+120 mm moves matched manual PMI, retaining key and following after tagging, TrackDistance, orientation and model-view registration. CachedSolve, visibility/Activate toggles, relationship command33067, UpdateDocument and one UpdateAll did not fix creation. Delete→Undo could not retrieve the dimension; Copy/paste failed E_POINTER.

Redo reconstructs recorded dimension data, consistent with reopening registering dependencies. That is an inference; native internals were not directly inspected.

`PmiRebuildForMotion` in `SolidEdgePmi.cs` immediately executes Edit Undo57643/Redo57644 for each new assembly dimension. It verifies only the new key disappears and other dimensions remain. A mismatch triggers Redo restoration and warning/`motionRebuilt=false`. Results include `added[].motionRebuilt`; no runtime update loop was added. Tool descriptions/docs and native regression were updated.

Development app/MCP builds succeeded without updating installed files. Test-AssemblyPmiNative passed all17 cases including immediate follow without reopen/updates; evidence `output/Assembly-Pmi-native-20261012-rebuild-01/report.json`. A Mill3 copy generated11 features-mode dimensions, all rebuilt. Two .3/.35 m swapped hole-position warnings also occurred in the old DLL and were a separate pre-existing orientation issue.

Old frozen dimensions, including key822, still require recreation or reopen. Auxiliary output/ creation programs use separate code paths and do not inherit the fix. Probe copies were retained as local evidence.

## Enabling MCP for this workflow

Install optional MCP, preserve all ZIP files if using ZIP, and run AI client/MCP/MechCue/CAD as the same Windows user with .NET 8 Desktop Runtime x64. Configure the client for local stdio at `C:/Program Files/MechCue/MCP/MechCue.Mcp.exe`, usually with empty args. Reconnect after updates.

The tray independently controls CAD access (none/read-select/create-edit); human selection of create/edit is needed for PMI/moving/saving. Tray preference persists in `%LOCALAPPDATA%\MechCue\mcp-access.json`, overrides legacy initial args and applies without MCP restart. Closing the tray does not stop/reset MCP.

Chart AI connection is a separate toggle, on by default in current code; direct CAD reads need no chart, while seek/play requires an open enabled session, targets and Apply. Verify capabilities/version/path/access, active document/fullName, session list and state. Relist sessions after reopen. Missing tools need client reconnect/path checks; empty sessions need chart/AI/same-user checks; permission errors need tray changes, not merely AI connection. Full setup/examples: [MCP guide](Mcp/README.en.md), [configuration example](Mcp/mcp-config.example.json).
