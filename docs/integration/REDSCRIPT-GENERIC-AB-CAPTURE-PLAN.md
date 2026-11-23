# GCO generic REDscript A/B validation and capture contract

**Purpose:** measure actual effects of SOURCE-GENERATED generic REDscript optimizations after generator admission, source-overlay composition, SCC compilation and gameplay compatibility. This document is a test protocol, **not a measured gain or deployment authorization**.

## Before any comparison

- GCO Analyze/Generate requires one completed, sealed GRSP F11 capture and a source-stack fingerprint matching the live installed REDscript sources. A function not exercised in that capture may still be source-eligible; GRSP measured callsites determine prioritization.
- Keep game build, REDscript 0.5.31, frameworks, full mod installation, graphics settings, FPS cap, route and profiler versions identical across comparison runs. Test on separate Vortex overlay/profile configurations; do not overwrite installed sources.
- Generate optimizations only through the checked GCO ordered REDscript plan; verify existing-source/partial/idempotent cases, no stale source drift, no dropped previous-pass markers, and official SCC compilation on the resulting **effective** source stack. Stage into an isolated game-relative preview until a test ZIP is expressly authorized.
- Only install test source after a clean original-source backup and verified Restore capability. Never include profiler binaries, analysis JSON or documentation inside the optimized game ZIP.

## Experimental groups

1. **A — baseline:** full live source stack *without* experimental REDscript optimization overlay. Keep the otherwise identical CET/GRSP/PresentMon capture environment.
2. **B — optimized:** same source stack with only the compiler-validated generic source overlay installed (plus its mandatory compatible G-REDruntime runtime assets). Keep the profiler installed the same way.

Make at least two reproducible gameplay comparisons, preferably several complete A/B pairs. Use the existing 4-minute repeatable route and the same 60-second police/combat segment. Include deliberate actions that exercise the targeted source functions, since a perfectly valid optimization might be absent from ordinary gameplay.

Each run: synchronize/start as supported by GCO F10 capture flow, complete and STOP with **F11**, let GCO collect only completed CET and GRSP results, and retain the automatic PresentMon evidence. Never compare a partial/crashed run with a completed run. Preserve the originals and the two independently sealed `GCO-Capture-YYYYMMDD_HHMMSS` bundles.

## What to compare (never invent savings)

| Domain | Baseline vs optimized |
|---|---|
| GRSP per-function/callsite | Calls per second, `exclusive_instrumented_ms`, `active_frames`, and exact owner/function/target. Match by semantic owner/method/target: inserted source lines can change `source_line` and callsite IDs |
| Source-local service reuse | Fewer outgoing `ScriptGameInstance::Get...System` edges **in the exercised candidate methods**; original `GetFact`, `GetStatValue`, setters, callbacks and other observable operations still execute |
| GRSP expensive work | Check downstream `RSP_CallSites.csv` / `GRSP_ByFunction.csv` for changed repeated work, not merely smaller line counts or theoretical eliminated sites |
| CET | Allocation/s, alloc bytes/s, GC pressure, recurring Lua work and spike counts where present; CET and GRSP may overlap and must not be summed as total CPU |
| PresentMon | Frame-time median, P95/P99, worst outliers, reproducible frame-time spike periods and capture quality |
| Correctness | Quest fact changes, stat modifiers, delayed events, UI updates, controls, wrapped callbacks, save/reload and game restart. A successful SCC compile is not gameplay proof |

Reject comparisons if either source fingerprint, gameplay path, graphics settings, capture completeness or profiler health differs materially. Treat unmeasured/inactive candidate work as **not observed**, never as a saving.

## Current expected outcome / limitation

The first new source-local mechanism, `LOCAL_SYSTEM_HANDLE_REUSE_V1`, replaces only repeated service **handle** retrieval in consecutive same-method statements. It **does not** cache live facts or remove callbacks. Its theoretical benefit is one or more avoided getter calls per exercised sequence, likely small relative to expensive geometry/UI/loop work. Do not treat a general 384-second `GetQuestsSystem` aggregate as savings attributable to this pass.

The November 16, 2026 sealed capture is a useful **ranking baseline**, not a valid matched A/B performance comparison for a later installation with different source binaries. New paired gameplay captures require a user-run game session; CI cannot simulate frame timing or real REDengine runtime behavior.
