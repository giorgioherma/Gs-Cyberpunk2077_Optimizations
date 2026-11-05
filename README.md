# G's Cyberpunk 2077 Total Optimization Generator — v1.1.2

**Version 1.1.2 (development shell and triple-profiler test).** The .NET 10 Windows shell is CI-built. CET/GRSP installation, REDscript generation, capture/restore and full all-in-one packaging remain unimplemented; this is **not a stable functional release**.

One user-oriented Windows application for **CET/Lua profiling, REDscript profiling, optional CapFrameX frame measurements, unified analysis, and a single game-installable optimization ZIP**. The project's currently tested game target is Cyberpunk 2077 **2.31**. Do not use the game version or third-party mod versions as rigid source-hash admission criteria.

**Previous v1.1.1 portable development build (historical):** [Windows CI #41950289302](https://github.com/giorgioherma/Gs-Cyberpunk2077_total_optimization_generator_v2.31/actions/runs/41950289302) — [download the CET-style portable folder](https://github.com/giorgioherma/Gs-Cyberpunk2077_total_optimization_generator_v2.31/actions/runs/41950289302/artifacts/13380467867). **101/101 read-only/source/CLI contracts passed**. This is not the completed optimizer.


## Portable Windows folder (v1.1.2)

Extract the entire CI artifact and keep the files together:

```text
G-Total-Optimizer-v1.1.2/
├─ G-Total-Optimizer.exe          # small native launcher (portable fallback)
├─ VERSION.txt
├─ app/                           # managed WinForms app and ONE .NET 10 runtime
│  └─ G-Total-Optimizer.App.exe
└─ RESULTS/                       # reserved capture results workspace
```

Run the root launcher; **do not move the internal `app/` contents separately**. The root launcher resolves the application relative to its own location, and the managed app resolves component paths against the public package root. No CET profiler, GRSP, CapFrameX or optimization generator binaries are bundled yet. MO2 source resolution is read-only and uses the live physical game as the baseline, with only validated selected-profile winners layered above it; an absent `modlist.txt` excludes all MO2-managed sources rather than discarding physical game mods. The MO2 virtualized runtime/RootBuilder behavior remains unverified.

## The agreed product

**One game folder · Two pages · Three primary first-page actions · One ZIP.**

- **Page 1: Profiler.** `Install Profilers` → `Create Game Capture` → `Continue to Analytics & Pass Generation`. A complete, current capture unlocks continuation.
- **Page 2: Analytics & Pass Generation.** Automatically collect, safely restore temporary profiler components, analyze current installed sources, show a clear report, and enable `Generate Optimization ZIP` only for proven safe generation.
- Suggested capture: around five minutes of varied gameplay, **not** a forced timer. CapFrameX is optional.
- **Vortex overlay recommended.** The user retains their original mods. No profilers, reports, diagnostics, documents, or PDB files enter the optimization ZIP.

## Documents — start here

1. [Agent instructions](AGENTS.md) and [mandatory handoff](docs/HANDOFF/CORE.md).
2. **[Original accepted two-page product specification](docs/product/UNIFIED-TWO-PAGE-WORKFLOW.md)** — preserved verbatim from the October 16 CET experimental branch.
3. [Current decisions and explicit changes to the original contract](docs/product/CURRENT-DECISIONS.md).
4. [Architecture and ownership](docs/technical/ARCHITECTURE.md) and [existing component interfaces](docs/technical/COMPONENT-CONTRACTS.md).
5. [Implementation milestones](docs/ROADMAP.md) and [latest work/status](docs/LATEST-WORK.md).

The old TOTAL Profiler is **historical reference only**, not a migration binary baseline.

## Authoritative existing projects

- [G-CET Profiler and Resolver](https://github.com/giorgioherma/G-CET_Runtime_Profiler/tree/dev/resolver-profiler-handoff) — current CET profiling, source-aware Lua generator, 0-Engine/native integrations, G-GC.
- [GRSP REDscript Profiler](https://github.com/giorgioherma/GRSP-G-REDscript-Profiler/tree/dev/resolver-profiler-handoff) — native REDscript profiling and future evidence extensions.
- [G-REDruntime Framework](https://github.com/giorgioherma/G-REDruntime_Framework) — optional REDscript framework services.
- [CapFrameX](https://github.com/CXWorld/CapFrameX) — optional independent frame-time companion.

**This repository uses `main` for product development and documentation.** CET and GRSP retain their own independent development branches. No separate handoff branch is intended here.
