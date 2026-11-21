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

## Implemented candidate

- `src/GRedscript.Resolver/Phase2/VirtualAtelierSpawnPacingPass.cs`: one source-aware pass `VIRTUAL_ATELIER_DROPPOINT_SPAWN_PACING_V1`.
- Isolated CLI entry `phase2-vad-spawn --game ... --out ... [--base-overlay ...]`.
- `tests/GRedscript.Resolver.Tests/test_phase2_vad_spawn_pacing.py`: stock, partial (field/callback), complete, relocate/custom, prior overlay, incompatible, absent and idempotence.
- Independent `.github/workflows/redscript-pass6.yml` validates the post-generic source plus the existing GCO one-pass semantic overlay and official SCC 0.5.31. Do not ship SCC to users.

**Integration status:** Isolated branch candidate; central GCO generator stage and numbering are **intentionally untouched**. After independent CI succeeds, the integrating owner must add this source-aware pass to the canonical one-generation stage after reconciling any overlapping work, rerun combined SCC/full app CI, and obtain in-game gameplay/performance evidence before claiming speedups.

**No current generated overlay has been verified by a new in-game capture.**
