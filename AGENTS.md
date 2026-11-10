# Agent instructions — unified G's Cyberpunk 2077 optimizer

**Repository:** `giorgioherma/Gs-Cyberpunk2077_Optimizations`. **Work on `main` only** unless the user explicitly changes that decision. Do not assume the historically created `dev/resolver-profiler-handoff` ref in THIS repository is the active branch.

## Mandatory startup

Read, in order:
1. `docs/HANDOFF/CORE.md` (current boundaries and startup).
2. `docs/product/UNIFIED-TWO-PAGE-WORKFLOW.md` (**verbatim 2026-10-16 user-approved UX/product contract**).
3. `docs/product/CURRENT-DECISIONS.md` (later explicit decisions; only these override the original).
4. `docs/LATEST-WORK.md` then `docs/ROADMAP.md` (actual completion state / next milestone).
5. `docs/technical/ARCHITECTURE.md` and `docs/technical/COMPONENT-CONTRACTS.md`.
6. `docs/design/PAGE1-APPROVED-IMPLEMENTATION-PLAN.md` (latest approved Page 1 and profiler admission).
6. Before touching a source component, its **own** repository's `AGENTS.md` / handoff / integration docs and current checked-out implementation. Never substitute an old TOTAL snapshot for these.

**Current checkpoint (2026-11-10):** **GCO v1.1.8**, CET + GRSP + optional PresentMon, .NET 10 two-page WPF. Last successfully packaged checkpoint [Windows CI #187](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/42747108217) on `c8cd8de4` (**185/185**, artifact 13732365520). Later source `617f9105` moved the owner's logo 20 DIP right; [#188](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/42748365602) failed solely because the old test incorrectly required the previous logo position. The proportional CI/semantic render-smoke policy below supersedes those aesthetic assertions. Gameplay capture/restore, independent CET-only/GRSP-only operation, and optimization generation remain unverified.

## CI workflow ownership — one active integrated pipeline (mandatory)

- **ONLY canonical CI/build pipeline:** `.github/workflows/build-windows.yml`, display name **Gs-Cyberpunk2077_Optimizer**. Relevant `main` source pushes **automatically run the complete existing Windows integration pipeline**: source build, verified CET/GRSP component acquisition, installer/restore smoke, the single shared .NET 10 runtime, package verification, and downloadable artifact upload on success. Manual `workflow_dispatch` runs the **same full pipeline**, not a reduced or alternate lane. Product version remains at the user's currently selected version.
- **Do not create duplicate workflows, new build lanes, separate test bundles, parallel "experimental integrated" workflows, or replacement scripts for routine fixes.** Extend the existing `build-windows.yml` steps and `tests/GTotalOptimizer.Core.Tests` in place. Reuse the original component owner managers/builders; fix errors at source.
- **Retired obsolete test:** `.github/workflows/three-profiler-test.yml` (former display name **Build all-three CET GRSP PresentMon capture test**) was a standalone *manual triple-test* with PresentMon registering F11. It was superseded by the integrated GCO workflow and has been removed. **Do not recreate or dispatch it.** Old artifacts/runs are historic, not current downloadable app builds.
- **CRITICAL shared script:** `integrations/three-profiler-test/Build-Test.ps1 -Integrated` is still used by `build-windows.yml`. The directory name is historical; **do not delete this build script or replace it with a duplicate**. `Run-Triple-Test.ps1` is historical developer test code, not the shipping application or normal capture flow.
- **CapFrameX workflows** `capframex-private-runtime-test.yml` and `capframex-runtime-check.yml` are legacy/specialized compatibility experiments, **not alternate GCO release workflows**. Do not use, activate or copy them for normal GCO work; run only if CapFrameX compatibility is explicitly being investigated.
- **Before editing or running CI:** inspect `AGENTS.md`, `docs/LATEST-WORK.md`, `.github/workflows/`, current `main` HEAD, relevant source and latest completed Actions run. Prefer the existing automatic full integrated `main` push run; do not additionally dispatch an equivalent manual build for the same commit.
- **Before proposing a new workflow:** prove the active workflow cannot serve the requirement; seek the user's explicit authorization; document why in `docs/LATEST-WORK.md` and retire/migrate its predecessor *in the same change*. An agent's convenience, temporary debugging, or a failed CI run is **not** justification for another workflow.
- **No hardcoded aesthetic assertions:** Never gate CI on an owner's chosen logo X/Y, pixel width, margins, icon size/shine, decorative RGB samples, border placement or similar subjective design values. The owner validates appearance from the real app screenshot; source smoke validates real WPF loads, essential controls exist and both pages render. Keep meaningful functionality, source integrity, component provenance, install/restore safety and final ZIP contracts. Report exact CI run, SHA and pass/fail; artifact ID only when packaging was deliberately run. A successful integrated push CI **does** upload a packaged artifact; it does not prove gameplay. Documentation-only commits need no Windows build. Never bump version without explicit owner request.

**2026-11-10 owner reversal:** the source-only/dispatch-only experiment (#189, 170 source contracts, no artifact) is **historical**. The user explicitly restored the original behavior: relevant `main` commits automatically execute full integrated Windows CI and upload a downloadable artifact. Keep the revised high-value contract policy; reject trivial implementation-value assertions. Latest restored-run evidence belongs in `docs/LATEST-WORK.md`.

## Non-negotiable ownership

4. **Respect component ownership.**

- **CET:** Owns its profiler, existing Lua Resolver/Generator, native optimizations and G-GC. Preserve all existing work. CET profiling requires 0-Engine v0.17.2+.
- **GRSP:** Owns REDscript profiling and instrumentation.
- **GCO:** Owns unified analysis, cross-domain coordination, new REDscript Resolver/Generator, UI, reporting and final optimization ZIP.
- **G-REDruntime:** Owns shared framework services for REDscript, included only when required.
- **One combined optimization plan**, using CET and GRSP evidence and live sources. Preserve CET's authoritative generation pipeline and REDscript compilation/source-safety checks. Never duplicate implementations.

- **CET source:** `giorgioherma/G-CET_Runtime_Profiler` on `dev/resolver-profiler-handoff`. Its source-owned installer/restore and CET optimization generation remain authoritative; GCO never replaces or duplicates them. Standalone CET's core-only mode is a documented capability, but the user-approved **GCO CET profiling workflow** validates 0-Engine v0.17.2+.
- **GRSP source:** `giorgioherma/GRSP-G-REDscript-Profiler` on `dev/resolver-profiler-handoff`. Missing measurement hooks/report fields are fixed THERE, not reimplemented in GCO.
- **G-REDruntime source:** `giorgioherma/G-REDruntime_Framework` on `main`. Shared REDscript runtime files enter the generated ZIP only for accepted rules which require them; not a universal dependency.
- **GCO source:** this repo on `main`. One **central cross-domain coordination/optimization plan**, using CET and GRSP evidence and current live mod sources. CET retains its authoritative Resolver/generator decisions and fixed order; GCO owns new REDscript Resolver/Generator. GCO validates compatibility, composes owner outputs and assembles the final ZIP.
- **PresentMon and any legacy CapFrameX:** independent third-party frame-time sources; preserve user settings/source evidence and verify distribution provenance. Never add overlapping CET+GRSP measurements as total script CPU.
- The historical `G-Cyberpunk2077_Total_Profiler` is only a source of design/correlation ideas, never a current CET/GRSP binary baseline.

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
- **Owner GitHub repository rename COMPLETED and verified:** `giorgioherma/Gs-Cyberpunk2077_Optimizations` (`main`). Public launcher and CI artifact retain their separate `Gs-Cyberpunk2077_Optimizer` names. No duplicate repository or workflow.
- The user's canonical ICO is `GOT_LAG_Hybrid_16-24_brush_32plus_V.ico`; committed at `assets/icons/GOT_LAG_final/GOT_LAG_Hybrid_16-24_brush_32plus_V.ico`; retain this single master. Both the C native root launcher and managed WPF app should embed the same image for Explorer/taskbar/titlebar; do not depend on loading an ICO through fragile relative paths at runtime. Decorative art is not the Windows icon and must be treated separately.
- **Latest user-approved direction:** build the GCO presentation in .NET 10 WPF while preserving the existing two-page backend, owner managed tooling and shared runtime. Visually yellow-led (#FFF000), black (#070B0D), cyan (#00E2F2) accent, fixed V art, three obvious stage controls, truthful status and one clearly enabled next action. Move long procedural text into START-HERE.txt and optional in-app Help; retain actionable error/recovery copy. Do not replace functioning controls with raster graphics or claim frame-time data before capture proof. See `docs/design/GCO-VISUAL-DIRECTION.md`.

## Latest approved Page 1 and independent-domain rules — 2026-11-06

**Read [docs/design/PAGE1-APPROVED-IMPLEMENTATION-PLAN.md](docs/design/PAGE1-APPROVED-IMPLEMENTATION-PLAN.md) before editing GCO UI, owner capture flow, or MO2.** It supersedes earlier both-profiler and image proposals without changing the preserved original two-page contract.

- Canonical owner-renamed repo: `giorgioherma/Gs-Cyberpunk2077_Optimizations` on `main`; root launcher and CI artifact retain `Gs-Cyberpunk2077_Optimizer` naming. Do not duplicate repos or workflows.
- CET-only (GCO verifies 0-Engine), GRSP-only (RED4ext/REDscript side, **not CET-dependent**), and both must be supported. PresentMon-only never grants Continue. Standalone CET core-only can operate without 0-Engine, but GCO's user-approved full CET path must verify it. No unrelated mods/frameworks as universal dependencies.
- Only **current-session verified CET OR GRSP** evidence qualifies for Continue. Popup naming missing **expected** domains only on Continue; Return stays on Page 1 with valid results/Continue for safe retry. Page 2 requires original-file restore of **every actually installed** profiler and recovery verification.
- Locked taller Capture design: full-height V + whole black trail, **BLUE** skyline, three real cards, Game Root small Refresh, **global** CET/REDscript owner counts in Direct and MO2. Black before Start, irregular color flashes including 30ms bursts and enforced dark pauses during capture, intensified throughout collection, steady color only after qualifying collection; no fake percent.
- MO2 remains **disabled/deferred**. Normal future UI game root + one MO2 folder, auto INI/profile/modlist and configured external drives; exceptional custom/category locations in a separate Advanced path-repair dialog with effective-profile proof. Counts are not an assertion all user mods were found.
- Existing GCO code still assumes both owners (`UnifiedProfilerWorkflow`) and frame-inclusive `HasAny` (`CollectedCaptureStore`); above is **pending implementation**, not achieved CI or game validation. Keep GCO v1.1.6, one Windows pipeline, original source-owner managers.

## User-approved WPF frontend migration — 2026-11-06

- **Read [WPF migration handoff](docs/design/WPF-MIGRATION.md)** before editing GCO UI. The user explicitly switched only the GCO GUI to native **.NET 10 WPF**, and allowed replacing the old artifact; do not duplicate CET/GRSP profilers, managers, or replace the current integrated workflow.
- `MainWindow.xaml(.cs)` is the running GCO two-page UI. Old `MainForm.cs` is retained in git but **excluded from compilation**. `UserSettings` and headless `--verify-package` / elevated PresentMon handling are preserved. The WinForms framework reference currently remains only for FolderBrowserDialog, not a second running GUI.
- Latest [integrated Windows CI #42277190679](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/42277190679), source `3fe24fb`, **SUCCESS 179/179 contracts**, single private .NET 10 runtime, artifact ID `13532694947`. This is package/CLI/source proof, **not** real WPF visual, idle GPU, DPI or Cyberpunk gameplay proof.
- Original user V black/color image pair and BLUE skyline are **not yet committed**. Build admits exact files when present but no substitutes. CET-only/GRSP-only backend independence, 0-Engine version proof, partial retry and optimizers remain pending; don't treat WPF's UI affordances as proof of those backend capabilities. Keep v1.1.6.

## 2026-11-07 compact Page 1 implementation checkpoint

- **Read [compact WPF Page 1 layout](docs/design/PAGE1-COMPACT-WPF-LAYOUT.md)** before more visual work. **Normal 1220×680 DIPs, minimum 1040×600 DIPs** as first testing baseline; preserve accessible scrolling, native Continue/restore gates and one shared runtime. These are adjustable after user screenshots.
- **Rajdhani** for ordinary text, **Oxanium** for labels/buttons/tabs; owner committed both with OFL licences. Embed only used styles with WPF Resource paths; include OFL notices, not a system-wide installer.
- Exact existing full V source must use `Stretch=Uniform` and proportional sidebar width (not cropping `UniformToFill`). Place top brand/logo gap across app; drop skyline below it. Big central parallelogram tabs; title `GOT LAG?`; one Game Root panel; three tall cards with real in-card controls; console and special Continue at bottom. No duplicate instructions/status box. Yellow-to-cyan clipped vectors and cyan slash motifs are **WPF geometry**, not PNG overlays.
- The first-pass visual changes are not proof of real gameplay or independent CET-only/GRSP-only backend support. Keep one existing main CI workflow and v1.1.6.

## 2026-11-07 compact WPF verification

- [Single integrated Windows CI #42325021822](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/42325021822) **SUCCESS, 179/179 contracts**, source `a1c2a547d2a5c6a60e596bdb7bdd655af107a29f`. Bundles exact full V pair, full user blue skyline and WPF Rajdhani/Oxanium embedded fonts (OFL licences) with **one .NET10 private runtime**. This supersedes older last-green references for WPF.
- Page 2 is source-layered to keep the tabs visible and cover the Capture footer; active tab styling follows verified restore. **No Windows visual/DPI or gameplay test yet.** Do not conflate CI with end-to-end validation or completed CET-only/GRSP-only backend admission.

## 2026-11-07 — Latest layout correction (supersedes earlier window sizes)

- **Default 1280×760 DIP, minimum 1080×640 DIP** per explicit user decision; supersedes 1220×680 and 1040×600 in historical handoff. Leave natural vertical scrolling at the minimum and full status/Continue legible. Current approved palette remains #060D0F background, #081012 cards, #11191A path and #151C1C buttons.
- Real screenshot revealed Game Root's unconstrained 1000-unit decorative Path incorrectly made its StackPanel **huge**, hiding workflow cards in the normal window. Fix **at layout source** using a constrained 108-DIP panel that ends directly after its four status rows. Game Root title is a separate larger **white** heading outside/above the panel. Keep the box aligned to the lower blue skyline transition.
- Tabs now show only **CAPTURE** / **ANALYZE & OPTIMIZE**, with significantly steep-sided parallelogram shapes. GOT LAG? grows; increase spacing before titles and panels. Icons are centered atop all three workflow cards; cyan triple slashes tightened. Vertical scrollbars must be a **narrow yellow thumb on #060D0F**, not Windows default white. Preserve real capture/restoration bindings and source owner contracts.
- Font decision is **not final**: Rajdhani and Oxanium remain the current embedded test fonts, but the user is evaluating their appearance. Don't replace them or finalize another typeface without user direction. [compact UI source f733383](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/commit/f73338321b6e564fe69e4fd2299169efaa534899). [Windows CI #42328168943](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/42328168943) launched, not user gameplay/DPI-verified.

## 2026-11-07 — Single 45-degree WPF frame geometry (user-approved direction)

- The WPF Page 1 border system now uses **one reusable size-aware corner geometry**, implemented in `src/GTotalOptimizer.App/Visuals/ChamferGeometry.cs` and used from `MainWindow.xaml`. **Do not restore stretched 1000×1000 Path outlines**: nonuniform `Stretch=Fill` distorts corner angles. Standard design cut **14 DIPs**, clamped for small controls; scale with window at 1280×760 DIP normal / 1080×640 min. Every corner has equal horizontal and vertical cuts, hence 45° at every size.
- **Outer chassis:** thick (~3.6 DIP) static yellow-to-cyan stroke, layered soft glows, bright corner accent fins near the client edge. **Inner chassis:** thin (~1.8 DIP) parallel cut-corner border inset 10 DIPs, with black gap and header-rule visual connection. All cards, Game Root, console, Continue and page 2 containers share reusable chamfer rendering; action buttons have responsive cut corners as well. Only the normal editable path input remains a standard rectangular input.
- Inset the original aligned V image a few DIPs and clip its actual artwork to the same 45° geometry; do not regenerate or stretch/crop the art. The two center tabs are **equal 246×44 DIPs**, exactly sloped and labeled only **CAPTURE** and **ANALYZE**. Decorative WPF-only change: preserve existing tab authorization, profiler owner pathways, packaged fonts/assets, single `.github/workflows/build-windows.yml`, GCO v1.1.6 and no added runtime.
- Current [source commit](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/commit/b53e2c728a20ef039cd62ab7d62ad7c46baf416e); [integrated Windows CI](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/42331860013) was running at authoring time. A user Windows screenshot and DPI test remain required for visual approval. No claim about idle-GPU/capture quality from code compilation.

### Verified frame-system build — 2026-11-07

- [Integrated Windows CI #42332115489](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/42332115489) on code `c478f9da3ee29c43107be3402bd7132c8006981e` **SUCCESS, 179/179 contracts**; artifact `Gs-Cyberpunk2077_Optimizer` ID **13557508098**, **86,547,719 bytes**. Original V pair, full blue header, embedded OFL fonts, CET/GRSP owner managers, PresentMon and single .NET 10 private runtime all packaged. First attempt [#42331860013](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/42331860013) **FAILED frontend compilation only** from WinForms/System.Drawing vs WPF Brush type ambiguity; fixed at source in `c478f9d` using explicit WPF drawing aliases. **No Windows screenshot, visual glow/DPI or gameplay claims from CI.**

## Distribution identity / download warnings (2026-11-09)

- Keep the GCO version on `Directory.Build.props` until owner explicitly changes it. `START-HERE.txt` is generated from the canonical MSBuild version; include `BUILD-INFO.json` in the app root to identify the exact GitHub commit, run and attempt. The **GitHub artifact filename** must include GCO version and run/attempt; avoid same-named ZIP confusion and do not wrap the GitHub Actions ZIP in another ZIP. The public executable name remains `Gs-Cyberpunk2077_Optimizer.exe`.
- Never treat 183 passing source contracts, clean archive CRCs, or matching source manifest hashes as malware scans. Preserve evidence of Firefox/Nexus classification and vendor reports. No silent AV bypass, deleting profiler binaries to reduce detection, or speculative hashing/version churn to chase download reputation.

## Latest approved owner artwork — 2026-11-09

- New canonical V portrait files are assets/ui/images/V_background_1_black.png and V_background_1_color.png (both 482x928 RGBA). Owner replaced TrueHeader_GCO_page1.png under the same filename (1870x652 RGB), fading to true #000000. This supersedes the old narrow portrait pair and generated Round-6 yellow halo/opacity mask. Preserve the old narrow 743:2117 reserved column and V left/top/height; the new pair's right-hand pixels flow over that column at 482:928 natural height-based aspect. Do not reintroduce synthetic transitions or adjust right-hand UI positions; preserve black/color stage animation and six-DIP lower overlap. Only WPF page/Game Root/skyline fade baseline is true black; keep existing panels, inner/outer border, icon and native owner workflows. Product stays v1.1.8.

## 2026-11-09 — Approved Page 1 overflow/layout refinement

- Keep `ContentRegion` above the thin `ContentChassisOutline` (z=12 vs z=10), and keep the **single user-supplied outer Regular border** on top (z=30). Within ContentRegion, the new V 482x928 PNG pair remains behind UI controls, so its ink puddle crosses only the thin inner frame. Never duplicate the V image, crop its right alpha, or paint it over functional text.
- `TopTabsHost` now lives in the 68-DIP header band above the inner top line, with 246x44 equal CAPTURE/ANALYZE read-only state indicators; slightly steeper slopes (0.92× horizontal skew), responsive header alignment at 1080–1280 and beyond. Capture/Analyze pages start directly at row 0 of the right content region; the GOT LAG hero and Game Root are raised. Game Root moves 11 DIPs left toward V. `ConsoleTextHost` is transparent with no decorative box or duplicate slash; its owner StatusText and RESTORE still function. `ResultsPage` has a small inset so its black surface cannot erase the inner chassis. Only presentation, no profiler/runtime change.
- Windows CI #42628830845 source 8bedbcc **SUCCESS 184/184** including actual offscreen WPF regular/min/resize checks and Analyze page top inner border. Owner screenshot acceptance still pending. v1.1.8 unchanged.

## Latest owner-only puddle correction (verified 2026-11-09)

- The earlier full-V-over-inner-border direction is **superseded**. Source: `assets/ui/images/The overflow.png`, 365×49 RGBA with authentic transparency; use its exact filename and pixels. V pair 482×928 is now 1 DIP to the right of the thin inner frame at default client size (V starts x28, frame x27), with original top/height and animation unchanged. **Full** V remains in ContentRegion root z12 **below** the thin inner border z13. **Only** the separately scaled owner bottom strip at root z14 (width=VHeight×365/928, height=VHeight×49/928, same left/bottom) may overpaint inner border; tabs z22, outer PNG z30. No new/generated texture masks, single outer frame, skyline-to-V layering and WPF controls unchanged.
- [Windows CI #42646172031](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/42646172031) **SUCCESS 185/185**, code `20f24767a2a6dcc8303b68db23651dad788f3c86`, artifact ID 13686974054; WPF actual rendered overlay darkens 293 pixels of inner bottom stroke and preserves left frame x27. User's real Windows visual approval remains pending. Product v1.1.8, one .NET10 runtime and owner-profiler internals unchanged.

## 2026-11-09 — Latest Page 1 path/header design override

- Shift `HeroTitle` (GOT LAG + subtitle) right 22 DIPs; restore `GameRootPanel` to original scroll-content X alignment (remove old -11 DIP shift). Game-path input is **rectangular**; do NOT reintroduce the old chamfered PathInput or allow its background to extend into the parent chamfer's transparent corner. Actual input starts at +22-DIP inner margin, OPEN sits after at least 16 DIP, with matching 22-DIP right inset.
- REFRESH is in the top-right app header, visible at minimum/window resize; the Reduce flashing checkbox is removed while maintaining V animation. Do not show placeholder MO2-LATER/deferred text on Page 1; future MO2 remains an architectural note. Existing three cyan step icons are unchanged pending agreed artwork; WPF vector shapes recommended for basic interactive icons. Preserve all source-owner install/capture/restore, public v1.1.8, one .NET10 runtime, current owner images/overflow, tab placement and one Windows CI.

## 2026-11-09 — Latest Page 1 visual alignment and global spatial neon (verified)

- Owner screenshot requested GOT LAG headline + subtitle **as one +62 DIP left-inset container** (was heading alone +22). Leave original Game Root X alignment; center its inner 108-DIP content vertically with margin 22/0/22/0. Path is still editable; smaller **13.5-DIP font**, compact 9/0 padding and 32-DIP field height prevent text clipping; on lost keyboard focus return view to drive/root prefix without changing the string.
- Top header now has empty 165-DIP logo reservation (no GCO placeholder), centered brand title and cyan `CET and REDscript Optimization` subtitle, aligned REFRESH and `v1.1.8` at top right. 0-Engine/prerequisite text uses #FFF000. Three workflow icon/title/description blocks are horizontally centered while step numbers stay left. Existing icon shapes are unchanged pending owner graphics choice. Continue has margin -10/-25/+10/+25, giving +10 DIP left and +25 DIP upward visual placement without affecting its safety gate.
- **One actual root-coordinate gradient field** `Visuals/ChamferGeometry.cs:SpatialNeonField`: yellow anchor at (0.09×rootW, 0.80×rootH) near the lower V, cyan anchor at (0.93×rootW, 0.12×rootH) near the skyline. WPF absolute-mapping brushes are translated into each element's real root position; Game Root, three cards and thin inner chassis opt in via `SpatialStroke=True`. This is not five independent gradient midpoints. The outer border remains owner PNG, and the actual V / transparent puddle stay registered unchanged. Don't reintroduce a local center gradient on these boxes.
- [CI #42679052240](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/42679052240) success 185/185; WPF test demonstrated field sample indices 0.29/0.54/0.79 from card 1 through 3 and upper/right > lower/left within each card, min-size checks, original PNG alpha. First attempt #42678736240 failed C# local-name collision in test, corrected in next source commit. User Windows pixel/art acceptance outstanding; GCO version unchanged.
