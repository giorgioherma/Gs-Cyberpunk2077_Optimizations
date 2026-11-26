# Architecture, ownership and internal state machine

Technical detail of [approved GCO architecture](../ARCHITECTURE.md). Scope and branch routing are defined only by [CURRENT-TASK](../../CURRENT-TASK.md).

## Product topology

```text
                  Unified Windows app (this repository)
      Single game-root input | friendly two-page UI | session state
                            |
          +-----------------+------------------+
          |                 |                  |
     CET Profiler       GRSP Profiler     PresentMon
     (CET repo)        (GRSP repo)         (external)
          |                 |                  |
          +---------- captured evidence -------+
                            |
             synchronized session/correlation
             no invented additive script CPU
                            |
                 GCO combined analysis
               cross-domain optimization plan
                 (evidence + live sources)
                       |           |
              CET Resolver      REDscript Resolver
              + Generator         + Generator
              (CET-owned)         (GCO-owned)
              0-Engine/native     generic-first
              Lua/G-GC/semantics  .reds proof/compile
                       |           |  optional G-REDruntime
                       +-----+-----+
                             |
              one verified game-root-relative ZIP
               bin/    r6/    red4ext/ (if proven)
              (no profilers, docs, reports, PDBs)
```

GCO owns **one combined analysis/decision plan**, UI, reporting and final ZIP; it does **not** duplicate CET's mature Lua Resolver/Generator or either profiler's install/restore logic. CET retains authoritative source-aware CET code generation and its fixed stages. GCO owns a **separate in-repository REDscript Resolver/Generator and full-stack REDscript SCC validation component**, consuming GRSP native measurements and genuine PresentMon chronology. G-REDruntime supplies services only when accepted rules need them. The orchestrator invokes this single GCO-local REDscript component, without duplicating CET's original source-owned generator.

## Component responsibilities

| Domain | Source of truth | Owner's work | Unified app's work |
|---|---|---|---|
| CET native profiler and .NET manager | CET repo | LuaJIT/CET hooks, 0-Engine scheduler measurements, on-disk transaction and restore, raw archive | Invoke API, show truthful status, correlate copied evidence |
| CET Lua Resolver/Generator | CET repo | Existing source-aware decisions, generic 0-Engine/native/final/semantic generation, G-GC, staged idempotence; preserve all current work | Consume CET evidence/output in one combined cross-domain plan, never duplicate CET generation |
| GRSP native/manager | GRSP repo | RED4ext Rust call-edge hooks, source/callsite/owner evidence, safe install/restore | Invoke API, preserve full raw developer evidence and trust gates |
| G-REDruntime | Framework repo | Shared framework services **for REDscript**: Scheduler, cache, dirty flags, context/input/events/hooks | Include in final ZIP **only** for accepted optimizations which need those services |
| REDscript Resolver/Generator | GCO (`main`), independent internal .NET10 component | Generic-first `.reds` source analysis and generation, source-state admission, SCC verification, changed-only REDscript overlay | Use collected GRSP native evidence/real PresentMon and live sources; one in-repo backend and no extra form |
| GCO unified planning, reporting and final ZIP | This repo (`main`) | Cross-domain CET+GRSP planning, real PresentMon-backed consolidated profiler reporting, verified outputs and dependency/collision-safe merge | Coordinate CET generator and GCO-owned REDscript outputs into one clean game-relative ZIP |
| PresentMon 2.6 | External | Recorded frame intervals and CPU/GPU busy when captured | GCO seals and aligns actual QPC/Unix chronology; missing is NOT_CAPTURED, no GCO CapFrameX fallback |

**CET profiling admission:** GCO checks 0-Engine mod directory and init.lua presence; CET owner decides injection compatibility. GRSP-only remains independent. Standalone CET core-only is distinct.

## UI contract (do not add more primary pages)

- **Page 1: Profiler**: one effective game root (Direct or selected MO2 instance/profile), component health, Install, Start, Collect and Continue; independent Restore. Continue requires verified current-session evidence and successful restoration.
- **Page 2: Analytics & Pass Generation**: after verified collection/restoration, explicitly user-clicked ANALYZE reads current effective mod sources and shows understandable supported/already/unsupported/unsafe decisions; `Generate Optimization ZIP`.
- **Advanced only**: multi-owner diagnostics, per-mod exclusion and regenerate, source provenance, restored-file verification, session recovery. No mandatory separate Resolver launcher, static-location UI, or mod-folder selection.

## Session state contract

```text
GAME_SELECTED
  -> PREFLIGHT_OK
  -> PROFILERS_INSTALLED
  -> CAPTURE_ACTIVE
  -> CAPTURE_READY             [only the current session]
  -> COLLECTING_AND_VERIFYING  [archive before removing live-owned evidence]
  -> RESTORING                 [verify CET and GRSP originals restored]
  -> EVIDENCE_COLLECTED
  -> ANALYZING                 [explicit Analyze; read only effective source]
  -> REPORT_READY / NO_PASS / UNSUPPORTED
  -> ZIP_READY                 [verified, never automatically installed]

Any interruption -> RECOVERY_REQUIRED when ownership/restore or capture
verification cannot be proven; no unchecked continuation.
```

Collection and restore order must obey each standalone owner's actual contract: GRSP requires the game closed for collection; manager restore may itself archive remaining output. We must not delete or alter evidence before verifying an archived copy. This refines implementation ordering but does not change original design's requirement that temporary native profiling is safely restored before normal generation.

## Safety / failure independence

- Missing GRSP does not disable CET-only operation, nor vice versa; show precise missing-domain status.
- Failed second profiler installation rolls back its own files through its owner; the session orchestrator then requests verified restoration of previously installed first profiler. Do not assume atomicity just because one user action launched both.
- Never copy profiler DLLs/PDBs into the optimization ZIP or pin a profiler test DLL as a permanent gameplay dependency. Separately generated static-location runtime belongs in ZIP only for accepted native substitutions.
- Never rewrite live CET or REDscript source merely by pressing Generate. Maintain user choice to deploy, recommend Vortex managed overlay, and keep owner-scoped exclusion/regeneration safe.
- Source-proof is not implied by framework presence or profiler call-frequency; REDscript has distinct compiler and game-lifecycle behavior.
- If independent CET and REDscript generated paths collide or have coupled dependencies, refuse a merged ZIP until ownership and composition are independently proven.

## Packaging/version provenance

The product CI should consume **verified current artifacts**, with upstream repo, actual commit SHA, CI run/artifact ID, SHA-256, version, license and supported API signature in a product component manifest. Do not use snapshots from legacy TOTAL, hardcoded stale artifact IDs, or unverified downloaded payloads. If CapFrameX is bundled, independently verify availability, license/compliance and exact upstream bits; otherwise the app can link the player's copy.

Never promise universal v2.31-only code behavior: game 2.31 is the current test target; other versions or mod formats must be evidence/admission-driven.

## Domain independence and MO2

Current workflow, approved Direct/MO2 source selection, owner restore and explicit Analyze are specified in [approved architecture](../ARCHITECTURE.md). The November 6 prototype status is [historical](../history/2026-11-26-documentation-reset/TECHNICAL-ARCHITECTURE.legacy.md); its safety constraints remain applicable where not superseded by later owner decisions.
