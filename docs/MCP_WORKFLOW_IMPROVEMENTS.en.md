# Concept-to-detail MCP workflow improvements

<!-- mechcue-docs-language -->
[日本語](MCP_WORKFLOW_IMPROVEMENTS.md) | [English](MCP_WORKFLOW_IMPROVEMENTS.en.md) | [Documentation](INDEX.en.md)

Source changes recorded on 2026-10-02, following installed-version testing. This record did not include version changes, installer creation or updating the installed app.

| Problem | Resolution |
|---|---|
| No native report for zero interference | Create a MechCue summary only for definitively clear analysis; return source/created flags. Missing other reports remain warnings, not clear |
| Duplicate feature name fails after geometry creation | Check names before mutation; post-creation failures state partial success and require inspection before retry |
| No bulk concept-to-detail migration/save/reopen | Add saved-settings read, add-in migration/explicit save and clean-only reopen |
| Sample graphs remain after axis import | Optional hiding of unbound tracks, without deleting data or hiding assigned/unresolved tracks |
| View commands can change dirty state | Return before/after dirty state without automatic saving |

Migration requires identical axis IDs/order/hierarchy/type/direction/ranges/origins. The detailed manifest uniquely targets direct parts or rigid subassemblies in the original reference pose. Graph/pattern IDs, all points and source reference axis values remain. Existing destination settings/bindings, ordinary source CAD bindings and out-of-range values reject. Migration loads the UI and needs explicit save. Restoration warnings report partial success to discourage unsafe retries.

Application/MCP/add-in builds passed. Automated tests covered clear summaries, unknown/incomplete handling, duplicate names, migration IDs/patterns/keys and mismatch/range rejection. Mock CAD/UI integration verified hidden unbound samples, unchanged reimport, four-axis restoration, multiple patterns, duplicate migration rejection and embedded save. The historical stdio suite checked 70 tools, read attributes, permissions, isolation and prior editing/pattern behavior.

Hardware work was incomplete because Solid Edge was not running (`MK_E_UNAVAILABLE`). `scripts/Test-McpWorkflowFixes.py` was prepared for dedicated copies; native name rejection, clear reports, display dirty state and reopen still required checking at this point.

New migration/save/import options require both updated MCP and updated MechCue add-in; an old add-in cannot handle the requests. Restart the MCP connection after updating. See [MCP guide](../Mcp/README.en.md).
