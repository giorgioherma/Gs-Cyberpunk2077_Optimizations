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
2. Test actual WPF launch, interactive layout, original images, 100/125/150% DPI, USB? (not applicable), keyboard access, reduced motion, focus/background idle CPU/GPU, native capture/restore on Windows. Remove old `MainForm.cs` only after parity is proven.
3. The backend currently still has `UnifiedProfilerWorkflow.BothInstalled`, `CleanForInstall` and frame-inclusive `CollectedCaptureStore.HasAny`; **independent CET-only/GRSP-only** admission and full GCO **0-Engine v0.17.2+** version proof remain pending. WPF UI only blocks PresentMon-only Continue; do not misrepresent the backend as complete. Implement safe Return/retry/owner restore separately.
4. MO2 stays disabled. GCO unified CET+GRSP analysis and new REDscript Resolver/Generator remain future source-validated milestones.

**No new workflow, packaged runtime, native owner DLL, optimization generator or GCO version change.**
