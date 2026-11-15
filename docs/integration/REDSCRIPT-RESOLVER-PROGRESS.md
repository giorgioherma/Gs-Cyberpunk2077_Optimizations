# GCO REDscript Resolver integration — active R0–R10 progress ledger

**Active GCO branch:** `feature/redscript-resolver-integration`, forked from finished `feature/presentmon-resolver-integration` at `3caccafbdf39ec2b6474b69ed8f34c9392e2ff20` (2026-11-15). **Protected GCO `main`:** `f7b8e6baa66ce363fd272f5cb8370548383e8bd2`.
**Canonical full blueprint:** [GRSP-owned REDscript Resolver blueprint](GRSP_REDscript_Resolver_Blueprint.md).
**Authoritative REDscript code branch:** [GRSP `dev/resolver-profiler-handoff`](https://github.com/giorgioherma/GRSP-G-REDscript-Profiler/tree/dev/resolver-profiler-handoff), [owner handoff](https://github.com/giorgioherma/GRSP-G-REDscript-Profiler/blob/dev/resolver-profiler-handoff/docs/RESOLVER_HANDOFF.md), [owner progress](https://github.com/giorgioherma/GRSP-G-REDscript-Profiler/blob/dev/resolver-profiler-handoff/docs/RESOLVER_PROGRESS.md); GRSP ownership/docs commit `b82e73cfe1bf43389cbc35cf9c65bd7a312ab5f0`.

## Stage register

| Stage | Status | Exit evidence required |
| --- | --- | --- |
| R0 branch/ownership handoff | DOCUMENTATION COMPLETE | New GCO branch, full canonical blueprint, GRSP+GCO AGENTS/handoffs/decisions/ledgers aligned; no code implementation |
| R1 private reference inventory only | NOT STARTED | Index 61 paths/owners/helpers/provenance privately; **no semantic recipe implementation** |
| R2 fresh three-profiler measurement | FIRST CAPTURE REVIEWED; LIVE SOURCE PROOF PENDING | One new genuine 309.6 s WORLD three-profiler capture analyzed (13,377 verified PresentMon frames). R2 owner audit: [GRSP analysis](https://github.com/giorgioherma/GRSP-G-REDscript-Profiler/blob/dev/resolver-profiler-handoff/docs/MEASURED-GENERIC-OPPORTUNITIES-2026-11-15.md). Effective live source inventory, source-byte matching and repeated scenario proof pending |
| R3 GRSP Resolver evidence contract | OWNER READ-ONLY CLI BUILT; INTEGRATION PENDING | GRSP net10 owner CI #3 passed 8 tests; actual private replay / GCO invocation not yet proven |
| R4 structural REDscript source admission | TOKEN/SPAN SOURCE DIAGNOSTICS ONLY | Method-name/line and source SHA audit; receiver, partial/already-complete and rule-specific proof pending |
| R5 measured generic discovery + passes FIRST | NOT STARTED | Rank full live source from fresh data, apply high-value generic rules regardless of portfolio, compile/behavior/idempotence and coverage proof |
| R6 iterative GRSP/G-REDruntime enablement | CONDITIONAL | May interleave with R2–R5 to unlock high-value reusable passes; prove bounded overhead and lifecycle |
| R7 61-file GAP MATRIX → semantic remainder LAST | BLOCKED BY R5/R6 COVERAGE | After generic output, classify per-operation GENERIC_COVERED / ALREADY_PRESENT / SEMANTIC_REMAINDER / UNPROVEN_OR_DRIFTED / NOT_APPLICABLE; only remaining approved work may be implemented |
| R8 whole-stack compiler and output safety | NOT STARTED | Authoritative REDscript compiler, untouched/partial/complete/drift tests, changed-only overlay |
| R9 GCO headless integration | NOT STARTED | Source-built GRSP on shared .NET10, tested owner interface, source/current-capture proof, no new lifecycle gates |
| R10 combined ZIP + real gameplay | NOT STARTED | One collision-safe game-relative ZIP, actual game regression and measured before/after |

## Immutable existing baselines

- CET original owner Resolver/generation: last private full source-state milestone **GCO CI #416 SUCCESS**, original source snapshot 5,459 files, generated 396 ZIP entries / 394 Lua; real gameplay **NOT TESTED**.
- GCO genuine PresentMon-only consolidated profiler report: **CI #426 SUCCESS**, 14,573 PresentMon, 14,572 GRSP aligned frames, 568,724 CET timeline rows; sealed evidence unchanged. Real report visual acceptance and native gameplay **NOT TESTED**.
- Preserve GCO two-page UI, COLLECT/report independent of CONTINUE/RESTORE and ANALYZE, source-owned CET generator, single existing Windows CI, one private .NET10 runtime. No fabricated CapFrameX or additive CET+GRSP CPU.

## Dated progress and decisions

### 2026-11-15 — R0 branch and authoritative documentation, no production code

- Created `feature/redscript-resolver-integration` from completed CET feature at `3caccafb`; original complete blueprint committed in `7e2e17589c3a6ba0f4ee163c21fe8ae0e4cac9bf`.
- GRSP development source branch `dev/resolver-profiler-handoff` now documents authoritative REDscript Resolver/Generator/compiler ownership in commit `b82e73cfe1bf43389cbc35cf9c65bd7a312ab5f0`, with new `AGENTS.md` and progress records. GCO delegates code to GRSP and retains cross-domain reporting/one-ZIP responsibility. Historical conflicting owner text updated as necessary in this feature.
- **Verification scope:** GitHub repository document/branch checks only. No CI dispatched for documentation-only changes, no source generation/REDscript compilation or game tests. Last relevant baselines #416 and #426 as above.
- **Next single action (historical R0 note; superseded by ordering decision below):** R1 private reference indexing (no semantic recipe design), then R2 fresh capture ranking and R3 machine evidence. Do not implement transformations from old captures or assume fixture mods are current installs.

### Mandatory updates for future agents

**On every meaningful code or design change, immediately append:** (1) date, exact repo/branch/commit and files; (2) approved decisions and rejected alternatives; (3) exact real-source/capture/CI/compilation tests with PASS/FAIL/SKIP and links, not promises; (4) drift/idempotence/install/gameplay proof level; (5) unresolved regressions and next action. Update `docs/LATEST-WORK.md` promptly and update GRSP's `docs/RESOLVER_PROGRESS.md`/`docs/LATEST-WORK.md` for owner code. Record a failure before retry, not only the final green. Do not create parallel CI workflows or leak private source archives to public build artifacts.

### 2026-11-15 — CI trigger routing for REDscript feature

- Existing sole `.github/workflows/build-windows.yml` push branch list now includes `feature/redscript-resolver-integration`; no new workflow, skipped checks, reduced package/test contracts or code changes. This workflow config change should trigger the first REDscript-branch baseline Windows run; **result pending until exact Actions conclusion is recorded**.
- This tests the inherited GCO/CET/GRSP bundle at existing owner pins, **not the future GRSP Resolver/Generator**. The later GRSP implementation needs independently proven source/compilation and GCO owner pin/CLI integration. Private CET current-source replay remains manual milestone-only.

### 2026-11-15 — owner correction: generic opportunities first, 61-file semantic remainder last

- **Decision / sequencing contract:** First fresh CET+GRSP+PresentMon measurements and verified live source → broad measured **generic discovery** → implement/compile/idempotence-test high-value generic passes; extend GRSP native instrumentation or G-REDruntime source as needed to enable reusable opportunities. Only after generic coverage exists compare its **actual output** against the 61 approved reference files at individual transform level; then implement **only** proved residual approved semantics.
- **R1 is private reference inventory only**, not authority to start semantic code. **R7 is blocked** until R5/R6 generic coverage is evidenced and the per-operation five-status gap matrix has skip reasons. Many/zero semantic changes are both acceptable if justified; neither 61 modified files nor historical pass numbering is a success metric.
- **Verification:** decision/docs only. No fresh captures, source transforms, REDscript compiler runs or in-game tests performed by this change. CET original CI #416 and GCO PresentMon report CI #426 remain historical baselines; do not treat them as new optimizer validation.
- **Next:** collect fresh evidence, rank full-stack generic candidates, establish structural safety and measure/test the first useful generic pass. Build the portfolio semantic gap matrix after generics.

### 2026-11-15 — first REDscript-branch baseline CI result recorded

- **[GCO Windows CI #427](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/43704425184) COMPLETED SUCCESS**, source `41681da885a529708d7231f1f2d59293fba8e77c`, verifying inherited GCO Windows build/package workflow on `feature/redscript-resolver-integration`. This is **NOT** a GRSP REDscript Resolver/Generator, full-stack REDscript compilation or gameplay test; subsequent documentation-only generic-first commits were not CI-built.
- Documentation-only generic-first sequencing commit `a064acf511d94d75922cf33dd295cea80513870b`; GRSP owner matching commit `479be2425dc13462cb30ad6cff24ea7332cd27ea`. Existing CET branch and GCO `main` unchanged.
- **Next:** fresh source-matched capture and high-return generic candidate audit; no semantic port until R5/R6 output and R7 matrix.

### 2026-11-15 — R2 captured data reviewed, generic opportunities measured (no generator changes)

- GRSP **owner's** [first measured generic opportunity audit](https://github.com/giorgioherma/GRSP-G-REDscript-Profiler/blob/dev/resolver-profiler-handoff/docs/MEASURED-GENERIC-OPPORTUNITIES-2026-11-15.md), commit `2278007d861e7f84a7c37089f0ddcf41f8405bf6`. Based on the user's newly collected 309.614 s WORLD capture with genuine `PRESENTMON_2_6` and complete GRSP developer CSVs. All original raw ZIPs were inspected **locally** and are not committed.
- Early **generic-first** opportunities: (A) input/action routing and device-state-change gating (important `custom_quickslots` + `PlayerPuppet.OnAction` wrapper traffic), (B) equivalent UI/update-state write suppression (important Dark Future buff controller), then (C) same-scope service reads and (D) periodic work after lifecycle/cadence proof. Exact `OnUpdate` is not universal; GRSP call edges are not invocations. Largest `FlushingEtiquette` minimap cost is dominated by unresolved wrapped proxy and must **not** be naively optimized.
- **Validation limitations:** 2,280-file older local source snapshot path-matches most observed functions but is NOT current source-bytes proof; three observed hot paths are missing there. No new REDscript compiler/generator, no optimization ZIP, no gameplay test, no post-change gain, no CI run; existing CET and GCO reporting retain their prior verified state.
- **Next:** author GRSP-native Resolver evidence schema and verify actual live source path/line + callback entry/invalidation semantics for generic A/B; design/compile/test generic passes before any R7 semantic portfolio gap analysis. GCO should consume native owner output, not repeat analysis inside its report.

### 2026-11-15 — GRSP R6 callback measurement pass plus GCO main ranked reporting

- GRSP owner `dev/resolver-profiler-handoff` added native post-capture `RSP_CallbackPressure.csv` + `RSP_CallbackTargets.csv` at `01cc4c82003b0b8e993a17f1aa717abcce9b0ddc`. This reuses existing InvokeStatic/InvokeVirtual source-bound call-edge and frame counters after F11 STOP; no new VM hook, changed gameplay, callback-entry-count claim or semantic pass. Owner-specific independent `.github/workflows/native-instrumentation.yml` at `19c90f43acbf93d9785be6ec3dfe29814d070e7d`. [GRSP source CI #1](https://github.com/giorgioherma/GRSP-G-REDscript-Profiler/actions/runs/43716286993) **SUCCESS** (2 Rust unit tests, `cargo check`, source/lifecycle contracts). Manual release-DLL job intentionally **SKIPPED**; only explicitly dispatched on dev via CLI/API, never silently included by GCO. GRSP stable `main` remains unchanged to avoid automatic release republishing.
- GCO `main` source-owned consolidated COLLECT profiler report now displays **eight ranked views** from real CET and GRSP CSVs: highest measured ms/s and most frequent calls/s for both owners and callbacks/functions, plus a machine-JSON counterpart. [GCO Windows CI #433](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/43716510455) **SUCCESS** with real native source reporter fixture and order/attribution checks, no change to GCO COLLECT/CONTINUE/restore or ANALYZE.
- This REDscript development branch remains code-isolated; progress recorded only. **Next owner gate:** explicit native DLL build and new capture with callback pressure CSVs, live source/byte proof and true unchanged-state/entry-count measurement if the existing instrumentation cannot answer candidate safety. R7 semantic work still blocked until generic passes are implemented and compared with 61-file reference.

### 2026-11-15 — native CET player/state and NEAR/FAR included in REDscript planning

- Reviewed uploaded dated source and hand-optimized FlushingEtiquette FE_Places.reds. FE's fixed-location toilet/sink scans are candidate **generic** native FAR gating, whereas the hot minimap wrapper is unresolved base/proxy work and the historical portfolio removes it separately. See [CET native player and near/far contract](GRSP-NATIVE-PLAYER-PROXIMITY.md).
- Model CET Resolver native cache as PLANNED_BY_CET_RESOLVER so its absence from current unoptimized files does not block design; prove final owner output, typed REDscript bridge, safe UNKNOWN behavior and full compile before emitting any transformation. No gameplay claims or REDscript pass yet.
- GRSP native dev commit 61b0be963b9c16827be5c4ea5df12b5b61bd5e11 adds strict F11 feature; source CI #2 SUCCESS. GCO main commit f0e30e92ec470aa7cfd5c2544f83b308e49f15a1 admits only a manually built verified owner artifact. The native release DLL is not built and not in normal GCO packages. Next: independent GRSP manual native build, GCO import validation and real capture.

### 2026-11-15 — GCO native owner-run SHA admission baseline CI #435 SUCCESS

- [GCO Windows CI #435](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/43719910633) **SUCCESS**, exact tested GCO source 0be794152b30a11c0eebea0b03883ba27543ccd8. Confirmed 323 installation/CLI/package contracts, hash-preserved real CET+GRSP+PresentMon report replay (14,573 PresentMon, 568,724 CET timeline rows, 14,572 GRSP frames), source-owned install/restore smoke and WPF Page 2 Normal-Maximized-Normal geometry. GCO v1.2.5 artifact Gs-Cyberpunk2077_Optimizer-v1.2.5-run435-a1, artifact ID 14164740012, 89,864,542 bytes.
- This is the regular/default GCO lane only, with the prior GRSP native baseline; manual GRSP instrumentation DLL CI and opt-in GCO artifact import **NOT YET EXECUTED**. Source-owned GRSP gco_explicit_f11 feature/default compile checks passed GRSP CI #2. No new callback-pressure CSV in the default bundle, no new game capture or gameplay acceptance.
- Final manual import path now resolves the exact GRSP source SHA from the approved successful manual native CI run, so new GRSP documentation commits cannot cause stale source pin rejection. It still enforces manual run, dev branch, source-owner workflow, F11 mode, native SHA, embedded evidence and Cargo lock without building that native code in GCO. Next: execute manual GRSP native CI and GCO explicit opt-in build, then test a fresh capture.

### 2026-11-15 — native GRSP instrumentation automatically linked into normal GCO main CI

- User approved **independent GRSP native CI, automatically consumed by GCO main**. GRSP owner source commits `5046d331` (native release job on relevant dev push) and `f36e9d8` (test contract correction after CI #3 failed on stale manual-only assertion) build source-owned `gco_explicit_f11` DLL with `RSP_CallbackPressure.csv`/`RSP_CallbackTargets.csv` after Rust contracts. Stable GRSP main unchanged. [GRSP CI #4](https://github.com/giorgioherma/GRSP-G-REDscript-Profiler/actions/runs/43721633499) source checks PASSED; native release job still PENDING at this entry.
- GCO main source commits `ede01fff`, `f9eb4418`, `dfec85ce` automatically find the latest successful GRSP source-owned native CI artifact, check source SHA/provenance/F11/CSV/hash and package it using the regular GCO workflow. Old GCO Rust build, source rewrite, native cache and fallback are removed. [GCO #438](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/43721962903) FAILED CLOSED because the new native artifact had not yet completed; do not claim a downloadable verified new instrumentation app until the owner-native artifact and repeat GCO CI both succeed. No separate user build input.
- No REDscript optimizer, generated pass, new gameplay capture or measured performance gain in this stage. The CET source-backed native NEAR/FAR proposal remains a planned cross-domain dependency, not deployed REDscript code. R7 semantic work still gated by generic coverage.

### 2026-11-15 — actual GRSP-native owner DLL accepted in standard GCO package

- [GRSP independent native CI #4](https://github.com/giorgioherma/GRSP-G-REDscript-Profiler/actions/runs/43721633499) **SUCCESS**, compiled source `f36e9d8ba64ecc2733ed0d7428044722e1780c59`, native artifact `14165291190`; strict-F11 GCO native DLL SHA256 `9bf22968a54570dbec614ce7368127e738e4848d30f055500e7c4cc60453d6f5` includes both new callback-pressure developer writers.
- [GCO standard Windows CI #438 attempt 2](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/43721962903) **SUCCESS**. Native DLL taken automatically from GRSP owner CI and verified without GCO Rust build or manual input; 323 package/install/restore contracts, explicit-F11 native smoke, PresentMon/CET/GRSP reports and original Page 2 WPF checks passed. GCO downloadable artifact `Gs-Cyberpunk2077_Optimizer-v1.2.5-run438-a2` ID `14165845184`. Early failed attempts were blocked by missing owner artifact; they did not ship an old fallback.
- **Next gate:** user gameplay F11 capture with this app to confirm real `RSP_CallbackPressure.csv` and `RSP_CallbackTargets.csv` and safe RESTORE. Then GRSP source-proven generic candidate/input-update instrumentation analysis. No optimization pass, full REDscript compilation, changed-source ZIP or runtime performance claim yet; the 61-file semantic remainder is still blocked by generic coverage.

### 2026-11-15 — GRSP R3/R4 managed owner CLI available, integration remains gated

- GRSP `dev/resolver-profiler-handoff` implemented independent `net10.0` framework-dependent Resolver CLI at [commit 6490f90](https://github.com/giorgioherma/GRSP-G-REDscript-Profiler/commit/6490f90c7c02e421bae85da5b9f92e1fadcfcb8c); fixes `ede4f5e` and `4173e67`. Separate [GRSP managed Resolver CI #3](https://github.com/giorgioherma/GRSP-G-REDscript-Profiler/actions/runs/43736450803) **SUCCESS**, 8/8 CLI safety tests and owner-only artifact `GRSP-Resolver-owner-net10-4173e677bfdbc7be22bcddc06a0711d94844dc3f` (ID `14170063290`). First CI #1 failed on C# namespace ambiguity, corrected at owner source.
- The GRSP headless `analyze` command reads complete native GRSP raw Developer/Runtime CSVs and genuine GCO PresentMon chronology; validates original CSV hash, monotonic QPC and F11 completion; preserves repeated callsite IDs as separate observations. Read-only source token/span/name diagnostics use current live `r6/scripts`, report `UNSAFE_OR_UNPROVEN` rather than approving transformations. Real private capture has **17,163 callsite rows / 17,137 distinct IDs / 26 repeated-ID observations**, 13,369 PresentMon frames. Full private replay of the *new CLI* is not yet proven by CI.
- **No GCO code was changed in this step.** GCO feature owns future owner-artifact invocation, cross-domain candidate planning and one final ZIP; GRSP owns generated REDscript. Preserve GCO main #438 native GRSP instrumentation/restore baseline while reconciling feature divergence. No new optimizer ZIP, full compiler run, applied pass, or gameplay gain claim.
- **Next:** prove GRSP CLI on real source/capture, expand R4 rule-specific admission to pristine/partial/complete/drift/dependency; validate source-wide reusable R5 pass and scc compilation before GCO R9 wiring.
