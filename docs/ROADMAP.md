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

- [ ] Combine CET + GRSP reports with trusted time alignment and optional CapFrameX frames; display hitch/tail, allocations where measured and source-specific costs. Do not sum overlapping CPU domains.
- [ ] On Page 2 entry auto-run CET Resolver using collected current evidence and read-only **live** mod sources. Show understandable optimized/already/not-beneficial/unsupported/unsafe statuses and reasons.
- [ ] Integrate existing CET generated pass unchanged, source-proven, in an app workspace. No new app-owned CET semantic rules.
- **Acceptance:** one game-root input; accurate current mod inventory; no altered capture attribution; unchanged tested CET output and stage order.

## Milestone 4 — REDscript generic-first optimizer

- [ ] Audit GRSP full source/callsite CSVs for the **first** generic rule's actual proof requirements; extend GRSP independently only if measured evidence is missing.
- [ ] Implement separate REDscript source parser/admission/generator library; support original, partial and fully optimized installed source and reject unproven drift.
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
