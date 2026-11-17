# R5 — Shared-work / UI invalidation evidence: measured source scan

**Checkpoint:** 2026-11-17, [GCO CI #598 SUCCESS](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/44082314052), code commit `2ce186bc47e8f6c1c10473ad11a5fd2b89007adf`. **Implemented:** read-only source-wide classifier and real F11 evidence admission. **Not implemented:** dirty UI source rewrite, generic callback pause, observer deletion, gameplay or measured speedup.

## Why this stage

The uploaded `r6LIVEnow.zip` contains **2,319** `.reds` files. The genuine sealed `GCO-Capture-16112026_132224(1).zip` ran **384.517 seconds**. The existing measured `UPDATE_REFRESH` family recorded **12,448.7 ms** outgoing instrumented exclusive cost across 75 owners and ~11.51M outgoing VM edges. `INPUT_ACTION` recorded ~5,377.8 ms. Those are **not** saved CPU or independent frame-time totals and cannot be added to CET timing.

Actual source checks expose several different, unsafe-to-conflate refresh behaviors:
- **Dark Future:** `UpdateBuffDebuffList` reconstructs UI arrays/widgets and reads changing buff `timeRemaining`; `SingleCooldownManager.Update` applies changing time, animation and visibility.
- **HQ_HoloTravel / HQ_HoloGirl:** independent recurring visuals and particle updates; dynamic display effects are not automatically idempotent.
- **RedWindows:** `UpdateListVisual` adds/removes live ink children and mutates arrays and widget state.
- **VehicleMileage:** `VM_FuelGauge.Refresh` already has a source-specific hidden/animation gate; do not remove needed visibility, transform, theme or temperature logic before it.
- **ShatteredChrome:** `SCHUD.UpdateHUD` creates and queues an event with fresh region data, not a trivially cacheable display setter.

These are **examples from current source**, not mod-specific admission rules or whitelist. Every discovered source is scanned. No user-requested broad automatic throttling is approved.

## Implemented source and interface

- **GCO owner:** `src/GRedscript.Resolver/CallbackSemanticResolver.SharedWork.cs`, with `scan-shared-work --game <live-root> --out <isolated-private-json> [--analysis <verified-GRSP-Resolver-json>]`.
- Shared the **one existing source index, method owner parser, token scanner, capture source-hash proof, method-span source mapping and measured callsite join** with `CallbackSemanticResolver`. The existing `resolve-callbacks` function uses the same `ReadAllMethodSources` after refactoring, rather than retaining a parallel parser.
- Scans method-name hints for `Update`, `Refresh`, `Rebuild`, `Populate`, `Render`, `Redraw`, `Tick` across **every** indexed `.reds` source. Method names are **discovery only**, not callback-entry/execution/safety proof. Source tokens record candidate time/animation dependencies, UI setters and widget lifetimes, event dispatch, array mutation, member reads, player-handle reads, existing version reads, early gates, loops, wrappers and possible consumed returns.
- Optional capture evidence is accepted only when the exact sealed F11 collection provenance and **current source stack SHA256** match. Original `RSP_CallSites.csv` rows and `sourceObservations` must remain aligned; only exact source path, method span, function name and file SHA-matched outgoing edges are ranked. Unknown wrappers and unproven source associations cannot inflate work.
- Every finding has a **first failed prerequisite**: `SOURCE_MATCHED_MEASUREMENT_MISSING`, `WRAPPER_SIDE_EFFECT_OR_ORDER_UNPROVEN`, `TIME_OR_ANIMATION_DEPENDENCY`, `EVENT_DELIVERY_SIDE_EFFECT_UNPROVEN`, `UI_WIDGET_VALUE_OR_LIFETIME_UNPROVEN`, `MUTATOR_INCOMPLETE`, `LIFETIME_UNKNOWN`, `RETURN_CONSUMPTION_UNPROVEN` or `PURE_INPUT_DEPENDENCIES_NOT_PROVEN`. This is conservative *classification*, not semantic proof.
- JSON schema `gco.redscript.shared-work.scan.v1` reports source hash, each method's measured outgoing edges and exclusive time **as observations, never projected savings**, source hints, first blockers and counts. `callbackEntries=null`, `expectedSavingsMs=null`, `completeWriterSetProven=false`, `safeToSkipBody=false`, `generationAuthorized=false`, `sourceWrites=false`, `generatedFiles=0`. Output must be isolated; existing output, installed game writes and source drift refuse.

## Verified real-source results

**GCO CI #598 (code `2ce186b`) full SUCCESS.** Replayed the real sealed capture with private `original_cet_source` and the authoritative framework checkout:

| Contract | Actual result |
| --- | --- |
| Private original source | 2,319 `.reds` files audited/unchanged |
| Broad refresh/maintenance source-discovery | **1,200** method-name-family entries |
| Exact source-matched F11 outgoing callsite evidence | **187** methods |
| No exact matched F11 method evidence | **1,013** methods |
| Missing dependency blocker accounting | All 1,200 findings accounted for; no bypass |
| `.NET 10` REDscript managed source/CLI tests | **94 passed** (five new shared-work scanner contracts) |
| Existing blackboard generic | 22 eligible statements/16 files, **14 disjoint + 2 composed**, 0 deferred |
| Official SCC 0.5.31 | Full 2,319-file private stack, 22 combined preview files and all **10** authoritative G-REDruntime `.reds` files; `framework-all-generics-preview` exit 0 |
| GCO installer/CLI/package tests | **323 passed** |
| Capture/source/game writes | None |
| Saved CPU, FPS, GC or frame-time claim | **None** |

Tests cover source-only refusal, a precisely source-aligned synthetic measured row and ranking, capture/source drift refusal, unresolved wrapper rows not being credited, and isolation/existing-output refusal. Owner framework version and previous player-lifetime source contracts remain unchanged. Portable GCO **app** artifact `Gs-Cyberpunk2077_Optimizer-v1.2.5-run598-a1` is **not** a game-relative optimization ZIP.

**Important reading:** 1,200 are broad *method-name hints*, **not 1,200 clean optimization opportunities**. Only 187 have a source-aligned measured outgoing-call observation, **not 187 proven hot callback entries or safe skips**. Reported exclusive instrumented work is **not a predicted gain or total CPU load**.

## Next stage: real DirtyFlags producer/consumer graph

1. Source-rank the 187 measured methods by inclusive observable effects and current method-line costs. Focus on expensive operations before UI/widget writes; separate mandatory per-tick animation/time from pure derived-data preparation.
2. For promising generic source shapes, **prove every writer and alias** for each input across all current source owners, event subscriptions, first controller creation, save/load, detach/replacement, visibility and fallback. Collect explicit `MUTATOR_INCOMPLETE`, `RETURN_CONSUMPTION_UNPROVEN`, `LIFETIME_UNKNOWN` refusals. Matching `this.member` reads does **not** prove a closed mutator graph.
3. Only after that, build a minimal source-owned writer `DirtyFlags.Mark` + per-consumer `ChangedSince` adapter on G-REDruntime **existing** APIs and preserve original source paths, exact write/read order, and unchanged/first-run behavior. No global `Consume`; no automatic 50-ms timer or broad refresh throttling.
4. Validate untouched/partial/complete/idempotence, source drift, missing framework, source-original fallback and official SCC; then user-led gameplay/reload/UI behavior and matched three-profiler before/after. If a generic cannot eliminate whole recurring work safely or cheaply, reject rather than build a semantic library.

No additional captures requested; inspect available F11 evidence first. CET `main`/development rules, GRSP native owners, framework `main` and GCO stable `main` stay unchanged by this scanner.
