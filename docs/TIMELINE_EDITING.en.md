# Timeline and chart editing

<!-- mechcue-docs-language -->
[日本語](TIMELINE_EDITING.md) | [English](TIMELINE_EDITING.en.md) | [Documentation](INDEX.en.md)

Use **+**, **−** and **Fit** below the chart to zoom or return to the complete time range. The horizontal scrollbar reaches times outside the view, whose limits are shown in seconds. Full/compact switching preserves zoom and scroll.

Ctrl + mouse wheel zooms around the time under the pointer; the ordinary wheel scrolls horizontally. These operations do not change points or CAD poses. A new window starts with the full range. During playback the view advances as the cursor reaches the right edge, including at start, end and loop wrap. While paused you can scroll freely.

| Gesture | Action |
|---|---|
| Drag a point/segment | Change values only |
| Ctrl + drag | Change time and value |
| Alt + segment drag | Move adjoining equal-valued horizontal sections with the segment |
| Shift + drag, or drag blank space | Seek |
| Esc or loss of mouse capture | Cancel the entire pending edit |

Ctrl+Z undoes and Ctrl+Y redoes chart history while the chart is active. Text and number inputs retain their own editing shortcuts. The add-in Edit group also provides Undo and Redo.

Alt dragging includes each uninterrupted run equal to the chosen segment's left or right endpoint, even when the chosen segment slopes. The same displacement preserves all times and slopes; equality is exact. Alt takes precedence over Ctrl, and Shift seeking takes precedence over both.

Undo, Redo and Compact use icons with tooltips and accessible names. A new edit clears redo history. Track deletion and motion-pattern switching clear history. Graph Undo does not restore deleted CAD parts or changed bindings. The track-list delete button is **− Delete**.

Tests use mock CAD and test windows to check absolute seconds under zoom, range limits, zoom anchors, playback following, adjacent horizontal runs, nonaccumulating mouse moves, cancel, scroll controls, view restoration and undo/redo history. Actual Alt+mouse handling inside Solid Edge needs hardware verification after an update.
