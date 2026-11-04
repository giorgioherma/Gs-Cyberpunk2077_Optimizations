# Mandatory unified optimizer startup / handoff

**This repository**: `giorgioherma/Gs-Cyberpunk2077_total_optimization_generator_v2.31`. **Working branch: `main`** (user decision 2026-11-04). Do not use this repository's temporary/historical `dev/resolver-profiler-handoff` branch as the active line. Related component repos may legitimately use branches with that name.

## Read order

1. [Original accepted unified two-page specification](../product/UNIFIED-TWO-PAGE-WORKFLOW.md) — **authoritative UX + ZIP/rollback contract, imported verbatim**.
2. [Current decision deltas](../product/CURRENT-DECISIONS.md) — approved November 4 scope/ownership refinements, distinguish from exploratory proposals.
3. [Current status](../LATEST-WORK.md) and [implementation roadmap](../ROADMAP.md).
4. [Architecture and state machine](../technical/ARCHITECTURE.md), [headless component contracts](../technical/COMPONENT-CONTRACTS.md), [document index](../README.md).
5. Project-specific source-of-truth docs for the component being modified.

## Scope

Build an **all-in-one, user-oriented app** bundling verified current CET Profiler + current GRSP Profiler + the CET Resolver/generator + optional CapFrameX, with one new **generic-first REDscript Resolver/backend**, combined evidence and one game-root-relative optimization overlay ZIP. The legacy TOTAL Profiler is a historical reference, never a source of current CET/GRSP binaries or installer logic.

**Only one game folder** should be asked in normal setup. **Exactly two ordinary pages** and **three first-page actions** from original design; advanced controls must not silently add a new mandatory step. Reliable capture ownership, transactional restoration and a clean Vortex-friendly ZIP are design-critical.

## Actual state on 2026-11-04

- New repo was empty, then initialized with a README. This milestone is **documentation-first** on `main`.
- The October 16 source design was found in CET's `dev/native-static-anchors-experimental` branch; it was missing from CET's current handoff branch. Source: [original document](https://github.com/giorgioherma/G-CET_Runtime_Profiler/blob/dev/native-static-anchors-experimental/docs/product/UNIFIED-TWO-PAGE-WORKFLOW.md), original Git blob `f4925759fc612311b72432b329708b84b9dd279e`. Preserve as original design, do not fold later changes into it silently.
- CET current development branch: `dev/resolver-profiler-handoff`; GRSP current dev branch: same name in GRSP; G-REDruntime: `main`. **These are *other repositories*.**
- A **first read-only .NET 10 WinForms two-page app shell** and core CLI status adapters are authored, pending CI verification; no new REDscript generic generator exists. No all-component game run, combined profiler restore, or final one-runtime packaging is claimed.
- **Next task:** verify and repair Windows app CI, exercise read-only status against real current CET/GRSP packages, then design safe multi-component session lifecycle. Do not expose enabled install/capture/ZIP actions before tests. [Private .NET 10 runtime contract](../technical/SHARED-NET-RUNTIME.md).

## Strict boundaries and proof

CET generator fixes happen in CET's authoritative generators; GRSP instrumentation fixes happen in GRSP; G-REDruntime internals stay in its repo. New app owns coordination and genuinely new REDscript generic source generation. Never replace standalone transactional lifecycle with an app-owned duplicate implementation. Do not claim all-in-one support before reversible install, real game capture, source/compile validation and recovery tests.

Update [LATEST-WORK](../LATEST-WORK.md) after every substantive milestone, including unverified status.
