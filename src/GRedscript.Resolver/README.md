# GCO-local REDscript Resolver

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
