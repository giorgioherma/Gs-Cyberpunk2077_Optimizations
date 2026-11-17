# R5 — REDscript work-elimination research: large generic passes first

**Status:** source/capture-grounded research and prioritized measurement queue; **NO new generator authorization, profiler hooks, source rewrite, game install, reported FPS gain, or user-approved targeted pass**.  
**Created:** 2026-11-17. **Active owner:** GCO managed REDscript Resolver on `dev/redscript/resolver_handoff`. **Framework owner:** `giorgioherma/G-REDruntime_Framework/main`. **Profiler owner:** GRSP `dev/native-instrumentation`.  
**READ FIRST FOR NEXT AGENT:** GCO `AGENTS.md` → `docs/integration/REDSCRIPT-RESOLVER-LOCAL-HANDOFF.md` → **THIS DOCUMENT** → `REDSCRIPT-RESOLVER-AGREED-OPTIMIZATIONS.md` → `REDSCRIPT-RESOLVER-PROGRESS.md` → `docs/LATEST-WORK.md`. The agreed seven-mod review is **not** an implementation queue; generic-first work precedes R7 targeted remainder.

## Decision in one paragraph

**Optimize work that never needed to run** before optimizing how quickly that work runs. The greatest likely opportunities are (1) bounded spatial/event broadphase that rejects inputs **before** expensive per-item work, (2) input/callback admission that prevents irrelevant events from entering long REDscript call trees, (3) change-driven/UI dirty propagation that avoids unnecessary repeated setters and updates, and (4) debug-off/idle-state work removal. Prefer **physically absent invocations** and eliminated nested VM edges; counting an early return as a paused callback is not enough. Retain an unmodified behavior path for any source, timing, receiver, state, or lifecycle uncertainty.

## 1. Exact existing measurement and its limits

**Directly inspected:** supplied `GCO-Capture-16112026_132224.zip`, inside `G-REDscript/GRSP_Summary.json`, `Data/Runtime/GRSP_ByMod.csv`, `GRSP_ByFunction.csv`, and `Data/Developer/RSP_CallSites.csv`, `RSP_CallbackPressure.csv`, `RSP_CallbackReuse.csv`; cross-checked actual newer REDscript source at `giorgioherma/original_cet_source/main/r6/scripts`. This is a historical capture, **not authority for future live source hashes or installed mod inventory**.

| Recorded property | Observation | Interpretation |
|---|---:|---|
| WORLD capture duration | 384.517 s | One mixed scenario, not controlled A/B |
| Captured outgoing VM calls | 146,839,501 | **Not** callback entries, allocations, or avoidable calls |
| GRSP instrumented exclusive work | **280.756851 ms/s** | Approximately the user's reported “300”; **not** proven comparable to an earlier “90” |
| Average / P99 / worst script frame | 9.130 / 48.754 / 1,772.474 ms | GRSP script-frame observations, not PresentMon frame-time totals |
| Source population in current original-source repo | 2,319 `.reds` files | Private repo's fresher tree differs from the capture-linked archived tree in 3 Negotiable Affection files |
| Old RSP_CallbackReuse rows | 81 target families; 4,710,077 outgoing read-like edges | 41 rows have **TRUNCATED_NOT_ZERO_WORK** frame evidence; 95,096 dropped frame rows. No invented missing frames or safe caches |
| Completion/provenance | F11 complete, GOOD, 12/12 shards, no dropped spikes/hot paths; 11,823 PresentMon-aligned frames | Good for triage; still NOT a matched before/after performance experiment |

**Important:** `GRSP_Summary.capture.measuredOneCorePct` is the reported proxy equivalent of 28.08% of one core for instrumented work. Never call it all game CPU, add CET timing to it, or infer actual FPS gain. Source `RSP_CallbackReuse` explicitly sets `callback_entries_proven=false`, `cache_safe_proven=false`. The previous exporter truncated **per-frame** coverage across targets; GRSP owner-native CI [#26](https://github.com/giorgioherma/GRSP-G-REDscript-Profiler/actions/runs/43870345597) later **SUCCESS** for the source fix; a new game capture from that producer is still needed to prove actual output quality, current installed provenance, overhead, and safety. No new native hook is authorized here.

**Concentration matters.** ReinforcementsSystem logged 38,103.6 ms exclusive / 99.095 ms/s (**35.30%** of the owner-attributed capture's exclusive work), Dark Future 10,151.7 ms / 26.40 ms/s, FlushingEtiquette 7,046.3 ms / 18.33 ms/s (**wrapper/proxy attribution incomplete**), Custom Quickslots 4,263.0 ms / 11.09 ms/s, Crowd Voice 4,173.7 ms / 10.85 ms/s (**MAYBE, no targeted permission**), and Much Better Contracts 3,995.7 ms / 10.39 ms/s. These are exclusive measurements at specific scopes. **Never sum inclusive + exclusive, parent + child, per-mod + callsite, or overlapping CET/GRSP clocks as a new total.** Focus on operation families with real work exposed across multiple sources.

### High-cost source-anchored exposure (NOT predicted savings)

| Work family | Exact observed exposure | Why investigate |
|---|---|---|
| Geometry / query explosion | **216,006** `IsPointInQuad → SortSquare` calls in **260 active frames**, max **9,435** sort calls in one frame; `SortSquare` owner function 11,630.6 ms exclusive | Burst suppression could remove complete sort + triangle + sign subgraphs, not just 3 `AtanF` calls |
| Device/input bridge | **273,887** `CustomQuickslotsGlobalInputListener::OnAction → SetLastUsedKBM` calls, 2,623.4 ms outgoing callsite exclusive; OnAction has **14,240,876** outgoing edges | Source shows CET/Lua override updates a Boolean after grace; Lua reload can reset state. Stable arguments alone ≠ safe suppression |
| Buff UI writes | **38,318** `buffListGameController::UpdateBuffDebuffList → SetData`, 3,394.6 ms outgoing callsite exclusive; **592,252** `inkWidget::GetController` reads, 271.8 ms | Only avoid setter work if complete tuple, exact widget identity and all setter effects are unchanged |
| Cooldown animation | **22,772** `SingleCooldownManager::Update → FillOutroAnimationStart` calls, 2,287.3 ms exclusive | Can state-machine transition work be executed only on relevant transitions? Unknown until source and timing proof |
| Diagnostics disabled | **24,375** calls each from `MBCGigSnapshot::TraceEnabled` to `MasterEnabled` and `EnableDebugLog`, respectively **1,886.5** and **977.1** ms outgoing exclusive | Potential expensive per-event settings/debug gates when normal mode is off |
| Resource-event filter | `ResourceRef::GetPath` **436,737** calls across HQ HoloGirl/HoloTravel, 471.6 ms exclusive; `ResourceEvent::GetResource` about 436,745 recorded read-like edges | Two owners share high-frequency path-query shape; event delivery is **ResourceEvent** callbacks, not necessarily per-frame `OnUpdate` |
| Sector scene scan | `CrowdSeatOverlapFix.CSOF_Service::OnSectorReady` 6,113,073 outgoing edges, 1,042.6 ms exclusive (703 active frames) | Expensive scene traversal may admit bounded one-time work/deduplication, with exact lifecycle proof |
| Cross-callback reads | `RSP_CallbackReuse.csv`: 4,710,077 outgoing read-like edges, but only **1,545.2 ms** instrumented exclusive across target groups | Huge edge counts can be cheap; most event-name/type reads do **not** justify an expensive shared-cache subsystem |

Numbers refer to **observed full capture**, never per-frame predicted savings. Callsite timing is instrumented exclusive at that callsite; if the call is removed, **downstream work might also change**, but cannot be estimated by naively adding function or child timings. Only matched capture measures real savings.

## 2. Priority ladder — new generic operation families

Rank is an **investigation** sequence, not preapproved rewrites. `SCANNABLE` means a source structure could be classified read-only; `PROOF_REQUIRED` means we cannot yet rewrite it. A recurring pattern across mods is a generic candidate even when today's measured match occurs in only one mod. Do **not** introduce mod-specific paths, allowlists, source hashes, or version locks in any generator.

### P0-A — G-SpatialBroadphase: reject before costly geometry **[large bursts / SCANNABLE]**

**Core idea:** recognize a function that uses a fixed or source-proven set of vertices/areas to perform costly triangulation, angle sorting, distance or shape work, and add a **strictly conservative cheap exclusion** *ahead* of the expensive call graph. First prototype candidate: finite-coordinate **AABB outside rejection** for a quadrilateral before `SortSquare` / `PointInTriangle`. A triangle built from the original vertices cannot contain a point outside the min/max of those vertices; on the candidate's unchanged inside/boundary cases, fall through to the **exact original implementation**. This does not move RNG or remove author exclusions. A broader pattern may create an immutable, lifetime-verified area-bound index when many repeated area tests use the same data; only do so after proving all mutation/invalidation paths.

- **Evidence:** ReinforcementsSystem's 216,006 quad-sort calls concentrated into 260 frames; source `r6/scripts/ReinforcementsSystem/Utils.reds`, `ReinforcementSystem.reds`. Existing generic `ANGLE_SORT_INVARIANT_V1` hoists three `AtanF` calls per four-point sort; it **does not** eliminate the 216,006 full geometry checks.
- **Why broad:** same safe outside-of-bound operation can appear in other region, proximity, UI geometry, scene or polygon filters; discover via AST/lexer/shape, not mod name.
- **Proof:** all positions/vertices and NaN/Infinity/degenerate/boundary cases; preserve exact acceptance behavior and original fallback; stable source operands and side effects; constructor and local-vs-cached data lifetime. Do not reinterpret zones or reorder `RandRange`. The source may prove only a *local AABB gate* first, not persistent indexing.
- **Measure next:** hit/miss ratio at broadphase (if bounded capture instrumentation is justified), actual per-frame quad-call suppression, avoided downstream edge counts, P99/peak script frames and genuine PresentMon P99; compare matched source/game scenes. Reject if coarse bound computation costs more than it removes.

### P0-B — G-EventAdmission: **don't enter irrelevant OnAction and other event bodies** **[high volume / PROOF_REQUIRED]**

**Core idea:** if a callback handles only an exactly source-proven finite set of action names and types and has no relevant work for other events, route/filter at **registration time** (specific input actions) or at the cheapest safe handler entry, *before* bridging to CET, allocating events, making game queries or traversing action-name arrays. For read-only stage, synthesize an **interest-set and side-effect graph** from each function and forwarded helpers; classify which events must still go through `wrappedMethod`, consumption, wildcard subscribers and effectful observers. The high-value metric is **absent callback entries/expensive body invocations**, not cheaper `GetName` calls.

- **Evidence:** Custom Quickslots 14.24M outgoing edges in `OnAction`, 273,887 Lua bridge calls. DrugDealer has five parallel action listener methods plus other owners (GearRecoverySystem, GigCore, etc.). Source: `custom_quickslots/player_puppet.reds`, CET `CustomQuickslots/init.lua` override, plus each mod's current source.
- **Potential runtime:** G-REDruntime **InputHub** supports action-specific subscriptions and lazy listener registration. It does **not** automatically reproduce arbitrary existing wildcard/PlayerPuppet wrapper callbacks; first device observation is silent and device-change polling is **100 ms**, so not a transparent substitute for immediate CET bridge updates.
- **Proof:** true callback entries vs outgoing edges, exact names/types, identical forwarding/consumption order, attach/detach, first state, grace period, controller changes, Lua reload, source owner and third-party `@wrapMethod` chain. If any effectful input callback must observe every action, do **not** selectively suppress it.
- **Measure next:** distinct callback invocations, action-interest hit rate, bridge unchanged-pair rate, reinit/reload count, actual per-action work and per-frame burst distribution. No blind change-driven Lua bridge optimization.

### P0-C — G-DirtyOutput / G-ActivitySleep: **no work while output cannot change** **[high recurring UI / PROOF_REQUIRED]**

**Core idea:** derive **all output-affecting dependencies and invalidation/wake boundaries** for recurring widget, HUD, buff, countdown, cooldown and update paths. If the controller is invisible/inactive **and its original semantics permit dormancy**, physically stop scheduling or avoid rebuilding its list. When active, suppress an identical, provably side-effect-free output write to the **same live widget** only after its full input tuple and version are unchanged. Prefer explicit event/dirty notification to arbitrary fixed throttling; if no reliable wake exists, keep the original update. A source-local “same value” optimization can be admitted separately from a full framework migration.

- **Evidence:** Dark Future `UpdateBuffDebuffList → SetData` 38,318 callsite events/3,394.6 ms, `SingleCooldownManager::FillOutroAnimationStart` 22,772/2,287.3 ms; live `SetData(icon, timeRemaining, timeTotal, stackCount)` includes a **time-varying float**. Similar HUD/window refresh patterns appear across other owners.
- **Potential runtime:** G-REDruntime `DirtyFlags.Version/ChangedSince` is suitable for independent consumers; shared `Consume` is **global** and can starve other subscribers. `Scheduler` has a 50-ms minimum interval and **does not replay missed ticks**. Do not silently change animation deadlines, tooltip timing or game-state cadence.
- **Proof:** complete output tuple, animation/timing side effects, last-write/notification effects, same widget/controller instance, initialize/recreate/show/hide/attach/detach, guaranteed invalidation for every dependency. No count-only guards, “9 of 10 ticks”, or cached timeRemaining when the display must move.
- **Measure next:** actual redundant-unchanged setter fraction **per widget generation**, hidden/active intervals, timer- and state-transition counts, wake latency, resulting avoided setter calls.

### P0-D — G-DiagnosticsOff: **zero normal-mode diagnostic work** **[known expensive gate / PROOF_REQUIRED]**

**Core idea:** source-proven debug-only branches should not repeatedly allocate strings, walk debug rosters or query multi-setting debug policy in normal mode. Explore an **event-invalidated debug-enable predicate** or a single cheap early source guard in *every* tracing entry point. Preserve immediate enable/disable behavior and all errors, gameplay, emergency diagnostics, return effects and author preferences. Do not freeze a mutable setting or silently disable explicitly enabled debug.

- **Evidence:** Much Better Contracts `MBCGigSnapshot::TraceEnabled` recorded 2,911.8 ms function exclusive; its two settings lookups have 1,886.5 and 977.1 ms exclusive; live source performs `MasterEnabled && EnableDebugLog && DebugLogLevel >= 3`. Other mods require their own source-proven diagnostic-only classification.
- **Proof:** disabled-state evidence and cost, all code inside disabled branches has no required gameplay effects, exact settings-change notification/lifetime or safe fallback; no suppression of critical error paths.
- **Measure next:** number of debug-off calls, counts of work skipped on that branch, live settings transitions and error-path preservation.

### P1-E — G-TransitionWork: once per proven state transition, not per refresh **[large callsite / PROOF_REQUIRED]**

**Core idea:** if an expensive method computes the same transition payload repeatedly while its **complete state key** is identical, compute it only on a real transition; preserve exact first call, time-dependent data, side effects, reentry, cancellation, ordering and repeated animation behavior if intentional. Prefer preserving the original method and adding a source-proven guard to its caller.

- **Evidence:** Dark Future `SingleCooldownManager::Update → FillOutroAnimationStart` 22,772 calls / 2,287.3 ms; source semantics need deeper inspection before rewriting.
- **Proof:** distinguish a one-time animation-start derivation from a required ongoing timer/update; track entry/exit and changed keys; source and runtime evidence. Unknown => refuse.

### P1-F — G-ResourceInterest / G-PhaseOnce: reject irrelevant engine/resource events early **[cross-owner pattern / PROOF_REQUIRED]**

**Core idea:** classify callbacks that receive many resource/sector/scene events but care about only an immutable, known resource/action subset. If engine registration permits it, narrow listener interest; else use a cheap identity/key check *before* expensive conversion/lookup/iteration. For a proven resource instance, optionally reuse immutable metadata only within its exact lifetime. Rebuild **only once per resource generation**, not once per callback, if all mutations and repeat-event side effects are understood.

- **Evidence:** HQ HoloGirl + HoloTravel make 436,737 `ResourceRef::GetPath` calls/471.6 ms; both receive `ResourceEvent` and inspect paths. Crowd Seat Overlap Fix `OnSectorReady` has 6.11M outgoing edges across 703 frames. These are different event families but both illustrate heavy work near an event boundary.
- **Critical nuance:** `ParticleHandler::OnUpdate(e: ref<ResourceEvent>)` is an event callback (e.g. `BindCallbackTRL`), **not** proof of per-frame polling. Never convert based on the method name alone.
- **Proof:** exact type/identity and mutable resource semantics, all interested resources, phase side effects, duplicate event behavior, multiple owner/wrapper listeners, engine callback thread/order, resource unload/reload. No generic “ignore repeats” without proof.

### P1-G — G-LoopInvariant / G-BoundedTraversal: fewer expensive inner-loop operations **[structurally reusable / SCANNABLE]**

Generalize the proven *idea* behind `ANGLE_SORT_INVARIANT_V1`: within a recognized bounded loop, identify pure/immutable repeated values and hoist **only when writes, aliases, ordering and exception/error behavior are proven unchanged**; use a local precomputed value or an immutable transient index. For expensive repeated collection scans, a source-proven **constant-size or finite-set dispatch** may eliminate entire linear searches. Do not build a generic “memoize all getters” pass.

- **Evidence:** Reinforcements geometry uses 15.55M outgoing edges from `SortSquare`; Dark Future has repeated widget-controller reads and list copies; input mods often build CName arrays. This family may apply beyond a known mod, but impact is **per exact shape**, not universally high.
- **Proof:** lexical/semantic types, write and call effects, alias invalidation, evaluation order, failure behavior, dynamic allocations, duplicates/first-match precedence and comparator semantics.
- **Lower priority by itself:** a small local operator-saving transform does not substitute for broadphase/event admission, even when CI demonstrates SCC compilation.

### P2-H — G-SafeSharedRead: reuse **identical receiver+arguments+epoch**, not merely same named target **[low direct measured cost / PROOF_REQUIRED]**

The existing `RSP_CallbackReuse` is a *discovery index*. Of 4.71M read-like edges, 2.62M are input `GetName/GetType` events, with only 170.7 ms total associated exclusive work; all read groups together are 1,545.2 ms across the capture. Such counts alone are a **poor first target compared with geometry/GUI/bridge**. Begin only with observed expensive, verified pure, stable, identical receiver+arguments getters. Prefer callback-local same-event reuse; consider G-REDruntime `StateCache/ContextService` only with proved lifetime and invalidation. Reject frame-shared caching merely because calls occur in the same frame.

## 3. What to cut first (not what to code first)

1. **Whole operations:** eliminate a region/scene/input event before expensive fan-out, safe outside bounds, or unactionable callback entry. This is the biggest theoretical structural reduction: fewer methods, loops, temporary values, downstream calls and spikes.
2. **Complete repeat refresh:** no-op/dirty gating when output and side effects are proved unchanged. Can remove large UI and animation subtrees.
3. **Dormant scheduling/registration:** when an existing valid wake path can resubscribe/resume with identical latency and lifecycle. No unconditional slower polling.
4. **Diagnostic-only work in normal mode:** when off-state and settings updates are proven.
5. **Local pure subexpressions:** only if material measured cost remains after 1–4. Do not pursue tiny shape expansions merely to increase pass count.

**Avoid the seductive wrong metric:** Do not prioritize by outgoing edges alone. `GetName` generated millions of edges with low direct cost, while far fewer `SetData`, `FillOutroAnimationStart` and debug-setting calls incurred much higher measured exclusive time. Rank using **recorded cost + evidence of actually avoidable fraction + repeated frames + burst severity + safe generic coverage**. Until avoidable fraction is proven, write `UNKNOWN` rather than a projected percentage.

## 4. What GRSP and the next user captures must tell us

The **first analysis task is read-only**: join real `RSP_CallSites.csv`, `RSP_WorkMap.csv`, `RSP_CallbackPressure.csv`, `RSP_CallbackReuse.csv`, `GRSP_ByFunction.csv`, `GRSP_ByMod.csv`, real GCO PresentMon chronology, and current source admission by capture/source IDs, caller and callsite identities. Report nested/unknown attribution explicitly. `RSP_CallbackReuse` older truncated rows must remain `UNKNOWN_FRAME_COVERAGE` for per-frame conclusions.

### Minimal new captures from the user

**First:** one **unoptimized, complete F11 WORLD baseline** using the usual reproducible route (same save, mod set, graphics/options, game/profile versions), with CET + GRSP + genuine PresentMon, followed by **Collect Results** and Restore as currently required. Include a period of busy city/world streaming and actual scene/region events; record approximate time landmarks for major transitions. Prefer new GRSP owner-native output with the fixed per-target frame cap, but **do not install an unproven profiler build over known-safe gameplay**; verify native producer SHA, F11 completion, game stability and no missing/drop flags. This is for discovery, **not** an A/B FPS proof.

**Second:** one **~120-second stationary/noninteractive scene** in the same save and mod stack. Its purpose is to reveal persistent update/input/UI callbacks during visible inactivity (true idle work), without confusing input or streaming with background work. After these two, select only **one targeted follow-up** if a hypothesis is otherwise blocked:
- input: device/grace transition and bridge reload during explicit KBM/gamepad changes;
- UI: repeated same widget/tuple across buff, cooldown and visibility changes;
- geometry: active world/busy scene emphasizing repeated area rejection and per-frame burst;
- resources: load/unload/refire of known materials or streaming sector events.

**Do not request all specialized captures upfront.** User controls testing; GCO does not alter installed mods to capture. F11 must finish explicitly; incomplete/crashed sessions are not evidence for generation. Follow the current GCO CAPTURE → COLLECT lifecycle.

### Minimal missing proof fields, only when existing data cannot answer

| Family | Unknown that matters most | Bounded observation / stop rule |
|---|---|---|
| Spatial | Number of negative/outside tests; downstream geometry actually skipped; immutable area generation | Source-side simulated AABB admission first; later bounded outcome counters **only** if justified and owned by GRSP/native or approved test build |
| Input | Actual per-listener **entry counts**, action interests, unchanged bridge pairs, Lua reload/grace transitions | Do not equate outgoing call edges with entries or change InputHub semantics without explicit proof |
| UI/transition | Exact complete data tuple and widget generation, unchanged setter fraction, first/show/hide/wake | Never capture unbounded object identity or user/private data; coarse outcome counters only |
| Debug | Off-mode fraction, settings changes, work inside debug-only branches | No stripping gameplay/error effects |
| Resource/sector | Resource identity/key equality rates, duplicate event effects, reset/reload | No permanent cache or global sector hook without lifetime proof |
| Shared read | Same receiver+arguments+epoch and invalidations, not merely target name/frame overlap | Refuse when native receiver identity is unavailable; fix narrow evidence gap at GRSP owner only if stable/low-overhead |

All new instrumentation is owned by **GRSP** and must be source-owned, bounded, post-F11 where practical, crash-safe, and measured for overhead. Never create a second capture pipeline in GCO. If low-overhead measurement is impossible, keep the candidate as `UNPROVEN` and rank another family.

### Comparison contract for “90 → 300” or future improvement

- Compare exactly the same **unit** (`GRSP_Summary.capture.exclusiveMsPerSecond`), capture scenario, game version, source/mod inventory, profiler producer SHA/settings and duration; analyze **observed exclusive work per second**, real PresentMon median/P95/P99 and hitches separately.
- Determine whether the apparent rise came from workload/scenario, source changes, new callbacks, changed instrumentation, or genuine increased game script work. **Cause remains UNKNOWN.**
- Preserve the actual worst case; don't hide a 1.77-s worst script frame behind a lower average.
- For optimization A/B: same installed mod sources except the exact single tested overlay, same save/route/conditions, identical profiler, distinct source SHA markers, verified SCC and gameplay; compare direct callsite suppression, high tail, average and real frame time. Never claim a “% performance improvement” from source structure, GRSP counted edges, CI time, or hypothetical AABB hit rate.

## 5. Immediate highest-value source-only research (before new runtime code)

**Research sprint 1 — discovery, not generation:**

1. Read the already existing GCO `CallbackSemanticResolver` whole-stack callback catalog and source/capture reconciliation. Build **operation-family grouping**, not mod lists: (a) expensive geometry downstream of cheap-negative tests, (b) input listeners that can prove a finite interest set, (c) setter/transition functions with an input tuple and known invalidations, (d) debug-off-only branches, (e) resource/sector event selectors. Count eligible/rejected **by structural reason and actual observed cost**.
2. For **G-SpatialBroadphase**, investigate a source-only exact AABB prefilter with the original geometry **unchanged** as fallback. Compare mathematical behavior over inside/outside/edge/degenerate cases before any preview. If broadphase is proven, create one generic source-shape matcher; no permanent per-mod tables or special restricted-zone catalog.
3. For **G-EventAdmission**, classify independent callback bodies vs `PlayerPuppet` wrappers; determine whether original listener registration can be narrowed **without any lost side effect**, and where a cheap entry guard is the only viable transform.
4. For **G-DirtyOutput**, map actual `SetData` and transition setters to all output dependencies and lifecycle; report `IDENTICAL_TUPLE_NOT_PROVEN` where time float or side effects are unresolved.
5. Produce a **ranked avoidable-work ledger**. Rank cost exposure and unknown fraction separately; record `GENERIC_COVERED`, `ALREADY_PRESENT`, `SEMANTIC_REMAINDER`, `UNPROVEN_OR_DRIFTED`, `NOT_APPLICABLE` as the next R5/R7 boundary. Mark Reinforcements geometry as **PARTIALLY GENERIC COVERED**: only the `ANGLE_SORT_INVARIANT_V1` preview passed; early combat/faction ordering and restrictions are **not** implemented.

Do not implement another `CNAME_ARRAY_MEMBERSHIP_V1` broadening, duplicate scheduler/input hub, large semantic catalog, broad profiler hook, hardcoded mod allowlist, or arbitrary polling delay as a substitute for these high-impact leads.

## 6. Contract of each future pass

Every generic candidate must record these machine-readable fields (no fixed source hash locks; SHA only for invocation-local source proof):

```text
family, operation_shape, measured_capture_id, measured_source_key,
observed_edges, exclusive_instrumented_ms, active_frames, burst_max,
entry_count_status, avoided_fraction_status, effect_proof_status,
source_state, receiver_argument_identity_status, lifetime_epoch,
invalidation_and_wake_proof, wrapper_consumption_RNG_order_proof,
dependency_and_owner, first_failure_reason, original_fallback,
generated_file_owner, partial_state, second_pass_zero_op,
whole_stack_scc_result, gameplay_result, matched_ab_result
```

Admit generation only after exact current source/proven types, old+new control-flow equivalence, no missing dependencies, safe partial/complete handling and zero-op second pass; verify **actual generated game-relative files** against original plus full official SCC 0.5.31. If framework is needed, obtain current **authoritative G-REDruntime repository source** as an external dependency, and refuse/avoid changing the framework if it is absent or incompatible. No standalone parallel optimizer UI, no extra ZIP, no profiler files in optimized output. All real gameplay assertions remain manual/user-validated.

## 7. Verified code and CI baseline / unresolved

- GCO managed branch `dev/redscript/resolver_handoff`. Current generic source preview includes strict `CNAME_ARRAY_MEMBERSHIP_V1`, `CNAME_FIXED_INDEX_V1`, `ACTION_LITERAL_MEMBERSHIP_V1`, `ANGLE_SORT_INVARIANT_V1`. Source-only and SCC **preview**, not production generator deployment. The November 17 [GCO CI #567](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/44005258693), [#568](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/44005848757), [#569](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/44006145184) all **SUCCESS**; #568/#569 include isolated compile of current owner G-REDruntime with all staged generic previews. That is compilation, not gameplay.
- G-REDruntime `main` manifest `0.6.0-pass6`; source-owned StateCache, ContextService, DirtyFlags, InputHub, Scheduler, EventBus, HookBus. InputHub device polling 100 ms; Scheduler interval floor 50 ms and no missed-tick backlog; DirtyFlags global `Consume` must not be used for multiple consumers. No changes to framework requested here.
- GRSP native `dev/native-instrumentation` owner CI #26 SUCCESS for repaired read-overlap exporter; **new producer game capture not yet validated here**. No new hook authorized.
- Open: possible 90→300 metric comparability; complete input entry/state/bridge effects; UI tuple changes/animations; resource identity; wrapper proxy attribution; source-drift/lifecycle proof; conservative broadphase correctness; A/B gameplay and PresentMon tail benefit. **Stop rather than guess.**

**Handoff instruction:** Next agent should start from the **ranked work-elimination families above**, read real sources and original CSVs, and work in **GCO `dev/redscript/resolver_handoff`**. Prioritize eliminable whole subgraphs over mechanical operator gains; update this document, `REDSCRIPT-RESOLVER-PROGRESS.md`, and `docs/LATEST-WORK.md` after accepted evidence or tested implementation. Keep the earlier approved targeted backlog for R7.
