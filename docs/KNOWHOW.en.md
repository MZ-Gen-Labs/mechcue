# Development know-how

<!-- mechcue-docs-language -->
[日本語](KNOWHOW.md) | [English](KNOWHOW.en.md) | [Documentation](INDEX.en.md)

Causes, workarounds, decisions and validation learned during MechCue development. Entries follow symptom → cause → resolution → evidence. Use `git show <short-hash>` for cited commits. Last source update: 2026-10-15 during 0.3.3-alpha.04 development. This English edition consolidates repeated explanations; linked records preserve detailed experiments.

## Physics engine (BepuPhysics)

### Quarter-strength box friction in 2.4.0

A box on an accelerating plate received one-quarter expected friction across 1/2/4/8 substeps, while a single-contact sphere matched the coefficient. Evidence indicates a library multipoint cap based on one contact's normal force; callbacks only passed the coefficient. Use locked 2.5.0-beta.33 and run the accelerating-box self-test on any version change. See [physics development](MCP_033_DEVELOPMENT.en.md), [issues](OPEN_ISSUES.en.md), `85d11d4`, `3e5300b`.

### Spring contact penetration

Resting WZ became negative because contact is a spring: penetration is `g/(2πf)²`. Measured 30/60/120/240 Hz values were .276/.069/.017/.004 mm. `ContactHz` defaults120, range10–2×PhysicsHz; 120–480 Hz remained stable. See `Physics/BepuEngine.cs`, `85d11d4`.

### Contact skin for native interference

Solid Edge flagged even .017 mm penetration (distance0, native status2). Add `ContactSkinMm` (default .05, range0–5 mm) to manifold depths through `GetDepthReference`, rather than altering geometry. At120 Hz, bodies settle about .033 mm above the floor. Tests check .01–.05 mm resting separation; arm5p ends WZ+.03 mm. This contact offset generalizes beyond current boxes/cylinders. See [development](MCP_033_DEVELOPMENT.en.md), physics contracts/simulator/self-tests.

### Position-driven fingers are not force-controlled

Force depends on penetration×stiffness, about284 N/mm at120 Hz/.5 kg. Use per-finger `GripForceN` for a force limit, targeting graph positions through LinearAxisServo and constraining other axes with PointOnLineServo/AngularServo. Disable finger-hand collision, cancel finger gravity and use workpiece-equivalent finger mass to avoid excessive penetration. More than2 mm tracking error emits `finger-stopped`. With .5 kg/μ=.5, fingers need over4.9 N each;3/4 N slip,6+ N lift. Position tests use .1 mm penetration. CAD still draws commanded, not force-stopped finger positions. See [issues](OPEN_ISSUES.en.md), `85d11d4`.

### Restitution needs an impulse

Spring damping gave .49/.27 for requested .8 at30/120 Hz, so it was unsuitable. After contact arrests over half an incoming speed≥.1 m/s, add an impulse for outgoing speed=e×incoming, retaining the first speed across speculative-contact steps. A .3 m drop gave .28/.48/.77 for requested .3/.5/.8. See `BounceThreshold`, `approaching`, `85d11d4`.

### Isolate engine computation from Solid Edge

Alpha.02 loaded Bepu into the add-in; native pools/aligned allocation/unverifiable code made failures a host risk. Alpha.03 uses JSON stdin/stdout with `MechCue.Physics.exe`; missing executable, crash or120 s timeout is a normal app error. Write input on a separate thread so a nonreading child cannot defeat timeout. The root app again has no external packages. Developer override: `MECHCUE_PHYSICS_EXE`. A typical call took .43 s including .18 s startup. See `PhysicsProcess.cs`, `c6194c6`; installed isolation was checked in `bf632d6`.

### Separate commands from results

Six workpiece axes are read-only derived tracks. `ComputedHash` covers commanded joints/setup, survives reopen and rejects stale results after concurrent edits; no recomputation during playback. Adding setup fields legitimately invalidates saved hashes. Migration must copy physics setup/hash/Auto too; omission was fixed. See `3d22449`, `6ca9be5`, [development](MCP_033_DEVELOPMENT.en.md).

## Motion inspection and interference

### Sliding pairs can cause endless refinement

Without allowed contacts, arm5p spent4895 evaluations near the first2.3 ms and exhausted600 s. Already interfering pairs were immediately violations, not the cause (the earlier diagnosis was withdrawn). Clear-but-touching moving pairs could never satisfy `max(endpoint gap) − relative motion − .01 mm≥0`, yet split to depth20.

`MotionBoundMath.SlidingContact` identifies pairs whose endpoints are below required gap+margin and move relatively in both halves, leaving them **unverified** with advice to register a justified contact. Separating/stopping pairs still refine. See `ContinuousMotionInspection.cs`, `MotionKinematics.cs`, `PlaybackSafetyTests.cs` and [development](MCP_033_DEVELOPMENT.en.md).

### Register intentional zero-gap sliding contacts explicitly

Base–saddle/saddle–table cannot prove positive separation while sliding in contact. Reasoned `AllowedContacts` completed the mill3 check within ten seconds. Arm5 used five joint contacts plus table/workpiece; an arm5p fixture used11 pairs. Never auto-exclude. `timeBudgetSeconds` accepts1–3600; `verdict="clear"` requires complete uncancelled verification, not just clear samples. See [concepts](CONCEPT_TARGETS.en.md), [playback inspection](MCP_PLAYBACK_INTERFERENCE.en.md), `ff83418`, `3996319`.

### Static touch and zero distance

For verified saved geometry, exactly zero relative motion and clear completed native endpoints, do not subtract an extra movement margin that endlessly refines static touch. This exception excludes dirty, moving, interfering and incomplete pairs. Zero distance alone is neither collision nor clearance proof: contained solids can have positive boundary distance. Use native interference too. Overlapping bounds with `IncludeInInterference=false` mean incomplete. See [continuous inspection](CONTINUOUS_MOTION_INSPECTION.en.md).

### Large results break memory and transport

Repeating all leaf pairs/long keys at every pose produced a2 GB buffer failure and MCP−32603 for269022550 bytes on a44-part model. Store shared identity once and only defect pairs/aggregates, spill details, return summary/reportId and page at most100 poses/4 MiB. Separate graph keys from coarse evaluation poses, refine when needed and stream export. The later full-range result was328066 bytes, with several contributing changes. See [model verification](MILL5_ALPHA04_ACTUAL_MODEL_VERIFICATION_20261003.en.md), [compact implementation](COARSE_MOTION_INSPECTION_IMPLEMENTATION_20261003.en.md), [completion](MOTION_INSPECTION_COMPLETION_20261003.en.md), `b527a60`.

### Initial concept overlaps

Mill columns ignored saddle depth/travel and collided at creation; mill5 also had30 mm trunnion/table overlap. Position columns beyond full saddle travel plus20 mm and extend bases. Check every linear endpoint combination, with rotary axes zero. Existing models need regeneration. See [0.3.2 development](MCP_032_DEVELOPMENT.en.md), `33a0962`.

## Solid Edge COM and automation

### License variables missing from MCP children

Three of nine MCP processes lacked machine SE_LICENSE_SERVER; their children inherited an incomplete environment. Resolve five license variables process→user→machine only at launch, keep explicit values, change only the child and log names/sources, not values. See [launch investigation](SOLID_EDGE_MCP_LAUNCH_LICENSING.en.md), `SolidEdgeLifecycle.cs`, `Test-SolidEdgeLaunchNative.py`.

### Interference array types

Pass occurrence arrays as `SAFEARRAY(IDispatch)` with `VT_DISPATCH` elements. Self-tests create real native VARIANT/SAFEARRAY objects and check type. See [validation](VALIDATION.en.md), `6299617`, `SelfTest.cs`.

### Newly created assembly PMI does not follow its second target

`AddDimension` could keep cached points/values despite changing reference transforms; saving alone failed and reopening worked. Immediately Undo/Redo only the new dimension, verifying its key/count and other dimensions. If the wrong action was undone, Redo restores it and returns warning/`motionRebuilt=false`. No movement-time update loop is added. See [PMI](PMI_AUTOMATION.en.md), [investigation](../PMI_FOLLOW_INVESTIGATION_20261012.en.md), `PmiRebuildForMotion`, `799ddb7`.

### First PMI access dirties a document

Native PMI storage may initialize even on listing. Return `nativePmiInitialized=true`; never clear dirty flags or save automatically. See [PMI](PMI_AUTOMATION.en.md).

### Dimension names differ from variable-table names

`Dimension.Name` can remain `Dimension 363` after PutName, hiding SizeX from variable tools. Use `Variables.GetName` and retain old-name compatibility. See [0.3.2 development](MCP_032_DEVELOPMENT.en.md), `910ba5b`.

### Sketch symmetry and persistent planes

Projected reference lines cannot directly serve as the tested AddSymmetric axis (`E_FAIL`); use a construction line constrained onto the reference. Name driving dimensions/depth, reapply values then verify size/centre (`33a0962`). Create standalone offset planes with `Local=false`, since unused local planes vanish on save (`b46e86a`).

### HoleData identity and threads

After thread changes, HoleDiameter may mean nominal; retain native `diameterMm` and expose `effectiveBoreDiameterMm`. Reapply the same HoleData to preserve saved/reopened Depth. Multiple Hole2d centres may collapse: use one centre plus Pattern. Use exact configured descriptions such as M8, not guessed aliases. See [machining](MACHINING_MCP_20261003.en.md), [0.3.2 findings](MCP_032_DEVELOPMENT.en.md).

### Concept driving can conflict with ordinary workpiece tracks

Concept driving rewrites all placements, including fixed parts. Represent the workpiece through WX→WY→WZ concept axes rather than an ordinary competing track; MeasureConcept can read translation-only descendant chains. See `5acfce4` and robot development.

### Loaded child documents and dialogs

Repeated Documents.Open on an already loaded child hung on hardware; reuse the document. The original same-path retest was incomplete. Native save/solve dialogs can block calls: use solve-local alert suppression with restoration, while recognizing license/solver exceptions. Preflight dirty document switches and return `document_switch_requires_save`. See [workflow](MCP_WORKFLOW_FIXES_20261002.en.md), [Simulation](SIMULATION_AUTOMATION.en.md), [recovery](MCP_RECOVERY_031.en.md).

## MCP, stdio and sessions

### Child stdout corrupts JSON-RPC

`UseShellExecute=false` children inherited protocol stdout and emitted native directory lines. `DetachStandardStreams` gives them dedicated streams and closes the parent's ends. Hardware changed three stray lines to zero. See [0.3.2 development](MCP_032_DEVELOPMENT.en.md), `9d767aa`, `6d172ed`.

### Pipe reachable does not mean UI responsive

External COM-created WinForms windows appeared to lack a continuing message loop. Dispatch OpenChart through the host STA created on OnConnection. Probe UI-thread response within one second through `uiResponsive`, separately from reachable/handle-ready. Also skip delayed Shown reconnection when already connected to preserve documentReady/save. See [dispatch fix](MCP_UI_DISPATCH_FIX_20261003.en.md).

### Reused PIDs create stale sessions

Windows reused a chart PID for SEPropDB.exe. Check recorded process start time; legacy records require a live pipe. See `fe71fa6`.

### Separate transport deadlines from CAD lifetime

Use async start/get operation with UUID deduplication for saves/path checks. Separate pipes can read status while the UI is busy. Cancel/budgets are cooperative between calls and cannot interrupt one native CAD call. See [workflow](MCP_WORKFLOW_FIXES_20261002.en.md).

### Reject unknown arguments and reconnect after updates

Compare case-sensitive public schemas before execution; return `unknown_tool_arguments`, valid names and `mutationStarted=false`, including unknown/duplicate edit-JSON keys. Replacing files does not update running MCP/add-in code. Setup stops targeted old MCP/tray, but the AI client must reconnect/restart. Confirm capabilities releaseVersion/path, and update the add-in for new add-in commands. See [recovery](MCP_RECOVERY_031.en.md).

## UI and add-in

Ribbon range errors were resolved with unique GUID/local-ID names, individual SetAddInInfoEx bitmap sets and its runtime IDs, without duplicate AddCommand. Increment GuiVersion for cached ribbon changes. Native magenta was visible: composite backgrounds/antialiasing against white and reject pure-magenta pixels. See [validation](VALIDATION.en.md), `0f15d00`, `f68e057`, `086fe16`.

Selecting a point with a header-only/tiny grid caused FirstDisplayedScrollingRowIndex failure: skip scrolling while retaining selection (`2cfa607`). CI desktop width1024 could not meet a1180 minimum: enforce one-line tests only at full toolbar width while always checking overflow (`232ddb7`, `f467c5e`).

Mode changes use `WM_SETREDRAW` through one shared path, resuming after final bounds. Undo/Redo handles chart-native child input rather than global shortcuts, avoiding text/inactive/key-release interception. See [placement](WINDOW_PLACEMENT.en.md), [completion](MOTION_INSPECTION_COMPLETION_20261003.en.md).

UI document access uses `Bridge.DocumentPath`. AI dispatch uses a command→conditions/handler table with always/video-allowed/idle-only states. See `2550c50` and [0.3.3 development](MCP_033_DEVELOPMENT.en.md).

## Installer, build and releases

Inno Setup recreates the uninstall key, erasing custom InstallMode. Prefer its remembered standalone component choice; legacy fallback checks prior MechCue.exe/display name. Seven temporary HKCU tests cover detection (`fe71fa6`).

NuGet rejects numeric alpha.01. Keep padded display/tag/filename/InformationalVersion, normalize SDK Version only through Build.ps1 and align three default-version files. Lock MCP/Physics packages and restore in locked mode; the root app has none after process isolation. See [releasing](RELEASING.en.md).

Alpha GitHub releases remain `draft=true, prerelease=true`. Sort within base versions by creation time through seconds with [release list](RELEASE_LIST.en.md); do not rename tags for ordering. Follow the [release skill](../.agents/skills/mechcue-release/SKILL.md).

Validate a locally built installer on Solid Edge before requested GitHub delivery. Build into a fresh output directory and keep latest installer under artifacts/latest as used in the development workflow. Record automated versus native results separately. See [open issues](OPEN_ISSUES.en.md), `bf632d6`, `b16d0a9`.

## Validation methods

| Target | Entry point |
|---|---|
| App/UI | `MechCue.exe --self-test`, `--smoke-test` |
| Physics/calculator boundary | `MechCue.Physics.exe --self-test` |
| Native concept models | `MechCue.exe --concept-integration-test <fresh-folder>` |
| Native motion inspection | `dotnet MechCue.dll --advanced-motion-integration-test <fresh-folder>` |
| MCP | `scripts/Test-Mcp.py` |
| Native individual features | `scripts/Test-*Native.py` |

Compare physics with theory: free fall, slope/accelerated-plate friction, tipping, grip/slip, bounce height and penetration. Different contact shapes distinguish library failures. UI physics tests substitute physicsRunner to isolate read-only/state/debounce/concurrent-edit logic.

To test MCP directly, use UTF-8 stdio JSON-RPC: initialize→initialized notification→tools/call. `MECHCUE_MCP_NO_TRAY=1` suppresses test tray startup. This can inspect actual binaries even when a client connector is disconnected/stale.

Native tests create only new test documents and record/restore original document, dirty state and window count. Save/close completed test files; do not discard user work or quit CAD as cleanup. Never commit CAD/native outputs/Siemens DLLs; output/ is excluded.

Distinguish clear samples from `continuousPathCertified`, completed sampling/analysis from `allSamplesClear`, and automated checks from hardware confirmation. Cancel, timeout, unknown analysis and budget exhaustion never mean safe.
