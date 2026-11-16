# GRSP native proxy-attribution work — preserved GCO handoff

## 2026-11-16 — native hook crash-safety update

GRSP native-only branch `dev/native-instrumentation` now has TLS bookkeeping re-entrancy protection, pre-reserved bounded proxy counters, and a synchronized F11 STOP barrier covering both active root calls and any in-flight TLS bookkeeping before its `UnsafeCell` shard merge. Failed merges cannot seal a valid capture. GCO `main` and `dev/redscript/resolver_handoff` fetch only provenance-checked owner-native artifacts from the native-only branch. The previously archived source snapshot remains historical; do not compile or duplicate native Rust in GCO. **No verified Windows native CI, fresh F11 game evidence or proven crash fix is available yet.**


**Status:** read-only source snapshot / implementation record. **NOT compiled, linked, or owned as a new GCO native profiler.** No new native CI, generated mod files, or gameplay proof.

## Exact source preservation

- Source repository: `giorgioherma/GRSP-G-REDscript-Profiler`, historical development ref `dev/resolver-profiler-handoff`.
- Original file: `src/proxy_attribution.rs`, Git blob SHA `3807c74b255b4f6f6e44136fca9820552402b7da`.
- Preserved byte-for-byte UTF-8 source: [reference/GRSP-proxy_attribution.rs](reference/GRSP-proxy_attribution.rs).
- Native module integration in original `src/lib.rs`: `mod proxy_attribution;` plus `proxy_attribution::export(&developer_dir)?;` directly after the existing `callback_pressure::export(&developer_dir)?;` in `dump_results()`. Entire GRSP `lib.rs` deliberately **not copied** into GCO to prevent a duplicate source owner.
- GRSP commits: `f1940890d7477fc21f41c35ae0dd44fc6a81089b` (source), `a8981958871aa0818d22f10bd7d6648de8d8b473` (writer integration), `fa676e975004ba974c9735817b9e6d452a5791c0` (one-pass bounded aggregation), `96600511172fc679151c8ac4e95ea055db524bd2` (OUTPUT_SCHEMA notes).
- Native output: `Data/Developer/RSP_ProxyAttribution.csv` after complete F11 STOP. Columns: `capture_id,stable_callsite_id,source_owner,source_function,source_path,source_line,call_kind,target,target_resolution,attribution_status,outgoing_edges,outgoing_edges_per_sec,exclusive_instrumented_ms,observed_inclusive_ms,max_inclusive_ms,active_frames,max_edges_per_frame,sparse_observed_paths,sparse_observed_max_depth,sparse_observed_parent,hot_paths_dropped_global,receiver_proven,callback_entries_known,causality_proven`.

## Interpretation and limits

This is source-aware **post-capture** reporting based on existing GRSP `FUNCTIONS`, `TARGET_NAMES`, `CALLSITES` and bounded `HOT_PATHS`. No additional VM hooks or argument/receiver inspection. Exact virtual receiver remains unknown; `receiver_proven=false`, `callback_entries_known=false`, `causality_proven=false`. This is diagnostic evidence, not proof that a source optimization can be made. The full GRSP profiling and F11 lifecycle owner should continue to own any operational native code; GCO's separate managed REDscript resolver remains at `src/GRedscript.Resolver`. Do not compile this reference Rust file in GCO.

## Verification gates still outstanding

Rust native compilation/tests and independent GRSP workflow run on these changes; output from new gameplay F11 capture; owner-native artifact provenance and GCO adoption; measured overhead, receiver attribution and performance improvement. Historical green CI does not validate this snapshot.

**Reason for preservation:** retain the exact recent native experiment before cleaning obsolete managed Resolver content and branch history. Deleting the historical GRSP branch is not necessary to remove obsolete managed Resolver files; branch retirement is a separate explicit decision.
