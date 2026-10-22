# Physics for convex polyhedra from real assemblies

<!-- mechcue-docs-language -->
[日本語](CONVEX_PHYSICS.md) | [English](CONVEX_PHYSICS.en.md) | [Documentation](INDEX.en.md)

Development changes dated 2026-10-23. Updating the installed version is a separate step.

The initial scope is an assembly with 2–128 top-level single-solid `.par` components whose bodies are convex polyhedra bounded entirely by planar faces. One is a free workpiece; the others are fixed obstacles/supports. Each part may have 4–256 vertices and 4–512 faces. Rotated placements and centres of mass offset from part origins are supported.

Curved surfaces, concavity, cavities, nested assemblies, variable parts, simplified representations and assembly-replaced bodies are rejected. Unsupported geometry is not filled by a bounding box or convex hull to continue. Mechanism/gripper movement or constraint driving is not inferred by this capture.

1. Save the assembly and referenced parts. Use `solidedge_get_document` and `solidedge_list_parts` to identify the document and workpiece number.
2. Call `solidedge_capture_convex_physics(expectedDocument, workpieceNumber, outputPath)`. Supply a new absolute `.json` path whose parent folder exists.
3. Default `workpieceMassKg=0` obtains mass from CAD material density and computed volume. Missing density rejects: assign material or specify a positive mass. The engine calculates inertia from actual convex shape and uniform density. Multiple materials and direct inertia-tensor input are unsupported.
4. Connect MechCue to that assembly and pass returned `manifestPath` to `mechcue_import_concept_axes`. The workpiece's six axes become physics tracks with save/restore support. Without commanded axes, calculation still uses chart duration.
5. Call `mechcue_recompute_physics` and inspect **Physics results** or `mechcue_get_state` → `physics.report`. This initial feature uses the existing chart time range and automatically applies captured CAD mass.
6. For standalone result inspection use `solidedge_simulate_concept_rigid_body(manifestPath)`. Defaults use captured mass and two seconds. Supplying `physicsJson` replaces the entire setup, so include mass too.

Capture is a geometry/pose snapshot and does not follow later part edits. Recapture into a new file after geometry changes. Capture itself changes neither CAD poses/materials nor saved CAD files. Chart Apply/Save are separate actions.

## Validation

```text
scripts/Test-ConvexPhysicsNative.py --mcp <development-MechCue.Mcp.exe> --mechcue <matching-MechCue.exe> --output <fresh-folder>
```

The test creates only a new 20-part fixture, checking CAD-derived mass, convex capture, native support/workpiece interference after settling, chart import/recompute/continuous inspection/save/restore. Failure state is recorded in `report.json`; continuous results in `chart-path-report.json`. Existing document edits are not discarded.

API references: [vertex coordinates](https://support.industrysoftware.automation.siemens.com/trainings/se/106/api/SolidEdgeGeometry~Vertex~GetPointData.html), [volume and centre of mass](https://support.industrysoftware.automation.siemens.com/trainings/se/107/api/SolidEdgeGeometry~Body~ComputePhysicalProperties.html), [Bepu convex hull, centre and inertia](https://docs.bepuphysics.com/api/BepuPhysics.Collidables.ConvexHull.html).
