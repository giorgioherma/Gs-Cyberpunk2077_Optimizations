# CET + PresentMon GCO integration — active stage ledger

**Active development:** `feature/presentmon-resolver-integration`. **Stable branch:** `main` remains untouched by this plan. **Read together with:** [four-stage plan](CET-PRESENTMON-GCO-PLAN.md), [GCO handoff](../HANDOFF/CORE.md), [latest work](../LATEST-WORK.md).

**Last documented checkpoint:** 2026-11-15, Stage 1 verified full feature CI #385 on `444518c7342a0f6ca9d55274ac821345f8c067b4` (**SUCCESS**). **Baseline:** GCO v1.2.5, [main CI #383](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/43668057045) SUCCESS (308/308), source commit `c16d28965a7ed886ef313dc00313968c67a88b07`; docs-only main head `f7b8e6baa66ce363fd272f5cb8370548383e8bd2`. Branch fast-forwarded to that head. **Stage 1 Windows package and owner CLI fixture verified in CI #385; real collected CET capture replay against a current/effective mod installation is not yet tested.**

## Stage checkpoints

| Stage | State | Implemented and verified | Required next action |
| --- | --- | --- | --- |
| **1 — CET Resolver .NET10 package** | **IN PROGRESS** | Source `e191663` and canonical **CI #385 SUCCESS** on `444518c`: **308/308** original contracts, compiled .NET10 Resolver versus owner .NET8 CLI behavior, pinned assets/native service, single CLR, complete WPF/tray/restore and artifact **14146380024**. | Replay a representative **real CET capture** in a private disposable folder against available representative live CET source, record truthful source mapping/report, preserve capture hashes. Only then mark Stage 1 PASS. |
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

### 2026-11-15 — CI #384 red: expected negative CLI exit leaked into workflow; source fixed, rerun pending

- **Observed failure:** [feature CI #384](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/43672252005) on `5ab64ef7e0a46e28d3f9e724763739468817b251` **FAILED** at `Test source contracts and verify the final downloadable package`. The package, **308/308** previous core contracts and the new actual owner-vs-GCO Resolver CLI comparison **all reported PASS**. Failure immediately followed the Resolver smoke's PASS and was `Packaged CET Resolver owner-equivalence contract failed` from its calling workflow guard.
- **Root cause:** `Verify-CetResolverPackage.ps1` intentionally invokes Resolver with a missing capture and missing game source, checks that both produce `{"ok":false}` and nonzero exit, but left PowerShell's global `$LASTEXITCODE=1` from the second expected rejection. The calling workflow checked that stale native exit code and reported a false negative. No observed owner-CLI, source template, manifest, installer/restore or runtime defect.
- **Fix:** [`a4d7de5a311de1a26f68971cbbbf4baf569638ea`](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/commit/a4d7de5a311de1a26f68971cbbbf4baf569638ea) resets `$global:LASTEXITCODE=0` **only after the required nonzero/error-JSON negative-case assertions complete**. All existing positive/negative checks remain required; a real exception still fails the script. No production code, owner components, pipeline structure, caching or UI changed.
- **Verification:** CI #384 establishes Windows .NET10 Resolver publish, owner asset/hash/one-CLR proofs, real measured callback report, owner equivalence, missing-input rejection and existing 308 checks. **Patch itself not yet CI verified**; complete post-contract WPF/tray/package tests in the job did **not** run because CI stopped. **Stage 1 remains IN PROGRESS**, not PASS. No user gameplay proof.
- **Next single action:** manually rerun the **same** full workflow on the feature branch after fix, inspect exact run, publish result and artifact; then validate the Stage 1 representative real CET capture in a disposable copy before declaring PASS. Keep stages 2–4 NOT STARTED.

### 2026-11-15 — Full CI #385 SUCCESS; Stage 1 packaged baseline verified, real capture gate pending

- **Verified:** canonical [Windows CI #385](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/43674390992), branch `feature/presentmon-resolver-integration`, exact tested source `444518c7342a0f6ca9d55274ac821345f8c067b4`, **SUCCESS**. Full original **308/308** installation/CLI/package contracts pass; the complete .NET10 CET Resolver is compiled from pinned source and its original owner .NET8 CLI comparison, measured callback fixture, hash-indexed owner knowledge/runtime/native assets, null/invalid input refusal and unchanged source evidence all **PASS**.
- **Broader package checks passed:** one private `.NET 10.0.15` Core/Desktop runtime; actual CET/GRSP owner CLI and restore; supported REDscript compiler reference; native PresentMon observer / recovered missing writer; actual WPF Normal-Maximized-Normal / both pages; native tray auto-reopen after 10 seconds; normal X process termination; packaged launchers and payload manifest.
- **Artifact:** `Gs-Cyberpunk2077_Optimizer-v1.2.5-run385-a1`, ID **14146380024**, 89,802,577 bytes, [download](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/43674390992/artifacts/14146380024). Version unchanged. Compiler showed existing CS8604 possible-null WPF warning; no failing checks. The failed #384 expected-negative-exit condition is fixed and verified; preserve history.
- **Still unverified:** replay of an actual collected CET capture with a representative currently effective game CET source stack, correct real owner inventory, and any **in-game gameplay**. An older real capture exists in a provided G-CET archive, but its presence alone is **not** a replay using the new GCO package, and it is not an install inventory. Do not publish private capture or mods to CI.
- **Stage status:** 1 **IN PROGRESS** until the real-capture acceptance gate; stages 2/3/4 **NOT STARTED**. Full Windows CI success is not permission to claim Stage 2 PresentMon integration, Page 2 wiring, generated pass or gameplay validation.
- **Next single action:** run the newly packaged Resolver on a **disposable copy of a representative real CET capture** with a source-confirmed compatible CET mod tree; inspect report and verify input/source hashes and provenance, then record Stage 1 gate result. Do not begin Stage 2 before Stage 1 is proved.
