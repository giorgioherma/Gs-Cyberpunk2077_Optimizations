# Implementation plan — documentation first, then app

**Product authority:** [accepted two-page workflow](product/UNIFIED-TWO-PAGE-WORKFLOW.md) and [explicit current decisions](product/CURRENT-DECISIONS.md). This plan is not authorization to bypass their acceptance gates.

## Milestone 0 — canonical documentation and agent continuation (current)

- [x] Locate original October 16 design on CET `dev/native-static-anchors-experimental`.
- [x] Preserve original accepted design unedited in this repository; record source Git blob.
- [x] Create unified product `main` documentation, source ownership, contract inventory and agent startup.
- [ ] Verify/document current CI/binary identities, payload packaging, licenses and environment with an actual build.
- [ ] New app code: **not started**.

## Milestone 1 — minimal, real application shell

- [x] Create one .NET 10 WinForms desktop project and initial Windows CI (source committed; CI/Windows runtime verification tracked in LATEST-WORK).
- [x] Implement the **first read-only Page 1/Page 2 shell** and original action labels; no Page 2 unlock without real capture evidence. Current game root persists under the user's LocalAppData.
- [~] Read-only detect game, CET source, RED4ext directory, REDscript source, 0-Engine and G-REdruntime source **as unverified presence**; full native/API health proof and compatibility still pending.
- [~] Implement optional standalone CET/GRSP known-path `--status` adapters and separate JSON parsing; no backend binaries bundled or status-ready claim until verified.
- [~] Add core test harness for source paths, missing backends and domain-specific JSON error handling; real Windows process invocation and package staging still pending.
- **Acceptance:** app runs and accurately displays status with both, either or neither profiler. No pretend "Install succeeded", generated ZIP, or measured report.

[Shared .NET 10 Desktop Runtime packaging contract](technical/SHARED-NET-RUNTIME.md): single private runtime for final integrated managers and CapFrameX is **not yet validated**.

## Milestone 2 — verified packaging, reversible profiler install and capture

- [ ] Build/package **verified current** CET Profiler, CET Resolver, GRSP and optional CapFrameX companion (if license/download verified) with component manifest hashes and provenance; no legacy TOTAL binaries.
- [ ] Coordinate CET + GRSP standalone `--install` / `--collect` / `--restore`, including failed second install and interrupted sessions. Never duplicate underlying file mutation logic.
- [ ] Launch game and configured CapFrameX; explain current F11 capture and optional F10 controlled benchmark without changing existing controls blindly.
- [ ] Track one uniquely identified session; detect started/ended/archived current data. Continue remains disabled if evidence incomplete/stale.
- [ ] Archive and verify profiler evidence before clearing temporary live captures, then restore both native layers **before normal Page 2 generation**. Require explicit recovery on failed restore.
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
