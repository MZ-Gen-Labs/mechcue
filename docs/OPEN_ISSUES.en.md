# Open issues — 0.3.3 series

<!-- mechcue-docs-language -->
[日本語](OPEN_ISSUES.md) | [English](OPEN_ISSUES.en.md) | [Documentation](INDEX.en.md)

This is the project's dated work record, not a live query of GitHub release state. Header last updated 2026-10-15 during alpha.03 preparation; later entries include alpha.04 results. Move resolved items out of this list and record context in [development](MCP_033_DEVELOPMENT.en.md).

## Resume context from 2026-10-15

The record lists draft prereleases `v0.3.3-alpha.01` (`3e5300b`) and `.02` (`3d22449`), with four assets/hashes checked; alpha.02 was installed and tested as an add-in. They must remain drafts/prereleases, not published.

Alpha.03 on `codex/0.3.3-alpha.03` includes separate-process physics, three high-priority fixes, structural tasks 2–7 and removal of geometric physics. A locally installed alpha.03 verified add-in calculator operation (.55 s, same results as alpha.02), no BepuPhysics in Solid Edge, and 116 MCP tools.

Entry points: `MechCue.Physics.exe --self-test`, `MechCue.exe --self-test`. Native fixture: `output/Arm5pPhysics_20261014/model/concept.asm`, pattern named “摩擦把持と落下（Bepu）”. Output is excluded from Git. Its path was clear using eleven explicitly chosen contact pairs; these were inspection choices and were not saved in the assembly.

## High priority and structural work

No high-priority item remains in this record. Alpha.03 resolved installed add-in process verification, PhysicsUiTests, documentation gaps and lost physics settings on concept migration. Structural tasks 1–7 were also completed, including retiring old geometric physics. See the development page for details.

## Physics issues

| Issue | State |
|---|---|
| Beta engine | Recorded dependency is BepuPhysics 2.5.0-beta.33 (published 2026-10-11; latest at the record's 2026-10-15 check). Consider stable migration when appropriate and rerun calculator self-tests. Process isolation does not establish result accuracy |
| Contact-skin native check | Default `contactSkinMm=.05` compensates .02 mm penetration, leaving roughly .03 mm gap. Installed path checking without excluding table/workpiece remained unverified |
| Force-driven finger display | Physics fingers stop against the object, but CAD playback still draws commanded graph positions. Align commands to the surface or add actual-position output in future |
| Collision geometry | Only concept boxes/cylinders; see real-assembly work below |
| Engine selection | `IRigidBodyEngine` exists, but calculator implementation is fixed to Bepu; no user setting |
| Allowed contacts | Eleven pairs used in alpha.02 path verification were not saved into the assembly |

Alpha.04 resolved the 2.4.0 multipoint-friction diagnosis, contact stiffness/penetration reduction, force-limited gripping, restitution, contact skin and stalled sliding-contact subdivision. These fixes do not remove the remaining limits above.

## Deferred request

Arm5p implements the initial concept request for physical bodies/gripping/workpiece/gravity. Arbitrary real assemblies still need mesh/convex/compound collision conversion, face-based selection and mass/inertia from Solid Edge properties. Solid Edge Motion/DDM investigation was removed as unnecessary on 2026-10-15; development proceeds with rigid-body BepuPhysics.
