# GCO Page 1 — authoritative artwork

- `Upright_hotka.png` — original user-supplied full-color V, **743×2117**.
- `GCO_V_original_pixel_locked_silhouette.png` — aligned user hand-painted black V, **743×2117**.
- `GCO_approved_blue_skyline_crop.png` — small **blue Night City motif extracted from the APPROVED full Page 1 mockup** (`UI_DESIGN-fire.png`, crop x=1226..1380/y=127..296). It was **not** supplied or designed as a separate standalone header. The WPF window displays the crop at the right in the correct aspect ratio, not stretched over the header.

**Do not use `02_cityline_header.png`: it is an unrelated yellow panorama mistakenly packaged earlier, not part of the approved design.** Retain only as unselected source material until the user requests removal; the current builder explicitly ignores it.

The correct cropped blue skyline is included in `GCO-WPF-approved-blue-skyline-assets.zip` delivered in chat but **has not been committed to GitHub yet**. The build packages it when added. Missing skyline must render a dark background, never fall back to the yellow design. The original V pair has already been committed to `assets/ui/`. Do not regenerate the user's V art or claim an exact match without a visual Windows test.
