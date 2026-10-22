# Holes, threads, patterns, chamfers and driving dimensions

<!-- mechcue-docs-language -->
[日本語](MACHINING_MCP_20261003.md) | [English](MACHINING_MCP_20261003.en.md) | [Documentation](INDEX.en.md)

Development dated 2026-10-03: seven tools increased the 92-tool 0.3.1-alpha.01 MCP to 99 tools in alpha.02. Older setup files were retained.

| Tool/option | Native operation |
|---|---|
| `solidedge_create_hole` | Ordered Hole via `HoleDataCollection.AddEx` and finite/through-all holes, including counterbore/countersink/internal thread; not an extruded cut |
| `solidedge_list_metric_threads` | Exact metric descriptions, nominal and internal/external minor diameters from configured HoleSizeFile; unknown descriptions reject before machining |
| `solidedge_list_holes` | Diameter, bore/countersink/thread/type/depth/effective bore and feature health |
| `solidedge_pattern_hole` | Native rectangular/circular pattern of one Hole seed, with verified count/spacing |
| `solidedge_chamfer_edges` | Current edge keys and native equal-setback 45° chamfer |
| `solidedge_extrude_profile(drivingDimensions=true)` | Rectangle width/height plus horizontal/vertical relations, or circle driving diameter |
| `solidedge_list_profile_dimensions` / `solidedge_set_profile_dimension` | Read/change exact native variable names, recompute and verify all feature states/value |

Failure removes only newly added hole/chamfer/pattern and attempts recomputation; dimension failure attempts the original value. Restoration failures are explicit. No automatic save; inspect partial state before continuing.

## Limits

Single-model Ordered `.par` only, not Synchronous/multibody. A Hole accepts one centre; repeated positions use Pattern. Multiple Hole2d centres collapsed to one on hardware, so multiple input centres reject before creation.

Threads use standard metric internal cosmetic thread data. Finite thread/hole depths are supported. Pilot diameter must be at least the internal minor and less than nominal diameter, but this check does not qualify a manufacturing drill selection. The source is the configured legacy `.txt` HoleSizeFile, not new JIS/ISO XLSX databases. External/pipe/left-hand threads, modeled thread solids and automatic hole tables are unsupported.

Through holes use the explicit entry plane and positive/negative direction; `planeOffsetMm` follows the reference-plane normal and does not imply symmetric through-all. Patterns have fixed rectangular/circular spacing, 2–256 occurrences including seed. No arbitrary/omitted/compound patterns or dedicated pattern editing in this release. Rectangular axes are plane-local X/Y; circular centres are document 3D coordinates. Duplicate 360° endpoints reject.

Chamfers are equal-setback only. Relist keys after geometry changes. Driving dimensions initially support rectangle width/height and circle diameter, not fully constrained position, arbitrary polygons, angle/chamfer editing or general standalone sketches. Unsupported read types return null `valueMm`; changes reject.

## Example

First verify that reference plane 1 is XY with +Z normal:

```text
solidedge_extrude_profile(expectedDocument=..., shape="rectangle",
  widthMm=100, heightMm=100, depthMm=20, xMm=-50, yMm=-50,
  featureName="base", drivingDimensions=true)

solidedge_create_hole(expectedDocument=..., centersJson='[{"x":0,"y":0}]',
  diameterMm=8, holeType="counterbore", counterboreDiameterMm=16,
  counterboreDepthMm=5, planeOffsetMm=20, direction="negative",
  featureName="mounting-hole")
```

Use the exact returned thread description. The tested table contained `M8`, not an assumed `M8x1.25` alias. Example: 6.8 mm pilot, 15 mm hole, 10 mm thread with `threadDescription="M8"`; zero thread depth means full length. Save CAD/chart settings, close completed temporary files and return to the original assembly.

## Validation

Solid Edge 2026 new fixtures verified Ø8 holes, Ø16×5 counterbores, Ø16/90° countersinks, M8 finite/M6 full threads, unknown-thread guards, 3×2 rectangular and four-at-90° circular patterns, C1 on 12 edges, stale keys and restoration after failed C1000. Driving rectangle 100→120/100→80 and circle Ø40→50 changed actual bounds; circle dimensions survived save/reopen.

Original documents/dirty state/window count were preserved and test parts saved/closed, including diagnosed leftovers. Evidence: `output/machining-native-20261003-12/summary.json`, `results.json`, `output/machining-mcp-regression.txt`. The 99-tool protocol/access regression passed. This tested development binaries, not an automatic update of an installed alpha.01.

API: [HoleData.AddEx](https://support.industrysoftware.automation.siemens.com/trainings/se/107/api/SolidEdgePart~HoleDataCollection~AddEx.html), [rectangular patterns](https://support.industrysoftware.automation.siemens.com/trainings/se/107/api/SolidEdgePart~Patterns~AddByRectangular.html), [circular patterns](https://support.industrysoftware.automation.siemens.com/trainings/se/107/api/SolidEdgePart~Patterns~AddByCircular.html), [length dimensions](https://support.industrysoftware.automation.siemens.com/trainings/se/107/api/SolidEdgeFrameworkSupport~Dimensions~AddLength.html). Calls were checked against installed 2026 type libraries.
