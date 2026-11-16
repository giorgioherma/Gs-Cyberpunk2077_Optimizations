# GCO-local REDscript Resolver

## GCO main integration — 2026-11-16

This component is **now included in the single GCO `main` portable application artifact**, at `components/redscript-resolver/app/GRedscript.Resolver.dll` and `GRedscript.Resolver.exe`, sharing the one bundled .NET 10 runtime with the WPF app and other components. The original GCO `build-windows.yml` builds, runs the inherited CLI/safety tests, publishes this component, records file hashes in `components/manifest.json` and verifies the final package.

This is internal **read-only analysis / conservative source preview** infrastructure. The existing Page 2 ANALYZE/GENERATE lifecycle remains unchanged: **do not claim that automatic combined generation is ready or deployable**. The native GRSP profiler is not implemented here; GCO imports an independently verified native DLL from the source-owner `dev/native-instrumentation` branch. The tested component was copied byte-for-byte from `dev/redscript/resolver_handoff` before this packaging change; further managed development stays source-owned by GCO.

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

See GCO `docs/product/CURRENT-DECISIONS.md` and the development branch's `docs/integration/REDSCRIPT-RESOLVER-LOCAL-HANDOFF.md` for the detailed R0–R10 work ledger.

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
