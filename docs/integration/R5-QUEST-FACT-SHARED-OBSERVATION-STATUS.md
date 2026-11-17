# R5 — Quest facts: first opt-in shared observation producer and source graph

**Date:** 2026-11-17. **Code ownership:** GCO managed Resolver on `dev/redscript/resolver_handoff`; G-REDruntime framework owner on `main`. **Scope:** source-first fact reader/writer/listener classification across every live REDscript owner, plus one optional source-owned framework observer. **Not deployed:** no existing mod or 1,013-method callback rewrite, no change to installed game source, no runtime gain, no final game-relative optimization ZIP.

## Why quest facts first

The user's proposed shared trigger set is a research model, **not** a reason to migrate unrelated live callbacks. Most 1,013 "unmeasured" methods contain multiple dependencies and 0 have a proven complete shared-trigger closure. The original 187 refers to source-matched F11 outgoing method evidence, not 187 player-state methods. The existing `DirtyFlags`, `EventBus`, `StateCache`, `ContextService`, `Scheduler` and `InputHub` remain authoritative; **no second runtime, global timer or broad poller**.

Current source already uses `QuestsSystem.RegisterListener(fact, receiver, callback)`, including correct `UnregisterListener(fact, originalID)` patterns. Existing mods return **different Bool values** from their fact callbacks, so replacing their listener or silently changing dispatch order is explicitly disallowed. Some fact names are computed dynamically, and `RegisterListener` is also used by unrelated UI/resource systems. Exact name/receiver and full callbacks must be source-proven.

## Implemented GCO source-wide fact evidence

- `src/GRedscript.Resolver/CallbackSemanticResolver.QuestFacts.cs` and CLI `scan-quest-facts --game <live-root> --out <isolated-json> [--analysis <sealed-capture-analysis>]` reuse the existing `SourceAdmission` and authoritative `CallbackSemanticResolver.ReadAllMethodSources` (one parser, one F11 verification path).
- Reads **all current REDscript methods** (not only 1,200 Update/Refresh/Tick names), discovers direct `GetFact/GetFactStr/SetFact/SetFactStr/RegisterListener/UnregisterListener` callsites, distinguishes exact `n"fact"` strings from dynamic/unknown arguments and source-declared `QuestsSystem` listeners from unrelated `RegisterListener` calls. The receiver declaration match is still a **limited source hint**, not proven runtime binding.
- The isolated report includes fact names and per-owner readers/writers/registered/unregistered listener sites for private local inspection. It explicitly sets `everyWriterAndAliasProven=false`, `receiverTypeProven=false`, `listenerCallbackReturnAndOrderProven=false`, `sharedSubscriptionAuthorized=false`, `generationAuthorized=false`, `sourceWrites=false`. Outgoing callsite evidence is not a fact-read or callback-entry count.
- **[GCO #615 SUCCESS](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/44096705852):** 2,319 current private `.reds` files; **24,276** indexed source methods; **2,799** exact-literal and **1,140** dynamic/unresolved fact-like callsites (**3,939** total); **961** distinct fact-like literal names in this conservative graph; **3** cross-owner fact names with source-declared QuestsSystem listener receivers; **203** listener callsites with unproved receiver type (not counted as shareable quest watchers). **101** managed tests; official SCC 0.5.31 with 10 then-current framework source files, 323 GCO package/install contracts. The separately uploaded source-only regex pass found 106 literal registration expressions / 94 names / eight cross-owner names under a **broader heuristic**; those are not equivalent to the three source-typed matches and must not be reported as proven duplicate registrations.

## Implemented owner G-REDruntime source

- `r6/scripts/G-RedRuntime/Events/QuestFactSignals.reds`, created and shut down from **existing** `Core/Runtime.reds`, accessed via `GetQuestFactSignals()`.
- **Opt-in only:** no `GetFact`, engine registration, timer, polling or callbacks when nobody calls `AcquireFact(fact: CName)`. First interested consumer adds **one** extra `QuestsSystem.RegisterListener` for that exact name; further consumers receive unique interest tokens and reuse the same registration. `ReleaseFact(token)` can only revoke that active token; the last release unregisters with the original returned engine ID and releases observer state.
- `Version(fact)` advances on every observed callback, even repeated values. `HasObservedValue(fact)` is false until an engine callback; `LastObservedValue(fact)` is **not** authoritative unless known. Missing framework, absent quest system, failed registration, unknown observation, save/load and source drift **must preserve the original GetFact path**.
- The existing `DirtyFlags.QUEST_FACT` generic flag is marked for any watched change; exact version remains per-fact. An existing `EventBus` `QUEST_FACT/CHANGED` event is published only when interested subscribers exist, with exact `nameValue` and `intValue`. On player attach/detach, observed values become UNKNOWN, versions advance, and one `QUEST_FACT/SESSION_INVALIDATED` event is published *after* the watch traversal. Original mod callbacks are unchanged.
- No wide fact registry, fixed mod names, guaranteed known values on registration, duplicate observer service, passive global quest scan or automatic migration.
- Detailed framework contract: [`documentation/QUEST_FACT_SIGNAL_CONTRACT.md`](https://github.com/giorgioherma/G-REDruntime_Framework/blob/main/documentation/QUEST_FACT_SIGNAL_CONTRACT.md). The framework manifest/version remains unchanged; this is a **candidate**.

## Real profiling context and verification

- User-supplied Nov 16 sealed F11 capture (~384.517s): `RSP_CallSites.csv` has **463,942 outgoing edges across rows mentioning GetFact**, and **283.8123 instrumented exclusive milliseconds** summed across those rows. This includes native/synthetic wrapper and user helper targets; it is **NOT** 463,942 confirmed original `QuestsSystem.GetFact` invocations, not cumulative total CPU and not a savings forecast. True payoff must be eliminating repeated expensive *work conditioned on fact state* rather than simply moving a tiny read to another service.
- **[GCO #620 SUCCESS](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/44098418784)**: managed 101 tests, **six** new framework-owner quest signal mutation/source contracts, real sealed 2,319-source replay, official SCC 0.5.31 `framework-all-generics-preview` exit 0 with all **11** G-REDruntime source files and 22 generic preview overlays; 323 GCO install/package contracts. Later source refinements enforce bounded token/watch storage and session-invalidating EventBus signaling; GCO #622 includes stronger contracts and must be checked separately before claiming those final refinements as verified.
- NO original mod installation, source capture, GRSP/CET branch, GCO `main`, WPF workflow or deployment overlay modified. SCC and source-mutation contracts **do not** prove in-game callback ordering/reentrancy, save/load/reload or measured elimination.

## Requested 16 observation families: current boundary

| Family | Existing source basis | Current permission |
| --- | --- | --- |
| Quest/fact | Actual QuestsSystem listener + new opt-in fact bridge | Candidate, no consumer rewrites |
| Player attach/detach and session transitions | Existing runtime authoritative player generation/invalidation | Source-verified, gameplay pending |
| Input actions | Existing InputHub opt-in bridge | Requires per-action return/consumption and ordering proof |
| Generic event delivery | Existing EventBus/HookBus | No blanket replacement of engine/mod callbacks |
| Live player/blackboard | Lazy ContextService and source-specific blackboard definitions | No event-exact shared value source |
| UI widget lifecycle | Ink source hooks and DirtyFlags | Requires widget identity, all writes and time effects |
| Time/animation | Existing Scheduler is deadline-oriented | Never suppress required frame/timer semantics |
| Resource/asset ready | Real Resource/PostLoad / owner callbacks | Exact path and callback order required |
| Inventory/stats | Game event APIs and mod source | Producer/receiver binding not proven |
| Config/settings | Original mod writer/setter paths | Per-owner writer invalidation required |
| Position/NEAR-FAR | CET native precedent, no typed REDscript ABI | Exact refresh/unknown proof missing |
| Status effect/buffs | Existing apply/remove callbacks across sources | Candidate: structure separately from live duration |
| Mod-owned field changes | Owner field/alias write graph | Complete writer and helper closure required |
| Entity/target lifecycle | Engine/spawn and de-spawn source patterns | Callback lifetime/cleanup required |
| Vehicle/mount state | Existing lazy ContextService 50ms getter | Event-exact producer not proved |

The listed line combines player session with attach/detach; 16 user-mentioned categories are covered conceptually but **not implemented as 16 active watchers**. Do not create a central per-frame scanner to simulate coverage.

## Next exact gate

1. Source-admit a **generic pure literal fact predicate** shape (e.g. multiple literal `GetFact` comparisons and a Boolean result) without hardcoding a mod. Require complete body control flow, all fact names/aliases and check for independent changing dependencies. Prioritize matched hot source evidence, refuse dynamic identities and synthetic receiver/wrapper ambiguity.
2. Compare the original live `GetFact` path with opt-in `AcquireFact/Version` and unknown fallback. Preserve original per-event callback invocation and return/ordering. Produce a **nondeployable** isolated preview only after structural proof; full SCC with current 11 source-owned framework files, synthetic mutation negatives, partial/completed state and source-drift idempotence.
3. User-led in-game load/reload/scene transition, repeated-value delivery, multiple subscribers, final unsubscribe and missing framework checks. Collect matched GRSP/CET/PresentMon F11 A/B before any savings statement. No automatic final optimizer ZIP or broad dependency suppression yet.

**Status: infrastructure and source graph committed, first source-contract SCC pass verified; game behavior/performance and first generated consumer integration remain open.**
