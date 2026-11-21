# GCO — validation evidence

This page is a concise index for maintainers and external reviewers. It describes **what was tested**, not an endorsement by Nexus Mods or a guarantee that the game cannot crash.

## What the checks actually verify

| Checkpoint | Scope | Evidence |
| --- | --- | --- |
| **GCO full integration** | .NET 10 build, packaging, game-relative ZIP and ownership, available integration and source-integrity contracts | [main full CI runs](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/workflows/build-windows.yml?query=branch%3Amain) |
| **REDscript SCC** | Existing post-generic source fixture, generator output and official REDscript compiler on the relevant development revision | [REDscript SCC runs](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/workflows/redscript-phase2-checkpoint.yml) |
| **CET profiler** | Separate owner implementation, deployment and profiler evidence; not proven by GCO's REDscript-only CI | [G-CET Runtime Profiler source](https://github.com/giorgioherma/G-CET_Runtime_Profiler) |

**Limitations:** Compilation and integration checks do not establish in-game gameplay parity, measured performance gains, Nexus Mods approval, or antivirus safety clearance. Those require separate evidence. Development branches can have newer code than `main`; only associate validation with its exact commit SHA.

## Consolidated checkpoints — how to read them

- Source changes are committed normally without generating a full Windows CI run for each change.
- A deliberate edit to `.ci/checkpoints/redscript.txt` runs one REDscript SCC checkpoint for the current source revision.
- A deliberate edit to `.ci/checkpoints/full-gco.txt` runs one full integration/package validation.
- Manual workflow dispatch remains available. Every checkpoint run includes a summary with its commit SHA, marker, the prior successfully validated run when identifiable, a commit count and a compare link.
- Failed and cancelled historical runs remain available in Actions. No failures are deleted or reclassified; an SCC-only pass must never be described as a verified complete application.
- The actual GitHub job result and logged test failures determine validation status; the change-history summary is presentation-only.

## How to review a delivery

1. Open a **successful full integration checkpoint** for the exact commit being proposed.
2. Inspect its summary for the commit range and link to changed files, plus the test and packaging steps.
3. Examine the generated artifact and owner/version manifest; do not substitute an old artifact for a newer source revision.
4. For the REDscript generator, inspect the separate SCC checkpoint at the matching revision. Keep native CET profiler validation separate from GCO integration evidence.

For Nexus Mods file moderation, provide the specific uploaded archive/version and file list, explain what each executable does, and link to the matching owner CI/release evidence. This GitHub page is supporting technical documentation, **not** a claim about Nexus Mods' internal moderation criteria.
