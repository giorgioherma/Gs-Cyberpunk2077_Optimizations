# Source-owned CET native player and near/far cache: REDscript integration contract

2026-11-15. Owner-approved planning: model the CET Resolver-generated native player/location cache as a **planned component of the final combined optimization ZIP**, despite its absence from the untouched capture inventory. Physical installation and REDscript API compatibility are separate proof gates.

## Verified CET owner implementation

- CET CompiledNativeLocations.cs compiles G-CET.locations from supported **current source** coordinates rather than version-locked inventories. Its source-owned native service is GCETStaticLocations.dll, with game-relative generated location data under red4ext/plugins/G-CET-StaticLocations/.
- CET GCETNativeLocationHost.lua owns NEAR, FAR and ORIGINAL/fallback transitions. On UNKNOWN, native unavailability, callback failure, lost subscription or snapshot resync, original behavior remains available. Do not duplicate a second player location polling service.
- CET NativePlayerVectorStage.cs uses strict **on-demand live** GCETPlayerVector4Live plus GCETPlayerVector4Valid, with original getter fallback; it expressly rejects unproven phase-shared/stale Vector4 substitution.

## FlushingEtiquette candidate assessment — original and approved partial sources

- Original scripts/FlushingEtiquette/FE_Places.reds calls Toilets() and Sinks(), rebuilding static Vector4 arrays before checking player GetWorldPosition at a **1.0** radius. IsInCleanWater uses two source-literal locations at **15.0**. These are good candidates for a **generic source-proven immutable-location FAR guard**. Preserve precise author behavior for NEAR/UNKNOWN and invalid/teleport/session states.
- The hottest observed FE BaseMinimapMappinController.Update wrapper is a **different** issue. Its wrappedMethod() path dominates unresolved proxy time; the known washroom icon/tint logic is low cost. FAR cannot authorize skipping wrappedMethod or suppressing unrelated mappins.
- The approved 61-file portfolio already removes the broad minimap Update wrapper, retaining MinimapPOIMappinController.UpdateIcon, and moves washroom mappin ownership to the session. This is an **approved semantic compatibility oracle for R7**, after generic passes, not authority for an immediate global wrapper-rewrite pass.

## Required composition and safety gates

1. Model CET native player/cache capability as PLANNED_BY_CET_RESOLVER in the future combined Resolver graph. Verify the actual CET owner output contains its DLL, generated location table and runtime hosts before packaging one optimized ZIP. Do not pretend the pristine game already has it.
2. REDscript does not automatically see Lua Game.GCETStatic functions. A typed REDscript-compatible bridge and ABI/lifecycle proof must be implemented at its **native CET owner or G-REDruntime owner**, not recreated inside GCO.
3. GRSP generic source analyzer must derive literal coordinates, conservative radii, metric (XY vs 3D), source dependencies and failure behavior **from current live .reds**, not hardcoded FE ownership or old reference SHA.
4. FAR may bypass work only when it proves the author's negative result. NEAR/UNKNOWN must retain exact live checks, callbacks and fallback. Test save/reload, teleport, fast travel, player detach, missing owner native assets, drift, partial installations, repeat runs and whole-stack compilation.
5. Never rewrite or drop an unresolved proxy/wrappedMethod on proximity evidence alone. Test actual source output and measure before/after with GRSP + CET + PresentMon. No claimed savings yet.

Decision: CET owns native capability and future extension; GRSP owns REDscript generic optimization and its source proofs; GCO only stages the verified dependency and merges the ZIP. Generic passes first, semantic remainder R7 last.