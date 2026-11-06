# Page 1 — approved design, profiler eligibility and MO2 handoff (2026-11-06)

**Approved design; NOT YET IMPLEMENTED.** This document is the latest explicit update to [the original unchanged two-page specification](../product/UNIFIED-TWO-PAGE-WORKFLOW.md). See [current decisions](../product/CURRENT-DECISIONS.md) and [latest work](../LATEST-WORK.md).

**Canonical repository:** `giorgioherma/Gs-Cyberpunk2077_Optimizations`, `main` (owner-renamed, verified). **Public filename is different:** keep launcher `Gs-Cyberpunk2077_Optimizer.exe` and artifact `Gs-Cyberpunk2077_Optimizer.zip`. Preserve GCO v1.1.5, **.NET 10 WPF (later explicit user approval)**, one shared private runtime, one canonical `.github/workflows/build-windows.yml`, two pages and one game-relative generated optimization ZIP.

## Locked Page 1 presentation

- **Reference:** user-selected taller yellow/black Capture window, full original V portrait on left and **entire long black ink trail to the bottom**. **KEEP the BLUE/cyan upper-right skyline** for contrast; yellow skyline substitute rejected. Palette yellow `#FFF000`, near-black `#070B0D`, limited cyan `#00E2F2`.
- **Do not regenerate the art.** Latest supplied `Transition2-more_exact(1).zip` has aligned **743×2117** `Transition2-more_exact/Upright_hotka.png` (full color) and `Transition2-more_exact/GCO_V_original_pixel_locked_silhouette.png` (user hand-blacked). Preserve intentional imperfections and distressed G. **User-uploaded originals have NOT been committed to GitHub**; admit real assets separately.
- Exactly three real functional cards: **Install Profilers → Start Cyberpunk/Capture → Collect Results**. Continue is the final gated CTA. Do not bake controls/status into a raster background; keep readable text, keyboard focus and DPI scaling.
- Small cyan **Refresh** at the upper-right of the **Game Root** section, not another workflow card. Reread selected source, current owner readiness and counts; never write to game, switch selection or invalidate verified session/result/restore state. Disable during active owner mutations.
- **Global** `Game found · N CET mods · M REDscript mods` line in **Direct and MO2** alike. CET count = effective owners with `<owner>/init.lua`; REDscript count = effective owner groups, not `.reds` file count. Never claim all of a user's mods are detected or double-count shadowed paths. Explicitly identify an incomplete/unknown source inventory.

## V transition — actual state driven, not fake loading

1. **Before game/capture:** black silhouette, no flashing.
2. **After Start Cyberpunk/Capture:** irregular color glimpses underneath black overlay; include **30 ms** rapid microflashes, sporadic single flashes, short intense multi-flash bursts and **mandatory long completely-dark pauses**. No regular repeating cycle or percentage.
3. **While Collect Results is actually running:** retain the random glitch behavior and **increase intensity** through potentially long stop/copy/hash/validation work. Stop only after a real terminal collection/verification result; errors must not show false success.
4. **Qualifying collection completed:** fade away the black top layer, reveal permanent full-color V when verified current-session CET **or** GRSP results make Continue available. The colored state is a *collection* milestone; it is **not** a claim that all original profiler files have already been restored. Restore is mandatory before Page 2.
5. Original aligned images only. Lightweight WPF animation; provide reduced-motion/no-flash alternative for accessibility. Do not depend on loading timers to determine capture success.

## Independent CET and REDscript profiler eligibility (mandatory change)

| Actual selected installation | Supported capture |
| --- | --- |
| Supported CET + **GCO-validated 0-Engine**, GRSP absent | CET only |
| RED4ext + REDscript-side environment, CET absent | GRSP only |
| Both environments valid | CET and GRSP, with independent state |
| Neither environment valid | Block native profiler setup |
| PresentMon alone | Optional frame timing, **cannot unlock Continue** |

- **0-Engine is the only extra GCO CET-side dependency**; check its real installed source before admitting the **full integrated GCO CET route**. Standalone CET supports a separate `--core-only` mode without 0-Engine; do not confuse the standalone owner's optional behavior with the user's stricter GCO CET route.
- REDscript-side capture must **not** require CET, 0-Engine or unrelated framework/mod files. RED4ext is GRSP's host, and REDscript-side runtime/stack is discovered on its own merits. Do not turn a missing `r6/scripts`/CET mod count, Codeware, G-REDruntime, ArchiveXL, TweakXL, etc., into universal hard dependencies.
- Install, inspect, collect, and restore **only the applicable/selected** native domains using their **authoritative standalone owner installers/restorers**. Absence of the other domain is valid, not failure. PresentMon remains optional, independent and game-external.

## Capture evidence, partial results, retry and navigation

- **At least one current-session hash-verified CET or GRSP completed capture** unlocks Continue. **PresentMon-only never does**. Preserve separate owner capture proofs and optional CSV.
- Do **not** show a popup immediately upon partial collection. When the user **presses Continue**, show missing **expected/installed** domain(s), optionally an enabled-but-missing PresentMon, with **Return** and **Continue** buttons. An intentionally absent/unselected profiler is not a missing result.
- **Return:** remains on Page 1; previously verified results and enabled Continue remain. User may repeat a capture. Implement **safe new attempt/session provenance**, never mix stale/unrelated evidence, delete owner recovery evidence or overwrite previous confirmed results.
- **Continue:** explicitly acknowledges absent expected outputs; then invokes **all actually installed source-owner restorers**, verifies original files and recovery, and **only then** opens Page 2. Restoration failure must block navigation. Page 2 Back remains absent until a separately designed safe new session.
- Keep PresentMon collection on step 3, independent of CET/GRSP restore. A missing optional frame capture is not automatically an invalid CET/GRSP profiling result.

## Direct now; MO2 deferred

- **Direct installation first**, with a single physical game-root input and global owner counts; prioritize real CET/GRSP gameplay capture tests.
- Future normal MO2 UI uses the same game root **plus one MO2 application-folder input** when MO2 selected. Portable has `ModOrganizer.ini` beside the app; instance mode normally uses `%LOCALAPPDATA%/ModOrganizer/<instance>/ModOrganizer.ini` or a verified custom instance location. INI provides configured Mods/Profiles/Overwrite paths even on different drives, and selected profile's `modlist.txt` gives enabled packages and precedence. Physical game files remain the base; don't assume configuration alone proves USVFS/RootBuilder injection.
- Users may have **several arbitrary external/category folders** not inferred from ordinary INI/profile data. Only then expose a **separate Custom/Advanced path-repair window** rather than filling Page 1 with extra fields. Show resolved paths and counts; attach additional folders to the *active profile* only with real enable/priority/provenance proof. Never count an arbitrary directory as enabled because it exists; mark incomplete inventory visibly.
- Actual CET/GRSP output may be in physical game folders or MO2 **Overwrite**. Future implementation must verify the **runtime** effective source/result locations and owner-managed install/restore. PresentMon CSV stays inside GCO RESULTS. MO2 integration remains disabled (`Mo2FeatureGate.Enabled=false`) until tested.

## Required GCO changes — all PENDING

- **`UnifiedProfilerWorkflow.cs`:** remove current `BothInstalled`, `BothCaptured`, `CleanForInstall` and both-required install/collect/restore assumptions. Represent per-domain eligibility/managed state/capture proof, with existing owner APIs and safe recovery.
- **`CollectedCaptureStore.cs`:** `HasAny` currently includes frame-only PresentMon evidence; do not use this as Continue gate. Require **CET-or-GRSP verified native evidence**, keep frame data independent.
- **`ProfilerSetupView.cs` + WPF Page 1:** per-domain prerequisite/install/missing status and correct expected-domain popup, Continue and safe retry, Refresh preserving proof, global effective-owner counts and approved V UI.
- **MO2 later:** build on existing `Mo2InstallLocator` and `Mo2SourceInspector`; no duplicate VFS projection or unverified native capture path.
- **Testing:** extend current `tests/GTotalOptimizer.Core.Tests` and **single existing** CI for CET-only, GRSP-only, both, neither, CET missing 0-Engine, PresentMon-only, partial Return/Continue, retry identities, stale evidence refusal, owner restore failure, Refresh integrity, output and gameplay. No version bump, speculative generator or extra workflow.

**Last verified integrated CI before these decisions:** [run #42148988246](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/42148988246) on code `e7b12fb3cc40f9c3e13423ecff5e45bafcd0d41d`, **179/179 passing**. This is NOT validation of the new design or gating rules.

## User-approved UI toolkit supersession — 2026-11-06

The user explicitly chose **native .NET 10 WPF** rather than redesigning WinForms first. The visual and capture-safety criteria above remain binding. [WPF migration handoff](WPF-MIGRATION.md) records the green source/build stage, outstanding exact original image admission and real Windows/gameplay testing.

### 2026-11-06 asset provenance clarification

The upper-right **BLUE skyline is a feature of the approved full-composite UI mockup**, not a separate user-owned original image. The file `02_cityline_header.png` (yellow panorama) was an incorrect choice and must not be used in the WPF header. Source-specific solution: display an exact **small blue skyline crop** from `UI_DESIGN-fire.png` at the right, leaving the rest of the WPF header dark. The selected original color/black V pair remains unchanged. See [WPF migration handoff](WPF-MIGRATION.md).
