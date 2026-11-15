# GRSP-owned REDscript Resolver / Generator — implementation blueprint

**Date:** 2026-11-15  
**Status:** design and source audit only; no repository changes, generated REDscript, compiler execution, or gameplay test performed in this review.

## 1. Fixed architecture and ownership

| Owner | Authoritative responsibility |
| --- | --- |
| G-CET | Existing Lua profiling, source-owned Resolver/Generator, native integration and G-GC. Freeze its four-stage GCO integration. |
| GRSP (`dev/resolver-profiler-handoff`) | Native REDscript instrumentation, complete REDscript evidence contract, **new REDscript Resolver, generator, and full-stack compiler validation**. Preserve standalone GRSP 1.0.0 stable release and original install/restore managers. |
| G-REDruntime (`main`) | Optional source-owned REDscript shared caching, scheduling, input/event and framework services. Expand only in response to measured, proof-backed generic demand. |
| GCO | Existing COLLECT, independent genuine PresentMon-backed consolidated reports, cross-domain planning, invocation of *both* original owner Resolvers, compatibility checks and **one final game-relative ZIP**. No REDscript rewrite engine in GCO. |

**Workflow:** `COLLECT` seals actual CET + GRSP + PresentMon → independent GCO profiler report → GCO `ANALYZE` invokes source-owned CET and GRSP Resolver JSON using the same sealed evidence and *current live source* → `GENERATE` calls both source-owned generators → GCO validates/merges into one clean ZIP. Existing `CONTINUE` still performs profiler-owner restoration. Neither reporting nor ANALYZE adds a second restoration/lifecycle gate. No extra page/UI, second .NET runtime or CapFrameX imitation.

**Frame evidence:** Use GCO's existing versioned PresentMon 2.6 QPC/Unix chronology and F11 overlap as native REDscript Resolver evidence. Model missing data as `NOT_CAPTURED`, unknown overlap as unknown and optional CPU/GPU metrics as null. Never fake CapFrameX JSON, infer PC latency/FG, add CET+GRSP workload as total CPU, or pretend a hot function caused a particular render hitch.

## 2. Input audit: the user's staged hand-optimized overlays

Read-only ZIPs examined:

- `G-REDruntime_Optimizations_Partial.zip`: **61 `.reds` files in 30 source-owner groups**, under `r6/scripts/...`. No framework core files and no `scc.exe` are bundled. The files are user-supplied previously hand-optimized and gameplay-verified reference outputs, **not an automatically safe universal mod patch catalog**.
- `r6LIVEnow.zip`: **2,280 `.reds` files** under `scripts/...` (equivalent to game `r6/scripts/...`), including **10 `G-RedRuntime` core files**; no REDscript compiler binary. Treat this as a dated local comparison snapshot, not the authoritative installed sources at generation time.
- Of 61 overlay paths, **56 match** snapshot paths; **55 have different bytes** and **one is identical** (`negotiableaffection/Main/NA_Interaction.reds`). **Five have no matching path** in the snapshot: `Exodus - Arasaka Training/ExodusCombat.reds`, `Exodus - Arasaka Training/ExodusSamurai.reds`, `Repeatable Cyberpsycho Encounters/RepeatableCyberpsychos.reds`, `ImmersiveCyberware/GRedHotpathCache.reds`, and `zzy_bChipwareExpansion/chipsScripts/GRedHotpathCache.reds`. The latter two are *added source helpers* rather than missing source to modify.
- **23 files** introduce a `GRedRuntime.*` import relative to the comparison snapshot. Others optimize locally without adding that dependency. **Explicit historical comments identify Pass 5, Pass 7, Pass 8 and Pass 9** in particular files, but the ZIP contains **no complete sequential patch manifest**: do not reconstruct unknown earlier stages by guesswork.
- Comparison *does not* prove the snapshot's matching files are untouched original versions. Source changes, prior optimization, versions and deployment may differ. The approved overlay is reference evidence; current installed source and compilation are authoritative for future output.

### Reusable patterns visible in actual diffs

| Pattern | Concrete evidence from partial archive | Generic prospect and proof boundary |
| --- | --- | --- |
| Repeated stable getter and expensive lookup | `BrowserExtension.System` journal/quest handle cache; `DVCore_Footsteps` reflection-class cache; `ArrivalPhantomFix` mappin-system cache | Strong first candidate: scope-local value reuse when callee is pure/stable and lifetime is proven. Persistent cache only with invalidation/lifetime proof. |
| Cheap guard before expensive work | `NightCityRemembers/NCR_SpawnCulling` crowd/quest/companion exclusions; `StealthRunner/npc_puppet` quest/player short-circuit; `ReinforcementsCall` stimulation guard | Candidate after proving no skipped side effect, original ordering, native wrapper result and fallback preservation. |
| Avoid redundant UI/state mutation | `RedWindows/RW_Window` identical color/opacity skip; `ImmersiveCyberware/handCyberware` scale-on-change; `ArrivalPhantomFix` visibility-on-change; `TriggerModeControl` hint gating | Candidate when setter really is idempotent, initialization/recreation is handled and observer/notification semantics are unchanged. |
| Cheap precomputation / reuse | `TriggerModeControl` static `CName` tags; `ReinforcementsSystem/Utils` prepared geometry; `HQ_HoloGirl` resource key | Often local and portable, but verify value scope and game/locale/resource changes. |
| Stateful short-lived hot-path cache | `ImmersiveCyberware/GRedHotpathCache.reds`; `zzy_bChipwareExpansion/.../GRedHotpathCache.reds`; `RunningMan` perk checks | Generic *mechanism* may be reusable; exact stats, invalidation and gameplay choices are approved stack-specific semantics. |
| Shared input dispatch | `BrowserExtension`, `CyberwareEx`, `DrugDealer`, `FlushingEtiquette`, `StealthRunner`, `Vehicle Call Plus`, `custom_quickslots` call `InputHub` | A generic capability only after input-consumption order, action types, registration and player detach/re-attach are fully proven. No blanket wrapper replacement. |
| Scheduled work and demand dormancy | `VehicleMileage` four UI files, `ShatteredChrome`, `GearRecoverySystem`, `NightCityRemembers`, `ReinforcementsSystem`; `DigitalVixenCore` overlay-only slower polling (Pass 9) | Requires original frequency, callback order, latency, idle/wake trigger, cancellation and cleanup proof. **Not a blind generic throttle.** |
| Source/feature-specific transformations | `LizziesBDs` NPC dispatch, `FlushingEtiquette` mappin ownership, `VirtualAtelierDelivery` spawn batching, `ChipwareExpansion` perk mapping, `Reinforcements` phase splitting | Preserve as **finite approved semantic recipes**, only when the live source structure and optional dependencies match. Do not build a broad semantic library. |

### Historical pass markers recoverable from the files

The archive shows these **explicitly commented** previous hand-optimization stages. This is a partial reconstruction, not a complete chronology of every edit:

| Tagged historical pass | Examples | Transfer rule |
| --- | --- | --- |
| **Pass 5** | `TriggerModeControl` suppresses repeated hint writes while retaining native callback cadence | Extract safe repeated-state/write suppression; keep weapon-mode behavior as approved semantics |
| **Pass 7** | `NightCityRemembers/NCR_SpawnCulling`, `LizziesBDs/NPCPuppet`, `ReinforcementsCall`, `StealthRunner/npc_puppet` front-load exclusion/guard logic | Generalize only proven cheap guard ordering; NPC classification and gameplay exclusions remain approved source-aware semantics |
| **Pass 8** | `ImmersiveCyberware` and `ChipwareExpansion` added hot-path caches, `RedWindows` retained-widget lookup, `RunningMan` state/perk caching, `custom_quickslots` input changes | Extract generic caching/change-detection mechanics, but require exact invalidation, ownership and input lifecycle for each adopted site |
| **Pass 9** | `DigitalVixenCore` overlay-only callbacks, `FlushingEtiquette` minimap wrapper removal, `RedWindows` render-presence checks | Treat inactivity gates and callback removal as feature-specific until wake/cleanup and UI behavior are proven |

Earlier passes and untagged edits cannot be assigned to a stage reliably from this final ZIP alone. Never use a `Pass N` comment as a structural admissibility condition.

## Mandatory development ordering — **generic coverage before semantic remainder** (2026-11-15 decision)

**Do not port the 61 hand-optimized files as 61 semantic passes first.** The partial ZIP is a finite, already gameplay-tested *reference and eventual coverage oracle*, not a prescribed code-generation queue or measurement substitute. Enforce this order:

1. **Capture and prove:** collect **fresh** CET + GRSP + genuine PresentMon on the user's current mod stack, validate GRSP quality/attribution and live source inventory; optionally index the 61 references privately and read-only for later comparison, but **do not implement semantics from that inventory**.
2. **Discover generic opportunities across the entire live REDscript stack:** rank measured recurring operations and exact structural shapes. Treat CET generic mechanisms as hypotheses only; REDscript compiler/side-effect/lifecycle rules determine feasibility. Candidate priority comes from observed value, not old portfolio owners or a fixed G0/G1 rule order.
3. **Implement and validate broad generic passes first:** add GRSP instrumentation or extend optional G-REDruntime services **at this point, even before a pass ships**, if a specific reusable high-impact opportunity needs them and their behavior/overhead can be proven. Compile actual REDscript output, test untouched/partially/already optimized inputs and idempotence, and compare performance with fresh captures where practicable. Do not claim savings without a comparable measurement.
4. **Only AFTER the worthwhile generics have been applied and validated, run a portfolio gap analysis:** compare each of the 61 approved files' *behavioral transformation patterns* against the actual generic-generated output, pre-existing live optimizations and source state. Record per item/operation: `GENERIC_COVERED`, `ALREADY_PRESENT`, `SEMANTIC_REMAINDER`, `UNPROVEN_OR_DRIFTED`, or `NOT_APPLICABLE`, with structural and dependency evidence. A file may span multiple statuses. No item is automatically a recipe.
5. **Implement only proven `SEMANTIC_REMAINDER` work**, limited to this explicitly approved portfolio; skip the rest with reasons. Generate missing work only, never copy historical whole-file output or stack duplicate generic and semantic versions of the same transform. Compile/validate the composed effective REDscript stack and test gameplay independently.

**Hard gate R7:** No mod-specific semantic implementation is admitted before R5/R6 generic coverage is established and a documented 61-file gap matrix demonstrates the residual behavior. R1 is metadata/reference preparation only, not a first semantic pass. The goal is useful measured improvements and preserved gameplay, **not** maximizing transformed file counts or forcing all 61 files to change.

## 3. Implementation sequence with exit criteria

### Stage R0 — record new owner contract and protect existing baselines

- Update GRSP `docs/RESOLVER_HANDOFF.md`, relevant GRSP handoff and GCO architecture/decisions: GRSP owns REDscript Resolver, generator and compilation; GCO invokes and combines. Record approval and exact baseline commits. Never silently promote the CET feature branch or touch GCO `main`.
- Explicitly freeze current GCO PresentMon reporting / CET source-owner generation; retain one canonical Windows CI, one .NET10 runtime, GRSP native install/restore. New GRSP code lives on `dev/resolver-profiler-handoff`.
- **Exit:** both handoffs agree on owner CLI/data contracts; no source rewriting/UI changes.

### Stage R1 — privately index the 61-file reference (NO semantic implementation)

- Index all 61 approved optimized source files and 30 owners by their game-relative paths. Store source provenance and accepted feature behavior as **test evidence**, not release/payload mod inventories or hardcoded game source hashes.
- Record file paths, owners, known historical pass tags, helper additions, known existing dependencies and whether comparison source matches. Mark unmatched/ambiguous source as `UNPROVEN`. **Defer rule-specific structural predicates, transformation choices and semantic recipe implementation to R7, after the generic coverage gap analysis.**
- Preserve the user's reference ZIP unchanged. Keep third-party original source fixtures and generated test passes **private**; do not embed or upload them to public GitHub Actions artifacts.
- **Exit:** all 61 paths privately indexed with provenance or `UNPROVEN`, including added helpers and known identical files. **Zero semantic passes implemented** and no historical whole-file copying.

### Stage R2 — acquire **fresh** full three-profiler capture evidence

- Use GCO's existing completed F11 capture flow and real installed stack: CET+GRSP+PresentMon. Representative idle/world/vehicle/combat/UI scenarios as actually relevant; capture provenance, native GRSP quality/drop diagnostics and aligned PresentMon QPC intervals.
- Consume **complete** GRSP `Data/Runtime/GRSP_{ByMod,ByFunction,Frames,Timeline,Spikes,Markers}.csv` plus **complete** developer `RSP_{FunctionMap,CallSites,SharedTargets,Cadence,WrapperChains,WorkMap}.csv`. `GRSP_Summary.json` is compact and insufficient as sole source for ranking.
- Verify QPC/F11 quality, actual observed scope, per-owner source identity, dropped events, unresolved calls, nested/wrapper attribution. Rank by repeated measured work, call volume, duration and scenario/hitch overlap. **No predetermined priority mod list, no guessed performance savings.**
- **Exit:** ranked, provenance-rich native candidate data; loss/uncertainty explicitly represented; live installed mod inventory verified separately.

### Stage R3 — build source-owned, machine-readable REDscript Resolver evidence

- Create a GRSP-owned, versioned `GRSP_Resolver_Input.json` (name/version final at source contract), generated from native raw evidence and GCO's **actual** PresentMon chronology, independent of any HTML report or visual chart.
- Include original file/callsite IDs, owner attribution, complete cadence/frame workload, source path and line, observation/quality status, measured overlaps and uncertainty. `frameEvidence.provider=PRESENTMON_2_6` if truly available; otherwise `NOT_CAPTURED`. Accept no legacy CapFrameX fallback inside GCO.
- Prove exact indexing and chronological alignment from the sealed input, no mutation, no loss of rows, no sum of overlapping CET/GRSP CPU. For GRSP standalone use, do not break existing optional CapFrameX report support.
- **Exit:** stable owner headless JSON schema, rigorous negative tests and real-capture replay; no source edits yet.

### Stage R4 — establish a structural, source-aware `.reds` admission layer

- Discover currently effective `r6/scripts` dynamically, including root-level and foldered mods, import/module relationships, wrapper chains, `@addMethod`, `@addField` and conditional `@if(ModuleExists(...))` guards. A capture from another stack is not a mod inventory.
- Use a validated REDscript-capable parser or a conservative source-syntax tree with token/span fidelity and compiler-checked transformation. **No formatting-regex-only admission** and no generated-output repair pass. Identify receiver, call side effects, scope, liveness, signatures, compile-time optional types and source drift.
- Build exact source-state classification: `PRISTINE_APPLICABLE`, `PARTIALLY_APPLICABLE`, `ALREADY_COMPLETE`, `UNSAFE_OR_UNPROVEN`, `DRIFTED`, `MISSING_DEPENDENCY` with the proof that established the state. Never rely on fixed source file hashes as version locks; hash only the *current* live inventory for invocation consistency.
- **Exit:** positive/negative structural proofs against the real partial ZIP and original snapshot, plus current-source drift/repeated-pass tests; still no on-disk game changes.

### Stage R5 — discover, implement and verify **high-value generic coverage FIRST**

- Rank **all currently installed REDscript source candidates** using fresh GRSP/PresentMon measurements and structural proof before selecting any generic pass. The following G0–G4 are hypotheses, **not a fixed priority sequence**. Do not use the 61-file portfolio as a candidate whitelist.
- **G0:** scope-local reuse of duplicate proven-pure/stable provider reads; refuse if side effects, branches, receiver/lifetime or invalidation are ambiguous.
- **G1:** semantic-preserving cheap guard ordering and unnecessary repeated lookup elimination; preserve exact call effects and early-return semantics.
- **G2:** conditional no-op setter / unchanged retained-widget update skipping, only with proof that setter is idempotent and notifications are not significant.
- After actual measured wins, consider **G3** source-proven short-lived cache/invalidation and **G4** event-driven/demand dormancy, InputHub/Scheduler consolidation. These are *mechanisms*, not universal auto-throttling rules.
- Each generic candidate must emit a measured justification or `SOURCE_FIRST_UNMEASURED`, application structural proof and reason for exclusion. Generate missing work only. Target real recurring work, not count of modified lines.
- **Exit per pass:** representative actual output compiles, behavior/lifetime invariants tested, supported partial/optimized states proven, second run emits zero transforms. Benchmark gain requires fresh after-capture, not resolver-estimated percentages. **Proceed to R7 only after recording applied generic coverage and why remaining generic candidates are unsupported, too low value, or deferred.**

### Stage R6 — iteratively expand GRSP/G-REDruntime when a **generic** opportunity requires it (may precede an R5 pass)

- If a desired transform lacks reliable observations, extend the **existing GRSP writer** with the narrowest bounded measurement: stable dynamic callsite shape, actual framework job runs/wake/stop, per-owner costs, lost-event counts or proven eligible allocation boundary. Measure capture overhead and preserve standalone native contracts. No CET LuaJIT allocation/GC hooks copied to REDscript.
- If independent mods exhibit the *same* safe reusable need, design narrow framework API additions (session-safe lookup / explicit dirty versions / actual per-job scheduling / dispatcher tracking) in **G-REDruntime source**. Must remain dormant with no consumers and never create optional-mod compile dependencies in the framework core.
- No framework requirement for G0/G1/G2 if normal REDscript can implement them. No invented framework API or blanket conversion to `InputHub`/`Scheduler` without original input/cadence/cleanup proof.
- **Exit:** owner-native tests/overhead evidence, no default behavior regression, and an accepted generic pass that materially uses the added capability. R6 may interleave with R2–R5 to make a measurable generic pass possible; it is **not** held until after all R5 passes or used to justify semantic rules in advance.

### Stage R7 — first prove the **61-file generic-coverage gap**, then implement only semantic remainder

- **Prerequisite:** validated R5 generic pass results on current sources (with R6 where justified). Reassess all 61 reference files **after applying generic output**, at individual transformation/behavior level, not by filename or byte comparison alone. Produce a coverage matrix with `GENERIC_COVERED`, `ALREADY_PRESENT`, `SEMANTIC_REMAINDER`, `UNPROVEN_OR_DRIFTED`, `NOT_APPLICABLE` and supporting structural/dependency proof. Recalculate after generic-rule changes.
- Only the verified `SEMANTIC_REMAINDER` may become small **source-structural recipes and feature regression fixtures**, never whole-file copies or 61 obligatory patches. Generic patterns belong to R5. The rest is the *only current approved mod-specific semantic set*.
- Reapply a semantic recipe **only** when the live source's actual operations, callback graph, dependencies and expected original state are proven. If an edited mod version differs, fail that recipe closed and continue unrelated safe passes. Preserve existing user optimizations; recognize already-done states; never duplicate helper classes/fields/subscriptions.
- Special attention: `ChipwareExpansion` and `ImmersiveCyberware` added helper dependencies; `InputHub` migrations require detach and input-consumption parity; `Scheduler` migrations require exact wake/pause and cancellation; `DigitalVixenCore` UI sleep requires open/close invalidation; `FlushingEtiquette` mappins and `VirtualAtelierDelivery` spawn batching require ownership/cleanup proof.
- **Exit:** per-operation coverage matrix completed and every residual approved semantic either structural match+compiles+regressions or has an explicit skip reason; no duplicate generics, unsupported catalogs or mod-version locks. **Zero residual semantics is a valid successful outcome.**

### Stage R8 — full-stack compile, dependencies and source-state proof

- Assemble the **entire current effective** REDscript stack plus the proposed changed/new files in a disposable mirror; verify all optional modules and the deployed G-REDruntime API version. Locate/use the authoritative supported REDscript compiler (baseline user stack `0.5.31`) with actual compile logs; neither supplied ZIP contains `scc.exe`.
- Run pristine → first generation → partial input → remaining work → fully overlaid second run (zero transforms) on real source states; structural diff, deterministic output, no unrelated changes, negative drift/missing-dependency/compile/error tests.
- Preserve file and mod ownership, callback/wrapper ordering, full signatures, lifecycle, unload/reload, game state and fallback semantics. Reject *only* the unsafe owner/pass where possible; never publish an uncompiled or partially invalid overlay as safe.
- **Exit:** fully compiled, changed-only, game-relative GRSP-owned `r6/scripts/...` overlay ZIP with sidecar source/dependency/exclusion proof **outside** the ZIP. Gameplay remains independently unproven until tested.

### Stage R9 — GRSP-owned headless integration into GCO

- Provide GRSP-owned CLI commands conceptually equivalent to `--analyze --capture <verified-GRSP> --game <root> --frame-evidence <GCO-PresentMon-Frames> --json` and `--generate-pass --pass-output <zip> --json` (freeze exact switches/schema only after first tested contract).
- GCO consumes owner JSON and generation ZIP **without** implementing a parallel REDscript Resolver or modifying GRSP's native raw measurements. Package the original owner source on the same shared .NET 10 runtime and use the one existing Windows CI workflow, with real source-replay gated to milestones as with CET.
- **Exit:** independent REDscript owner parity, live-source read-only analysis, real generated ZIP/compile, bounded failure, repeatable source-state CI; GCO Capture/Restore/CET unaffected.

### Stage R10 — combine CET and REDscript outputs and validate in game

- GCO merges source-owned CET and GRSP clean contributions into **one** game-root-relative ZIP: `bin/...` and `r6/scripts/...` plus optional proven shared files. Reject collisions, unsafe dependencies and original-source drift; put manifests, reports, profiler binaries, logs, compiler artifacts and PDBs **outside** the release ZIP.
- Validate Vortex overlay/rollback ownership. Run real Cyberpunk loading, representative saved games, relevant mod features, F11 before/after performance and instability regression checks. Separately report script-domain activity and PresentMon frame intervals; never claim additive CPU or fabricated gain.
- **Exit:** original 2-page workflow, verified one ZIP, verified full-stack REDscript compile, reproduced gameplay validation and measured outcomes. Do not promote to `main` or release on green CI alone.

## 4. Acceptance and stop conditions

**Must pass:** actual data ingestion, exact owner/callsite evidence, real current source, absent/partial/already-complete state, idempotence, full-stack compiler, dependency checks, drift refusal, no live mod writes, unchanged source-owner lifecycle, genuine PresentMon, clean ZIP, gameplay acceptance and credible before/after data.

**Must refuse/skip:** unresolved source mapping, dynamic or non-pure getter, uncertain callback timing or input consumption, missing optional dependency, duplicated patch from earlier stage, unverifiable compiler coverage, framework API mismatch, unrelated source drift, stale or low-quality captures presented as guaranteed CPU wins.

**Next actionable milestone:** R1 remains read-only portfolio indexing, while R2–R4 establish fresh measurement and structural source proof. Select and validate the most valuable **generic** opportunities first (not necessarily G0/G1); evolve GRSP/G-REDruntime where justified. Only **after** generic output exists perform the 61-file coverage-gap analysis and implement its proven semantic remainder, if any. CET and GCO consolidated reporting are frozen baselines, not redevelopment targets.

## Appendix A — partial ZIP file inventory

The table below is a **path and source-state audit**, not a claim that every reference patch remains applicable. `Different` means only that the file differs from the comparison ZIP; it does not prove either side is unoptimized. Rows with `No match` require separate original-source evidence or are intentionally added helpers. No actual compiler or game test was run during this review.

| Approved reference path (game-relative) | Snapshot comparison | Initial pattern group |
| --- | --- | --- |
| `scripts/BrowserExtension/browserController.overrides.reds` | Different | Input dispatch; journal cache |
| `scripts/BrowserExtension/BrowserExtension.System.reds` | Different | Input dispatch; journal cache |
| `scripts/BrowserExtension/HomePagePagination.reds` | Different | Input dispatch; journal cache |
| `scripts/custom_quickslots/player_puppet.reds` | Different | Input dispatch; hot-state gate |
| `scripts/CyberwareEx/CyberwareEx.Global.reds` | Different | Input dispatch; state lookup |
| `scripts/Dark Future/UI/DFBaseGameUIOverrides.reds` | Different | UI work gate |
| `scripts/DigitalVixenCore/DVCore_BodyMapWindow.reds` | Different | Overlay dormancy; reflection cache |
| `scripts/DigitalVixenCore/DVCore_Footsteps.reds` | Different | Overlay dormancy; reflection cache |
| `scripts/DigitalVixenCore/DVCore_ItemEditor.reds` | Different | Overlay dormancy; reflection cache |
| `scripts/DigitalVixenCore/DVCore_StatusWindow.reds` | Different | Overlay dormancy; reflection cache |
| `scripts/DisablePositionReveal/DisablePositionReveal.reds` | Different | Redundant state-write guard |
| `scripts/DrugDealer/Crackhouse/Crackhouse.reds` | Different | Input dispatch |
| `scripts/DrugDealer/Job/SellDrugs/Interaction.reds` | Different | Input dispatch |
| `scripts/DrugDealer/Lab/Lab.reds` | Different | Input dispatch |
| `scripts/DrugDealer/NightMarket/NightMarket.reds` | Different | Input dispatch |
| `scripts/DrugDealer/Operation/OperationProximity.reds` | Different | Input dispatch |
| `scripts/Enhanced Vehicle System/Enhanced Vehicle System.reds` | Different | Duplicate map lookup; component lifecycle |
| `scripts/Exodus - Arasaka Training/ExodusCombat.reds` | No match | Input/state integration (unmatched) |
| `scripts/Exodus - Arasaka Training/ExodusSamurai.reds` | No match | Input/state integration (unmatched) |
| `scripts/FlushingEtiquette/FE_Input.reds` | Different | Input dispatch; owned mappin lifetime |
| `scripts/FlushingEtiquette/FE_Places.reds` | Different | Input dispatch; owned mappin lifetime |
| `scripts/FlushingEtiquette/FlushingEtiquette.reds` | Different | Input dispatch; owned mappin lifetime |
| `scripts/GearRecoverySystem/25_GRSInteractionController.reds` | Different | Shared scheduler and cleanup |
| `scripts/HQ_HoloGirl/Engine/HoloGirl.reds` | Different | Material lookup/precompute |
| `scripts/HQ_HoloTravel/HQ_HoloTravel.reds` | Different | Processing-window gating |
| `scripts/ImmersiveCyberware/GRedHotpathCache.reds` | No match | HUD change guard; added hotpath cache |
| `scripts/ImmersiveCyberware/handCyberware.reds` | Different | HUD change guard; added hotpath cache |
| `scripts/ImmersiveCyberware/lenses/FocusScanner.reds` | Different | HUD change guard; added hotpath cache |
| `scripts/LizziesBDs/NPCPuppet.reds` | Different | NPC hook dispatch |
| `scripts/MapWaypointBugFixes/ArrivalPhantomFix.reds` | Different | System cache; redundant visibility |
| `scripts/negotiableaffection/Main/NA_Interaction.reds` | Identical | Already identical in snapshot |
| `scripts/NightCityRemembers/NCR.reds` | Different | Scheduler; early NPC exclusion |
| `scripts/NightCityRemembers/NCR_SpawnCulling.reds` | Different | Scheduler; early NPC exclusion |
| `scripts/Quickhack_Fixes/sonic_shock.reds` | Different | Direct existence predicate |
| `scripts/RedWindows/RW_Builder.reds` | Different | UI widget lookup/cache/no-op writes |
| `scripts/RedWindows/RW_Logs.reds` | Different | UI widget lookup/cache/no-op writes |
| `scripts/RedWindows/RW_Widgets.reds` | Different | UI widget lookup/cache/no-op writes |
| `scripts/RedWindows/RW_Window.reds` | Different | UI widget lookup/cache/no-op writes |
| `scripts/ReinforcementsSystem/ReinforcementsCall.reds` | Different | Scheduler; guards; geometry precompute |
| `scripts/ReinforcementsSystem/ReinforcementSystem.reds` | Different | Scheduler; guards; geometry precompute |
| `scripts/ReinforcementsSystem/TickSystem.reds` | Different | Scheduler; guards; geometry precompute |
| `scripts/ReinforcementsSystem/Utils.reds` | Different | Scheduler; guards; geometry precompute |
| `scripts/Repeatable Cyberpsycho Encounters/RepeatableCyberpsychos.reds` | No match | Unmatched reference |
| `scripts/RunningMan/double_sprint.reds` | Different | Perk lookup at transition |
| `scripts/RunningMan/sprint_direction.reds` | Different | Perk lookup at transition |
| `scripts/ShatteredChrome/SC.reds` | Different | Scheduler; retained HUD state |
| `scripts/ShatteredChrome/SC_Callbacks.reds` | Different | Scheduler; retained HUD state |
| `scripts/StealthRunner/npc_puppet.reds` | Different | NPC guard; input routing |
| `scripts/StealthRunner/Pickpocket/Choice.reds` | Different | NPC guard; input routing |
| `scripts/TriggerModeControl/TriggerModeControl.reds` | Different | Constant tags; hint update gating |
| `scripts/Vehicle Call Plus/VehicleCall_FastTravel.reds` | Different | Input dispatch |
| `scripts/VehicleMileage/UI/VM3DOdoHUD.reds` | Different | Scheduled UI polling |
| `scripts/VehicleMileage/UI/VMFuelAvailable.reds` | Different | Scheduled UI polling |
| `scripts/VehicleMileage/UI/VMFuelGauge.reds` | Different | Scheduled UI polling |
| `scripts/VehicleMileage/UI/VMPriceSign.reds` | Different | Scheduled UI polling |
| `scripts/VirtualAtelierDelivery/VirtualAtelierDelivery.reds` | Different | Spawn batching |
| `scripts/zzy_bChipwareExpansion/chipsScripts/chipPerkUnlock.reds` | Different | Perk/cache helper; guard ordering |
| `scripts/zzy_bChipwareExpansion/chipsScripts/GRedHotpathCache.reds` | No match | Perk/cache helper; guard ordering |
| `scripts/zzy_bChipwareExpansion/chipsScripts/reflexChipsScripts/flashAndThunderclap.reds` | Different | Perk/cache helper; guard ordering |
| `scripts/zzy_bChipwareExpansion/chipsScripts/reflexChipsScripts/wreckingBall.reds` | Different | Perk/cache helper; guard ordering |
| `scripts/zzy_bChipwareExpansion/chipsScripts/softwareChipsScripts/kerenzikovUpdate.reds` | Different | Perk/cache helper; guard ordering |

## 2026-11-15 — native player and NEAR/FAR planned dependency

The existing CET Resolver-generated native player/state and location cache is a known **planned** combined-ZIP dependency and may be considered during generic opportunity discovery even if absent from unoptimized game source. See [CET native player and near/far contract](GRSP-NATIVE-PLAYER-PROXIMITY.md). Final deployment still requires verified CET owner output and REDscript-visible ABI, and must fail open on UNKNOWN. FE fixed point checks are candidate generic targets; FE minimap wrappedMethod is a different unresolved hotspot. R7 semantic-only work remains last.
