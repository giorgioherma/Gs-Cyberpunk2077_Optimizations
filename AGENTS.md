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

**Current user instruction as of 2026-11-04:** document the design and agent continuation fully **before starting app implementation**. Design/document migration is not evidence of running code. Do not mark any UI/backend feature implemented without testing it.

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
