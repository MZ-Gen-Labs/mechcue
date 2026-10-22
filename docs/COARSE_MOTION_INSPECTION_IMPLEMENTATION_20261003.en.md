# Coarse concept charts and compact inspection results — 2026-10-03

<!-- mechcue-docs-language -->
[日本語](COARSE_MOTION_INSPECTION_IMPLEMENTATION_20261003.md) | [English](COARSE_MOTION_INSPECTION_IMPLEMENTATION_20261003.en.md) | [Documentation](INDEX.en.md)

This is an intermediate implementation record. Later software fixes/test completion are in [completion](MOTION_INSPECTION_COMPLETION_20261003.en.md).

The five-axis concept charts were reduced mainly to one-second keys, preserving extrema, reversals, stop boundaries and each pattern's duration. Display-key density and CAD-evaluation density are independent.

| Pattern | Points per axis before | After | Duration |
|---|---:|---:|---:|
| 01 Loading/unloading position | 97 | 25 | 24 s |
| 02 Index/approach/retract | 129 | 33 | 32 s |
| 03 Simultaneous five-axis study | 121 | 31 | 30 s |
| 04 Safe retract/reference return | 73 | 19 | 18 s |
| 05 Full-travel collision search | 265 | 67 | 66 s |
| Total | 685 | 175 | — |

Inspection starts from coarse poses, then subdivides intervals whose bounds cannot prove clearance. All original keys still contribute to total-motion bounds, including return excursions and full turns. Dense sampling remains selectable. Sample completion and continuous verification are separate.

Each pose retains defective pairs and aggregates, with occurrence identity stored once per report instead of repeating all clear combinations. Native collision, possible collision and insufficient gap are distinct and named. At this intermediate stage, limits were one million working pair entries, 16 MiB compact poses and 500 records/32 MiB UI history. Unknown/budget exhaustion never means safe; export streams data.

MCP completion returns summary/reportId. Detail retrieval is paged, at most 100 poses/4 MiB per page with continuation. Clients expecting large inline results must adapt.

## Saved model and results

`output/Mill5-detail-study-20261003-1007/detail/mill5-detail.asm` saved five patterns, 41 justified contacts and a provisional .5 mm inspection clearance, returning to reference pose before save. This is not a manufacturing requirement. Original/modified charts, pre-save assembly/settings and post-save settings remain under `output/mill5-coarse-chart-20261003/`; restoring a pre-save assembly copy requires its original path. Final state had only the target document open, clean, with chart reopened.

All planned samples in the 44-part/five-pattern model completed and restored poses, retaining five known collision pairs and four unresolved gap pairs. It was not globally safe. Full-range output shrank from 269022550 bytes/265 poses to 219907 bytes/67 poses, reflecting both chart and format changes; all five reports totaled 653513 bytes. Evidence: `output/mill5-compact-inspection-20261003-01/`.

Release builds, smoke checks, 19 native advanced-motion cases and 23 MCP checks including 102 tools passed. Full self-tests failed an existing placement case reproducible in the old alpha.04 package, so full success was not claimed. A final smoke rerun after reopening the user's chart stopped on existing Undo/Redo registration before reaching changed inspection code; earlier smoke/native UI checks passed.

At this point, independent continuous checking after defects, invariant-pair caching and automatic budget splitting remained unfinished, while model geometry fixes were separate. Code was uncommitted and there was no version change/setup/install; installed alpha.04 was unchanged. See [proposal](STAGED_MOTION_INSPECTION_PROPOSAL_20261003.en.md), [model verification](MILL5_ALPHA04_ACTUAL_MODEL_VERIFICATION_20261003.en.md) and subsequent completion.
