# Single-part stress and displacement analysis through MCP

<!-- mechcue-docs-language -->
[日本語](SIMULATION_AUTOMATION.md) | [English](SIMULATION_AUTOMATION.en.md) | [Documentation](INDEX.en.md)

Introduced in 0.3.0-alpha.01, this operates Solid Edge 2026's native Simulation API, rather than a MechCue finite-element solver. It supports single-solid `.par` parts, linear static studies and tetrahedral solid meshes with fixed faces, force and pressure. Ordered test parts were verified on hardware.

Solid Edge must be running with Simulation features and a suitable license; API presence alone does not establish licensing. Tray edit mode permits setup/solve; read mode permits listing, selection and results. Assembly contact, shells/beams, nonlinear, buckling, vibration and thermal studies are outside scope.

## Workflow

1. Read `fullName` using `solidedge_get_document` and use it as `expectedDocument`.
2. List materials/libraries with `solidedge_list_simulation_materials`; apply the exact name with `solidedge_apply_simulation_material` if needed. Shared library properties are unchanged.
3. Create a study with `solidedge_create_simulation_study`; retain its native `study.number` as `studyNumber`.
4. Select fixed faces in CAD and call `solidedge_add_simulation_fixed`, or list faces, inspect positions/areas, highlight IDs and pass a JSON string array as `faceIdsJson`.
5. Add loads with `solidedge_add_simulation_load`: `kind=force` uses N and a normalized global XYZ direction; `pressure` uses MPa and native face-normal conventions. Force distribution across multiple faces follows Solid Edge.
6. Check `solidedge_list_simulation_studies`. `solidedge_run_simulation(meshOnly=true)` meshes only; otherwise it meshes and solves.
7. Require `completed=true` and `study.solved=true`. Retrieve results with `solidedge_get_simulation_results`, display them with `solidedge_show_simulation_results`.
8. Explicitly call `solidedge_save_document` to save conditions/results as native study data, separate from chart JSON.

Example requests: create a steel study and fix selected faces; load the selected face with 100 N in global −Y; solve with a 3 mm mesh and report maximum equivalent stress/displacement; show displacement then save the study.

## Tools and units

The workflow tools are complemented by `solidedge_list_simulation_faces` and `solidedge_select_simulation_faces`. `faceIdsJson` is a string containing an array such as `["returned-face-id"]`. Omitted/empty IDs use selected faces for condition creation; the selection tool itself rejects empty arrays. Nonface selections and stale IDs reject. Relist faces after shape edits.

Since alpha.03, `meshLevel=1..10` controls the native slider (1 coarse, 10 fine); new studies default to 5. Actual size is shape-dependent, with both `meshLevel` and `meshSizeMm` returned. Creation defaults to `meshSizeMm=0, meshLevel=5`; a positive absolute size takes precedence. At run time both zero preserves settings, and both cannot be positive simultaneously. To change an existing mesh use, for example, `meshLevel=8, regenerateMesh=true, suppressAlerts=true`.

Absolute `meshSizeMm` accepts 0.1–1000 mm; runtime zero retains it. Alpha.02 corrected conversion to native metres; regenerate meshes from alpha.01, whose displayed setting did not prove actual size. Stress/displacement output bits are added to existing result options.

`resultKind=stress` is solid von Mises stress in MPa; `displacement` is resultant translation in mm; `active` is the active plot. Native type numbers/SI values are also returned. Reading does not change the plot. Extrema coordinates are converted to mm; inspect local peaks in native graphics too.

## Alerts and remeshing

`solidedge_get_automation_settings` reads application-wide `DisplayAlerts`. `solidedge_set_display_alerts` changes it until explicitly restored, returning old/new values. Prefer solve-local `suppressAlerts=true`, which restores the previous value on success or exception.

`regenerateMesh=true` deletes/recreates only the selected study's mesh, preserving geometry/material/conditions but invalidating old results. For example, `meshSizeMm=2, regenerateMesh=true, suppressAlerts=true` remeshes and solves at 2 mm.

Some license/external-solver dialogs may ignore DisplayAlerts. It does not promise universal hiding or approval. If Solid Edge exits, restoring its setting is impossible and communication fails. See [DisplayAlerts API](https://support.industrysoftware.automation.siemens.com/trainings/se/107/api/SolidEdgeFramework~Application~DisplayAlerts.html).

## Validity and errors

Existing studies/conditions are not removed. Repeating creation adds more; use Solid Edge UI to edit/delete existing conditions in this initial feature. No automatic save occurs.

MechCue compares geometry/material/conditions/mesh against its successful-solve fingerprint in `MechCueSimulation` AddInsStorage. Missing/mismatched records reject result retrieval and require solving again. `nativeSolved` is the native flag, `resultsCurrent` the comparison, and `solved` requires both.

Creation or solving another study while in results mode rejects before mutation. Close results/return to the model environment first. Partial COM failures may leave some conditions; inspect lists before retrying.

Solve is synchronous. License/mesher/solver dialogs can leave MCP waiting; a client timeout does not establish that CAD stopped. Check the screen and do not start duplicate solves. `completed=false` is not success: inspect study errors, `nastranError` and native messages. There is no automatic retry.

Tests validate conditions, units and integration; mesh convergence and physically appropriate support/material assumptions still depend on the analysis purpose.

## Validation

```powershell
python scripts/Test-SimulationMeshSlider.py --mcp <built-MCP.exe> --output <fresh-output>
python scripts/Test-SimulationNative.py --mcp <built-MCP.exe> --output <fresh-output>
```

The slider test creates a 200×30×10 mm part and checks level 5, endpoints 1/10, automatic size changes, regeneration and a 200 N solve. The native test creates a 100×10×10 mm cantilever, checks materials/fixing/100 N/mesh/stress/displacement, compares beam displacement and doubling-load response. Tests isolate settings and do not alter/close existing parts. Use `--material` and `--library` for non-Japanese material libraries. `Test-Mcp.py` covers access/inputs without CAD; CI does not run native solves.

API references: [AddStudy](https://support.industrysoftware.automation.siemens.com/trainings/se/107/api/SolidEdgePart~StudyOwner~AddStudy.html), [Solve](https://support.industrysoftware.automation.siemens.com/trainings/se/107/api/SolidEdgePart~Study~Solve.html), [SetResultOptions](https://support.industrysoftware.automation.siemens.com/trainings/se/107/api/SolidEdgePart~Study~SetResultOptions.html). COM declarations and behavior were checked on installed Solid Edge 2026.
