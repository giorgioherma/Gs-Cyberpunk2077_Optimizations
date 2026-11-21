# GCO REDscript Phase 2 — Agent 6 / Historical Pass 8 isolation handoff

**Date:** 21 November 2026  
**Repository:** `giorgioherma/Gs-Cyberpunk2077_Optimizations`  
**Isolated branch:** `dev/redscript/pass8`  
**Fork point:** `96d66c66642ac52dd13fd4001416572b9169bace`  
**Tested production code HEAD:** `c981ad0fb5567a743291cf9bfd4c33cc5a7ed9f8`  
**Pass 8 CI #11:** [SUCCESS, source-state and combined official SCC 0.5.31](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/44875634142)

## Scope / ownership

This branch originally held only standalone generators. It now **also contains the actual canonical GCO one-pass integration** as stages #37–#39. No change was made to the central handoff branch or stable `main`. Subsequent multi-agent integration must reconcile any overlapping Pass 4/5 operations and later stage numbering, preserving structural guards and one-pass behavior.

## Canonical one-pass integration (21 November 2026)

- **#37 `pass8-chipware-throughput`:** source-local chipware perk admission and two cheap combat gates.
- **#38 `pass8-chipware-combat-cache`:** 8 Kerenzikov/WreckingBall/Flash combat callbacks plus source-owned cache helper.
- **#39 `pass8-immersive-cyberware`:** ammo/FocusScanner source paths plus helper.
- `RedscriptGenerationPlan.Stage` invokes `Pass8CanonicalComposition` directly after existing generics and stages #11–#36. Each stage generates against the previously composed effective-preview and merges only declared owner source files. It records partial refusals independently, preserves generic markers, rejects path escapes, unexpected paths, empty/unchanged outputs and output count drift.
- `CombinedOptimizationPassService` automatically includes the canonical effective-preview, completed stage metadata and source-local refusal reasons in its existing single game-only ZIP/external ownership sidecar, without client-side SCC.
- `tests/GRedscript.Pass8.Checkpoint/Verify-CanonicalPass8.ps1` compares all 8 Pass 8 owner/helper source files **byte-for-byte** with independently generated output and compiles the full combined source using official SCC 0.5.31. **Pass 8 CI #12 SUCCESS** ([run](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/44876279303)), 21 Nov 2026.
- Full GCO replay `Verify-RealGrspResolverReplay.ps1` independently composes stages #34–#39, checks canonical count/refusal/byte parity, and verifies the existing game-relative ZIP contract. Full app workflow now runs on this isolated branch.
- **[Full GCO CI #1103 SUCCESS](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/44878479063)** at `c981ad0f`: captured 2,319-file source independent stage #34–#39 composition matched the canonical one-run preview (101 game-relative REDscript files) and official SCC 0.5.31 with real native/framework sources; .NET 10 WPF app, CET/GRSP installer/restore, full package and final ZIP verification green. Published `Gs-Cyberpunk2077_Optimizer-v1.2.5-run1103-a1` (artifact ID `14697347017`, 90,583,017 bytes). The CI SCC harness now accepts only the two new Pass 8 helper sources when their hashes, producing stage, owned mod folder, and PlayerPuppet class/field/method ABI match; unknown additive files remain refused. #1095/#1097/#1099/#1102 failed on successive CI-harness gaps, resolved without weakening safety or altering already green generated gameplay source.
- Remaining: 24-perk timed memoization still unported for insufficient state/fallback proof; no freshly generated game-side behavioral or measured performance validation. Do not call this a stable release or merge it into `main`.


Historical Pass 8 contains four owner groups:
- `custom_quickslots`: automatic input-device cache #17 already implemented, **not duplicated**.
- `RedWindows`: lookup/cursor/setter optimization stages #19/#23/#26 already implemented, **not duplicated**.
- `ImmersiveCyberware`: two actual post-generic owner methods plus one additive helper, **newly implemented**.
- `zzy_bChipwareExpansion`: existing 24-perk query early admission and two cheap eligibility gates, followed by eight source-specific combat stats cache callbacks and one helper, **newly implemented**.

Source priority is `original_cet_source/main/base_after_generic/r6/scripts` (2,331 files); archived `G-RED_optimizatios_seperate_passes.zip` is **behavioral reference**, never copied as a replacement. Actual installed effective source decides eligibility. No absent owner is emitted.

## New files and isolated runners

- `src/GRedscript.Resolver/Phase2/Pass8ThroughputPass.cs`: three independent chipware cheap-path operations. Source-derived 24-perk admission guard before stats; non-melee WreckingBall gate; non-Gorilla FlashAndThunderclap gate. Recognizes manually pre-applied perk switch/guards and preserves original `wrappedMethod`.
- `src/GRedscript.Resolver/Phase2/Pass8ChipwareCombatCachePass.cs`: 8 callback/decision transformations, plus additive `GRedChipwareHotpathCache.reds`, 50ms historical equipment stat refresh. Kerenzikov three methods, WreckingBall three, FlashAndThunderclap two. Existing callback cadence, branch return/fallbacks, charges and capacity checks remain source-owned.
- `src/GRedscript.Resolver/Phase2/Pass8ImmersiveCyberwarePass.cs`: ammo HUD conditional visibility/scale and FocusScanner cached stat/config with source-sampled 100ms stat / 500ms settings cadence, plus additive `GRedICHotpathCache.reds`. Tracks widget identity for first render/replacement and retains wrapped callback.
- `tests/GRedscript.Pass8.Checkpoint/`: independent .NET 10 runner using `--owner chipware|combat|immersive` plus canonical byte-parity/SCC verification. Main `Program.cs` is unchanged; `RedscriptGenerationPlan.Stage` and `CombinedOptimizationPassService` now compose stages #37–#39 in the existing single ZIP path.
- `tests/GRedscript.Resolver.Tests/test_pass8_{throughput,combat,immersive}.py`: structural stock, handwritten partial, complete, custom/moved source, absent, localized incompatibility and repeated-generation cases.
- `.github/workflows/redscript-pass8-checkpoint.yml`: **branch-isolated** test, source generation and SCC checkpoint. Sequentially generates chipware cheap paths, composes combat helper/callbacks against that overlay, joins independent ImmersiveCyberware output, validates actual merged files, compiles whole post-generic source with native dependencies using official CI-only SCC 0.5.31, and deletes private compiler/source before job exit.

**Checkpoint cadence:** the isolated workflow runs on manual `workflow_dispatch` or a deliberate update to `tests/GRedscript.Pass8.Checkpoint/CI-TRIGGER.txt`; ordinary source/test commits no longer start or cancel CI. Source validation for this handoff is the complete green run #11 above.\n\n## Verified result

- [CI #1 SUCCESS](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/44874902530): original three chipware cheap paths.
- [CI #5 SUCCESS](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/44875188945): three cheap paths plus two Immersive source methods and one helper.
- [CI #11 SUCCESS](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/44875634142): both above plus eight chipware movement/charge callbacks and chipware helper. **Expected isolated source files:** eight composed owner/helper files (`chipPerkUnlock`, `wreckingBall`, `flashAndThunderclap`, `kerenzikovUpdate`, chipware helper, two Immersive owners, Immersive helper). Reused already-optimized Quickslots/RedWindows are not counted again.
- CI #9 failed due an unused C# local under warnings-as-errors; fixed at source, and CI #11 is green. Intermediate superseded runs were cancelled by per-branch workflow concurrency.

The isolated output is **not independently shipped as a player ZIP**; the canonical production generator now includes these Pass 8 operations in the existing combined CET+REDscript output and **full .NET 10 GCO package CI #1103 passed**. Gameplay tests are still required (Kerenzikov equip/charge transitions, WreckingBall melee/sprint and stamina, Flash activation/overshield, ammo widget replacement, scanner setting changes, reload/death and player lifecycle). Never claim measured savings based on source callsite reductions.

## Remaining independent work / integration restrictions

1. **Historical chipware 24-entry 100ms perk result memoization** is not ported: the current source-derived early admission removes unrelated-perk stat work without moving all 24 branch returns, but caching mutable perk ownership/charges through a new player helper requires a complete source-state transition proof. Do not transplant the archived 208-line helper or 55-line historical whole-file switch.
2. Existing custom/missing-class partial helpers need explicit structural dependency proof before extending. The isolated generators refuse unproven helper ABI; they do not append unrelated code to an unknown same-named class.
3. **Canonical integration is implemented:** cheap chipware gates precede combat cache, then Immersive; all previous generics/semantics are preserved on the current effective source. During central multi-agent integration, independently compare Pass 4/5 overlapping chipware owner operations and renumber stages as needed without losing behaviors.
4. **Implemented and CI validated:** canonical one-invocation `RedscriptGenerationPlan`, source-state/byte-parity, and combined game-relative CET+REDscript ZIP/ownership contracts passed full #1103. Still required: actual in-game A/B (Kerenzikov, WreckingBall, Flash, ammo HUD, FocusScanner, reload/death and 50/100/500 ms transition behavior). No merge to `main`, no changes to other component owner repositories.

**Verified status:** Pass 8 isolated SCC #12 **SUCCESS** and full integrated GCO CI #1103 **SUCCESS**. Canonical one-pass staging, two additive helpers, ownership sidecar, ZIP, official SCC and unified Windows package are validated. **Gameplay parity, performance capture and multi-agent branch integration remain separate release gates.**
