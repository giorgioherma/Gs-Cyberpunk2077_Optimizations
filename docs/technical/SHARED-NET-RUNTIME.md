# Shared Windows .NET runtime and CapFrameX compatibility

**Decision 2026-11-04:** target **.NET 10 Windows Desktop / win-x64** for the new unified app and aim for **one private Desktop Runtime** in the final integrated package. This is a **packaging goal**, not a claim that the existing independently published managers/CapFrameX have already been retargeted or can launch from one shared runtime.

## Verified source-level requirements

- CET Profiler manager: `net8.0-windows`, WinForms, currently self-contained; CET Resolver: `net8.0-windows`, currently self-contained. The native CET ASI/LuaJIT profiler does not use .NET.
- GRSP manager: `net8.0-windows`, currently self-contained. The native Rust/RED4ext profiler does not use .NET.
- CapFrameX v1.9.1.2 Beta: vendor requires **.NET 10 Desktop Runtime (x64)**, including for portable builds. CapFrameX v1.8.x requires .NET 9; do **not** downgrade to a much older version just to avoid a managed retarget. The native Visual C++ redistributable is separate.
- New unified app: `net10.0-windows`, WinForms. Initial standalone shell is **self-contained**, shipping exactly one runtime for itself. **Proof:** [Windows CI #41859261728](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/41859261728) successfully packaged a single `coreclr.dll` in the shell while excluding unverified profiler/CapFrameX binaries. This does not yet prove CapFrameX or .NET 8 standalone manager execution through that same runtime.

## Required path to final one-runtime distribution

1. Keep existing original CET and GRSP release packages untouched.
2. Build a separate **unified-specific** packaging configuration for CET Profiler, CET Resolver and GRSP manager against `net10.0-windows` under their authoritative source repositories or reproducibly checked-out exact sources. Verify framework-dependent apps use the unified private runtime. Never silently substitute the old 8.0 apphost under 10.0 runtime.
3. Keep Profiler installers, payloads, generated workers and native binaries in component-owned subfolders with separate manifest/signatures. No duplicate managed code ownership or game runtime side effects.
4. Obtain the official CapFrameX portable package, preserve vendor-signed binaries, full `portable.json` directory layout and original config/captures. Verify that its apphost **and dependency checker** recognize the private .NET 10 Desktop Runtime under controlled launch conditions. Do not hex-edit, IL-patch or rebuild CapFrameX merely to bypass that check. If CapFrameX requires a machine-level .NET 10 install, report that requirement clearly and support existing external CapFrameX.
5. In CI, test Windows launch/status for all three managed components and CapFrameX, with **no machine-wide Desktop Runtime preinstalled** (or equivalent isolated runtime resolution test). Compare output reports/restore from the same package root. This cannot be accepted based on SDK build success alone.
6. **Do not bundle any unverified upstream binary** or claim a single runtime across all components until these checks pass.

## Initial shell scope

Only the **new unified WinForms executable** is compiled and packaged, self-contained `net10.0-windows` (CI passed on 2026-11-04). CET and GRSP existing binaries are not bundled into the shell artifact; status adapters can read them if explicitly placed under known `components/cet` and `components/grsp` paths. Install/capture/generate buttons stay disabled until their real lifecycle, source and recovery contracts have been implemented and tested.

### Potential separate runtimes during development

If someone manually places current self-contained CET (.NET 8) or GRSP (.NET 8) standalone releases beside the new shell, they each carry their own runtime. This is temporary **development compatibility**, not the final optimized distribution layout. Never advertise this stage as "one shared runtime for the whole suite." 


## 2026-11-05 — user-approved source build exception and test outcome

The previous default instruction to leave CapFrameX *completely unchanged* is superseded **for the explicit private-runtime test only**. User authorized a source-built adjustment to its dependency checker. No manual binary patching or editing of official native hooks or capture executables. The vendor's original `CapFrameX.exe` is kept, and only `CapFrameX.Configuration.dll` is compiled from pinned upstream MIT-licensed source with the scoped dependency check. Keep MIT and accompanying license notices.

- Implemented dedicated non-shipping Actions workflow `.github/workflows/capframex-private-runtime-test.yml` and its source-checked builder `integrations/capframex/Build-PrivateRuntime-Test.ps1`. Test package is framework-dependent managed WinForms + vendor CapFrameX, one private Microsoft .NET 10.0.14 Desktop/Core runtime root, app-local Microsoft VC x64 CRT from official VS redistributable.
- Unified root native launcher detects a bundled `runtime/dotnet.exe`, selects process-local `DOTNET_ROOT_X64`, `DOTNET_ROOT` and `G_TOTAL_PRIVATE_DOTNET`, and does nothing to the user's Windows global environment. CapFrameX requires administrator elevation, so its separate native `Start-CapFrameX-Private.exe` is built with requireAdministrator and configures the same process-local runtime after UAC.
- [Dedicated CI #42020999015](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/42020999015) validates exact upstream source/artifact, successful source checker compilation, bundle contents, and exactly one private runtime, with artifact ID **13412011394**. This is **not proof of actual app startup/capture** or of fully standalone native child services; Windows user test required.
- Existing production v1.1.1 shell workflow still packages itself self-contained, so the **one-shared-runtime claim applies to the dedicated TEST artifact only**. Do not switch stable shell packaging or authorize profiling from this limited evidence.


## 2026-11-05 — CET/GRSP/PresentMon test shows one real shared managed runtime

- [Triple test CI #42035822902](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/42035822902) SUCCESS: CET v1.3.4 source-owned WinForms manager, GRSP v1.0.0 source-owned WinForms manager, and unified GUI all rebuilt framework-dependent for `net10.0-windows`, published in one portable test root with ONE private `coreclr.dll` under `runtime/`. Microsoft.NETCore.App and Microsoft.WindowsDesktop.App both 10.0.14. No second .NET 8 runtime, and no framework-dependent .NET10 installed system-wide by this bundle.
- Both native Win32 root launchers completed headless CLI smoke using explicit redirected awaited process execution; GRSP's owner manager passed isolated install/status/restore. This verifies bundled runtime execution on the GitHub runner, **not** fresh Windows without system .NET or native gameplay. The authoritative source files were changed only in disposable CI worktree, not in the CET/GRSP product branches.
- PresentMon 2.6.0 official standalone Console executable is a native x64 tool (~0.94 MB). It is bundled with no CapFrameX/GUI/PresentMon MSI, exact sha256 provenance in the CI builder. Its Windows admin ETW and F11 needs real user testing.
