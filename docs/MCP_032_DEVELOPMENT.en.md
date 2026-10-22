# 0.3.2 development record

<!-- mechcue-docs-language -->
[日本語](MCP_032_DEVELOPMENT.md) | [English](MCP_032_DEVELOPMENT.en.md) | [Documentation](INDEX.en.md)

This series branched from stable 0.3.1 and was consolidated into [0.3.2](releases/0.3.2.en.md). Dates and tool counts below describe historical builds.

## Alpha.01: planes, threads and detailed-feature editing

`solidedge_create_offset_plane` creates a persistent native global parallel plane on an empty or single-model Ordered part, offset along a selected normal. Position/normal/health are verified and removal attempted on failure. No naming is provided; save explicitly and relist numbers after deletion. Returned plane numbers can feed protrusions, cuts and holes. Set native `Local=false`: unused local planes disappeared on save in hardware tests.

```text
solidedge_list_planes(expectedDocument=...)
solidedge_create_offset_plane(expectedDocument=..., planeNumber=1, offsetMm=50)
solidedge_extrude_profile(expectedDocument=..., planeNumber=<returned-number>,
  shape="rectangle", operation="cut", direction="negative", ...)
solidedge_list_metric_threads(nominalDiameterMm=8, descriptionContains="M8", limit=20)
```

Thread search combines case-insensitive description substring, exact family and nominal diameter filters using AND. `offset/limit`, `matchedCount/nextOffset` support paging. Exact native descriptions are retained; XLSX thread catalogs remain unsupported.

On 2026-10-04, additional detailed-design work passed 14 native checks and the full common build (including 111-tool MCP). Only dedicated test parts were used after user-requested saving/closing of existing work; completed tests closed back to no documents. This was source/local delivery, not GitHub publication or user installation.

| Tool | Scope |
|---|---|
| `solidedge_get_feature_details` | Ordered-single-model entry plane frame, normal side, finite depth, sketch line/circle/hole data and native parent/dependent relationships; unsupported curves are explicit |
| `solidedge_edit_hole` | Same-entry-plane single-centre position/diameter/depth/counterbore/countersink/finite thread depth; snapshot/restore/verify; shared HoleData rejects |
| `solidedge_set_hole_thread` | Change/remove cosmetic internal thread, bore and depth using the same native HoleData; reject shared data and fully verify restoration |
| `solidedge_edit_hole_pattern` | Single-seed rectangular count/pitch or circular count/angular spacing, retaining entry plane/axis |
| `solidedge_delete_machining_feature` | Refuse dependents, require a new recovery-copy path, verify only the requested hole/pattern vanished; Undo on failure, report recovery copy if needed |
| `solidedge_check_interference_pairs` | Occurrence-specific leaf keys/index paths/module/files for whole/two-set comparisons; preflight pair limits and unknown disabled/incomplete checks |
| `solidedge_check_hole_interface` | Read-only coaxiality/order/bore/nominal/thread engagement/bottoming/head checks for two single-seed holes; caller supplies bolt diameter/length/stack/required engagement; unknown never passes |

`includeVolume=true` parses temporary native interference text only with recognized Japanese/English labels, decimals, explicit mm³ and matching counts. Values are rounded native results; other formats/units remain unknown. No interference solids are created. Request filtering matches exact schema argument names/case and rejects unknown names before execution with `unknown_tool_arguments`, valid names and `mutationStarted=false`. Edit JSON rejects unknown/duplicate keys too.

Native findings: `HoleDiameter` can become nominal after thread updates; compatibility `diameterMm` retains it while `effectiveBoreDiameterMm` distinguishes bore. Older `threadNominalDiameterMm` may be zero. `ThreadDataByDescription` and reapplying the same HoleData preserve depth; replacement HoleData broke post-reopen depth reads. Circular patterns change profile Count/AngularSpacing. Report CR/CRLF is normalized.

`Test-OffsetPlaneNative.py` checks signed/three-direction/derived planes, cuts, persistence and thread paging. `Test-FeatureEditingNative.py` checks dependencies, edits/restoration, deletion, reopen, pairs/volume and interfaces, preserving original document/window count. Results/models are not committed.

The real mill's X+300/Y±200/Z−200 collisions need design changes and reinspection. Interface checks do not qualify manufacturing: actual bolts/pins, tolerances/fits, strength, access, every pattern position and real constraints remain. Hole type/plane/direction conversions, multiple centres, curved/sloped/Synchronous/multimodel parts and moving circular-pattern axes are outside edit scope.

API: [AddParallelByDistance](https://support.industrysoftware.automation.siemens.com/trainings/se/107/api/SolidEdgePart~RefPlanes~AddParallelByDistance.html), [Hole](https://support.industrysoftware.automation.siemens.com/trainings/se/107/api/SolidEdgePart~Hole_members.html), [Pattern](https://support.industrysoftware.automation.siemens.com/trainings/se/107/api/SolidEdgePart~Pattern_members.html), [PartDocument](https://support.industrysoftware.automation.siemens.com/trainings/se/107/api/SolidEdgePart~PartDocument_members.html). Calls were checked against installed 2026 Part.tlb.

## Alpha.02–03: installer and sessions

Alpha.02 unified setup into Standard/Add-in only/Custom with optional standalone/MCP and four assets: Setup, standalone ZIP, MCP ZIP and SHA256. Removing standalone removes its executable/shortcut without uninstalling the add-in. Only package/helper checks were run, not actual installation switching.

Alpha.02's old-install detection failed because Inno Setup recreated its uninstall key, erasing custom `InstallMode`. Alpha.03 trusts Inno's remembered component selection; without it, it checks the previous installation's `MechCue.exe`, or display name if the folder is absent. Explicit silent `/COMPONENTS=`/`/TYPE=` wins. `Installer/AddInOnlyDetection.iss` and seven temporary-HKCU scenarios test this without installation.

Stale session records could appear alive when Windows reused a PID. Alpha.03 records/checks process start time; legacy records require an actual named pipe. Integration tests reject both stale-record variants.

## Alpha.04: dimensioned concepts and placement

Concept parts use fully constrained sketches referenced to projected global planes, centred through construction-axis symmetry for rectangles or centre-on-reference constraints for circles, with driving dimensions and symmetric extrusion. Variables are `SizeX/SizeY/SizeZ` or `Diameter/Length`. Values are reapplied after generation and size/centre verified. Direct symmetry about a projected line caused `E_FAIL`, so a constrained construction line is used.

Mill columns previously collided with the 260 mm-deep saddle. The column front is placed beyond half saddle depth plus half Y travel plus 20 mm, extending the base. Mill5's 30 mm trunnion/rotary-table overlap was corrected by lowering the trunnion.

All four original templates passed endpoint-combination self-tests and native dimension/centre tests; regenerated mill3 had no native interference at home/four endpoints. Tilted rotary poses are not part of this generation check. Existing models change only if regenerated.

## Alpha.05: variables and inspection verdicts

Variable tools mistakenly used dimension object names such as `Dimension 363` rather than variable-table names. They now use `Variables.GetName`, still accepting the old name. A generated mill3 column changed SizeX 200→260 mm through development MCP.

`check_motion` gets `timeBudgetSeconds` 1–3600, default ten. Timeout returns `timedOut=true`, `verdict="incomplete"`; clear requires an uncancelled complete clear path, rather than relying on `allSamplesClear`. Mill3's prolonged inspection actually came from unregistered base–saddle/saddle–table zero-gap sliding contacts. Justified AllowedContacts completed the path within ten seconds with supported interval proof.

## Alpha.06: five-axis robot

`arm5` adds J1 swivel Z, J2/J3/J4 parent-Y shoulder/elbow/wrist bend and J5 wrist X roll, with dimensioned table/base/links/hand/fingers/workpiece. Tests cover hierarchy, swivel, hand-only J5 and J3 lowering. All five templates passed 15 native checks.

A 12-second pick/place example calculated joint poses numerically and aligned fingers via J5. Five justified joint contacts enabled a continuously clear path. Workpiece axes WX→WY→WZ were added because concept driving rewrites all part poses and would conflict with a separate ordinary workpiece track. `MeasureConcept` can measure a translation axis lacking a direct part through descendants on distinct translation axes.

Carrying example `output/Arm5Concept_20261012_carry` computed workpiece points every 0.1 s from 4–8 s and set J5=J1, with at most 0.14° hand tilt variation. Static checks and a 1533-pose, roughly 8.5-minute path inspection were clear under the defined policy. Alpha.05 could not import this model's WX measurement; alpha.06+ is needed.

## Alpha.07: protect stdio

Solid Edge and tray processes inherited MCP stdout, letting native lines such as `.. <DIR>` corrupt JSON-RPC. `SolidEdgeLaunch.DetachStandardStreams` supplies dedicated streams and closes the MCP ends after launch. Child output then cannot enter the protocol. Tests cover noninheritance and heavy output after closure. Hardware launching arm5/mill3 produced zero non-JSON lines versus three in the same alpha.06 procedure.
