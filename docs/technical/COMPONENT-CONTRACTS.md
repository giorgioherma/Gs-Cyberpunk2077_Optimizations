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
