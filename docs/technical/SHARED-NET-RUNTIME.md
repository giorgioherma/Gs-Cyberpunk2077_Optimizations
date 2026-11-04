# Shared Windows .NET runtime and CapFrameX compatibility

**Decision 2026-11-04:** target **.NET 10 Windows Desktop / win-x64** for the new unified app and aim for **one private Desktop Runtime** in the final integrated package. This is a **packaging goal**, not a claim that the existing independently published managers/CapFrameX have already been retargeted or can launch from one shared runtime.

## Verified source-level requirements

- CET Profiler manager: `net8.0-windows`, WinForms, currently self-contained; CET Resolver: `net8.0-windows`, currently self-contained. The native CET ASI/LuaJIT profiler does not use .NET.
- GRSP manager: `net8.0-windows`, currently self-contained. The native Rust/RED4ext profiler does not use .NET.
- CapFrameX v1.9.1.2 Beta: vendor requires **.NET 10 Desktop Runtime (x64)**, including for portable builds. CapFrameX v1.8.x requires .NET 9; do **not** downgrade to a much older version just to avoid a managed retarget. The native Visual C++ redistributable is separate.
- New unified app: `net10.0-windows`, WinForms. Initial standalone shell is **self-contained**, shipping exactly one runtime for itself. **Proof:** [Windows CI #41859261728](https://github.com/giorgioherma/Gs-Cyberpunk2077_total_optimization_generator_v2.31/actions/runs/41859261728) successfully packaged a single `coreclr.dll` in the shell while excluding unverified profiler/CapFrameX binaries. This does not yet prove CapFrameX or .NET 8 standalone manager execution through that same runtime.

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
