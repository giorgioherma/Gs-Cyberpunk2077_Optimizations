# GCO MO2 integration — stage plan and evidence ledger

**Branch:** `MO2_integration` (from GCO `main` `598fe0ec7bd3da1844e2b0431740ef2e3aab92aa`, 2026-11-23).  
**Authoritative design:** [MO2-INTEGRATION-DESIGN.md](MO2-INTEGRATION-DESIGN.md).  
**Status at creation:** **DESIGN DOCUMENTED; NO IMPLEMENTATION STARTED; NO MO2 GAMEPLAY VALIDATION**. Existing `Mo2FeatureGate.Enabled=false` remains unchanged. Each stage below must update this ledger with commit, tests, evidence, unresolved gaps, and the next gate; no stage may be marked complete from tests of a mere file listing.

## 2026-11-23 — isolated Stage 0/1 foundation committed; runtime gates still BLOCKED

**Scope:** GCO `MO2_integration` only. Compared checkpoint workflow conventions with the separate `integration/phase2-main-reconcile-20261120` branch as an example; did not merge, change or reinterpret its implementation. Existing `.github/workflows/build-windows.yml` and `redscript-phase2-checkpoint.yml` now include `MO2_integration` **only in their pre-existing TXT checkpoint push filters** (`.ci/checkpoints/full-gco.txt`, `redscript.txt`). Ordinary source, test and docs commits do not launch CI.

**New source code:**
- `src/GTotalOptimizer.Core/Mo2ProfileSelection.cs` verifies requested portable/global instance, explicit configured profile, API-reported Cyberpunk executable and the existing `Mo2SourceInspector` candidate winner map. A missing/corrupt modlist or foreign profile is not treated as a verified MO2 stack. It is **read-only** and explicitly cannot authorize profiler install or generation.
- `integrations/mo2/gco_mo2_bridge.py` is a **prototype MO2 IPluginTool** publishing its actual managed game's directory, currently displayed profile, profile directory, configured Mods/Overwrite, MO2 executable and process identity into a JSON heartbeat under the user's LocalAppData. This prototype has no active install/launch/delete operations and requires a real MO2 Python-plugin test before distribution.
- `src/GTotalOptimizer.Core/Mo2CompanionDiscovery.cs` consumes the read-only heartbeat only after checking path identity (MO2 executable directory, configured Mods/Profiles/Overwrite), process liveness, schema, JSON shape and freshness; no status report enables profiler installation. Fake process, stale and foreign-root data must be rejected.

**Regression fixtures:** `tests/GTotalOptimizer.Core.Tests/Mo2ProfileSelectionTests.cs` and `Mo2CompanionDiscoveryTests.cs`, callable via `--mo2-profile-contract`; full GCO TXT checkpoint invokes this focused test only on `MO2_integration`. No live MO2, native Cyberpunk capture, Python plugin loading or gameplay proof yet. GCO feature gate stays disabled.

**Current MO2 API finding:** documented `IOrganizer.startApplication(..., profile=...)` explicitly selects a launch profile. `IModList.setActive(name, active)` and `setPriority(name, priority)` have **no profile argument**; `IOrganizer.profileName()` reports the *currently loaded* profile, and no public `setCurrentProfile` appeared in the documented Python interface. `IPluginFileMapper.mappings()` is a possible research avenue, but its relative precedence and profile-scoped lifecycle are **unverified**. Do not confuse these API declarations with real selected-noncurrent-profile activation or native forced-load proof. Never edit a user's modlist directly or ask them to coordinate profile selection manually.

**Still required before any deployment:** test a supported API mechanism to activate a single session-owned profiler overlay for selected profile A while MO2 UI displays B, with B unchanged and no original source writes; prove CET ASI + GRSP loading and result locations in real MO2. Failure is a hard blocker, not a reason to enable `Mo2FeatureGate`.

## Stage 0 — API and source-contract proof [IN PROGRESS — RUNTIME BLOCKED]

- Review `AGENTS.md`, `docs/HANDOFF/CORE.md`, WPF Page 1 and the current GCO/CET/GRSP owner source. Record current versioned MO2 Python API/Cyberpunk plugin capabilities, supported native loaders and RootBuilder exceptions.
- Verify exact API semantics for `IOrganizer.startApplication(..., profile=...)`, `createMod`, `IModList.setActive/setPriority`, `removeMod`, virtual source winner resolution, game root/profile enumeration, and output routing.
- **Hard question:** can the companion activate a mod for the dropdown-selected profile when the MO2 UI currently shows a different profile, without manual user selection and without unsafe `modlist.txt` editing? Prototype the supported method and a refused case; do not assume that the explicit launch-profile argument also changes the selected UI profile.
- Define fixture profiles: direct-only, portable MO2, global multiple instances, external Mods/Profiles/Overwrite paths, mixed physical + MO2, disabled/shadowed/partial mods, old RootBuilder, custom overwrite; collect source hashes and minimal runtime evidence.
- **Exit:** documented reproducible API probe and clear supported/refused matrix. No user-facing feature activation.

## Stage 1 — Discovery and Page 1 shell [IN PROGRESS — READ-ONLY CORE ONLY]

- Implement two equal-footprint Direct/MO2 source modes inside existing Page 1 Game Source box; keep main layout and three existing capture buttons. In MO2: one MO2 folder picker, auto-resolved game root, optional instance choice, **profile dropdown listing all configured profiles**, real source/status counters; persist selected instance/profile. Advanced recovery only when auto-discovery cannot prove paths.
- Extend existing `Mo2InstallLocator`/`Mo2SourceInspector` rather than new scanner; include physical game baseline, exact enabled packages, precedence, Overwrite and source-owner provenance. Never claim VFS-verified from physical scanning.
- **Exit:** portable/global/custom-drive discovery and dropdown unit tests; Direct UI unchanged; Refresh read-only; MO2 installer buttons still blocked until later stages.

## Stage 2 — MO2 companion and selected-profile control [NOT STARTED]

- Create a minimal MO2 plugin/adapter and a local GCO request/reply protocol; authorize only narrow operations scoped to the chosen instance, profile, session and owned temp mod. Check plugin installation/restart handshake; no unsafe arbitrary execution or path traversal.
- Use supported MO2 API to create a uniquely named temporary mod, enable and prioritize it for the **chosen** profile, verify exact winner paths; launch game via `startApplication(..., profile=<selected>)`.
- Prove selected profile A operates while the MO2 UI is on B; prove B remains untouched before/after, including interruption/restart. If impossible with supported API, record exact blocker and stop; do not silently ask the user to switch or edit MO2 profile files behind its back.
- **Exit:** real MO2 process/API proof of activation, winner precedence, selected launch and reversible state in multiple profiles. No native profiler needed yet.

## Stage 3 — Reuse source-owner profiler payloads [NOT STARTED]

- Read winning CET ASI, 0-Engine `init.lua`, scheduler(s), controls and bindings for the selected profile; create an isolated, game-shaped *disposable fixture*. Run the existing CET manager's authoritative validation/injection against that fixture; inspect exact before/after and assemble only necessary overrides into one MO2 temporary mod. **No actual game/mod originals modified.**
- GRSP uses its owner-verified DLL + required data paths in the same temporary mod. Preserve both profiler identities, F11 source-owner controls and 0-Engine scheduler ownership; do not introduce a duplicate injection implementation.
- If CET manager cannot safely produce an overlay in staging, explicitly introduce a minimal source-owner `prepare-overlay` contract in the CET repo after owner review; never replace its authoring logic in GCO.
- **Exit:** stock/adaptive/integrated/partial/unsafe 0-Engine fixtures, ASI hash identities, duplicate-bridge refusal, file diff and immutable original hash tests; safe rollback on every failure.

## Stage 4 — Actual MO2 runtime and gameplay capture [NOT STARTED]

- Activate the verified single profiler mod under the user-selected profile and launch Cyberpunk through MO2. Confirm the **native CET profiler ASI**, GRSP DLL, 0-Engine scheduler and controls really load and execute exactly once. Preserve gameplay and native fallback.
- Verify current MO2 Cyberpunk plugin forced-load behavior and mixed physical CET/RED4ext installs; refuse unproven older RootBuilder or incompatible loaders. Verify session handles, F11 start/stop, native output and PresentMon independence under active capture and game shutdown.
- **Exit:** real Windows/MO2/Cyberpunk captured evidence and no double profiler, no source-owner transaction in originals. Mocked VFS tests alone are insufficient.

## Stage 5 — MO2-aware collection, cleanup, recovery [NOT STARTED]

- Discover whether CET/GRSP write to real game paths, MO2 Overwrite or selected custom output; bind each result to current capture and hash-seal it into the established GCO RESULTS/session archive with source-owner report and PresentMon chronology.
- After confirming collection and game closure, disable/remove **only** the session-owned temp mod and profiler-owned scratch. Verify effective originals restored by hashes and winner/owner identity. Never clear arbitrary Overwrite content or another profile's captures.
- Maintain durable journal across GCO/MO2/game crashes, interrupted installs, failed collection, profile change, result/overwrite drift, manual restore and retry. Restore must be available even if the normal GCO journal is damaged but owned MO2 state survives.
- **Exit:** end-to-end install → F11 capture → collect → verify → restore under MO2, tested recovery/refusal paths, original sources byte-for-byte preserved and no result loss.

## Stage 6 — Effective-source adapter for Page 2 [NOT STARTED]

- After profiler removal, expose exact **current selected-profile** CET Lua and `r6/scripts` effective winners plus physical baseline in a read-only, hashed source provider. Stage an immutable disposable source snapshot only as needed to feed the existing CET owner `--game` and GCO REDscript source-root based consumers, without duplicating their generators.
- Bind capture/session provenance to selected instance/profile; re-evaluate live source at Analyze/Generate, detect drift between plan and execution, refuse incompatible changes, and admit all safe new work. Do not require captured mod lists as source inventory.
- Preserve generic CET 0-Engine → native integration → generic finalization → source-aware semantics; REDscript generic-first and structural partial/full/idempotent generation; one game-relative optimization ZIP and external ownership sidecar.
- **Exit:** full untouched/partial/complete/current source coverage, shadow/conflict tests, no source modification, same Page 2 layout and output as Direct, exact profile isolation.

## Stage 7 — Integration, compatibility matrix and feature activation [NOT STARTED]

- Run normal Direct regression and selected-profile MO2 multi-instance end-to-end suites using the **existing** Windows CI workflow/checkpoint policy. Cover portable/global, all configured paths, UI profile B vs selected A, 0-Engine variants, physical game baseline, duplicated filenames, modified Overwrite, native failures, interruption, no-capture retry, restore, idempotence, generated ZIP collisions and gameplay.
- Test with actual MO2 versions/capabilities claimed supported. Mark untested setups unsupported; do not silently implement ad-hoc RootBuilder copying.
- Verify native capture hashes/F11 windows, original source hash restoration, active-file winners, one owner for each worker/hook, correct current-profile source analysis and generated ZIP installation via MO2.
- Only when all acceptance gates pass and user approves, switch the feature gate and enable MO2 mode. Never silently merge to `main`, tag a release, bump version or trigger unrelated CI.

## Minimal next task

**Execute Stage 0 only.** First establish the noncurrent-profile mod-activation method in the MO2 plugin API; this is the hardest non-negotiable acceptance condition and determines the viable companion protocol. Then establish source/writer routing with tiny real-profile fixtures. Do not build UI assumptions or a copy-based fallback first.

## Status / milestone record (append dated entries here)

| Date | Stage | State | Evidence | Remaining blocker |
| --- | --- | --- | --- | --- |
| 2026-11-23 | 0–7 | Not started | Design/branch handoff only; no code changes, tests or real-game proof | Selected noncurrent-profile activation, USVFS native loading, output location, source-provider equivalence |

On each substantive commit: record branch HEAD, exact changes, unit/CI evidence versus actual MO2/gameplay proof, known source drift/restore risks, and next stage. Never label a stage passed because CI is green or a mocked provider reports success.
