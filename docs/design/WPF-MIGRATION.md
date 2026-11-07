# GCO WPF migration handoff — 2026-11-06

**User-approved:** switch the **GCO frontend** to native **WPF on .NET 10** now; preserve GCO Core and authoritative CET/GRSP owner managers/installer/restore, plus PresentMon; use **existing `.github/workflows/build-windows.yml`**. The previous WinForms artifact does not need byte-for-byte preservation. Maintain two pages, one private Windows Desktop Runtime, GCO v1.1.5, and approved launcher/ZIP names.

## Implemented

- `src/GTotalOptimizer.App/MainWindow.xaml` and `MainWindow.xaml.cs`: live WPF two-page shell with black/yellow/cyan layout, blue-skyline image slot, full-height V image pair slots, Game Root Refresh, global Direct CET/REDscript counts, tool statuses, Install / Start Capture / Collect, Continue/recovery, Page 2 report containers and disabled unimplemented optimizer actions.
- Original **black silhouette → irregular color flashes → permanent full color** as capture/collection-driven state; 30ms-capable bursts, enforced long dark intervals, more activity during Collect, reduced-motion checkbox. Window deactivation stops the timer to avoid consuming game GPU/CPU. Graphical state is not a success signal.
- `Program.cs` preserves original `--verify-package` CLI and elevated `--presentmon-control` **before** constructing `System.Windows.Application`; headless original workflow still operates.
- `GTotalOptimizer.App.csproj`: `UseWPF=true`, `UseWindowsForms=true` **only** for native folder browser; legacy `MainForm.cs` remains in Git but is explicitly excluded from compilation. `UserSettings.cs` retains old persisted JSON preferences. No duplicate live UI, no source-owner installer rewrite and no new runtime.
- `integrations/three-profiler-test/Build-Test.ps1` in the **single existing** Windows workflow checks WPF and copies exact user art if all three images exist under `assets/ui`; CI currently builds functional controls without the uncommitted images.

## Verification

- First WPF CI [#42275920638](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/42275920638) failed `CS0262` (window class accessibility); fixed in `ad87d72`.
- Second WPF CI [#42276583524](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/42276583524) failed missing IO implicit imports and WPF/drawing aliases; fixed in `3fe24fb` with explicit global `System.IO` and WPF media aliases.
- **Green:** [Windows CI #42277190679](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/42277190679), source `3fe24fb59c5846ead0823e547d029698eb1ac9ef`, **179/179** contracts, CET and GRSP owner CLI smoke, private runtime and public launcher checks, artifact `Gs-Cyberpunk2077_Optimizer` ID **13532694947**. **81,871,453 bytes** compressed vs 81,883,798 previous WinForms artifact; this is not proof of actual idle memory/GPU overhead. WPF actual window/display/gameplay have not been tested on user Windows yet.

## Remaining work

1. Add **exact**, user-provided original `assets/ui/Upright_hotka.png`, `GCO_V_original_pixel_locked_silhouette.png` (both aligned 743×2117) and `02_cityline_header.png` (BLUE skyline). Their source is the conversation uploads, **NOT yet GitHub**, and substitutions are prohibited. Restore hybrid ICO from user original separately.
2. Test actual WPF launch, interactive layout, original images, 100/125/150% DPI, keyboard access, reduced motion, focus/background idle CPU/GPU, native capture/restore on Windows. Remove old `MainForm.cs` only after parity is proven.
3. The backend currently still has `UnifiedProfilerWorkflow.BothInstalled`, `CleanForInstall` and frame-inclusive `CollectedCaptureStore.HasAny`; **independent CET-only/GRSP-only** admission and full GCO **0-Engine v0.17.2+** version proof remain pending. WPF UI only blocks PresentMon-only Continue; do not misrepresent the backend as complete. Implement safe Return/retry/owner restore separately.
4. MO2 stays disabled. GCO unified CET+GRSP analysis and new REDscript Resolver/Generator remain future source-validated milestones.

**No new workflow, packaged runtime, native owner DLL, optimization generator or GCO version change.**

## 2026-11-06 — Blue city skyline asset correction

User clarified the approved **BLUE upper-right skyline was generated inside the full Page 1 design mockup**, not provided as an independent original header. The previously selected `02_cityline_header.png` is the **wrong, yellow panoramic header**. That erroneous mapping is removed from WPF's `MainWindow.xaml.cs` and from `Build-Test.ps1`. The WPF city slot now expects **`GCO_approved_blue_skyline_crop.png`**, extracted from the user-approved `UI_DESIGN-fire.png` composite (x=1226..1380, y=127..296), displayed at natural aspect ratio on the upper right. The corrected asset ZIP was provided in chat but the cropped binary still needs to be uploaded to `assets/ui`. The approved two original V files are present in GitHub; the original yellow panorama remains unselected/ignored, not silently deleted. Missing blue asset produces a dark header rather than a yellow substitute. Windows visual verification required.

## 2026-11-06 — Full user-supplied blue skyline supersedes temporary crop

- User added [`a241ec2`](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/commit/a241ec2485bf5026aa334393de7f35eec3de5216), source `assets/ui/TrueHeader_GCO_page1.png`, **2048×682 RGBA**, already blended blue on the right to near-black on the left and bottom. This is the authoritative header asset, **not** the small excerpt from `UI_DESIGN-fire.png`. Earlier crop recommendation is superseded.
- WPF `CityImage` now loads `TrueHeader_GCO_page1.png` and spans the header and upper right-panel content as a background behind live controls. `Stretch=Uniform` preserves proportions through resizing and Windows DPI; no fixed 150×160 skyline, no second rectangular yellow header, no extra broad gradient that suppresses the blue illumination.
- Applied user-approved surface colors: window/background **#060D0F**, step/card background **#081012**, path input **#11191A**, step button **#151C1C**. Builder requires and hash-verifies both original V portraits and the full header. It ignores the rejected yellow and earlier temporary crop.
- This is **source integration**. Windows DPI/interactive visual match and effects remain for a separate real Windows test; verify [the existing Actions workflow](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/workflows/build-windows.yml) for this commit before claiming a passing build.

## 2026-11-07 — compact desktop redesign now in WPF

The first-pass Page 1 source uses [normal 1220×680 / minimum 1040×600 DIPs](PAGE1-COMPACT-WPF-LAYOUT.md). The user's `Rajdhani` and `Oxanium` (OFL) are embedded as WPF resources. Side V `Stretch=Uniform` preserves full width, small dynamic sidebar, top brand band shifts the full blue skyline lower, sloped centered page tabs, GOT LAG? headline, compact path/status, in-card workflow actions and pinned console/Continue. Cut-corner neon outlines and cyan stripes are resolution-independent WPF vector paths. Native Windows titlebar remains pending logo approval. [New CI #42324708424](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/42324708424) pending; user must visually test window and source-owned capture/recovery. Other capability backlog unchanged.
