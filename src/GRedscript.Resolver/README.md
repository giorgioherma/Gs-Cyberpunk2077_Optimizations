# GCO-local REDscript Resolver

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
