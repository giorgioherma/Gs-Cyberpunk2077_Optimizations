# Implementation plan — documentation first, then app

**Product authority:** [accepted two-page workflow](product/UNIFIED-TWO-PAGE-WORKFLOW.md) and [explicit current decisions](product/CURRENT-DECISIONS.md). This plan is not authorization to bypass their acceptance gates.

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
- [ ] Implement the **new GCO-owned REDscript Resolver/Generator** using combined CET+GRSP evidence and source-safe REDscript parsing/admission. Support untouched, partially optimized and fully optimized live source; reject unproven drift. GRSP continues to own REDscript instrumentation, not the generator.
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
