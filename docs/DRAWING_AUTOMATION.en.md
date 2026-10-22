# Automatic drawings and principal dimensions

<!-- mechcue-docs-language -->
[日本語](DRAWING_AUTOMATION.md) | [English](DRAWING_AUTOMATION.en.md) | [Documentation](INDEX.en.md)

Since 0.2.0-alpha.8, MCP can create drawings from saved 3D models: concept assemblies, boxes, plates, simple cylinders, steps and circular-hole parts. `solidedge_auto_drawing` supports `.par`, `.psm` and `.asm`. Save and activate the model, pass its path as `expectedDocument` and a fresh `.dft` path as `outputPath`. For a view in an existing drawing use `solidedge_add_drawing_view`.

| Tool | Purpose |
|---|---|
| `solidedge_plan_drawing` | Read-only front candidates, reasons, bounds and recommended views |
| `solidedge_list_drawing_views` | Active-sheet directions/scales/model links and dimension values/status |
| `solidedge_auto_drawing` | Create and save a new drawing from the saved active model |
| `solidedge_complete_drawing` | Add missing projected/isometric views and dimensions around a chosen front; save separately |
| `solidedge_dimension_drawing_view` | Add principal dimensions to a chosen view; save separately |

`frontOrientation`: `auto/front/back/top/bottom/right/left` in model coordinates. An automatically selected top direction becomes the drawing front. Selection prefers an existing standard view, concept-model front (−Y), or a cylinder axis/projected area for generic parts; generic assemblies use bounds. Geometry cannot determine design intent, so specify a direction when needed.

Top and right projected views use third-angle projection. A simple cylinder viewed axially omits the redundant right view. Set `includeIsometric=false` to omit the isometric view. New drawings fit scale/layout to paper; isometric may use a smaller scale. Completion retains existing layout and warns about sheet boundaries.

## Dimensions

- `overall`: front width/height and projected depth; circular ends use diameter.
- `features`: adds supported projected-circle diameters, centre positions from edges and principal straight steps.
- `none`: views only.

These are native reference dimensions associated with projected lines/circles, not overwritten numeric labels. Same-diameter circles share a dimension per view; up to eight centre positions and representative steps are selected. Repeated step dimensions are reduced between views. Generated IDs prevent duplication on rerun; manually created dimensions are not generally deduplicated.

Dimension lanes lie outside geometry and diameter text is adjusted. Complex geometry, fonts and company templates may require manual layout. Unidentifiable edges or mismatched measured values are omitted with `warnings`. Check references after edge deletion/splitting.

Section/detail-view selection, complete internal-feature dimensioning, GD&T, fits, surface finish and manufacturing datum decisions are outside scope. Review output as concept drawings.

## Usage and failure handling

Example requests: “Create a principal-dimension drawing of the open gantry”; “Use the right direction as front”; “Complete views and dimensions using drawing view 1 as front.”

Use a new absolute `outputPath`; optional `templatePath` should be a DFT template without model views, preferably A3 landscape or larger. Unsaved changes in the 3D model stop creation rather than silently saving it. A failed creation leaves the partial drawing open for inspection. Creation requires CAD edit permission; planning/listing supports read mode.

## Placement and duplicate positions

Linear dimensions are arranged per view/direction, shortest nearest geometry and longest outermost. Horizontal dimensions go below and vertical ones left, starting at 9 mm with 7 mm lane spacing on the sheet. Diameter placement remains.

`solidedge_arrange_drawing_dimensions(expectedDocument, viewNumber=0)` rearranges generated dimensions without adding or saving; zero means all active-sheet views. Values/associations and manual dimensions are preserved. Ambiguous ownership in older overlapping views is warned and left unchanged. This reduces crossings for nested dimensions but is not general annotation collision avoidance.

Since 0.2.0-alpha.12, equal hole coordinates share a position dimension only for the same view, direction and datum. Five holes with Y values 20/70 produce one 20 and one 70 Y dimension. X=20 and Y=20, different datums or different views remain distinct. Limits still apply.

Arrangement removes old generated circle-position duplicates only when reference point, direction and actual value agree, returning `removedDuplicates`. Manual, diameter, overall and step dimensions are retained. Unknown datum/view ownership is warned and preserved. Save explicitly afterwards.

## Validation

Build tests cover direction selection, omitted views, scale, inputs and MCP. Native validation with Solid Edge 2026 and edit access:

```powershell
python scripts/Test-DrawingNative.py --mcp artifacts/0.2.0-alpha.8/mcp/MechCue.Mcp.exe --output C:/CAD/FreshDrawingTest
```

This historical command uses a versioned example; select your built MCP path. Tests generate separate parts/drawings and check values, cylinder views, deduplication, completion and assembly updates, writing `result.json`. The original test leaves documents open for review; finish/close only test documents and restore the original context afterwards.

API references: [DrawingView](https://support.industrysoftware.automation.siemens.com/trainings/se/107/api/SolidEdgeDraft~DrawingView_members.html), [Dimensions](https://support.industrysoftware.automation.siemens.com/trainings/se/107/api/SolidEdgeFrameworkSupport~Dimensions_members.html). Arguments/constants were checked against installed Solid Edge 2026 definitions.
