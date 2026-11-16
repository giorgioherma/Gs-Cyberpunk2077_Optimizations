# Implementation plan — documentation first, then app

**Product authority:** [accepted two-page workflow](product/UNIFIED-TWO-PAGE-WORKFLOW.md) and [explicit current decisions](product/CURRENT-DECISIONS.md). This plan is not authorization to bypass their acceptance gates.

## Current REDscript milestone — 2026-11-16 (GCO-local development branch)

- **GCO branch:** `dev/redscript/resolver_handoff`, forked from completed REDscript integration work; stable `main` protected. **GRSP development:** `dev/resolver-profiler-handoff` remains for native profiling/instrumentation and original capture tools only; old managed resolver prototype is migration reference. See [current GCO-local handoff](integration/REDSCRIPT-RESOLVER-LOCAL-HANDOFF.md).
- **Canonical stages R0–R10:** [complete source audit and implementation blueprint](integration/GRSP_REDscript_Resolver_Blueprint.md); [live progress/CI ledger](integration/REDSCRIPT-RESOLVER-PROGRESS.md). This supersedes the historical CET-only `STOP HERE` language below **on this new feature branch**. The CET scope stopped correctly; the REDscript milestone is now independently approved.
- **Current:** New GCO-local ownership/branch documented, managed source and tests now moved into GCO with [local full CI #483 SUCCESS](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/43798781332), proving 41 managed tests, 2,319 real REDscript sources and dual SCC compilation, and 323 package contracts. GRSP's redundant managed backend/CI was retired on its dev branch at `a2fe918`; native profiler unchanged. On the previously verified GRSP managed prototype, R2/R3 evidence replay, one R4/R5 strictly proven preview and real R8 baseline/optimized compilation have passed in GCO CI #482; R4 source-wide rejection evidence refined in GCO CI #486 (40 member-access, 4 reversed-shape, 2 other left, 1 return, 1 exact); semantic/type proof, broader generics, R7 and product R9/R10 not complete. R1 may privately inventory the 61 files, but the **implementation order is fresh captures → full-stack generic discovery → generic passes first → validated generic-output coverage gap against the 61-file reference → only proven semantic remainder**. Never make 61 semantic rules first; optional GRSP/G-REDruntime evolution may enable generic passes. Start with fresh CET+GRSP+PresentMon evidence. GCO owns a separate local REDscript parser/resolver/generator/compiler component while GRSP continues producing native measurements; GCO preserves its consolidated profiler reporting and merges one ZIP. No functionality, compiled REDscript or gameplay acceptance implied by this documentation.
- **Baseline acceptance:** CET source-backed generated ZIP/partial/idempotence passed CI #416; GCO real consolidated three-profiler PresentMon report passed CI #426. Preserve original UI, Capture/Restore, fixed CET stages, existing Windows workflow and one private .NET10 runtime. Manual source generation replay remains milestone-only.
- **After each substantive change/decision:** update `docs/integration/REDSCRIPT-RESOLVER-PROGRESS.md` and `docs/LATEST-WORK.md` promptly with tested commit/CI and next action; cross-link GRSP `docs/RESOLVER_PROGRESS.md` and `docs/LATEST-WORK.md` as source ownership evolves.

## Historical CET scoped implementation — 2026-11-15 (CET only; completed)

**Branch:** `feature/presentmon-resolver-integration`; `main` is preserved at the successful CI #383 baseline. **Authoritative stage gates:** [four-stage implementation plan](integration/CET-PRESENTMON-GCO-PLAN.md); **current stage/status:** [live progress ledger](integration/CET-PRESENTMON-GCO-PROGRESS.md). These replace older speculative "next" instructions for this specific task; the original product milestones below remain the eventual target.

1. **IN PROGRESS (full CI #385 SUCCESS, real capture pending):** original CET Resolver packaged under one .NET10 runtime, original owner-versus-GCO CLI comparison passes, 308/308 core contracts and complete GCO artifact uploaded. Replay real compatible CET capture/source before final Stage 1 PASS.
2. **NOT STARTED:** present the real F11-aligned PresentMon frames to CET report/hitch/G-GC logic with measured semantics and safe missing-data behavior.
3. **NOT STARTED:** connect sealed GCO results and live CET source to Page 2's read-only Resolver analysis.
4. **NOT STARTED:** prove actual CET generated output/source safety and produce the REDscript integration handoff. **STOP HERE**.

**REDscript generator and the final combined CET+REDscript ZIP are separate later projects, not steps inside this four-stage map.** All development CI uses the existing manually dispatched full Windows workflow; agents must promptly update the progress ledger, `docs/LATEST-WORK.md`, and relevant decisions/handoffs before calling a stage complete.

## Milestone 0 — canonical documentation and agent continuation (current)

- [x] Locate original October 16 design on CET `dev/native-static-anchors-experimental`.
- [x] Preserve original accepted design unedited in this repository; record source Git blob.
- [x] Create unified product `main` documentation, source ownership, contract inventory and agent startup.
- [x] Verify/document current owner binary identities and actual integrated .NET10 portable packaging in Windows CI; gameplay verification remains separate.
- [x] New two-page WinForms shell and CET/GRSP session coordinator implemented on `main`; actual gameplay and optimizer integration pending.

## Milestone 1 — minimal, real application shell

- [x] Create one .NET 10 WinForms desktop project and initial Windows CI (**successful**; see LATEST-WORK for exact run and artifact).
- [x] Implement the **first read-only Page 1/Page 2 shell** and original action labels; no Page 2 unlock without real capture evidence. Current game root persists under the user's LocalAppData.
- [~] Read-only detect game, CET source, RED4ext directory, REDscript source, 0-Engine and G-REdruntime source **as unverified presence**; full native/API health proof and compatibility still pending.
- [x] Bundle verified CET/GRSP owner managers and parse their actual independent status JSON. Live installed-state checks now drive GUI controls; in-game checks are still pending.
- [x] Run 121 offline contracts; Windows CI launches both source-owned managed CLIs under shared .NET10 and validates isolated GRSP install/restore. Interactive GUI/gameplay not yet tested.
- **Acceptance:** app runs and accurately displays status with both, either or neither profiler. No pretend "Install succeeded", generated ZIP, or measured report.

[Shared .NET 10 Desktop Runtime packaging contract](technical/SHARED-NET-RUNTIME.md): one private runtime verified for integrated GCO/CET/GRSP; CapFrameX remains optional and outside the normal package.

## Milestone 2 — verified packaging, reversible profiler install and capture

- [~] Package verified CET Profiler and GRSP, plus PresentMon, with payload SHA manifests; CET Resolver remains a later milestone. No legacy TOTAL binaries.
- [~] Coordinate CET + GRSP owner `--install` / `--collect` / `--restore` in the GUI, with pre-install journal and failed-second-owner recovery. Needs actual partial/crash game validation; never duplicate owner mutations.
- [~] GUI launches the game and elevated PresentMon with F11; preserves existing CET F10/F11 choices. Validate the hotkey interaction and update remaining CapX UI text.
- [~] Journal one owner-installed session and record archive paths/optional PresentMon CSV. Validate real synchronized capture windows before trusting full correlation.
- [~] CET/GRSP owner collection and restoration wired; Page 2 requires verified completed capture and restore. Test real interrupted/recovery and raw report completeness.
- **Acceptance:** actual end-to-end game run + crash/partial rollback tests, not only a DLL on disk. CapFrameX absent is honest optional status.

## Milestone 3 — unified analytics/report + CET Resolver

- [ ] Implement **one GCO cross-domain analysis and optimization plan** from CET+GRSP measurements, optional PresentMon frame data and verified live sources. Prioritize compatible measured work without double-counting overlapping timing; identify dependencies, already-optimized owners and conflicts.
- [ ] On Page 2 entry auto-run CET Resolver using collected current evidence and read-only **live** mod sources. Show understandable optimized/already/not-beneficial/unsupported/unsafe statuses and reasons.
- [ ] Integrate the **existing authoritative CET Lua Resolver/Generator** without relocating/reimplementing its rules or G-GC. Source-safe CET candidate/selection integration changes, if truly needed, are implemented in CET at source; preserve fixed generation order.
- **Acceptance:** one game-root input; accurate current mod inventory; no altered capture attribution; unchanged tested CET output and stage order.

## Milestone 4 — REDscript generic-first optimizer

- [ ] Audit GRSP full source/callsite CSVs for the **first** generic rule's actual proof requirements; extend GRSP independently only if measured evidence is missing.
- [ ] Migrate and integrate the **separate GCO-local REDscript Resolver/Generator/SCC component** using genuine CET+GRSP+PresentMon evidence and existing conservative source admission. Support untouched, partially optimized and fully optimized live source; reject unproven drift. **GRSP retains native instrumentation only**; GCO owns managed REDscript generation, compilation, orchestration and final ZIP.
- [ ] Start with a very small number of safe broadly applicable generic transforms; no forced semantic catalog. Preserve public REDscript signatures, call/registration order, lifetimes and game invariants.
- [ ] Compose and validate **full effective REDscript source stack** through supported compiler and dependency checks; no guessing about optional mods or implicit G-REdruntime dependency.
- **Acceptance:** real untouched-source one-pass, no errors/changed behavior in regression probes, idempotence, compiler success and matched GRSP performance on selected features. Note gameplay limits honestly.

## Milestone 5 — one clean ZIP, Vortex/recovery, release

- [ ] Combine independently proven CET/REDscript/native locations into one **game-root-relative changed-files-only ZIP**, no profiler/report/docs/PDBs, no wrapping folder.
- [ ] Include shared native services only if accepted owners use them; verify every dependent file and emit owner dependency/exclusion manifest outside ZIP.
- [ ] Support advanced per-owner exclude and clean regeneration (not manual destructive rewrite), clarify Vortex conflict-winning order and manual backup guidance.
- [ ] Test Vortex topology against actual deployment and rollback. Validate partial/manual installation recovery separately; do not promise Vortex can restore overwritten manual files.
- [ ] Build release through CI, verify all binary/payload hashes/rights, install/restore, redscript compile and user in-game validation. Release only with scoped known limits.

**Do not automatically bump CET/GRSP versions or release/tag their repos as a side effect of this product.** Maintain docs/LATEST-WORK.md with evidence at each milestone.

## Next authorized implementation sequence — 2026-11-06 (PENDING)

1. Build [approved Page 1](design/PAGE1-APPROVED-IMPLEMENTATION-PLAN.md) for Direct installations: full V trail, BLUE skyline, Game Root Refresh, global source-owner counts and 3-stage glitch visual.
2. Refactor per-domain CET/GRSP eligibility/owner install/collect/restore and GCO 0-Engine validation; block PresentMon-only Continue. Add partial expected-results Return/Continue and provenance-safe retry, with verified restoration before Page 2.
3. Add tests to the **existing** core contract harness and run only the **single** Windows workflow; real CET-only, GRSP-only and both-domain game capture tests required. Keep v1.1.6.
4. MO2 runtime later: standard one extra folder + automatic INI/profile; exceptional custom source folder repair in advanced window; USVFS/Overwrite/output/restore proof required before enabling.

## User-approved frontend migration — 2026-11-06

- [x] Switch GCO's running two-page GUI to native **.NET 10 WPF** while keeping the old backend and one shared runtime. Main window, real buttons, no replacement app/CI, original headless package verification and PresentMon preserved. **[Integrated Windows CI #42277190679](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/42277190679): 179/179 green.**
- [ ] Commit the exact user V original silhouette/full-color pair and BLUE skyline, embed canonical master ICO, test WPF actual window, 100/125/150% DPI and game-focused animation overhead; confirm Windows capture/recovery behavior.
- [ ] Complete independently eligible CET-only/GRSP-only backend workflows and exact 0-Engine v0.17.2+ proof; retain original owner restorers and strict native evidence admission. MO2 integration and optimization generators remain separate later milestones.

## 2026-11-07 — Compact Capture UI progress

- [x] First-pass source implementation of compact **1220×680 DIP** WPF window, **1040×600 DIP min**, Rajdhani/Oxanium, whole-width V, lowered true blue skyline, centered parallelogram tabs, GOT LAG? headline, three enlarged in-card workflow controls, console and vector special Continue. See [layout contract](design/PAGE1-COMPACT-WPF-LAYOUT.md).
- [ ] Finish integrated Windows build [#42324708424](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/42324708424), inspect actual 100/125/150% DPI screenshots, verify font rendering, scrolling, no cut content, frame overhead and real CET/GRSP capture/restore. Geometry/style code has not yet been user visually approved.
- [ ] Continue independent CET-only/GRSP-only backend gating and precise 0-Engine v0.17.2+ admission separately, without duplicating owner managers.
