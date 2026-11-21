# REDscript Pass 4–9 consolidation audit — 2026-11-21

**Coordinator branch:** `dev/redscript/integration`, created from `dev/redscript/resolver_handoff` code/docs HEAD `b4b668931d9521f72396a2b57f93ec5b7cafe4dc`. **Status: inventory only, no branch imports or SCC of combined 4–9 yet.** Do not treat individual isolated CI passes as proof of the consolidated ZIP.

| Pass | Exact audited branch HEAD | Latest source-code CI and scope | Production stage wiring in isolated branch | Handoff |
| --- | --- | --- | --- | --- |
| 4 | `499a650d60143f8439be61de6f8cae1b3d472b06` | [isolated #10 SUCCESS](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/44875974828), 12 tests, canonical SCC | #37–39: StealthRunner weak player, Chipware perk relevance, Reinforcements geometry | `docs/integration/REDSCRIPT-PASS4-ISOLATED-HANDOFF.md` |
| 5 | `182ad0c679e540190e43cbd8a610bc16145d32ae` | [SCC #215 SUCCESS](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/44875328295); [full #1087 SUCCESS](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/44875337791) | #37 HoloTravel material event guard/hoist | `docs/integration/REDSCRIPT-PASS5-AGENT-HANDOFF.md` |
| 6 | `a1e1551d16951d7adf2f5d301deebc563ca749b6` | [SCC #6 SUCCESS](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/44875675607); [full #1092 SUCCESS](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/44875742283) | #37–38 VirtualAtelier spawn pacing, EVS camera registrations | `docs/integration/REDSCRIPT-PASS6-AGENT-HANDOFF.md` |
| 7 | `4d3b092cd83e43d4662710d91e83ecf29d617452` | [SCC #15 SUCCESS](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/44908744794), 10 accepted ops + Chipware refusal. Earlier full #1098 tests only first six ops | #700–709 chase/sprint/StealthRunner/Lizzies/Reinforcements | `docs/integration/REDSCRIPT-PASS7-ISOLATED-HANDOFF.md` |
| 8 | `41018c3f9c1c64b0388e1266a16bd70a0151ce24` | [SCC #12 SUCCESS](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/44876279303); [full #1103 SUCCESS](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/44878479063) | #37–39 chipware cheap gates/combat cache and ImmersiveCyberware cache helpers | `docs/integration/REDSCRIPT-PASS8-AGENT6-HANDOFF.md` |
| 9 | `430d331a3892967231a895049f9e1b0a14ef3ac4` | [SCC #8 SUCCESS](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/44875735836); [full #1091 SUCCESS](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/44875735837) | #37 RunningMan double sprint/stand dormancy | `docs/integration/REDSCRIPT-PASS9-AGENT6-HANDOFF.md` |

**Source provenance:** Every audited branch is a descendant of `96d66c66642ac52dd13fd4001416572b9169bace`, diverges from newer handoff work, and modifies `src/GRedscript.Resolver/RedscriptGenerationPlan.cs`. Comparative GitHub diffs show each branch's changes *after* its most recent cited successful source-code validation are documentation-only. Its actual generator code therefore remains at the validated source state, but cross-branch composition is entirely unverified.

## Known conflict map

1. **Central stage order / same-file edits:** Handoff already owns #37–41. Passes 4/5/6/8/9 independently reused #37–39; Pass 7 uses #700–709. Reconcile calls **within the current handoff plan**, not wholesale branch copies, and retain ten generic passes first. Match stage IDs and reporting to actual dependencies.
2. **StealthRunner:** Pass 4 NPC player handle cache and Pass 7 #702 target the same weak player field/lifecycle. Produce a single source-state-aware owner implementation; retain distinct Pass 7 quest detection gate. Test partial preexisting cache.
3. **Chipware:** Pass 4 relevance filter; Pass 8 cheap gates plus combat helpers; Pass 7 refuses historical 24-perk memoization absent a proven perk/equipment/charge invalidation epoch. Compose only compatible nonduplicate parts and carry that narrow refusal; do not invent invalidation.
4. **RunningMan:** Pass 7 #703 sprint perk cache is a prerequisite/overlap for Pass 9 sprint evaluator gate; compose Pass 7 before Pass 9 and ensure Pass 9 only fills missing work. Preserve StandEvents original cadence/wrapped path.
5. **Reinforcements:** Pass 4 `ReinforcementsSystem` prepared-quad geometry and Pass 7 `ReinforcementsCall` stimulus, police/prevention, burst guard must coexist; preserve mutable geometry fallback, faction and pursuit behavior.
6. **New helper files and packaging:** Pass 8 adds two narrowly owned REDscript helper source files; integration must keep SCC combined-source admission, correct ZIP game-relative paths, dependency ABI, rollback and explicit unowned-addition rejection. Do not merge brittle harness-specific source layout assumptions.
7. **Full handoff blocker:** [GCO #1093 FAILED](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/44875750423) at `Replay real sealed GRSP capture with private REDscript source`, error `GCO_REDSCRIPT_ORDERED_GENERATOR_FILE_COUNT_DIFF` (stage effective-preview count vs independently assembled source count). Do not change code or test solely to obtain green; compare actual expected and generated source manifests and reconcile approved stages/eligibility. The [focused handoff #216 SUCCESS](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/44876759857) does not resolve the full product failure.

## Next acceptance gates

- Import only independent source-owner generators/tests into this branch. Integrate central Stage one operation at a time according to dependencies, **without corrective passes** or rewinding newer source.
- Resolve overlap based on structural behavior; verify stock, partial, complete, compatible custom, incompatible, absent and repeat behavior; refuse only genuinely unproven operations.
- Fix the real #1093 effective-source parity mismatch; do not weaken legitimate tests or hardcode arbitrary expected counts.
- Run **one focused consolidated SCC checkpoint** on the actual merged 2,331-file post-generic stack; separately run **one full GCO package/checkpoint** on the combined output, verifying true one-ZIP path and ownership sidecar.
- Real in-game parity and matched profiling captures remain a separate shipping gate. No measurable FPS/CPU improvement is established from compilation alone.
- Pass 10 has no live branch as of this audit and remains pending review/approval. No merge to `main` is authorized by this audit.
