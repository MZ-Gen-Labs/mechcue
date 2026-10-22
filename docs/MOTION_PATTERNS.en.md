# Named motion patterns

<!-- mechcue-docs-language -->
[日本語](MOTION_PATTERNS.md) | [English](MOTION_PATTERNS.en.md) | [Documentation](INDEX.en.md)

Full and compact views have a motion selector beside the chart title. **⋯** offers New, Duplicate, Rename, Delete and Manage; the Solid Edge MechCue tab also has a Motion group.

Edits belong to the active pattern. Switching retains edits, pauses and rewinds without moving CAD. The original introduction disabled Apply; the current toolbar preserves Apply/Collision/Loop checks and temporarily suspends reflection during switching. Subsequent play/seek follows the retained Apply selection. See [current chart controls](CHART_MENUS.en.md).

New creates constant tracks at the current chart values; Duplicate copies all points. Track names, driving methods and targets are shared across patterns. Points and speed belong to each pattern; pattern data also stores loop/interference options, although current switching retains the active toolbar choices. Display settings, language and panel layout are shared. New tracks are added to other patterns as constants. Names may contain up to 100 characters, with up to 500 patterns. The last pattern cannot be removed.

CAD Save stores every pattern and shared targets. Legacy data migrates to one pattern. JSON exports all patterns and chart settings without CAD bindings; old point-only JSON remains readable. CSV/Excel handles only the active pattern.

MCP tools: `mechcue_list_patterns`, `mechcue_create_pattern`, `mechcue_switch_pattern`, `mechcue_rename_pattern`, `mechcue_delete_pattern`. Select by name or stable ID. State includes `activePatternId` and `patternName`. Graph edits affect the active pattern; switching clears Undo history.

Settings use Version 2 and accept Version 1. Open Version 2 with a compatible application to avoid losing patterns through older software.
