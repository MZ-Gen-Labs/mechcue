# Proposal: staged path inspection — 2026-10-03

<!-- mechcue-docs-language -->
[日本語](STAGED_MOTION_INSPECTION_PROPOSAL_20261003.md) | [English](STAGED_MOTION_INSPECTION_PROPOSAL_20261003.en.md) | [Documentation](INDEX.en.md)

This proposal followed alpha.04 real-model checks. At this point it was not implemented and no version/CAD geometry changes had been made. Later [implementation](COARSE_MOTION_INSPECTION_IMPLEMENTATION_20261003.en.md) and [completion](MOTION_INSPECTION_COMPLETION_20261003.en.md) record what actually happened.

## Decision and priorities

Separate chart density from CAD inspection density instead of evaluating/storing every pair at every dense key by default. For this concept study, .25 s charts were excessive; use mainly one-second keys, retaining extrema, reversals, stops, approach/retract boundaries and important compound poses, including noninteger times. Preserve original charts for restoration. Do not merge distinct motions merely to shorten runtime; precise curve approximation is not this study's requirement.

Coarsening alone cannot solve 269 MB from 265 poses: compact result generation/storage/transfer too. Normal results should contain path summary, scope, defects and unknown intervals, with shared identity tables and typed working data instead of repeated JSON. Detail belongs in separate report/interval/pages; exports should stream. Apply time/point/memory/output budgets and cancellation. Show defect names, time/pose and native interference versus gap versus analysis failure. Preflight should estimate samples/output.

## Stages

Preflight validates documents, geometry, bindings and contacts, then checks the reference pose. Verify fixed geometry/rigid relations before measuring invariant pairs once. Known collisions remain failures, while investigation can continue on other moving pairs.

Coarse inspection begins with one-second/start/end/stop/reversal/extreme poses, using motion, rotation and bounds too. Bounds may omit native distance measurements for provably distant pairs and concentrate on approaching pairs. Distinguish “coarse defects found,” “none found at coarse samples” and “unexecuted/unverified.” Clear samples alone never prove a clear path.

Detailed verification splits only inconclusive intervals, maintaining independent per-pair state so one fixed defect does not block others. Refine around defects when needed. Only a bound for every target pair/interval yields conditional continuous verification; budgets, cancel, unsupported movement and changed shape remain unknown.

## Dense keys and CAD design

Do not automatically turn every original key into a CAD sample. Keep every key for interpolation/total variation (`MotionBoundMath.Variation`), preserving intermediate return motion/full turns. Revalidate predicted poses and bounds crossing keys. A future precision simplifier should provide position/angle/velocity tolerances and Undo; do not apply concept simplification indiscriminately.

For the actual mill, fix two static overlaps first, then review X-motor/table/A-support placement or range. Define approach/cutting/retract intervals and removed material before judging tool/workpiece contact. Four zero-gap pairs need support/shape design intent before relief or reasoned contacts. Do not promote the provisional .5 mm gap into a manufacturing requirement.

## Acceptance criteria

The 44-part/five-path fixture must avoid the 2 GB buffer allocation failure, expose lightweight MCP status/summary and keep memory/responses within budgets. Preserve the five known collision pairs and named classifications. Test intermediate collisions despite clear endpoints, reversals, full turns, dense keys and simultaneous axes. Keep coarse findings distinct from interval proof, never label exhausted budgets safe, and preserve CAD poses, original graphs and pre-existing user documents.
