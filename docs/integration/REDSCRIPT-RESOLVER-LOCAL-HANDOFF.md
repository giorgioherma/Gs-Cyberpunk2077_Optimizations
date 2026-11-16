# GCO-local REDscript Resolver handoff — approved 2026-11-16

**Active development branch:** `dev/redscript/resolver_handoff`, created from GCO `feature/redscript-resolver-integration` at `0d62eaff7b19ca9263dc355250beccbe184461da`. **GCO `main` stays untouched.** This decision supersedes the November 15 **repository-location/ownership** decision that GRSP would be the permanent home of the REDscript Resolver. It does **not** revoke the generic-first blueprint, past test results, or source-owner profiling boundaries.

## Approved component boundaries

- **GCO repository:** develop the REDscript Resolver, source-aware generic Generator, compiler/output validation and their tests as a **separate internal .NET 10 component**. The existing CET Resolver remains CET-owned and invoked as-is, with its fixed generation order preserved.
- **GRSP repository:** continues owning REDscript native instrumentation, F11 capture, profiler measurements and native evidence writers. Existing managed resolver/preview/scanner code in the GRSP development branch is the verified **migration source** for the GCO-local component; preserve it until the GCO replacement passes real regression parity. Do not independently develop duplicate optimizers indefinitely.
- **G-REDruntime repository:** continues owning optional shared REDscript runtime services and any proven native player-state/NEAR-FAR bridge contract.
- **Unified GCO application:** owns orchestration, genuine PresentMon-backed combined reporting, shared CET/REDscript candidate planning, one existing two-page WPF interface and final **one game-relative optimization ZIP**. This does **not** authorize a second UI form/page, an additional runtime or a second CI workflow.

## Migration order / non-negotiable gates

1. Preserve the last verified code baseline: **[GCO CI #482 SUCCESS](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/43794814481)**, [GRSP CI #61 SUCCESS](https://github.com/giorgioherma/GRSP-G-REDscript-Profiler/actions/runs/43794298639); 2,319 private .reds files, 48 shallow membership candidates, 1 strict match, 47 rejected (46 early identifier-position/collision combined gate, 1 loop-body gate); untouched and one-file preview both compile with official SCC 0.5.31, unchanged originals and 323 GCO contracts. **No gameplay or performance gain proven.**
2. Relocate **one authoritative implementation** of the existing GRSP managed Resolver, source scanner, conservative `CNAME_ARRAY_MEMBERSHIP_V1` preview and headless SCC into a clearly bounded GCO internal REDscript component; migrate actual source and high-value CLI/safety contracts without altering the native GRSP profiler, CET source, or installed game. Preserve .NET 10, hash/source-drift refusal, measured provenance, partial/complete idempotence and real full-stack compilation.
3. Rewire the **existing canonical GCO Windows workflow**, not a new workflow, to build/test the local REDscript component against the same private live source/dependency fixture. Do **not** retain both local and GRSP-owned generators as active competing paths; remove the old runtime/CI pin only after parity is verified.
4. Continue R4/R5 generic-first candidate classification, source-aware proof and measured prioritization; GRSP native instrumentation changes remain GRSP-owned, and optional G-REDruntime services remain framework-owned. Keep R7 semantic remainder blocked until useful generics and the operation-level gap matrix exist.
5. Before enabling generation: untouched/partial/completely optimized installations, structural semantic proof, second-pass zero-op, source drift and missing-dependency refusals, **real whole-stack SCC**, merged CET/REDscript compatibility and owner-by-owner gameplay checks. Never write directly into installed mods.

## Current status

**Branch and decision only.** No resolver source has been moved yet; GCO still invokes the pinned GRSP owner in its current CI. The existing green CI #482 is a **pre-migration** baseline, not validation of GCO-local code. Game installation, GCO main, old feature branch, CET and GRSP repositories remain unchanged by this branch-creation milestone.

**Next:** migrate the tested managed component into GCO without redesigning the product, then demonstrate CLI and full private SCC parity in the original workflow before doing further generic candidate work.
