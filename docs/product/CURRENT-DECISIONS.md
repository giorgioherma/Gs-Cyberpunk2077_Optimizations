# Product decisions and explicit delta to the October 16 design

**Authority model.** The original [UNIFIED-TWO-PAGE-WORKFLOW](UNIFIED-TWO-PAGE-WORKFLOW.md) is copied **unchanged** from the accepted 2026-10-16 user/agent design. This file records **later explicit user decisions and implementation interpretation only**. If a suggestion here conflicts with original behavior without a direct user authorization, the original wins; ask the user before changing it.

## Explicitly approved since the original specification

1. **New repository, `main`.** User created [Gs-Cyberpunk2077_total_optimization_generator_v2.31](https://github.com/giorgioherma/Gs-Cyberpunk2077_total_optimization_generator_v2.31) on 2026-11-04 and explicitly rejected using its temporary `dev/resolver-profiler-handoff` as the development branch. Keep this product on `main`. This does not change existing CET/GRSP branch choices.
2. **Current components, not old TOTAL.** The historical TOTAL application was based on completely different, obsolete CET profiler code. It is only a reference for interface and correlation concepts. New product integrates current CET, GRSP and REDscript framework components.
3. **REDscript generic-first.** The user explicitly **does not intend a large REDscript semantic library**; apply generally safe/reusable generic rules, with rare optional stack-specific semantics only when the user decides. This tightens the October 16 provisional REDscript scope without prohibiting agreed exceptions.
4. **One user-facing resolver with separate domain-specific generators.** Reuse the existing CET Resolver as the authoritative CET generator; build a REDscript-specific backend with REDscript compiler/source/lifecycle validation. One UI, not two ordinary Resolver launchers. No cross-language parsing or implementation duplication.
5. **GRSP can evolve.** Extend existing GRSP native instrumentation/reporting when the REDscript generic backend needs new, provable measurements. Changes stay in the GRSP repository, and standalone GRSP remains functional.
6. **Documentation first, then app.** User asked to locate and preserve the original design and prepare a complete handoff before writing the unified GUI. Documentation alone does not satisfy app milestones.

7. **Shared managed runtime / CapFrameX:** user wants one bundled .NET runtime for all normal users. New app targets **.NET 10**, retains current CapFrameX 1.9.1.2 Beta (requires .NET 10 Desktop), and plans verified framework-dependent CET/GRSP manager builds with a private Desktop Runtime. No downgrade to earlier CapFrameX; private-runtime compatibility is not yet proven. See [runtime contract](../technical/SHARED-NET-RUNTIME.md).

## Existing original decisions retained

- **Two pages / three page-1 actions, one game-folder input.** Normal users are not confronted with profiler internals, extra path fields or an extra analysis button. Analysis starts automatically upon entry to page 2.
- The suggested **~5-minute gameplay capture** samples idle, running, jumping, driving and combat. It is **not** an enforced duration. Avoid menus, shops and vendors for representative gameplay.
- Both profilers' temporary native changes are reversibly installed and **restored before normal optimization generation**. Recovery is explicit and cannot be bypassed by a stale capture.
- **Vortex-managed one-ZIP overlay** is recommended, installed **after** original mods via user-defined conflict rules. Original mods remain installed. A manual install requires full backups before applying the ZIP. Owner exclusion/regeneration is an advanced safety feature.
- User's deep research stack demonstrates capability; other stacks should get **high-return-first**, generic-first best-effort optimizations. No universal performance/coverage claims or mandatory new semantic rules for modest mods.
- REDscript and CET inputs come from the current *effective* installed sources, not captures or past author ZIPs. Unsupported MO2 virtual source resolution must be identified instead of guessing.
- Package only game-root-relative runtime files; keep reports, source proofs, origin hashes and restore plans in the app workspace.

## Open / not user-approved implementation choices

- **UI toolkit**: .NET 10 Windows desktop is the selected target; CET and GRSP standalone managers still currently target .NET 8. WinForms is a likely low-risk default, but final UI visual design can evolve **without changing the two-page UX contract**.
- **Component binary delivery**: verified current standalone GitHub Actions artifacts or locally built, checksum-verified identical sources. No blind pin of stale TOTAL binaries. Prove legal and technical availability of optional CapFrameX bundling; linking existing copy remains supported.
- **REDscript optimizer algorithm**: rule families are hypotheses until source- and compile-proven. Do not claim a general generic rule works merely because GRSP flags a candidate.
- **Capture orchestration**: coordinate existing CET F10 (120s benchmark), user-defined F11, GRSP F11 and optional CapFrameX, but no verified one-key three-way session yet. Never overwrite bindings broadly to pretend synchronization.
- **MO2 and other virtual installs**, and every dependency-dependent native optimization path, remain unsupported until positively proven.
- **Gameplay validation**: none for the new unified app at documentation stage. CI, source validation, REDscript compile and in-game results are different evidence levels.

## Precedence / historical documentation caveat

The 2026-11-04 CET `docs/integration/UNIFIED-OPTIMIZER-MIGRATION.md` was drafted **before rediscovering** the October 16 original UX document. It documents useful component boundaries but its broader "Setup → Measure → Optimize → Results" concept must **not replace** the original accepted two-page workflow. This repo's unchanged original contract plus explicit decisions above are the correct product authority. The original document's wording about an established CET semantic library describes CET, **not a mandate to build a comparable REDscript library**.


## 2026-11-04 — explicitly approved CET-style UI refinement (implementation target)

The user explicitly selected the current **CET Runtime Profiler v1.3.4** look: nearly black (#080D12), dark blue panels, cyan (#36F4F4), green verified statuses, magenta accent, and compact source/capture sections. This replaces the initial light UI shell; it does not authorize copying component-owned installer or generator logic.

- **Two pages, no visible tabs.** Page 1 has source selection, brief capture/keybind instructions and buttons: **Install Profilers**, **Start Cyberpunk and Record**, **Collect Results and Restore**, then a **Continue** gate. Page 2 shows **Analyze**, **Generate Optimization Pass**, **Open Results Folder**, with separate scrollable CET and REDscript findings. Page 2 Analyze is now **explicitly user-approved instead of auto-analyze on arrival**.
- The main source chooser accepts a physical game root or **direct live CET \`mods\` directory**; installed CET owner identity requires immediate \`<mod>/init.lua\`. Direct physical CET path may infer game root only through exact known game-relative parent chain and executable proof. An external/MO2 mods folder may be inspected but must not be misrepresented as the effective game tree or a valid target for profiler install, REDscript scan or pass generation until its VFS precedence and game association are proven.
- Key status is **read-only evidence**, not static green checkboxes: CET \`CETProfilerControls/CETProfiler_Toggle\` F11 or existing custom, GRSP's native F11 start/stop, and CapFrameX \`CaptureHotKey\` from its actual portable/config settings. Preserve custom CET/user CapFrameX controls; do not silently rewrite them. CET's component-owned installer seeds missing F11; bundled CapFrameX with an approved F11 preset still requires exact package and distribution validation.
- The capture instructions specify 5+ minutes as **guidance**, stress idle, movement, driving, heavy combat; avoid menus, map, inventory, CET UI and missions, remain in-world. After STOP, **close game, collect verified current evidence and restore both profiler layers before Continue**.
- The combined ZIP is named \`G-Cyberpunk2077-{timestamp}_Optimization_Pass.zip\`. Display a Vortex-winning overlay instruction. Owner exclusions must be **dependency-aware**: removing one owner's files is not guaranteed safe if remaining generated owners depend on its scheduler/runtime or cross-owner glue. Required 0-Engine/G-REdruntime files remain only when corresponding accepted optimizations require them.
- CapFrameX is an optional frame-time companion, **not a game profiler installed into red4ext/CET**; new app can launch its approved portable deployment once provenance/private .NET compatibility is proven, otherwise link the existing copy.
