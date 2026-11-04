# Latest work / evidence status

**Date:** 2026-11-04. **Repository:** `giorgioherma/Gs-Cyberpunk2077_total_optimization_generator_v2.31`. **Branch:** `main`.

## Completed — documentation and planning only

- Located the actual **2026-10-16 user-approved design** on CET `dev/native-static-anchors-experimental` at `docs/product/UNIFIED-TWO-PAGE-WORKFLOW.md`. Source Git blob: `f4925759fc612311b72432b329708b84b9dd279e`. It prescribes one game-root input, two pages, three first-page actions, current-session capture gating, reversible profiling, one deployable ZIP, Vortex/rollback safety, MO2 disclaimer and generic-first high-value optimizations.
- The unified product's initial repository was created and a README committed. The user then explicitly decided **this repo works on `main`, not a separate handoff branch**.
- Reviewed the CET and GRSP source headless entry points and project ownership. CET code remains in CET repo; GRSP instrumentation in GRSP; G-REDruntime in its own repo; new REDscript generic generator and unified UI here.
- Added authoritative original design, explicit delta, architecture, exact CLI/summary contracts, milestone roadmap, mandatory agent startup and documentation index.

## App foundation — source committed (Windows CI outcome pending)

- Selected **.NET 10 WinForms** for the new unified app; no downgrade to older CapFrameX. Verified current CET Profiler/CET Resolver/GRSP manager projects target .NET 8 and that CapFrameX 1.9.1.2 Beta requires .NET 10 Desktop. Shared private runtime across **all** processes remains unproven; see [runtime contract](technical/SHARED-NET-RUNTIME.md).
- Added a two-page read-only desktop shell, one game-root input, basic source presence states, optional executable-path detection and distinct CET/GRSP CLI status JSON handling. Install, capture, continue and ZIP generation are disabled. The shell cannot proceed without validated current-session capture proof.
- Added Windows CI with .NET 10 self-contained build, one-runtime-in-shell check and a no-dependency test harness; production runtime and multi-component packaging remain separate future work. Commit and CI evidence will be recorded after completed build.

## Not implemented / not validated

- The new UI and build definitions exist **in source**, but Windows CI outcome and launch remain unverified until Actions completes. No verified component-bundle manifest, unified session controller, synchronized three-producer capture, combined report or combined optimization ZIP exists.
- No new REDscript generic source transformation or compiler validation implemented.
- No test of install/restore across both real current native profilers; no new verified game world-load/gameplay acceptance.
- No approved transfer of old TOTAL binary components. Unused old experimental branch was created before the user clarified main-only ownership; this branch is **not** the intended development target.

## Next work (after user acceptance of documentation)

**Milestone 1: app shell verification**. Run Windows CI to completion, fix compile/packaging defects in the app source, verify missing/read-only CET/GRSP status semantics and optional later component staging. No installation or rewriting until read-only checks are reliable.

**Active blockers before implementation claims:** actual current standalone component artifacts and API schema under installed builds must be checked against source; CapFrameX packaging licensing/provenance and cross-profiler session synchronization have not been validated. MO2 effective mod discovery is unsupported until a separate proven provider exists.

When implementation starts, append exact commits, build jobs, test output and gameplay evidence to this file; preserve separate completion labels for implementation, CI/source, compilation and gameplay.
