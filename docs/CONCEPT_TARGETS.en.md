# Target applications and concept-model MCP

<!-- mechcue-docs-language -->
[日本語](CONCEPT_TARGETS.md) | [English](CONCEPT_TARGETS.en.md) | [Documentation](INDEX.en.md)

## Targets

| Application | Study | Priority |
|---|---|---|
| Vertical machine tools/machining centres | XYZ feed, rotation/tilt, moving parts versus workpiece | 1 |
| Gantries/Cartesian transfer | Travel envelope, axis hierarchy, transfer order | 2 |
| Assembly/press-fit/inspection | Clamp, lift and slide synchronization | Next |
| Packaging/filling | Transfer, opening/closing and pushing timing | Next |
| Index tables | Intermittent rotation and process synchronization | Next |
| Pallet/tool changers | Exchange, translation, rotation and retraction | Next |
| Press/die opening | Vertical movement, opening and extraction | Next |
| Articulated robot cells | Joint/peripheral synchronization | Future: inverse kinematics and more |

Priorities reflect current functionality and development efficiency, not market size. [DMG MORI](https://us.dmgmori.com/products/machines/milling/vertical-milling) and [Festo](https://www.festo.com/us/en/c/products/handling-systems-and-industrial-robots/cartesian-robots-id_pim108) illustrate categories; generated models are original simplified layouts, not copies of those products.

## Tools and templates

| Tool | Function |
|---|---|
| `solidedge_list_concept_templates` | List templates/axes without CAD |
| `solidedge_create_concept_machine` | Generate/save ordered parts, an assembly and axis manifest in a new folder |
| `solidedge_get_concept_machine` | Read axis types, hierarchy, ranges and actual placements |
| `solidedge_set_concept_pose` | Set all axes together, including child components |

- `mill3`: Y saddle → X table → workpiece, with independent Z spindle/tool.
- `mill4`: adds a C rotary table between X table and workpiece.
- `mill5`: X table → A tilt → C table → workpiece; both rotary axes are table-side.
- `gantry`: fixed Y rails → Y beam → X carriage → Z slide, with four posts, two Y rails, open beam/guides, simplified gripper and fixed workpiece. Alpha.7 fixed the beam following X; existing models are not automatically changed.
- `arm5`: table-mounted five-axis robot, available from 0.3.2-alpha.06.
- `arm5p`: arm5 plus finger slides and six free-body workpiece axes for rigid-body physics.

A rotates about local X and C about local Z in the parent coordinate system. Identical axis counts can describe different structures; this is not a spindle-tilt design. See the source study's [C-axis reference](https://www.haascnc.com/service/online-operator-s-manuals/mill-operator-s-manual/mill-spindle-c-axis-drive-.html).

## Parametric parts

Since 0.3.2-alpha.04, each part uses one constrained sketch extruded symmetrically about the first global reference plane. Orthogonal global planes are projected as reference lines. Rectangles have horizontal/vertical and symmetric constraints; cylinder centres lie on both reference lines. Width/depth or diameter are driving dimensions, preserving the origin when resized.

Box variables are `SizeX`, `SizeY`, `SizeZ`; cylinders use `Diameter`, `Length`. Change them with `solidedge_set_variables` on the part document. This does not update manifest `sizeMm` automatically.

Mill columns leave 20 mm clearance at maximum Y saddle travel; the base extends to the rear of the column. Generation checks all linear-travel endpoint combinations with rotary axes at zero. Older mill models through alpha.03 initially intersected the column and saddle.

## Generate and pose

Start Solid Edge and allow CAD creation/editing in the tray. The parent output folder must exist; the output folder itself must be new.

```json
{
  "type": "mill5",
  "outputDirectory": "C:\\CAD\\ConceptMill5",
  "xTravelMm": 500,
  "yTravelMm": 300,
  "zTravelMm": 300
}
```

Pass this to `solidedge_create_concept_machine`. Optional `partTemplate` and `assemblyTemplate` are absolute paths to empty templates; otherwise Solid Edge defaults are used. Retain returned `fullName` and `manifestPath`:

```json
{
  "expectedDocument": "C:\\CAD\\ConceptMill5\\concept.asm",
  "manifestPath": "C:\\CAD\\ConceptMill5\\mechcue-concept.json",
  "valuesJson": "{\"X\":100,\"Y\":50,\"Z\":30,\"A\":45,\"C\":90}"
}
```

Pose values are absolute, not increments. Supply every axis: linear mm, rotary degrees. Zero is the reference pose. Linear ranges are ±half travel, A ±110°, C ±360°. Displayed home values are not measured current positions; `actualMatrix` is a column-major 4×4 matrix with metre translations.

Part names and referenced files guard identity. Move assembly, parts and `mechcue-concept.json` together. Renamed/replaced references cause rejection. Pose changes do not save CAD; explicitly use `solidedge_save_document`. Invalid ranges/targets are rejected before moving. Solver rejection triggers an attempted rollback; failed restorations identify the affected parts.

Parts are grounded and linked through forward kinematics in this feature, not a CAD mechanism solver. Additional active constraints other than grounding cause rejection before driving. This restriction is specific to concept axes, not ordinary component-coordinate driving.

## Import into the chart

Connect MechCue to the generated assembly and choose Data → Import concept axes, or call `mechcue_import_concept_axes(manifestPath, sessionId)`. The UI finds a sibling manifest or asks for one.

Axes initialize as constant graphs measured from actual placements, without moving CAD. Playback/Apply starts off. Unbound graphs remain; reimporting the same definition preserves edited points and stable IDs without duplication. Release ordinary bindings first. `unboundTracks="hide"` can hide unbound samples without deleting their data; default is `preserve`.

Edit graphs, enable Apply and play/seek. All axis values are evaluated together. Out-of-range values are rejected before motion. Interference mode uses discrete checks and restores the previous pose when stopped; simplified contacts may stop the initial pose.

CAD Save embeds graphs, manifest, axis IDs and registered reference values, returning to that reference pose. Reconnection restores assignments with Apply off and needs no external JSON, though CAD part files remain necessary. Mismatches retain settings and request reassignment.

Angle measurement cannot distinguish full turns; import chooses representative values within ±180°. Incompatible manual changes cause rejection. Axis method/direction is defined by the manifest and cannot be edited in the UI. Individual concept-axis release is unsupported; disconnect releases the set.

## Robot axes and carrying

| Axis | Motion | Range |
|---|---|---|
| J1 | Vertical-axis swivel | ±170° |
| J2 | Shoulder | ±90° |
| J3 | Elbow | ±135° |
| J4 | Wrist bend | ±120° |
| J5 | Wrist roll | ±180° |
| WX/WY/WZ in arm5 | World workpiece translation; zero is its table position | X −700…200 mm, Y ±300 mm, Z 0…500 mm |

The model contains a tabletop/four legs, base, swivel, shoulder, upper arm, forearm, wrist, hand, two fingers and a 60 mm cube. Adjacent links are offset beside each other to avoid passing through on bending. Travel arguments do not apply to `arm5` generation.

Arm5 import creates joint rotation and workpiece translation tracks. It does not automatically attach a gripped object: carrying needs dense WX/WY/WZ points computed from the hand pose. With the hand pointing down, matching J5 to J1 cancels swivel-induced finger rotation when keeping workpiece orientation fixed. Arm5 has no finger-opening axis or inverse kinematics.

## Rigid-body physics

Use `arm5p` and `solidedge_simulate_concept_rigid_body` for physical carrying. The old geometric `solidedge_simulate_concept_physics` was removed in 0.3.3-alpha.03.

Arm5p adds F1/F2 slides (negative F1 and positive F2 close the fingers), and workpiece WX/WY/WZ/WRZ/WRY/WRX. BepuPhysics treats fixed parts as static, joints/hand/fingers as commanded moving bodies and the workpiece as a dynamic body with mass/friction. The tool can compute six-axis workpiece points without a Solid Edge connection.

`physicsJson` sets mass, friction, restitution, gravity, `floorZMm` and contact stiffness `contactHz` (default 120 Hz). Without `gripForceN`, fingers follow commanded positions and penetration/contact stiffness determines pressing. With `gripForceN`, each finger closes toward its target under that per-finger force limit and stops against the workpiece.

The chart exposes six read-only dotted physics tracks, automatically recomputing after joint edits, with manual recompute and Auto controls. Computation is isolated in `MechCue.Physics.exe`. The abstraction is `IRigidBodyEngine`; current calculations use BepuPhysics. The recorded version is 2.5.0-beta.33 because 2.4.0's effective friction was about one-quarter of the specified coefficient. See [development](MCP_033_DEVELOPMENT.en.md) and [open issues](OPEN_ISSUES.en.md).

Collision shapes are boxes/cylinders, not actual CAD contact geometry. Axis values are machine axes, not tool-tip/work coordinates. Tool compensation, TCP, inverse kinematics, NC/G-code, cutting/stiffness calculations and arbitrary real-assembly physics are outside scope.

## Contact policy and validation

Concept models have intentional zero-clearance sliding surfaces. Before `check_motion`, get occurrence keys with `mechcue_list_inspection_parts` and register only justified contacts in `mechcue_set_inspection_policy` → `AllowedContacts`. Otherwise continuous inspection may keep subdividing because it cannot prove separation at zero gap.

For mill3 with 200 mm Y travel, allowing base–saddle and saddle–table completed a six-second all-endpoint motion check in under ten seconds. Allowed pairs are excluded from collision checks; reasons must reflect actual acceptable contact. Robot examples include base–swivel, shoulder–upper arm, upper arm–forearm, forearm–wrist, wrist–hand and workpiece–table, subject to the actual model/policy.

Self-tests cover template hierarchy, rotation centres, child rotation after tilt and rejection of range errors, missing axes and cycles. Native validation:

```powershell
MechCue.exe --concept-integration-test C:\CAD\FreshConceptTest
```

Uses local Solid Edge 2026 ISO Metric templates, creates/closes only new test documents, restores the original document and writes `result.txt`. Concept models can also be used with [drawing](DRAWING_AUTOMATION.en.md) tools; this does not supply complete manufacturing tolerances/dimensioning.
