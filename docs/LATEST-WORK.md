# Latest work / evidence status

**Date:** 2026-11-04. **Repository:** `giorgioherma/Gs-Cyberpunk2077_total_optimization_generator_v2.31`. **Branch:** `main`.

## Completed — documentation and planning only

- Located the actual **2026-10-16 user-approved design** on CET `dev/native-static-anchors-experimental` at `docs/product/UNIFIED-TWO-PAGE-WORKFLOW.md`. Source Git blob: `f4925759fc612311b72432b329708b84b9dd279e`. It prescribes one game-root input, two pages, three first-page actions, current-session capture gating, reversible profiling, one deployable ZIP, Vortex/rollback safety, MO2 disclaimer and generic-first high-value optimizations.
- The unified product's initial repository was created and a README committed. The user then explicitly decided **this repo works on `main`, not a separate handoff branch**.
- Reviewed the CET and GRSP source headless entry points and project ownership. CET code remains in CET repo; GRSP instrumentation in GRSP; G-REDruntime in its own repo; new REDscript generic generator and unified UI here.
- Added authoritative original design, explicit delta, architecture, exact CLI/summary contracts, milestone roadmap, mandatory agent startup and documentation index.

## App foundation — source and Windows CI VERIFIED, gameplay/GUI still not validated

- Selected **.NET 10 WinForms** for the new unified app; no downgrade to older CapFrameX. Verified current CET Profiler/CET Resolver/GRSP manager projects target .NET 8 and that CapFrameX 1.9.1.2 Beta requires .NET 10 Desktop. Shared private runtime across **all** processes remains unproven; see [runtime contract](technical/SHARED-NET-RUNTIME.md).
- Added a two-page read-only desktop shell, one game-root input, basic source presence states, optional executable-path detection and distinct CET/GRSP CLI status JSON handling. Install, capture, continue and ZIP generation are disabled. The shell cannot proceed without validated current-session capture proof.
- Added Windows CI with .NET 10 self-contained build, one-runtime-in-shell check and a no-dependency test harness. **COMPLETED SUCCESS: [Windows CI #41859261728](https://github.com/giorgioherma/Gs-Cyberpunk2077_total_optimization_generator_v2.31/actions/runs/41859261728)**, source SHA `fa9309de92fa2022b784e78b0cd4e7ae61e1f1b1`, **14 read-only core contracts PASS**, WinForms win-x64 publish PASS, package checked for exactly one `coreclr.dll` and no unverified CET/GRSP/CapFrameX binaries. Uploaded artifact `G-Total-Optimizer-Shell-win-x64` **13339416350**. This proves the build and contract tests, **not** an actual UI launch, profiler status response from real current binaries, any game runtime or shared .NET packaging of all components.

## Not implemented / not validated

- The .NET 10 shell build and offline tests are CI-verified; **interactive UI launch on user's Windows PC remains unverified**. No verified component-bundle manifest, unified session controller, synchronized three-producer capture, combined report or combined optimization ZIP exists.
- No new REDscript generic source transformation or compiler validation implemented.
- No test of install/restore across both real current native profilers; no new verified game world-load/gameplay acceptance.
- No approved transfer of old TOTAL binary components. Unused old experimental branch was created before the user clarified main-only ownership; this branch is **not** the intended development target.

## Next work (after user acceptance of documentation)

**Milestone 1 continuation:** Windows CI and 14 offline contracts are green. Next verify interactive shell launch and actual standalone CET/GRSP status against real packages, then stage independently verified managers, without starting installation or rewriting until restore safety is complete.

**Active blockers before implementation claims:** actual current standalone component artifacts and API schema under installed builds must be checked against source; CapFrameX packaging licensing/provenance and cross-profiler session synchronization have not been validated. MO2 effective mod discovery is unsupported until a separate proven provider exists.

When implementation starts, append exact commits, build jobs, test output and gameplay evidence to this file; preserve separate completion labels for implementation, CI/source, compilation and gameplay.


## 2026-11-04 — CET-style two-page redesign implementation (CI pending at commit)

- User authorized replacing the original light shell with CET v1.3.4 colors and an updated **two-page / no-tab** workflow: Source + Capture → Analytics & Pass Generation. Page 1 displays one root-or-CET-mods chooser, instructions and live key evidence; Page 2 uses separate CET and REDscript scrollable text reports and explicitly requested **Analyze / Generate / Open Results** buttons.
- Added exact physical root recognition and immediate \`init.lua\` CET owner-count detection. Direct canonical CET mods paths resolve the same game exe; external/MO2 source roots are **source-only**, never a claim of resolved VFS, game executable, profiler readiness or deployable generation.
- Added non-destructive CET F11 / custom-key inspection, REDscript F11 availability hint and CapFrameX \`portable.json\`/AppSettings \`CaptureHotKey\` inspection; mismatch/absence is not green or silently edited.
- Added contract tests for direct/external CET source, missing init.lua, F11/custom/corrupt bindings, CapFrameX F11/custom/missing. Kept real install/collect/restore, page-2 unlock and generation safely gated: no verified component provenance, current-session evidence or REDscript compiler integration yet. This milestone **does not** claim those actions work, nor a real UI launch.
- Original October 16 workflow remains preserved verbatim. New current-decision delta authorizes visible Page 2 Analyze and distinct Page 1 collect/restore steps.
- **CI evidence:** pending; must append actual run ID, conclusion, test count and artifact before calling this verified.
