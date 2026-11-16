# R5 — high-impact REDscript evidence and source-proof programme (2026-11-16)

**Status:** read-only source/capture audit and next instrumentation contract; NO rewrite, benchmark improvement, source admission extension, or gameplay acceptance. Active GCO branch `dev/redscript/resolver_handoff`. The narrow `CNAME_ARRAY_MEMBERSHIP_V1` prototype stays frozen after CI #486 (1 eligible / 47 rejected in 2,319 files). Do not spend CI on its 4 reversed loops without material measured benefit.

## Evidence already available

- GRSP WORLD capture: 309.614 s, 100,887,542 **outgoing VM call edges** (not callback invocations), 59,900.126 ms exclusive instrumented work. Real PresentMon offers frame intervals, not callback causation.
- `CustomQuickslotsGlobalInputListener::OnAction`: 332,916 `SetLastUsedKBM` outgoing calls (~1,075/s); 17,309,776 outgoing edges across the callback; 3,898.073 ms exclusive, of which the SetLastUsedKBM callsite has 2,823.849 ms. These are capture observations, not predicted savings.
- `buffListGameController::UpdateBuffDebuffList` (Dark Future): 6,509.664 ms exclusive; 46,264 `buffListItemLogicController::SetData` outgoing calls, 5,244.839 ms associated exclusive work.
- `GetScriptableSystemsContainer` across 45 observed owners; `GetQuestsSystem` across 21; multiple cross-owner repeated reads have *lower direct measured cost* than the two above.
- Source inspection here is of the user-supplied **dated** `r6LIVEnow.zip` (2,280 source files), NOT authoritative current 2,319-file source bytes. GCO's real-stack private CI must establish current identity before any pass.
- In that dated Custom Quickslots source, `SetLastUsedKBM(this.player.PlayerLastUsedKBM(), this.player.gracePeriodOver)` precedes local device change detection on every `OnAction`. A nearby declaration notes the call's implementation lives in CET/Lua. It is **not proved side-effect free**.
- In that dated Dark Future source, `SetData(StringToName(data.UiData().IconPath()), buffTimeRemaining, buffTimeTotal, Cast<Int32>(buffList[i].stackCount))` executes inside buff-list updates. `timeRemaining` changes over time: icon/count equality alone cannot authorize suppression.

## First native evidence upgrade: input transitions and external bridge effects

**Owner: GRSP** `dev/resolver-profiler-handoff` (profiler/native only). GCO consumes the measurement, does not recreate the hook. Prefer piggybacking on existing captured source/callsite measurement infrastructure; no unbounded per-action samples or production profiler overhead outside an explicit capture.

Required bounded, source-keyed counters:
1. Actual **entry count** of each targeted `OnAction`, separate from outgoing calls; source/callsite identity and F11 session quality.
2. Counts of **previous/current input-device state**, **grace-period state changes**, unchanged consecutive observations, and first observation after player/session reset; track UNKNOWN when not observable.
3. Outcome/effects of the CET/Lua `SetLastUsedKBM` bridge if measurable without changing Lua behavior; otherwise mark **EFFECT_UNPROVEN**. A stable pair of arguments is not proof of a no-op.
4. Targeted exclusive time and bounded per-frame burst distribution for the exact bridge/callsite, *not* all `OnAction` edges and not summed with CET.
5. Rejected/unknown sample counts, drop/overflow counters, and measured instrumentation overhead in matched captures.

**Admission gate:** inspected current source; register/detach/reset ordering, return/consumption/wildcard behavior and native/CET side effects proven; exact target and arguments; no changes to listener order; no suppression while UNKNOWN, during grace transitions, or before first valid state. Consider existing G-REDruntime InputHub only after proving its interface and source-owner behavior, not as an assumed safe substitute.

## Second native evidence upgrade: UI unchanged-data/invalidations

**Owner: GRSP** for capture-only measurement; **GCO** for eventual source proof and generic generation.

Per source-matched widget/controller `SetData` family, measure complete tuple equality (icon, remaining time, total time, stack count), first write, controller/widget replacement, visibility changes, explicit invalidations, and unknown values. If floats vary each tick, report that rather than inventing a stable UI-state rate. Time-window aggregates only; avoid retaining user/player data or high-cardinality raw arguments.

**Admission gate:** the complete output-relevant tuple is identical, all setter side effects/notifications are demonstrably redundant, widget instance is unchanged, and initialization/recreation/cleanup/animation/timing remain correct. Do not add count-only guards or generic 9-of-10 throttles.

## Source proof and framework decisions

- GCO `src/GRedscript.Resolver` continues read-only source inventory and captures exact current method/receiver/scope, dependency, control-flow, lifetime and source-state evidence. Reject missing/changed source. No transformations authorized by simple function-name or path matches.
- Reuse G-REDruntime's existing input/event services **only if** measured benefit and stable consumer/lifecycle contracts are proved. Framework changes belong to G-REDruntime `main`; avoid expanding dependencies gratuitously.
- A shared CET-native player-state/NEAR/FAR service is a possible later reusable enabler. Require actual typed REDscript ABI, lifetime/UNKNOWN fail-open semantics and the real CET-generated ZIP dependency. It is not proof that input/UI work can be skipped.
- Every accepted pass needs unoptimized/partial/complete/drift/idempotence tests, actual full-stack SCC 0.5.31, generated-file inspection, and user-owned game behavior/after-capture. CI and compiler success are not gameplay or measured speedup.

## Execution order / stop conditions

1. Use existing GRSP tables to produce **source-keyed measured hotspots and uncertainty** for input bridge and UI SetData; no new sampling if the original counters already answer the question.
2. If unchanged-state/entry/side-effect rates remain UNKNOWN, implement **one bounded native instrumentation addition** in the GRSP owner, with independent native CI, source/lifecycle/overhead tests and automatic provenance-checked GCO adoption.
3. Capture matched gameplay, verify writer output and rank **avoidable** measured work, not just busy callbacks.
4. Prioritize the first **source-proven, reusable** input or UI rule only if its benefit justifies complexity. Otherwise move on to repeated read or polling families; zero new pass is better than an unsafe one.
5. Preserve R7 semantic remainder as blocked; do not optimize the 61-file portfolio to manufacture coverage.

**Validation on this document change:** read-only documentation/review only. NO Rust native build, actual game capture, new source scan, SCC compile, CI, generated ZIP or performance measurement is claimed.
