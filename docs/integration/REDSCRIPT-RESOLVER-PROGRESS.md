# GCO REDscript Resolver integration — active R0–R10 progress ledger

**Active GCO branch:** `feature/redscript-resolver-integration`, forked from finished `feature/presentmon-resolver-integration` at `3caccafbdf39ec2b6474b69ed8f34c9392e2ff20` (2026-11-15). **Protected GCO `main`:** `f7b8e6baa66ce363fd272f5cb8370548383e8bd2`.
**Canonical full blueprint:** [GRSP-owned REDscript Resolver blueprint](GRSP_REDscript_Resolver_Blueprint.md).
**Authoritative REDscript code branch:** [GRSP `dev/resolver-profiler-handoff`](https://github.com/giorgioherma/GRSP-G-REDscript-Profiler/tree/dev/resolver-profiler-handoff), [owner handoff](https://github.com/giorgioherma/GRSP-G-REDscript-Profiler/blob/dev/resolver-profiler-handoff/docs/RESOLVER_HANDOFF.md), [owner progress](https://github.com/giorgioherma/GRSP-G-REDscript-Profiler/blob/dev/resolver-profiler-handoff/docs/RESOLVER_PROGRESS.md); GRSP ownership/docs commit `b82e73cfe1bf43389cbc35cf9c65bd7a312ab5f0`.

## Stage register

| Stage | Status | Exit evidence required |
| --- | --- | --- |
| R0 branch/ownership handoff | DOCUMENTATION COMPLETE | New GCO branch, full canonical blueprint, GRSP+GCO AGENTS/handoffs/decisions/ledgers aligned; no code implementation |
| R1 private reference inventory only | NOT STARTED | Index 61 paths/owners/helpers/provenance privately; **no semantic recipe implementation** |
| R2 fresh three-profiler measurement | NOT STARTED | New CET+GRSP+PresentMon captures, native quality/trust, actual current `r6/scripts`, measured ranking |
| R3 GRSP Resolver evidence contract | NOT STARTED | Genuine GCO PresentMon chronology / NOT_CAPTURED, complete GRSP raw data, owner JSON and negative tests |
| R4 structural REDscript source admission | NOT STARTED | Pristine/partial/already-done/drift/dependencies; conservative parser; no regex-only safety |
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
