# Agent instructions — unified G's Cyberpunk 2077 optimizer

**Repository:** `giorgioherma/Gs-Cyberpunk2077_total_optimization_generator_v2.31`. **Work on `main` only** unless the user explicitly changes that decision. Do not assume the historically created `dev/resolver-profiler-handoff` ref in THIS repository is the active branch.

## Mandatory startup

Read, in order:
1. `docs/HANDOFF/CORE.md` (current boundaries and startup).
2. `docs/product/UNIFIED-TWO-PAGE-WORKFLOW.md` (**verbatim 2026-10-16 user-approved UX/product contract**).
3. `docs/product/CURRENT-DECISIONS.md` (later explicit decisions; only these override the original).
4. `docs/LATEST-WORK.md` then `docs/ROADMAP.md` (actual completion state / next milestone).
5. `docs/technical/ARCHITECTURE.md` and `docs/technical/COMPONENT-CONTRACTS.md`.
6. Before touching a source component, its **own** repository's `AGENTS.md` / handoff / integration docs and current checked-out implementation. Never substitute an old TOTAL snapshot for these.

**Current checkpoint:** GCO v1.1.5 integrated CET + GRSP + bundled PresentMon; last verified Windows integrated CI [#42125370146](https://github.com/giorgioherma/Gs-Cyberpunk2077_total_optimization_generator_v2.31/actions/runs/42125370146) passed **153/153 contracts** at code SHA `6b76fe60659bb392319508124f5572dc09bec23d` (artifact `G-Total-Optimizer-v1.1.5-Integrated-win-x64`, ID `13460736516`). PresentMon no-hotkey launch and Button 3 collection have CI/source proof only; real gameplay remains unverified. Always read `docs/LATEST-WORK.md` and inspect newer `main`/CI before relying on this checkpoint.

## CI workflow ownership — one active integrated pipeline (mandatory)

- **ONLY current shipping/build pipeline:** `.github/workflows/build-windows.yml`, GitHub Actions display name **Gs-Cyberpunk2077_Optimizer**. This builds and publishes the actual two-page GCO v1.1.5 app using CET, GRSP, PresentMon and **one shared .NET 10 runtime**. It runs on relevant `main` source/workflow changes and supports manual dispatch.
- **Do not create duplicate workflows, new build lanes, separate test bundles, parallel "experimental integrated" workflows, or replacement scripts for routine fixes.** Extend the existing `build-windows.yml` steps and `tests/GTotalOptimizer.Core.Tests` in place. Reuse the original component owner managers/builders; fix errors at source.
- **Retired obsolete test:** `.github/workflows/three-profiler-test.yml` (former display name **Build all-three CET GRSP PresentMon capture test**) was a standalone *manual triple-test* with PresentMon registering F11. It was superseded by the integrated GCO workflow and has been removed. **Do not recreate or dispatch it.** Old artifacts/runs are historic, not current downloadable app builds.
- **CRITICAL shared script:** `integrations/three-profiler-test/Build-Test.ps1 -Integrated` is still used by `build-windows.yml`. The directory name is historical; **do not delete this build script or replace it with a duplicate**. `Run-Triple-Test.ps1` is historical developer test code, not the shipping application or normal capture flow.
- **CapFrameX workflows** `capframex-private-runtime-test.yml` and `capframex-runtime-check.yml` are legacy/specialized compatibility experiments, **not alternate GCO release workflows**. Do not use, activate or copy them for normal GCO work; run only if CapFrameX compatibility is explicitly being investigated.
- **Before editing or running CI:** inspect `AGENTS.md`, `docs/LATEST-WORK.md`, `.github/workflows/`, current `main` HEAD, relevant source and latest completed Actions run. Identify the canonical last green commit and continue from it. Prefer the existing `main` push trigger after source edits; do not additionally dispatch another equivalent run.
- **Before proposing a new workflow:** prove the active workflow cannot serve the requirement; seek the user's explicit authorization; document why in `docs/LATEST-WORK.md` and retire/migrate its predecessor *in the same change*. An agent's convenience, temporary debugging, or a failed CI run is **not** justification for another workflow.
- **Report only verified evidence:** exact run URL, commit SHA, pass/fail and artifact ID; never equate successful CI with gameplay validation. Documentation-only changes need no redundant Windows rebuild. Preserve product version unless the user explicitly requests a bump.

## Non-negotiable ownership

- **CET**: `giorgioherma/G-CET_Runtime_Profiler` on `dev/resolver-profiler-handoff` owns native CET profiler, managed install/restore, 0-Engine/native/Lua optimizations, G-GC and all authoritative CET generators. **Never duplicate/rewrite its existing generator in this repo.** For defects, fix the existing owner.
- **REDscript profiler**: `giorgioherma/GRSP-G-REDscript-Profiler` on its `dev/resolver-profiler-handoff` owns the Rust RED4ext profiler and capture/reporting. Extend missing measurement evidence THERE, not a second copy of the native hooks.
- **REDscript runtime**: `giorgioherma/G-REDruntime_Framework` owns shared REDscript services. Its framework is not an automatic dependency for an otherwise standalone mod.
- **Unified app** (THIS repo): friendly launcher/UI, verified component packaging, cross-component orchestration, capture/session manifest, correlation and **new REDscript generic optimization generator/backend**, integration of the existing CET Resolver. One UI, distinct domain-specific generators.
- **CapFrameX**: third-party tool. Preserve user settings and source captures, check distribution rights before bundling, verify exact downloaded artifacts, never patch its binary.
- The historical `G-Cyberpunk2077_Total_Profiler` is only a source of design/correlation ideas, not a usable current CET/GRSP baseline.

## Product constraints

- **One selected game root** drives all paths. Detect effective live installations; capture inventories are evidence only, not mod-presence authority. Identify unsupported mod-manager virtual overlays (e.g. MO2); never analyze the wrong source tree.
- **Two pages, no extra ordinary setup**. Page 1: install → capture → continue after verified evidence; page 2 auto-analyze → review → generate one game-root-relative ZIP. Expose detailed technical reports and owner exclusions as advanced actions, not a new workflow page.
- **Capture/cleanup safety**: use standalone component headless lifecycle APIs; coordinate multi-component partial failures with verified recovery. Capture must belong to current session; no stale-export acceptance. The original profiler binaries are restored safely before normal pass generation. Respect CET F10 benchmark and independently user-owned F11; do not globally overwrite bindings.
- **Optimization ZIP**: *only game-installable files*, no wrapping root or profiler/manager files/reports/docs/symbols. Generation never writes into live mods. Keep owner/dependency metadata, safety evidence, hashes and recovery plan outside ZIP. Vortex overlay is the recommended install/rollback path; warn manual users to back up CET/REDscript and affected RED4ext folders first.
- **REDscript generic-first**: reusable source-proven generic rules are the objective. No mandatory broad REDscript semantic library; author-specific rules only when user separately approves stack-specific research. Parser/source proof, full-stack REDscript compilation, partial-input support, structural idempotence and source-drift refusal are required.
- **CET generator order is fixed**: generic 0-Engine → native integration → generic finalization → source-aware semantics. Do not move worker ownership, create corrective passes or rely on previous G-CET output.
- Preserve original authors' callbacks, gameplay state, native/system dependencies, input consumption, lifecycle, cleanup and supported fallbacks. Source checks and green CI do **not** prove gameplay behavior.
- Never merge CET/GRSP script CPU blindly or call it total frame CPU; the two profilers measure different/overlapping domains. Recognized CapFrameX synchronization is needed before claiming frame overlap or hitch causality.
- Do not publish or commit original third-party mod source archives, private capture content, proprietary binaries, developer symbols or player data.

## Development and evidence

- Document the **actual** running component versions/SHAs/build artifacts before importing anything; verify hashes, manifest identity, dependencies and license.
- Every app feature must have unit/contract tests; add integration tests for CLI status/install/collect/report/restore, partial failures, source discovery, capture synchronization and ZIP topology. Run GitHub Actions to completion and separately report source, CI and gameplay acceptance.
- Never claim the new app is already implemented because its requirements, handoff or mocked UI exist.
- Keep `docs/LATEST-WORK.md` updated with exact committed implementation, verification and blockers, and `docs/HANDOFF/CORE.md` stable.
- Do not silently weaken the October 16 product contract in favor of a newer assistant proposal. Any unavoidable conflict must be reported and explicitly agreed with the user before changing the contract.

## Explicit version control

- Never bump the GCO product version, executable metadata, CI artifact version, or release tag unless the user explicitly requests a version change. Routine fixes/builds retain the current version. CET, GRSP and PresentMon are independently versioned and must not be changed as part of a GCO bump.

## User-approved branding and portable delivery (2026-11-06)

- The root public executable is `Gs-Cyberpunk2077_Optimizer.exe`. The managed `app/G-Total-Optimizer.App.exe` is an **internal implementation detail**, and remains unchanged to avoid new runtime dependencies. Keep `PortablePackagePaths.RootLauncherName`, native launcher resource metadata, integrated builder, tests and workflow synchronized.
- The **sole integrated workflow** creates a downloadable GitHub Actions ZIP artifact named `Gs-Cyberpunk2077_Optimizer.zip`, containing exactly `START-HERE.txt` beside `G's Cyberpunk 2077 Optimization/` (with launcher/runtime/components/RESULTS inside). The user guide is **not** part of a generated game-install optimization ZIP. No extra setup page or installer wizard.
- The GitHub repository rename to `giorgioherma/Gs-Cyberpunk2077_Optimizer` is requested; it requires the account owner's GitHub repository Settings rename action. Coordinate references on completion; **never create a second repository or duplicate workflow**. Existing URL redirects are not a replacement for updating documentation and dependencies.
- The user's canonical ICO is `GOT_LAG_Hybrid_16-24_brush_32plus_V.ico`; once committed, keep one master source at `assets/brand/GOT_LAG_Hybrid_16-24_brush_32plus_V.ico`. Both the C native root launcher and managed WinForms app should embed the same image for Explorer/taskbar/titlebar; do not depend on loading an ICO through fragile relative paths at runtime. Decorative art is not the Windows icon and must be treated separately.
- Design direction: keep .NET 10 WinForms and the existing two-page backend through initial visual refresh. Visually yellow-led (#FFF000), black (#070B0D), cyan (#00E2F2) accent, fixed V art, three obvious stage controls, truthful status and one clearly enabled next action. Move long procedural text into START-HERE.txt and optional in-app Help; retain actionable error/recovery copy. Do not replace functioning controls with raster graphics or claim frame-time data before capture proof. See `docs/design/GCO-VISUAL-DIRECTION.md`.
