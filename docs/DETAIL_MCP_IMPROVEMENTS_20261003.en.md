# Detailed-design MCP improvements — 2026-10-03

<!-- mechcue-docs-language -->
[日本語](DETAIL_MCP_IMPROVEMENTS_20261003.md) | [English](DETAIL_MCP_IMPROVEMENTS_20261003.en.md) | [Documentation](INDEX.en.md)

Development record following the five-axis study in `output/Mill5-detail-study-20261003-1007/`. Six tools increased the historical count from 86 to 92. No version change, installed deployment or release was performed in this work.

| Area | Implementation and limits |
|---|---|
| Detail assemblies | `solidedge_create_detail_assembly` independently copies a saved flat concept assembly's `.par` parts into axis-based/custom groups, converts local coordinates and verifies world placements, hierarchy and reference pose. Each group must share a driving parent axis. Motion settings require separate migration. Partial failures report destination/completed files |
| Existing extrusion profiles | `solidedge_edit_extrusion_profile` changes undimensioned rectangle width/height or circle radius, preserving centre/plane/depth, recomputing and checking downstream health. It attempts restoration on failure. Single-model ordered parts only; use variables for dimension-driven sketches |
| Rounds and edge identity | `solidedge_list_edges` returns keys/bounds; `solidedge_round_edges` creates native rounds, rejects stale keys and checks every feature. Failed new rounds are removed/recomputed, with restoration failures reported. Relist keys after geometry changes |
| Reference-plane frames | `solidedge_list_planes` adds origin in mm, normal and local X/Y in document coordinates; assembly-world conversion is separate |
| Connection diagnostics | `mechcue_get_capabilities` returns running version/path/access/source without CAD permission. It does not change permissions or provide a generic resumable batch |
| Closing completed work | `solidedge_close_document`, initialization instructions and application `windowCount` support orderly cleanup. Unsaved chart edits require separate save; closing a window does not guarantee unloading a referenced document |

## Document lifecycle

Record the original working document and pre-existing user windows. For temporary parts/subassemblies opened during assembly work, finish edits, verify health and save; close them when no longer needed using `solidedge_close_document(expectedDocument, returnDocument)` and return to the working assembly. Do not close pre-existing user documents without authorization, discard changes, save unrelated documents in bulk or quit Solid Edge as cleanup. Save modified chart settings first.

Closure accepts an existing saved file guarded by active document. Dirty targets/descendants reject; the return document must already be open and different. It verifies the target window closed and reports whether references keep it loaded. It never saves/discards. Detail generation saves/closes its generated subassemblies and leaves the final assembly open; partial dirty work remains for inspection.

## Validation and remaining work

Development build passed with zero warnings/errors. Self-tests covered four machine groupings, A/C coordinate transforms and invalid/missing/duplicate/cross-axis groups, paths and filename collisions. The 92-tool MCP regression covered instructions, read attributes, edit permissions, chart save and session isolation.

Solid Edge 2026 verified rectangle 20×30→40×50 mm, radius 10→12 mm, R1 on 12 edges, plane frames, stale-key rejection, dirty-close rejection and saved-close restoration. A new five-axis fixture had six groups/ten leaf parts; world placement matched the source at X100/Y−50/Z100/A65/C90. Generated module windows closed and original dirty state/window count remained. Evidence: `output/detail-tools-native-20261003-01/summary.json`, `results.json`, and `output/detail-tools-mcp-regression.txt`.

At this date, dedicated holes/threads/counterbores/patterns/hole tables, chamfers, dimensioned sketches, real-face/cylindrical constraints and DOF, contact/clearance/continuous checks, assembly mass/inertia/loads and resumable generic jobs remained. Subsequent pages describe later additions. Use updated MCP binaries and reconnect; source edits do not update an already running server.
