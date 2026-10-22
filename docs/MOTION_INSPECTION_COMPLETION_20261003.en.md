# Completion of software-side motion-inspection tasks

<!-- mechcue-docs-language -->
[日本語](MOTION_INSPECTION_COMPLETION_20261003.md) | [English](MOTION_INSPECTION_COMPLETION_20261003.en.md) | [Documentation](INDEX.en.md)

Implemented for 0.3.1-alpha.05, commit `b527a60`: window/Undo test failures, independent pair verification after defects, invariant-pair measurement reuse and automatic splitting/result integration. At this record's point, no GitHub push/tag/publication or installation occurred.

Placement tests explicitly set active state for each input, avoiding background-process focus changes. Undo/Redo no longer use global Ctrl+Z/Y registration; chart-native child windows handle keys without intercepting text editing, inactive windows or key releases.

Continuous results are per pair, so one defective pair does not stop others' verification. Global defective paths remain failed. Unsupported motion still records sampled pair defects. Constant relative motion reuses measurements only within one run, transforming closest-point coordinates for common rigid movement. Document/file versions, saved state and actual poses are verified; shape changes stop the run.

Work-unit point/pair limits split processing and merge all boundary/pair results. Separate total samples/evaluations/depth/file budgets and cancellation leave unknowns. Large pose/interval detail spills to owned files. MCP uses summary/pages; UI shows named verdicts/initial detail; JSON streams everything. Eviction/window close cleans spill files. See [shared specification](CONTINUOUS_MOTION_INSPECTION.en.md).

Twenty-seven dedicated Solid Edge 2026 tests passed: continuous/intermediate collisions, reuse/closest-point transformation, independent pair proofs, unsupported motion, splits, version changes, cancel, contacts, batch UI, simultaneous XYZ/A/C/full turns and restoration. Evidence: `output/advanced-motion-completion-20261003-03/`.

Common build passed self/smoke, spill export/paging, 23 MCP checks including 102-tool discovery, traffic, update shutdown, COM and installer input/version/hash checks. Delivery directory: `artifacts/0.3.1-alpha.05-delivery/`.

The real 44-part/five-pattern model completed all initial samples and restored poses while retaining five known collision pairs and four insufficient-clearance pairs. Its latest full-range result was 328066 bytes versus 269022550 before; chart simplification, format and pair aggregation all contributed. Evidence: `output/mill5-completion-inspection-20261003-02/`. Dirty `FixedStructure.asm` disabled reuse/continuous proof. This did not establish a clear machine.

Test documents were saved/closed; original `mill5-detail.asm` pose was restored/saved and its chart reopened. Installed alpha.04 was unchanged; the fixes required alpha.05 setup. Model collisions/gaps remained separate design work; unsupported constrained/deforming motion, exclusions and exhausted budgets did not gain a safe verdict.
