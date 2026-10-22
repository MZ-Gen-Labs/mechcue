# Adaptive motion sampling and interference checks

<!-- mechcue-docs-language -->
[日本語](ADAPTIVE_MOTION_INSPECTION.md) | [English](ADAPTIVE_MOTION_INSPECTION.en.md) | [Documentation](INDEX.en.md)

Implemented 2026-10-03 for 0.3.1-alpha.03; absent from the older alpha.02 installer. This page describes that release. Later leaf-part/contact/continuous capabilities are in [continuous inspection](CONTINUOUS_MOTION_INSPECTION.en.md).

## UI records

Open **Path inspection** beside the motion selector to inspect the active path or all patterns from start to end. Collisions do not prevent checking later points. Analysis failure, cancellation and sample overflow are incomplete; unexecuted patterns after cancellation are recorded too. The original pose, selected pattern and graphs are restored, with playback stopped.

Records show pattern, verdict, interval, completed/planned samples, clearance and timestamp. Detail shows per-time state, minimum gap, parts and errors. JSON export includes axes, nearest points and conditions. Playback and MCP checks are also recorded; successive clear playback intervals originally shared a record.

Alpha.03 retained 100 session records and at most 10001 points per playback record. Export before closing. Results are snapshots and need rerunning after CAD/placement/target/graph changes. Later limits differ.

## Clearance and adaptive samples

Zero required clearance disables distance checking; positive clearance uses native `AssemblyDocument.MinimumDistance` for top-level part/subassembly pairs, at most 10000. It records minimum gap, nearest pair and closest points. Failure is not safe. Alpha.03 does not inspect internal subassembly gaps or exclude intentional contact, so fixed contacts fail a positive clearance requirement.

`Occurrence.Range`, component reference placement, concept-axis offsets and travel estimate a rotation radius. Intervals subdivide when translation plus radius × rotation in radians exceeds the default 5 mm surface-motion guide. Existing 5 mm linear/1° angular conditions remain. Missing bounds reject. This is conservative sampling guidance, not a strict bound for arbitrary constrained mechanisms; large/distant models can require many points.

Playback checks all intervening keys and inserts points for at most 1 second, total absolute linear-axis change 5 mm and angular change 1°. This retains peaks, full turns and reverse seeking. Initial alignment checks only its destination pose. For the whole path, start at the beginning and reach the end. Loop wrap is unverified, so checked playback stops at the end.

Collision/possible collision/analysis failure stops and restores the previous completed update pose. More than 5001 points in one update rejects before motion; use a shorter interval. Slow checks do not skip samples.

## MCP

Call `mechcue_start_operation` with `operation="check_motion"` and an `argumentsJson` string containing:

```json
{
  "startTime": 0, "endTime": 10, "step": 1,
  "adaptive": true, "maxLinearStepMm": 5, "maxAngularStepDeg": 1,
  "surfaceBased": true, "maxSurfaceStepMm": 5,
  "requiredClearanceMm": 0.5, "maxSamples": 5001,
  "stopOnInterference": false
}
```

In alpha.03, adaptive defaults true; step is a maximum time gap. `maxSamples` is 2–10001 and overflow rejects before movement. `adaptive=false` uses the old fixed-time grid, limited to 501 samples. Poll with operation tools.

Check `allSamplesClear`, `samplingComplete` and `analysisComplete`. Early stop leaves later times uninspected; analysis errors abort regardless of collision-stop setting. The original pose is restored afterwards.

Alpha.03 always returns `continuousPathCertified=false`: clear samples do not prove clear intervals. Smaller steps may help inspect tight clearances but are not continuous proof. Planned work included justified contacts, internal gaps and bounded arbitrary mechanism motion; later supported work is documented separately.

## Validation

Self-tests cover peaks, 360° turns, simultaneous axes, budgets, reverse motion and restoration. Solid Edge 2026 tests moved a 10 mm cube past an obstacle: endpoints were clear and adaptive sampling found the intermediate collision. A 20 mm gap failed a 21 mm requirement. A part 1 m from its rotation origin generated more surface-based than angle-only samples. UI recorded 13/13 collision-path and 3/3 clear-path samples and exported JSON while preserving source state.

Historical local evidence: `output/motion-sampling-native-alpha03-08/results.json`, `ui-reports.json`, `ui-results.png`. Test documents were saved/closed and the original assembly state preserved. Outputs are not committed.

API: [MinimumDistance](https://support.industrysoftware.automation.siemens.com/trainings/se/107/api/SolidEdgeAssembly~AssemblyDocument~MinimumDistance.html), [Occurrence.Range](https://support.industrysoftware.automation.siemens.com/trainings/se/106/api/SolidEdgeAssembly~Occurrence~Range.html).
