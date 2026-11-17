# GCO-local REDscript Resolver

## Active R5/R6 plan — generic source generation

Use [the single work-elimination implementation plan](../../docs/integration/R5-WORK-ELIMINATION-OPPORTUNITIES.md) for priorities, source proof, actual `.reds` output, GRSP/SCC acceptance and remaining framework services. G-PureConstantSlice V1 is implemented in the existing `CallbackSemanticResolver` and has an isolated preview; no new parser or framework duplicate. **Verified [GCO CI #655 SUCCESS](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/44163079434)** (source + corrected runner commit [`b741284`](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/commit/b741284e5ef94b9784976f1256c662c0710eeeb2)): **125/125** managed .NET 10 Resolver safety tests, 7 player + 8 quest framework contracts; private current 2,319-source F11 replay generated **30 source-proven `PURE_FINAL_STATIC_BOOL_V1` substitutions in 13 `.reds` files** with no original writes, combined **44 external generic preview files**, official redscript **SCC 0.5.31** compiles untouched, preview and 11-file framework-composed source; GCO v1.2.5 integrated portable app/payload checks passed. The 30 direct-call substitutions in this installed stack are **concentrated in one owner** despite a source-wide generic rule: this is a proof of generated partial work, **NOT broad cross-owner adoption**, final game-relative optimization ZIP, gameplay validation or measured savings. F11 source-matched direct settings calls included **24,375 MasterEnabled / 1,886.5224 ms** and **24,375 EnableDebugLog / 977.0677 ms** exclusive; they are **observed original cost exposure**, not an A/B delta or a forecast. CI #653/#654 showed valid full-stack SCC and package checks but only **118** counted tests because the new `unittest.TestCase.run` method was accidentally shadowed; fix at source and 125 executed tests verified in #655. Next focus **cross-owner substantial G-ClosedLocalReuse / G-ChangeKeyedPartialWork**, and promote only after user gameplay/reload and matched measurement.


## 2026-11-17 — CI #639 closes the partial-subscriber callback contract

[GCO CI #639 SUCCESS](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/44111739033), code `adff7c560c768e54eedb2acb7547bc6e6813a393`: **106/106** managed .NET10 Resolver tests (including preserving `wrappedMethod` for shared-fact OnAction), **7/7** player and **8/8** quest observer source contracts; private 2,319-file real-source replay, 1,465 literal fact reads / 753 cohorts / 28 cross-owner facts / 145 narrow same-owner helper reads, official SCC 0.5.31 full-stack with 11 authoritative framework files and existing generic previews, **323/323** GCO package contracts, combined CET+GRSP+PresentMon report PASS. Portable GCO **app** artifact `run639-a1` ID `14340384025` is not a game-relative optimization ZIP. **No authorized source substitution, gameplay test or measured improvement.**


## 2026-11-17 — Bidirectional fact subscribers, mixed-method preservation (CI #638 SUCCESS)

**Read first:** [R5 partial subscriber cohorts](../../docs/integration/R5-PARTIAL-QUEST-SUBSCRIBERS.md). In GCO `dev/redscript/resolver_handoff`, the existing source-proven `scan-quest-facts` now compares shared exact fact names across both direct `GetFact` and narrowly recognized same-owner `this.GetFact(CName)` helpers, and across refresh, input and other methods with different field/helper/argument dependencies. **[CI #638 SUCCESS](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/44110788827):** 2,319 live files, **1,465** literal read sites, **753** fact-name cohorts, **28** cross-owner, **145** same-owner wrapper sites, **27** direct+wrapper cohorts, **6** lexically simple+mixed cohorts; 105 managed .NET tests, 7 player + 8 quest owner contracts, full 11-file framework SCC 0.5.31, 323 GCO package contracts. A fact may be partially shared while the original method retains UI/time/action effects. **0 fact getter replacements, 0 whole-method skips authorized, 0 gameplay speedup claims.** Framework `main` also checks exact listener-bridge identity before callbacks can mutate a newly re-acquired same-name watcher. The OnAction/wrappedMethod versus refresh mixed-consumer regression is now verified in GCO CI #639 SUCCESS (106 managed tests); do not convert source-input sharing into an automatic getter or whole-callback rewrite.


## 2026-11-17 — Quest fact full-refresh reader proof gate, CI #629 SUCCESS

[GCO CI #629 SUCCESS](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/44102461164) for code `70ffc33779ebac823237b80a59dd8b2aa4292a9e`: **102/102** managed tests, **7/7** player owner-source contracts and **7/7** quest observer contracts; full private **2,319-file** source scan found **67** direct GetFact/GetFactStr Update/Refresh/Tick family readers (**17** with sealed source-matched outgoing edge evidence, **8** with dynamic reads), **0** proven fully covered, **0** authorized source substitutions. Strict quest graph: **24,276** source methods, **2,799** literal callsites, **1,140** dynamic/unresolved, **961** conservative literal names, **1** source-typed cross-owner listener fact, **228** unresolved receiver callsites. SCC **0.5.31** successfully compiled the 11 owner framework scripts with the earlier 22 combined generic previews; **323/323** GCO installer/package contracts and CET/GRSP/PresentMon reporting passed. The portable GCO **app** artifact `run629-a1` (ID `14337756119`) is **not** an optimization ZIP. No gameplay validation or performance improvement claimed.

**Next:** source-prove the complete writer/callee/alias set for a measured generic derived-data subgraph; confirm exact engine notification delivery, save/reload and lifecycle fallback in game before a source-owned optimizer rewrite. The 67 source-name matches are **not** 67 safe skips.


## Quest-fact source graph / direct refresh-reader gate (2026-11-17)

`scan-quest-facts --game <live-root> --out <new-external-json> [--analysis <sealed-source-matched-analysis.json>]` is read-only and uses **the existing** `CallbackSemanticResolver` owner/method parser and verified F11 callsite join; no second parser, no fixed mod/fact allowlists. Graph schema `gco.redscript.quest-fact.graph.v1` includes named GetFact/SetFact/listener callsite summaries, dynamic-expression refusal and `directRefreshReaders` rows for name-family Update/Refresh/Tick methods. These rows expose literal fact names plus field/helper/argument dependencies, measured-source association and explicit `completeFactWriterCoverage=false`, `completeTransitiveDependencyClosure=false`, `safeToReplaceGetterOrSkipMethod=false`, `generationAuthorized=false`. A `RegisterListener` named method can belong to ink/UI; source-declared QuestsSystem receiver is scoped to **its own class/method**, never an unrelated class.

G-REDruntime `main` owns opt-in `QuestFactSignals` (11th .reds file); do not copy it into GCO or GRSP. [GCO #622 SUCCESS](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/44099363200) compiled the full private 2,319-file stack with 11 framework files and earlier generic previews; #626 passed stronger source type gating. **No first consumer is yet safe to generate:** original engine writers, return/event ordering and all nonfact changing dependencies are unresolved. [Full status](../../docs/integration/R5-QUEST-FACT-SIGNAL-STATUS.md).



## 2026-11-17 — Quest fact observer final source contract: CI #622 SUCCESS

- [**GCO #622 full Windows CI SUCCESS**](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/44099363200), source SHA `505c8ea1d8714167797e412f3dc7dba81056077e`: **101 managed REDscript safety tests**, **7 existing player lifecycle + 7 new quest-fact owner source/mutation contracts**, exact 24,276-method private fact graph (**2,799 literal / 1,140 dynamic**, 961 distinct names, three cross-owner source-typed fact-listener matches and 203 unknown listener receivers), official SCC 0.5.31 `framework-all-generics-preview` exit **0** with **2,319 current private .reds files, 22 generic preview files and 11 authoritative G-REDruntime source files**; **323 GCO package/installer tests**, combined CET/GRSP/PresentMon report PASS. App artifact `Gs-Cyberpunk2077_Optimizer-v1.2.5-run622-a1` ID **14335662786**, not game-relative optimization ZIP.
- Final owner-source candidate has **one registration per watched fact, unique consumer release tokens, bounded token/watch storage, UNKNOWN on player session transitions, exact event versioning (even repeated values), one `QUEST_FACT/SESSION_INVALIDATED` notification after safe watch traversal, existing EventBus/DirtyFlags, and original live GetFact fallback**. Source replay/SCC and mutation tests are successful; **game load/reentrant callback delivery/feature regression/CPU savings are NOT tested or established**. Zero original installed mods are redirected or rewritten. The requested 16-category shared observation model remains staged; no broad automatic event/position/time service has been enabled.


## Quest fact read/write/listener graph and optional shared observer (2026-11-17)

`scan-quest-facts --game <live-root> --out <new-external-json> [--analysis <sealed-GRSP-Resolver-analysis>]` is a source-wide read-only operation built on the **existing** `CallbackSemanticResolver` method parser and source hash. Schema `gco.redscript.quest-fact.graph.v1` lists `GetFact/GetFactStr/SetFact/SetFactStr/RegisterListener/UnregisterListener` lexical callsites, exact `n"fact"` names versus dynamic/unknown, source-declared `QuestsSystem` registration receivers versus unknown UI/engine registration receivers, and only source-aligned sealed-F11 outgoing work. Every fact row refuses generation (`sharedSubscriptionAuthorized=false`, `everyWriterAndAliasProven=false`, `receiverTypeProven=false`). No method/capture/source is written or auto-optimized.

[GCO #615 SUCCESS](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/44096705852): 2,319 private original `.reds`, 24,276 source methods, 2,799 literal + 1,140 dynamic fact-like callsites, 961 candidate literal fact names, 203 source-unproven listener receiver calls. Framework owner `main` now owns a **dormant opt-in** `QuestFactSignals` service backed by original QuestsSystem notifications and existing G-REDruntime EventBus/DirtyFlags, with interest tokens and unknown-state fallback. GCO #620 SCC-compiled 11 owner framework source files against the complete 2,319-file private stack, with no mod integration. [Full R5 status](../../docs/integration/R5-QUEST-FACT-SHARED-OBSERVATION-STATUS.md).



## Direct dependency/trigger inventory (2026-11-17)

The read-only `scan-shared-work` CLI now also emits `directDependencyCategoryCounts`, `candidateSharedTriggerCounts`, `primaryCandidateSharedTriggerCounts` and **separate measured/unmeasured** category totals. Every method has `directDependencies.instanceFields`, `instanceMethodCalls`, `usedCallbackParameters`, overlapping lexical `observedCategoryHints`, `possibleSharedTriggers`, and `primaryCandidateSharedTrigger`. All methods explicitly declare `completeCallGraphProven=false`, `completeWriterGraphProven=false`, `sharedTriggerProven=false` and `generationAuthorized=false`. The same authoritative REDscript token/method parser and sealed F11 source verification are reused without any second parser or postprocessor. No mod inventory is hardcoded.

[GCO CI #607 SUCCESS](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/44089551952): **97** managed tests, **1,200** source-name methods in 2,319 files, **187 measured + 1,013 without matched capture evidence**, 902 `this.field`, 829 `this.Method()`, 431 used parameters; official SCC 0.5.31 with all 10 owner G-REDruntime sources, 323 GCO installer tests. This is a source-dependency *inventory*, not safe shared-trigger admission or an optimizer output. [R5 full exact accounting](../../docs/integration/R5-SHARED-DEPENDENCY-TRIGGER-STATUS.md).



## Shared-work and dirty refresh source scan (2026-11-17)

Use `dotnet GRedscript.Resolver.dll scan-shared-work --game <live-game-root> --out <external-private-json> [--analysis <verified-GRSP-Resolver-json>]` for read-only whole-stack discovery. This uses the **same `CallbackSemanticResolver` parser, source index and exact measured F11 callsite join** as the existing `resolve-callbacks` CLI. A method-name match is a discovery hint, **not proof of a callback entry, complete immutable inputs, closed writer set, safe UI skip, or CPU saving**. Original sources, capture and installed game are never written.

JSON schema `gco.redscript.shared-work.scan.v1` records direct measured outgoing edges, optional direct exclusive observations and explicit blocker reasons; `callbackEntries` and `expectedSavingsMs` remain null, `generationAuthorized=false`. Source-only use is permitted but never reported as measured. Output under game, conflicting output or changed source SHA refuse. [GCO #598](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/44082314052) verified **94 managed tests**, **1,200** source-family methods/187 measured matches in the actual 2,319-file private stack, full SCC and 323 integration tests. This is still a classifier, not a REDscript optimization pass. See `docs/integration/R5-SHARED-WORK-EVIDENCE-STATUS.md`.



## Generic pass 3: ANGLE_SORT_INVARIANT_V1 (2026-11-17)

The new `scan-angle-sort --game <current-root> --out <external-json>`
and source-matched `preview-angle-sort --game <current-root> --analysis
<verified-sealed-capture.json> --out <empty-external-directory>` are
independent, nondeployable generic geometry optimizations. Admission requires
the exact pure four-vertex `Vector2` angle-sort loop structure, unchanged
comparison/swap order and source fingerprint. Reuse the computed `angleI`
within the inner loop and refresh it from already-computed `angleJ` on swap,
reducing `AtanF` evaluations from 12 to 9 per invocation of that pattern,
not promising a measured FPS/CPU improvement. The rule is not keyed to a
mod name and refuses missing/drifted capture proof, partial generated state,
unsupported variants and writes into the game folder. The original
2,319-file historical source stack has a measured-hot candidate in
ReinforcementsSystem's angle-sort helper; whole-stack official SCC
replay passed in GCO CI #567. Gameplay validation is still required.

GCO's canonical Windows CI additionally checks out the independent
`G-REDruntime_Framework/main` source and SCC-compiles it in an isolated
fourth scenario (CI #562 SUCCESS), then tests combined runtime + all
generic overlays in a fifth isolated scenario (commit `ec87f3d`,
outcome pending). The framework source stays in its owner repository.

Relocated **unchanged in behavior** from GRSP development branch commit `1da43d9a7197ad4fd1c2debc004a2f3ee31b9dab`
into this independent internal .NET 10 component, following the 2026-11-16
owner decision. The original GRSP version and 41 contract tests were accepted
by GRSP CI #61, and the 2,319-file external compiler baseline by GCO #482.

- Managed evidence reader, source audit and scanner, narrow CName preview and
  native SCC 0.5.31 compiler runner live **here in GCO**.
- GRSP retains all native profiling, F11 capture, measurement outputs and
  install/restore; no GRSP profiler sources or Rust/native packages were copied.
- CET remains the owner of the Lua Resolver. One GCO WPF UI and one canonical
  GCO Windows CI.
- Preserve original `GRedscript.Resolver` assembly/JSON schemas and CLI
  to avoid breaking tested capture/source tooling during the ownership move.
- Authoritative generator safety rules: read-only live source; captured
  evidence must match source; reject drift, missing dependencies and unsupported
  structural shapes; no automatic game writes. Compilation is not gameplay
  validation or a claim of measured performance improvement.
- The source-owner GRSP CLI remains a historical migration fixture; only
  the GCO-local component is used for new active development after parity.

See `docs/integration/REDSCRIPT-RESOLVER-LOCAL-HANDOFF.md`.

## Composable blackboard definition preview (2026-11-17)

The existing `preview-blackboard-defs --game <root> --analysis <verified.json> --out <isolated-preview>` accepts optional `--base-preview <earlier-generic-preview-root>`. It applies **the same authoritative statement-local source matcher** against the earlier source-owned staged effective REDscript text, only when the original admitted candidate statements and group identity are unchanged. Missing/changed target statements, unverified capture/source, preview directory reuse or game-root staging fail closed. Preview writes only the new external overlay containing both sets of edits; it does not patch the installed source or issue deployment approval.

[CI #587](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/44068339695) validated 89 safety tests and compiled the real 2,319-file stack with 22 eligible definition statements, 16 files (14 independent + 2 composed; zero deferred) and 10 owner G-REDruntime files via SCC 0.5.31. This covers the two observed same-file collisions; automatic six-pass production generation, game behavior and performance gains remain unverified.



## R5 native proxy evidence interpretation

The existing `analyze --capture ... --game ... --out ...` command now consumes
the **optional source-owned** `G-REDscript/Data/Developer/RSP_ProxyAttribution.csv`
after normal sealed-F11 status and source-stack checks. The resulting JSON
`proxyAttribution` section reports `NOT_CAPTURED` for legacy captures, or
`CAPTURED_UNRESOLVED` with source/callsite-linked proxy rows for new captures.
Native CSV observations are reconciled with `RSP_CallSites.csv` by original
capture ID, stable callsite ID, owner, source function/line/path, invocation
kind/target/resolution and observed edge count. Contradictions, malformed
numbers or invented receiver/causality claims fail closed.

Interpretation is intentionally **not** new native instrumentation: Rust hooks,
actual virtual receivers, callback-entry counting and in-game evidence remain
owned by GRSP. This GCO implementation makes existing bounded native evidence
useful for source-wide hotspot diagnosis without copying profiler hooks,
changing game source, creating a second report pipeline or authorizing output.
Neither inclusive time nor CET/GRSP domains may be summed as overall CPU cost.

## CET-inspired generic callback phase (2026-11-16)

This is the **first executable source-local generic callback rule**, not a
REDscript clone of CET's 0-Engine dispatcher. It keeps original REDscript
callback registration and invocation unchanged. Source-wide discovery is
a separate opt-in CLI stage:

- `scan-callbacks --game <current-game-root> --out <external-json>`:
  enumerate current source into callback families (`ONACTION`, `ONUPDATE`,
  `ONDRAW`, `ONTICK`, other `ON_EVENT`), observed syntactic properties, and
  strict source matches for `CNAME_FIXED_INDEX_V1`. Output contains private
  mod paths and stays local/private; stdout is aggregate-only. This scan
  does not admit any callback routing or frequency change.
- `preview-callback-index --game <current-game-root> --analysis <sealed-Resolver-analysis.json> --out <empty-external-directory>`:
  stage a **nondeployable** generic REDscript preview only when existing
  verified analysis and current full-source fingerprint agree. Original
  files are never written. The real whole-stack SCC harness is the separate
  compiler validation step.

**First admissible mechanical shape:** a static `CName` → `Int32` lookup
method with one exact local loop over a same-class, zero-argument static
helper returning a pure literal `array<CName>`. Replace the loop with
ordered direct `Equals(literal, name)` tests returning the same first-match
indices and `-1` for a miss. The helper and caller remain unchanged.
Unknown loop bodies, dynamic arrays, nonliteral selectors, source drift or
altered generated markers refuse the rewrite. Source shape and
helper semantics—not a mod/folder name—are the rule.

**Status and limits:** the archived private 2,319-file stack gave 3,545
callback-family source methods and **one** strict fixed-index match.
This is a synthetic/real-source admission milestone, **not** a throughput
claim. Existing targeted examples include input action handling, but the
rule does **not** claim that arbitrary REDscript `OnAction` or `OnUpdate`
callbacks can yet be rerouted, deduplicated or slept. CET's callback
classification/side-effect/lifecycle admission is being adapted to
REDscript syntax incrementally. Full-stack SCC, tests, gameplay and
new captured performance are independent gates.

See `docs/integration/REDSCRIPT-RESOLVER-AGREED-OPTIMIZATIONS.md` and
`docs/integration/R5-HIGH-IMPACT-EVIDENCE-PLAN.md`. Do not port CET Lua
rewrites by regex or change unrelated GCO installer/Restore functionality.


## Generic pass 2: ACTION_LITERAL_MEMBERSHIP_V1 (2026-11-16)

**Owner:** GCO-local `GenericActionFilterPass.cs`. This is a literal-array
construction/membership optimization *inside existing `OnAction` methods*,
**not** CET `Observe` routing, callback cadence modification, nor early
filtering of arbitrary downstream handlers.

A source-only diagnostic `scan-action-filter --game <game> --out <external-json>`
enumerates current callbacks and classifies exact-match source shapes.
The separately gated `preview-action-filter --game <game> --analysis
<source-matched-sealed-capture.json> --out <empty-external-folder>` produces
an isolated **nondeployable** `r6/scripts/...` overlay. Only the same
`grsp.resolver.input.v1` verified captured-collection identity and complete
current source-stack SHA are allowed. No live source writes occur.

**Precisely admitted source shape:**

- A standard `OnAction(action: ListenerAction, consumer:
  ListenerActionConsumer) -> Bool` starts with `let actionName =
  ListenerAction.GetName(action);`, then `let names = Class.Helper();`,
  then `if !ArrayContains(names, actionName) { return false; }`.
- The called `Class.Helper()` has **one** unambiguous source definition,
  a static zero-argument `-> array<CName>` **expression body containing only
  two to sixteen literal CNames**; no dynamic input, calls, writes or runtime
  array lifecycle are admitted.
- The caller's source explicitly imports that exact class from its module or
  is in the same module; ambiguous/missing imports and alternate same-name
  provider definitions are refused.
- No matching `@wrapMethod`, `@replaceMethod`, or `@addMethod` alters the
  provider method. A partial generated marker must fail; a complete marker
  must prove *literal bytes as well as lexer tokens* before claiming
  `ALREADY_COMPLETE`.

The only rewritten source region is the local temporary array getter call and
its `ArrayContains` guard. It becomes ordered
`!Equals(actionName, n"X") && !Equals(actionName, n"Y") ... { return false; }`.
This preserves the original name read, return-false behavior for unrelated
actions, callback registration, consumer, all game actions and remaining
method bytes. The helper definition and every other caller remain intact.

**Cross-mod applicability:** the dated private installed source contains
multiple input listeners using this structural pattern. Source name/path is
NEVER a match condition. Real stack admission requires current CI source
proof and full effective SCC. This does not measure avoided allocations,
callback entries or FPS savings. It is *not* the proposed finite-action early
bypass of CET/Lua bridges, widget updates or game-system getter side effects;
those still need independent evidence and gameplay tests.

The existing first `CNAME_FIXED_INDEX_V1` generic remains independently
gated. The real-stack replay stages **disjoint** overlays for both generic
rules in the existing GCO-owned SCC 0.5.31 harness, never installing either.
