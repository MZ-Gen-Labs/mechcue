# Fix for unresponsive add-in windows opened through MCP

<!-- mechcue-docs-language -->
[日本語](MCP_UI_DISPATCH_FIX_20261003.md) | [English](MCP_UI_DISPATCH_FIX_20261003.en.md) | [Documentation](INDEX.en.md)

## Observation and diagnosis

On 2026-10-03, MCP successfully created/saved a mill5 assembly. `solidedge_open_mechcue` returned a session, but import/state/pattern requests timed out while ping and CAD reads still worked. Add-in logs showed window creation but no edit dispatch; later enumeration found Solid Edge but no MechCue top-level window. This was consistent with a WinForms window created on a temporary external COM thread without a continuing message loop. No native thread stack was captured, so that mechanism was an inference from evidence.

## Fix

`OnConnection` creates a host STA dispatcher. External COM `OpenChart` marshals creation/display/connection onto that host thread. Logs record connection/open thread IDs and apartments for verification.

Ping adds `uiResponsive`, a read-only UI-thread probe with a one-second deadline, while retaining `reachable` and `uiReady`. Communication can respond with false while CAD occupies the UI.

## Validation

An STA message-loop test requests creation from a temporary MTA caller and verifies responsiveness after the caller exits. A deliberately blocked UI returns false and then true after recovery. Common app/UI, 85-tool protocol, traffic/update, COM/ribbon and setup tests passed. Local version remained 0.3.0-alpha.08, with no GitHub/tag/release changes.

After the first fixed installation, hardware MCP opening, responsive health, state, five-axis import and five-pattern editing succeeded. A delayed `Shown` event then attempted a second connection, displayed a dialog and cleared `documentReady`, breaking saving. The follow-up skips Shown reconnection when already connected; regression checks retain readiness and successful saves.

After reinstalling the follow-up, Solid Edge 2026 verified open/import/five-pattern editing, completed asynchronous CAD Save, close/reload, identical settings JSON, restored five axes/patterns and responsive UI without a duplicate dialog. Four ordinary saved patterns passed 108 sampled poses with restoration; this was not a universal continuous-motion proof.

Historical outputs: `artifacts/ui-dispatch-fix-20261003/release`; final double-connect fix: `artifacts/ui-startup-fix-20261003/release`.
