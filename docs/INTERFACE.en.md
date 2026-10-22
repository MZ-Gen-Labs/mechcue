# Mechanism editor design notes

<!-- mechcue-docs-language -->
[日本語](INTERFACE.md) | [English](INTERFACE.en.md) | [Documentation](INDEX.en.md)

This is a design record. Use [setup](../Installer/README.en.md) and the [user guide](USER_GUIDE.en.md) for current instructions.

The left panel lists tracks; the centre shows the chart and point table; the scrollable right panel contains name, driving method, target and motion template.

- Editing changes point times/values and moves entire segments vertically, including beyond current limits. Shift seeks; Esc cancels. Current ordinary/Ctrl/Alt gestures are documented in [timeline editing](TIMELINE_EDITING.en.md).
- Review/View mode only seeks, including on points and segments, and sends values only with Apply enabled.
- Undo covers committed point edits, table changes and templates. It does not undo track creation, metadata or target assignments. File loading clears history.
- The move–hold–return template replaces the selected graph using origin, travel, one-way duration and hold duration; units are mm/degrees.

## Assign from CAD

Connect, choose a driving method, select one top-level part in Solid Edge Pathfinder, retrieve candidates, inspect current values/units/related part names, highlight the intended target and register it. **Show all candidates** restores the full list. Faces, edges and nested parts are outside this original selection-filter feature. Selection is read on demand, not continuously monitored.

Add tracks while connected. Release a registered track before changing its method/axis. Disconnect one application before using the other. Direct part movement originally rejected constrained parts; see the current guide for grounding and coordinate-driving exceptions.

Reassignment restores only the old target to its reference value before registering the replacement. Other tracks and the connection remain. Duplicate targets are rejected and the old assignment is preserved. The original operation disabled Apply; current operations preserve its checkbox while temporarily suspending reflection.

X/Y/Z applies to direct component movement/rotation, not distance/angle constraints. Constraint offsets follow CAD face orientation; global axes do not automatically filter constraint candidates.

The record verified builds, editing/cancel, out-and-back templates, Undo, switching and UI display; candidate filtering used test objects. Actual new selection/highlight operations still required hardware checks at that point. PLC and cylinder on/off charts were unimplemented.
