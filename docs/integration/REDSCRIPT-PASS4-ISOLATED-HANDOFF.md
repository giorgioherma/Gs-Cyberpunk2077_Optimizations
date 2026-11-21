# REDscript historical Pass 4.0 — isolated combat handoff

Date: 2026-11-21. Branch: `dev/redscript/pass4`, based on green parent `96d66c66642ac52dd13fd4001416572b9169bace`. Do **not** cherry-pick changes from another agent's unfinished working branch.

## Ownership boundary
- This branch owns `src/GRedscript.Pass4/**`, `tests/GRedscript.Resolver.Tests/test_pass4_isolated.py`, `.github/workflows/redscript-pass4-checkpoint.yml`, and this handoff only.
- It deliberately **does not change** `src/GRedscript.Resolver/RedscriptGenerationPlan.cs`, `src/GRedscript.Resolver/Program.cs`, semantic stage numbering, or existing Phase 2 shared CI. Integration agent combines accepted operations in the original one-run REDscript generator and assigned stage order, not by shipping a second runtime CLI.
- Reuses `SourceAdmission.cs` from GCO via a linked compile item, not a forked lexer or source parser.
- No changes to `main`, installed game, CET/GRSP/G-REDruntime owner sources or profiler artifacts.

## Pass 4 historical audit (six files, five owners)
- **DigitalVixenCore/DVCore_Footsteps.reds**: historical successful RTTI class cache already covered by semantic **#11**. Not duplicated.
- **HQ_HoloGirl/Engine/HoloGirl.reds**: historical holo material event/cache and approved Pass 5 value hoist already covered by **#22**. Not duplicated.
- **StealthRunner/npc_puppet.reds**: isolated `STEALTHRUNNER_NPC_PLAYER_CACHE_V1`. Adds weak NPC-local player handle, stores only at existing activation, clears on detach, resolves on cache miss in detection handler. Leaves original quest registration, wrapped handlers, NPC and detection logic unchanged.
- **zzy_bChipwareExpansion/chipsScripts/chipPerkUnlock.reds**: isolated `CHIPWARE_PERK_RELEVANCE_GATE_V1`. Builds the relevant-perk predicate **from source actual branches** rather than hard-coded historical 24-name set, skips costly game/player/stat reads only for unrelated perk type, always preserves `wrappedMethod(perkType)` fallback. Supports source-added eligible perk branches and partial predicates.
- **ReinforcementsSystem/Utils.reds + ReinforcementSystem.reds**: isolated `REINFORCEMENTS_PREPARED_QUAD_CACHE_V1`. Prepares sorted quad data at player attach, verifies all eight live coordinate components on each cached use, uses original `IsPointInQuad` (including generic safe AABB) on any mismatch/missing entry. Original public `restrictedCordinates` remains mutable, the original `SortSquare` and `IsPointInQuad` methods and both generic markers are retained, and existing RNG, quest, district, NPC eligibility and callback cadence are unchanged. Clears owner cache on detach. This deliberately does **not** bring the old 250 ms context polling state, which could delay quests/district/race transitions; that independent historical operation requires source/timing proof.

## Generator / acceptance
- CLI `dotnet GRedscript.Pass4.dll pass4 --game <post-generic-root> --out <isolated-overlay> [--base-overlay <prior-overlay>]`.
- JSON `pass4-result.json` lists each independent owner status, edit counts, source paths, precise safe-refusal reason and source-stack fingerprint. Output includes only .reds files under game-relative paths and remains outside installed source.
- Validate untouched → applied, partial → missing completed, complete → no-op, relocated/custom → applied, incompatible → narrowly refused, absent → no output, repeated run → zero new changes. The independent fast CI must validate source-state fixtures, original private 2,331-file post-generic input, generated merged `.reds` SCC 0.5.31 and original-byte immutability.
- The isolated CI tests **only Pass 4 atop the post-generic base**. The integration agent must add these classes to the existing one-pass generation plan, compose with all already-integrated semantics through latest stable stage, run full combined SCC and GCO build, then game-test with comparable capture. Do not claim gameplay/FPS/CPU benefits from historical output or SCC.

## Remaining work / responsibility
1. Finish independent CI (record exact run ID, conclusion and full commit SHA here).
2. Investigate any SCC type/ABI or source structural refusal, fixing the owner generator and tests — no corrective stages.
3. Integrate accepted operations into the central plan **only by the integration owner**. Respect other agents' concurrent Stage 2.2/3/5 changes.
4. ReinforcementsSystem historical context scheduler/racing/district/quest throttling remains unported pending event/timing safety proof; do not silently add delayed gameplay-state checks.
5. zzy chipware Pass 5/6/7/8 changes are owned by their respective historical groups, not imported here.

This is a **source-generation implementation branch**, not player-side accepted optimization until integrated and independently validated.
