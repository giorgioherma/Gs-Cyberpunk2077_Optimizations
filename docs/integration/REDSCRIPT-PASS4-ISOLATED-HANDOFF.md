# REDscript historical Pass 4.0 — isolated combat handoff

Date: 2026-11-21. Branch: `dev/redscript/pass4`, based on green parent `96d66c66642ac52dd13fd4001416572b9169bace`. Do **not** cherry-pick changes from another agent's unfinished working branch.

## Ownership boundary
- This branch owns the three Pass 4 generators and their tests, the standalone and production shared-source build links, the canonical `RedscriptGenerationPlan.cs` integration, `.github/workflows/redscript-pass4-checkpoint.yml`, and this handoff. It does not edit the handoff agent's branch, `main`, the existing Phase 2 shared workflow or CET/GRSP/G-REDruntime source.
- Pass 4 is **integrated into the production one-run Stage on this branch** using provisional orders **#37 StealthRunner, #38 chipware and #39 ReinforcementsSystem**. Production and isolated CLI compile **the same source-owning implementation**; no second player-side generator, parser or corrective pass is shipped. Reconcile stage numbers when merging other agents' stages. `src/GRedscript.Resolver/Program.cs` was not edited.
- Reuses `SourceAdmission.cs` from GCO via a linked compile item, not a forked lexer or source parser.
- No changes to `main`, installed game, CET/GRSP/G-REDruntime owner sources or profiler artifacts.

## Pass 4 historical audit (six files, five owners)
- **DigitalVixenCore/DVCore_Footsteps.reds**: historical successful RTTI class cache already covered by semantic **#11**. Not duplicated.
- **HQ_HoloGirl/Engine/HoloGirl.reds**: historical holo material event/cache and approved Pass 5 value hoist already covered by **#22**. Not duplicated.
- **StealthRunner/npc_puppet.reds**: isolated `STEALTHRUNNER_NPC_PLAYER_CACHE_V1`. Adds weak NPC-local player handle, stores only at existing activation, clears on detach, resolves on cache miss in detection handler. Leaves original quest registration, wrapped handlers, NPC and detection logic unchanged.
- **zzy_bChipwareExpansion/chipsScripts/chipPerkUnlock.reds**: isolated `CHIPWARE_PERK_RELEVANCE_GATE_V1`. Builds the relevant-perk predicate **from source actual branches** rather than hard-coded historical 24-name set, skips costly game/player/stat reads only for unrelated perk type, always preserves `wrappedMethod(perkType)` fallback. Supports source-added eligible perk branches and partial predicates.
- **ReinforcementsSystem/Utils.reds + ReinforcementSystem.reds**: isolated `REINFORCEMENTS_PREPARED_QUAD_CACHE_V1`. Prepares sorted quad data at player attach, verifies all eight live coordinate components on each cached use, uses original `IsPointInQuad` (including generic safe AABB) on any mismatch/missing entry. Original public `restrictedCordinates` remains mutable and original `SortSquare` and `IsPointInQuad` methods are unchanged. All generic markers present in the actual input are retained; compatible markerless geometry is admitted after source-structure/purity proof. Modified triangle flow, nonlocal sorting dependencies, unsupported broadphase logic and unsafe other mutations are narrowly refused. Existing RNG, quest, district, NPC eligibility and callback cadence are unchanged. Clears owner cache on detach. This deliberately does **not** bring the old 250 ms context polling state, which could delay quests/district/race transitions; that independent historical operation requires source/timing proof.

## Generator / acceptance
- CLI `dotnet GRedscript.Pass4.dll pass4 --game <post-generic-root> --out <isolated-overlay> [--base-overlay <prior-overlay>]`.
- JSON `pass4-result.json` lists each independent owner status, edit counts, source paths, precise safe-refusal reason and source-stack fingerprint. Output includes only .reds files under game-relative paths and remains outside installed source.
- Validate untouched → applied, partial → missing completed, complete → no-op, relocated/custom → applied, incompatible → narrowly refused, absent → no output, repeated run → zero new changes. The independent fast CI must validate source-state fixtures, original private 2,331-file post-generic input, generated merged `.reds` SCC 0.5.31 and original-byte immutability.
- The **dedicated** CI tests both isolated Pass 4 and the **actual canonical production one-run Stage** after all integrated stages on this checkpoint, with byte-for-byte identity of the four accepted Pass 4 output files. Official SCC compiles the full resulting game-relative source overlay; GCO's app and Core test project build and run. This is source/build validation, **not** full packaged publisher/release CI, a game-tested ZIP, or a measured FPS/CPU result.

## Validated checkpoint (2026-11-21)

- **Code commit:** `a3afca3b1450bc2469c5799fa3bd3c18947c4ca8`.
- **Independent [Pass 4 CI #3 SUCCESS](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/44874456828)** (run ID `44874456828`).
- .NET 10 isolated generator and canonical GCO SCC runner both built successfully.
- **10/10** owner source-state tests passed: post-generic/stock, partial, complete/no-op, custom/relocated, absent, incompatible, original source unchanged and sibling callsite owner disambiguation.
- Real authoritative post-generic input: **2,331** .reds files. **3 accepted independent owner operations**, **15** generated source edits across **4** game-relative .reds files.
- Repeated generation on first overlay: **COMPLETE, 0 edits, 0 output files**.
- **Official SCC 0.5.31 SUCCESS** on merged full post-generic real source plus the four isolated Pass 4 outputs: **43,485,218 compiled bytes**; original sources unchanged.
- Initial CI #1 found improper partial-chipware relevance recognition. CI #2 found discovery ambiguity from sibling method callsites. Both were fixed in the source-owning generator and regression contracts. No corrective pass was introduced.
- The subsequent production integration and stricter structural geometry proof are validated in independent Pass 4 CI #10 below. The **official full publisher/package workflow after cross-agent merge**, actual in-game ZIP parity, and measured gameplay/performance remain pending. Do not equate a successful SCC with real-world gameplay approval.

## Verified production integration checkpoint (2026-11-21)

- **Validated code commit:** `06f1b83c1020d0549e46a1364c1a9b7c76ef7902`.
- **[Dedicated Pass 4 CI #10 SUCCESS](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/44875974828)** (run ID `44875974828`). The branch is isolated; no full publisher workflow was triggered.
- **12/12 source-state tests**, including partial → missing work, fully complete → no-op, relocated/custom owners, markerless compatible geometry, side-effectful sort and altered triangle-flow refusal, absent owner and no duplicates.
- **.NET 10 app build SUCCESS** and **310 Core installation/CLI/package contracts PASS** (these contracts did not inspect a published Pass 4 ZIP).
- **Real source:** 2,331 post-generic `.reds`; independent Pass 4 stage applied **15 edits in 4 game-relative files** across **3 owners**; repeat output COMPLETE **0 edits / 0 files**.
- **Official SCC 0.5.31 PASS** for the four isolated sources: **43,485,218 compiled bytes**, unchanged original source. **Canonical GCO one-run Stage PASS**: ten generic families + existing semantic stages #11–#36 + provisional Pass 4 #37–#39; **all 4 Pass 4 files byte-for-byte equal** to isolated output. Full **33-file semantic overlay SCC 0.5.31 PASS**, **43,515,691 compiled bytes**, original source unchanged.
- Early integration run #5 passed canonical stage and SCC but its *app build validation command* failed NETSDK1150 from incorrectly building a self-contained WPF app against the framework-dependent Resolver. Next run #6 compiled the app and all REDscript sources but a new Core test command failed NETSDK1004 due to `--no-restore`. Both were CI harness mistakes corrected by configuring a framework-dependent **build-only** app check and restoring Core test dependencies; the original self-contained **publish** properties and production code were not changed. No source repair passes.

## Remaining work / responsibility
1. Cross-agent integration owner merges the completed branch into the newer shared resolver source, reconciles provisional stage orders #37–#39 with concurrent Pass 2.2/3/5 additions, and reruns full combined SCC and the real GCO package/ZIP workflow. **Do not cherry-pick outdated central files wholesale over newer work**; rebase the three tested generator calls into the live plan.
2. ReinforcementsSystem historical context scheduler/racing/district/quest throttling remains unported pending event/timing safety proof; do not silently delay dynamic gameplay-state checks.
3. zzy chipware Pass 5/6/7/8 operations remain distinct and cannot be duplicated by this Stage.
4. Real gameplay parity and comparative F11/PresentMon captures remain pending. There is **no verified performance improvement yet**.

This is **integrated and SCC-validated in the isolated Pass 4 branch**, not yet merged into the cross-agent release branch or game-tested.
