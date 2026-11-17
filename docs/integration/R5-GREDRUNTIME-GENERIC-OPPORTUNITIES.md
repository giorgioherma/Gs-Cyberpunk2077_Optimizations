# R5 — G-REDruntime-backed generic passes: runtime-evidence admission

**Status:** source+F11-backed candidate contracts, **not implemented generic passes, compilation proof or measured savings**. Date: **2026-11-17**.  
**Code owner:** GCO `dev/redscript/resolver_handoff` — managed Resolver, source proof, tests and generated overlays.  
**Runtime owner:** `giorgioherma/G-REDruntime_Framework` `main` (currently `3cf3740`, manifest `0.6.0-pass6`). Profiler evidence remains GRSP `dev/native-instrumentation`. **Do not duplicate framework source into GCO**.

## Critical generic-first scope clarification

**NO MOD IS EXCLUDED from generic discovery, admission, tests or future generation.** This explicitly **includes all seven previously reviewed mod owners**, and all others found in the current user's installation. “Seven at the end” means defer only **mod-specific semantic cleanup** to R7, after broadly reusable R5/R6 rules. If a generic pass matches ReinforcementsSystem, Dark Future, FlushingEtiquette, Custom Quickslots, Much Better Contracts, DVCore Footsteps or Crowd Voice Unlocked, evaluate it with exactly the same source-structure gates as every other mod. Crowd Voice's *mod-specific* experiment remains MAYBE/not approved. No whitelist, blacklist, mod-path allowlist, hardcoded source hashes or author-specific synthetic support.

**Working rule:** optimize operations, not named mods. Rank actual avoidable *work and bursts*, not number of generated files, owners, VM edges or a hypothetical hit rate. Preserve every mod's timing, actions, consumption, lifecycle, state, author rules, fallback and output.

## Grounded evidence / do not misread

- User's current source snapshot: `r6LIVEnow.zip`, **2,319 REDscript files** across 249 top-level directories (many are dependencies/organizational folders, **not 249 proven runtime owners**); none of these `.reds` files currently references `GRedRuntime`. The independent framework archive contains **10 owner-runtime `.reds` files**. A **different user-provided partial optimization reference** (`G-REDruntime_Optimizations_Partial.zip`) contains **61 `.reds` files**, of which **24 files across 14 distinct folder owners reference the framework**: **10 owners InputHub**, **5 Scheduler**, **9 StateCache**, **2 ContextService**. These are **design references, not generated-pass/admission/compilation or gameplay proofs**; do not copy whole files or treat already integrated owners as permanent lists.
- Read `GCO-Capture-16112026_132224.zip/G-REDscript/Data/Developer/RSP_CallbackPressure.csv`, `RSP_CallSites.csv`, `RSP_CallbackReuse.csv` and `Data/Runtime/GRSP_ByFunction.csv` as separate evidence types. The `INPUT_ACTION` callback-family rows span **32 owners**, **21,113,624 outgoing VM edges**, **5,377.8 ms instrumented exclusive**; the `UPDATE_REFRESH` family spans **75 owners**, **11,510,248 outgoing edges**, **12,448.7 ms instrumented exclusive**. **These are heuristic callback-family aggregates, NOT proven callback entry counts, avoids, or forecast savings.** The `RSP_CallbackReuse` file has known frame coverage truncation in 41/81 rows in this historical capture; GRSP producer CI #26 fixed its source, but a newly captured game result is not present here.
- Source inventory found **90 `RegisterInputListener(...)` call sites across 20 top-level folders**, including **13 apparent one-argument wildcard registrations across nine folders**. *Apparent* is intentional: these are source-text candidate counts, not structurally/proven registrations or distinct runtime listeners; some are inside wrappers and conditional attach/detach. Named action registrations exist too. The 13 candidates include independent owners BrowserExtension, GigCore, GearRecoverySystem, DrugDealer, custom_quickslots, gt_8ug_apt, RedWindows and Dark Future (plus VirtualAtelierDelivery).
- `GameInstance.GetQuestsSystem` target: **633,163 outgoing calls, 301.3 ms exclusive, 23 GRSP owners**. Generic `GetPlayer`-named target family: **307,794 calls, 482.5 ms, 45 owners**. All `GetGame`-named targets: **2,479,427 calls, 470.3 ms, 90 owners**. These **cannot** be added as pure service-cache savings: they have different receiver scopes, lifetimes and side effects, and direct exclusive cost is relatively small.
- `DelaySystem`-named target family: **160,768 calls, 167.0 ms exclusive, 39 owners**. Thus a scheduler-only refactor has **no evidence of a large direct CPU saving**. `G-REDruntime.Scheduler` offers integration and deadline consolidation, not automatic elimination of callback bodies.

All figures are recorded over **384.517s** from one F11 WORLD capture. Historical calls alone do not prove current dynamic mod inventory. One 240–270s hotspot contributes **39.25% of captured exclusive work**. No new capture is requested by default.

## Candidate 1 — `G_INPUTHUB_INTEREST_V1`: engine ingress narrowed to the exact required actions

**Priority:** **High**, because it has an actual cross-author **potential to prevent a large number of irrelevant callbacks**, not merely speed up CName comparisons. **Framework:** `InputHub.Subscribe(actionName, listener)`, `Unsubscribe(handle)`, lazy per-action engine registration, synchronous `InputEvent` (name, type, consumed). This service already exists; prefer it to new duplicated routing systems.

**Recognizable generic input shape:**
1. A source-proven `RegisterInputListener(listener)` **wildcard engine registration**, with a uniquely resolved listener class/method and its exact `OnAction` consumer behavior.
2. The listener has a **closed, complete finite set of literal or safely derived CName actions** and exact action-type predicates. Unknown actions must have **no required side effects**. Source-aware effect tracing includes forwarded helpers and other mods' wrappers.
3. The original registration/unregistration, ordering, attached player instance, first action, return value, consumption and failure fallback can be preserved by a generated lifecycle adapter. No new per-frame poll, no global player listener removal and no changed keyboard/gamepad interpretation.

**Source/capture evidence examples—not an owner allowlist:** DrugDealer's five wildcard input listeners each produced **821,665 outgoing edges** (4,108,325 combined), GearRecoverySystem input listener **821,661 edges**, GigCore scroll listener **536,239 edges**, BrowserExtension input listener **268,026 edges**; Custom Quickslots has **14,240,876 outgoing OnAction edges**, but it has cross-language input-device observation effects and **must not be silently narrowed**. The existing `ACTION_LITERAL_MEMBERSHIP_V1` cuts pure membership work but **does not prevent wildcard callback dispatch**. This pass targets a different, bigger layer.

**Important blocker in actual framework source:** `InputEvent` exposes only `actionName`, `actionType`, `consumed`; the original `ListenerAction` and `ListenerActionConsumer` are **not forwarded**. `IsButtonJustPressed(action)`, `IsMainAction(action)`, held-action predicates and engine consumer effects cannot be assumed equivalent. The current Hub also runs an array-based specific-route match and has separate wildcard/specific ingress policy, so a new hub subscription is not always cheaper. **First narrow version** must accept only actions expressible through existing fields with a proven same consumption/return contract; otherwise return `RAW_ACTION_REQUIRED`, `CONSUMPTION_UNPROVEN` or `REGISTRATION_ORDER_UNPROVEN`. Any required InputHub API extension must be implemented/tested **in the owner framework repo** before GCO consumes it.

**Proof/CI:** compare engine callback ingress, forwarded listener deliveries, consumption, same-event ordering, first input, duplicate subscriptions, attach/detach, reload, each keyboard/gamepad action, missing-framework fail-safe behavior; source untouched + idempotence/partial/drift + entire current SCC stack; compile genuine game-relative files. No guessed percentage reduction from outgoing edges.

## Candidate 2 — `G_DIRTY_VIEW_GENERATION_V1`: shared invalidation, whole refresh work avoided

**Priority:** **High conditional**. **Framework:** `DirtyFlags.Mark/Version/ChangedSince` for independent consumers, plus optional `EventBus` fan-out and `StateCache` where valid. This is **not blanket UI throttling** or indiscriminate sleep. It is a source-proven **versioned dependency graph** between real mutators and expensive readers.

**Recognizable generic shape:**
1. A recurring `Update/Refresh` (or any other named callback—method name alone is never enough) rebuilds a derived list/selection/widget while its inputs originate from a **closed set of source-proven events/mutators**.
2. The source parser proves all writers/aliases, widget identity/generation, first-use/invalidation and any output-relevant time/animation/notification effects. The callback's side-effecting segments stay in original order; only a pure, provably unchanged subgraph is skipped.
3. A cheap per-consumer `ChangedSince(flag, lastSeenVersion)` guard plus `Mark` on **every** relevant mutation/wake boundary replaces the repeat rebuild. On unknown, source drift, new/replaced controller, reconnect/reload, missing framework or liveness change, run the full original computation. **Use per-consumer version, not global `Consume`**, which would starve other listeners.

**Evidence:** `UPDATE_REFRESH` pressure (818 callback-family rows, 75 owners, **12,448.7 ms** exclusive and **11.51M** outgoing edges). It includes Dark Future buff/UI, HQ HoloTravel and HoloGirl resource updates, DigitalVixenCore UI, ShatteredChrome HUD, VehicleMileage gauge, RedWindows list visuals, CourierJobs tick. These are **independent source families**, not all automatically eligible. E.g. Dark Future's `SetData(icon,timeRemaining,timeTotal,stackCount)` has a changing time float; resource `OnUpdate(ResourceEvent)` is event driven, not proof of frame polling. Test the *actual refresh dependency*, not the name.

**Critical runtime nuance:** G-REDruntime `ContextService` refreshes combat and vehicle state **lazily** with a **50 ms cache interval**; `DirtyFlags` is **not** an automatic game-state change detector. The generator must add or prove all write-side `Mark` boundaries; otherwise a polling consumer can miss real changes. `Scheduler` min 50ms and missed-tick policy cannot substitute for a required animation frame.

**Payoff metric:** the number of **whole rebuild bodies and outgoing calls not executed** in unchanged generations, with exact changed/first/recreated behavior preserved. Candidate may require a limited owner-side framework feature **only** after a successful narrow source proof; no large general semantic catalog.

## Candidate 3 — `G_LIFETIME_SYSTEM_HANDLE_V1`: source-local repeated system resolution via StateCache

**Priority:** **Secondary / lower risk** relative to 1–2. **Framework:** already available `StateCache` weak system references and explicit player lifecycle invalidation. This pass could be broad (23 owners quest-system readers, 45 owners player-lookups), but direct measured calls are **hundreds of milliseconds over the entire six-minute capture**, not tens of thousands. Avoid making this the marquee performance pass.

**Recognizable generic shape:** repeated identical **stable service accessor with same exact `GameInstance` and owner scope**, with no intervening invalidation, external pointer mutation, lifecycle transition or observable side effects. A source-owned weak handle may reuse the exact service only inside a verified validity epoch. `GetFact`, changing quest state and arbitrary player combat/vehicle status are **not** immutable values and may not be cached as if they were. Refuse unknown owner/game/receiver identity. Runtime readiness and absent framework must preserve original fallback (no crash, no silent mod disabling).

**Potential nice low-complexity first variant:** replace *multiple identical safe resolutions inside a single callback* with a local reference; avoid framework lookup if it would cost more. **Framework integration** is only justified for proven reuse across callbacks and stable lifetimes; test actual A/B call elimination. The amount of `GetGame`/service access removed, not changed code size, is success.

## Optional low-priority family — shared Scheduler / EventBus only when they remove real work

The hand-optimized reference demonstrates Scheduler integrations in **five distinct owners**; `DelaySystem` itself accounts for only **167 ms** direct exclusive over 384.5 s, 39 owners. Moving a 0.1 s timer to shared Scheduler without removing its expensive body may save little or regress performance. Scheduler carries a **50 ms interval floor** and **does not replay missed deadlines**. Don't change cadence, queued work, cleanup or callback order as a “generic” shortcut. Similarly, EventBus/HookBus fanout creates a new dispatch layer; absence of measured duplicate event work means no autonomous EventBus or HookBus migration pass is justified yet.

## Practical next work for an agent

**Do NOT code three broad rewrites immediately.** Each rule needs a narrow family scan (not owner allowlists), a measured skip-work admission threshold, and conservative first-failure reason. Recommended:

1. Extend GCO's existing **read-only** `CallbackSemanticResolver`/generic opportunity scanner to identify complete wildcard-registration → finite action-interest → listener-result chains across **all installed `.reds`**. Join source keys to F11 `RSP_CallbackPressure` and `RSP_CallSites`, separating outgoing edges and callback entries. Rank source-proven subfamilies, classify `RAW_ACTION_REQUIRED`, `CONSUMPTION_UNPROVEN`, `WRAPPER_SIDE_EFFECT`, `ATTACH_DETACH_UNPROVEN`. Only after that consider InputHub code generation.
2. Likewise classify `Update/Refresh` source spans with write-side event proofs. Prioritize high measured **pure** rebuild bodies and exact invalidations. Refuse animation/time/widget mutation uncertainty; do not throttle. Use G-REDruntime `DirtyFlags` on proof, and only then `EventBus` if publisher/subscriber ownership is viable.
3. Run a small fallback-safe StateCache scope experiment on one **generic** exact getter family; cap effort strictly if measured direct gain is insignificant. Avoid extra native hooks and new captures for mere broad *frequency*. Use existing data and shorter measured frame windows first; request a single bounded new F11 capture only for a near-admitted operation with a narrowly missing high-impact fact.
4. Mandatory tested outputs: untouched, partially optimized, fully optimized, drifted, missing framework, dependency and override conflicts, idempotence, SCC official 0.5.31 whole stack with **current owner G-REDruntime checkout**, actual output ZIP, owner-by-owner gameplay proof, and before/after PresentMon when a performance gain is claimed.

**Keep exact branch ownership:** GCO managed `dev/redscript/resolver_handoff`, GRSP native `dev/native-instrumentation`, framework `main`; CET Lua resolver unchanged unless a CET-owned change is separately proven. No extra UI, workflow, runtime binary or second optimization ZIP.

## Verification status

This is **analysis and design documentation only**. Existing last full **GCO #569** was SUCCESS on prior source. No framework DLL/source edits, new compiled transforms, game capture, valid “actual saved ms” estimate or gameplay result is claimed here. Only source-level candidate counts and historical runtime exposure are established.

Related: [R5 work-elimination research](R5-WORK-ELIMINATION-OPPORTUNITIES.md) · [agreed source-first backlog](REDSCRIPT-RESOLVER-AGREED-OPTIMIZATIONS.md) · [R5 high-impact evidence](R5-HIGH-IMPACT-EVIDENCE-PLAN.md).
