# GCO REDscript Phase 2 — Agent 6 / Historical Pass 8 isolation handoff

**Date:** 21 November 2026  
**Repository:** `giorgioherma/Gs-Cyberpunk2077_Optimizations`  
**Isolated branch:** `dev/redscript/pass8`  
**Fork point:** `96d66c66642ac52dd13fd4001416572b9169bace`  
**Tested code HEAD:** `f8439c427ccb1954b21a4ba296cddb2d9fc72822`  
**Pass 8 CI #11:** [SUCCESS, source-state and combined official SCC 0.5.31](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/44875634142)

## Scope / ownership

This is a **standalone source-aware implementation branch**, not the canonical release order. Do not merge it directly to stable `main`. A central integration owner must bring the rules into the existing `RedscriptGenerationPlan` after generics and previous compatible semantics, assign canonical stage IDs, update full integration validation, and separately run the GCO Windows application/combined ZIP workflow.

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
- `tests/GRedscript.Pass8.Checkpoint/`: independent .NET10 runner using `--owner chipware|combat|immersive`. No main `Program.cs`, canonical stage IDs or shared service changed.
- `tests/GRedscript.Resolver.Tests/test_pass8_{throughput,combat,immersive}.py`: structural stock, handwritten partial, complete, custom/moved source, absent, localized incompatibility and repeated-generation cases.
- `.github/workflows/redscript-pass8-checkpoint.yml`: **branch-isolated** test, source generation and SCC checkpoint. Sequentially generates chipware cheap paths, composes combat helper/callbacks against that overlay, joins independent ImmersiveCyberware output, validates actual merged files, compiles whole post-generic source with native dependencies using official CI-only SCC 0.5.31, and deletes private compiler/source before job exit.

**Checkpoint cadence:** the isolated workflow runs on manual `workflow_dispatch` or a deliberate update to `tests/GRedscript.Pass8.Checkpoint/CI-TRIGGER.txt`; ordinary source/test commits no longer start or cancel CI. Source validation for this handoff is the complete green run #11 above.\n\n## Verified result

- [CI #1 SUCCESS](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/44874902530): original three chipware cheap paths.
- [CI #5 SUCCESS](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/44875188945): three cheap paths plus two Immersive source methods and one helper.
- [CI #11 SUCCESS](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/44875634142): both above plus eight chipware movement/charge callbacks and chipware helper. **Expected isolated source files:** eight composed owner/helper files (`chipPerkUnlock`, `wreckingBall`, `flashAndThunderclap`, `kerenzikovUpdate`, chipware helper, two Immersive owners, Immersive helper). Reused already-optimized Quickslots/RedWindows are not counted again.
- CI #9 failed due an unused C# local under warnings-as-errors; fixed at source, and CI #11 is green. Intermediate superseded runs were cancelled by per-branch workflow concurrency.

The output is **not** a player ZIP, new gameplay/performance capture, full `.NET 10` GCO package or final integrated approval. Gameplay tests are still required (Kerenzikov equip/charge transitions, WreckingBall melee/sprint and stamina, Flash activation/overshield, ammo widget replacement, scanner setting changes, reload/death and player lifecycle). Never claim measured savings based on source callsite reductions.

## Remaining independent work / integration restrictions

1. **Historical chipware 24-entry 100ms perk result memoization** is not ported: the current source-derived early admission removes unrelated-perk stat work without moving all 24 branch returns, but caching mutable perk ownership/charges through a new player helper requires a complete source-state transition proof. Do not transplant the archived 208-line helper or 55-line historical whole-file switch.
2. Existing custom/missing-class partial helpers need explicit structural dependency proof before extending. The isolated generators refuse unproven helper ABI; they do not append unrelated code to an unknown same-named class.
3. At canonical integration, order cheap chipware gates **before** combat cache, then Immersive; compose all with the current post-generic source and previously staged semantics. Preserve prior generic markers, all newer/custom owner code, and narrowly scoped refusal reasons. If Pass 4/5 other agents touched the same chipware owner, independently compare semantic operations rather than discarding either.
4. Add the canonical one-invocation `RedscriptGenerationPlan` output/source-state and combined game-relative CET+REDscript ZIP/ownership contract. Run integrated full GCO CI and actual in-game A/B captures. Do not modify `main`, CET/GRSP/G-REDruntime owners, REDscript client SCC policy or the shared stage sequence on this isolation branch.

**Verified current status:** isolated historical Pass 8 throughput subset implemented, compiled, and tested. **Canonical acceptance and behavioral equivalence remain pending.**
