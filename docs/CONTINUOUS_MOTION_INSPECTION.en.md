# Nested parts, allowed contacts and continuous intervals

<!-- mechcue-docs-language -->
[日本語](CONTINUOUS_MOTION_INSPECTION.md) | [English](CONTINUOUS_MOTION_INSPECTION.en.md) | [Documentation](INDEX.en.md)

Introduced in 0.3.1-alpha.04 and extended in alpha.05 with pair-specific proofs, coarse initial sampling, automatic splitting and compact results.

## Scope and proof

Subassemblies expand to leaf occurrences, with repeated instances inspected separately. Native APIs test internal/external interference and minimum clearance. Allowed contacts exclude exactly a chosen occurrence pair with a reason; unresolved references reject before checking instead of silently excluding.

For fixed geometry and supported rigid driving, an interval clearance lower bound is the larger endpoint bound minus maximum relative motion and numerical margin. Bounding boxes supply conservative distance bounds; native distance/interference is used where necessary. Inconclusive intervals split in half. Depth/budget/cancellation/unknown results remain unverified.

Piecewise-linear total graph variation includes all keys, peaks, reverse motion and 360° turns. Rotational radii cover actual bounds and nested offsets. Common rigid-ancestor motion cancels from relative motion. Applied poses are checked against predictions.

`continuousPathCertified=true` covers every interval and nonexcluded pair only under fixed geometry, supported rigid driving and configured native distance accuracy/margin. Default margin is 0.01 mm, configurable 0.001–10 mm. Results are snapshots: rerun after any relevant geometry, CAD or graph change.

General constraint-driven mechanisms, active nonground constraints, variable parts/subassemblies, simplified geometry and assembly-overridden shapes cannot receive continuous proof; reasons are recorded while sampled checks remain possible. This is not universal swept-B-rep verification.

Overlapping parts with native `IncludeInInterference` disabled yield incomplete results. Enable the native setting or explicitly define a justified MechCue contact. No automatic contact exclusions occur. Excluding every pair or having nothing to compare does not produce certification.

Initial CAD-to-first-pose alignment and loop wrap are outside the path proof; checked playback stops at the end. Collision, insufficient clearance or unverified intervals stop playback and restore the interval's start pose. Inspection-only restores its original pose.

## Policy and MCP

Use `mechcue_list_inspection_parts` for names, occurrence-specific `KeyPath`, bounds and policy; `mechcue_set_inspection_policy` requires edit mode and does not save CAD; `mechcue_get_inspection_reports` retrieves recorded results. These raised the historical tool count from 99 to 102.

```json
{
  "IncludeNested": true,
  "VerifyContinuous": true,
  "NumericalMarginMm": 0.01,
  "MaxRefinementDepth": 20,
  "AllowedContacts": [
    {"FirstKeyPath": "/actual-key-1", "SecondKeyPath": "/actual-key-2", "Reason": "Intentional shaft-bearing contact"}
  ]
}
```

Choose keys from the actual model. Policy, nested/continuous options, margin and clearance persist through explicit CAD Save. `check_motion` uses document policy or a per-call `inspectionPolicy` override. Alpha.05 defaults `adaptive=false`, starting with requested time steps and reversal/stop boundaries. All keys still contribute to motion bounds but need not be CAD sample poses. `baseSampleTimes` returns the actual base grid.

## Pair results and working limits

Other pairs continue to receive interval verification after one collision/gap failure. `continuousVerification.PairResults` refers to part-table IDs and full-path pair verdicts. Global certification requires every relevant pair/interval. Unsupported continuous models still record sampled defects per pair.

Pairs with constant relative pose reuse measurements only within a single inspection. Referenced saved state/file versions and actual poses are checked; changes stop/invalidate the run. Dirty parts/subassemblies disable reuse and continuous proof, with reasons; save and rerun.

`AutoSplit=true` by default. `maxSamples` (default 5001, range 2–10001) is a work-unit sample limit; splitting releases working data and continues. `MaxTotalSamples` (default 100001, range 2–100001) limits all initial/refined samples. Without splitting, maxSamples becomes the total limit. Evaluation, numeric precision, depth and cancellation limits never turn unknown into safe. Pair proofs combine across work boundaries.

Working pair data is limited to one million entries. Pose/interval details spill separately at 8 MiB with up to 256 MiB per series. UI history is limited to 500 records/32 MiB and avoids repeated full clear-pair combinations. Large result views show the first 100 details; JSON export streams all detail. Eviction/window close removes owned spill files, so export before closing.

MCP completion returns summary and `reportId`. Reports default to summaries. Supply report ID, `includeSamples=true`, `sampleOffset` and `sampleLimit` for details, including spilled data. Pose and interval continuation offsets are separate. Responses are capped at 4 MiB; global verdicts are not limited to the current page.

Playback checks remain separate proofs. Do not combine independent results into a whole-path certificate without geometry-version guarantees. Use Path inspection for the entire path. Inspect `samplingComplete`, `analysisComplete`, `allSamplesClear`, `continuousPathCertified`, `continuousVerification.Segments` and `inspectionPolicy.AllowedContacts` in each record.

## Validation

Nineteen dedicated Solid Edge 2026 checks covered repeated/rotated nested instances, a 10 mm gap, instance contacts, other collisions, continuous bounds, intermediate collisions, limits/cancel, stale keys, stopping/restoration and UI batch checks. A simultaneous XYZ/A/C fixture, including 360° C rotation, also verified supported intervals and restored its pose. This is not proof that an entire real machine is collision-free.

Run `dotnet MechCue.dll --advanced-motion-integration-test <fresh-output>`. Historical evidence: `output/advanced-motion-native-20261003-07/results.json`, `ui-reports.json`, `ui-results.png`. Self-tests, UI tests and 102-tool protocol regression passed at that point. Test-created files were saved/closed.

API: [Occurrence](https://support.industrysoftware.automation.siemens.com/trainings/se/107/api/SolidEdgeAssembly~Occurrence_members.html), [SubOccurrence](https://support.industrysoftware.automation.siemens.com/trainings/se/107/api/SolidEdgeAssembly~SubOccurrence_members.html), [MinimumDistance](https://support.industrysoftware.automation.siemens.com/trainings/se/107/api/SolidEdgeAssembly~AssemblyDocument~MinimumDistance.html).
