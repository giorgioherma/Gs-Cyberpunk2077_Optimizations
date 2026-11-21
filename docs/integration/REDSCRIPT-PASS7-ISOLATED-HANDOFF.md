# REDscript historical Pass 7 — isolated production one-pass branch

**Owner:** Agent 5, historical Pass 7. **Branch:** `dev/redscript/pass7`.
**Base:** verified `96d66c66642ac52dd13fd4001416572b9169bace` (do not merge unfinished handoff/Pass4/Pass5 work).
**Scope:** isolated GCO-owned source-aware REDscript generation, source-state tests and branch-scoped official SCC. The app's existing two-page workflow, CET and GRSP profiler owners, and shared G-REDruntime services remain untouched.


## 2026-11-21 — Complete remaining historical Pass 7 owner review (production branch-local)

**Branch-only source integration:** `dev/redscript/pass7`, never merged into `main` or another active agent's branch. Canonical `RedscriptGenerationPlan.Stage` now records **11 independently scoped Pass 7 operations** in provisional #700–710; 10 expected compatible operations, one deliberately refused unsafe historical perk-result TTL. The current CI run is the explicitly marker-triggered [focused Pass 7 #12](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/44908307127); confirm its conclusion before treating this checkpoint as validated.

| Stage | Owner and operation | Branch-local disposition |
| --- | --- | --- |
| 700 | NightCityRemembers spawn guards | Existing source-aware port; no change |
| 701 | StealthRunner quest detection | Existing early quest-negative gate; no change |
| 702 | StealthRunner weak player handle | Existing cache/reattach/detach/fallback; **same field/behavior as Pass 4**, must reconcile on merge |
| 703 | RunningMan sprint-entry perk | Existing conditional entry cache |
| 704 | RunningMan direction perk | Existing optional module live fallback |
| 705 | LizziesBDs performer attach | Existing fast non-performer gate |
| 706 | LizziesBDs performer index | **New** per-NPC identity-to-index memo in existing `LizziesBDsIsEnt()`; every invocation reads the **current** `GetRecordID()`, returns cache only when record matches, otherwise runs the original classifier and stores the exact indexed result (including -1); seven performer records, all existing appearance, position and event callsites reuse the same helper; no duplicate attach/extra callbacks |
| 707 | ReinforcementsCall stimulus | **New**: after original `wrappedMethod(stimData)`, null-check the task, reject defined non-player/non-approved source stimuli ahead of mod-owned NPC/stat work; approved CombatHit, Explosion, Gunshot, HijackVehicle, CarryBody, Bullet; null cached event follows historical path; preserves original mod callsite |
| 708 | ReinforcementsCall police prevention | **New**: after owner resolution and null check, suppress *additional* reinforcement probe for prevention/police NPCs; vanilla wrapped police pursuit remains unchanged, original calls are retained |
| 709 | ReinforcementsCall burst probe | **New**: original historical 100 ms per-`ReactionManagerComponent` engine-time gate, after stimulus and police checks, before stats; duplicate field prevention and time rewind recovery guard; do not throttle `wrappedMethod` |
| 710 | Chipware 24-perk result memoization | **Narrow `REFUSED`**, not silently emitted: neither Pass 4 nor Pass 8 proves a complete invalidation epoch covering perk purchase/reset, equipment-stat/charges, mod gameplay state **and the mutable vanilla `wrappedMethod(perkType)` fallback result**. Historical 100 ms timed memo can return stale results. Do not introduce a second competing cache or assume mutable values constant; keep Pass 4 source-derived relevance gate and Pass 8 combat/cheap-path caches intact |

**Coexistence:** Pass 4 `ReinforcementsSystem/Utils.reds` and `ReinforcementSystem.reds` geometry source files are independent of this branch's `ReinforcementsCall.reds`; integrate both against effective staged sources without replacing either. Pass 4 StealthRunner and Pass 7 #702 own the **same** `m_stealthRunnerPlayer` field, OnActivate/OnDetach/fallback; consolidation must select a single implementation or structurally converge once (not duplicate). Chipware Pass 4/8 already optimize the same perk wrapper; #710 never modifies it. Other historical work fully covered by baseline semantic stages: Dark Future #25, DVCore #27, DisablePositionReveal #21, Quickhack Fixes #18. Pass 9 RunningMan dormancy remains a separate pass, not part of these remaining Pass 7 gates.

**Acceptance / limits:** source-state cases cover post-generic stock, manual partial/complete, custom/moved, narrow incompatible, missing owner, repeat no-op, source immutability. Canonical Generate composes the current post-generic effective preview and emits only owner-local files. Official SCC compilation verifies the combined generated REDscript **in CI**, not on a player installation. Fresh in-game A/B validation still required for wanted-level chases with police/non-police, reinforcement pacing, NPC attach/detach and Lizzie appearance/positioning/events, plus perk/equipment gameplay transitions for the independently cached Pass 4/8 operations. Do not assert measured speed gains or a stable-main release.

---

## Integrated on this branch

The canonical `RedscriptGenerationPlan.Stage` now invokes `Pass7ChaseFrameFloorPass.Generate` **after the ten generics and existing Phase 2 semantic stages**. It composes against effective previously staged source, never an old whole-file ZIP, and produces only owner-local game-relative files. Reserved semantic operation slots 700–705 deliberately avoid collision with other agents' active 37+ sequences; the integration lead may renumber when combining branches.

| Slot | Operation | Owner/file | Functional contract |
|---|---|---|---|
| 700 | `NCR_SPAWN_EARLY_CULL_GUARDS_V1` | NightCityRemembers / NCR_SpawnCulling.reds | Hoist proven quest/companion/crowd negative guards; retain actual RNG point, faction checks, gameplay/culling callback |
| 701 | `STEALTH_RUNNER_QUEST_DETECTION_GATE_V1` | StealthRunner / npc_puppet.reds | Reject unrelated quest NPC detection before player/blackboard lookups; original wrapped callback still runs |
| 702 | `STEALTH_RUNNER_PLAYER_HANDLE_CACHE_V1` | same file | Cache only a valid weak player handle at NPC init, clear at detach and re-fetch when missing; no stale strong global reference |
| 703 | `RUNNING_MAN_SPRINT_ENTRY_PERK_CACHE_V1` | RunningMan / double_sprint.reds | Sample sprint perk on original OnEnter, preserve sprint timing, resume/stamina/modifier transitions |
| 704 | `RUNNING_MAN_SPRINT_DIRECTION_PERK_CACHE_V1` | RunningMan / sprint_direction.reds | Cache perk in entry condition with explicit live-query fallback when optional EasierControllerSprint excludes that method; preserve unrelated perk checks |
| 705 | `LIZZIES_BDS_PERFORMER_ATTACH_GATE_V1` | LizziesBDs / NPCPuppet.reds | Source-derived performer record filter only around original Lizzie attach block; retain wrapped method, custom neighboring code and all event functions |

Source detection and local skips are per operation, not per owner. The StealthRunner operations compose atomically into one final source file. Missing optional mods remain not applicable. Generic source markers and all earlier effective transformations must survive. Source admission checks source drift before publishing output. No player-side SCC, installed file writes, source hash/version locks, repair passes, or stable-main merges.

## Validation contracts

Dedicated branch-only workflow `.github/workflows/redscript-pass7-checkpoint.yml` verifies:
1. Real authoritative 2,331-file post-generic source input and .NET 10 resolver build.
2. Post-generic stock, handwritten partial, markerless complete, repeat no-op, relocated/custom, incompatible, absent and operation-specific independent continuation.
3. One standalone coherent batch, source-file ownership and source immutability.
4. Exact byte parity between independently generated Pass 7 owner files and the **production one-pass** `stage-redscript-plan` effective source output.
5. Official SCC 0.5.31 compilation of the **entire canonical generic + existing semantic + Pass 7 composition**, never just a syntactic toy fixture.

**[Dedicated Pass 7 CI #11 SUCCESS](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/44876625041)**: all six operations (23 edits / five original-path owner files) passed stock/partial/complete/custom/incompatible/absent/repeat tests, canonical one-pass byte parity and SCC 0.5.31 compilation across 2,331 post-generic files plus 34 semantic effective files (43,512,829 compiled bytes; sources untouched). `build-windows.yml` now has a **mandatory branch-scoped reusable Pass 7 CI/SCC prerequisite**, so a successful app build alone cannot mask invalid output. **[Full GCO #1098 SUCCESS](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/44876651197)** at `3f8a8615ba8b22d4b38c38ccbbe00e60280820ac`: reusable source/SCC prerequisite plus integrated .NET 10 build, one game-relative combined ZIP/ownership contracts and package integrity passed. Development app artifact `Gs-Cyberpunk2077_Optimizer-v1.2.5-run1098-a1`, ID `14696655833`, size `90,572,902` bytes. **CI ship-path ready on this branch**, pending independent generated-overlay gameplay/A-B proof; source correctness alone does not verify frame-time benefit. CI results are not gameplay or measured savings. Historical handmade ZIP behavior was played/tested, but **the new generator-produced files still need comparable in-game A/B checks**.

## Remaining Pass 7 review / isolation

- **Already implemented on baseline:** Dark Future throttles (#25), DigitalVixenCore windows (#27), DisablePositionReveal tracing (#21), Quickhack_Fixes status-tag probe (#18). Do not duplicate these rules.
- **ReinforcementsCall / 7.1:** correlated with combat Pass 4 ReinforcementsSystem and Pass 6 scheduling; owner must agree exact file/operation boundaries, stimulus admission, police prevention, 100-ms throttle and 7.1 correction before integration. Do not race another agent or import old source.
- **zzy_bChipwareExpansion:** overlaps Pass 5 and Pass 8 perk-state caching; do not create competing perk rewrites.
- **LizziesBDs:** implemented only the independently safe attach-gate operation; historical across-method index rewriting is not automatically claimed.
- **RunningMan Pass 9 dormancy and other Pass 7 operations:** remaining independent work requires source audit and explicit behavior/lifecycle proof.

Production one-pass integration and focused real-source compilation are verified on this development branch. Full application/package CI and actual generated in-game behavior need distinct acceptance; source/CI correctness alone does not establish FPS/CPU improvement.
