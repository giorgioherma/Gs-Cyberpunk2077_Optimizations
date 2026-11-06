# Architecture, ownership and internal state machine

## Product topology

```text
                  Unified Windows app (this repository)
      Single game-root input | friendly two-page UI | session state
                            |
          +-----------------+------------------+
          |                 |                  |
     CET Profiler       GRSP Profiler     Optional CapFrameX
     (CET repo)        (GRSP repo)         (third party)
          |                 |                  |
          +---------- captured evidence -------+
                            |
             synchronized session/correlation
             no invented additive script CPU
                            |
                Analytics & Pass Generation
                       |           |
             CET Resolver      REDscript backend
            (CET-owned)        (unified repo)
              0-Engine          generic-first
              native/G-GC       .reds proof/compile
              semantics         optional G-REDruntime
                       |           |
                       +-----+-----+
                             |
              one verified game-root-relative ZIP
               bin/    r6/    red4ext/ (if proven)
              (no profilers, docs, reports, PDBs)
```

The graphical app owns **coordination, not a second copy of instrumentation or installer internals**. CET and GRSP release artifacts, payloads and restore logic remain independently verifiable. The redscript generator is a new domain-specific library, not a separate ordinary launcher and not an extension of CET Lua syntax.

## Component responsibilities

| Domain | Source of truth | Owner's work | Unified app's work |
|---|---|---|---|
| CET native profiler and .NET manager | CET repo | LuaJIT/CET hooks, 0-Engine scheduler measurements, on-disk transaction and restore, raw archive | Invoke API, show truthful status, correlate copied evidence |
| CET Resolver | CET repo | Current-source scan, generic/native/final/semantic generation, G-GC, staged idempotence | Invoke existing CLI, present explanation, merge safe game-root files without changing CET semantics |
| GRSP native/manager | GRSP repo | RED4ext Rust call-edge hooks, source/callsite/owner evidence, safe install/restore | Invoke API, preserve full raw developer evidence and trust gates |
| G-REDruntime | Framework repo | REDscript Scheduler, cache, dirty flags, context/input/events/hooks | Verify dependency only for proven REDscript rule families |
| REDscript generic backend | This repo | Safely parse current effective .reds, discover admissible generic transformations, compile/check, generate changed-file overlays | Same application, clear admission/refusal report |
| CapFrameX | External | Frame-time capture | Optional launch/link, copy-only import, sanity-check synchronization |

## UI contract (do not add more primary pages)

- **Page 1: Profiler**: one game-root picker and distinct component health/error states; `Install Profilers`, `Create Game Capture`, `Continue to Analytics & Pass Generation`. Continue enabled only after the current capture session is fully verified.
- **Page 2: Analytics & Pass Generation**: auto-collect and validate evidence, restore native temporary changes, auto-analyze current effective mod sources, show understandable supported/already/unsupported/unsafe decisions, `Generate Optimization ZIP`.
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
  -> ANALYZING                 [read only live effective source]
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

## 2026-11-06 — approved future per-profiler state (not yet implemented)

The latest [Page 1 plan](../design/PAGE1-APPROVED-IMPLEMENTATION-PLAN.md) explicitly requires CET-only, GRSP-only, or both. GCO CET full route validates 0-Engine; standalone CET core-only can operate without it. GRSP only requires its RED4ext/REDscript-side host, not CET/0-Engine. The present `UnifiedProfilerWorkflow.BothInstalled`/`CleanForInstall` design **does not** satisfy this requirement. `CollectedCaptureStore.HasAny` also admits optional PresentMon-only evidence and **must not** authorize Continue.

A current-session hash-verified CET **or** GRSP owner capture can enable Continue. Missing **expected** domains are acknowledged in a Return/Continue dialog **on Continue**, with Return preserving evidence and safe retry capability. Actual owner restores must complete before Page 2. Visual black/glitch/color states and Refresh are not capture proof. MO2 custom-path/runtime result routing is deferred and must not assume the Overwrite folder is always the output. No current-source changes claimed here.
