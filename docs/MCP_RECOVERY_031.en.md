# MCP save and connection recovery in 0.3.1

<!-- mechcue-docs-language -->
[日本語](MCP_RECOVERY_031.md) | [English](MCP_RECOVERY_031.en.md) | [Documentation](INDEX.en.md)

## Document switching

`solidedge_open_document` checks the active document and references before switching. Dirty data returns `document_switch_requires_save` with `mutationStarted:false`, without opening/activating another document. Reselecting the already visible document is permitted.

Save chart changes explicitly with `mechcue_save_document`, referenced parts/subassemblies with `solidedge_save_referenced_document`, and the active parent last with `solidedge_save_document`. Referenced save only saves the specified existing writable reference without opening a window. Dirty descendants must be individually saved before their subassembly. It neither saves unrelated files, changes save paths nor discards changes.

These guards prevent known save prompts during switching, not every native warning/license dialog.

## Missing sessions

`session_not_found`, `session_unavailable` and `session_selection_required` include recovery guidance. Edits are never automatically sent to another window. Relist sessions, reread the active document and reconnect through `solidedge_open_mechcue` if needed. Saved settings can reload; unsaved settings from a closed window are not guaranteed recoverable.

## After updating

Setup stops old MCP/tray programs within its installation and suppresses restart during update. Restart the MCP connection in the AI client afterwards, or restart that client if no reconnect action exists. MechCue cannot automatically restore the client's connection. Setup completion, the guide and capabilities describe recovery. Check `releaseVersion`, permissions and sessions before continuing, without blindly repeating interrupted edits.

Common build checks include app self-tests, MCP protocol/permissions/isolation, update shutdown/new connection initialization, COM declarations, setup versions and hashes. `scripts/Test-DocumentSwitchNative.py` uses new hardware test files to check dirty-switch rejection, explicit saves, reference subassembly saves, rejection of unrelated saves and preservation of original document/window count. Save user documents before running. CAD models/native logs are excluded from public source.
