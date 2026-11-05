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
- A **first read-only .NET 10 WinForms two-page app shell** and core CLI status adapters are **compiled/tested in [Windows CI #41859261728](https://github.com/giorgioherma/Gs-Cyberpunk2077_total_optimization_generator_v2.31/actions/runs/41859261728)**, source commit `fa9309d`; 14 offline contracts pass and a self-contained Windows shell artifact exists. Interactive GUI launch and real backend integration are not yet tested. No new REDscript generic generator, combined profiler restore, or final one-runtime packaging is claimed.
- **Next task:** exercise the CI-produced read-only Windows shell and actual status against verified current CET/GRSP standalone packages, then design safe multi-component session lifecycle. Do not expose enabled install/capture/ZIP actions before tests. [Private .NET 10 runtime contract](../technical/SHARED-NET-RUNTIME.md).

## Strict boundaries and proof

CET generator fixes happen in CET's authoritative generators; GRSP instrumentation fixes happen in GRSP; G-REDruntime internals stay in its repo. New app owns coordination and genuinely new REDscript generic source generation. Never replace standalone transactional lifecycle with an app-owned duplicate implementation. Do not claim all-in-one support before reversible install, real game capture, source/compile validation and recovery tests.

Update [LATEST-WORK](../LATEST-WORK.md) after every substantive milestone, including unverified status.

## Update 2026-11-05 — live-game-first MO2 and portable shell

- Latest completed Windows CI: [#41950289302](https://github.com/giorgioherma/Gs-Cyberpunk2077_total_optimization_generator_v2.31/actions/runs/41950289302) **SUCCESS, 101/101 contracts**, source SHA `d13c79c99148ec27d54cf47a8ed4c6e791acb2a9`, portable artifact **13380467867**.
- Physical game CET, REDscript and RED4ext sources are always the base. Only valid/selected modlist-enabled MO2 packages override identical relative paths; MO2 Overwrite is included only after reading a real modlist. Missing/unusable modlist means **physical game only**, `ProfileVerified=false`, no MO2 staging; an existing empty/all-disabled list is a valid zero-enabled profile. Unknown/corrupt/incomplete profiles also exclude all MO2 layers but retain physical evidence.
- CET-style portable `G-Total-Optimizer-v1.1.1/` folder: public root native `G-Total-Optimizer.exe`, `.lnk` shortcut, internal managed `app/G-Total-Optimizer.App.exe` with exactly one .NET 10 runtime, and `RESULTS`. Package root is resolved from the internal app. No unverified profiler/CapFrameX binaries are included.
- Backend install/collect/restore, MO2 USVFS/RootBuilder runtime fidelity, REDscript optimizer, and final combined game-relative ZIP are NOT implemented. Verify portable shortcut after moving the extracted folder and visually test WinForms on user Windows PC.

## Latest source/CI checkpoint — 2026-11-05 integrated GUI

- **Supersedes the earlier read-only shell summary above.** On unified `main`, GCO **v1.1.2** now packages CET v1.3.4, GRSP v1.0.0 and PresentMon v2.6.0, with the managed GUI and both owner managers source-retargeted to one shared private .NET 10 Desktop runtime. All native owner DLLs remain unchanged. Current integrated [Windows CI #42049460675](https://github.com/giorgioherma/Gs-Cyberpunk2077_total_optimization_generator_v2.31/actions/runs/42049460675) SUCCESS, source `c7d22c68ff86b039280885dd8fc97bd9f6d61da9`, **121 contracts**, artifact **13426900277**.
- Real Page 1 buttons now call source-owner status/install/collect/restore CLI and PresentMon initial launcher. `UnifiedProfilerWorkflow` owns a portable recovery journal and gates all owner mutations on verified manifest, game closed, and fresh native status. CET/GRSP completed capture + verified original restoration unlocks Page 2. Legacy triple harness is manual-only. MO2 remains disabled.
- User's previous standalone test proved actual CET+GRSP install and owner restore, but **new GUI game capture/restore has not been user-validated**. Critical next tasks: real GUI opening/placement, F11 multiway hotkey behavior, live capture collection, recovery failures, and source-owned CET Resolver + generic-first REDscript generator. **Do not claim finished optimizer or enable ZIP generation.** Replace obsolete CapFrameX UI text after capture acceptance.
