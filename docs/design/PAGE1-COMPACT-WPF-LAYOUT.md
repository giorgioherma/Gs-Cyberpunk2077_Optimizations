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
