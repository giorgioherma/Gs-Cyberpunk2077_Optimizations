# GCO approved Page 1 imagery

- `Upright_hotka.png`: original full-color V portrait, 743 × 2117.
- `GCO_V_original_pixel_locked_silhouette.png`: aligned, user hand-painted black V portrait, 743 × 2117; both source files are unchanged.
- **`TrueHeader_GCO_page1.png`**: the new user-authored **2048 × 682 RGBA** blue-to-dark skyline header, committed on `main` as [a241ec2](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/commit/a241ec2485bf5026aa334393de7f35eec3de5216).

This is a wide standalone **background layer**, not a crop from the composite UI mockup. The WPF UI scales it uniformly over the full right panel and lets it extend down from the top header behind real controls. It blends into the approved main surface `#060D0F`; no artificial yellow overlay or hard 150-pixel skyline edge.

The sole Windows builder **requires all three files**, copies them with SHA-256 verification into the package's `assets/ui/`, and intentionally excludes `02_cityline_header.png` (rejected yellow panorama) and the former `GCO_approved_blue_skyline_crop.png` reference. Don't regenerate or replace the originals. The uploaded header is user-owned; WPF drawing/layout and live game validation are still pending.

## One full-size master border (2026-11-07)

- **Only active source:** `assets/ui/elements/GCO_Border_Regular_1280x760_FINAL.png` (1280 × 760).

The owner may replace this PNG under exactly the same path and filename. The app loads it once and uses `Stretch="Fill"` plus high-quality bitmap scaling at every window size; no live image replacement can occur when resizing. The Small variant is obsolete, optional in the repository and **not packaged**. The integrated builder copies the Regular source to the unchanged package path with source-to-package SHA-256 verification. The original V portrait pair, skyline, inner WPF outline and all profiler behavior are unchanged. Screenshot/DPI acceptance remains pending.
