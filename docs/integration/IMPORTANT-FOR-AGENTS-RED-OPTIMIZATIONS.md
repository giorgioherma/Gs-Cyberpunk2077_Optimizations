# IMPORTANT FOR AGENTS — REDscript Optimization Rules (Phase 2)

**MANDATORY — read before researching, implementing, reviewing, or modifying any REDscript semantic optimization.**

**Owner decision: 2026-11-20.** These are hard acceptance rules for GCO's REDscript Resolver/Generator. They apply to the historical semantic portfolio, the seven separately planned owners, and future accepted semantic rules. Read with `AGENTS.md`, `docs/LATEST-WORK.md`, `REDSCRIPT-PHASE2-SEMANTIC-PLAN.md` and the GCO/GRSP/G-REDruntime ownership contracts. Where older Phase 2 planning notes were stricter about exact source versions or rejecting any custom edits, **this explicit owner decision supersedes them**. Safety, functional equivalence and verifiable structural proof remain mandatory.

## Rule 1 — Recognize and **complete partially implemented optimizations**

The Resolver **must detect what is already optimized** and determine precisely what is missing. A mod can contain the original implementation, some semantic changes, a prior generated adapter, manually performed equivalent changes, or a mixture with the ten already-applied generic rules.

- Determine completion **per independent semantic operation and its required dependencies**, not by finding a single marker, checking one whole-file hash, or assuming a whole mod is either untouched or complete.
- Accept a previously optimized portion only after checking its **actual structural and behavioral postconditions**. Markers are helpful evidence, **not** a substitute for checking code; an equivalent unmarked implementation can qualify.
- Generate **only the missing pieces**, preserving verified optimized pieces and the user's unrelated changes. Never stack duplicate fields, wrappers, subscriptions, callbacks, timers, adapters, or framework services.
- A partial optimization that depends on multiple coordinated changes must be completed **coherently**. If an essential change cannot be safely matched, do not publish a half-functional version of that operation; explain the exact incompatible dependency and continue other independently valid operations.
- Fully optimized sources produce **zero new semantic work**. Regeneration remains idempotent even when files were optimized by earlier passes or manually.

## Rule 2 — One complete pass from stock / post-generic source

**One ordinary GCO Generate invocation must apply all eligible approved semantic optimizations directly to an unoptimized mod source — with the existing ten generic rules already applied first.** For Phase 2, that **post-generic source is the stock starting point**.

- Do **not** require users to install historical passes 2.0, 2.1, ... 9.0 in sequence, start from `G-REDruntime_Optimization_All_Mods.zip`, or run a prerequisite semantic optimization and then a corrective pass.
- The same generator must also accept partially and fully semantically optimized inputs (Rule 1). All three states should converge to the **same proven final functionality**, with only missing work emitted.
- Source transformations must be composed **at their generating source and in a deterministic dependency/order plan**. Fix an incorrect transformation in its owner generator; no after-the-fact repair layer that relies on broken staged output.
- Prefer one complete, owner-safe output per Generate. For an operation needing multiple source edits and a helper, stage them together and verify all requirements before including that operation in the ZIP.
- The output contains only game-relative optimized files. GCO never modifies the player's live source during Generate and never silently overwrites installed mods. Keep the existing sidecar/rollback/source-ownership contract.

## Rule 3 — Version-fluid, **semantic/structural** compatibility

The Resolver must understand **what code does**, what exact operation the optimization targets, and what is needed to preserve its behavior. Do not bind acceptance to a specific mod release, historical file contents, an entire file checksum, original line numbers, formatting, comment placement or a list of known mod versions.

- Locate the real relevant classes, methods, wrappers, registration sites, calls, source writers, state transitions and dependencies **in the installed source at generation time**.
- Match the **required structural/semantic invariants** of the optimization: the correct target and identity, control flow, data flow, effect/read/write ordering, side effects, return/callback semantics, lifecycle, cleanup, fallback and required framework behavior.
- Allow equivalent source shapes across older/newer versions: reordered unrelated methods, additional helper functions, comments, whitespace, shifted line numbers, extra unrelated fields or neighboring logic **must not cause rejection**.
- **Custom-modified files remain eligible.** Preserve their unrelated additions and edits byte-for-byte where practical, and transform only the localized verified operation. If the user's changes alter the target, reason about the **actual new source**; do not assume a previous version.
- Do not copy an older full optimized source file over a newer one. Prefer a **new, per-mod, owner-local adapter file** when its public/wrapper boundary really preserves behavior; otherwise use a minimal, structurally located edit in the file at its original path. Framework core remains G-REDruntime-owned; do not create duplicate runtime implementations.
- Source manifests and hashes may record **provenance, source drift between Analyze and Generate, and rollback safety**. They must **not** be used as a permanent *version allowlist* or a rule that rejects a valid structural match just because the user's file differs from a historical fixture.

## Rule 4 — **Only actually incompatible optimization targets are skipped**

The default goal is **apply the optimization**, including to changed versions and custom source when its essential operation is still recognizable and compatible.

- **Do not skip** because a mod version is newer, files are longer, comments or unrelated blocks differ, generic edits are present, or only part of the historical optimization exists.
- If the necessary target operation is **completely replaced, absent, or changed in a way that makes the optimization semantically incompatible**, skip that **specific operation**, with a precise reason. If the whole relevant mod functionality is incompatible/removed, only then mark the entire mod incompatible.
- If structural analysis genuinely cannot prove a critical safety property (e.g. mutation/registration/callback identity, behavior-preserving order or lifecycle), **do not guess or emit unsafe code**. Record the **narrowest unresolved operation**, preserve the remaining source, and continue any independently safe operations. The aim is improving structural recognition and coverage, **not** turning all source differences into broad refusal.
- Missing optional mods are simply **not applicable**, not a reason to package them or mark unrelated installed mods incompatible.
- There is no fixed historical mod list, source-hash whitelist or version lock. Discover the current effective installed mod source dynamically.

## Required implementation / CI contract for every Phase 2 semantic rule

Before accepting a rule, demonstrate the **actual generated output** under these cases:

1. **Post-generic stock:** one invocation emits the *entire* applicable semantic optimization, including adapters/fields/registration/cleanup where required.
2. **Partially optimized:** recognize each already-safe element and produce **only missing elements**, converging to the same functional result; no duplicate wrappers/listeners/caches.
3. **Fully optimized:** no-op with proven postconditions, even if authored without GCO-specific markers.
4. **Version variation:** equivalent new/old method layouts, benign extra code, moved blocks, modified formatting and source line drift **still apply**.
5. **Custom source variation:** unrelated user edits survive; current target code remains compatible and is optimized without replacing the file wholesale.
6. **Genuine incompatibility:** missing/altered essential operation or provably unsafe changed semantics refuse **only the affected optimization**, with actionable diagnostics; unrelated safe work remains eligible.
7. **Generic composition:** the ten approved generic transformations and existing custom/semantic additions survive the Phase 2 rewrite; no discarded generic work, duplicated producers or shadow source copies.
8. **Integrity and repeatability:** repeat Generate has zero new edits; Analyze→Generate detects **concurrent source change** and safely re-analyzes or refuses stale output rather than silently using old proof; no uncontrolled writes to the live game.
9. **End-to-end safety:** real generated files, dependencies, official REDscript 0.5.31 **CI** compilation, merged CET+REDscript ZIP/sidecar integrity, feature/lifecycle and gameplay validation. Do not ship SCC to the player or claim CI proves gameplay.

**Design objective:** `GCO post-generic stock -> all compatible semantic work in one Generate`; `GCO partially semantic-optimized -> missing work only`; `GCO fully semantic-optimized -> no changes`; `compatible newer/custom source -> still optimize`; `truly incompatible target -> narrow safe skip`.

**Scope:** This document is policy for agents and the future Phase 2 implementation. Creating/updating it does **not** authorize importing old ZIP files wholesale, implementing unapproved semantic behavior changes, or weakening source/runtime safety gates.
