# G's Cyberpunk 2077 Total Optimization Generator — v1.1.5

**Current milestone: integrated CET + REDscript profiler workflow, not a stable optimizer release.** The normal two-page GUI now packages and coordinates the authoritative CET v1.3.4 and GRSP v1.0.0 managers, with PresentMon 2.6.0 for frametimes. All managed applications run on **one private .NET 10 Core + Windows Desktop Runtime**. Source-owned native profiler DLLs remain unchanged. Direct physical game installations only; MO2 is disabled.

**[Download GCO v1.1.5 Integrated](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/42068835609/artifacts/13435107401)** · [Windows CI #42068835609](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/42068835609) **SUCCESS**, build SHA `f3222aaaacb33b874854fee360cf7d2c4035b0a9`, **131 passing contracts**, actual shipped app host self-verification PASS. **Never bump GCO's version without an explicit user request.**

## Integrated package and initial gameplay test

Extract the complete **v1.1.5 Integrated** artifact to a new, empty, writable folder (do not merge with an older v1.1.2 shell or test package). Run `G-Total-Optimizer.exe`, not the internal app EXE. Select the physical **Cyberpunk 2077 game root**. The Page 1 actions are now real: `INSTALL PROFILERS` (transactional owner CLI), `START CYBERPUNK / CAPTURE` (PresentMon admin prompt; F11 twice in game), then after closing the game `COLLECT RESULTS / RESTORE`. A verified completed CET + GRSP capture **and** verified restoration unlock Page 2. PresentMon evidence is optional and independently reported; its F11 registration interaction still needs a user game test.

The package contains:

```text
G-Total-Optimizer.exe                 # relocatable native launcher
VERSION.txt
app/                                  # GCO managed .NET10 desktop application
runtime/                              # one private .NET10 Core + Desktop runtime
components/
  manifest.json                       # launcher and native payload integrity
  cet/                                # CET owner CLI / payload, managed app retargeted to .NET10
  grsp/                               # GRSP owner CLI / payload, managed app retargeted to .NET10
  presentmon/                         # pinned native PresentMon CLI
RESULTS/                              # created when capturing
```

The GUI preserves user CET bindings, including F10, and warns rather than silently remapping. A session journal is created before native installation; failed second-component installs trigger owner-led restoration. If a restore cannot be proven, the journal remains for recovery. No direct changes are made to mod sources outside the source-owner profiler transactions.

**Not validated yet:** actual three-way F11 game capture, PresentMon elevation/process behavior, game/world load, interrupted-game recovery, complete archived data and real-user GUI layout. **Not implemented yet:** combined frametime graphs and cross-domain correlation, CET Resolver integration, REDscript source optimizer/compilation, or the final single game-relative optimization ZIP. The corresponding generation controls remain disabled. Some Page 1 text still refers to CapFrameX and will be updated after the capture workflow has been exercised.

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

## GCO WPF frontend (November 6, 2026)

The unified GCO two-page GUI now builds with **native WPF on .NET 10** (the CET/GRSP standalone owner managers are unchanged). [Integrated Windows CI #42277190679](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/42277190679) succeeded with one private runtime and **179/179** contracts. This is build/CLI verification, not actual WPF gameplay or visual acceptance. [Migration handoff and remaining work](docs/design/WPF-MIGRATION.md). Original user V/silhouette and BLUE skyline image binaries have not yet been committed.
