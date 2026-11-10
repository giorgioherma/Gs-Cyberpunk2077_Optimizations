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

**Canonical repository:** `giorgioherma/Gs-Cyberpunk2077_Optimizations` `main`; verified owner rename. The public EXE and artifact remain `Gs-Cyberpunk2077_Optimizer.exe` / `Gs-Cyberpunk2077_Optimizer.zip`. Product v1.1.6, one shared .NET10 runtime/WPF app, only `.github/workflows/build-windows.yml`.

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

### 2026-11-07 — PNG skyline test correction

Following the PNG border relocation, CI #42362905376 **failed after assembly and smoke tests**, at a brittle final-artwork skyline assertion expecting an older documented exact PNG resolution. The packaged image is always copied from the owner's `assets/ui/images/TrueHeader_GCO_page1.png` with SHA-256 identity verification at source; test that it is a valid PNG with real positive dimensions rather than imposing an obsolete resolution. Keep the exact two border sizes and 743×2117 portrait pair checks. Validate the subsequent full CI run.

### 2026-11-07 — Border PNG visibility fix (native screenshot evidence)

Although first PNG build succeeded (#42363519634), the user's actual 1280-DIP Windows screenshot showed **no outer border**. The preceding foreground `OuterBorderArtwork` used a guessed 33-DIP perimeter clip, potentially erasing user-painted image parts. The correct rendering is to place the COMPLETE PNG as the **first child of the root Grid**, behind all actual WPF controls; make only the header Grid background transparent so the PNG top side is not hidden. Remove the image clip entirely; preserve the existing 2-image nearest-size selection and the WPF inner border. The root background and content backgrounds still protect layout, and the source artwork stays exact. CI and native screenshot acceptance are distinct.

### 2026-11-07 — Active product version GCO v1.1.6

Explicit owner command advances the **unified GCO package only** from `1.1.5` to `1.1.6`. Current version source of truth: root `Directory.Build.props`; synchronize WPF window title/header, CI package assertions, native launcher version-info resources (substituted by `Build-Test.ps1`), root `VERSION.txt` and adjacent `START-HERE.txt`. Historical handoff paragraphs mentioning 1.1.5 refer to past baselines and do not override the new version. CET, GRSP and PresentMon retain independent exact versions and binaries. Last verified pre-bump CI #42364084944 SUCCESS; run new integrated CI and report separately. Border PNG artwork remains subject to Windows visual acceptance.

### 2026-11-07 — Regression: v1.1.6 PNG border missing despite packaged correct pixels

Owner screenshot of v1.1.6 proved **no visible outer border**. Exact CI #42364636686 artifact contains both valid original PNGs, with bright yellow/cyan RGBA pixels near the outer perimeter; packaging is NOT the issue. In `MainWindow.LoadOriginalArtwork`, the first Source assignment previously went through `UpdateOuterBorderArtwork` which returned when the Image was still zero size. A WPF `Image` with no Source can measure zero; initialize Source before size tests. Added an existing-pipeline CI regression calling `--verify-border-ui` on the **real packaged apphost** to render real WPF content and assert colored pixels. Keep both original user-painted PNGs and the WPF inner frame unchanged. No profiler/gameplay behavior changes; native user screenshot acceptance and CI outcome remain required.

### 2026-11-07 — Auto-refresh after Cyberpunk process exit

`MainWindow.xaml.cs` now monitors **only Cyberpunk process state transitions** every 2 seconds while the WPF window is loaded. On exit/launch transition, invoke the same authoritative `RefreshSourceAsync` status path, deferred when a source-owner transaction is busy. Addresses the case where `COLLECT RESULTS` remains disabled because the desktop window does not regain activation. Does not shortcut evidence/restore proof, or poll owner CLIs every timer tick. v1.1.6 and original artwork unchanged. CI + native user workflow test pending.

### 2026-11-07 — V photograph lower-edge fill, black 45° outline

The approved original V portrait must NOT be moved. The left artwork host is anchored at the same top/left coordinates and extended **16 DIPs downward** using a negative bottom margin; its width increases proportionally at 743/2117 to prevent stretching. `ContentRegion` stops clipping the extension but the right content grid keeps its own clip. One source-owned `PortraitOutline` overlays a thin black stroke on the exact existing `CreatePortraitClip` geometry (left 45° chamfers, straight right edge), completing the black line around the image. No modification to portrait PNGs, outer border PNGs, CET/GRSP, GCO version or frame logic. `--verify-border-ui` now checks top-left pinning, +16 DIP extension, ratio and border pixels in CI. Real Windows screenshot still required.

### 2026-11-07 — Owner corrected portrait/border overlap requirement

User annotations override the previous +16-DIP V extension: V should move **3 DIPs LEFT**, top unchanged, and end flush at the INNER raised 27-DIP+3-DIP baseline, not continue into the chipped OUTER border. Photo retains original lower-left pixels; `CreatePortraitClip(clipLowerLeft:false)` clips only upper-left for image host while `PortraitOutline` draws black 45° around it. Original 743:2117 color/black portrait pairing and transitions remain aligned. Root owner border PNG overlays image raster; WPF inner outline is higher z-order and wins overlaps. `CreateContentFrame` bottom has ONE straight run at first chamfer height (no 3-DIP downward jogs). Existing `--verify-border-ui` checks geometry/layout. No PNG rebuild, new version or game backend changes.

### 2026-11-07 — Correct faulty line-path CI introspection

CI #42372818627 built and smoked successfully (183 contracts) but its newly added WPF geometry assertion incorrectly indexed `GetFlattenedPathGeometry().Figures[0]` on an unfilled `StreamGeometry`, causing an out-of-range exception. That says nothing about border visibility. Replaced the test in the existing app's `--verify-border-ui` with real composed-raster samples along four positions at the desired single raised bottom y. UI geometry, PNGs and product version unchanged. New CI needed.

### 2026-11-07 — Final verified CI after V lower-edge and inner-outline correction

Latest [Windows CI #42373161499](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/42373161499) **SUCCESS**, source `b876c61`, **183/183 package/CLI contracts**, CET/GRSP smoke, actual WPF rendered inner baseline PASS and yellow/cyan painted raster pixels PASS. Artifact `Gs-Cyberpunk2077_Optimizer` ID **13574358681**. Initial #42372818627 failed only because test queried `.Figures[0]` on unfilled WPF geometry; fixed to pixel-level check at four x coordinates. Visual approval on user's native Windows screenshot remains a separate pending gate; v1.1.6, assets and profilers unchanged.

### 2026-11-07 — Portrait meets top of outer decorative bottom band, inner frame frozen

Latest owner screenshot supersedes the previous portrait stop at raised INNER line. Portrait host now has **`Margin="-3,0,0,-8"`**: same upper-left origin, image resized 9 DIPs larger than prior build in height and approximately 3.16 DIPs in proportional width (two original aligned layers, 743:2117). Its bottom meets the UPPER line of the outer PNG bottom border, not the decorative dent below. **Do not touch the inner frame** (`ContentChassisOutline` / `CreateContentFrame`), original PNG assets, outline Z-order or version v1.1.6. The packaged WPF check asserts host overhang +8 relative to content and original geometry checks still apply. Await actual CI and Windows visual confirmation.

**Final check:** Source `5b61541` passed canonical integrated [CI #42374053133](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/42374053133), artifact `Gs-Cyberpunk2077_Optimizer` ID 13574009212. CET/GRSP smoke, original border pixel render and proportional V host geometry PASS. Inner frame untouched. Await user visual screenshot, no bump beyond 1.1.6.

### 2026-11-07 — Final portrait seam and stacking correction

Actual root WPF order must be **owner outer PNG > single V portrait > thin inner outline**. Since nested WPF z-index cannot cross parent siblings, `VPortraitHost` is now the same SINGLE Grid/image pair as a root Grid.Row=1 child, at x=26 (29−3), y=row1 top, bottom inset=21 (29−8), width=proportional V column +3 DIPs. ContentRegion left-column placeholder stays transparent; right-side layout is unchanged. `ChamferClip` uses existing CreatePortraitClip with lower-left cropping **after** normal proportional resizing, so no large shape hacks. `PortraitOutline` is an OPEN black upper/side path: NO bottom black seam or lower-left diagonal. Root z-levels outer=30, V=20, inner=10; inner frame's own geometry/baseline unchanged. Run original `build-windows.yml` and inspect actual Windows screenshot before accepting.

### 2026-11-07 — Single border asset

The sole active master canvas is `assets/ui/elements/GCO_Border_Regular_1280x760_FINAL.png`. Owner replaces artwork under same name/path and removes Small. WPF loads the Regular source once for all resolutions; removed automatic image substitution on size changes. Package builder and tests no longer require or ship the Small raster and test unchanged source reference during resize. Original PNG assets are not modified by this commit. Preserve all profiler ownership, version 1.1.6, V portrait and inner outline. Validate canonical integrated CI and subsequent user asset replacement.

### 2026-11-07 — Single Regular border CI verified

[CI #42377895908](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/42377895908) SUCCESS at `ce838d9`: 183 contracts, CET/GRSP smoke, both-size WPF rendering without source switching and single-asset package proof. Artifact ID 13576457299. **This CI uses the previous Regular PNG**; after owner uploads corrected artwork, new build and native screenshot check are still required. No version change.

### 2026-11-07 — Proportional V +2 DIP on corrected Regular master

Owner updated the sole Regular border PNG in commit `b8e5abb`. V host overhang is now 10 DIPs (`MainWindow.xaml` bottom margin 19, `PortraitBottomOverhang=10`, packaged WPF assertion 10), versus 8 DIPs before. Top/left placement, color/black image overlap, aspect 743:2117, inner border geometry, border art, version v1.1.6 and CET/GRSP binaries stay unchanged. Verify canonical CI and actual native screenshot.

### 2026-11-07 — V lower edge +5-DIP proportional resize

Owner approved a final +5 DIP proportional growth on the corrected single Regular master PNG. `VPortraitHost` bottom margin 19→14, `PortraitBottomOverhang` 10→15, packaged WPF regression 10→15. Same top-left x=26, same pair of `Stretch=Uniform` original V images; width automatically increases by ~1.75 DIPs; border source, layer order, inner frame and app v1.1.6 unchanged. Verify canonical CI and user screenshot.

### 2026-11-07 — GCO v1.1.7 version bump (owner requested)

The owner requested a fresh build and one version bump following a download browser security flag. Update **only unified GCO package version** to `1.1.7` (assembly/file `1.1.7.0`) across `Directory.Build.props`, WPF title/header, integrated builder package/version messages and sole Windows workflow's version contracts; do not change CET/GRSP/PresentMon binaries, game installations, V geometry/PNG or .NET10. Version change does not establish malware detection as false positive or resolve a reputation flag. Verify new CI and separately investigate any repeated detection. Pre-bump [CI #42386209903](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/42386209903) succeeded on v1.1.6.

### Verified GCO v1.1.7 integrated CI result — 2026-11-07

- [Windows CI #42390727451](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/42390727451) **COMPLETED SUCCESS** at `d095c7b89c69333c58aa0b26a058eba00ccc0b73`. Verified full Windows .NET10 integrated assembly, CET/GRSP native owner launch/install/restore smoke, **183 installation/CLI/package contracts**, executable file version `1.1.7.0`, `VERSION.txt` `1.1.7`, root launcher and packaged WPF source. WPF rendered straight raised inner baseline and Regular border at both sizes without swapping raster; sampled yellow RGB(254,245,30), cyan RGB(6,167,175). Packaged output reports `PASS finished GCO v1.1.7 package and complete owner payload manifest verification`.
- Uploaded artifact `Gs-Cyberpunk2077_Optimizer` ID **13581730772**, **86,599,160 bytes**. Two nonblocking `UNICODE` C macro redefinition warnings in launcher and Node dependency deprecation warnings noted, not new failures.
- **Security limitation:** user's browser previously flagged v1.1.6 ZIP. This new CI verifies source/contract integrity, **not a malware scan or browser reputation check**. Do not advise bypassing protection on the premise of successful CI. If the new ZIP remains blocked, capture exact detection/provider and inspect or submit file for analysis before use. No actual game runtime, native Windows approval screenshot, or antivirus verdict established here.

### 2026-11-07 — V bottom overshoot correction (-10 DIP)

Owner's native magnified screenshot of v1.1.7 establishes prior `PortraitBottomOverhang=15` is too large by about 10 pixels. Apply -10 DIP consistently: root `VPortraitHost` bottom margin 14→24 (same left=26 and top=0), proportionally constrained `PortraitBottomOverhang=15→5`, and packaged WPF assertion 15→5. No art edit, no frame change, no profiling change, no version bump. Validate full Windows CI; actual seam still needs owner acceptance.

### 2026-11-07 — Intentional +1 DIP V/outer-frame overlap

Owner clarified that a slight V/border overlap hides a rasterized dark hairline better than exact edge-to-edge positioning. Current GCO v1.1.7 moves V overhang **5→6** DIP by reducing root host bottom margin **24→23**; the existing WPF test checks extension **6**. No new clip, PNG modification, border z-order change, separate image resizing, or version bump. Both original V layers retain their proportional alignment. Validate single Windows workflow and owner screenshot.

### 2026-11-07 — V +1-DIP overlap verified CI

- [Windows CI #42394574929](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/42394574929) **SUCCESS**, source `8a745ba571a154b3ff6c9e3db0e36e0fd7109672`; integrated CET/GRSP smoke, **183 installation/CLI/package contracts**, packaged WPF baseline + unchanged Regular bitmap at two sizes PASS. Pixel probes: yellow RGB(254,245,30), cyan RGB(6,167,175). Artifact `Gs-Cyberpunk2077_Optimizer` ID **13582852649**, size **86,599,160 bytes**. The intentional ~1-DIP overlap has been implemented and geometrically validated, but physical Windows screen/DPI appearance still requires the owner's screenshot. Version remains **v1.1.7**.

### 2026-11-09 — Versioned download artifacts, unchanged product v1.1.8

- GitHub Actions used a generic artifact filename, so different builds downloaded as `Gs-Cyberpunk2077_Optimizer.zip` despite embedded `VERSION.txt`. Offline inspection of user's upload confirmed it was v1.1.7. New existing-workflow upload name includes v1.1.8 and source run/attempt; `BUILD-INFO.json` records commit/run/attempt and the guide states current version in the first line. Same portable root launcher/folder and component payload. Run integrated CI before claiming completion. The ZIP warning remains unclassified without Firefox's precise explanation or a vendor detection report.

### 2026-11-09 — Owner-painted V and pure-black background

- Source commit 7d95d081 replaces current WPF art references with 482x928 V_background_1_black/color.png, while retaining the former narrow sidebar, old V top/left/height and lower border overlap. The 482:928 full width now extends right with user-painted transition and no generated WPF halo/mask/right outline. Owner updated existing TrueHeader_GCO_page1.png to RGB 1870x652; the 174-DIP host is unchanged. WPF canvas/Game Root/skyline fade are #000000. Existing unified CI must verify packaging/WPF; owner must confirm native appearance. No v1.1.8, profiler, shared runtime or workflow changes.

### Verified true-black V background artifact — 2026-11-09

- Code 4b45b4bb6a5e07917f9d726b5ea6199019671319 passed canonical [Windows CI #42623681521](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/42623681521), **184/184** package/source tests and actual offscreen WPF checks for new V pair/header, black color and minimum/resized layout. Artifact ID 13677361617, 85,261,782 bytes, versioned filename Gs-Cyberpunk2077_Optimizer-v1.1.8-run173-a1.zip. V is inside ContentRegion above skyline, below real controls, letting wide yellow fade pass behind UI instead of covering text. Real owner Windows screenshot and gameplay capture remain separate acceptance. One workflow, v1.1.8 and profiler binaries unchanged.

### Final verified Page 1 visual layout — 2026-11-09

- New owner-approved composition source `8bedbcc939d561dc6381fb4f972d41efd53879c4` passes [Windows CI #42628830845](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/42628830845) **SUCCESS 184/184**, artifact 13680655577, `Gs-Cyberpunk2077_Optimizer-v1.1.8-run175-a1.zip`. V/puddle flows **above thin inner chassis, below original outer PNG and below right-side controls**. Tabs move into top band, GOT LAG moves up, Game Root shifts 11 DIPs left, inline StatusText replaces framed console/second slash, and Page 2 remains inset to retain its frame. Actual offscreen WPF checked normal/min/resize, z-order, and result-frame pixels. Product v1.1.8, 2-page behavior and source-owner profilers unchanged. Owner screenshot acceptance outstanding.

### 2026-11-09 — Verified owner-only puddle overlap, no full V overdraw

- Latest source `20f24767a2a6dcc8303b68db23651dad788f3c86` [Windows CI #42646172031](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/42646172031) **SUCCESS 185/185**, artifact **13686974054**, downloaded filename `Gs-Cyberpunk2077_Optimizer-v1.1.8-run177-a1.zip`. User's `assets/ui/images/The overflow.png` **365×49 RGBA** is shipped byte-identically, scales with the V's 482×928 source height and aligns exactly to V's left/bottom; only this strip is above inner line (root z14), the complete V remains beneath frame z13, right UI and skyline in ContentRegion z12, outer raster z30. V shifts x26→x28 to reveal inner left x27 line. Actual Windows WPF pixel probe shows 293 darkened inner bottom-line pixels with strip while x27 yellow left line remains visible. The owner has not yet approved the new physical screenshot. GCO remains v1.1.8; no profiler changes.

### 2026-11-09 — GCO visual polish with V/skyline-anchored global neon

- User's current requested Page 1 source `6beddb1870b743fc8ddef2ab25ae3e17a25d50cb` passed [Windows CI #42679052240](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/42679052240) **SUCCESS**, 185/185 install/CLI/package contracts and real WPF offscreen layout verification; downloadable artifact `Gs-Cyberpunk2077_Optimizer-v1.1.8-run180-a1.zip`, ID **13701687127**. The first #179 compile failed because of duplicated verifier local `first`, fixed at source before this success. Owner pictures, .NET10 shared runtime, native owner binaries and UI workflow unmodified. Artifacts are compile/layout evidence only, not game-capture proof.
- Header: remove temporary GCO logo lettering, preserve logo slot, align app title/cyan `CET and REDscript Optimization` text and right REFRESH + version. GOT LAG headline and white description now shift together +62 DIPs; Game Root stays original x. Game Root input contents vertically center, font shrinks to 13.5 and path resets horizontal view to start after focus leaves; do not interrupt typing. Dependency yellow normalizes to #FFF000; workflow steps center content (not 01/02/03). Continue nudges 25 up, 10 left. Thin inner frame and card outlines use one WPF screen-space V-yellow/skyline-cyan gradient; no repeated box-center transition or raster edits. Tested spatial gradient card indices 0.29/0.54/0.79 plus min layout.

### 2026-11-10 — Canonical GCO CI policy (supersedes historical build-trigger notes)

- Keep exactly `.github/workflows/build-windows.yml`; normal `main` source pushes run only Windows .NET10 compilation and core contracts, **not** CET/GRSP downloads, private-runtime packaging or a downloadable ZIP. Documentation-only commits do not trigger a Windows job. `workflow_dispatch` explicitly performs the original complete single-runtime integration, source-owner CLI install/restore smoke, packaged-WPF smoke, provenance/manifest, ZIP verification and one artifact. Read version from `Directory.Build.props` through MSBuild. No other workflow or alternate builder.
- Owner correction of the logo's placement in `617f9105` surfaced [run #188](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/42748365602), which compiled and passed 185 source contracts but **failed on an obsolete CI assertion for the previous logo X**. Avoid arbitrary visual design coordinates, color samples and dimensions as CI gates. User screenshots approve appearance; packaged WPF smoke checks assets decode, core controls lay out and both screens render, without hardcoded positions.
- Last complete green portable ZIP before the policy change: [run #187](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/42747108217), artifact **13732365520**, v1.1.8. New fast CI/check result and subsequent manual package are distinct; neither proves gameplay, source optimization, or CET-only/GRSP-only capture.

### 2026-11-10 — Owner restored original automatic full-build CI policy

- The owner explicitly reversed the experimental source-only push / manual-only packaging split. Relevant `main` commits once again launch **all stages** of the canonical `.github/workflows/build-windows.yml`: verified owner checkouts/packages, complete GCO .NET 10 build, integration/restore tests, native launcher and shared-runtime/manifest/package checks, then artifact upload on success. Manual dispatch continues to do the same work; no alternate lane or second workflow. Pure documentation commits remain excluded.
- Keep the newer **high-value CI contract admission standard**: test real critical behavior and outputs, not subjective styling or incidental constants. The old 600-line decorative pixel verifier remains replaced by compact real packaged WPF smoke. No version bump.
- The same-day source commit `706d56a8` inadvertently removed three `Margin` attribute names in the WPF button template and failed source CI #191 (XAML MC3000 at line 99). This correction restores only those attribute names while preserving the owner's new logo margin and styling. Full restored integrated push CI must be checked before reporting a new artifact.


### 2026-11-10 — GCO v1.1.9 version centralization

- Owner approved **GCO v1.1.9** and requested product version be maintained in just one place: `Directory.Build.props`. WPF reads the compiled assembly version for its title/header; the existing integrated build script and single Windows workflow resolve version through MSBuild. File and assembly versions are derived from `VersionPrefix`; artifacts inherit verified package version. CET v1.3.4, GRSP v1.0.0 and PresentMon v2.6.0 are independently versioned and unchanged. Full auto-build CI to verify before calling the new release available.

- **Verified:** automatic [Windows CI #195](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/42765559887) from `e1d82a9f61eb331342de3dbf90862abe8648bee9` **SUCCESS**, 185/185 contracts; GCO v1.1.9 downloadable artifact **13738789129**. The product version is centralized without changing owner component versions.

### 2026-11-10 — Owner-approved Page 1 final layout and simpler cyan icons (source CI pending)

- Exact final GCO logo placement from latest owner `main` is immutable for this pass. Raise GOT LAG title and description 14 DIP while compensating bottom spacing; Game Root moves 30 DIP right and loses 30 DIP width from its left. Capture Workflow heading and **one** three-card parent region align with that shortened Game Root; no double shifting each card. Narrow each of three cards 12 DIP total through available column width/insets, preserving non-reduced inter-card spacing.
- Keep action buttons, tabs, V/skyline, chassis and captures unchanged. Remove folder symbol, use flat card fill, yellow 1/2/3 page markers and original rendered glowing bold cyan download, game controller and layered-data WPF icons. Capture description now warns against menus, inventory and pausing; Collect describes continuing to Analyze. Update details in `docs/LATEST-WORK.md`. Existing Windows pipeline must validate compiling XAML, runtime package and integration; user Windows screenshot judges aesthetics. Keep v1.1.9.

- **Verified latest UI build:** source `f9f3194d`, automatic [Windows CI #202](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/42778746419) **SUCCESS 185/185**, artifact **13744838319**, GCO v1.1.9. New cards/icons and single-shift capture layout packaged without changing logo or native owners; user screenshot acceptance remains pending.
