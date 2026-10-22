# MCP workflow fixes from a transfer-machine study — 2026-10-02

<!-- mechcue-docs-language -->
[日本語](MCP_WORKFLOW_FIXES_20261002.md) | [English](MCP_WORKFLOW_FIXES_20261002.en.md) | [Documentation](INDEX.en.md)

This historical development prioritized operations previously requiring direct API work and Solid Edge lifecycle handling. It did not resolve every gap, change versions, install or publish. Local source study: `output/Gantry-MCP-study-20261002-1900/`.

## Tools (70 → 85)

| Tools | Behavior and limits |
|---|---|
| `solidedge_get_application_state` | Process ID, all documents/dirty state and readiness; process existence alone is not ready; definitive license-wait diagnosis unsupported |
| `solidedge_start_application` | Launch registered executable or reuse existing instance; prevent duplicate pending launches and poll state |
| `solidedge_exit_application` | Require exact process ID and no dirty documents, request normal exit; never force, discard or save |
| `solidedge_open_mechcue` | Open/connect chart through installed updated add-in COM and return session ID |
| `mechcue_get_session_health` | Probe actual pipe/UI/protocol, distinguishing stale records |
| `mechcue_start_operation` / `mechcue_get_operation` | Async save/migration/path-check completion, UUID deduplication, up to 128 session operations |
| `mechcue_set_keyframes` / `mechcue_delete_keyframe` | Bulk multiaxis edits/time deletion, validated together with one Undo |
| `solidedge_position_nested_part` | Resolve keys within each parent document, change local pose, verify and restore on failure |
| `mechcue_bind_nested_part` | Additional nested driving such as gripper fingers alongside concept XYZ, using unbound tracks and persisted keys/reference poses |
| `solidedge_set_variables` | Batch exact-name changes with mm/degree conversion, verification and restoration |
| `solidedge_get_custom_properties` / `solidedge_set_custom_property` | Read/write string Custom properties |
| `solidedge_get_bom` | File-level counts/keys through nested structure; explicit incomplete trees, not manufacturing/material/mass BOM |

## Existing behavior fixes

CAD operation lifetime is separate from connection-read deadlines, with old synchronous save wait extended to 120 s. Separate pipe connections can query health/operations during UI-thread saving. Concrete exceptions reach MCP and AI save failures no longer block on confirmation dialogs.

Nested keys resolve in their parent document, avoiding equal-key collisions across documents. Nested bindings use settings Version 3 so older apps reject rather than misdrive. Video/path checks restore and verify nested poses and concept state. Already loaded child documents are reused instead of reopened; the observed reopening hang motivated this fix, whose same-path hardware retest was then incomplete. Gantry hierarchy text was corrected to Y→X→Z.

## Workflow

Start in edit mode and poll until `ready`/`ready_no_document`. Open the assembly and obtain an explicit session with `solidedge_open_mechcue`. Start async `save_document` with document path, a new UUID `requestId` and session. Poll until completed; after a timeout retry with the same UUID and arguments, and inspect the real error on failure. IDs do not transfer between sessions.

The original `check_motion` accepted start/end/step, at most 501 poses, explicitly moved CAD and restored its starting pose. That release provided sampled interference, not continuous/clearance proof. See later [continuous inspection](CONTINUOUS_MOTION_INSPECTION.en.md).

Normal exit requires saving according to user intent, using the exact process ID and confirming later process state; an exit request is not proof of termination.

Nested driving changes the child assembly file and therefore every reference to it, requiring `modifySharedSubassembly=true`. Flexible-instance-specific poses are unsupported. Save modified child files explicitly; saving root chart settings does not save descendants.

## Validation and gaps at the time

App/MCP/add-in Release builds had zero warnings/errors. Self-tests checked 11 s saves, parallel health queries, UUID deduplication, retained errors, nested persistence/restoration, invalid bulk input and Undo. Protocol tests checked 85 tools/access/isolation; COM/ribbon checks passed.

Hardware verified instance reuse, mismatched-PID and dirty-exit rejection, extrusion 10→12 mm, Custom properties, nested X −50→−60 mm and two-instance BOM. Evidence: `output/Workflow-fixes-native-20261002-2/results.json`. The test stopped on its final child reopen, so it was not an end-to-end success; original-window restoration/child save completion remained unconfirmed. Fresh launch/normal exit, updated OpenChart and native nested playback/save/reload/path checks required later installed validation.

Remaining priorities were automatic detail grouping/migration, grip/release following, trapezoidal/S-curve motion, contact/clearance/continuous inspection, dedicated machining, mass/inertia/manufacturing BOM, real-face constraints/DOF, production drawings, detailed license/modal diagnostics and unified error codes. Several were later addressed; consult later dated records. Adding API operations did not complete automated production-ready detailed design.

Unit references: [UnitTypeConstants](https://support.industrysoftware.automation.siemens.com/trainings/se/106/api/SolidEdgeConstants~UnitTypeConstants.html), [native units/conversion](https://support.industrysoftware.automation.siemens.com/trainings/se/106/api/UnitsOfMeasure.html).
