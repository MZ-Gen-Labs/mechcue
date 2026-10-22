# User guide

<!-- mechcue-docs-language -->
[日本語](USER_GUIDE.md) | [English](USER_GUIDE.en.md) | [Documentation](INDEX.en.md)

Open `examples/sequence.json` without a CAD connection to try chart editing. The left panel lists tracks, the centre contains the chart and point table, and the scrollable right panel contains the selected track's settings.

## Editing and connecting

| Action | Result |
|---|---|
| Drag a point or segment | Change values vertically |
| Ctrl + drag | Change time as well as value |
| Alt + drag a segment | Move its adjoining equal-valued horizontal runs together |
| Shift + drag, or drag blank space | Move the time cursor |
| Esc | Cancel the current drag |
| Undo / Redo | Undo or redo chart edits |
| View mode | Seek without editing points |

Dragging can extend beyond the current vertical limits. Apply table edits with **Update chart**. Points use linear interpolation. Distances are millimetres and angles are degrees. Constraints use absolute values; relative part movement/rotation is measured from the registered reference state.

Connect to Solid Edge and register a target for each track. Select a part in CAD, retrieve candidates and highlight the target before registering it. Enable **Apply** to send the current values to CAD. Constraint directions come from CAD; direct part driving uses assembly X/Y/Z.

Release a single track's assignment to change its driving method or axis while keeping the connection and other tracks. JSON import requires disconnecting; chart JSON does not contain CAD bindings. If nothing moves, check the target assembly, registrations and Apply. The bottom status shows registration count and Apply state. Reconnect and register again after the model has been closed.

## Reading current values

Choose a CAD part and retrieve candidates. If there is one candidate, its value is applied to all points of the selected track without changing their times. With multiple candidates, choose one and use **Read current value into all points**. Undo can restore the old points.

Distances use mm, angles degrees, and component position is an absolute coordinate on the selected axis. An unregistered relative movement/rotation reads as zero using the current pose as reference; once registered it reads the difference from registration. Relative rotation is read in the range −180 to 180 degrees. Reading before registration helps avoid sudden movement.

## Driving grounded parts by coordinate

Choose **Component position**, select X/Y/Z, read the current value and register the part. Chart values become absolute coordinates in mm on that axis; other axes and orientation retain the registered values.

This supports top-level free parts and parts with grounding. Other constraints may coexist and are preserved. Parts with other constraints but no grounding should be driven through distance/angle constraints. Ground constraints are suppressed while registered and restored with the original pose on release, disconnect or normal exit. After a forced exit, verify the restored state before saving CAD.

## Stop on interference

Enable interference stop and Apply. During playback, seeking and graph edits, Solid Edge compares the assembly part collection against itself, expanding subassemblies through its interference API. Interference, possible interference, incomplete analysis and API failure stop operation. Part names are shown and the previous pose is restored; the cursor also returns when there is a previously checked time.

This basic feature creates neither interference solids nor reports. Its original implementation does not support exclusion pairs or ignoring initial collisions, and does not continuously certify intervals between updated poses. Slower playback does not guarantee detection. Large models can be slow. It is not a machine safety or protective control. Separate path inspection features have their own scope and contact policies; see [continuous inspection](CONTINUOUS_MOTION_INSPECTION.en.md).

## Chart display and snapping

Choose **Selected only**, **Selected + checked** (default), or **All tracks** above the chart. The selected edit track remains visible even if unchecked. Checkboxes only affect visibility; they do not unregister a track or stop its CAD updates. Click a track name or coloured legend entry to select it. Only the selected track's thicker line and points can be edited.

Distance/position/translation share the left axis in mm; angle/rotation share the right axis in degrees. Hide tracks with very different magnitudes to improve scaling. Hidden tracks do not contribute to the vertical range. All tracks use the same time cursor. CAD Save persists display mode and checks; point-only JSON does not.

Ordinary dragging changes values only. Start with Ctrl held to move time too. Segment dragging retains slope and duration and cannot cross neighbouring point times. Shift seeks. Nearly constant charts receive at least 10 mm/degrees and 10% of the absolute value as vertical margin: 215.9 mm gives about 194.3–237.5 mm.

The default drag increment is 0.1. Points round their value; segments round the displacement to preserve slope. Set zero for no rounding. Table entries and CAD readings are not rounded. The increment is shared across the screen and is not included in point-only JSON.

## Playback, compact view and language

Play at the end restarts from the first point; otherwise it resumes at the current time. The play icon becomes pause. There is no separate on-screen stop button; MCP stop remains available. Hover over icons for explanations.

Compact view shows the graph, legend, playback, time, speed, Loop, Apply and interference controls. Points cannot be edited there; dragging seeks. **Back** restores panels and the previous full-view size. Switching display mode stops playback.

Choose **Auto / 日本語 / English** in language settings. Auto follows Japanese Windows display language, using English otherwise. The preference is saved per user. Track names, CAD part names and chart data do not change. Restart Solid Edge for add-in menu language changes. Translation dictionaries and command explanations are stored in `Localization` JSON files.

## Save settings inside an assembly

**CAD Save** embeds graphs, targets, axes, reference values/poses, original grounding state, playback speed, loop/interference settings, overlay/checks and drag increment, then saves the Solid Edge document. It stops playback/Apply and restores the reference pose and grounding; it does not save the current playback pose or time.

Solid Edge Save also stages settings. Editing marks the document modified. Closing or disconnecting the add-in stages changes but you still need to save in Solid Edge. Read-only documents cannot be saved; failures are reported, and closing can offer to proceed without saving.

On the next connection, embedded settings and targets load automatically with time zero, playback stopped and Apply off. Loading alone does not move parts or suppress grounding. Driving starts when Apply is enabled.

Part reference keys and MechCue GUID attributes on constraints identify targets. The application does not guess by name or list position. Deleted/replaced objects, duplicate IDs or changed grounding leave affected tracks requiring reassignment; other tracks remain. Unresolved binding information is retained when saving. Unsupported settings versions are rejected without automatic overwrite.

### Verify persistence on a copy

Register targets, change graphs/speed and use CAD Save. Confirm the reference state, close the chart and assembly, reopen them and check graphs, speed and bindings. Enable Apply and verify the restored targets. Repeat using Solid Edge Ctrl+S, Save As and a Solid Edge restart. Test grounded-part coordinates: loading should preserve grounding; releasing after driving should restore it. Delete a target in a test copy and confirm only that axis requests reassignment, without binding to another object.

Developer integration entry point: `--persistence-integration-test`. It copies the locally installed Siemens Fourbar tutorial into a dedicated directory and also creates a new assembly. Original models are not saved, the original active document is restored, and tutorials are not redistributed.

## External editing with CSV and Excel

1. Export all points of the active motion as `.xlsx` or `.csv`.
2. Edit `Time_s` and `Value`, then save. CSV uses UTF-8 and `.` for decimals.
3. Import the file, review the results and enable Apply when ready. Import stops playback and temporarily disables CAD reflection.
4. While connected, matching `TrackId` values update existing tracks while retaining targets and unlisted tracks. Disconnected import replaces the track list.

Each row is one point. Columns: `TrackId, Name, Kind, Axis, Time_s, Value, Unit`.

| Column | Meaning |
|---|---|
| TrackId | Stable track ID; preserve it when updating an existing track |
| Name | Track name; renaming with the same ID retains its target |
| Kind | `distance`, `angle`, `translation`, `rotation`, or `position` |
| Axis | `X`, `Y`, `Z`; used for part driving, not constraint direction |
| Time_s | Time in seconds |
| Value | Value in the declared unit |
| Unit | `mm` for distance/position/translation; `deg` for angle/rotation |

All rows of a track must agree on ID/name/kind/axis. Each track needs at least two points; import sorts by time. Duplicate, negative or over-100000-second times, bad numbers and unit mismatches are rejected before modifying data. Release a registered track before changing its kind/axis. Create new tracks in MechCue before exporting if convenient.

Excel formulas must be recalculated and saved in Excel: MechCue reads cached results and does not evaluate formulas. CSV needs computed numbers. Track names starting with `=`, `+`, `-`, or `@` receive an apostrophe on CSV export to reduce formula interpretation; it is removed on reimport. Use CAD Save to retain target/settings information absent from tables. PLC acquisition and acceleration conversion are outside this feature.

## Menus, panels and AI

The Data menu contains JSON open/save, table import/export and CAD Save. Drag vertical splitters to resize track/settings panels and the horizontal splitter to resize the numeric panel. Hide panels with their **−** header controls and restore them from View; compact/full switching retains hidden panels and sizes.

The add-in groups connection, playback, data, editing, tracks and display in the MechCue tab. Track commands apply to the selected track. Check states are shared with the chart. Successful registration clears CAD selection; candidate retrieval preserves it. The ribbon menu registration issue from alpha.14 was fixed in alpha.15; update with Solid Edge closed.

For AI control, install MCP and register `MechCue.Mcp.exe` in your AI client. AI connection is on by default since alpha.20. Inspect the track list before requesting edits. Bulk edits are one Undo operation and retain targets. See [MCP setup and tools](../Mcp/README.en.md).

## Logs

Open Settings/Data → Log folder as available in your version. Logs are under `%LOCALAPPDATA%\MechCue\Logs`; diagnostic files use `mechcue-<date>-<process-id>.jsonl`. Include the failure time and relevant log when reporting an issue. Logs record actions, panel states and exception details; they do not automatically send CAD files. Files rotate at about 5 MB and the most recent 20 are retained. The add-in also uses `addin-log.txt`.

Some sections above describe the release in which a feature appeared. For the current toolbar's preservation of Apply/Auto check states during edits and motion changes, see [chart menus](CHART_MENUS.en.md) and [window placement](WINDOW_PLACEMENT.en.md).
