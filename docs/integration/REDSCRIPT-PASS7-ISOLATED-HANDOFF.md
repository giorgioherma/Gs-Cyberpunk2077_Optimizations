# REDscript historical Pass 7 — isolated production one-pass branch

**Owner:** Agent 5, historical Pass 7. **Branch:** `dev/redscript/pass7`.
**Base:** verified `96d66c66642ac52dd13fd4001416572b9169bace` (do not merge unfinished handoff/Pass4/Pass5 work).
**Scope:** isolated GCO-owned source-aware REDscript generation, source-state tests and branch-scoped official SCC. The app's existing two-page workflow, CET and GRSP profiler owners, and shared G-REDruntime services remain untouched.

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

A full branch-specific `build-windows.yml` must additionally validate integrated .NET 10 application, one game-relative combined ZIP, rollback/ownership sidecar and package integrity before this code is accepted as ship-ready. CI results are not gameplay or measured savings. Historical handmade ZIP behavior was played/tested, but **the new generator-produced files still need comparable in-game A/B checks**.

## Remaining Pass 7 review / isolation

- **Already implemented on baseline:** Dark Future throttles (#25), DigitalVixenCore windows (#27), DisablePositionReveal tracing (#21), Quickhack_Fixes status-tag probe (#18). Do not duplicate these rules.
- **ReinforcementsCall / 7.1:** correlated with combat Pass 4 ReinforcementsSystem and Pass 6 scheduling; owner must agree exact file/operation boundaries, stimulus admission, police prevention, 100-ms throttle and 7.1 correction before integration. Do not race another agent or import old source.
- **zzy_bChipwareExpansion:** overlaps Pass 5 and Pass 8 perk-state caching; do not create competing perk rewrites.
- **LizziesBDs:** implemented only the independently safe attach-gate operation; historical across-method index rewriting is not automatically claimed.
- **RunningMan Pass 9 dormancy and other Pass 7 operations:** remaining independent work requires source audit and explicit behavior/lifecycle proof.

Development-branch integration is complete at the stage wiring level, but deployment acceptance depends on green focused **and full** CI and actual gameplay verification.
