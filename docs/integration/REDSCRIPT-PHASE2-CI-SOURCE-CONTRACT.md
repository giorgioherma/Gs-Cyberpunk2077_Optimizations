# REDscript Phase 2 — post-generic source CI contract

**Active CI:** existing GCO `.github/workflows/build-windows.yml` on `dev/redscript/resolver_handoff`. Do **not** add a parallel product workflow, compiler implementation, separate managed runtime or source ZIP. The private GitHub fixture is already in `giorgioherma/original_cet_source`, default branch `main`.

## Inputs — distinct purposes

- Historical sealed-capture source: `GCO-Capture-15112026_021913/r6/scripts/`. Keep this as the original 2,319-file source/capture-correlated fixture for ten generic source generators and their original official SCC scenarios. No silent source replacement and no claim that historical callsite measurements describe changed files.
- Independently staged **post-generic stock**: `base_after_generic/r6/scripts/`. This is the user's actual 2,331-file generic-applied baseline, including currently installed `G-RedRuntime`; use as **the starting source for Phase 2 semantic validation**, not as input to reinstall all generic transformations. Other installed versions/custom files may be used in additional structural tests without replacing this fixture.
- Keep the existing private `Compiler/ssc.zip`, original cache, effective cache and native plugin scripts as CI-only SCC fixtures. Do **not** package SCC or third-party source in the released app/optimization ZIP. The full effective source plus plugin scripts are compiled in isolated workspaces.

## Current Phase 2 gate

The canonical REDscript CI job sparse-checks out `base_after_generic/r6/scripts` from the existing private repository and forwards it to the *same* `Verify-RealGrspResolverReplay.ps1` → `Verify-RealStackCompile.ps1` chain, using `-PostGenericSourceRoot`. The SCC owner script:

1. Requires a distinct, meaningful REDscript source tree; verifies no filesystem link files, duplicate/escaping relative paths, at least one approved generic source marker, and an existing framework source.
2. Compiles the actual post-generic source and existing installed framework under the isolated `phase2-postgeneric-stock` scenario, with independently checked official 0.5.31 compiler and existing effective/plugin inputs.
3. Preserves the original five SCC scenarios and all original generic/ZIP source tests.
4. Compares source hashes before/after to prove that all post-generic installed source remains byte-for-byte unchanged.
5. Selects comparison scenarios by **explicit name**, not assumed last array position. The Phase 2 baseline is independent and must not be falsely treated as an unoptimized or callback preview.

**Validation note:** The initial run [#988](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/44769788675) compiled all six scenarios successfully, including the post-generic source, but then failed at an obsolete last-position assertion for the framework scenario. Fixed at source in commit `ea2844dd`; [#989](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/44770961006) revalidates the corrected gate (record its final outcome before promoting).

## Semantic rule acceptance — extension to this SAME lane

No semantic generator is yet implemented in this checkpoint. **Do not claim that the Phase 2 baseline compile has tested semantic output.** For each approved semantic generator, extend this canonical SCC gate to compile its *actual* new/original-path staged output on top of post-generic source, plus all other accepted generic and semantic changes, with optional framework-owned files as needed.

Require real regression fixtures for:
- post-generic stock → complete semantic rule in one Generate;
- partially implemented → only missing changes, including markerless equivalent code;
- already complete → unchanged;
- compatible older/newer/custom-modified source → applied with unrelated edits preserved;
- genuinely incompatible target → only affected operation refused, with precise reason;
- repeated generation → zero further changes;
- complete combined output (including dependencies, cleanup and owner paths) → official SCC 0.5.31 in CI, full gameplay validation separately.

Version numbers and historical whole-file hashes must never become production semantic admission allowlists. Preserve GCO's one ZIP, original installed mod source, native owner restore, and the single CI workflow. **Only verified output and integration safety can authorize merging Phase 2 implementation into the stable app.**
