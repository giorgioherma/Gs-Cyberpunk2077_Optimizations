# CET + PresentMon GCO integration — active stage ledger

**Active development:** `feature/presentmon-resolver-integration`. **Stable branch:** `main` remains untouched by this plan. **Read together with:** [four-stage plan](CET-PRESENTMON-GCO-PLAN.md), [GCO handoff](../HANDOFF/CORE.md), [latest work](../LATEST-WORK.md).

**Last documented checkpoint:** 2026-11-15, Stage 1 source commit `e1916631e482587b1f64412c8aff105cdb0230d2` (Windows CI **NOT RUN**). **Baseline:** GCO v1.2.5, [main CI #383](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/43668057045) SUCCESS (308/308), source commit `c16d28965a7ed886ef313dc00313968c67a88b07`; docs-only main head `f7b8e6baa66ce363fd272f5cb8370548383e8bd2`. Branch fast-forwarded to that head. **Source-only Stage 1 implementation exists; its new Windows tests have NOT RUN.**

## Stage checkpoints

| Stage | State | Implemented and verified | Required next action |
| --- | --- | --- | --- |
| **1 — CET Resolver .NET10 package** | **IN PROGRESS** | Source commit `e191663`: source-owned Resolver publish + pinned native service + full hashed analysis-tool manifest + .NET8-vs-.NET10 real CLI regression in original workflow. **CI: NOT RUN; outputs unverified.** | Manually dispatch existing Windows CI on feature branch; fix actual build/CLI defects, verify owner parity on source-backed fixture, then test a known-good CET capture copy before PASS. |
| **2 — PresentMon → CET frame analysis** | **NOT STARTED** | Current GCO QPC F11-trimmed collection exists; CET report integration remains missing. | Define and implement official PresentMon 2.6.0-based frame contract, owner report/hitch/G-GC adaptation, independent numerical and failure tests. |
| **3 — Page 2 read-only analysis** | **NOT STARTED** | Existing Page 2 shell/Collect/Restore baseline only. | Run staged Resolver on sealed current capture and live CET sources; verify report, source trust, error/repeat behavior. |
| **4 — CET generator proof + RED handoff** | **NOT STARTED** | Existing standalone CET generator only; no GCO-integrated generation validated. | Test untouched/partial/completed output, idempotence, actual game-relative ZIP, safety and gameplay; record RED handoff; stop there. |

**Outside this feature:** REDscript optimization generator and final combined CET/REDscript release — **DEFERRED, NOT STARTED**, separate approval/plan.

## Update contract for every agent/session

1. Read this ledger and the full plan *before* editing source. State current stage, next unproven acceptance criterion and exact blocker. Do not overwrite these with assumptions from previous agent prose.
2. Update the table whenever a stage changes. Add a dated entry below for **what changed / what passed / what failed / what's next**, using precise commit SHA, CI run link and test names. Track separate **source-complete**, **Windows CI**, **actual output**, and **gameplay** verification; use `NOT TESTED` when appropriate.
3. Also update `docs/LATEST-WORK.md` promptly. Structural product/architecture changes go to the relevant handoff/decisions/contract files. Preserve this ledger's historical failures, reasons and resolution.
4. Never promote a stage to PASS until the plan's gate is met. On blockage mark BLOCKED and describe the exact safe recovery. On return to active work restore IN PROGRESS with a documented reason.
5. Finish by recording the **single next implementation action**, so a subsequent agent can continue without relearning the work.

## Dated entries

### 2026-11-15 — plan established; documentation-only baseline

- **Changed:** fast-forwarded feature branch to current GCO `main` head `f7b8e6baa66ce363fd272f5cb8370548383e8bd2` (already verified production code CI #383); recorded authoritative four-stage, CET-only scope and explicit REDscript stop line. No production implementation or CI/caching modifications.
- **Passed:** baseline main CI #383 already passed 308/308; feature branch begins with same source.
- **Not tested:** Resolver .NET10 packaging, PresentMon-to-CET handoff, Page 2 invocation, CET generation under GCO, real-game behavior.
- **Next:** Stage 1 only — inspect exact CET source/publish assets and implement source-owned Resolver baseline under one .NET10 runtime. Keep subsequent stages NOT STARTED until their gates.

### 2026-11-15 — Stage 1 implementation committed, Windows verification pending

- **Stage:** 1 IN PROGRESS. Source commit [`e191663`](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/commit/e1916631e482587b1f64412c8aff105cdb0230d2), branch `feature/presentmon-resolver-integration`. Protected `main` unchanged.
- **Changed:** reuse exact CET owner source `7f3489d8fd7e02d595f4a053616044cf2f6b2dab` already checked out by GCO; rebuild original `G.CETResolver.App` for shared .NET 10 alongside CET/GRSP managers; preserve original owner's semantic library, 0-Engine templates, native `GCETStaticLocations.dll` from pinned CET Resolver artifact [13317096524](https://github.com/giorgioherma/G-CET_Runtime_Profiler/actions/runs/41820765951) (SHA-256 `737bcdd9d6cbfedd9b238d16ae49c9fa0b52497e4e1e14cb606e826dd56c1496`), and untouched CET command-line generation implementation. Do **not** bundle old .NET 8 CLR; original artifact used as runtime/native source and CLI behavior oracle only.
- **Existing CI extension:** `.github/workflows/build-windows.yml` now acquires that exact matching CET Resolver owner artifact, invokes original `Build-Test.ps1 -Integrated`, packages resolver under `components/cet-resolver/app`, records all compiled/source asset hashes in `components/manifest.json.analysisTools` and full source provenance, and runs `tests/GTotalOptimizer.Core.Tests/Verify-CetResolverPackage.ps1`. New test compares source-backed measured-callback results against original standalone owner EXE; checks one CLR, runtime/manifest, immutable source/handoff bytes, all assets and explicit error behavior.
- **Verification:** source blobs/commit written to correct feature branch. **Windows compilation: NOT RUN. New owner equivalence regression: NOT RUN. Complete portable artifact: NOT VERIFIED. Real-gameplay behavior: NOT TESTED.** Existing independent `main` CI #383 success is baseline only, **not** proof for this branch.
- **Next single action:** manually dispatch the **existing** `Gs-Cyberpunk2077_Optimizer` workflow against `feature/presentmon-resolver-integration`, inspect exact job/log/output, correct any real failures at source, update this ledger and `docs/LATEST-WORK.md` with run URL/SHA/result. Keep stages 2–4 NOT STARTED and do not merge to `main`.
