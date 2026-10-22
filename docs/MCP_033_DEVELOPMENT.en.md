# 0.3.3 development record

<!-- mechcue-docs-language -->
[日本語](MCP_033_DEVELOPMENT.md) | [English](MCP_033_DEVELOPMENT.en.md) | [Documentation](INDEX.en.md)

Branched from stable 0.3.2. See [open issues](OPEN_ISSUES.en.md) for remaining work. Sections describe successive implementations; later sections supersede earlier architecture.

## Priority work — 2026-10-23 development code

Actual force-limited finger positions are output as `fingerAxisKeyframes`, while commands remain unchanged. CAD playback and inspection use actual positions; dashed graph overlays show them. Physics events/warnings appear in Physics results and MCP `physics.report` and persist in CAD settings.

The development record reports successful app self/UI/engine and 119-tool MCP/protocol checks. With commanded finger positions −10/+10 mm, engine contact tests produced actual −2.540/+2.680 mm.

Initial real-assembly support captures planar convex polyhedra with CAD density/volume mass, centre transforms, shape inertia and six free-workpiece axes. Native capture of a 20-part fixture settled with .0327 mm gap and a clear native support/workpiece check. See [convex assembly physics](CONVEX_PHYSICS.en.md) for the exact scope and validation workflow. These builds are workspace validation; installed updates and publication were not performed by that work.

All 190 pairs in that fixture passed continuous inspection over 0–4 seconds without allowed-contact exclusions. CAD density 7833 kg/m³ and volume .001 m³ produced a 7.833 kg workpiece. Chart save/reconnect restored its geometry, mass and report. Physical-property computation preserves the original CAD density rather than replacing the density cache with a unit-density calculation.

Simulation condition editing/deletion was also verified natively: load value/direction/name/suppression, fixed-condition name/suppression, expected-name guards and deletion of the selected condition. Changes are read back; failed edits attempt restoration.

## Alpha.01: rigid-body workpiece motion

`solidedge_simulate_concept_rigid_body` computes workpiece keyframes from arm/finger joint graphs without connecting CAD or a chart. `physicsJson` controls gravity, mass, friction and floor height.

`arm5p` adds F1/F2 finger slides to arm5 (negative F1/positive F2 close) and workpiece WX/WY/WZ/WRZ/WRY/WRX. Home remains arm5's pose. Workpiece roll/yaw support ±720° and pitch ±180°. `MeasureConcept` resolves the translation-plus-Z/Y/X rotation chain together from its leaf part; both application forms share this code.

The engine is abstracted by `IRigidBodyEngine`. Initially it lived in MCP; BepuPhysics 2.5.0-beta.33 was chosen after 2.4.0 produced roughly one-quarter friction. At this stage the old geometric arm5 tool still existed; alpha.03 removes it.

Engine tests compare rest, 0.142 s free fall versus ideal 0.143 s, slope friction (μ=.3, 0.59 m/s²), accelerated-plate lag 1.547 m versus theoretical 1.548 m, tipping, lifting/slipping between fingers. A native 12 s example maintained hand/workpiece relative pose while carrying; opening at 9.2 s caused about 100 mm fall and rest at 9.6 s with zero tilt. Path inspection was not done at that stage. Shapes are simplified boxes/cylinders and restitution was initially approximate.

## Alpha.02: derived physics tracks

The six workpiece axes become dotted, point-free read-only tracks marked Physics. Dragging, table edits, renaming and direct MCP point edits reject; resampling skips them. Arm/finger or physics setup changes mark results stale and recompute after roughly 0.7 s, away from the UI thread. Edits during computation discard/restart the result; playback does not recalculate.

Manual recomputation uses the header button or `mechcue_recompute_physics`. `mechcue_set_physics(auto=..., setupJson=...)` controls automatic calculation/setup. State exposes availability, Auto, stale/setup and track `derived`.

Derived flags, setup and `ComputedHash` persist in assembly settings. Each motion has its own result/currentness. Same inputs after reopening retain current status. This release moved the engine into the main application and temporarily added locked NuGet dependencies/Bepu DLLs to both forms. **That in-process architecture was replaced in alpha.03.** Existing arm5/nonphysics models were unchanged.

## Alpha.03: separate calculator process

Because Bepu uses native aligned allocation/pools and unverifiable code, loading it inside the add-in exposed Solid Edge to engine failures. `Physics/MechCue.Physics.exe` now owns `IRigidBodyEngine`, `BepuEngine` and simulation. `--simulate` reads model/setup/joint JSON on stdin and writes result JSON on stdout. `--self-test` tests engine and process round-trip, invalid input, crash, timeout and missing executable.

The main app keeps derived-track metadata, fingerprints/results and `PhysicsProcess`, loading no BepuPhysics. It finds the calculator beside `MechCue.dll`, or via developer `MECHCUE_PHYSICS_EXE`. Missing/crashed/timed-out calculation (default 120 s) reports a normal error while CAD/app keep running. Input is written on another thread so a child that does not read cannot block the deadline.

MCP references the calculator project as a library and computes inside its own separate process. The root app again has no external package sources/lockfile; Physics dependencies are locked in `Physics/packages.lock.json` and restored in locked mode. Installer/portable ZIP include calculator files plus BepuPhysics/BepuUtilities DLLs.

A 13 s pick/place took about 0.43 s including 0.18 s startup, matching alpha.02: lift 4.36 s, rest 9.64 s, final WX −350.51 mm.

### Maintenance included in alpha.03

- Migration now copies `SavedConcept.Physics` (Auto/setup/hash); stable track/pattern IDs preserve hash validity.
- `VerifyPhysicsUi` smoke tests replace `physicsRunner` with a fake, covering read-only access, stale/busy/current labels, manual command-only input, delayed Auto, discarded concurrent edits and Auto off.
- README/MCP guide and draft 0.3.3 notes document physics tracks and tools.
- `ConceptTemplates` centralizes per-type generation/catalog descriptions and shared ArmToHand. All types/four travel variants had identical model JSON/catalog output before/after refactor.
- UI document paths use `Bridge.DocumentPath` instead of direct reflection.
- `HandleAi` dispatches a command table with always/video-allowed/idle-only conditions while preserving validation order/errors. All MCP command names are accounted for; transport handles ping/start/get operation.
- Drawing/feature-editing long lines were reformatted with no nonwhitespace change.
- Old geometric `solidedge_simulate_concept_physics`, its implementation/tests were deleted; shared points/events moved to `PhysicsPoints.cs`. Historical tool count changed 117→116; use arm5p instead.

Common self/smoke/calculator/MCP/COM/installer checks passed; this maintenance step did not itself perform native validation. Work initially on alpha.04 was included in alpha.03 by the user's instruction.

## Alpha.04: contact, gripping and restitution

Comparing Bepu versions on an accelerating plate found box friction exactly one-quarter in 2.4.0 across 1/2/4/8 substeps, while a one-contact sphere matched μ=.2. Beta.33 matched for both. Evidence points to a multipoint normal-force cap in 2.4.0 rather than callback usage; the callback only passes coefficients. The box acceleration regression detects it.

`ContactHz` defaults 120 Hz, range 10 to twice PhysicsHz. Resting penetration follows `g/(2πf)²`: measured 0.276/0.069/0.017/0.004 mm at 30/60/120/240 Hz. Tests remained stable at 120–480 Hz.

`GripForceN` caps force per finger (unset retains position driving); `GripAxes` defaults F1/F2. Fingers are constrained to the hand outside their sliding axis using PointOnLineServo/AngularServo, then driven toward graph position by a force-limited LinearAxisServo. Hand/finger collision is disabled, gravity on the fingers is cancelled, and finger mass equals workpiece mass to avoid excessive penetration. A deviation over 2 mm emits `finger-stopped`.

Position-driven force depends on penetration × stiffness (about 284 N/mm at 120 Hz/.5 kg); use GripForceN for meaningful force. Position-grip tests use 0.1 mm penetration. A .5 kg cube with μ=.5 needs over 4.9 N per finger: 3/4 N slip, 6+ N lift.

Spring damping poorly approximated restitution (target .8 yielded .49 at 30 Hz/.27 at 120 Hz). Instead, when contact arrests over half an approach speed of at least .1 m/s, an impulse produces outgoing speed `e × incoming speed`. The first approach speed is retained across speculative-contact steps. Drops from .3 m yielded effective .28/.48/.77 for requested .3/.5/.8.

The real 13 s graph retained alpha.03 motion at 30 Hz; at 120 Hz final WZ improved from −.28 to −.02 mm. Force 20 N carried, 3 N did not; restitution .5 settled at 9.80 s. New setup fields change hashes, so saved physics becomes stale and recomputes.

Installed alpha.04 on 2026-10-15 recomputed saved charts automatically after .8 s, ending WZ −.017 mm. Changing grip to 20 N/restitution .5 recalculated in 1.6–1.7 s. The initial diagnosis that starting overlap caused endless refinement was later corrected below.

## Contact skin and sliding pairs

Even .017 mm penetration caused native interference (status 2), while zero-distance touch alone did not. `ContactSkinMm`, default .05 mm, adds depth in the narrow-phase manifold (`GetDepthReference`). Bodies settle separated by skin minus spring penetration: about .033 mm at 120 Hz, final workpiece WZ +.03 mm. It offsets contacts, not geometry, and is applicable beyond current simple shapes. Installed no-table-exclusion path verification remained pending in the issue list.

Inspection did not refine already colliding pairs: those were immediately violations. It repeatedly split native-clear but zero-gap rotating/sliding joints whose `distance − motion − .01 mm` lower bound could never become nonnegative, up to depth 20. `MotionBoundMath.SlidingContact` now marks pairs unverified without further subdivision if both endpoints are below required gap+margin and relative movement exists in both halves. It prompts a justified AllowedContact instead of declaring clear. Pairs that separate or stop still refine.
