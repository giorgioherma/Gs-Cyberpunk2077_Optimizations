# Existing component/headless contracts and data boundaries

**Status: reviewed from current repository source; not yet exercised end-to-end through the new app.** Recheck current heads/build packages before implementation; commands below describe the interfaces observed on 2026-11-04.

## CET profiler

Source: [CET `src/G.CETProfiler.App/Program.cs`](https://github.com/giorgioherma/G-CET_Runtime_Profiler/blob/dev/resolver-profiler-handoff/src/G.CETProfiler.App/Program.cs). Normal packaged root launcher delegates to .NET core; the same core owns install/restore and collection.

```text
G-CET-Runtime-Profiler.exe --status --game <gameRoot> --json
G-CET-Runtime-Profiler.exe --install --game <gameRoot> [--core-only] --json
G-CET-Runtime-Profiler.exe --collect --game <gameRoot> --json
G-CET-Runtime-Profiler.exe --reset --game <gameRoot> --json
G-CET-Runtime-Profiler.exe --restore --game <gameRoot> --json
G-CET-Runtime-Profiler.exe --emergency-restore --game <gameRoot> --json
G-CET-Runtime-Profiler.exe --report --capture <collectedFolder> --json
```

CET capture owns its user keybinds. Existing F10 `COMBAT-POLICE-120` benchmark and F11 unlimited manual capture should not be silently rewritten by unified orchestration. CET's standalone manager installs its namespaced scheduler payload when source-proven/needed and restores only what it owns.

## CET Resolver / generator

Source: [CET `src/G.CETResolver.App/Program.cs`](https://github.com/giorgioherma/G-CET_Runtime_Profiler/blob/dev/resolver-profiler-handoff/src/G.CETResolver.App/Program.cs).

```text
G-CET-Resolver.exe --capture <collectedFolder> --game <gameRoot> --json
G-CET-Resolver.exe --capture <collectedFolder> --game <gameRoot> --generate-pass [--pass-output <zipPath>] --json
```

Its `--mods` alternative is for advanced paths, not a second ordinary UI input. JSON reports contain coverage and generated pass information. The Resolver performs analysis against current **read-only** CET source, preserving its own generic → native → generic finalization → semantic composition and G-GC calibration. The unified app must not attempt to regenerate CET changes separately or require an old profiler capture to define the installed mod inventory.

## GRSP REDscript profiler

Source: [GRSP `manager/Program.cs`](https://github.com/giorgioherma/GRSP-G-REDscript-Profiler/blob/dev/resolver-profiler-handoff/manager/Program.cs).

```text
G-REDscript-Profiler.exe --status --game <gameRoot> --json
G-REDscript-Profiler.exe --install --game <gameRoot> --json
G-REDscript-Profiler.exe --title <captureTitle> --game <gameRoot> --json
G-REDscript-Profiler.exe --collect --game <gameRoot> --json
G-REDscript-Profiler.exe --report --capture <collectedFolder> --json
G-REDscript-Profiler.exe --restore --game <gameRoot> --json
G-REDscript-Profiler.exe --start --game <gameRoot> --json
```

GRSP wraps output under `{ok:true, result:{...}}` (or `{ok:false,error:...}`); CET's CLI schema differs. Do not reuse parsing assumptions. GRSP reports `GRSP_Summary.json` as compact metadata; complete evidence comes from `Data/Runtime/*.csv` and `Data/Developer/*.csv`. GRSP `--collect` requires the game closed.

Developer files include `RSP_FunctionMap.csv`, `RSP_CallSites.csv`, `RSP_SharedTargets.csv`, `RSP_Cadence.csv`, `RSP_WrapperChains.csv` and `RSP_WorkMap.csv`. Reuse these before adding native instrumentation. New native measurement required for a rule belongs in GRSP. GRSP observes REDscript call edges, **not** a general REDengine allocation trace.

## GRSP/CET shared summary contract

Both sides already describe `interop.contractVersion=1.0` with `domain` = `cet` or `redscript`, common capture fields and own domain metrics. Sources:
- [CET contract](https://github.com/giorgioherma/G-CET_Runtime_Profiler/blob/dev/resolver-profiler-handoff/docs/integration/PROFILER_SUMMARY_INTEROP_CONTRACT.md).
- [GRSP contract](https://github.com/giorgioherma/GRSP-G-REDscript-Profiler/blob/main/docs/PROFILER_SUMMARY_INTEROP_CONTRACT.md).

**Never add CET and GRSP exclusive timing together and label it whole-game CPU.** Both may observe overlapping/nested work; REDscript scripts can call native/base work. Correlate independently against time-aligned CapFrameX/frametimes, identify sync quality and drops. Do not infer frame-time saving from unsupported heuristic overlaps.

## Effective source locations and output

Direct CET installed source: `<gameRoot>/bin/x64/plugins/cyber_engine_tweaks/mods`. REDscript source: `<gameRoot>/r6/scripts` plus positively discovered effective mod-manager overlays. MO2 virtual files are an **unresolved compatibility gate**. A source ZIP in CI or previous capture is not proof of live installation.

A unified generated overlay is **game-root-relative**; CET modifications under `bin/.../mods/<owner>/...`, REDscript under `r6/scripts/...`, compiled native service under `red4ext/plugins/...` **only if independently source-proven**. No wrapping folder, docs, result archives, logs, PDBs, profiler DLLs or manager assets inside it. Source version drift or REDscript compile errors must refuse generation, not produce a supposedly safe ZIP.

The app workspace (outside game ZIP) stores ownership manifests, hashes, source proof, exact redscript compile logs, per-owner exclusions, capture trust and native runtime provenance.

## CapFrameX contract

Optional and independently installed or later safely bundled after rights and byte verification. Link/launch the actual executable and import its existing capture **by copy**. Do not overwrite the user's general CapFrameX config, delete original captures or claim sync when timing proves weak. The game's F10/120s controlled benchmark remains available independently; default free-form F11 capture is open-ended.

## First integration tests needed

- CLI JSON parsing with both different schemas, nonzero exit, malformed output, timeout and unavailable process.
- Install one/both, partial second-install failure, interrupted install, collect, capture archive verification, verified restore and emergency recovery.
- Distinguish ready current capture from stale/partial/missing or mismatched session; synchronized vs unsynchronized optional CapFrameX.
- Mod inventory source proof for current live CET and REDscript; absent owners cannot appear from old captures.
- Generated ZIP topology and no forbidden files; per-owner exclusion, source-already/partial detection, unchanged generated second pass.

## 2026-11-06 — GCO independent native capture rule (planned)

The [approved Page 1 handoff](../design/PAGE1-APPROVED-IMPLEMENTATION-PLAN.md) supersedes GCO's both-profiler gate. CET-only with GCO-validated 0-Engine and GRSP-only with RED4ext/REDscript-side host must be independently possible; no unnecessary frameworks or absent secondary domain are hard blockers. Standalone CET's optional core-only behavior remains separately true. PresentMon-only never unlocks Continue. After at least one verified CET/GRSP current-session archive, missing **expected** evidence is acknowledged on Continue with Return or Continue; every actually installed original profiler is restored through its **own** source manager before Page 2. These are pending GCO implementation and tests, not alterations to the standalone component contracts.


## 2026-11-06 — authoritative ownership and combined plan

- **CET owns its existing Lua Resolver/Generator, native optimizations and G-GC.** GCO consumes its authoritative analysis/generation interfaces and does not reproduce CET source transformations or alter its fixed generation sequence.
- **GRSP owns REDscript profiler instrumentation.** If the combined decision process needs missing REDscript measurements, extend GRSP's evidence source there.
- **GCO owns the new REDscript Resolver/Generator** as well as a single combined CET+GRSP optimization plan, cross-domain prioritization, reporting, output compatibility validation and final ZIP. REDscript generation must preserve full source/compile safety.
- **G-REDruntime owns shared services for REDscript**, included in the generated ZIP only where an accepted optimization requires them.
- **CET profiling requires 0-Engine v0.17.2+** under the user-approved GCO CET route. This is distinct from standalone CET's optional core-only profiler mode. GRSP-only does not require CET/0-Engine. No duplicate owner implementations.


## 2026-11-13 — explicit GCO-bundle-only capture lifecycle exception

This section supersedes the earlier “consume exact standalone release unchanged” wording **for the integrated GCO bundle only**, by explicit user decision. The CET and GRSP standalone repositories and public releases remain unchanged.

- Integrated GCO CI checks out exact pinned owner sources into disposable worktrees, verifies official stable owner assets, applies reviewed source-only GCO integration adaptations, recompiles both managers against the existing private .NET 10 runtime, and source-builds a **GCO-only GRSP native Rust DLL** from pinned source/official Cargo.lock. Do not modify either standalone repo or maintain a second installer/restore implementation.
- GCO bundle-only headless owner command: \`--discard-live --game <root> --json\`. CET uses its manifest-defined native results inventory; GRSP uses its managed data folder and completion proof. Both verify ownership and require the game closed. Source-owned restore in the bundled managers discards uncollected live output; original standalone restore/archiving behavior does not change.
- GCO passes \`GCO_COMPONENT_RESULTS_ROOT\` only when invoking a bundled owner's \`--collect\` command. Each owner builds its usual verified report/archive in GCO's session-scoped staging under \`RESULTS/.staging/<session>/<domain>\`. GCO copies verified files into \`RESULTS/GCO-Capture-.../<domain>\` and removes only the verified owner staging archive, without accumulating separate permanent component-local archives.
- Bundled GRSP counts a native capture complete only with the successful explicit F11 STOP/export proof file \`GCO-F11-Complete.flag\` containing \`F11_STOP\`, plus the normal summary/status; shutdown and game crash never synthesize completion. Bundled CET's native F11 start/stop gate remains the source owner implementation.
- WPF close is no longer a reset or evidence-deletion trigger. The existing native GCO root launcher is the low-memory, no-CLR tray host and supervises graceful PresentMon ETW shutdown on game exit while WPF is unloaded. PresentMon stops/flushes without becoming *collected* until explicit Collect. The same capture journal and verified owner status survive UI reboot. Native tray exit and elevation errors preserve evidence for retry.
- Integrated Windows CI #318 (run 43516665150) passed pinned source build, real GCO-bundled GRSP negative crash marker fixture, positive F11 marker, discard+Restore CLI, package contracts and shared .NET 10 runtime; further restart contracts/gameplay are not yet verified.
