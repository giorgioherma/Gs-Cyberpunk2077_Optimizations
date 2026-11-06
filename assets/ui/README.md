# GCO approved Page 1 imagery

- `Upright_hotka.png`: original full-color V portrait, 743 × 2117.
- `GCO_V_original_pixel_locked_silhouette.png`: aligned, user hand-painted black V portrait, 743 × 2117; both source files are unchanged.
- **`TrueHeader_GCO_page1.png`**: the new user-authored **2048 × 682 RGBA** blue-to-dark skyline header, committed on `main` as [a241ec2](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/commit/a241ec2485bf5026aa334393de7f35eec3de5216).

This is a wide standalone **background layer**, not a crop from the composite UI mockup. The WPF UI scales it uniformly over the full right panel and lets it extend down from the top header behind real controls. It blends into the approved main surface `#060D0F`; no artificial yellow overlay or hard 150-pixel skyline edge.

The sole Windows builder **requires all three files**, copies them with SHA-256 verification into the package's `assets/ui/`, and intentionally excludes `02_cityline_header.png` (rejected yellow panorama) and the former `GCO_approved_blue_skyline_crop.png` reference. Don't regenerate or replace the originals. The uploaded header is user-owned; WPF drawing/layout and live game validation are still pending.
