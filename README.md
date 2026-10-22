# MechCue

<!-- mechcue-docs-language -->
[English](README.md) | [日本語](README.ja.md) | [Documentation](docs/INDEX.en.md)

**Drive Solid Edge assemblies with time–displacement charts.**

MechCue is a time-chart editor for Solid Edge 2026. Drag points and segments to edit motion and inspect multiple mechanisms against a shared time cursor. The add-in and standalone application share the same editor and chart format.

The released 0.3.2 series provides MCP control of Solid Edge, concept models built from dimensioned sketches (machine tools, Cartesian transfer systems and five-axis robot arms), drawings, 3D PMI and Simulation integration for single parts. It is intended for concept studies and motion checks.

Latest development release: **0.3.3-alpha.05** (draft prerelease). See [changes and validation](docs/releases/0.3.3-alpha.05.en.md).

## Features

- Overlay the selected track, selected and checked tracks, or all tracks, with separate distance and angle axes.
- Drag points and segments, move adjacent horizontal sections together, edit numbers, undo/redo and insert out-and-back motion templates. See [chart editing](docs/TIMELINE_EDITING.en.md).
- Zoom and scroll the timeline, follow playback, reorder tracks and remove tracks.
- Play, pause, change speed, loop and seek to any time.
- Drive distance/angle constraints, translate and rotate free or grounded parts, and set an absolute component coordinate on a chosen axis.
- Find candidates from a CAD selection, highlight targets, and reassign or release one track without disconnecting the others.
- Read a CAD value into every point of the selected track.
- Optionally stop on interference or analysis failure and restore the preceding pose.
- Restore the reference state and save/load chart JSON.
- Import/export CSV and Excel `.xlsx` tables. Matching track IDs retain CAD targets during connected imports.
- Use add-in ribbon commands for playback, compact view, data, connection, editing and tracks.
- Restore chart position, size and compact mode. Place it at nine positions with `Ctrl+Alt+Shift` plus `UIO / JKL / M,.`; repeat the same key to toggle full/compact view. See [window placement](docs/WINDOW_PLACEMENT.en.md).
- Save chart data, targets and playback settings inside the assembly and restore them on connection.
- Use a separate MCP server for AI chart editing, resetting values, evenly spacing points, undo and playback.
- Read Solid Edge documents, parts and variables through MCP, select parts, and control read/edit access from the tray.
- Create ordered protrusions/cutouts, assembly placements and planar constraints, drawing views and PDFs through MCP.
- Add third-angle views and principal overall, diameter, position and step dimensions. See [drawing automation](docs/DRAWING_AUTOMATION.en.md).
- Add reference dimensions to single-body parts and control their display in model views. See [3D PMI](docs/PMI_AUTOMATION.en.md).

The 0.3.3 development series adds the `arm5p` concept model with fingers and free-body workpiece axes. Rigid-body physics computes gripping, carrying, falling, sliding and tipping from the arm/finger keyframes. Results appear as read-only **physics tracks** and can be recalculated automatically after editing. Computation runs in a separate `MechCue.Physics.exe` process. See [0.3.3 changes](docs/releases/0.3.3.en.md).

PLC connection/program import, acceleration/deceleration models and load calculations are not supported.

## Get started

Requirements: Windows x64, Solid Edge 2026 and .NET 8 Desktop Runtime x64. Chart editing can be tried without a CAD connection.

1. Close Solid Edge and run `MechCue-<version>-Setup.exe` from [GitHub Releases](https://github.com/MZ-Gen-Labs/mechcue/releases).
2. Open MechCue from the Solid Edge assembly environment.
3. Select a track, confirm its intended CAD target and register it.
4. Enable CAD Apply and move the time cursor.

The application and installer support Japanese and English. In Settings → Language, choose **Auto**, **日本語**, or **English**. Auto uses Japanese for a Japanese Windows display language and English otherwise. The choice is saved per user; restart Solid Edge to update add-in menu labels.

Choose **Add-in only** if you do not need the standalone application. Starting with 0.3.2-alpha.02, one installer offers multiple component choices; earlier versions had a separate `AddIn-Setup.exe`. Run setup again to change components without uninstalling first. Start the standalone version with the installed `MechCue.exe` or the standalone ZIP.

For AI control, select the optional MCP server in setup and enable AI connection in MechCue. A separate MCP ZIP is also available. Configure your AI client using the [MCP guide](Mcp/README.en.md).

Open `examples/sequence.json` to try several motions. Values are examples: adapt them to your CAD model and register targets before the first CAD run. CAD Save enables restoration on the next connection.

Run `Open-Release-List.cmd` for a chronological release list, including drafts your account can access. Within a base version it orders releases oldest first using creation timestamps down to seconds. See [release list](docs/RELEASE_LIST.en.md).

## Guides

- [Documentation index](docs/INDEX.en.md) — user guides, automation, development records and release notes, with Japanese counterparts.
- [User guide](docs/USER_GUIDE.en.md), [installer](Installer/README.en.md), [chart menus](docs/CHART_MENUS.en.md).
- [0.3.2 changes and limits](docs/releases/0.3.2.en.md), [0.3.3 development](docs/releases/0.3.3.en.md).

## Validation and limitations

Automated tests cover interpolation, units, XYZ translation/rotation, dragging, disconnection and reassignment. Basic add-in display and distance-constraint driving were verified on Solid Edge 2026. The minimal COM declarations build without Siemens interop DLLs and have been checked against the 2026 signatures. Hardware validation of individual driving methods and axes is ongoing. See [validation](docs/VALIDATION.en.md) for dated results and outstanding checks.

Distance constraints use absolute millimetres and angle constraints use absolute degrees. Relative component translation/rotation is measured from registration in assembly X/Y/Z coordinates; rotation is about the component origin. Constraint directions are determined by CAD.

Absolute component-coordinate driving supports top-level free parts or parts with a ground constraint, including those with other constraints. Only grounding is temporarily suppressed; other constraints are preserved. Relative translation/rotation supports free parts or parts constrained only by grounding. Use constraint driving for constrained parts without grounding. A part cannot be registered to multiple tracks simultaneously.

Simple playback interference checks test updated poses; they do not guarantee that intervals between updates are clear. Large models may run slowly. Changes to constraints or replacement of parts while connected are unsupported, and tracks are updated sequentially.

Playback does not automatically save CAD files. CAD Save embeds settings, restores the reference pose/ground state and saves the CAD file. Solid Edge Save also stages settings. Disconnecting or closing the add-in stages changes, but you must save in Solid Edge to commit them to disk. Verify with copies of models.

## Development

Windows, .NET 8 SDK and Inno Setup 6 can build the add-in and standalone application without Solid Edge or its interop DLLs:

```powershell
./scripts/Build.ps1 -WithAddIn -WithMcp
```

Packages are written under `artifacts/<version>/release`: standalone ZIP, setup with optional standalone/MCP components, MCP ZIP, and `SHA256SUMS.txt`. Use a new `-OutputDirectory` when repeating a build. PRs and main updates run validation; `v*` tags create draft prereleases. See [release procedures](docs/RELEASING.en.md), [contributing](CONTRIBUTING.en.md) and [development know-how](docs/KNOWHOW.en.md).

| Directory | Contents |
|---|---|
| src/MechCue | Shared UI, charts, CAD integration and tests |
| AddIn | COM add-in and minimal COM declarations |
| Installer | Installation, registration, updates and removal |
| scripts | Build and package validation |
| tools/ComContractCheck | COM declaration checks |
| examples | Chart examples without CAD models |
| docs | User, validation, release and investigation documents |

Product names, filenames, project names and ProgID use MechCue; the existing CLSID is retained. [MIT License](LICENSE). Solid Edge requires a separate license. See [third-party notices](THIRD_PARTY_NOTICES.md) and [company deployment](docs/COMPANY_DEPLOYMENT.en.md).
