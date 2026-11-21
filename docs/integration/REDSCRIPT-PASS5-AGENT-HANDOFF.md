# REDscript Agent 3 — historical Pass 5.0 isolated implementation handoff

Branch: `dev/redscript/pass5`. Forked from green SHA `96d66c66642ac52dd13fd4001416572b9169bace`.
Owner: Pass 5.0 only. Do not merge into `main` or register stage numbers independently of the integration agent.

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

The test harness invokes the **production C# generator via reflection**, without duplicating its implementation or touching central stage registration. Source-state tests: post-generic stock, partially guarded, partially hoisted, missing holo guard, fully complete/idempotence, compatible relocated/custom source, unsafe filter/validation scoped refusal and absent mod. In GitHub CI, the independent candidate overlay is compiled with the existing official `Verify-Phase2Checkpoint.ps1` and SCC 0.5.31 against the complete 2,331-file private post-generic source.

## Validation and integration status

- This branch is an **isolated candidate**, not yet a canonical Generate stage. Main integration owner must add one ordered stage and the canonical combined source/ZIP tests after merging.
- The focused fast CI outcome must be recorded from the newest branch run before marking source/SCC green; no player-side SCC distribution or gameplay/FPS claim.
- Do not cherry-pick historical `.reds` files into an installed game or overwrite a current mod. Prefer cherry-picking the isolated generator/tests into the integration branch.
- Cross-pass conflicts: Pass 4 owns zzy chipware and potentially HQ_HoloGirl; integration owner decides combined code in a source-aware single-stage composition.

## Exact behavior boundaries

The HoloTravel patch only admits the event when `ParticleHandler` binds `OnUpdate` for `CMaterialInstance`, its beam/flare resource keys are source-proven, `BaseHandler.IsValid` is exactly the original event+holo defined guard and `ResolveColor` is an accepted current source utility. Unknown guard/filter/loop mutation refuses this operation, not the entire mod. The original callback binding and material parameter loop survive. Generator hashes verify original sources stayed unchanged; output is game-relative only.

Future enhancement: proving compatible partial custom material update loops and eventually canonical full combined generation. Do not assert runtime behavior or savings until gameplay parity and comparable capture.
