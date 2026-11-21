# REDscript Agent 3 — historical Pass 5.0 isolated implementation handoff

Branch: `dev/redscript/pass5`. Forked from green SHA `96d66c66642ac52dd13fd4001416572b9169bace`.
Owner: Pass 5.0 only. The user explicitly authorized branch-local production one-pass integration on 21 November 2026. No merge into `main` or another agent's active development branch.

## Audit of all eight historical Pass 5 files

Historical source: user-provided `G-RED_optimizatios_seperate_passes.zip`, directory `G-REDruntime_Pass5.0_RemainingHotOwners_MODS`.
Executable baseline: `scriptsliveAFTERgeneric.zip` and authoritative private `original_cet_source/base_after_generic`; preserve all generic and prior semantic outputs.

| Historical owner file | Disposition |
| --- | --- |
| `Dark Future/UI/DFBaseGameUIOverrides.reds` | Historical 3→6 refresh gating already superseded by approved aggressive rule #25 and tagged status-icon #13. No second timing rewrite. |
| `FlushingEtiquette/FE_Places.reds` | Global BaseMinimap Update wrapper removal with session pins already implemented at #15. No duplicate. |
| `HQ_HoloGirl/Engine/HoloGirl.reds` | Resource loaded filter, cached material path, color/emissive hoist already implemented at #22. Pass 4 agent owns any genuinely independent later combat work. |
| `HQ_HoloTravel/HQ_HoloTravel.reds` | **New isolated source-aware generator** `HQ_HOLOTRAVEL_MATERIAL_EVENT_FASTPATH_V1`: type-wide material event guard, null-material check, beam/flare filter before holo state read, single color resolution outside parameter loop; original branch writes, callbacks and config remain. |
| `MapWaypointBugFixes/ArrivalPhantomFix.reds` | Historical per-controller visibility-edge cache specifically incompatible with pooled mappin controller ownership; current documentation rejects it. Preserve SetRootVisible synchronization. |
| `RedWindows/RW_Window.reds` | Historical retained Find cache already superseded by stronger #19/#23/#24/#26, and later widget mutation/visibility contracts. No duplicate. |
| `TriggerModeControl/TriggerModeControl.reds` | Literal trigger CNames already #20. Historical removal of ReadyEvents refresh and aiming OnUpdate state cache remains unported: weapon identity/settings/hint invalidation depend on more than two state booleans; preserve original callbacks until proven. |
| `zzy_bChipwareExpansion/chipsScripts/chipPerkUnlock.reds` | Overlaps Pass 4 agent's same method/owner. Pass 5 historical jump-table rewrite also consolidates 24 chip conditions and changes fallback boundaries. **Do not edit concurrently**; integrate only after Pass 4 stage review and source-state comparison. |

## New isolated files

- `src/GRedscript.Resolver/Phase2/HoloTravelMaterialEventPass.cs`
- `tests/GRedscript.Resolver.Tests/Pass5Harness/{Pass5Harness.csproj,Program.cs}`
- `tests/GRedscript.Resolver.Tests/test_phase2_pass5_holotravel.py`
- Existing workflow branch trigger expanded for `dev/redscript/pass*`; existing stage order and central `RedscriptGenerationPlan.cs`/main product CI unchanged.

The test harness invokes the **production C# generator via reflection**, without duplicating its implementation. The production `RedscriptGenerationPlan.Stage()` now also invokes the same generator as ordered semantic #37. A public `phase2-holotravel-material` CLI exposes independent generation for exact one-pass parity verification. Source-state tests: post-generic stock, partially guarded, partially hoisted, missing holo guard, fully complete/idempotence, compatible relocated/custom source, unsafe filter/validation scoped refusal and absent mod. In GitHub CI, the independent candidate overlay is compiled with the existing official `Verify-Phase2Checkpoint.ps1` and SCC 0.5.31 against the complete 2,331-file private post-generic source.

## Validation and integration status

- **Final fast Phase 2 [#207 SUCCESS](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/44874654298)** at source/test commit `0424771bfdd8c5778cf70a4d775db29fee560a12`: **197 source-state tests passed**, including ten HoloTravel cases, now explicitly rejecting custom impure color resolvers. The candidate independently emitted one HoloTravel file from the real 2,331-file post-generic source, passed **official SCC 0.5.31** using the existing checkpoint verifier, and repeated generation returned COMPLETE with no extra output. The existing canonical stages independently passed combined SCC with **29 currently registered semantic overlays / 43,512,293 compiled bytes**. That canonical stack **does not yet include HoloTravel** because central stage registration belongs to the integration owner. Previous [#202 SUCCESS](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/44874209829) covered the earlier nine-case implementation before strengthening pure-color proof.
- **Full GCO product/ZIP [#1087 SUCCESS](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/44875337791)** at `e66852aa`: 361 package/installation contracts, unified .NET 10 desktop app, source/manifest/rollback integration and published development artifact `Gs-Cyberpunk2077_Optimizer-v1.2.5-run1087-a1` (ID `14694844565`, 90,564,552 bytes). No in-game generated overlay proof or performance claim. Not merged to main.

- **Canonical branch-local Generate stage #37 integrated:** `RedscriptGenerationPlan.Stage()` calls the same source-aware generator on the effective post-generic overlay, stages only accepted owner-local `.reds`, preserves prior generic markers, records `holotravel-material-event` applied or a narrow skipped reason, and includes it in the normal combined ZIP and ownership accounting. Stages #11–#36 remain in place.
- Added independent `phase2-holotravel-material` CLI output to the fast checkpoint with source-level contract, **byte-for-byte comparison against the canonical one-pass Stage #37**, and combined official SCC (without player-side SCC). [Fast #215 SUCCESS](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/44875328295) confirmed 197 tests, exact independent-to-canonical #37 bytes, 2,331 post-generic sources + 30 semantic overlays, official SCC 0.5.31 and unchanged original source.
- Do not cherry-pick historical `.reds` files into an installed game or overwrite a current mod. Prefer cherry-picking the isolated generator/tests into the integration branch.
- Cross-pass conflicts: Pass 4 owns zzy chipware and potentially HQ_HoloGirl; integration owner decides combined code in a source-aware single-stage composition.

## Exact behavior boundaries

The HoloTravel patch only admits the event when `ParticleHandler` binds `OnUpdate` for `CMaterialInstance`, its beam/flare resource keys are source-proven, `BaseHandler.IsValid` is exactly the original event+holo defined guard and `ResolveColor` is an accepted current source utility. Unknown guard/filter/loop mutation refuses this operation, not the entire mod. The original callback binding and material parameter loop survive. Generator hashes verify original sources stayed unchanged; output is game-relative only.

Future enhancement: proving compatible partial custom material update loops and eventually canonical full combined generation. Do not assert runtime behavior or savings until gameplay parity and comparable capture.
