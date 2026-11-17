# R5 — bidirectional quest fact subscriber cohorts and partial-work boundaries

**Owner:** GCO managed REDscript Resolver on `dev/redscript/resolver_handoff`, plus source-owned G-REDruntime quest observer in framework `main`. **Status (2026-11-17):** read-only full-source evidence and listener identity guard, **no automatic game-source rewrite, zero approved partial getter replacements, zero whole-method skips**.

## 2026-11-17 — Source-token partial-value continuation

[New R5 per-value source and F11 evidence handoff](R5-PARTIAL-QUEST-VALUE-FLOW.md), commit `8af047c`. Preserve all existing bidirectional cohorts and identify value-level subscribers even inside mixed methods. The reverted six-guard experiment is **not** the subscriber admission ceiling. No getter or method rewrite authorized. **CI #646 SUCCESS (source `8af047c`):** 108 managed Resolver tests; 7 player + 8 quest framework source contracts; 2,319 live REDscript sources and official SCC 0.5.31 with 11 framework files; 323 GCO installation/package contracts. Bidirectional source rows: **1,465** literal read sites / **753** exact-name groups. New per-value evidence: **1,456** reads inside mixed-dependency methods, **287** source-measured method inputs, **493** local-value assignments, **855** direct or local downstream-condition inputs, including existing **145** same-owner helper reads. These are site counts, not distinct optimized methods; **0** authorized rewrites, **0** gameplay tests or measured savings. [Run #646](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/44123182727).

## 2026-11-17 — Two-sided value-flow comparison within exact fact cohorts (CI #647)

Commit [`3cd1b5f`](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/commit/3cd1b5f92282eceffaee6b63d26ede53d4351b4c) extends the EXISTING per-fact cohort output (no second parser/grouping system): per fact name, compare direct conditions, local/derived-value uses, source-matched consumers and independent method inputs, explicitly showing conditional+other-value consumers together. Original methods, wrappers, getter fallback and mod ownership remain untouched. Full private CI [#647](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/44125762276) is the authoritative verification result; never infer production readiness from grouped source matches. All rewrite/engine-event/derived-purity authorizations remain false.

## The two-sided subscriber rule

An optimization unit is a **specific exact fact dependency or downstream pure computation**, NOT every method that references that fact. A method with other mutable inputs is not disqualified from observing the exact fact; it is disqualified from having its **whole body** suppressed based solely on the fact generation.

- **Shared compatible input:** same literal fact `CName`, verified quest-system producer and callback, source-owner lifetime, same source state epoch, no missing writers.
- **Independent/incompatible remainder:** changing UI/visibility, input action dispatch, local fields, callback arguments, player/mount/blackboard state, timers, animations, event effects, resource readiness, dynamic fact expressions and aliased/helper work continue on the *original* method path unless separately proven.
- **Token ownership:** `QuestFactSignals.AcquireFact(CName)` supplies an independent positive release token, with one pooled observer registration per watched name. `ReleaseFact(token)` stops that consumer; last interest unregisters the engine bridge. The service requires `HasObservedValue` for its value to be known and has source-backed stale-bridge identity protection; UNKNOWN or missing subscription uses the **original GetFact**.
- **Must-not-confuse:** token ownership and source-literal matching do **not** prove all quest/native writers are observed, callback dispatch is equivalent, or a getter can be removed. The current scanner sets `canReplaceOriginalGetter=false` and `canSkipOriginalMethod=false` for every read.

## Real source: compare BOTH sides of the same fact

1. **Input vs UI:** `r6/scripts/negotiableaffection/Main/NA_DialogueChoicePopup.reds` `PlayerPuppet.OnAction` checks `na_active` before normal action/consumer logic and `wrappedMethod`. It is a *read input* candidate but **never a safe wrapper skip**. The sealed F11 outgoing callsite to `questQuestsSystem::GetFact` recorded 274,553 call edges and 124.77ms direct instrumented exclusive in the complete capture, not projected savings.
2. **Helper vs direct:** `r6/scripts/CourierJobs/CJ_Runtime.reds` declares `GetFact(name: CName)` which reads `QuestsSystem.GetFact(name)` with source-null fallback. `UpdateStorageHUD`, `UpdateMetricsHUD`, `Tick` and other methods call `this.GetFact(n"...")`. The original 67-method direct-getter scan omitted many such helper callsites. The expanded read-only method-scoped helper recognition reports these as **source shape**, not proven pure/inlinable getters; the unchanged getter remains the fallback.
3. **Mixed dynamic state:** `r6/scripts/VehicleMileage/UI/VMFuelGauge.reds` `ApplyVisibilityGate` reads `vm_hud_visible` and `vm_fg_enabled`, but also checks vehicle UI power, blackboard state, modal depth and handles alarm and widget cleanup. A shared fact input can eventually be adopted **without freezing any of those other decisions**.
4. **Already event-driven owners:** `MuchBetterContracts` uses fact listener callbacks and a queued refresh guard. Do not add another global refresh queue or redundant callback subscription without first measuring net saved work.
5. **Cross-owner exact fact:** literal `ngplus_active` appears in the live source of several distinct owners, showing why fact-name sharing and subscriber identity can be discovered without fixed mod inventories. Source lexical overlap alone does not prove notification parity.

The source owner list above provides **counterexamples**, not a hardcoded mod catalog or authorized optimization recipes.

## Authoritative implementation

- GCO `src/GRedscript.Resolver/CallbackSemanticResolver.QuestSubscriberCohorts.cs` reuses **the existing single** `ReadAllMethodSources`, `SourceAdmission` tokens, `InspectDirectDependencies`, `ReadEvidence` and source hash seal. **No** parallel parser and **no** fixed mod/fact list.
- Read-only `scan-quest-facts` now emits `questSubscriberReads` and `questSubscriberCohorts` for **all 24,276 parsed current methods**, including direct literal getters and narrow **same-owner/same-file `this.GetFact(n"...")` helper shapes**; unrelated same-name wrappers refuse.
- Every read retains exact source path/function/line/fact name, read shape, same-owner helper witness, whether it belongs to the 1,200 refresh family, measured outgoing method edges (NOT a fact-call count), other field/helper/parameter dependencies, possible dynamic other facts, and explicit false authorization flags.
- Cohorts distinguish exact-name methods across owners, refresh vs non-refresh, direct vs owner helper, simple lexical vs mixed inputs, measured vs unmeasured. **The two sides are compared even where methods cannot sleep wholesale.**
- No transformation or output ZIP is emitted; captures, original scripts, CET, GRSP, stable GCO `main` and live user installation are untouched.

## Verified results

**[GCO CI #638 SUCCESS](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/44110788827)**, code `a354fe954ad8d822330538431e641e13cfc96c9b`:

| Proof contract | Result |
|---|---|
| Actual game sources / methods examined | 2,319 files / 24,276 methods |
| Exact literal read sites across all methods | **1,465** |
| Distinct exact-name reader cohorts | **753** |
| Fact names read across different source owners | **28** |
| Recognized same-owner fact wrapper read sites | **145** |
| Cohorts mixing direct and owner-wrapper reads | **27** |
| Cohorts containing both simple-lexical and other-input methods | **6** |
| Subscriptions or partial getter substitutions authorized | **0** |
| Whole method skips authorized | **0** |
| Managed .NET10 tests | **105 pass** |
| Framework owner contracts | **7 player lifecycle + 8 quest observer pass** |
| Official SCC 0.5.31 full private stack, 11 framework scripts, existing generic previews | **Pass** |
| GCO installation/package contracts | **323 pass** |
| In-game callback parity / speedup | **Not tested / not claimed** |

**[GCO CI #639 SUCCESS](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/44111739033), code `adff7c560c768e54eedb2acb7547bc6e6813a393`: **106/106** managed .NET10 Resolver tests (including preserving `wrappedMethod` for shared-fact OnAction), **7/7** player and **8/8** quest observer source contracts; private 2,319-file real-source replay, 1,465 literal fact reads / 753 cohorts / 28 cross-owner facts / 145 narrow same-owner helper reads, official SCC 0.5.31 full-stack with 11 authoritative framework files and existing generic previews, **323/323** GCO package contracts, combined CET+GRSP+PresentMon report PASS. Portable GCO **app** artifact `run639-a1` ID `14340384025` is not a game-relative optimization ZIP. **No authorized source substitution, gameplay test or measured improvement.****

## Exact remaining authorization gate

1. Identify a measured **pure subexpression** within a consumer. Trace direct and helper state, all aliases, member writers and engine/native fact writes; separate changing nonfact inputs. Source keys and method names do not prove complete closure.
2. Prove watched fact registration, initial fallback, callback delivery/order, repeated values, player/session invalidation, save/load, stale listener replacement, optional/missing G-REDruntime and original writer parity. A missed callback makes a stale getter reuse unsafe.
3. Retain the rest of each original method unchanged. A source-owned optimizer may replace **only** the proven input/computation; keep original `GetFact` for UNKNOWN and source drift. Untouched/partial/fully optimized source and idempotence contracts must pass.
4. Compile actual generated original-path REDscript overlay using official SCC, then user-led game tests and matched sealed GRSP/CET/PresentMon A/B. No claims of CPU/FPS gain from outgoing-call counts.

**Do not conflate “shares an exact observed fact” with “fully covered by shared observation.”**
