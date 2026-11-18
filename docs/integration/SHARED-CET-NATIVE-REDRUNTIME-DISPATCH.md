# Shared CET native coordinator + REDscript consumers — development contract

## 2026-11-18 native subscriber source-lifecycle correction (CI #758 PASSED)

The original `GCO_NativeLocation_<owner>.reds` wrapper-heavy design failed official SCC even with the original proximity predicate restored (GCO #752, `native-adapter-without-predicate` phase exit 3). No precise private compiler code was public. The GCO generator now emits a **pure listener companion**, while direct source replacements put the listener reference, subscription tokens, `GCOMayBeNear`, registration and cleanup **inside the existing source-defined ScriptableSystem class**, with injected Start/Stop calls. Original Start/Stop implementation, poll loop, notifications and interaction checks remain; only authoritative FAR skips the exact repeated distance expression. G-REDruntime `NativeLocationSignals` remains the single coordinator consumer. No mod lists/semantic recipes, no duplicate framework, no game ZIP. [GCO CI #758 FULL SUCCESS](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/44227121050) now validates full native-enabled SCC 0.5.31 (original/optimized, framework, native-listener-only and combined). 167 .NET10 safety tests, 9 ordered rules, 148 source edits/59 staged files, 323 package contracts. Original live files unaffected; no native gameplay validation or game ZIP.


Date: 2026-11-18. Owner-approved direction. GCO `dev/redscript/resolver_handoff` is the one managed REDscript optimizer alongside the CET owner Resolver. Framework owns runtime services; CET owns the C++ DLL. One .NET 10 application and one eventual output ZIP.

## Runtime architecture

```text
CET Resolver ----------\
                        +--> GCO shared native staging --> ONE GCETStaticLocations.dll
REDscript Resolver ----/                            \--> ONE merged G-CET.locations
                |                                  (owner-separated GCAS v1)
                +--> G-REDruntime owner optional native adapter
                +--> source-matched REDscript consumer adaptations
                      |                  |
                      +--> G-REDruntime StateCache / QuestFactSignals / InputHub
                      +--> native player/location state where appropriate
```

**Dispatch invariant:** Either Resolver can request the native component without waiting for the other. The source DLL is always the CET-owned `runtime/native/GCETStaticLocations.dll` shipped in GCO's CET Resolver component. Existing staged DLL is left intact after minimal API-family checks, never rebuilt or copied from the live game. The GCAS table is merged by owner; byte-identical owner records are skipped, changed/colliding owner records refuse. Missing table starts as canonical GCAS v1. Output must be external to the supplied live game.

`stage-shared-native --game <root> --cet-runtime <bundled CET Resolver root> --framework <G-REDruntime source root> --out <external shared overlay> [--red-locations <GCAS file>]`

No ZIP or profiler files are produced. The REDscript Resolver can create its own inert GCAS sites from current installed literal `SquareArea(new Vector2(...), ...)` source: four static vertices, XY centroids plus conservative containing radii, source-path-derived owner IDs, comments excluded. These are **inventory only**, not authorization to suppress polygon checks. Owner/source drift or mutable/dynamic inputs still require consumer-specific admission. No hardcoded mod names, source SHA allowlists or version locks.

## REDscript native interface — framework-owned backend

**Owner implementation:** `giorgioherma/G-REDruntime_Framework/main`.

- **Always-on framework:** `r6/scripts/G-RedRuntime/Location/NativeLocationSignals.reds` provides a dormant, opt-in subscription service with one native stream consumer and shared scheduler job only while subscribers exist. No native references are linked into the non-native framework code.
- **Native-enabled optional backend:** `optional-native/r6/scripts/G-RedRuntime/Native/GCETNativeLocationBackend.reds` contains source-owned RTTI declarations and the backend implementing `NativeLocationBackend`. GCO copies the authoritative file **only alongside CET's DLL** to `r6/scripts/G-RedRuntime/Native/GCETNativeLocationBackend.reds`. GCO must not invent a second declaration template.
- **Consumer contract:** source-generated mod listeners extend `NativeLocationListener`; `SubscribeLocation(owner,index,expectedCount,listener,backend)` returns a lifecycle-owned subscription token, and `UnsubscribeLocation(token)` releases it. Values remain -1 UNKNOWN, 0 FAR, 1 NEAR. UNKNOWN and failed subscription preserve original mod checks.
- C++ remains the sole player-position, NEAR/FAR and native ring-buffer producer; G-REDruntime drains its events at 0.10s through a **single opt-in job**. It does not poll the player independently. Native subscribers are still pending source-generated adoption, so no location-based gameplay behavior is changed yet.
- Headless SCC source compilation is a distinct checkpoint from live RTTI ABI/load-order and gameplay acceptance.

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

## First actual source-generated per-mod subscriptions (2026-11-18 experimental)

GCO now owns `GenericNativeLocationSubscriberPass`. It detects *source structure*, not mod names: static literal XYZ sphere factory; same-module ScriptableSystem; Start/Stop; OnProximityCheck; direct source-proven `GetPlayer()` receiver; per-site distance/radius loop. It generates an additive `GCO_NativeLocation_<owner>.reds` next to the original mod, containing `@addField` token/listener state, `@addMethod` register/cleanup and `@wrapMethod` Start/Stop calls. It never modifies original mod files or timer/effect logic in this first stage.

The generated `NativeLocationListener` retains -1 UNKNOWN / 0 FAR / 1 NEAR observations for **testing**, but has no authority to skip the original distance check. Generated code only exists in a **separate native-enabled compiler preview** until the actual source stack, framework, and RTTI backend compile together. Full/native generation is still experimental and **not** part of the production ZIP. `scan-native-location-subscribers` and `preview-native-location-subscribers` have source SHA verification, duplicate file ownership checks, idempotence for exact generated files and collision refusal for changed output.

**Regression isolation:** First validate generated source/SCC with all original mod mechanics untouched; test native callback NEAR/FAR and UNKNOWN with player attach/detach and fast travel; only then add an explicitly separate pass ID to suppress redundant far-away work. Never use player NEAR/FAR for ReinforcementsSystem's NPC-coordinate quad checks.

## Generic native FAR predicate replacement (2026-11-18)

The initial observer-only checkpoint now has an **actual source-generated work bypass**. The implementation reuses `GenericNativeLocationSubscriberPass` and the same `G-REDruntime NativeLocationSignals` subscriber; it does not create a semantic DrugDealer override. The GCO ordered REDscript plan runs the new `native-player-location-predicate` rule **after** the existing eight families.

For a strict source-typed positive test, the generator keeps the original `for site in Factory()` and author callback but prefixes the distance predicate with the adapter's cheap `GCOMayBeNear(ownerIndex)`. The generated caller increments a local stable index for each iteration, including no-hit sites. The runtime adapter treats -1 UNKNOWN or out-of-range indices as **maybe near**, so the original distance condition executes; native FAR is the only value that bypasses it. Dynamic entries appended after the proven literal prefix are out of range and remain original. The author source is emitted to the **same original game-relative path** in a staging preview. Separate new `GCO_NativeLocation_<owner>.reds` still owns native subscriptions/cleanup.

Admission is generic structure, not folder name: source-proven GetPlayer receiver and typed Vector4 distance, ScriptableSystem lifecycle, stable literal-prefix factory and exact typed location/radius fields, one matching loop, no mutations/reorders of prefix. The source compiler computes matching GCAS owners from the installed `.reds` files; no hardcoded coordinate or hash/version allowlists. Refused shapes retain original source. The new path must compose with earlier staged generic rules and produce equivalent bytes when run independently.

**Caution:** This experimentally skips a cheap distance call, not the original callback, `LabLocations` array construction or the original 0.25s scheduler. Native state has a 0.10s event drain, so stale FAR after rapid player movement, teleport and load ordering still requires a game test. This remains a preview; SCC success is not equivalent to native callback correctness or a measured performance return. Separate subsequent optimization for whole-array allocation suppression must not assume all sites are static when a dynamic tail exists.
