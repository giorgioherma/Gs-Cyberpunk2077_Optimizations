# GCO Capture Page — compact WPF layout checkpoint (2026-11-07)

**Status:** user-requested UI redesign has a **first source implementation**, not a final visual approval. [Implementation commit 33aca3f](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/commit/33aca3fac2a0278435c4c1cf5a15afbf30455424). Canonical repo `giorgioherma/Gs-Cyberpunk2077_Optimizations`, `main`, GCO v1.1.5; original CET/GRSP/PresentMon and native restorers are unchanged. [Approved original constraints](PAGE1-APPROVED-IMPLEMENTATION-PLAN.md).

## Window size decision — first test baseline

- **Normal:** `1220 × 680` WPF device-independent pixels (DIPs), including native Windows window chrome.
- **Minimum:** `1040 × 600` DIPs. Main content scrolls vertically on smaller usable areas and Windows DPI scaling. Test screenshots at 100%, 125%, 150%; do not shrink typography and click targets below legibility. The user may request an adjustment after actual Windows preview.
- The original V sources are 743 × 2117. Keep `Stretch=Uniform`, dynamically size sidebar to `(available height × 743/2117)`, clamp 170–245 DIPs. **Never use UniformToFill** (original screenshot cropped the sides). Text/borders/cards are actual WPF and scale independently.
- Skyline is `assets/ui/TrueHeader_GCO_page1.png` in the right panel, below the new title band. Preserve background/source proportion, fade to `#060D0F`. Yellow light stays independent and may be tuned after screenshot tests.

## Locked order and first-pass sizing

| Region | First-pass height / behavior | Actual content |
| --- | --- | --- |
| 0. Brand/title gap | **68 DIP**, full width | GCO logo placeholder + `G's Cyberpunk 2077 Optimizer` in Oxanium; logo asset still to be provided, small build version and reduced-flash control. Skyline starts below this gap. |
| 1. Page tabs | **54 DIP**, centered over skyline | Larger parallelogram `1 CAPTURE` / `2 ANALYZE & OPTIMIZE`, tabs only indicate current navigation (Page 2 remains capture-and-restore gated). |
| 2. Headline | Auto / approximately 65 DIP | **Yellow:** `GOT LAG?` **white:** `Cyberpunk 2077 CET and REDscript Optimizations`; smaller white capture/analyze/generate subtitle. |
| 3. Game Root | Auto / approximately 114 DIP | Game-folder editable path + Open + top-right Refresh; global Direct CET/REDscript effective counts; 0-Engine/RED4ext source notice; compact CET/REDscript/PresentMon status; MO2 later. |
| 4. Section label | **20 DIP** | Three cyan slashes + `CAPTURE WORKFLOW`. |
| 5. Three cards | **204 DIP**, 3 columns | Each with step number, **WPF vector icon**, Oxanium title, Rajdhani short instruction, **state-driven button inside**. No separate instructions panel or Tool Status card. |
| 6. Console and CTA | **63 DIP**, pinned bottom | 3 cyan slashes, truthful status text + conditional restore, **yellow cut-corner Continue** (native evidence/restoration gates unchanged). |
| Main outer frame | Whole content | WPF vector cut-corner outline, yellow-to-cyan gradient; no image overlay or border PNG required. |

At the normal size, all primary controls are intended to be available without vertical scrolling. At the minimum size, the path/steps area scrolls; the console and Continue remain pinned. The native Windows title bar remains until a user-approved custom logo/chrome replacement; no unsafe imitation of system controls is added.

## Typography, palette and art

- The user committed [Rajdhani and Oxanium](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/commit/7ca9e6d4a7944f9f3bc618c9984325bfa1ab55c4). **Rajdhani** is all ordinary/instruction/status text, **Oxanium** is titles, tabs and buttons. The actual static weights are embedded as WPF Resource entries in the GCO app; OFL notices are copied to the portable package. No system-wide font installation or separate runtime.
- Retain user palette: **`#060D0F`** main background, **`#081012`** 3 cards, **`#11191A`** path, **`#151C1C`** button surface. Primary yellow `#FFF000`, cyan `#00E2F2`.
- **Vector-only visual effects:** WPF `Path` geometry creates clipped/sloped/cut corners, `LinearGradientBrush` creates yellow-to-cyan border strokes, simple vector `Path` makes the three diagonal cyan stripes and the step icons. Borders are resolution-independent, not baked into an image. Keep effects static to limit GPU during game.
- **Exact original images:** V full color + user's black silhouette and full blue-to-dark header unchanged. Do not alter pixel content or copy an unapproved yellow banner. Existing V blackout/glitch during capture and collected-color state remain source-owner evidence driven.

## Safety and validation

- **Only the frontend and font packaging changed.** `UnifiedProfilerWorkflow` still requires both managed CET/GRSP profilers; CET-only/GRSP-only eligibility, full 0-Engine version proof and source-safe attempt retry remain **pending**. Neither a green build nor visual labels prove these are complete.
- Expected test matrix: layout screenshot with `1220×680` default and `1040×600` min, Windows DPI 100/125/150%, full-side V width, tab legibility, no clipping of status/errors, staged button-state behavior, reliable original-owner recovery, reduced-motion performance and real game capture. Observe no more idle rendering than needed.
- Use **only** `.github/workflows/build-windows.yml`; no new workflow, duplicate app, second runtime or GCO version change. CI [#42324708424](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/42324708424) was **in progress at documentation authoring time**. Verify conclusion before claiming success.

## First build result / next visual acceptance

- User font commit `7ca9e6d`; first compact source `33aca3f`; Page 2 tab/footer overlap correction `a1c2a54`.
- **Existing integrated CI [#42325021822](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/42325021822) COMPLETED SUCCESS, 179/179** with exact V/blue header art and WPF font licence packaging. Artifact `Gs-Cyberpunk2077_Optimizer` ID `13554623325`, compressed size **86,543,595 bytes**. Only compilation, CLI contracts, packaging, manager smoke are proven.
- Request **two actual Windows screenshots**: normal **1220×680 DIPs**, and minimum **1040×600 DIPs**. Also verify 100/125/150% DPI, full portrait composition (no side crop), reading all status/errors, active tab after Continue, button adaptive states, correct original portrait transitions, cut corners/yellow-blue neon borders and no noticeable game-focused rendering overhead. Adjust after objective user screenshot feedback.

## Superseding user screenshot decisions — 2026-11-07

- **LOCK NOW:** normal **1280×760 DIPs**, minimum **1080×640 DIPs**. Replaces 1220×680/1040×600 above, which were an earlier, undersized prototype; retain vertical scrolling at minimum but aim to show all elements comfortably in normal size.
- Actual screenshots exposed a source-layout defect: the Game Root border was a 1000×1000-unit WPF `Path` inside an unconstrained `StackPanel`; it consumed the page. [compact UI source f733383](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/commit/f73338321b6e564fe69e4fd2299169efaa534899) constrains `GameRootPanel` to **108 DIP height** with only its path input, global counts, prerequisite line and compact per-tool statuses, ending immediately after the last line. Put the bigger white `/// GAME ROOT / CYBERPUNK 2077` title ABOVE its border. Refresh remains outside above/right.
- Align its frame with the bottom of the full user-authored blue-to-dark header art. Larger **GOT LAG?** text, more space between heading/subtitle/panels; keep the second line white. Tabs are **CAPTURE** / **ANALYZE & OPTIMIZE**, no numbers, steep WPF parallelogram cuts. All three workflow icons centered above instructions with step numbers left.
- The original cyan **///** is now a tighter vector path at Game Root, Capture Workflow and console. At minimum window height, use a custom **yellow 3-DIP scroll thumb** over `#060D0F`; keep scroll wheel/keyboard functionality. No PNG mask or image border. Continue remains distinct, source-gated.
- Rajdhani body + Oxanium headings/buttons stay **provisional** as the user is not yet sure about the typography. The initial fixes have not passed a user visual acceptance test; [Windows CI #42328168943](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/42328168943) builds source `f733383`, and actual Windows screenshot/DPI/gameplay are still required.

### Compilation and package proof for revised sizing

[Existing Windows CI #42328168943](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/42328168943) completed **SUCCESS 179/179** on `f733383`, including one shared .NET10 runtime and original blue skyline / V art. Exact release ZIP artifact ID `13555538333` (86,544,138 compressed bytes). UI and performance acceptance require an actual Windows screenshot; CI cannot prove the Game Root frame, tiny neon scrollbar, angled tabs or font appearance renders as intended.

## 2026-11-07 — WPF chamfer/glow system v1 (first screenshot iteration pending)

- **Single source geometry:** `ChamferGeometry.Create` computes points from each element's actual WPF `RenderSize`; a standardized **14 DIP base cut** scales with 1280×760 normal / 1080×640 minimum window. X-cut equals Y-cut at all sizes, giving a genuine **45° clipped corner**; small controls clamp so the corners cannot overlap. Do not use `Stretch=Fill` on normalized rectangle Path frames again.
- **Outer border:** ~3.6 DIP multicolor stroke with two low-opacity static glow strokes (~6 and ~12 DIP wider) and prominent gold top-left/cyan bottom-right line fins; drawn inside the client edge. **Inner border:** ~1.8 DIP parallel line inset 10 DIP, leaving a narrow #060D0F gap and connecting visually with the rule under the GCO logo. The native Windows title bar is *outside* the client area and intentionally unchanged.
- **All box edges:** common chamfer outline for Game Root, 3 large capture cards, console, special yellow Continue, card buttons and Page 2 reports. Preserve the path input as a standard editable text field. Body/stage UI still adapts/scrolls at minimum size and retains real controls. No image overlays/PNG frames and no continuously animated borders.
- **Portrait:** original 743×2117 paired images inset by several DIPs and clipped with same chamfer outline, so it fits neatly between the frames; do not redraw or change the image files.
- **Tabs:** both **246×44 DIP**, steep 45° real parallelograms; labels **CAPTURE** and **ANALYZE**. Existing Page 2 gate remains. User visual review should focus on glow intensity, border line joins and the black gap against the original V art and blue header.
- [Source commit](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/commit/b53e2c728a20ef039cd62ab7d62ad7c46baf416e); [Windows CI #42331860013](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/42331860013) pending at writing. Actual screenshots, 100/125/150% DPI and gameplay must still be checked.

### Source/build status — user double-frame variant

- Actual-size 45° geometry first source `b53e2c7` required one WinForms/WPF type-alias correction at source (`c478f9d`). First CI #42331860013 failed WPF compilation; **[new CI #42332115489](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/42332115489) SUCCESS 179/179**. Artifact **13557508098**, 86,547,719 bytes. User visual feedback on the new outer/inner double frame, 45° cut consistency and tab size remains required; do not infer final rendering success from green compilation.
