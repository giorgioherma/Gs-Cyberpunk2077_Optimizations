# REDscript Historical Pass 6.0 — isolated Agent 4 handoff

**Development branch:** `dev/redscript/pass6` from verified `96d66c66642ac52dd13fd4001416572b9169bace`.
Do not merge to `main`, alter central `RedscriptGenerationPlan.cs` stage numbering,
or overwrite the other agents' Pass 2.2/3.0/4.0/5.0 generators.

## Source audit

Historical source: `G-RED_optimizatios_seperate_passes.zip`,
`G-REDruntime_Pass6.0_FramePacing_StutterElimination_MODS`.
Effective source authority: `original_cet_source/main/base_after_generic/r6/scripts`
(2,331 files). Historical files were inspected against the current post-generic
owner source, not copied into the game ZIP.

| Historical owner | Pass 6 decision |
| --- | --- |
| Dark Future / DFBaseGameUIOverrides | **Already covered in materially different form** by semantic #12/#13/#25. Historical pass-6 cadence shifts 6-to-3 and introduces an additional effect listener gate. Preserve current approved aggressive throttles; no competing tick. |
| Enhanced Vehicle System | #30 already reuses the OnAction PS handle; camera listener removal and component array clearing need independent component reassembly/edge proof. Do not reuse obsolete whole file. |
| HQ_HoloTravel | Historical global CMaterialInstance listener processing window can miss late streamed resources after its three-second expiry. Source does not establish complete invalidation on all resource-load paths. Defer; Pass 5 agent may touch independent material operations. |
| MapWaypointBugFixes | Historical per-controller visibility edge cache explicitly excluded for pooled controller ownership; no reintroduction. |
| ReinforcementsSystem (three files) | **Reserved for Pass 4 combat owner** (RNG, polygon edges, call/tick behavior). Do not independently modify. |
| ShatteredChrome / SC | #35 owns periodic scheduling and original callback fallback; historical HUD snapshot/coalescing changes event timing and needs separate parity proof. No competing scheduler. |
| VirtualAtelierDelivery | **Independent candidate implemented**: source-local, 50 ms, one-drop-point-per-tick queue; original synchronous operation when DelaySystem missing; duplicate-tag protection, cancellation on despawn/detach, attach generation invalidation. Original mappin/notification logic is unchanged. |
| zzy_bChipwareExpansion | Historical Pass 6 rewrites perk override condition evaluation. Also touched by Pass 5; defer independent analysis to avoid conflicting optimization of the same function. |

## Production one-pass integration on this isolated branch

Both independent Pass 6 operations are now wired directly into the production
`RedscriptGenerationPlan.Stage` behind the ten generic rules and existing
semantics: **#37 Virtual Atelier spawn pacing** and **#38 EVS camera registration
removal**. Stage numbers are provisional for branch integration because the
concurrent `dev/redscript/resolver_handoff` owner has since introduced new
stages 37+ on its own branch. Reconcile numbering during the eventual merge,
not by modifying their branch from this agent.

EVS camera listener operation:
- Removes only `CameraX`, `CameraY`, `CameraMouseX` and `CameraMouseY`
  `player.RegisterInputListener(this, ...)` lines in the proven
  `@wrapMethod(VehicleComponent) RegisterInputListener` wrapper when the
  corresponding action is never read elsewhere in the source.
- Preserves both `wrappedMethod()` calls (unsupported-vehicle branch and
  normal branch), all non-camera vehicle controls, current action snapshot
  generics, and existing #30 `OnAction` local PS reuse.
- Detects partial already-removed registrations and avoids reapplying
  changes; unknown camera use refuses only this operation.
- Uses version-fluid code matching and staged effective source; no old file
  wholesale replacement and no independent ZIP.

**CI evidence:**

- [Isolated Pass 6 #3 SUCCESS](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/44875368392) verified the VAD-only stage: 196 tests and official SCC 0.5.31, 2,331 original files + 30 semantic files, 43,514,581 compiled bytes.
- [**Expanded Pass 6 #6 SUCCESS**](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/44875675607) verified **both** owner generators integrated into the production one-pass Stage: 202 source-state tests, exact independent byte parity, 2,331 real post-generic files, 30 effective semantic overlays and official SCC 0.5.31, **43,514,477 compiled bytes**. Original source unchanged.
- [**Full GCO #1092 SUCCESS**](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/44875742283): 361 application/install/CLI/ZIP source contracts, one shared .NET 10 runtime, actual owner integrations, WPF and source/rollback validation. Artifact `Gs-Cyberpunk2077_Optimizer-v1.2.5-run1092-a1` (ID `14696061829`, 90,569,864 bytes).
- Production integration commit: `e2e248bb2a2c71c95f3a70f34d32a355d8f8f044`; final semantic code fix: `a83cc6efea3551bbe9cb8746d836148becbe6b0a`. All changes remain branch-local. The integration branch's independent stage numbers require reconciliation when these files are brought across.

## Implemented, stage-owned files

- `src/GRedscript.Resolver/Phase2/VirtualAtelierSpawnPacingPass.cs` — `VIRTUAL_ATELIER_DROPPOINT_SPAWN_PACING_V1`.
- `src/GRedscript.Resolver/Phase2/EnhancedVehicleCameraRegistrationPass.cs` — `EVS_UNUSED_CAMERA_INPUT_REGISTRATIONS_V1`.
- Canonical integration in `src/GRedscript.Resolver/RedscriptGenerationPlan.cs` — source-aware after generic #1–#10 and existing semantic #11–#36. The two branches of game runtime processing are NOT applied via separate corrective passes.
- `src/GRedscript.Resolver/Program.cs` contains independent test/reference commands `phase2-vad-spawn` and `phase2-evs-camera`; player Generate invokes the one-pass Stage, not these commands in sequence.
- Source-state tests: `tests/GRedscript.Resolver.Tests/test_phase2_vad_spawn_pacing.py`, `test_phase2_evs_camera.py`; independent CI `.github/workflows/redscript-pass6.yml`; full branch GCO packaging CI is enabled without modifying `main`.
- No game ZIP contains SCC, profiler binaries or historical whole-file copies.

**Integration status:** **Production one-pass generator implemented and green on isolated `dev/redscript/pass6`**. The two stage numbers #37/#38 are branch-local placeholders because concurrent agents have allocated stages 37+ in `dev/redscript/resolver_handoff`. Do not force-push, cherry-pick the entire central plan, or merge to stable `main`; migrate these two generator calls after the latest shared semantic stages, reconcile numbering and rerun full combined SCC/application CI.

**Gameplay status:** Not verified with fresh in-game tests or comparable before/after capture. Successful SCC and packaging do not constitute gameplay or FPS/CPU evidence.
