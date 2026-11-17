# Shared CET native coordinator + REDscript consumers — development contract

Date: 2026-11-18. Owner-approved direction. GCO `dev/redscript/resolver_handoff` is the one managed REDscript optimizer alongside the CET owner Resolver. Framework owns runtime services; CET owns the C++ DLL. One .NET 10 application and one eventual output ZIP.

## Runtime architecture

```text
CET Resolver ----------\
                        +--> GCO shared native staging --> ONE GCETStaticLocations.dll
REDscript Resolver ----/                            \--> ONE merged G-CET.locations
                |                                  (owner-separated GCAS v1)
                +--> GCO REDscript generated RTTI declarations
                +--> source-matched REDscript consumer adaptations
                      |                  |
                      +--> G-REDruntime StateCache / QuestFactSignals / InputHub
                      +--> native player/location state where appropriate
```

**Dispatch invariant:** Either Resolver can request the native component without waiting for the other. The source DLL is always the CET-owned `runtime/native/GCETStaticLocations.dll` shipped in GCO's CET Resolver component. Existing staged DLL is left intact after minimal API-family checks, never rebuilt or copied from the live game. The GCAS table is merged by owner; byte-identical owner records are skipped, changed/colliding owner records refuse. Missing table starts as canonical GCAS v1. Output must be external to the supplied live game.

`stage-shared-native --game <root> --cet-runtime <bundled CET Resolver root> --out <external shared overlay> [--red-locations <GCAS file>]`

No ZIP or profiler files are produced. The REDscript Resolver can create its own inert GCAS sites from current installed literal `SquareArea(new Vector2(...), ...)` source: four static vertices, XY centroids plus conservative containing radii, source-path-derived owner IDs, comments excluded. These are **inventory only**, not authorization to suppress polygon checks. Owner/source drift or mutable/dynamic inputs still require consumer-specific admission. No hardcoded mod names, source SHA allowlists or version locks.

## REDscript native interface

Conditional output path: `r6/scripts/G-RedRuntime/Native/GCOSharedNativeBridge.reds`.

- Declares CET's existing native `ScriptGameInstance` functions using `@addMethod(GameInstance)`, not another RED4ext plugin or player observer.
- `GCOSharedNativeBridge.LocationStatus(owner,index)`: **-1 UNKNOWN, 0 FAR, 1 NEAR**. The native coordinator must report ready with state 2 and a valid compiled owner/anchor index. **Never** treat UNKNOWN as FAR. A consumer must retain exact live author behavior for NEAR and UNKNOWN.
- `PlayerPositionLive()` + immediately following `PlayerPositionValid()`: strictly live typed Vector4, never assume the CET frame snapshot is fresh enough for arbitrary REDscript callback phases.
- No implicit timer, event bridge, player poll, dispatcher, subscription, teardown or gameplay callback is registered by this source alone. Native subscribed events will use the existing G-REDruntime scheduler when an actually compatible consumer is admitted, not a new timer per mod.
- Exact REDscript RTTI signature/return mapping is **not yet accepted by live gameplay**. The declarations and real DLL can be compiled together in headless SCC; this does not prove a runtime bridge call works.

## First actual G-REDruntime shared-state adoption

G-REDruntime `StateCache.reds` now exposes `SharedProviders.Player(game)`, `Quests(game)`, `Delay(game)` as thin entrypoints. They delegate to **existing** `Runtime` and `StateCache`, not independent caches.

GCO's **SHARED_PLAYER_SIGNAL_V1** source generator admits only direct `GameInstance.GetPlayerSystem(game).GetLocalPlayerMainGameObject() as PlayerPuppet` with one identifier argument and an ordinary module header. Generated source imports `GRedRuntime.*` and calls `SharedProviders.Player(game)`. The framework returns the event-backed attached player only while authoritative; UNKNOWN uses the original live player getter. Original callbacks and mod-owned gameplay state remain unchanged. No mod allowlist, listener detachment or added per-frame observer.

Other framework services already exist: `QuestFactSignals`, `InputHub`, `Scheduler`, `DirtyFlags`. Only the **player** source consumer is implemented in this checkpoint; general quest/input/location source substitutions remain pending, and can be introduced individually without changing earlier geometry/action passes.

## Isolated checkpoints and remaining risk

1. Base A/E generic geometry/action passes: GCO CI #677 success (source/SCC only). No player native changes.
2. Native dispatcher, merge and no-duplicate contracts: GCO CI #680 success. No gameplay changes.
3. Shared player provider source-adoption plus exact ordered-plan composition: in development; SCC and private full-source tests required before this checkpoint is described as passed.
4. REDscript native RTTI bridge on official SCC and real C++ DLL fixture: separate compilation checkpoint; runtime position/NEAR/FAR still requires game test.
5. Later one owner source-tested quest or input subscriber at a time. Keep pass IDs and output records so regressions can be attributed to a specific stage.

Game test symptoms when enabled: shared-player stale after save/load, attaching/detaching, vehicle transitions or teleport; native FAR false-negatives near a static area; input consumption/order mismatches after InputHub adoption; missed quest/UI refresh after quest listener adoption. Neither native generation nor player service integration is part of a production game ZIP yet.

**Do not silently deploy generated mods or native DLL into the live installation.** GCO generation will output one game-relative overlay and recommend Vortex deployment after integration and gameplay review.
