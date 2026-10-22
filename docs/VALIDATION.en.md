# Validation before publication

<!-- mechcue-docs-language -->
[日本語](VALIDATION.md) | [English](VALIDATION.en.md) | [Documentation](INDEX.en.md)

This page records checks at their original dates/releases. An earlier outstanding check is not silently promoted to success by later software changes. Native tests require Solid Edge; CI does not substitute for them. Use model copies and avoid connecting add-in and standalone simultaneously.

## Baseline and hardware checklist

Automatic checks cover interpolation/units/XYZ/rotation, drag beyond bounds/cancel, lost connections, individual release/reassignment, UI startup, COM GUID/method order and packages. Minimal COM signatures were compared with Solid Edge 2026 definitions locally.

On 2026-09-30 a user verified installation, basic add-in operation and Windows Apps uninstall. A company PC also downloaded/installed Inno Setup and ran basic assembly motion; Inno uninstall remained pending in that entry. Detailed methods/axes were not all verified.

Review on hardware: new-install ribbon presence; connection/candidates/highlight/distance driving/restoration; angle/free-part XYZ driving; correct updated registration paths; shared JSON; reconnect after CAD restart; and uninstall removing registration while retaining user graphs.

## Early alpha checks

| Release | Automated/native evidence and outstanding checks |
|---|---|
| alpha.6 | Absolute XYZ/relative readings/mm-degrees, read-all-values/Undo, grounding suppression/restoration, other-constraint guards and collision/possible/incomplete/API-error stops with previous-pose restore were mock-tested. Native grounded axes, obstacle stop, initial collision, disabled checking and subassembly names needed checks |
| alpha.7 | Grounding plus other constraints allowed for coordinate registration; mocks verify no other constraint state changes and restoration. Native related-part behavior still needed review |
| alpha.8 | Dual axes, edit only selected overlay, hidden-track refusal/scaling exclusion, constant height with many tracks, visibility and retained checks |
| alpha.9 | Native VARIANT/SAFEARRAY test verifies VT_DISPATCH occurrence arrays. Actual clear/collision results needed native recheck |
| alpha.10 | Time-fixed ordinary drag, Ctrl point/segment movement, duration preservation, constant-chart margins and compiled16/32 px resources; ribbon appearance needed hardware review |
| alpha.11 | Independent checks/selection, .1 increments and compact controls/panel restoration |
| alpha.12 | End restart, resume, Japanese/English labels/hints/kinds, internal kind retention and Auto language; English preview generated. Native ribbon language switching remained a check |

## Persistence and tables (2026-10-01)

Alpha.13 native API tests verified UTF-8 JSON AddInsStorage, BeforeDocumentSave, constraint GUID restore after reopen,1 mm driving, SaveCopyAs restore, new grounded-part keys/attributes,10 mm coordinate drive and reference/ground restoration. Only tutorial copies/new fixtures were saved. Automation also checked native IStorage, renamed/reordered objects, no movement on load/undriven exit, missing/duplicate IDs, unresolved binding retention, read-only/bad-format refusal and reconnect UI restoration. Actual Ctrl+S, installed UI, Save As and CAD-restart persistence remained user checks; see [user guide](USER_GUIDE.en.md).

Alpha.14 tested CSV/XLSX precision, Japanese/quotes/newlines/formula-like names, stable-ID roundtrips, invalid rows, shared strings/cached formula results, connected target/unlisted-track retention and preflight kind-change rejection. Independent XLSX reading confirmed compatibility. Native copies checked candidate-selection preservation, clearing selection, persistence and coordinates. Seven command bitmap sets were inspected; installed ribbon clicks/appearance remained native checks.

## Ribbon, MCP and UI alphas

Alpha.15 addressed AddCommandBarButton range errors. Historical registration added explicit IDs/individual bitmap sets, handled local/runtime IDs and requested GuiVersion6 rebuild. Six live API commands accepted registration/large style.

Alpha.16 checked official C# SDK initialization,13 tools, real standalone named pipes, ID/number point edits, invalid input, resampling/bulk Undo/reset/seek/play/stop, session isolation and direct-CAD opt-in. Native document/part/variable reads, document mismatch and selection on a test assembly passed. Updated add-in pipe use still needed installation verification.

Alpha.17 package tests checked selectable MCP, dependencies/licenses/config/HTML guide and absence of standalone EXE in add-in-only setup. Actual installation/selection/start-menu/removal remained user checks.

Alpha.18 replaced duplicate registration with unique GUID/local names and SetAddInInfoEx runtime IDs. GuiVersion7 rebuilt ribbon; seven live IDs62004–62010 and large styles succeeded. Bitmap resources/version titles including compact were tested; native appearance/persistence still needed review.

Alpha.19 added ResizeRedraw, icon/tooltips, short compact labels and measured minimum width for language/DPI. Smoke tests checked Japanese/English toolbar fit and document dispatch. Add-in windows stop/stage/disconnect/dispose on target close/switch; standalone keeps its behavior. Actual close/switch and resize visuals remained native checks.

Alpha.20 defaulted local same-user AI on, toggled Play/Pause, retained time, removed redundant Stop (MCP remains), grouped Data and introduced8 px splitters/hide/restore panels with retained layouts. GUI version8 registered34 commands in six groups, with35 bitmap sets plus Pause. Check styles/state constants, languages/layout/panels/AI/MCP were tested; native ribbon states/icons still needed post-install validation.

Alpha.21 fixed FirstDisplayedScrollingRowIndex when a numeric grid had only header space: skip scroll, retain selection. Hidden/compact/tiny tests passed. Local JSONL logs include version/layout/options/actions/exceptions, rotate about5 MB and retain20 files; logging failure does not block edits.

Alpha.22 changed visible magenta backgrounds/fringes to white, bumped GuiVersion9 and checked no pure magenta remains in color resources. Native appearance after upgrade still needed review; includes alpha.21 fixes.

## 0.2.0 native CAD MCP (2026-10-01)

Dedicated new documents verified ordered rectangle/circle/polygon, cuts, signed/symmetric/additive extrusion, native OK states, new saves/overwrite/document guards, assembly XYZ/Z rotation/grounding/planar mates and solved state. `ground=false` undoes native automatic grounding. Part/assembly drawing views, link reuse/update/DFT/PDF and actual write-enabled stdio creation through save passed.

CI checked25 tools/read annotations/permissions/chart regression, not native creation. At this stage manufacturing dimensions/tolerances, arbitrary constraints, revolve/loft and bulk CAD Undo were unsupported. Alpha releases remained drafts.

Alpha.2 checked three tray modes, shared live mode changes, checks/persistence/corrupt-file fallback using isolated settings and no user tray. Three graph modes, selected-unchecked visibility, All-mode check retention, persistence, Japanese/English and compact were checked. Installed tray/start-menu behavior remained native review.

Alpha.3 checked four concept templates, hierarchy/rotation centres, tilted-child rotation, missing/out-of-range/cycle guards and29-tool protocol/catalog/access. Native tests verified dimensions, ASM persistence, linked poses and no mutation on bad targets/ranges; actual stdio generated/read/posed mill5. Grounding-only concept constraints were intentional; automatic chart import was then pending.

Alpha.4 tested measured-axis import without moving CAD, deduplication, graph driving/range rejection, AddInsStorage axis definitions/IDs/reference restore for all four templates. The three-axis fixture also exercised AI edits/Apply/seek/CAD Save. CI covered30 tools and disconnected-import rejection.

For later detailed validation, see [0.3.2](releases/0.3.2.en.md), [0.3.3 development](MCP_033_DEVELOPMENT.en.md), [continuous inspection](CONTINUOUS_MOTION_INSPECTION.en.md) and the [documentation index](INDEX.en.md). Those dated records distinguish tested software from installed hardware checks.
