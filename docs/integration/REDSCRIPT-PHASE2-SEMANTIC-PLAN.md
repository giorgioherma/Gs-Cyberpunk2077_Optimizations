# REDscript Phase 2 — semantic optimization portfolio (planning ledger)

**MANDATORY PHASE 2 IMPLEMENTATION POLICY:** [IMPORTANT FOR AGENTS — REDscript Optimization Rules](IMPORTANT-FOR-AGENTS-RED-OPTIMIZATIONS.md). The owner requires **partial → complete**, **post-generic stock → complete in one Generate**, and **compatible version/custom edits → optimized**, with **narrow skips only for genuinely incompatible targets or unproven critical safety properties**. Historical archives demonstrate behavior, never serve as required precursor inputs or whole-file replacements.

**Status:** 2026-11-20 source/archive audit; **documentation and proposed ordering only**. No Phase 2 generator rules, compiled output, or measured A/B benefit are claimed. Owner approval of the historical gameplay-tested passes establishes a useful behavioral reference **for the versions on which they were tested**, not automatic admission of altered live sources. This document reserves the Phase 2 / historical R7 area for decisions before implementation.

**Authoritative implementation route:** GCO-owned REDscript Resolver/Generator, `dev/redscript/resolver_handoff`; deployment eventually through the one GCO application/ZIP. Existing **ten approved generic rules execute first**. Read `AGENTS.md`, `docs/LATEST-WORK.md`, `docs/integration/R5-WORK-ELIMINATION-OPPORTUNITIES.md`, `docs/integration/REDSCRIPT-RESOLVER-AGREED-OPTIMIZATIONS.md`, G-REDruntime's `documentation/INTEGRATION_OVERLAY_MODEL.md`, and the current component source before implementation. Do not edit the stable CET/GRSP profilers, duplicate framework services, or change generic generators just to accommodate historical output.

## 1. Evidence and exact archive inventory

Primary **current source**: uploaded `scriptsliveAFTERgeneric.zip`, SHA-256 `3d1aaff0327196350621dcc54bd99cf03433556602f09b3b572b926f05768c3b`; its `scripts/` root maps to game-relative `r6/scripts/`. It contains **2,331 .reds files**. The user's `original_cet_source/main/base_after_generic` is also a separately uploaded source snapshot; use current effective live source for every future Generate, not a permanent fixture/mod list.

Historical **individual pass references**: `G-RED_optimizatios_seperate_passes.zip`, SHA-256 `713cc45424df64ed763bc7a413ce7538c7adb790e3e8e86e3518999136abd8cc`. **13 named passes**, **92 pass-file occurrences**, collapsing to **62 unique paths**. The individual passes explain intent and chronology; repeated paths are cumulative, **not independently composable patches**.

Historical **combined implementation reference**: `G-REDruntime_Optimization_All_Mods.zip`, SHA-256 `e0746bf55026d8e68ac92cb5eedee3f8c7abcb6a706d3e51020b12e81256c2f6`: **62 .reds files across 31 owner folders**. All 62 combined file bytes match the **last occurrence** of that path in the separate pass sequence. Thus the combined archive is the correct historical end-state reference; it is **not** a compatible complete overwrite of the post-generic source.

Current-source join: **56 of 62** output paths already exist; **six do not**. Of these, four belong to currently absent mod source paths (two Exodus - Arasaka Training files; one Repeatable Cyberpsycho Encounters file; one Street Vendors file). The other two are **new generated helpers** for *installed* ImmersiveCyberware and zzy_bChipwareExpansion. Their absence from source is expected, but they may be emitted only with exact owner/dependency and callsite proof. There are **28 represented owners present and three absent** among these 31. Mods in the live collection without historical pass output remain eligible for source review; no historical mod inventory/ZIP may act as the live installed-mod list.

**Direct generic composition conflict:** The combined historical files omit **32 `GCO-GENERIC` markers in 12 files**, plus the `GRSP-GENERIC:CNAME_ARRAY_MEMBERSHIP_V1` marker in `custom_quickslots/player_puppet.reds`: **33 markers / 13 files in total**. A missing marker does not by itself prove loss of the optimized behavior, but does prove that overwriting with old combined bytes does not preserve the verifiable post-generic source shape. Examples: 16 generic sites in Enhanced Vehicle System, five in ReinforcementsSystem, five DrugDealer membership guards, three in negotiableaffection, and one each in FE_Input, RedWindows, BrowserExtension and custom_quickslots. All such changes must be **rebased at the transformation source** or refused, not corrected by a post-copy generic repair pass.

### Historical pass breakdown

| Pass | Files in that pass | Work represented / composition notes |
|---|---:|---|
| 2.0 | 6 | Shared InputHub/action routing: BrowserExtension, custom_quickslots, Enhanced Vehicle System, FlushingEtiquette |
| 2.1 | 7 | Input routing consolidation: DrugDealer, GearRecoverySystem, Street Vendors (absent) |
| 2.2 | 9 | Context/scheduler/state follow-up: GearRecoverySystem, NightCityRemembers, ShatteredChrome, Street Vendors (absent), VehicleMileage |
| 3.0 | 9 | Wrapper and hotpath work: CyberwareEx, Dark Future, Exodus (absent), Repeatable Cyberpsychos (absent), StealthRunner, Vehicle Call Plus, custom_quickslots, negotiableaffection |
| 4.0 | 6 | Combat/high-cost path work: DigitalVixenCore, HQ_HoloGirl, ReinforcementsSystem, StealthRunner, zzy_bChipwareExpansion |
| 5.0 | 8 | Remaining owners: Dark Future, FlushingEtiquette, HQ_HoloGirl/HoloTravel, MapWaypointBugFixes, RedWindows, TriggerModeControl, zzy_bChipwareExpansion |
| 6.0 | 10 | Frame-pacing work: Dark Future, Enhanced Vehicle System, HQ_HoloTravel, MapWaypointBugFixes, ReinforcementsSystem, ShatteredChrome, VirtualAtelierDelivery, zzy_bChipwareExpansion |
| 7.0 | 12 | Chase/frame-floor work: Dark Future, DigitalVixenCore, DisablePositionReveal, LizziesBDs, NightCityRemembers, Quickhack_Fixes, ReinforcementsSystem, RunningMan, StealthRunner, zzy_bChipwareExpansion |
| 7.1 | 2 | **Behavior fixes** for ReinforcementsSystem and MapWaypointBugFixes; must be included in final semantics, not omitted |
| 7.2 | 3 | Washroom/lifecycle fixes: FlushingEtiquette and MapWaypointBugFixes |
| 7.2r1 | 1 | **FE_Places compilation correction**, mandatory when porting that history |
| 8.0 | 11 | Sustained-throughput/cache work: ImmersiveCyberware (+ new helper), RedWindows, custom_quickslots, zzy_bChipwareExpansion (+ new helper) |
| 9.0 | 8 | UI dormancy: DigitalVixenCore, FlushingEtiquette, RedWindows, RunningMan |

### Owner coverage / disposition (all 31 historical owners)

| Historical owner | Combined files | Present post-generic? | Initial handling |
|---|---:|---|---|
| BrowserExtension | 3 | yes | InputHub migration; generic action snapshot overlap |
| CyberwareEx | 1 | yes | Input attach/detach and wrapper semantics |
| Dark Future | 1 | yes | Multi-pass cooldown/UI, high sensitivity to time changes |
| DigitalVixenCore | 4 | yes | Isolated Footsteps first; UI dormancy separately |
| DisablePositionReveal | 1 | yes | Re-evaluate combat/state callbacks |
| DrugDealer | 5 | yes | Shared input handlers; generic membership overlap |
| Enhanced Vehicle System | 1 | yes | Large source, 16 generic marker overlaps |
| Exodus - Arasaka Training | 2 | **no** | Skip absent mod; never install it from history |
| FlushingEtiquette | 3 | yes | Input + lifecycle + final compile fix as one compatible plan |
| GearRecoverySystem | 1 | yes | Input and context dependencies |
| HQ_HoloGirl | 1 | yes | Preserve combat/refresh side effects |
| HQ_HoloTravel | 1 | yes | Coordinate hotpath and pacing changes |
| ImmersiveCyberware | 3 (1 helper) | yes; helper new | Check owner and new cache-helper dependency |
| LizziesBDs | 1 | yes | Extensive callback restructuring; high drift risk |
| MapWaypointBugFixes | 1 | yes | Preserve 7.1/7.2 later behavior corrections |
| NightCityRemembers | 2 | yes | Context and culling semantics |
| Quickhack_Fixes | 1 | yes | Small targeted state path, verify gameplay |
| RedWindows | 4 | yes | Shared UI prerequisite for DVCore dormancy; array-bound overlap |
| ReinforcementsSystem | 4 | yes | 4.0/6.0/7.0/7.1 cumulative; complex geometry/RNG/timing |
| Repeatable Cyberpsycho Encounters | 1 | **no** | Skip absent mod |
| RunningMan | 2 | yes | Movement and UI cadence, preserve transitions |
| ShatteredChrome | 2 | yes | Context and frame-pacing integration |
| StealthRunner | 2 | yes | Wrapper/combat/change semantics |
| Street Vendors | 1 | **no** | Skip absent mod |
| TriggerModeControl | 1 | yes | Action state semantics |
| Vehicle Call Plus | 1 | yes | Fast travel/lifecycle proof |
| VehicleMileage | 4 | yes | Shared scheduling, four UI files |
| VirtualAtelierDelivery | 1 | yes | Frame pacing and actual delivery timing |
| custom_quickslots | 1 | yes | InputHub + device grace + cached D-pad, membership overlap |
| negotiableaffection | 1 | yes | **Unsafe historical overwrite**; current source has additional logic |
| zzy_bChipwareExpansion | 5 (1 helper) | yes; helper new | Large rewrite and generated cache helper need exact source proof |

### Source/version divergence requiring special treatment

- **negotiableaffection/Main/NA_Interaction.reds:** current post-generic source has quest fact registrations, active-client memory synchronization, save-lock handling, optional module import, and related functionality **absent from the old combined file**. The old file is ~689 lines vs historical optimized ~488; do **not** port that full file or silently drop any of these behaviors. This is a confirmed source-shape/behavior delta, but the archives alone do not prove which exact mod version introduced it.
- **zzy_bChipwareExpansion/chipsScripts/chipPerkUnlock.reds:** ~225 current lines vs 65 historical lines; entire blocks change. Treat as a high-risk semantic redesign, not a small replacement.
- **ReinforcementsSystem:** ~703-to-843-line change in ReinforcementSystem plus Utils replacement; old combined reintroduces generic `SortSquare` expression work, replaces point-in-quad with precomputed geometry, reorders `RandRange` before eligibility, and changes TickSystem's all-sweeps-every-3s schedule to quarter-phase 0.75s. These may have passed historical gameplay but **RNG call order, boundary equality, state freshness, event filtering and latency** require explicit current-source acceptance. Preserve fixes from 7.1.
- **Dark Future:** old buff update defers same-count refreshes and cooldown renders by callback phases. Identical list lengths do **not** prove identical icons, timeRemaining, timeTotal, stackCount or animation state. Re-admit with full output tuple + invalidation; do not copy unconditional throttles as if semantics-free.
- **FlushingEtiquette and custom_quickslots:** historical InputHub conversions replace input wrappers/registrations; current FE has generic fixed-index and custom_quickslots has strict source membership pass. Must preserve exact action consumption, per-device grace, callback ordering, fallback when runtime unavailable, registration cleanup and late FE compile/lifecycle corrections. Avoid duplicate listeners or lost inputs.
- **Framework drift:** the provided history depends on G-REDruntime InputHub/StateCache/Scheduler/UI contracts. Check **current framework owner source/API** rather than replacing framework files with older ZIP content. Source-specific adapters live under their original mod path; G-REDruntime core must not depend on optional mods.

## 2. The seven measured, separately planned owners (post-generic review)

Measured scores below are the owner's supplied **historical instrumented work rates**, **not** predicted savings after the ten generic transformations. Interpret the rate and recorded exclusive time independently; never sum overlapping CET/GRSP values.

| Owner | Historical rate (ms/s) | Generic coverage / legacy overlap | Post-generic semantic decision |
|---|---:|---|---|
| **1 ReinforcementsSystem** | 99.095 | Legacy 4 files/4 passes; generic blackboard, angle-sort and AABB changes in same files | **High upside, high interaction**. Target cheap pure rejection and precomputed geometry only with exact RNG/callback/geometry/3s timing proofs. Delay implementation until source-composition harness is established. |
| **2 Dark Future** | 26.401 | Legacy 1 file across passes 3/5/6/7; generic blackboard elsewhere in owner | UI refresh + indefinite cooldown are real candidates; previous count-only/periodic suppression needs full changing-data and animation analysis. |
| **3 FlushingEtiquette** | 18.325 | Legacy 3 files, passes 2/5/7.2/7.2r1/9; generic fixed-index overlaps FE_Input and player signal elsewhere | Rebase InputHub first, then washroom/visible UI logic with compile correction and full lifecycle proof. |
| **4 Crowd Voice Unlocked** | 10.854 | **No historical optimized file**; generic blackboard statement changes in CrowdLinesIdle | Source `SceneTake`/RecordTakes repeated traversal candidate. **Previously MAYBE, not an approved automatic rule**; require exact scene/lifetime/duplicate ID proof and fresh residual impact before implementation. |
| **5 custom_quickslots** | 11.087 | Legacy single file through 2/3/8; current strict `GRSP-GENERIC:CNAME_ARRAY_MEMBERSHIP_V1` in same file | Device-transition and D-pad caching only after InputHub and grace/held-modifier source proof. No second independent input listener system. |
| **6 MuchBetterContracts** | 10.391 | **No historical optimized file**; generic passes touch 16 of 137 owner files; multiple literal static bool sites | **Reassess first, do not blindly reproduce old cost claim.** `MBCGigSnapshot.TraceEnabled()` in current `Diagnostics/GigSnapshot.reds` is already `true && false` via two `PURE_FINAL_STATIC_BOOL_V1` replacements. The previously measured settings reads may already be eliminated. Re-profile residual actual enabled-path work; honor user-required fully silent default and CET settings behavior. |
| **7 DigitalVixenCore** | 8.139 | Legacy 4 files via passes 4/7/9; generic stable array bound in a different DVCore file | **Best first independent semantic rule:** `DVCore_Footsteps.reds::FindAudioClass` reflection class discovery, with source-proven cache lifetime/invalidations and original lookup fallback. UI dormancy is a later RedWindows-coupled rule, not part of this first change. |

The historical six owner totals and exact functions come from the agreed optimization plan and the user's evidence; **post-generic residual gains have not yet been measured**. In particular, the constant-false MBC trace gate is a strong reason to **deprioritize**, not a claim about measured savings.

**Dark Future — dedicated follow-up, 2026-11-20:** [Workload-first audit and five prioritized semantic candidates](REDSCRIPT-PHASE2-DARK-FUTURE-WORKLOAD.md) documents 22,772 costly outro-start calls, 22,687 infinite-duration timer visits, 38,318 `SetData` calls and ~50 pooled buff widget scans per active frame. Final historical pass 7.0 uses every-ten/every-sixth callback gating and retains a stale reactivation cache risk; do **not** copy it as behavior-preserving. The small isolated **DF-S1 steady infinite-duration update** merits *early* source/animation proof after the initial DVCore generator contract, independently of later Dark Future UI/dormancy work.

## 3. Implementation order to minimize integration problems

1. **First implementation: DVCore Footsteps reflection reuse only.** It is narrow, isolated from the generic-touched DVCore source, has a concrete repeated lookup target, and exercises the full **semantic source-admission → generator → partial/idempotence → owner ZIP → SCC CI** contract without entangling InputHub/geometry/quest handling. Add an adapter if REDscript API and lifetime permit; if the expensive private method cannot be safely intercepted, minimally transform its original source at its existing path. Do not make cached missing RTTI permanent and do not retain stale handles across load/restore.
2. **Early Dark Future DF-S1 infinite-duration source proof.** The current DF UI hot file has no generic marker conflicts; its negative-one timer sentinel and repeated costly outro calls make an isolated semantic no-op candidate. Validate correct animation semantics and invalidate on `ActivateCooldown` / pool reuse **before implementing**. A failed proof must skip DF-S1 without blocking other owners. [Dedicated audit](REDSCRIPT-PHASE2-DARK-FUTURE-WORKLOAD.md).
3. **Source-safe foundations from historical passes 2.0/2.1:** build the source-specific InputHub adoption family as independently opt-in per installed owner, not a hardcoded mod catalog or a generic rewrite by name. Audit current InputHub ownership, game attach/detach, no-hub fallback, action consumption and duplicate registrations first. Apply any existing generic benefits **before** the semantic rule. Then integrate FE / custom_quickslots as separate semantic remainder policies.
4. **Historical pass 2.2 and independent low-conflict owners:** rebase scheduler/context/state adapters only with proven cadence, cleanup, source ownership and no duplicated producer. Port independent owner changes from passes 3/4/5 where there is a simple equivalent boundary, with one current-source admission contract each. Do not treat one pass ZIP as an atomic rule.
5. **UI framework and dependent output:** reconcile RedWindows and its `IsRendered`/visibility behavior before DVCore ItemEditor/StatusWindow/BodyMap, FE UI and other pass 8/9 dormancy. Source-change wake, lifecycle and item/timer mutations must be complete. Port 6/7 pacing only when preserved latency/animation is proven; include historical 7.1, 7.2 and 7.2r1 fixes in the originating semantic rules, not corrective passes.
6. **Remaining Dark Future work after full-tuple UI/invalidation proof.** Explore empty buff-pool slots (DF-S2), exact `SetData` no-ops (DF-S3), finite label formatting (DF-S4, research-only) and true effect-list invalidation (DF-S5). Do not add arbitrary every-N frame skips. Re-measure residual work after DF-S1.
7. **ReinforcementsSystem after generic geometry integration and dependency proof.** Highest measured priority, but greatest regression/overlap risk. Review `Utils`, `ReinforcementSystem`, `ReinforcementsCall`, `TickSystem` together. Separate source-safe cheap guard from risky RNG reorder, event elimination and time slicing; the latter require explicit feature-by-feature approval.
8. **Residual-only review:** MuchBetterContracts after actual generic-post capture and source proof; Crowd Voice Unlocked only if the user authorizes the previously provisional scene index and source proof supports it. Other present owners absent from historical portfolio are newly discovered work, not automatically eligible semantic recipes.

**Rule naming/ownership before coding:** record stable per-semantic-operation IDs, source-specific structural guards and required framework capability; the rule may match more than one version/source shape *only when equivalent behavior is proven*. Do not embed an unconditional list of 31 owners or historical byte hashes into production admission. A pass history is a specification/fixture, not executable patch data.

## 4. Required acceptance contracts and unresolved decisions

- **Existing + new files:** default to no change or a new per-mod adapter under the original owner. Use original-path **minimal differential** transformation only for private implementation, callbacks or mutations that cannot be intercepted safely. Framework core remains owned by G-REDruntime; no shadow mod copies.
- **Current versions:** discover the actual effective installed source on every run. Never install absent Exodus/Repeatable/Street Vendors or emit optional owner helpers without current owner/dependency proof. **Do not reject new versions or custom changes merely because a version, signature layout, source hash, line position, unrelated code or formatting differs.** Recognize equivalent functions and essential dataflow/behavior structurally and apply the optimization while preserving user edits. Only a genuinely incompatible/missing required operation—or a critical semantic property that cannot be proven—causes a **narrow affected-operation refusal** with a precise diagnostic; independent compatible work continues.
- **Source states / one-pass completeness:** the **post-generic stock** state must generate **every applicable approved semantic optimization in one invocation**, without requiring historical separate passes. Partially optimized (including markerless equivalent code) must be recognized structurally and completed with **missing work only**; fully optimized is no-op. Preserve current generic transforms and unrelated custom edits, avoid duplicate adapters/registrations, and refuse only truly incompatible or unproven **target operations**, not an otherwise compatible whole mod. Concurrent live-source changes after analysis still require fresh proof or safe stale-input refusal.
- **No gameplay changes:** callback arguments/return/consumption, action and quest side effects, randomness, ordering, timers, status timers, time-varying UI, lifecycle, reload, failure fallback, mod dependencies, compatibility of saved games and any cleanup must be preserved.
- **Test real output:** compile the effective baseline + all ten generics + every accepted semantic overlay together in GCO official SCC **CI**; validate source owner, generated paths, missing-mod skip, preexisting partials, idempotence, source drift and combined CET+REDscript ZIP safety. **Do not bundle SCC or invoke it on the player's PC**, per current GCO ownership decision. CI green is not proof of gameplay.
- **Measure:** compare same-scenario captures after generic and after semantic, using independent GRSP and CET observations and PresentMon frame metrics; do not claim historical optimization numbers as current gains. Gameplay testing should include save/load, combat, UI/menu, input device switching, fast travel, absence of G-REDruntime/feature disabled, removal of one overlay and Restore behavior.
- **Decisions reserved for user discussion:** (a) acceptable behavior change versus strictly behavior-identical transformations (especially RNG, phase scheduling and UI throttles); (b) policy for mods whose legacy source differs; (c) whether Crowd Voice scene indexing joins the approved scope; (d) owner-specific semantic rule naming/admission/fallbacks; (e) which legacy owners are worth maintaining after measurement.

**Next checkpoint:** Implement semantic rules **only** under the [mandatory agent policy](IMPORTANT-FOR-AGENTS-RED-OPTIMIZATIONS.md), with complete one-go stock generation and version/custom edit compatibility. Discuss individual semantic behavior choices separately. **No semantic rule has been implemented in this planning milestone.** The isolated DVCore Footsteps rule remains the recommended first implementation, not a wholesale legacy ZIP import.
