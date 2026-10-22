# Chart controls

<!-- mechcue-docs-language -->
[日本語](CHART_MENUS.md) | [English](CHART_MENUS.en.md) | [Documentation](INDEX.en.md)

Playback, time, speed, Apply, Collision, Loop, Auto, Edit/View, Undo, Redo and Compact are directly available at the top. Full view keeps the toolbar on one line with narrower menu buttons. View is the former motion-review mode, which prevents graph editing.

| Menu | Commands |
|---|---|
| CAD | Connect, restore reference state, disconnect |
| Data | CAD Save, JSON open/save, CSV/Excel import/export, import concept axes |
| View | Full/compact, track/settings/points panels, maximize CAD and choose window position |
| Settings | AI connection, language, help, log folder |

**Auto** follows Loop in full and compact views. It enables Apply when playback starts, checking the initial pose before starting. A motion/Apply error also disables Auto. View and motion-pattern switches preserve Auto, but startup, loading new settings, connection, disconnection and restoration of the reference state disable it. Auto is not persisted. MCP and add-in playback follow the same setting.

Delete a motion from **⋯ → Delete** beside the pattern selector. The final motion cannot be deleted.

Delete a regular track with the button below the left track list, including initial sample tracks. This removes it from every motion, restores/releases its registered target and leaves CAD parts intact. A confirmation prevents accidental deletion. The final track remains; individual linked concept axes cannot be deleted.

Drag a track name to reorder it and release at the blue insertion line. Esc or release outside the list cancels. Checkboxes control visibility only. Reordering preserves targets, pattern data, selection and checks; the order is saved. Pending input is committed first.
