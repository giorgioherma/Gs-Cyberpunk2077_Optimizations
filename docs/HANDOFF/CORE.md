# Mandatory unified optimizer startup / handoff

**This repository**: `giorgioherma/Gs-Cyberpunk2077_Optimizations`. **Working branch: `main`** (user decision 2026-11-04). Do not use this repository's temporary/historical `dev/resolver-profiler-handoff` branch as the active line. Related component repos may legitimately use branches with that name.

## Read order

1. [Original accepted unified two-page specification](../product/UNIFIED-TWO-PAGE-WORKFLOW.md) — **authoritative UX + ZIP/rollback contract, imported verbatim**.
2. [Current decision deltas](../product/CURRENT-DECISIONS.md) — approved November 4 scope/ownership refinements, distinguish from exploratory proposals.
3. [Current status](../LATEST-WORK.md) and [implementation roadmap](../ROADMAP.md).
4. [Architecture and state machine](../technical/ARCHITECTURE.md), [headless component contracts](../technical/COMPONENT-CONTRACTS.md), [document index](../README.md).
5. Project-specific source-of-truth docs for the component being modified.

## Scope

Build an **all-in-one, user-oriented app** bundling verified CET and GRSP profilers, PresentMon, CET's **existing authoritative Lua Resolver/Generator**, and a **new GCO-owned REDscript Resolver/Generator**. GCO owns unified analysis and one combined optimization plan from both profilers and live installed sources, then a single verified game-relative optimization ZIP. Preserve CET's existing work. G-REDruntime provides shared services for REDscript only when accepted optimizations require them. The legacy TOTAL Profiler remains a historical reference, not a source of current binaries.

**Only one game folder** should be asked in normal setup. **Exactly two ordinary pages** and **three first-page actions** from original design; advanced controls must not silently add a new mandatory step. Reliable capture ownership, transactional restoration and a clean Vortex-friendly ZIP are design-critical.

## Actual state on 2026-11-04

- New repo was empty, then initialized with a README. This milestone is **documentation-first** on `main`.
- The October 16 source design was found in CET's `dev/native-static-anchors-experimental` branch; it was missing from CET's current handoff branch. Source: [original document](https://github.com/giorgioherma/G-CET_Runtime_Profiler/blob/dev/native-static-anchors-experimental/docs/product/UNIFIED-TWO-PAGE-WORKFLOW.md), original Git blob `f4925759fc612311b72432b329708b84b9dd279e`. Preserve as original design, do not fold later changes into it silently.
- CET current development branch: `dev/resolver-profiler-handoff`; GRSP current dev branch: same name in GRSP; G-REDruntime: `main`. **These are *other repositories*.**
- A **first read-only .NET 10 WinForms two-page app shell** and core CLI status adapters are **compiled/tested in [Windows CI #41859261728](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/41859261728)**, source commit `fa9309d`; 14 offline contracts pass and a self-contained Windows shell artifact exists. Interactive GUI launch and real backend integration are not yet tested. No new REDscript generic generator, combined profiler restore, or final one-runtime packaging is claimed.
- **Next task:** exercise the CI-produced read-only Windows shell and actual status against verified current CET/GRSP standalone packages, then design safe multi-component session lifecycle. Do not expose enabled install/capture/ZIP actions before tests. [Private .NET 10 runtime contract](../technical/SHARED-NET-RUNTIME.md).

## Strict boundaries and proof

**Ownership contract (user edited Rule 4, 2026-11-06):** CET owns its profiler, existing Lua Resolver/Generator, native optimizations and G-GC; preserve all existing CET work. **CET profiling requires 0-Engine v0.17.2+** in the supported GCO CET route (standalone core-only capability remains separate). GRSP owns REDscript profiling/instrumentation. GCO owns unified analysis, cross-domain coordination, the new REDscript Resolver/Generator, UI, reports and final ZIP. G-REDruntime owns shared **REDscript** framework services; include only if required. One combined optimization plan must use CET and GRSP evidence and live sources, without duplicating owner implementations. CET fixes stay in CET; native GRSP fixes stay in GRSP; REDscript generation remains compiler/source-safe in GCO. Never bypass owner install/restore.

Update [LATEST-WORK](../LATEST-WORK.md) after every substantive milestone, including unverified status.

## Update 2026-11-05 — live-game-first MO2 and portable shell

- Latest completed Windows CI: [#41950289302](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/41950289302) **SUCCESS, 101/101 contracts**, source SHA `d13c79c99148ec27d54cf47a8ed4c6e791acb2a9`, portable artifact **13380467867**.
- Physical game CET, REDscript and RED4ext sources are always the base. Only valid/selected modlist-enabled MO2 packages override identical relative paths; MO2 Overwrite is included only after reading a real modlist. Missing/unusable modlist means **physical game only**, `ProfileVerified=false`, no MO2 staging; an existing empty/all-disabled list is a valid zero-enabled profile. Unknown/corrupt/incomplete profiles also exclude all MO2 layers but retain physical evidence.
- CET-style portable `G-Total-Optimizer-v1.1.1/` folder: public root native `G-Total-Optimizer.exe`, `.lnk` shortcut, internal managed `app/G-Total-Optimizer.App.exe` with exactly one .NET 10 runtime, and `RESULTS`. Package root is resolved from the internal app. No unverified profiler/CapFrameX binaries are included.
- Backend install/collect/restore, MO2 USVFS/RootBuilder runtime fidelity, REDscript optimizer, and final combined game-relative ZIP are NOT implemented. Verify portable shortcut after moving the extracted folder and visually test WinForms on user Windows PC.

## Latest source/CI checkpoint — 2026-11-05 integrated GUI

- **Supersedes the earlier read-only shell summary above.** On unified `main`, GCO **v1.1.2** now packages CET v1.3.4, GRSP v1.0.0 and PresentMon v2.6.0, with the managed GUI and both owner managers source-retargeted to one shared private .NET 10 Desktop runtime. All native owner DLLs remain unchanged. Current integrated [Windows CI #42049460675](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/42049460675) SUCCESS, source `c7d22c68ff86b039280885dd8fc97bd9f6d61da9`, **121 contracts**, artifact **13426900277**.
- Real Page 1 buttons now call source-owner status/install/collect/restore CLI and PresentMon initial launcher. `UnifiedProfilerWorkflow` owns a portable recovery journal and gates all owner mutations on verified manifest, game closed, and fresh native status. CET/GRSP completed capture + verified original restoration unlocks Page 2. Legacy triple harness is manual-only. MO2 remains disabled.
- User's previous standalone test proved actual CET+GRSP install and owner restore, but **new GUI game capture/restore has not been user-validated**. Critical next tasks: real GUI opening/placement, F11 multiway hotkey behavior, live capture collection, recovery failures, and source-owned CET Resolver + generic-first REDscript generator. **Do not claim finished optimizer or enable ZIP generation.** Replace obsolete CapFrameX UI text after capture acceptance.

## Current handoff — 2026-11-06 owner-renamed repository and approved Page 1

**Canonical repository:** `giorgioherma/Gs-Cyberpunk2077_Optimizations` `main`; verified owner rename. The public EXE and artifact remain `Gs-Cyberpunk2077_Optimizer.exe` / `Gs-Cyberpunk2077_Optimizer.zip`. Product v1.1.5, one shared .NET10 runtime/WinForms app, only `.github/workflows/build-windows.yml`.

Latest verified integrated Windows CI [#42148988246](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/42148988246): code `e7b12fb3cc40f9c3e13423ecff5e45bafcd0d41d`, **SUCCESS 179/179**; historical v1.1.1/v1.1.2 sections above are no longer current. New user requirements are **not** source/CI/gameplay-verified.

**Required reading:** [approved Page 1 design/source plan](../design/PAGE1-APPROVED-IMPLEMENTATION-PLAN.md), [visual direction](../design/GCO-VISUAL-DIRECTION.md), [current decisions](../product/CURRENT-DECISIONS.md). Lock taller full V ink-trail mockup and **blue** skyline, small Game Root Refresh, global Direct/MO2 mod counts, black→random 30ms-capable glitch during capture (intensified while collecting)→permanent color after qualifying results.

**Required future code changes:** independent CET-only (with GCO 0-Engine check), GRSP-only (RED4ext/REDscript side without CET), and both; reject no-native/PresentMon-only. Current `UnifiedProfilerWorkflow` requires both; `CollectedCaptureStore.HasAny` counts PresentMon. Continue with any one verified current-session CET or GRSP archive; missing expected-domain Return/Continue popup **only on Continue**. Return preserves proof for retry; Page 2 must verify restoration of all installed native owner profiler changes.

**MO2 paused:** standard future game root + one MO2 app folder, portable/LocalAppData config and profile/modlist, custom extra/category directories via separate Advanced path-repair window, no automatic "all mods" claim or assumed Overwrite output. `Mo2FeatureGate.Enabled=false`. **Next:** direct-install UI, per-domain backend/tests and real user capture/restore tests; do not implement MO2 now or bump version.

## Current GUI checkpoint — 2026-11-06 WPF transition

**Supersedes historical WinForms-first decisions above.** The user approved native .NET 10 **WPF** for GCO's two-page frontend and allowed replacing the existing artifact. The CET/GRSP owner WinForms managers and native profilers remain unchanged, as do GCO Core, PresentMon, `--verify-package`, the single private .NET 10 Desktop runtime and **only** `.github/workflows/build-windows.yml`.

The WPF app is now built from `src/GTotalOptimizer.App/MainWindow.xaml(.cs)`, `MainForm.cs` is excluded from compilation, and `UserSettings` retains its old preference format. [Windows CI #42277190679](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/42277190679) **SUCCESS 179/179**, code `3fe24fb59c5846ead0823e547d029698eb1ac9ef`, artifact `Gs-Cyberpunk2077_Optimizer` ID `13532694947` (~81.87MB compressed). It proves packaged CLI/owner smoke, **not actual WPF window launch or gameplay**. Two earlier WPF compile failures were fixed at source (see LATEST-WORK).

**Next:** admit exact original black/color V pair and BLUE skyline; test WPF visuals/DPI, source-owner install/capture/restore on Windows; finish **independent CET-only / GRSP-only** backend admission and 0-Engine version check. MO2 integration and optimization generation remain deferred. [Full WPF migration handoff](../design/WPF-MIGRATION.md).

## 2026-11-07 — Compact WPF Page 1 first-pass implementation

Read [PAGE1-COMPACT-WPF-LAYOUT](../design/PAGE1-COMPACT-WPF-LAYOUT.md). Source [`33aca3f`](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/commit/33aca3fac2a0278435c4c1cf5a15afbf30455424) replaces the large 1380×890 shell with **1220×680 DIPs default / 1040×600 minimum**, proportional full-portrait V, new 68-DIP reserved brand/logo band, centered sloped tabs, GOT LAG? headline, one compact Game Root/readiness panel, three in-card actions and pinned console/special yellow Continue. Owner added OFL Rajdhani/Oxanium via `7ca9e6d`; GCO embeds weights and includes licences. Cut corner neon gradient borders, stripe symbols and step icons are XAML vector shapes. Independent CET-only/GRSP-only transaction and full 0-Engine version check remain **pending**. Source-only layout commit; real UI screenshots at DPI and [Windows CI #42324708424](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/42324708424) must be checked before acceptance. CET/GRSP/PresentMon native code untouched.

## Latest tested UI checkpoint — 2026-11-07

[`a1c2a54`](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/commit/a1c2a547d2a5c6a60e596bdb7bdd655af107a29f) fixes Page 2 results overlay: leave centered tabs accessible to view, cover former Capture console/footer, set selected tab upon verified restore. [Existing integrated CI #42325021822](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/42325021822) **COMPLETED SUCCESS 179/179**, one bundled .NET10 runtime, exact source-owned portrait pair+full blue skyline, font licence checks and owner CLI install/restore smoke. ZIP artifact `Gs-Cyberpunk2077_Optimizer` ID **13554623325**, size **86,543,595 bytes**. See [compact Page 1](../design/PAGE1-COMPACT-WPF-LAYOUT.md): normal **1220×680** and min **1040×600** WPF DIPs. Real WPF screenshots, 100–150% DPI, game capture, per-domain admission and output optimization generator remain unverified; don't claim them passed.

## 2026-11-07 — Current UI fixes and approved window dimensions

The user reviewed two real WPF screenshots. **1280×760 DIPs normal / 1080×640 DIPs min** replaces the former small baseline; larger size provides breathing space for the complete page. [compact UI source f733383](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/commit/f73338321b6e564fe69e4fd2299169efaa534899) fixes the Game Root panel's oversized 1000-unit vector measure via a 108-DIP constrained Grid, makes its title larger and white above the frame, steepens centered unnumbered CAPTURE / ANALYZE tabs, enlarges GOT LAG?, spaces headings/panels, centers workflow icons, tightens triple slashes and makes the vertical scrollbar a fine yellow line on #060D0F. Original V/blue art and author-owned profiler logic unchanged. **Rajdhani/Oxanium are provisional pending user typography approval**. [Windows CI #42328168943](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/42328168943) is the canonical integrated Windows source-build gate; actual new-window screenshot, minimum/DPI, game capture and independent CET/GRSP backend behavior require separate tests.

### Latest integrated validation — 2026-11-07

[CI #42328168943](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/42328168943) for the adjusted **1280×760 WPF** window and Game Root/tab/icon/scrollbar fix (`f733383`) **SUCCESS 179/179 contracts**. Artifact `Gs-Cyberpunk2077_Optimizer` ID `13555538333`, 86,544,138 bytes. This verifies build/packaging/owner CLI contracts; real WPF rendering, scrolling, DPI and capture/restore need the user's Windows test. User has not finalized typography.

## 2026-11-07 — Current visual frame/source handoff

[Commit b53e2c7](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/commit/b53e2c728a20ef039cd62ab7d62ad7c46baf416e) adds `Visuals/ChamferGeometry.cs` as the **single** 45-degree WPF cut-corner implementation (14 DIP design cut scaled with window); `ChamferOutline` draws the outer thick static neon-gradient/glow border with corner fins plus a tight parallel inner border and standardizes Game Root, workflow, console, Continue, Page 2 and button frames. The V image is inset, clipped to the same geometry and remains the exact owner original. Brand header rule meets the inner chassis. Tabs are now **equal-sized CAPTURE and ANALYZE** without other wording. This changes **only GCO presentation**, no profiler/controller, installer/restore, source-owned CET/GRSP, dependency admission or GCO version. [Windows CI](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/42331860013) must be verified, followed by actual user Windows screenshot at normal/min and 100–150% DPI; accent/glow intensity still needs user visual approval.

### Verified CI — standardized WPF frame, 2026-11-07

The reusable 45° frame + double neon border source from `b53e2c7` failed initial [CI #42331860013](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/42331860013) on **WPF drawing type ambiguity only** (`Brush` conflicted with System.Drawing imported for WinForms folder-browser compatibility). The source-correct alias fix [`c478f9d`](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/commit/c478f9da3ee29c43107be3402bd7132c8006981e) passed [Windows CI #42332115489](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/42332115489) **179/179**, successful CET/GRSP manager smoke, original V/blue art and Rajdhani/Oxanium packaging, one private .NET10 runtime. Artifact ID `13557508098`, **86,547,719 bytes**. Pending: real WPF glow/cut corners, native window screenshots at normal/min and DPI, live gameplay/capture, and independent CET-only/GRSP-only state logic.

### Slim outer-circuit-frame handoff — 2026-11-07

The latest user screenshot calls for subtle circuit-board detail exclusively on the outermost WPF perimeter, with selective glow and no multiple fat parallel border layers. `TechChassisOutline` owns local tabs, black-slotted cutouts, angular node traces and yellow-to-cyan accents while preserving the inner/header frame and all workflow behavior. See [LATEST-WORK](../LATEST-WORK.md). The new outer render remains pending integrated CI and visual Windows/DPI approval.

### Latest visible outer-chassis iteration — 2026-11-07

User's most recent sketch allows a moderately bolder outermost line with real browser-tab-like steps and black negative circuits/dots; it must NOT become multiple nested thick outlines. `TechChassisOutline` has been revised at source with locally widened yellow/cyan structural tabs on top/bottom/sides, black inset circuitry, selective glow and real-size 45-degree steps. The thinner under-header inner frame, original artwork, controls, profiler owner functionality, v1.1.5 and single .NET10 runtime are unchanged. See [LATEST-WORK](../LATEST-WORK.md). CI and user native screenshot/DPI review remain validation gates.

### 2026-11-07 — Current richer circuit-frame visual handoff

The correct GitHub owner/repo is `giorgioherma/Gs-Cyberpunk2077_Optimizations`, working on `main`; the only build entrypoint remains [build-windows.yml](../../.github/workflows/build-windows.yml). User supplied four detailed yellow/black circuit-strips and approved a modestly wider border with **different handcrafted compositions across each edge**, not repetitive stamped tabs. The existing `TechChassisOutline` now draws one 4.4-DIP spine plus irregular filled cyberpunk tabs, black negative artwork, route terminals/stripes and restrained local glow. MainWindow XAML increases inner frame inset and matching content margin to **29 DIP**, retaining the header divider at Y=68, V image touch and equal CAPTURE / ANALYZE tabs. No profiler or managed flow changes. Validate Windows CI, then actual normal/min screenshots and DPI; do not infer render quality from compilation.

### Carved-band chassis handoff — 2026-11-07

The user explicitly rejected a central spine with added modules. Correct model: **one continuous WPF gradient band with material removed by boolean geometry**, no visible guide rails. Current `TechChassisOutline` computes a 3.2–22.8 DIP envelope, subtracts distinct black circuit patterns on each side and caches the resulting frozen WPF geometry; it does not draw colored terminal traces. `MainWindow.xaml` now aligns the thinner inner frame and actual content to 35-DIP margins, preserving black separation and the shared under-header top divider. CI and Windows/DPI acceptance remain mandatory. See [LATEST-WORK](../LATEST-WORK.md).

### 2026-11-07 — Latest carved-band CI fix and width correction

The last carved-band run [#42347339591](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/42347339591) **failed compilation only** on ambiguous WPF/WinForms `Size`; current source explicitly aliases `System.Windows.Size`. Per user's latest design adjustment, the inner frame and content returned to **29 DIP** (not 35), and the original subtractive WPF band width reduced by moving its invisible inner limit to **19.0 DIP** (outer remains 3.2). The user asked for a slightly thinner band and no more inward drift of inner frame. Do not change profiler workflow or add painted guide rails. Verify integrated Windows CI and user screenshots.

### Current denser/carved wiring UI iteration — 2026-11-07

User confirmed the current **15.8-DIP subtractive band thickness** on a native screenshot and requested more variation, including much longer black wires that bend back, cross and sometimes run in parallel. `TechChassisOutline` now combines a small `CutCurve` cubic Bezier-to-negative-geometry helper with its existing `CutTrace` and hand-authored top/left/right/bottom Boolean cuts. Maintain invisible 3.2–19.0 DIP band limits and existing 29-DIP inner frame/content margins; don't drift inward or add centered spines. Build and real screenshot acceptance remain pending.

### 2026-11-07 — Current angular-carving direction after visual rejection

The previous attempt to add *Bézier* wiring (`f0864bd`) compiled but was explicitly rejected by the user. Current source is rebuilt from the earlier visually accepted `bf7afe2` carve-only renderer. New longer `CutTrace` paths use **straight plateaus and 45-degree bends only**, with broken segments to leave colored material bridges. Add more varied fully enclosed cutouts (thin yellow/cyan rims remain on both sides), asymmetrical edge openings, different cavity lengths and drilled terminals. Do not add smooth curves, symmetric attached modules, central spine or painted-over corrective layers. Keep the 15.8-DIP colored envelope (`3.2..19.0` scale DIPs), 29-DIP inner frame and all UI/backend behavior intact. New Windows CI and native screenshot approval required.

### 2026-11-07 — Current outer-chassis assembly iteration

User rejected the fragmented scatter of tiny independent black cuts visible in the `18cea8b` screenshot. The `TechChassisOutline` renderer now composes **four connected design assemblies across the top, three left, three right and four bottom**: three parallel 45-degree fan traces from a shared feed with negative terminal dots; thick-to-thin routed power channels; grouped hatches; shrinking pad ladders; enclosed chamfered windows; and integrated arrows. This replaces the four prior carve methods at source, not a new pass. Keep the fixed 3.2..19.0 scaled-DIP outer envelope, 29-DIP inner margin, dynamic sizing/geometry cache, black circuitry through Boolean subtraction and GCO workflow untouched. Confirm CI then visually judge native Windows screenshots; do not claim design approval from compilation.

### 2026-11-07 — Reference-led carving instead of repetitive circuit icons

The user's latest visual rejection of `0781e5d` is not a compilation issue (Windows CI passed), but a composition issue: too much uniform solid band with small scatter-pattern cuts. The original reference PNG strips were re-examined: their strongest motifs are **substantial black stepped silhouettes**, tapering power bodies, 2 long angled parallel traces and sparse filled/hollow terminal circles, plus one compact hatch-and-pad series. The current `TechChassisOutline` contains three distinctly designed major carving zones along each edge with substantial bare color between, and an annular `CutRing` subtractive helper. Preserve 3.2..19.0 DIP geometry envelope, 29-DIP inner frame, no smooth curves, no repetition/central spine/overlay. Next validate CI and actual Windows render before acceptance.

### 2026-11-07 — Full-canvas owner-painted PNG border and relocated assets

Owner placed `GCO_Border_Regular_1280x760_FINAL.png` and `GCO_Border_Small_1080x640_FINAL.png` in `assets/ui/elements/`, and original V/skyline in `assets/ui/images/`. Procedural `TechChassisOutline` was replaced *at the WPF composition point* by an image-backed outer perimeter, selected by nearest client size. A WPF perimeter clip prevents any nontransparent image background overpainting the application. The 29-DIP inner frame remains generated WPF; no changes to profiler workflows/restore. Builder now copies original five PNGs preserving `images/` and `elements/` with SHA-256, and package contracts check PNG signatures/sizes. Do not claim visual approval until actual Windows screenshots, including scaling, are reviewed.
