# Real-capture CI regression inputs

The ZIP under this directory is an owner-provided real GCO collection, committed only to exercise development integration tests. It **must never be copied to the distributable GCO artifact or to a game's optimization ZIP**.

- `GCO-Capture-15112026_021913.zip`: 17,544,211 bytes; Git blob SHA-1 `1eccfd6dded883b0977d6524446d643a96899690`. This is a publicly visible repository fixture. Preserve exact collected evidence rather than silently anonymizing, adjusting, trimming or rebasing its `collection.json`.
- Existing Windows CI invokes `tests/GTotalOptimizer.Core.Tests/Verify-RealCaptureFixture.ps1` on a temporary extraction. Checks use original index hashes, CET/GRSP summary evidence, official PresentMon frame/QPC fields and unchanged original ZIP integrity.
- The ZIP records measurements, **not the installed source inventory**. It does not automatically authorize CET transformations or prove a real Resolver run is source-compatible. A separately verified, read-only source tree is required for the owner Resolver's meaningful semantic/generic decisions.
- Do not add hardcoded runtime mod lists, source hashes or user-specific paths. Tests may pin **this fixture** and enforce its integrity, but product inventory remains live/dynamic.
- Replacing this public dataset needs explicit owner agreement and revalidation of the actual collection; do not update tests merely to bypass a legitimate failure.
