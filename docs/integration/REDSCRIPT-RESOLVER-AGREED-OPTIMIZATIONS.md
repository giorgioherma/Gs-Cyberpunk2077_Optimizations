# GCO REDscript Resolver — approved optimization backlog and generic-first gate

**Decision date:** 2026-11-16  
**Owner:** GCO `dev/redscript/resolver_handoff` (`src/GRedscript.Resolver`).  
**Status:** owner-approved *optimization directions*, NOT accepted source transformations, compiled passes, gameplay verification, or demonstrated speedups. Documentation only. GCO `main` and the installed game remain unchanged.

This document records the November 16 review of the user's actual `GCO-Capture-16112026_132224.zip` and earlier source review. The goal is to convert observed costs into **safe, reusable REDscript Resolver/Generator rules first**, then return to the agreed mod-specific remainder. Do not implement the numbered mod fixes in order simply because they were discussed in that order.

Read with [GCO-local ownership handoff](REDSCRIPT-RESOLVER-LOCAL-HANDOFF.md), [R5 high-impact evidence plan](R5-HIGH-IMPACT-EVIDENCE-PLAN.md), [R0–R10 blueprint](GRSP_REDscript_Resolver_Blueprint.md), and [active progress ledger](REDSCRIPT-RESOLVER-PROGRESS.md). The original `CNAME_ARRAY_MEMBERSHIP_V1` remains strict and frozen; it is not evidence that other loop rewrites are safe.

## A. Authoritative execution order

1. **Measure the complete installed stack**, not just this shortlist: completed F11 GRSP + CET and genuine PresentMon, with source identity, attribution quality, bounded counts, chronology, and explicit unknowns. Native VM call edges are not automatically callback entries, side-effect proofs, avoided work, or CPU-frame savings.
2. **R4 source admission and generic discovery across mods:** match actual live `.reds` syntax/control flow, receivers, types, dependencies, observation points and lifecycle. Categorize candidate *operations*, including those outside the reviewed mods. Establish whether multiple mods share a provably equivalent shape. A mod name or historical file path is never an admission rule.
3. **R5/R6 generic passes first:** rank by measured *avoidable* cost and proof strength; implement the smallest safe reusable REDscript transformation. Iterate instrumentation in GRSP only if a specific missing fact justifies bounded native work. Consider G-REDruntime services only when already provided or independently approved and genuinely shared.
4. **Validate every generic pass** before portfolio semantics: current live source/invalidation proof; pristine, partial and already-optimized variants; source drift/missing dependency/UNKNOWN refusal; repeat-generation zero-op; official full effective-stack SCC 0.5.31; inspect the actual game-relative overlay; then user-controlled gameplay and comparable before/after captures. A compile or CI success is not proof of gameplay or FPS improvement.
5. **Freeze the generic coverage ledger** by operation: `GENERIC_COVERED`, `ALREADY_PRESENT`, `SEMANTIC_REMAINDER`, `UNPROVEN_OR_DRIFTED`, `NOT_APPLICABLE`. Report exact source spans, measurements and refusal reasons; don't count files as performance.
6. **Only then open R7**: reconsider this agreed shortlist and the historical 61-file reference for source-proven, genuinely uncovered semantics. An approved investigation is **not** permission to ship an unsafe targeted pass or to copy whole historical mod files. No speculative throttling or source-agnostic transformations.

**Owners:** GCO develops managed REDscript Resolver/source scanner/generator and final ZIP; GRSP `dev/native-instrumentation` owns native REDscript evidence and F11; CET owns its existing Lua Resolver/Generator. Any CET Lua edit, shared native bridge or G-REDruntime API requires its proper owner and a coordinated contract. Keep one WPF app, one canonical GCO CI, one final combined game-relative ZIP and current installer/Restore guarantees. Never modify installed mods in-place.

## B. Reviewed shortlist — owner decision vs implementation permission

| Review # | Mod | Measured exclusive instrumented work in 384.517 s capture | Decision | First generic family to examine |
| --- | --- | ---: | --- | --- |
| 1 | ReinforcementsSystem | 38,103.6 ms | **AGREED** — cheap eligibility before expensive restricted-area geometry | Guard ordering / bounded lookup and geometry reuse |
| 2 | Dark Future | 10,151.7 ms | **AGREED** — avoid redundant buff UI and infinite-duration cooldown work | Idempotent UI writes / invalidation / unchanged-state handling |
| 3 | FlushingEtiquette | 7,046.3 ms | **AGREED** — early unrelated-input rejection and smaller modifier recognition | Input action admission / fixed-name membership |
| 4 | Crowd Voice Unlocked | 4,173.7 ms | **MAYBE ONLY** — temporary scene-patch lookup index | Repeated traversal evidence, likely targeted remainder |
| 5 | custom_quickslots / Custom Quickslots | 4,263.0 ms | **AGREED** — changed-device bridge, fixed action matching, redundant read elimination | Input state/change synchronization and fixed-name membership |
| 6 | Much Better Contracts | 3,995.7 ms | **AGREED** — zero logging/diagnostic work in normal mode; early unrelated NPC filtering second | Debug-off fast path / event eligibility |
| 7 | DigitalVixenCore (DVCore Footsteps) | 3,129.4 ms for whole owner; 652.5 ms for `FindAudioClass` | **AGREED** — reuse audio reflection lookup, reject irrelevant refreshes | Reflection handle reuse / change-driven invalidation |

Numbers are observations of the whole capture or named functions, **not predicted savings**. Instrumented exclusive time can be nested in broader mod totals; do not add function/callsite inclusive values as if independent. `DigitalVixenCore` owner total includes much more than footsteps. `FlushingEtiquette` has substantial unresolved wrapped/proxy attribution. This is a *review order*, not generator priority.

### Evidence provenance and source-state caveat

- F11 evidence: user-supplied `GCO-Capture-16112026_132224.zip`; `G-REDscript/Data/Runtime/GRSP_ByMod.csv`, `GRSP_ByFunction.csv`; `G-REDscript/Data/Developer/RSP_CallSites.csv`, `RSP_CallbackPressure.csv` and related exports. 384,516.822 ms (`WORLD`), 11,824 GRSP frames, summary quality `GOOD`, `shard_merge_ok=true`, zero dropped spikes/hot paths. GRSP measures **outgoing VM edges**; callback-entry, side-effect and receiver equivalence remain separate proof requirements.
- Source examples below: [archived 2026-11-15 original mod tree](https://github.com/giorgioherma/original_cet_source/tree/main/GCO-Capture-15112026_021913), especially `r6/scripts` and `bin/x64/plugins/cyber_engine_tweaks/mods`. This is a **dated archived stack**, not authoritative current source bytes for the November 16 capture or future deployment. Hash/structure-match the current effective user installation before admitting anything.
- R4 CI #486 previously found 48 shallow CName membership shapes in 2,319 source files but only **one** strict eligible shape; 47 failed admission. Do not broaden the rule merely to cover `FlushingEtiquette` or `custom_quickslots`.

## C. Candidate detail and correctness contracts

### #1 ReinforcementsSystem — AGREED: reject impossible reinforcements early

**Observed cost.** Whole owner 38,103.6 ms; `Utils.SortSquare` 11,630.6 ms, `Sign` 8,139.9 ms, `IsPointInQuad` 7,373.0 ms, `PointInTriangle` 4,749.0 ms, `ReinforcementsChecksCall` 2,057.0 ms. These figures identify concentration of geometry-related VM work, not independent savings. Source: `ReinforcementsSystem/ReinforcementSystem.reds` (`TryCallingReinforcements` around line 671; eligibility checks around 453) and `Utils.reds` (`SortSquare`/`IsPointInQuad`).

**Source problem.** `TryCallingReinforcements` calls the full `ReinforcementsChecksCall(puppet,false,false)` first. The latter traverses quest, district, status, restricted-character and restricted-coordinate exclusions; each point-in-quad sorts corner coordinates and performs triangle geometry. Only **after** these checks does the caller roll `RandRange(0,8)` and check `NPCPuppet.IsInCombatWithTarget(puppet, player)`. Most unrelated NPC events should never require geometric exclusion.

**Agreed direction.**
- A semantically equivalent **cheap, source-proven combat-with-player rejection** before expensive eligibility/geometry for the ordinary reinforcement-call path. Early faction eligibility where safe; do not force extra TweakDB/handler work onto every NPC. Preserve valid companion, police, fast-call and biomon semantics where those code paths differ.
- Preserve **every** author restriction: all configured restricted coordinate quads (including the previously reviewed 37-area set, subject to live-source identity), named no-go zones, character restrictions, quest/scene exclusions, NPC statuses, player/vehicle rules, faction coverage and original reinforcement probability.
- Consider reusing invariant prepared quad geometry or replacing repeated sorting **only** with proven equivalent corner order, boundary inclusion and geometry semantics. Do not silently change polygon behavior.
- `RandRange` consumes RNG state. Moving or skipping the roll can change subsequent RNG sequence even if the displayed 1-in-8 probability is unchanged. Prove or explicitly classify that behavior before admission; do not claim perfect behavioral equivalence just from equal probabilities.
- No global NPC listener removal, uncontrolled caching, or broad changes to independent `fastCall`/`biomon` call paths.

**Potential generic:** side-effect-safe cheap rejection reordering and lifetime-proven immutable geometry preparation. **Likely targeted remainder:** faction/reinforcement-specific applicability and original RNG/quest semantics.

**Acceptance:** no call into restricted-geometry path for source-proven ineligible ordinary calls; exhaustive positive/negative tests of all exclusions and polygon boundary cases; unchanged eligible reinforcement behavior, odds, initial state, detach and in-game encounters.

### #2 Dark Future — AGREED: preserve indefinite cooldown semantics, avoid unnecessary UI updates

**Observed cost.** `buffListGameController::UpdateBuffDebuffList` 4,402.8 ms and `SingleCooldownManager::Update` 2,522.9 ms; additional cooldown wrapper/`OnEffectUpdate` work. Source: `Dark Future/UI/DFBaseGameUIOverrides.reds`.

**Source problem.** `UpdateBuffDebuffList` reconstructs a buff/debuff list and updates widget data/visibility repeatedly. `SetData` is invoked with **icon, timeRemaining, timeTotal and stackCount**, not merely icon/count. `SingleCooldownManager::Update` maintains cooldown visualization; Dark Future explicitly treats `timeLeft == -1.00` and `DarkFutureInfiniteDurationEffect` differently from normal expiring effects. Avoidable repeated writes are plausible, but a countdown that changes every frame is **not unchanged state**.

**Agreed direction.**
- Use full output-relevant widget state, actual controller identity and lifecycle to suppress only *proved no-op* setter writes, visibility notifications, text/transition writes or redundant infinite-duration display updates.
- Preserve infinite indicator/time text handling, gameplay tag check, stack count, timed countdown, transition/wipe effects, widget replacement, first render, animation and hiding behavior.
- Investigate the **same identifiable infinite-duration/unchanged-UI pattern across DVCore or other mods**; do not infer equivalence merely because both have durations.
- No blanket per-frame skip, fixed refresh-rate throttle, icon-only cache or assumption that `SetData` is side-effect-free.

**Potential generic:** full-tuple unchanged-widget write suppression and stable infinite-duration state reuse, with invalidation proof. **Possible targeted remainder:** Dark Future's custom cooldown semantics.

**Acceptance:** real UI-state and invalidation evidence on first write/replacement/expiration/stack changes; complete timed and infinite-duration gameplay tests; compile all replacement/wrapper combinations. If tuple changes continuously or effects cannot be proven redundant, decline the pass.

### #3 FlushingEtiquette — AGREED: reject unrelated input before repeated modifier work

**Observed cost.** Whole owner 7,046.3 ms. `FEInput.Handle` 631.2 ms, `FEInput.IndexOfModifier` 477.5 ms. A much larger ~4,925.7 ms belongs to the `gameuiBaseMinimapMappinController` wrapper and has **unresolved virtual/proxy attribution**; do not assert it is avoidable by this input optimization.

**Source problem.** `FlushingEtiquette/FE_Input.reds` registers dedicated actions but also wraps `PlayerPuppet.OnAction`. For each event, `FEInput.Handle` obtains name/type and calls `IndexOfModifier`; that helper reconstructs a fixed three-name CName array, then walks it, even for unrelated input.

**Agreed direction.**
- Source-proven early reject for non-owned actions **before** expensive system/config lookups and modifier-array construction; recognize three explicit `FEM_Flush_*` modifier names without rebuilding an array per input.
- Preserve `ShowStatusEffects` press/release, all `Flush_Relief`, `Flush_VehicleRelief` and `Flush_WashHands` actions, modifier hold transitions, keyboard/gamepad fallback, return/consumption values, wrapped-method call order and attach/registration lifecycle.
- Analyze the costly minimap wrapper separately only with resolved proxy/call ownership and source proof; no blanket disable or proxy rewrite.

**Potential generic:** fixed-name action-set matching plus early unrelated-action rejection after full handled-action and side-effect inventory. **Likely targeted remainder:** FE's actual action/return semantics.

**Acceptance:** unknown actions still reach original wrapped method and remain unconsumed; owned actions behave identically across keyboard, controller, hold/release, status HUD and save/reload.

### #4 Crowd Voice Unlocked — MAYBE, NOT APPROVED FOR IMPLEMENTATION

**Observed cost.** Whole owner 4,173.7 ms; `CLF_Service::Patch` 991.3 ms; `RecordTakes` 781.9 ms; `SceneTake` 589.7 ms. Six caller sites account for **13,674 calls to `SceneTake`**. Source: `Crowd Voice Unlocked/CrowdLinesPhone.reds` around `SceneTake` line 906.

**Source behavior.** The mod unlocks existing recorded NPC dialogue. `SceneTake` linearly searches scene entry points, graph nodes and screenplay lines. The source already gates repeat `OnScene` patching and `RecordTakes` for recorded resource hashes; broad whole-scene caching would duplicate existing safeguards.

**Only provisional direction:** a temporary, **scene-local lookup index** during one proven patch phase; invalidate/update it if `Patch` adds graph nodes or dialogues. Preserve **last matching entry/node**, missing-ID behavior, dialogue variety, triggers, subtitles, lip sync, existing recordings and scene-resource lifetime. Never persist across mutable scene lifetimes without proof.

**Disposition:** keep below recurring work. Probably a targeted R7 candidate, **not an agreed pass**, and may be dropped if its burst benefit is weak after the generic phase.

### #5 Custom Quickslots — AGREED: make input observation change-driven

**Observed cost.** Whole owner 4,263.0 ms. `CustomQuickslotsGlobalInputListener::OnAction` 3,677.7 ms and 14,240,876 **outgoing call edges**, not necessarily callback invocations. `SetLastUsedKBM` site **273,887 calls / 2,623.4 ms**; fixed-loop operator counts exceed 6 million. `ObserveAction` reached only 34 times. Sources: `custom_quickslots/player_puppet.reds` and CET `CustomQuickslots/init.lua`.

**Source problem.** REDscript calls the CET Lua-implemented `SetLastUsedKBM(player.PlayerLastUsedKBM(), player.gracePeriodOver)` before comparing the locally cached device state, then checks an array of **22 D-pad/modifier action names** on every observed event. The Lua callback stores the state only after the grace period, but unchanged arguments alone do **not** prove a cross-runtime call is effect-free across initialization/reload.

**Agreed direction.**
- Synchronize input-device state on verified *change* and required initial/grace/re-attach/reload transitions, without losing mixed keyboard/controller mode or UI hint changes. No suppression until the first valid known state.
- Replace fixed 22-name array traversal with source-proven cheaper matching; preserve exact recognized action set, ordering/duplicates if meaningful, action type, modifier holds and `ObserveAction`/`QueueEvent` outcomes.
- Read `PlayerLastUsedKBM` once per callback only when equivalent across the original evaluation points; prove purity/observability.
- Any edit to the CET Lua implementation itself is outside the managed REDscript pass: coordinate with CET's existing owner; do not have GCO create a competing Lua generator.

**Potential generic:** transition-driven cross-runtime bridge with explicit side-effect/lifecycle proof; fixed CName action membership; repeated pure getter reuse.

**Acceptance:** native evidence of actual callback entries, unchanged input-state rate and bridge effects; full device-switch/grace/reload tests, first display hints, keybinds and controller behavior. Without effect proof, refuse the bridge-elision pass even if it is the top measured callsite.

### #6 Much Better Contracts — AGREED: normal mode must do zero logging work

**Observed cost.** Whole owner 3,995.7 ms; `MBCGigSnapshot::TraceEnabled` **2,911.8 ms**. Inside that path, `MuchBetterContractsSettings.MasterEnabled` **24,375 calls / 1,886.5 ms** and `EnableDebugLog` **24,375 calls / 977.1 ms**. Source: `MuchBetterContracts/Diagnostics/GigSnapshot.reds` and CET `MuchBetterContracts/settingsManager.lua`, `logger.lua`.

**Source problem.** Trace checks on common NPC/gig callbacks cross REDscript -> CET Lua to read settings repeatedly, although the archived default is `EnableDebugLog=false`. The REDscript `MBCError` path and CET `Logger.Error` can still write ERROR messages when debugging is otherwise disabled. Expense occurs even when no debug content is ultimately logged. Runtime NPC tracing also fans out through numerous gig-specific wrappers.

**Owner's explicit normal-mode requirement:**
- **Logging OFF**: **no output at any log level**, no debug snapshots, no diagnostic string construction, no logging-state Lua getter calls per NPC event; preserve gameplay behavior exactly.
- **Logging ON by explicit user choice**: all previous debug/error/warning/info/trace facilities remain available, regardless of expense. A settings transition must establish new state safely at runtime; source/cross-runtime ownership and synchronization must be proven. Never permanently compile logging away if that breaks user enablement.
- Gate before diagnostic work, rather than doing the work and suppressing `ModLog`/`print`. Distinguish purely diagnostic code from messages that are tied to operational recovery, side effects or user-visible failure handling; those need independent proof.
- **Secondary candidate:** cheaply reject unrelated NPC/gig event paths before owner-specific processing, preserving complete attach/detach, mission activation, fact tracking, spawning, rewards, quests, security responses and cleanup. Avoid blanket NPC-hook removal.
- `ContractDirector` already runs a **3-second active-contract tick** and stops rescheduling when no contracts exist; do not misidentify that as the main measured culprit.

**Potential generic:** off-mode diagnostic fast path and source-proven event eligibility; **specific ownership remainder:** MBC's CET settings bridge and diagnostic semantics. Ensure a cross-domain change does not violate CET/source-owner rules.

**Acceptance:** logging disabled produces no MBC log traffic and avoids diagnostic computation; enabled diagnostic output works at runtime; inactive unrelated NPC events avoid debug-only work; original missions, events, completion and Restore unaffected.

### #7 DigitalVixenCore / DVCore Footsteps — AGREED: reuse reflection and stop irrelevant refresh

**Observed cost.** Entire DVCore owner 3,129.4 ms (includes other features); `DVFootstepsService.FindAudioClass` **652.5 ms** and `EquipAs` **271.6 ms**. **41** calls to `FindAudioClass` trigger **41** `Reflection.GetClasses` calls (~245.8 ms) and **513,730** per-class `GetFunction` calls (~169.0 ms); roughly **12,530 class probes per refresh**. Sources: `DigitalVixenCore/DVCore_Footsteps.reds` and `DVCore_PlayerOutfit.reds`.

**Source behavior.** Footstep sound depends on footwear classification and supported AudioXL sets, not a per-step audio callback. `FindAudioClass` scans engine reflection classes for `EquipNewFootwearByPlayer` every time the service tries to equip a sound donor. `DVPlayerOutfit` invalidates outfit/clothing on **any** item added or removed from a slot and then calls `DVFootstepsService.Poke`; not every equipment change affects shoes. `m_applied` only gates a note/log, not redundant reapplication.

**Agreed direction.**
- Resolve the audio class/function handle once for its **proved valid lifetime**; invalidate on relevant session/reload/resource changes or fail open and re-resolve when the handle is invalid. Do not silently cache a stale RTTI reference across a changed game context.
- Skip footwear work on proven irrelevant equipment changes or repeated identical effective sound state **only if** original side effects/other mods cannot require reapplication. Protect first application, missing donated records, session start/end, `RestoreCarriers`, donor selection, original worn-shoe recovery and other mods' audio changes.
- Preserve every sound mapping, direct vs carrier sets, custom AudioXL contributions, Equipment-EX outfit classification and resource readiness. Do not disable footsteps or replace the mod's sound choices.

**Potential generic:** reflection lookup reuse with lifetime guard, and event-driven dirty-state no-op suppression. **Possible targeted remainder:** footwear/audio state invalidation, multi-mod donor interactions.

**Acceptance:** repeat outfit changes and nonfootwear equipment churn do not rescan reflection unnecessarily; shoes always sound correct across load/restore, unavailable resources, AudioXL and Equipment-EX combinations. Re-check `m_applied` semantics before a same-set early exit.

## D. Cross-mod generic candidate families — order by evidence, not review number

| Family | Candidate sources | Minimum source/measurement proof | Do not do |
| --- | --- | --- | --- |
| **G-Guard** — early cheap eligibility | ReinforcementsSystem, FE input, MBC NPC dispatch | Source-proven purity/order, full accepted-event and rejected-event sets, wrapper/return behavior, RNG/quest effects | Global callback removal, reordering side-effecting calls or skipping required RNG without disclosure |
| **G-Input** — fixed names and transition bridges | Custom Quickslots, FlushingEtiquette, other input owners | Actual callback entries, input transitions, exact CName types, bridge side effects, grace/registration/detach | Blind array-to-switch conversion, universal InputHub replacement, unknown-state suppression |
| **G-UI** — no-op retained state writes | Dark Future, possibly DVCore UI and other owners | Complete output tuple, unchanged widget/controller identity, animation/notification meaning, invalidation events | Count-only guards, arbitrary UI rate limits, treating timeRemaining as stable |
| **G-Reflect/Read** — repeated stable lookup | DVCore Footsteps, other reflection/provider owners | Symbol/receiver identity, side-effect/purity, scope/lifetime and invalidation; cross-mod structural recurrence | Global permanent cache of unverified engine objects or rewriting every same-name getter |
| **G-Diagnostics** — off-state fast path | Much Better Contracts, other proven debug-only hotspots | Debug state transitions, log call graph, cost before output, proof of diagnostic-only effects | Stripping fatal recovery behavior, disabling explicitly enabled debugging, silent changes to unrelated mod settings |
| **G-SceneIndex** — bounded repeated traversal | Crowd Voice Unlocked (provisional) | Mutation stage, duplicate-ID/last-match semantics, resource lifetime, repeated searches and burst benefit | Persistent whole-scene cache or initial targeted optimization before generic freeze |
| **G-Dormancy** — active-work scheduling | Other mods only if supported by observed proof | True callback entry rates, stable activity/wake/cancel lifecycle, measured gain and acceptable latency | Generic slow-down, auto-interval changes or unrelated game-state polling |

The first priority should be **high-value generic rules with actual *avoidable* work proved**. The first high-work candidate may still be refused: e.g. cross-language bridge side effects or UI time changes can invalidate a seemingly obvious optimization. If a mechanism applies only to one mod after full proof, mark it targeted for R7 rather than disguising it as generic.

## E. Resolver and generator contract for each admitted operation

Maintain a per-candidate decision record with at least:

- `capture_id`, capture quality, verified GRSP producer and PresentMon provider/overlap or `NOT_CAPTURED`, measured callsite/function source key, observed costs, edge-vs-entry status, uncertainty, and pre/post scenario comparability.
- Real current effective game-relative source path, structural source span, exact receiver/type, dependency/module guards, source-state category (`PRISTINE_APPLICABLE` / `PARTIALLY_APPLICABLE` / `ALREADY_COMPLETE` / `UNSAFE_OR_UNPROVEN` / `DRIFTED` / `MISSING_DEPENDENCY`), and hash only for invocation-consistency/drift detection.
- Preconditions, excluded states, observable side effects, evaluation/order/return and RNG semantics; first-use/UNKNOWN state; player attach/detach, save/load, reload, restore and invalidation triggers.
- Generic family and machine-readable `eligible` / `refused` result with a **specific refusal reason**, never a guessed savings figure. Identify any required CET, GRSP native or G-REDruntime owner change before generation.
- Tests: positive and adversarial source fixtures; pristine/partial/complete/idempotence; current full-effective-source SCC (0.5.31); compiled game-relative ZIP overlay and provenance; scenario-level in-game behavior; comparable capture evidence where gain is claimed.
- When a candidate overlaps a CET optimization or earlier partial overlay, generate missing work only, neither duplicated pass nor destructive revert.

**Stop/refuse conditions:** stale/missing current mod source, unsupported syntax, unknown receiver/side effects, unresolved wrapper costs treated as direct ownership, unknown initialization/invalidation, incompatible dependencies, unproven RNG/order equivalence, incomplete F11 evidence, code that cannot compile against the full stack, or no meaningful expected benefit after proof. Emit an explicit reason and leave source unchanged.

## F. Immediate next work — generic phase only

1. In GCO, build the **cross-mod operation ledger** from the 13:22 capture's native evidence and real current source inventory. Separate call edges from actual callback counts and isolate the cheap-guard, input, reflection and UI families. Do not use the shortlisted mods as a hardcoded allowlist.
2. Evaluate **G-Guard**, **G-Input**, **G-Reflect/Read** and **G-UI** as generic R5 candidates using exact source proof. For `Custom Quickslots` and `Dark Future`, the existing R5 high-impact evidence plan's UNKNOWN/side-effect/tuple gates are mandatory; if extra evidence is needed, request one bounded GRSP-native measurement, not a second GCO profiler.
3. Implement only the first structurally safe, worthwhile generic rule, with conservative refusals and whole-stack SCC; validate fresh game evidence before calling it a performance improvement. Extend generic coverage while it remains justified.
4. Reconcile and freeze the **generic coverage ledger**. Then revisit **#1/#2/#3/#5/#6/#7 targeted remainder** and **#4 only if separately approved**, alongside the historical 61-file reference. This backlog is *not* permission to jump directly to targeted mod patches.
5. Do not touch GCO `main`, the existing two-page UI, installer/Restore, or GRSP's Rust source as part of this documentation task.

## G. Review-state changelog

- **2026-11-16:** owner agrees #1 ReinforcementsSystem, #2 Dark Future, #3 FlushingEtiquette, #5 Custom Quickslots, #6 Much Better Contracts (explicit fully silent default), and #7 DVCore Footsteps *optimization directions*. #4 Crowd Voice Unlocked remains **MAYBE**. No transform or CI performance result approved by this conversation.
- **2026-11-16:** first real generic pass and generic coverage proof **must precede** any targeted remainder. This explicit priority reinforces existing R5/R7 contracts and supersedes any reading of the review numbers as an implementation queue.
