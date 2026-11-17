# R5 — quest-fact shared observation handoff and source admission

**Owner branch:** GCO `dev/redscript/resolver_handoff` for read-only resolver/optimizer admission; G-REDruntime `main` for shared service source. **Date:** 2026-11-17. **Status:** opt-in source-owned quest notification service and whole-source fact dependency analysis implemented; **no automatic replacement of mod callbacks, no fully covered refresh method, no game-relative ZIP, no measured savings.**

## Latest verified foundation

- [CI #622 SUCCESS](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/44099363200), code SHA `505c8ea1d8714167797e412f3dc7dba81056077e`, verified **101/101** GCO managed Resolver tests + **7/7** player-lifetime contracts + **7/7** quest observer contracts, original 2,319 source-file full-stack SCC 0.5.31 with **11** source-owned G-REDruntime framework files and earlier 22 generic preview files, **323** GCO application/package contracts, three-profiler report.
- Read-only `scan-quest-facts` analyzed **24,276 source methods**, located **2,799 exact literal callsites**, **1,140 dynamic/unresolved** and **961 distinct literal fact names** in the conservative graph. These callsites include nonquest `RegisterListener` names and source-unverified operations: exact method name does not prove receiver type or an engine-delivered notification.
- [CI #626 SUCCESS](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/44101848346) applied a stronger receiver-scope check and recompiled all 11 framework scripts. The source-typed cross-owner literal listener overlap dropped **3 → 1** and **228 listener receiver callsites remain unproven**. These figures correct the earlier less-specific lexical registrations inventory; one proven *source-declared type* is **not** proof of runtime event parity.
- **Subsequent code:** `CallbackSemanticResolver.QuestFacts.cs` now also outputs `directRefreshReaders` for exactly the existing `Update/Refresh/Render/Tick` method-name family, preserving per-method exact literal names, dynamic reads, local fields, helper calls, used parameters, optional sealed-F11 source-method evidence, and per-method skip refusal. `test_quest_fact_graph.py` tests cross-class type leakage; `Verify-RealGrspResolverReplay.ps1` enforces no unproven generator admission. Source test casing issue from CI #628 corrected at `70ffc33779ebac823237b80a59dd8b2aa4292a9e`; **CI #629 pending final result at documentation authoring**. No improvement claim before it passes.

## Framework API — owner source on `main`

- `Runtime.GetQuestFactSignals()`: initialized with the existing `Runtime`, **zero fact registrations/polls without opt-in users**.
- `AcquireFact(CName) -> Int32`: unique ownership token; one observer engine registration per exact fact, when successfully available. `ReleaseFact(token)`: independently owned idempotent cleanup, final-interest unregister, bounded storage, shutdown cleanup.
- `OnFactChanged`: increments per-fact generation on **every callback**, even identical values; maintains last observed Int32 only after the notification, marks existing `DirtyFlags.QUEST_FACT`, conditionally emits `EventBus QUEST_FACT/CHANGED`. Unknown first-use values **must use original live getter**.
- Player attach/detach invalidates cached observation, advances watched fact versions, marks dirty and conditionally emits `QUEST_FACT/SESSION_INVALIDATED`. UNKNOWN is not equivalent to 0. No new timer, global poll, native hook, hardcoded fact list or independent framework. The [authoritative contract](https://github.com/giorgioherma/G-REDruntime_Framework/blob/main/documentation/QUEST_FACT_SIGNAL_CONTRACT.md) documents cleanup/owner requirements.
- **Not yet proven:** engine delivery under reentrancy, save/reload, mod-optional lifetime, missing framework, aliased/dynamic writes, full writer/reader graph, return/dispatch semantics, or matched GRSP/CET/PresentMon speedup. Unchanged original mod callbacks remain authoritative.

## First consumer selection: actual live mod source

- Full live `r6LIVEnow.zip` source inspection identified **59** Update/Refresh/Tick/Render-family methods containing an exact literal `GetFact` call; all 59 have other fields, helpers or parameters (none is an obviously isolated short no-dependency body). This separate inspection is **directional**; use GCO's exact sealed source hashes to admit any optimized file.
- Examples: `VehicleMileage/UI/VMPriceSign.reds` has `RefreshFromFacts` but also widget and helper work; `CourierJobs/CJ_Runtime.reds` has literal fact reads mixed with timers, state mutation and updates; `MuchBetterContracts` already queues/coalesces many fact-triggered refreshes. Do not add a redundant global queue or replace existing fact callbacks. These examples are analysis fixtures, **not mod-specific recipes**.
- Only **one** source-typed fact appears in cross-owner literal listener registration after stricter scope proof. This makes naïve pooling of already-registered listeners a low-confidence ROI case; prioritize eliminating repeated source-proven **dependent computations**, not merely relocating registrations.
- No whole refresh method, fact getter or first consumer has an authorized rewrite at this checkpoint. Mod-owned writers, quest engine/native writers, reentrant event delivery and callbacks with side effects remain blockers.

## Next acceptance contracts

1. Pick one *measured* recurring generic derived-data expression conditional on an exact quest fact. Close all local/transitive writers and nonfact dependencies; preserve visible refresh timing, UI lifecycle and original callback effects. Dynamic or unsupported fact names remain original.
2. Prove exact event subscription lifetime, duplicate updates, registering during dispatch, shutdown, player detach/attach, reload and fallback when framework or registration unavailable. Never infer full coverage from one shared category.
3. Add an owner-source generator **only when** an actual end-to-end equivalence proof exists, with untouched/partial/fully optimized and source-drift/idempotence refusal. Compile exact game-relative output with official SCC 0.5.31, not only baseline framework compilation.
4. User-led in-game tests and matched sealed GRSP + CET + real PresentMon A/B are mandatory before any gain estimate or deployment-ready optimized ZIP. Preserve the one-folder, two-page GCO design and component ownership.

**Research tools:** GCO `scan-shared-work` and `scan-quest-facts --game <live-root> --analysis <verified-sealed-analysis> --out <isolated-path>`, both read-only, both via single `CallbackSemanticResolver` parser. The latter's private output contains source paths; CI logs disclose only aggregated counts.
