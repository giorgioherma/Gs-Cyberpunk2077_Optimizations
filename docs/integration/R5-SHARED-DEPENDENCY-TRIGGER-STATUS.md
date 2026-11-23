# R5 — Shared trigger dependency inventory for all 1,200 REDscript methods

**Verified:** 2026-11-17, [GCO Windows CI #607 SUCCESS](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/44089551952) for code commit `5a6085347611198be992c667d50b570d20bd8c1f`. **Scope:** source-wide read-only dependency discovery of existing update/refresh/tick *method-name shapes*. These are **not 1,200 proven recurring engine callbacks** and **not 1,200 eligible optimizations**.

## Correct the 187/1,013 misunderstanding

- **187** methods had measured outgoing F11 callsites cryptographically source-matched to the current private stack by the existing GCO admission logic.
- **1,013** methods **did not have source-matched measurements** in this capture. This is NOT a group of functions without dependencies and does NOT imply they all check player state.
- The 1,200 broad method-name matches span the authoritative **2,319 live `.reds` source files**; the separately uploaded `r6LIVEnow.zip` source-only inspection recovered the same 1,200 methods in 413 files and 101 top-level source folders. Families: 652 Update/Render, 378 Refresh/Rebuild/Populate and 170 Tick/Maintenance.
- Direct player-identity lexemes appeared in only **70/1,200**, comprising **19/187 measured** and **51/1,013 unmeasured**. This is still just source-token evidence, not proof that those functions rely solely on attach/detach state.

## Authoritative dependency counters (overlapping)

| Direct source-token/declaration evidence | All 1,200 | Measured 187 | Unmeasured 1,013 |
|---|---:|---:|---:|
| `this.field` instance references | 902 | 139 | 763 |
| `this.Method()` same-object calls | 829 | 134 | 695 |
| Used callback/method parameters | 431 | 105 | 326 |
| UI/widget API/name hints | 237 | 29 | 208 |
| Collection operations | 210 | 43 | 167 |
| Quest/fact/journal hints | 91 | 18 | 73 |
| Event/lifecycle hints | 87 | 38 | 49 |
| Player identity/handle hints | 70 | 19 | 51 |
| Resource/asset hints | 60 | 11 | 49 |
| Clock/time hints | 37 | 12 | 25 |
| Configuration hints | 33 | 6 | 27 |
| Inventory/equipment/stat hints | 28 | 6 | 22 |
| Blackboard-like API hints | 28 | 5 | 23 |
| Spatial/proximity hints | 20 | 4 | 16 |
| Live player state hints | 18 | 1 | 17 |
| Input/device hints | 10 | 1 | 9 |
| External/reflection hints | 1 | 0 | 1 |

**Counts overlap**: a function can read three fields, call helpers, use a new callback argument, mutate widgets and get quest facts in the same execution. Some callable names (e.g. `GetInt`, `SetData`) are not receiver/type proofs. The engine might read or mutate the true value through an entirely different alias or native producer.

## Primary investigation categories (one priority per method)

The mutually exclusive primary classification is **not an inferred source of truth or permission to register an event**. It orders next proof work, not deployment.

| First research classification | All | Measured | Unmeasured |
|---|---:|---:|---:|
| Instance fields; writer set not closed | 300 | 32 | 268 |
| Fresh arguments | 204 | 33 | 171 |
| Widget/UI identity and lifecycle | 191 | 23 | 168 |
| Quest/fact publisher needed | 85 | 17 | 68 |
| Instance helper effects unresolved | 68 | 4 | 64 |
| Event delivery and lifecycle effects | 84 | 36 | 48 |
| Local/unresolved dependencies | 46 | 0 | 46 |
| Resource ready/reload | 44 | 5 | 39 |
| Timer/animation | 47 | 12 | 35 |
| Player/blackboard live updates | 27 | 3 | 24 |
| Player identity attach/detach | 29 | 8 | 21 |
| Inventory/stat publisher | 26 | 5 | 21 |
| Configuration changes | 21 | 5 | 16 |
| Position/NEAR-FAR geometry | 19 | 4 | 15 |
| Input routing | 9 | 0 | 9 |
| **TOTAL** | **1,200** | **187** | **1,013** |

These first-category numbers differ from raw overlapping evidence by design. E.g. 70 player-identity hints occur, but only 29 receive player identity as their **first** candidate; other methods also contain higher-priority changing time/quest/UI dependencies.

## Evidence from actual current source: which events already exist?

- `QuestsSystem.RegisterListener(factName, receiver, n"OnFactChanged")` is used by current mods including DigitalVixenCore and MuchBetterContracts; the latter already queues refresh requests on fact change. **Fact names, registration/unregistration lifetime, delivery order and return semantics differ** and must be proven before any generic shared listener.
- REDscript callback bodies `OnStatusEffectApplied` and `OnStatusEffectRemoved` appear in multiple current source owners, providing a plausible **buff/list-structure invalidation** route; remaining cooldown **time still changes every tick**, so skip only provably unchanged structural preparation, not live timers/animations.
- `G-REDruntime`'s existing framework owner `main` now has an authoritative attach/detach player-identity signal from prior GCO #593. This **does not** make combat, position, target, inventory or blackboard state immutable.
- MuchBetterContracts already uses `m_refreshQueued` and `QueueRequest` to coalesce refreshes; a second global refresh queue could be redundant or break order.
- `OnUpdate(ResourceEvent)` and ink view refreshes are not universally polled callbacks. Event-driven resource handling, widget recreation and changing visibility must remain correct.
- Typed REDscript native NEAR/FAR, a complete shared quest fact publisher, and per-widget dependency graphs are **not implemented or approved** simply because a method has the matching lexical hint.

## Source-owned implementation and CI

- GCO owner branch `dev/redscript/resolver_handoff`: `src/GRedscript.Resolver/CallbackSemanticResolver.SharedDependencies.cs` classifies direct instance fields vs `this.Method()`, used parameters and overlapping generic engine-API hints. It is invoked from existing `CallbackSemanticResolver.SharedWork.cs`, sharing the **single** authoritative source parser, method index, exact sealed F11 source SHA provenance and source-drift refusal.
- Existing `scan-shared-work --game <current-game-root> --out <isolated-json> [--analysis <sealed-GRSP-resolver-analysis>]` now provides `directDependencyCategoryCounts`, `measuredDependencyCategoryCounts`, `unmeasuredDependencyCategoryCounts` and the corresponding primary/multi-trigger distributions, plus per-method `directDependencies`. New direct arrays distinguish instance field vs local method call vs used argument; **completeCallGraphProven=false`, `completeWriterGraphProven=false`, `sharedTriggerProven=false`** on every row.
- **[GCO #607 SUCCESS](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/44089551952):** **97** managed .NET 10 REDscript CLI/safety tests (including new negative classification and measured/unmeasured accounting), **7** separate G-REDruntime owner-source lifecycle contracts, real full-stack audit of **2,319** original source files, exact 187/1,013 measured/no-measurement classification, official SCC **0.5.31** `framework-all-generics-preview` exit 0 with previous 22 combined generic preview files and **10** owner framework files; **323** GCO installer/CLI/package contracts. Portable GCO app artifact `Gs-Cyberpunk2077_Optimizer-v1.2.5-run607-a1`, artifact ID `14330368868`. No game-file writes, mod-specific recipes, framework source edits or performance claims.

## What remains (actual shared-trigger proof)

1. Trace each **high-cost** measured candidate backwards through `this.Method()` and `this.field` references, all same-class/inherited/aliased/native writers and original event publishers. A heuristic offline inspection found other same-class writes for many of the 902 field-reading methods but **cannot prove complete writer closure**. No caching until that proof exists.
2. Identify the smallest safe **generic subgraph** to skip: for live buffs, separate structural membership and widget recreation from changing time; for quest facts, prove exact fact-name subscriptions and cleanup; for owner fields, preserve lifecycle, first load, change and fallback.
3. Use G-REDruntime `DirtyFlags.Mark` at *every* proven writer and per-consumer `ChangedSince` with original behavior on unknown. Avoid global `Consume`, guessed 50ms throttling, new source-local singleton or another framework service.
4. Validate partial/complete optimized sources, idempotence, source drift and strict SCC before user-led in-game save/reload/control regressions, matched CET/GRSP/PresentMon and final merged game-relative ZIP. Do not infer callback entries from outgoing F11 edges or invent saved FPS/CPU.

**No broad generated shared-state rewrite is authorized by these read-only counts.**
