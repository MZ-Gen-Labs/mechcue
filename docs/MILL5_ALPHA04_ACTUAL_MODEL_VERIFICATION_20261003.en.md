# Alpha.04 real five-axis model verification — 2026-10-03

<!-- mechcue-docs-language -->
[日本語](MILL5_ALPHA04_ACTUAL_MODEL_VERIFICATION_20261003.md) | [English](MILL5_ALPHA04_ACTUAL_MODEL_VERIFICATION_20261003.en.md) | [Documentation](INDEX.en.md)

Environment: installed 0.3.1-alpha.04 (`70a6cc6`), Solid Edge 2026. All five patterns completed all sampled key times, but every pattern had defects. No continuous clear-path/gap guarantee was established, and no geometry or chart safety correction was performed.

Target: `output/Mill5-detail-study-20261003-1007/detail/mill5-detail.asm`, six rigid subassemblies, 44 leaf parts and 50 nodes including assemblies. Of 946 pairs, 41 justified mounting/guide/joint contacts were excluded and 905 inspected. Keys/reasons are in `output/mill5-alpha04-inspection/contact-review.json`. Required .5 mm gap was provisional, not a manufacturing/use specification. Margin was .01 mm; nested/continuous options were on.

The one-second base step included all .25 s chart keys, so effective sampling was .25 s. Defects did not stop subsequent samples.

| Pattern | Range | Executed/planned | Defective pairs |
|---|---|---|---:|
| 01 Loading/unloading | 0–24 s | 97/97 | 7 |
| 02 Index/approach/retract | 0–32 s | 129/129 | 6 |
| 03 Simultaneous five-axis | 0–30 s | 121/121 | 6 |
| 04 Retract/reference return | 0–18 s | 73/73 | 6 |
| 05 Full-travel search | 0–66 s | 265/265 | 9 |

All had sampling/analysis complete and restored poses, but `allSamplesClear=false`, `continuousPathCertified=false`. Completion is not safety.

## Five native interference pairs

Zero-required-gap native analysis independently confirmed:

| Pair | Confirmed pose |
|---|---|
| Fixed base ↔ Y-feed motor | Constant from reference pose |
| Fixed column ↔ rear Y support | Constant from reference pose |
| YFeed X motor ↔ XFeed table | Pattern 01 at 18 s: X+220/Y−120/Z150 mm; pattern 05 at 10 s: X+300/Y0/Z150 |
| YFeed X motor ↔ XFeed A-support 2 | Pattern 05 at 10 s: X+300 |
| Tool ↔ workpiece | Pattern 05 at 35 s: Z−200, X/Y/A/C zero |

A/C use degrees. No cutting interval/removal geometry was defined, so tool/workpiece overlap was not automatically excluded. With .5 mm required gap, abnormal sample spans included 01:15.25–20.75 s and 05:8.25–12.5 s (motor/table), 9–11.25 (motor/A-support), 33.75–36.25 (tool/workpiece). These are first/last abnormal samples, not exact continuous collision times.

Four unresolved zero-gap pairs were base/Y-saddle, Y-screw/Y-saddle, Y-saddle/X-table and X-screw/X-table. Native zero-gap interference did not flag them at reference pose. They remained included pending whether they represent intended contact or need relief.

## Software/connection issues discovered

Results repeated every leaf pair and long keys in both `analysis.pairs` and `leafPairs`, producing roughly 100 MB at around 100 samples. UI pattern 04 failed `Cannot allocate a buffer of size 2147487687.` and Solid Edge working set reached about 11 GB. Confirmed cause was result-buffer allocation, not proven CAD restart or memory leak.

Pattern 05 completed, but MCP get-operation returned `-32603`; direct read-only local API retrieved the same 269022550-byte result. Exact transfer/serialization cause needed isolation. Proposed fixes were shared identity, defect/summary records, paged detail, streamed files, byte-limited history, lightweight status and cancellation/partial results.

Default surface 5 mm/max5001 rejected patterns 02/03/05 before motion for sample budget. Pattern 01 completed 363 samples; 04 hit the buffer error. Preflight estimates, interval splitting and target-specific rotational radii were needed, retaining non-safe budget verdicts.

UI pair labels expected old `first.name/second.name` rather than nested `Part1/Part2`, hiding some defect names. Needed named defect tables, time/pose, gap versus collision and unknown reasons. The configured connector was `Transport closed` with stale tool discovery; testing used the installed alpha.04 executable through direct JSON-RPC. That connection problem was separate from MechCue inspection capability.

## Restoration and evidence

Restored pattern 05, time 12.5946146000006 s and Apply/Auto. Graphs/bindings were unchanged and all 50 node matrices matched. Original dirty state remained; no geometry edits/overwrite save occurred. The 41 contacts/.5 mm gap were active and exported separately, requiring CAD Save for assembly persistence. No extra component windows opened; the original assembly/results remained visible. Source/version/setup were unchanged.

Evidence under `output/mill5-alpha04-inspection/`: contact review; coarse-01…05 summaries; MCP detail 01…04; 05 MCP error and direct-pipe result; native zero-gap rechecks at 01/18, 05/10, 05/35; restoration/state/document comparisons; UI screenshots/text. Prefer summaries over huge detail files. Outputs are local and not committed. Subsequent [compact results](COARSE_MOTION_INSPECTION_IMPLEMENTATION_20261003.en.md) and [completion](MOTION_INSPECTION_COMPLETION_20261003.en.md) address software issues.
