# MechCue MCP

<!-- mechcue-docs-language -->
[日本語](README.md) | [English](README.en.md) | [Documentation](../docs/INDEX.en.md)

A local MCP server for AI control of MechCue, supporting both standalone and add-in windows. It runs separately and communicates with the same Windows user's chart through named pipes. It includes neither an AI model nor API keys.

## Installation and connection

1. Close Solid Edge and select the optional **MCP server (AI control)** component in setup (available since alpha.17; initially unselected).
2. Default executable: `C:\Program Files\MechCue\MCP\MechCue.Mcp.exe`. Start-menu MechCue MCP entries open the folder/guide. For ZIP use, retain every extracted file together.
3. Open MechCue and enable **AI connection**, or start standalone with `MechCue.exe --enable-ai`. Since alpha.20 AI connection is on by default and can be turned off.
4. Add the full executable path to your AI client's local stdio MCP configuration. See `mcp-config.example.json`; formats vary between clients and paths must match the actual install/extraction location.
5. Choose CAD read/selection or creation/editing in the MechCue MCP tray settings.

```json
{
  "mcpServers": {
    "mechcue": {
      "command": "C:/Program Files/MechCue/MCP/MechCue.Mcp.exe",
      "args": []
    }
  }
}
```

Requires Windows x64 and .NET 8 Desktop Runtime x64. CAD tools need running Solid Edge under the same user. The AI client starts the stdio executable; normally do not double-click it. Protocol uses standard input/output and diagnostics use standard error. The MCP server communicates locally; any AI-service internet connection belongs to your client.

## Access modes

The separate `control/MechCue.Mcp.Control.exe` tray program starts with MCP, with one icon per Windows session. Check hidden tray icons if necessary. Click either mouse button to choose:

- **No Solid Edge access:** chart operations only.
- **Read and select only:** inspect CAD data and select parts/faces.
- **Allow creation and editing:** also create, move, constrain, save and export CAD.

Settings are shared by that user's MCP processes and apply on the next operation without restarting. Running CAD operations are not interrupted. Chart AI connection is an independent control.

Start tray settings from the Start menu or the ZIP's `control` directory. Closing the icon does not stop MCP or reset access. The saved file is `%LOCALAPPDATA%/MechCue/mcp-access.json`; unreadable settings mean no CAD access. A person changes modes in the tray; MCP tools cannot change them. First start defaults to no CAD access. Legacy `--allow-solidedge`/`--allow-solidedge-write` supply an initial mode only before a tray preference exists.

With MCP installed, setup starts the icon after normal installation and at Windows login, using user privileges and preserving access choices. Silent setup starts it at next login. Uninstall removes its Startup shortcut. The AI client still starts the actual server.

## Chart tools

| Tool | Purpose |
|---|---|
| `mechcue_list_sessions` | List AI-enabled windows |
| `mechcue_get_capabilities` | Version, supported capabilities and recovery guidance |
| `mechcue_get_session_health` | Pipe reachability and UI readiness/responsiveness |
| `mechcue_get_state` | Tracks, stable IDs, points, units, targets and playback/physics state |
| `mechcue_set_keyframe` / `mechcue_set_keyframes` | Update/add points; bulk editing where supported |
| `mechcue_reset_values` | Set every value of a track, retaining times |
| `mechcue_resample` | Resample from zero to an end time at equal intervals |
| `mechcue_undo` | Undo the last graph edit |
| `mechcue_seek` | Set the time cursor |
| `mechcue_play` / `mechcue_stop` | Play/stop |
| `mechcue_set_playback_options` | Set Apply, interference stop and subdivision |
| `mechcue_set_physics` | Auto recompute and physics setup |
| `mechcue_recompute_physics` | Recompute derived physics tracks now |

Numbers are one-based. Inspect the state first and prefer stable `trackId` over order. Supply `sessionId` when multiple charts are present. Reads return committed graphs; starting edits also commits pending table input.

Graph changes pause playback and temporarily suspend reflection without moving CAD, retaining targets and Apply checks. Next play/seek follows Apply. Resampling linearly interpolates the old graph and holds its final value after its endpoint; it does not stretch time. The end point is included. Limits: 10001 points per track and 100000 overall. Bulk edits are one Undo.

Play/seek respects Apply. Auto enables Apply only on Play; a failed initial pose prevents playback and disables Apply/Auto. Play at the final time restarts from the beginning.

Session health: `reachable` means communication succeeds, `uiReady` means a window handle exists, and `uiResponsive` means the UI thread answered within one second. A handle alone does not prove responsiveness. False may mean busy CAD or a hung UI: inspect before further edits. Older versions lacking this field have unverified responsiveness.

Named motion tools are `mechcue_list_patterns`, `mechcue_create_pattern`, `mechcue_switch_pattern`, `mechcue_rename_pattern`, `mechcue_delete_pattern`. Edits affect the active pattern; targets are shared. Switching pauses/rewinds without moving CAD and preserves current Apply/Collision/Loop checks. CAD Save persists all motions. See [motion patterns](../docs/MOTION_PATTERNS.en.md).

`mechcue_place_window(position=1..9, sessionId=...)` maximizes Solid Edge and places the connected chart using keypad positions (1 bottom left, 5 centre, 9 top right, default 3). Mode/size are preserved except to fit the monitor; repeats never toggle mode. Edit permission is required. See [window placement](../docs/WINDOW_PLACEMENT.en.md).

## CAD inspection

| Tool | Purpose |
|---|---|
| `solidedge_get_document` | Active name/path, read-only and dirty state |
| `solidedge_list_parts` | Top-level parts and XYZ in mm |
| `solidedge_list_variables` | Variable names, values, formulas and unit types |
| `solidedge_select_part` | Select/highlight a top-level part |
| `solidedge_get_assembly_tree` | Recursive paths, reference keys and local/world matrices |
| `solidedge_get_view` / `solidedge_set_view` | Read or change 3D camera, fit and zoom |
| `solidedge_export_view_image` | Export the current 3D view to a new JPEG |
| `solidedge_check_interference` | Static whole-assembly or two-set interference |
| `solidedge_export_interference_report` | Native part/centroid/location/volume report |

Selection and mutations require `expectedDocument`, using returned `fullName`; other reads should supply it too. Changed active documents reject. Selection replaces prior selection but does not change geometry/placement. Direct CAD tools do not need a MechCue chart or chart AI connection.

Tree `Path` is a current one-based index path such as `/1/3`, not a permanent ID. `KeyPath` combines native occurrence reference keys; unavailable keys return null plus a warning. Matrices are column-major 4×4 with metre translations. Truncation, failures and cycles are explicit. Rigid hierarchy is composed; top-document overrides for movable subassemblies are unsupported in this original tree inspection.

Camera directions are current/front/back/top/bottom/right/left/isometric; named views use parallel projection. Front looks from −Y, top from +Z, right from +X. `zoomFactor` accepts 0.1–10. JPEG size is 64–4096 px; export does not fit or overwrite automatically. View changes/exports require edit access and return `dirtyBefore/dirtyAfter`: display-only changes can still dirty a native document, without saving it.

Static interference compares the assembly against itself when `set1Json` is empty. For example `set1Json="[1]", set2Json="[5]"` compares only the groups, not their internal pairs. Duplicate/shared/out-of-range numbers reject. Native checks expand subassemblies. Unknown/incomplete results never become clear. Missing nested pair arrays return `pairDetailsComplete=false`; names are not guessed.

Report output needs a new absolute `.txt` path and returns up to 65536 native-text characters; units are in the report. No interference solids are created. When native analysis is definitively clear but creates no file, a `mechcue-clear-summary` is produced with `reportCreated/reportSource`. Other missing files return warnings, not a clear verdict, and do not repair missing pair details.

Variable-table values retain native units (metres/radians), unlike creation tools' mm/degrees. Failed property reads include row errors.

## CAD creation and editing

| Tool | Purpose |
|---|---|
| `solidedge_new_document` / `solidedge_open_document` | New ordered part/assembly/drawing or open a saved document |
| `solidedge_save_document` | Explicit save or save to a new path |
| `solidedge_close_document` / `solidedge_reopen_document` | Close/reopen saved documents with guards |
| `solidedge_list_planes` / `solidedge_list_features` | Read native planes/features and state |
| `solidedge_extrude_profile` | Finite rectangle/circle/polygon protrusion or cutout |
| `solidedge_place_part` / `solidedge_position_part` | Place a saved part/subassembly or set top-level pose |
| `solidedge_mate_planes` | Reference-plane constraints and offset |
| `solidedge_add_drawing_view` / `solidedge_update_drawing` | Add/update linked drawing views |
| `solidedge_export_pdf` | Export a drawing to a new PDF |

Use returned `fullName` as the next `expectedDocument`, even if an unsaved document has no extension. Plane/feature listing works in read mode. CAD lengths/coordinates use mm and rotation uses degrees.

Extrusion XY is local to the chosen plane; positive, negative or symmetric direction is supported. Polygon `pointsJson` must not repeat the closing vertex. The first extrusion is the base; additions must join existing shape. The original scope is single-body ordered parts; existing modeling modes are not silently changed. Feature names are checked for duplicates before mutation. A post-creation failure reports `PARTIAL SUCCESS`; inspect features before retrying.

Placement XYZ is absolute assembly coordinates; rotation order is X→Y→Z. Existing constraints can reject a pose. For planar mating, ground the base and place the other component with `ground=false`. This tool mates reference planes, not arbitrary faces/cylinders/edges. Drawing positions are sheet mm; scale 1 means 1:1. Models must already be saved.

Mutations do not save automatically. New outputs/PDFs never overwrite files or create missing parent folders. Omit `outputPath` to save the current file. CAD commands do not guarantee all-or-nothing Undo/rollback; inspect partial features, placements, constraints or views after failure. Operations and exceptions are logged.

After creating/opening temporary parts or subassemblies for assembly work, finish editing, verify health, save and close only those no longer needed with `returnDocument` pointing to the working assembly. Dirty documents or dirty referenced descendants reject closure. Preserve windows the user had open before the task and save chart settings before closing.

Later extensions include [workflow tools](../docs/MCP_WORKFLOW_FIXES_20261002.en.md) (launch/state/safe quit, asynchronous saves, nested driving, dimension changes, Custom properties and BOM), [detail tools](../docs/DETAIL_MCP_IMPROVEMENTS_20261003.en.md) (subassembly packaging, sketch editing and edge-key rounds), and [machining](../docs/MACHINING_MCP_20261003.en.md) (holes, threads, counterbores/countersinks, patterns, chamfers and driving dimensions). Historical lists are dated; current server tool discovery defines the installed version's actual capabilities.

## Concept models and detailed-model migration

Generate mill3/mill4/mill5/gantry/robot models and import axes using the [concept guide](../docs/CONCEPT_TARGETS.en.md). Import measures actual placements, creates constants and preserves edits on reimport. CAD Save embeds axis definitions/bindings.

To migrate:

1. Save the source chart/assembly with `mechcue_save_document(expectedDocument, sessionId)`.
2. Read saved `settingsJson` with `solidedge_get_mechcue_settings`; this excludes unsaved screen edits.
3. Prepare a detailed assembly/manifest with identical axis IDs, ordering, hierarchy, kind, direction, origin/ranges and reference pose, uniquely targeting direct parts/rigid subassemblies.
4. Call `mechcue_migrate_concept_settings(expectedDocument, manifestPath, settingsJson, hideUnboundTracks=true, sessionId)` on the destination session. IDs, all patterns/points and reference axis values remain. Existing destination settings/bindings, nonconcept source bindings or out-of-range points reject. Migration edits chart state only; keep the entire request below the AI connection's 64 Ki-character limit.
5. Save explicitly, then reopen the clean document if needed. Relist sessions because the ID may change.

`unboundTracks="hide"` hides only unbound sample graphs without deleting data; unresolved/existing bindings remain. Default `preserve` retains the original behavior. See [migration record](../docs/MCP_WORKFLOW_IMPROVEMENTS.en.md).

## Drawing, PMI and Simulation

- [Drawings](../docs/DRAWING_AUTOMATION.en.md): front planning, view listing, automatic/completed drawings, principal dimensions, arrangement and duplicate hole-position removal.
- [3D PMI](../docs/PMI_AUTOMATION.en.md): associated part/assembly dimensions and model views; component-to-component distances follow motion. Listing can initialize native PMI storage and dirty a file. No automatic save or tolerance inference.
- [Simulation](../docs/SIMULATION_AUTOMATION.en.md): single-solid linear static studies, materials, fixing/force/pressure, mesh, solve, stress/displacement. Needs native Simulation licensing. `meshLevel=1..10` defaults to 5; alert suppression can be local to a run; mesh regeneration is explicit.

## Motion inspection

Use `mechcue_start_operation(operation="check_motion", argumentsJson=...)` and operation polling for path checks. UI can inspect one or all motion patterns and export results. [Adaptive inspection](../docs/ADAPTIVE_MOTION_INSPECTION.en.md) describes alpha.03 sampled checks; [continuous inspection](../docs/CONTINUOUS_MOTION_INSPECTION.en.md) describes nested leaf pairs, reasoned allowed contacts and supported rigid-motion interval proofs.

Policy tools: `mechcue_list_inspection_parts`, `mechcue_set_inspection_policy`, `mechcue_get_inspection_reports`. Completed operations return a summary and `reportId`; page details with report ID, `includeSamples`, offsets and limits. UI JSON exports all stored detail. Budgets, cancellation, dirty geometry and unsupported movement never count as verified. Playback Apply/Collision/subdivision and time budgets are described in [playback inspection](../docs/MCP_PLAYBACK_INTERFERENCE.en.md).

## Updates, documents and sessions

Setup terminates only the old MCP/tray in its install location and suppresses restart during updates. It does not terminate Solid Edge or unrelated MCP installations. Reconnect MCP in your AI client afterwards; restart the client if it has no reconnect action. Starting the tray alone does not restore stdio. Check capabilities/version/access and session list before continuing; do not blindly replay interrupted edits.

`solidedge_open_document` checks the source and hidden destination with referenced descendants. Dirty state returns `document_switch_requires_save`; `mutationStarted=false` means no opening/closing/saving occurred. Save chart settings, then referenced children with `solidedge_save_referenced_document(expectedDocument, documentPath)`, then the parent. Subassembly descendants must be explicitly saved first.

`session_not_found`, `session_unavailable` and `session_selection_required` return recovery guidance; edits are never automatically redirected or resent. Relist sessions, check the active document and use `solidedge_open_mechcue` to reload saved settings. Unsaved charts from a closed window are not guaranteed recoverable. See [recovery](../docs/MCP_RECOVERY_031.en.md).

## Traffic monitor

Open **MCP traffic monitor** in tray settings. It refreshes every 500 ms, showing time, PID, direction, tool, success/failure and duration with newest first. Selection shows argument/result JSON; automatic selection follows new events unless paused. It does not show model-internal reasoning or conversations never sent to MCP.

Controls: persisted Always on top; Pause display (recording continues); Record traffic (default on, independent of CAD permission); Search by tool/request ID/JSON; Clear displayed history (not files); Save filtered JSON Lines.

Storage: `%LOCALAPPDATA%/MechCue/mcp-access.json.traffic`; preferences: `mcp-access.json.monitor.json`. Records contain paths/arguments/results: review before sharing. Logging uses separate local files and failure does not interrupt MCP.

Limits: latest 500 displayed events; about 4 MiB × three generations per process; periodic cleanup retains up to 64 older files and removes connection records older than seven days. Messages over 256 KiB have truncated monitor detail, not truncated MCP traffic. Reopening reads the last 256 KiB per file. Rolled-out history cannot be restored after display pause.

## 0.3.2 and 0.3.3 development

0.3.2 adds generic offset planes for protrusions/cuts/holes and paged/filterable metric-thread catalogs. See [0.3.2 development](../docs/MCP_032_DEVELOPMENT.en.md).

Arm5p rigid-body computation uses `solidedge_simulate_concept_rigid_body(manifestPath, jointsJson, physicsJson)` without a CAD/chart connection. Negative F1/positive F2 close fingers. Six workpiece axes are derived: state reports `derived=true`, and direct keyframe edits reject. Edit joint/finger graphs instead.

Chart physics recomputes after about 0.7 s. State exposes `physics.available/auto/stale/busy/setup`. Disable Auto with `mechcue_set_physics(auto=false)` or compute immediately with `mechcue_recompute_physics`. Settings/results/fingerprints persist in the assembly and migration. Computation runs in separate `MechCue.Physics.exe`, isolating crashes from CAD/MechCue. The old geometric `solidedge_simulate_concept_physics` was removed in alpha.03.

Alpha.04 adds contact stiffness `contactHz` (default 120 Hz, about 0.02 mm resting penetration), force-limited `gripForceN` and restitution via `physicsJson/setupJson`. Shapes are concept boxes/cylinders, not actual CAD contact analysis. See [physics development](../docs/MCP_033_DEVELOPMENT.en.md) and [open issues](../docs/OPEN_ISSUES.en.md).

## Development and validation

The official C# MCP SDK runs only in the separate server, not inside the add-in. NuGet dependencies are locked; the MCP project uses nuget.org. Build with `./scripts/Build.ps1 -WithAddIn -WithMcp`; use `-PythonPath <python.exe>` if necessary. Python is only a development dependency.

`Test-Mcp.py` checks stdio initialization/discovery, permissions, real test-window communication, graph changes, bulk Undo and session isolation; it stops only test standalone processes and does not save user CAD. `--native-read` enables hardware reads. Native suites use dedicated copies/new folders, including `Test-McpInspection.py`, `Test-McpWorkflowFixes.py` and feature-specific scripts linked above.

Historical creation integration entry point: `MechCue.exe --cad-integration-test <fresh-absolute-folder>` using running Solid Edge 2026 ISO Metric templates. An optional third argument supplies MCP executable testing over stdio. It checks ordered profiles/cuts, placement/rotation/grounding/mates, drawings/PDF and restoration, preserving the original document. CI without CAD validates protocol, tool definitions and permissions, not real native driving.
