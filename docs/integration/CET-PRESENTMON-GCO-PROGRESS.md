# CET + PresentMon GCO integration — active stage ledger

**Active development:** `feature/presentmon-resolver-integration`. **Stable branch:** `main` remains untouched by this plan. **Read together with:** [four-stage plan](CET-PRESENTMON-GCO-PLAN.md), [GCO handoff](../HANDOFF/CORE.md), [latest work](../LATEST-WORK.md).

**Last documented checkpoint:** 2026-11-15. **Baseline:** GCO v1.2.5, [main CI #383](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/43668057045) SUCCESS (308/308), source commit `c16d28965a7ed886ef313dc00313968c67a88b07`; docs-only main head `f7b8e6baa66ce363fd272f5cb8370548383e8bd2`. Branch fast-forwarded to that head. **No implementation work, new Resolver tests or feature CI runs claimed.**

## Stage checkpoints

| Stage | State | Implemented and verified | Required next action |
| --- | --- | --- | --- |
| **1 — CET Resolver .NET10 package** | **NOT STARTED** | No Resolver integration yet. Existing GCO CET/GRSP/PresentMon packaging and shared runtime baseline are green on main. | Reproduce owner Resolver CLI and source assets from pinned CET checkout in a disposable .NET10 GCO component; prove baseline output and unmodified captures. |
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
