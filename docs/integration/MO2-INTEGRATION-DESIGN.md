# GCO — MO2 integration architecture (approved design, not implemented)

**Decision date:** 2026-11-23  
**Development branch:** `MO2_integration` (forked from GCO `main` at `598fe0ec7bd3da1844e2b0431740ef2e3aab92aa`).  
**Authority:** User-approved GCO-specific MO2 design. Read [stage and evidence ledger](MO2-INTEGRATION-PROGRESS.md), [AGENTS.md](../../AGENTS.md), and [mandatory core handoff](../HANDOFF/CORE.md). Earlier "MO2 deferred" decisions describe the *shipped baseline*, not a prohibition on isolated work in this branch. No implementation or validation is claimed by this document.

## Product decision and invariant

Add **Direct / MO2** source tabs *inside the existing Page 1 game-source area*, without adding a page or changing the approved three-step workflow or visual design. In MO2 mode, the player chooses **one MO2 application folder** and **one profile from an automatically enumerated dropdown**. Detect the matching instance (portable or global), game directory, configured source/overwrite directories, native framework prerequisites and effective file winners. When there are multiple instances, present an instance choice only if necessary. Advanced recovery selectors are for genuinely unresolved configurations, not default setup.

GCO itself MUST activate the **selected** profile's temporary profiler mod, then launch Cyberpunk through MO2 for that very profile; the user is never required to change MO2's currently selected profile manually. The entire working set, profiler session, native outputs, and Page 2 source view must belong to the selected instance+profile, not to whichever profile MO2's UI happens to display.

One GCO build, one game-relative optimization ZIP, one existing two-page WPF workflow. No new optimizer or duplicated semantic rules. **Direct mode remains unchanged.** MO2 mode is disabled until actual runtime proof and explicit acceptance.

## Source/research distinctions

Verified from the GCO `main` source:

- `src/GTotalOptimizer.Core/Mo2InstallLocator.cs` discovers portable/global instance INIs; `Mo2SourceInspector.cs` parses configured directories, profiles, enabled `modlist.txt`, physical game baseline, Overwrite, loose-file precedence and winning owners. This is **candidate file provenance**, not proven USVFS runtime equivalence. `Mo2FeatureGate.Enabled` is currently `false`.
- `GameInspection.cs`, `FrameworkPreflight.cs`, `CetReadOnlyAnalysisService.cs`, `CombinedOptimizationPassService.cs`, `GRedscript.Resolver/SourceAdmission.cs`, and the WPF Page 1 currently rely on physical `gameRoot` paths. Existing REDscript/CET generator input cannot simply be relabeled as VFS without adapting source admission.
- CET owner `ProfilerService.Install` backs up/replaces `bin/x64/plugins/cyber_engine_tweaks.asi`; modifies/provisions `0-Engine/init.lua`, `modules/Scheduler.lua` and/or `modules/CETProfilerScheduler.lua`; adds `CETProfilerControls`; examines `bindings.json`; journals/collects/restores. It is **not a pure file-copy installer**. It must not be run directly on an MO2-backed live source or on original MO2 mod storage.
- GRSP owner manages `red4ext/plugins/G-REDscript-Profiler.dll` and `red4ext/plugins/G-REDscript-Profiler/{CaptureTitle.txt,RESULTS/}`; its direct installer is also physical-game-path owned. Actual native capture result locations under MO2 must be discovered.
- GCO `UnifiedProfilerWorkflow` has physical owner installer, capture, result archive and restore contracts; change via an additional deployment/provider boundary, not a parallel state machine.

Externally documented MO2 capabilities (API evidence; **not** user gameplay proof):

- MO2 Python `IOrganizer.createMod` creates a mod; `IModList.setActive` and `setPriority` control enable/priority; `removeMod` can remove a mod. `IOrganizer.startApplication(executable, ..., profile=..., forcedCustomOverwrite=...)` launches with USVFS and accepts an explicit **profile** parameter. These APIs are documented, but selected-profile mutation semantics need verification: `IModList` changes may apply only to the currently selected UI profile. Do **not** conflate explicit-profile launch with profile-specific mod activation. No documented `setCurrentProfile` method was found in the API reference inspected; investigate actual supported APIs/controlled synchronization without direct `modlist.txt` surgery.
- Contemporary MO2 Cyberpunk plugin documentation describes CET and RED4ext forced-load support without RootBuilder for newer installs, while legacy RootBuilder and mixed physical/virtual stacks exist. Treat support as a capability/runtime probe, not a universal assumption or version lock.
- Runtime-generated output may land in MO2 Overwrite or a configured custom output; native CET and GRSP F11 captures must be actually located and verified. Never blanket-clear Overwrite.

References:
- [Official MO2 Python API (mobase)](https://www.modorganizer.org/python-plugins-doc/autoapi/mobase/index.html)
- [MO2 Cyberpunk support plugin](https://github.com/ModOrganizer2/modorganizer-basic_games/blob/master/games/game_cyberpunk2077.py)
- [MO2 Cyberpunk compatibility notes](https://github-wiki-see.page/m/ModOrganizer2/modorganizer-basic_games/wiki/Game%3A-Cyberpunk-2077)
- [Cyberpunk modding MO2 setup](https://github.com/CDPR-Modding-Documentation/Cyberpunk-Modding-Docs/blob/main/for-mod-users/users-modding-cyberpunk-2077/getting-started/mo2-mod-organizer-2.md)

## Required source and deployment inventory

| Effective game-relative path | Purpose | GCO MO2 requirement |
| --- | --- | --- |
| `bin/x64/plugins/cyber_engine_tweaks/mods/**` | All active CET Lua sources/0-Engine/controls | Read exact effective winners; install only GCO-owned temporary overrides |
| `r6/scripts/**/*.reds` | All active REDscript source including subfolders | Read exact effective winners; generate from effective original/partial/complete source |
| `red4ext/plugins/**` | Framework/plugin inventory; GRSP DLL and capture output | Read; temporary GRSP ownership; collect exact session |
| `bin/x64/plugins/cyber_engine_tweaks.asi` | Active CET binary to profile | Verify native loader + temporary profiler ASI wins and loads |
| `bin/x64/plugins/cyber_engine_tweaks/bindings.json` | CET binding and capture key | Preserve user settings; no inadvertent persistent rewrite |
| `red4ext/RED4ext.dll`, `engine/tools/scc.exe`, `engine/tools/scc_lib.dll`, `engine/config/base/scripts.ini`, `bin/x64/Cyberpunk2077.exe` | Framework/game admission | Read-only effective + physical checks as appropriate; never deploy unnecessary copies |
| Selected instance configuration, profiles and `modlist.txt`, configured Mods and Overwrite paths | Profile, priorities, provenance, output routing | Read/prove selected profile; no guesses from directory presence |

Always include the **physical game source as baseline**, even when MO2 contains all enabled mods. Never infer active mods from a static capture inventory, all stored MO2 packages, or one root-folder presence. Recognize source-only packages, mixed installed paths and legacy configurations only with evidence.

## Proposed deployment boundary

**One temporary GCO profiler mod** per active GCO session, e.g. `GCO Temporary Profilers - <session-id>` in the selected MO2 instance, containing *only necessary winning overrides*:

```text
GCO Temporary Profilers - <session>/
  bin/x64/plugins/cyber_engine_tweaks.asi
  bin/x64/plugins/cyber_engine_tweaks/mods/0-Engine/init.lua            (only if actually changed)
  bin/x64/plugins/cyber_engine_tweaks/mods/0-Engine/modules/...        (required scheduler files)
  bin/x64/plugins/cyber_engine_tweaks/mods/CETProfilerControls/...     (required files)
  red4ext/plugins/G-REDscript-Profiler.dll
  red4ext/plugins/G-REDscript-Profiler/...                             (required controls/data only)
```

This mod uses the **real game-relative layout**, not an invented `root/` wrapper. Do not assume this works with every legacy RootBuilder setup; verify or refuse that configuration. Native binary loading and file winners are separate proofs.

**Reuse the CET owner's authoritative implementation** for safe 0-Engine compatibility classification, adaptive injection and profiler scheduler generation. Preferred feasibility probe: build a disposable physical-layout fixture from the *verified effective selected-profile winners*, run the real CET manager on the fixture, and extract the required verified changed files into the MO2 temporary mod. Ensure the fixture satisfies manager preconditions without fabricating profiler compatibility; it is never the live game. If that is insufficient or bypasses required owner checks, extend the existing CET owner with a small supported `prepare-overlay`/staging contract **in the CET repo**, not a duplicate GCO Lua transformer. GCO owns only staging/deployment/orchestration. GRSP supplies its own verified native payload; no duplicate Rust profiler.

MO2 companion/plugin is a small integration adapter, not a second profiler. It owns actual MO2 mod creation/activation/priority/launch/deactivation/removal through supported APIs, with an authenticated local/request-bound bridge to GCO (exact transport to be chosen after API reconnaissance). Prefer unique session-owned mod names that never trigger MO2's "merge or replace existing mod" dialog. Preserve modified user data even if encountered inside a GCO-named folder; do not delete foreign content.

**Critical selected-profile constraint:** MO2's documented `startApplication(..., profile=name)` chooses the launch profile, but `modList().setActive/setPriority` is not proven to target a *noncurrent* profile. Stage 2 must demonstrate automatically enabling the profiler **for the dropdown-selected profile** (even when MO2 UI is showing another one), with no unintended changes to another profile. Find a supported profile-targeted API or safe plugin-managed synchronization before installing. If impossible, block MO2 installation with an exact reported capability blocker rather than asking the user to coordinate or editing `modlist.txt` behind MO2.

## Session protocol and trust

```text
Select MO2 instance + profile
  -> verify instance/game/profile + effective source winners
  -> Prepare selected-profile CET/GRSP profiler overlay
  -> MO2 activates owned temporary mod for selected profile at proven precedence
  -> confirm native ASI/DLL, 0-Engine bridge and required modules win
  -> GCO START requests MO2 startApplication(..., profile=<selected>)
  -> native CET/GRSP capture, PresentMon independent
  -> game closed; discover actual capture locations (physical/Overwrite/custom)
  -> hash-seal source-owner result archives and F11 session evidence in GCO RESULTS
  -> verify collected data; disable/remove only owned temporary overlay/files
  -> verify original effective winner bytes and ownership restored
  -> Page 2 reads current selected-profile effective sources, no profiler overlay
  -> existing CET owner + GCO REDscript generators -> same one combined ZIP
```

Persist a **durable recovery journal before any mutation**: game root, MO2 app and instance canonical identity, profile identity, source snapshot identity/manifest, session/attempt ID, GCO temporary mod ID/path, planned/actual file hashes, former priority and enable state if modified, capture roots, collection proofs and each recovery stage. Check process closure, prevent double installs and cross-profile leakage. On cancellation/crash preserve journal and uncollected data; retry collect/restore, never silently discard uncertain bytes. Continue only after verifiably restored originals. Never clean an entire Overwrite folder.

**Capture identity vs live-source authority:** tie capture and generation to the same selected MO2 instance/profile; record capture's source fingerprint for provenance and source-drift checking. Page 2 must read the **current effective stack** for that selected profile (not the frozen capture mod inventory) and refuse unsafe drift or mismatches with precise reasons. Native measurement can inform applicable owners but cannot imply a mod is currently installed.

**Output:** unchanged root-relative CET+REDscript ZIP and external ownership sidecar. Instruct MO2 users to install it as a separate enabled mod with proven winning priority; do not automatically deploy generated optimizations. No profiler/temporary/capture files or private state in the ZIP.

## GCO internal surfaces to adapt (no new owner implementation)

- UI: existing `src/GTotalOptimizer.App/MainWindow.xaml[.cs]`, settings/source status only on Page 1; game root autolocated, selected profile dropdown and actual source counts. Keep current WPF art/layout/stage buttons/Refresh semantics.
- Discovery: extend `Mo2InstallLocator`, `Mo2SourceInspector` and `Mo2FeatureGate` as authoritative read-only services; do not replace with duplicate inventory walk. Keep the selected instance/profile explicit.
- Orchestration: add a direct/MO2 deployment provider boundary around `UnifiedProfilerWorkflow` and source-owner manager calls, preserving the *existing* capture/archive/recovery state machine rather than cloning it.
- Source: add a verified direct/MO2 effective-source provider consumed by `CetReadOnlyAnalysisService`, CET owner Resolver CLI and GCO `GRedscript.Resolver/SourceAdmission`. Use one read-only source snapshot/manifest if the backends require physical paths. Each authoritative generator still applies its existing stages/semantics; stage source only, never rewrite originals.
- Reporting: `CollectedCaptureStore`, owner outputs and PresentMon QPC/F11 alignment retain existing integrity contracts; add only MO2 origin paths/ownership in private evidence. Page 2 layout/reporting/output is unchanged.

## Invariants and non-goals

1. Direct installed-source mode and existing source-owner install/collect/restore are unchanged.
2. Only the selected MO2 instance/profile is modified; no user coordination, silent global profile switch, user mod edits or duplicate installer backend.
3. MO2 deployment must prove **effective runtime** bytes, native forced-load and Lua/REDscript behavior, not file listing, markers or successful API responses alone.
4. Original CET, REDscript and framework mods remain byte-identical; temporary bridge files are prepared from the actual winners. No source drift, duplicate callbacks, double-loaded profiler or competing 0-Engine scheduler.
5. No new Lua/REDscript optimization rules, native profiling hooks, version pins for user mods or alternate generator pipelines in this feature.
6. Scope exclusions: unsupported MO2/RootBuilder/mixed-loader arrangements are accurately refused until proven, not rewritten/migrated by GCO.
7. No code in `main`, release, product version bump, destructive overwrite cleanup or new arbitrary workflow. A doc-only branch is not evidence of a working MO2 feature.

See [MO2 staged plan and evidence ledger](MO2-INTEGRATION-PROGRESS.md) for staged acceptance gates and exact blockers.
