# Chart window placement

<!-- mechcue-docs-language -->
[日本語](WINDOW_PLACEMENT.md) | [English](WINDOW_PLACEMENT.en.md) | [Documentation](INDEX.en.md)

With the chart focused, press `Ctrl+Alt+Shift` plus a key below to maximize Solid Edge and place the chart while preserving its current mode and size. Repeating the same key toggles full/compact view at that position. A different key only moves it. Number-key shortcuts have been retired; keys are registered only while the chart is active.

| Left | Centre | Right |
|---|---|---|
| U: top left | I: top centre | O: top right |
| J: middle left | K: centre | L: middle right |
| M: bottom left | ,: bottom centre | .: bottom right |

Use View → maximize CAD and choose position, or **▦** in compact view, for the same placement. Menu actions only move, even when repeated. Placement uses the working area of the Solid Edge monitor, avoiding the taskbar and resizing only if needed to fit. Shortcut entry into compact view uses its minimum size; returning to full restores the prior full size.

Manual position/size/mode changes or MCP/menu placement reset the shortcut sequence. A repeat toggles only while position, size and mode still match the preceding shortcut. Holding a key does not repeatedly toggle.

Position, size, maximized state and compact mode are saved after moving/resizing and on close, separately for add-in and standalone windows. Restoration corrects bounds after monitor removal or resolution changes.

Mode changes stop playback and entering compact commits pending input. Placement alone does not stop playback. Menu, add-in, panel restoration, shortcut and previous-state restoration use the same redraw-suppression path.

Playback checks are ordered **Apply, Collision, Loop, Auto** in both views. View/motion switching, CAD Save, assignment/release, current-value reads, table imports and MCP edits preserve checks while temporarily suspending CAD reflection. The next playback/seek resumes reflection according to Apply. Motion speed is restored per pattern, while current Collision/Loop choices remain. New connections/settings, disconnection, reference restoration and driving errors disable Apply/Auto. Auto re-enables Apply on Play. See [chart controls](CHART_MENUS.en.md).

## MCP

`mechcue_place_window(position=9, sessionId="target-session-id")` places the connected chart at the top right. `position` is 1–9, default 3 (bottom right), using a numeric-keypad layout: 1–3 bottom, 4–6 middle, 7–9 top. Supply `sessionId` for multiple charts. CAD edit permission is required.

MCP placement retains mode/size and repeated calls never toggle mode. It returns position, compact state, CAD maximized state, chart bounds and monitor working area. It does not change CAD poses or save documents.

Tests with windows and mock CAD cover all nine positions, maximization, bounds restoration, Apply retention and native keyboard notifications. Mode changes suspend intermediate drawing with `WM_SETREDRAW`, then redraw at final bounds. Solid Edge's rendering settings are unchanged.
