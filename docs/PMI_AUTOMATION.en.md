# PMI on 3D models

<!-- mechcue-docs-language -->
[日本語](PMI_AUTOMATION.md) | [English](PMI_AUTOMATION.en.md) | [Documentation](INDEX.en.md)

Add associated reference dimensions directly to `.par` or `.asm` models without creating a drawing. PMI is stored in the model and shown through dedicated model views. Part PMI appeared in 0.2.0-alpha.11; assembly PMI in the updated 0.3.2-alpha.01.

Enable CAD editing in the tray. Example requests: add overall/hole diameter/position PMI to the open part; add overall and supported circular dimensions to an assembly; list PMI and show the thickness view.

Get `fullName` with `solidedge_get_document` and pass it as `expectedDocument`. Review warnings, values and layout; save explicitly with `solidedge_save_document`. Save a copy first if retaining the original model is required.

| Tool | Function |
|---|---|
| `solidedge_auto_pmi` | Add principal dimensions and PMI views |
| `solidedge_list_pmi` | List values, reference/display status and views |
| `solidedge_show_pmi_view` | Apply a view by a listed name |
| `solidedge_add_pmi_distance` | Add a distance between two assembly components for position, gap or height |

## Automatic dimensions

- `dimensionMode="features"`: axis-aligned overall sizes, closed-circle diameters/centres, orthogonal steps and supported slope angles.
- `overall`: overall dimensions only; cylinder diameter needs `features`.
- `maxDimensions=30`: added-dimension limit, configurable 3–100.
- `createModelViews=true`: creates `MechCue PMI XY`, `XZ`, `YZ` where needed and applies the first.
- `recreateGenerated=false`: when true, recreates generated overall/diameter/hole/step/angle dimensions, returning `removedGenerated`. User PMI and explicit distance PMI remain. This repairs older frozen assembly PMI.

Listed `valueNative` uses metres/radians; creation results use mm/degrees. These are reference dimensions and do not drive geometry.

## Assembly motion and rebuilding

Solid Edge 2026 assembly dimensions created by `AddDimension` can remain frozen when their second referenced component moves, for both real vertices and circular edges. Moving the first reference works. Saving alone does not fix this; reopening or rebuilding from recorded dimension data registers dependencies. Manual Smart Dimensions do not show this issue.

Immediately after creation, MechCue undoes/redoes only that dimension step, checking its key and dimension count. Successful reconstruction returns `added[].motionRebuilt=true`; dimensions then follow motion without `UpdateStatus`/`UpdateAll`. If Undo affected another operation, Redo restores it and a warning with `motionRebuilt=false` is returned.

For frozen/older dimensions, rerun automatic PMI with `recreateGenerated=true`, or explicitly save chart settings with `mechcue_save_document`, save the assembly, reopen with `solidedge_reopen_document`, then reconnect with `solidedge_open_mechcue`. Creation never automatically saves/closes. `motionFollowVerified=false` means actual movement has not yet been verified.

Position/gap/height values change with motion; rigid lengths/widths/thicknesses retain values while their placement follows. Manual PMI is not recreated.

## Distance between components

| Argument | Meaning |
|---|---|
| `fromPart`, `toPart` | One-based top-level number or name; nested paths use `subassembly/part` from the assembly tree |
| `fromPoint`, `toPoint` | `vertex:min,min,max`: nearest actual vertex to the requested bounds corner; `circle:zmin`: centre of the closed circular edge at minimum Z (or use max/another axis) |
| `axis` | World X/Y/Z measurement direction |
| `plane` | `auto` (X/Y→XY, Z→XZ), or XY/XZ/YZ containing the measurement axis |
| `lineOffsetMm` | Zero automatically places successive outer lanes; otherwise signed in-plane offset from the first point |
| `id`, `replace` | Stable MechCue PMI ID; duplicates reject unless `replace=true` |
| `addToModelView` | Register in the plane's MechCue PMI view without applying it |

Tool-tip height from base bottom: `fromPart="base"`, `fromPoint="vertex:min,min,min"`, `toPart="tool"`, `toPoint="circle:zmin"`, `axis="Z"`. Newly created distances are rebuilt immediately and measured against the intended value before receiving an ID.

Linear orientation is set and its value checked. One alternative horizontal/vertical orientation is tried if necessary, returning `orientationFallback=true` on success. If neither matches, the dimension is omitted with a warning. A reported orientation swap for diagonally placed points was not reproduced in final verification and its cause remained unknown.

## Layout, updates and side effects

Linear dimensions lie below/left of each model plane with short dimensions inside and long ones outside. Existing generated dimensions are reused by attributes; user dimensions are not removed/reordered. View membership does not delete original PMI.

Rerunning `solidedge_auto_pmi` after movement/shape changes recalculates existing generated dimensions through native reference-dimension updates and returns `updated`. If references no longer match intent, it warns without replacing/deleting them. Listing alone does not update dimensions.

First PMI access can initialize native PMI storage and mark a model dirty, even for listing. `nativePmiInitialized=true` reports this. MechCue does not clear dirty flags or automatically save. Assembly references are transformed through the occurrence hierarchy, supporting repeated parts/subassemblies, with dimensions added to the root. Component PMI/geometry is not modified or saved, though native annotation/view initialization can mark child display state dirty.

## Scope

Solid Edge 2026, single-body `.par` and `.asm`; standalone sheet metal and multibody parts are excluded. Loaded assembly children may be `.par`/`.psm` solids. Limits: 16 levels, 1000 occurrences, 100000 supported shape elements. Variable parts/subassemblies, simplified representations, assembly-side geometry replacement and scaling stop generation.

Associated vertex/circle bounds are compared with conservative solid bounds. Unsupported curved extrema/oblique overall directions are warned and skipped instead of approximated. Supported shape dimensions may still be created.

Annotations use global XY/XZ/YZ, straight edges and closed circles. Freeform surfaces, ellipses, partial arcs and exhaustive feature recognition are unsupported. Equal circles share a representative diameter per plane; duplicate centre coordinates are reduced. Manufacturing meaning of a hole versus an outer circle is not inferred. Up to eight circle centres per plane, representative steps and one representative slope angle are considered.

No tolerances, GD&T, datums, surface finish, section annotations or 3D PDF. Deleted/replaced edges and stale IDs are not automatically repaired. Failed individual dimensions are deleted and warned; successful ones remain, without whole-model rollback.

## Validation

`scripts/Test-PmiNative.py` creates dedicated hole-plate/cylinder/step/sloped parts and checks native values, references, views, save, deduplication and preserved features. Use running Solid Edge, edit permission and a fresh output directory:

```powershell
python scripts/Test-PmiNative.py --mcp artifacts/0.2.0-alpha.11/mcp/MechCue.Mcp.exe --output artifacts/pmi-test
```

Select the actual built MCP path. `Test-AssemblyPmiNative.py` additionally tests motion, rotation, repeated/nested occurrences, circles, preserved component files/PMI and four drawing views. It records/restores the original model and closes completed test windows. CI without CAD checks tool exposure, permissions, inputs and builds, not native behavior.
