# GCO visual direction — 2026-11-06 user asset review

This is an implementation map, **not** a new app/page/installer architecture. Preserve `docs/product/UNIFIED-TWO-PAGE-WORKFLOW.md` and later explicit decisions. All provided raster art is *visual source material*, not working UI or an approved bitmap of controls. The artwork is user-provided and may be reviewed locally without importing third-party mod source.

## Source assets reviewed (local user archive, not yet committed)

- `IMAGES-TOTAL.zip`: `Upright_hotka.png` (743×2117 vertical character art), `Header_image.png` (2120×742 city scene), `What_are_you_on.png` (GOT LAG? branding), `01_Profiler_Station.png`, `02_Night_City_Overlook.png`, `03_Rain_Combat.png`, `04_Cybernetic_Head.png`; the art pack also contains `GCO_Installer_Kit.zip` and the user's hybrid ICO.
- `GCO_Installer_Kit.zip`: `18_sidebar_original_V_278x640.png`, `02_cityline_header.png`, `22_installer_background_clean_860x640.png`, `24_installer_mockup_860x640.png`, `19_divider_transparent_570x16.png`, `20_corner_tech_transparent_110x70.png`, `21_hud_dots_tile_256.png`, `12_gco_wordmark.png`, `13_got_lag_wordmark.png`. Source `README_FOR_AGENT.txt` requires real buttons/text layered over clean backgrounds, >=16px control padding, DPI-aware scaling, aspect-correct portrait and no generated promotional boilerplate.
- Hybrid icon `GOT_LAG_Hybrid_16-24_brush_32plus_V.ico` has multi-size entries 16, 24, 32, 36, 40, 48, 64, 96, 128 and 256 pixels. It is the single brand icon, **not** the splash art.

## Retain .NET 10 WinForms first

- Styling may be painted in a thin shared layer: central palette, custom separators, square/cut-corner borders, status chips and a compact stage layout. Do not alter existing native installer/restorer, ETW, capture IDs, or backend/service contracts.
- WinForms controls remain functional and accessible. Do not represent real buttons, folder paths, state, progress, captured data, selection or error text as pixels in a background PNG. Validate scaling at 100%, 125%, 150% and keyboard focus/contrast; enable responsive minimum widths, not clipped labels. No need for GPU textures/animations in a profiler utility.
- If a future design truly requires extensive animations/vector manipulation, compare WPF behind the same orchestration/core boundary. **Do not migrate frameworks speculatively.**

## Page 1 — Capture (one game folder, no new onboarding/installer page)

- Overall palette: yellow #FFF000 for headings/current CTA, very dark #070B0D base, cyan #00E2F2 for thin technical outlines and informational details, green only for verified success, amber for missing/blocked, muted gray for unavailable steps.
- Header: compact GCO mark, `GOT LAG?` visual motif with restrained Night City skyline, 1 Capture / 2 Analyze & Optimize navigation **without creating more pages**.
- Optional left sidebar: preserve the portrait's full aspect ratio and use only on wide enough windows. Do not let it reduce the game root input or workflow cards to unreadable widths. On smaller windows, collapse/defer the sidebar art before hiding controls.
- Game root: one clear selector. Existing Normal/MO2 control stays; MO2 remains disabled until verified. Show current source detection in one compact status line.
- Primary content: three numbered action cards **Install → Start Capture → Collect**. Current step has yellow emphasis, completed steps green, unreached steps muted. Exactly one obvious enabled next action. When step 3 finishes, `CONTINUE` is the primary action; it retains the owner-original-restore contract. Never imply an installed PresentMon game mod.
- Right/secondary strip: CET and REDscript *installed/capture* states, PresentMon *automatic/recorded/failed* state. Use live owner/CSV proof; green check means verified, not merely binary present.
- Replace the huge permanent rules block with 1–2 sentences: `Install → Start Cyberpunk → F11 start/stop → Close game → Collect → Continue`. Put detailed instructions in the outer `START-HERE.txt`, with optional app Help or expandable `How to capture`. Do not hide actionable failure/safety guidance behind unexplained codes.
- Preserve independent evidence collection and explicit skip checkboxes for incomplete domains. Avoid redundant successful-operation modal dialogs.

## Page 2 — Analyze & Optimize (same page, source-proof only)

- Results-first hierarchy: verified CET and REDscript evidence, optional PresentMon FPS/framerate and tail latency only when captured/verified, key findings/owner ranking, then a single Generate Optimization ZIP primary action once the actual generators/compilers pass.
- Keep user-approved visible `Analyze`, `Generate`, `Open RESULTS` actions and existing safety gates. The concept art's sample FPS, CPU/GPU-bound classifications and promised gains are **placeholders only**, never hardcode or claim them.
- Preserve current source inventory authority, CET fixed generator order, REDscript generic-first compilation proof, per-owner source drift/rollback and Vortex-friendly single game-relative optimization ZIP. The visual art must not change these contracts.

## Brand icon and release structure

- Commit one master ICO to `assets/brand/GOT_LAG_Hybrid_16-24_brush_32plus_V.ico` before wiring it. Embed that file via Windows `rc.exe` `IDI_ICON1 ICON` in the **public native launcher**, and `<ApplicationIcon>` in the internal WinForms application. Assign `Form.Icon` from the embedded resource; taskbar/title bar/Alt+Tab/Explorer should resolve from the embedded icon. Shortcuts, if added later, should point to the public exe; do not ship copies of the ICO scattered across folders.
- Artifact `Gs-Cyberpunk2077_Optimizer.zip` (one integrated workflow) layout:
  ```text
  START-HERE.txt
  G's Cyberpunk 2077 Optimization/
    Gs-Cyberpunk2077_Optimizer.exe
    app/
    components/
    runtime/
    RESULTS/
    VERSION.txt
  ```
- **The outer START-HERE file is not a game optimization ZIP file.** Never include README, profilers or UI assets in the generated game-relative optimization pack.
- GitHub repository rename is an account-owner Settings action; update links, documentation and hardcoded references after actual change. Keep a single canonical workflow.

## Implementation order / acceptance

1. **Brand identity/delivery** — rename public exe, artifact and workflow; adjacent quick-start guide; CI checks ZIP topology/version and unchanged source owners.
2. **Icon** — commit original ICO and verify both binary icons at 16/24/32/256, title bar/taskbar and Explorer with Windows GUI smoke.
3. **Page 1 read/reflow** — yellow-led palette, compact instructions, 3 stage cards, truthful status, safe operation and recovery, 100–150% DPI screenshots.
4. **Page 2 results** — only real metrics; preserve disabled optimizer until compile/output proven.
5. **Manual end-to-end tests** — live game install/capture/collect/restore, missing-domain skips, PresentMon CSV frame proof, partial retry, no stale capture confusion.
