# R5 BLACKBOARD / shared-state implementation — exact done vs remaining

**Last updated:** 2026-11-17. **READ after** GCO `AGENTS.md`, `REDSCRIPT-RESOLVER-LOCAL-HANDOFF.md`, `REDSCRIPT-RESOLVER-PROGRESS.md`. **Active branch:** GCO `dev/redscript/resolver_handoff`. **Native profiler:** GRSP `dev/native-instrumentation`. **Runtime services:** owner repo `G-REDruntime_Framework/main`. **CET:** owner repo `G-CET_Runtime_Profiler/dev/resolver-profiler-handoff`. No source owner or stable main altered during this implementation.

## 1. Verified actual CET blackboard optimization — DONE in CET

This is **NOT** `tests/G-CETtoNATIVEplayer_state_machine/G-CET_TO_C++_PLAYER_STATE_MACHINE.zip`. That native archive contains `GCETStaticLocations.dll` and `G-CET.locations` (static/native player position and NEAR/FAR state), not a blackboard definition cache.

The verified CET optimization is the **separate** `src/G.CETProfiler.Core/Services/BlackboardPsmDefinitionCacheSource.cs` rule `0engine-psm-definitions-cache-stage-a`, generated marker `G-CET PSM_DEFS_STAGE_A_V1`, targeting `0-Engine/modules/BlackboardCache2.lua`.

- Initialization: resolve the **12 immutable** `GetAllBlackboardDefs().PlayerStateMachine` definition keys once during `BlackboardCache.Init()`, stored in `__gcetPsmFields`.
- Per-update: continue the **same 11 `playerBB:GetInt` and one `GetBool` live blackboard value reads**; no callback deletion, timing changes, cached changing values or native player-state-machine hook.
- Source/CI: `tests/BlackboardPsmStageAContract/Program.cs`, source compatibility, marker recognition, drifts, lifecycle anchors, tests for complete 12-callsite profiler evidence.
- User-provided `CET-20261019-222748_BLACKBOARD_CACHE_TEST.zip`: 1,416 sampled `BlackboardCache2.lua` C-callsite records, **5,664 sampled positive own allocation bytes, 118 positive ops**, across lines 194–205, all positive bytes at line 205. The older CET research `docs/experiments/0ENGINE-SHARED-STATE-AUDIT-20261019.md` records an earlier **105,000** positive sample bytes and the user's independently reported **~95% reduction in that particular allocation metric**. Samples/scenes differ: do NOT turn those into a controlled FPS, full GC, or complete workload claim.

## 2. Actual REDscript F11 evidence — measured, not savings

Directly checked attached `r6LIVEnow.zip` and `GCO-Capture-16112026_132224.zip`.

- Current provided source snapshot: **2,319** `.reds` files; **857 textual `GetAllBlackboardDefs` occurrences over 68 top-level source folders** (includes unexecuted code/comments; NOT runtime call counts or admission).
- There are **27 text lines** with two occurrences across **12 source folders**, including ReinforcementsSystem, DashFix, zzy_bChipwareExpansion, Dark Future, Crowd Voice Unlocked and independent others. This is shallow discovery only; the authoritative CLI parser applies structural/refusal gates.
- Genuine F11 `RSP_CallSites.csv` / `RSP_SharedTargets.csv`: **45,308 outgoing `GetAllBlackboardDefs` calls / 14.9077 ms exclusive** across **28 measured owners** and **49 distinct source functions** in a 384.517-second capture (**~0.0388 ms/s direct**). One ReinforcementsSystem callsite makes **22,460 calls / 11.8686 ms**, including two definitions per execution; this is not evidence that all 22,460 can be saved. Do NOT project the CET ~95% allocation improvement onto REDscript: its VM, API and measured direct costs differ.
- Existing full-capture source-keyed evidence **does not** show whether `GetAllBlackboardDefs` allocates materially in REDscript or guarantee return-type semantics for caching values across callbacks. No new capture requested solely for this small direct cost.

## 3. REDscript implementation — CODED, preview-only

**New generic rule:** `BLACKBOARD_DEFINITION_STATEMENT_V1`.

Authoritative code: `src/GRedscript.Resolver/GenericBlackboardDefinitionPass.cs`, invoked by **`scan-blackboard-defs`** and **`preview-blackboard-defs`** in the same .NET 10 GCO Resolver. No parallel executor/runtime.

**Exact transformation:** In a **single source-proven REDscript function statement**, if two `GetAllBlackboardDefs().<sameGroup>` references appear, emit a local `let __gcoBbDefL<line> = GetAllBlackboardDefs().<Group>;` immediately before the statement, then replace those two definition-group references with that local. Keep the original `GetInt/GetBool/SetVariant/RegisterListenerVariant` calls, current blackboard instance, method/body and callback order untouched. This stores **definition metadata within the same statement only**, not across callbacks or game sessions. Source shape is independent of mod names or permanent source SHA/version pins.

**Admission/refusal:** existing `SourceAdmission.ReadTokens` and full current-source fingerprint; only parsed function scopes; two matching definition-group references on the same straight-line statement; reject short-circuit `&&/||`, conditional `?`, source comments/strings, unknown/multiple groups, unsafe local collisions. Generated marker `// GCO-GENERIC:BLACKBOARD_DEFINITION_STATEMENT_V1` has postcondition checks; missing/partial marker fails closed. Read-only scan JSON identifies exact eligible source paths and groups **locally**, never exposes private sources as a public CI artifact. Preview requires the sealed collected F11 `GCO_INDEXED_COLLECTED_BYTES_VERIFIED` evidence and exact original source fingerprint; writes ONLY under isolated `--out`, never game/capture directories. CompilerValidated/GamePlayValidated/MeasuredSavingsClaimed = false until actual proofs.

**Tests:** `tests/GRedscript.Resolver.Tests/test_generic_blackboard_definitions.py`: two independent groups, dynamic values retained, pristine/completed/partial states, second-pass zero-op, mismatched groups, short-circuit refusal, source-comment/quoted-value refusal, forged marker, source-drift and game-output refusal. The canonical single Windows CI runs these alongside all existing tests, performs current/private 2,319-file scan and invokes the original full-stack official SCC v0.5.31 runner with staged files and external G-REDruntime sources.

**Pass ownership collisions:** Existing generic passes may edit a **different span in the same game-relative .reds file**. CI never overwrites that file: the standalone blackboard preview is retained in a disposable private workspace, and only **disjoint** generated files are merged into the combined SCC preview. Conflicted files are counted as **DEFERRED**. This is **not yet** proof of simultaneous installation of both rewrites; finish source-aware composition at generator source rather than run a corrective post-pass or discard one legitimate mod fix silently.

**Verified after implementation:** [GCO CI #579](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/44026242741) **SUCCESS**: **86/86 managed REDscript tests passed**, current private 2,319-file source scan reports **22 strictly eligible definition statements**, none already optimized, all originals unchanged; existing official SCC 0.5.31 full-stack/framework previews and GCO package contracts passed. The first attempt, #577, failed on a malformed **negative-test fixture** and was corrected without relaxing its safety requirement. [GCO CI #580](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/44026362823) **FULL SUCCESS**: 86/86 managed REDscript tests passed; newest 2,319-file source audit found 22 eligible immutable-definition statements across 16 generated owner files, with **14 disjoint files safely merged, 2 file collisions deferred**. The real sealed F11 replay and **official SCC 0.5.31** compiled the actual staged full source with **22 callback/generic preview files** plus **10 authoritative external G-REDruntime owner source files**; the `framework-all-generics-preview` phase exited 0 (7.3s SCC), and **323/323 GCO install/CLI/package contracts** passed. Source unchanged. Compilation is isolated, previewOnly=true, gameplayValidated=false, deployable=false, no FPS gain proven. The two deferred source files still require source-owner composition before claiming complete coverage.

## 4. Other generic source passes already present — DONE in source, still preview

| Pass ID | Existing coverage | Status |
|---|---|---|
| `CNAME_ARRAY_MEMBERSHIP_V1` | Conservative array literal membership source shape | isolated source/SCC preview |
| `CNAME_FIXED_INDEX_V1` | Source-proven fixed-index CName membership | isolated source/SCC preview |
| `ACTION_LITERAL_MEMBERSHIP_V1` | Pure literal action membership early dispatch | isolated source/SCC preview |
| `ANGLE_SORT_INVARIANT_V1` | Hoists inner-loop four-vertex `AtanF` within stable sort | isolated source/SCC preview; 12→9 evaluations per sort, **no runtime A/B** |
| `SYSTEM_READ_AFTER_ACTION_GUARD_V1` | Read only after action-type rejection, original `wrappedMethod` first | full CI #573 and #574 SUCCESS; preview; **small** observed direct-cost ceiling |
| **`BLACKBOARD_DEFINITION_STATEMENT_V1`** | Same-statement two same immutable definition groups; retains live state reads | **CI #580 SUCCESS**; 22 source matches/16 generated files, 14 disjoint SCC-compiled, 2 same-file collisions deferred; gameplay and A/B pending |

No mod exclusions from any generic pass, including the seven formerly reserved for **targeted semantic remainder only**.

## 5. Remaining work — do not conflate stages

1. **Completed CI gate:** [#580 SUCCESS](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/44026362823) verified current-source scanner (22 exact statement candidates), 86 managed tests, 16 generated files (14 compiled disjoint + 2 deferred collisions), original source hashes unchanged, complete external framework SCC v0.5.31 preview and 323 packaging tests. Preserve these contracts; never weaken a legitimate failure.
2. **Composable source-owned generator:** decide and implement deterministic **multiple generic edits to the same game-relative file** using the current effective staged source and original capture/source provenance. Do not overwrite one pass with another or add a later “repair” transform. Verify pristine, previously staged, fully optimized, missing piece, source drift, second run zero-op and whole-stack SCC.
3. **Significant runtime improvement separate from tiny metadata calls:** source-wide read-side classification of repeat player/game service accesses, matched receiver/epoch/arguments plus GRSP high-work callsites. Avoid generic StateCache calls if overhead exceeds ~0.04 ms/s measured static-definition work.
4. **Framework-owned event-driven shared state:** G-REDruntime currently has weak system handles, lazy 50-ms combat/vehicle ContextService and explicit DirtyFlags; it does **not** have CET's event-backed value/version/UNKNOWN/generation service for the complete player state set. Identify authoritative REDscript-compatible state-change producers, prove invalidation/teleport/attach/detach and first-state semantics, then implement the service **in G-REDruntime repo**; GCO emits structurally admitted adapters only after owner compilation. Never cache live PSM values as static definitions.
5. **Larger cross-mod generic opportunities:** InputHub finite-action ingress (original `ListenerAction`, consumer, action type and order proof), DirtyFlags-driven ink/list refresh (full mutator→consumer invalidation, time/animation), shared static NEAR/FAR coordinator (typed REDscript-native ABI and UNKNOWN fallback), shared observers, keyed collection indexes. Rank by measured **eliminable downstream CPU/VM work and spikes**, not source prevalence.
6. **Only after evidence and SCC:** matched in-game before/after F11 captures with real PresentMon tail and gameplay/lifecycle regression tests. Then production overlay source ownership, one clean game-relative ZIP, idempotence, restore and GCO 2-page workflow; **currently NOT deployed**.

**Next agent instruction:** start from the actual previous successful GCO run **#574**, inspect newly triggered runs for compiler+test results, and treat `BLACKBOARD_DEFINITION_STATEMENT_V1` as *preview only*. Preserve the separate CET native location and blackboard definition work. Avoid claiming the total benefit of 45,308 calls as potential savings; direct measured contribution is only 14.9 ms over six minutes. Move onward to substantial generic work elimination after this modest high-confidence proof.
