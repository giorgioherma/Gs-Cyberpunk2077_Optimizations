# R5 — shared player lifecycle signal: implemented source and verified boundaries

**Date:** 2026-11-17. **State:** framework-owner implementation, source contracts and official real-stack SCC **VERIFIED**; gameplay, performance and any generated consumer adapter **NOT VERIFIED**. All installed mods remain eligible for generic discovery. The seven reviewed mods are deferred only for any *mod-specific semantic remainder*. Preserve GCO `main` and the original two-page single-bundle workflow.

## Source owner and reason

The authoritative G-REDruntime `main` source already had a weak player handle in `StateCache`, `Runtime.OnPlayerAttach`, `DirtyFlags`, `ContextService`, `InputHub` and `EventBus`. The runtime lacked a matching authoritative player **detach** path; converting that player handle into cross-callback state without a detach signal could publish a stale player after transitions/reload. The uploaded 2,319-script REDscript tree contains independent `PlayerAttachRequest` and `PlayerDetachRequest` ScriptableSystem patterns. This work supplies a lifetime boundary, **not** a new timer/poll or player-state semantic cache.

Authoritative source paths:
- Framework `r6/scripts/G-RedRuntime/Core/Runtime.reds`: `OnPlayerDetach` ignores a delayed detach belonging to a different currently attached player; otherwise invalidates player state and context, stops opt-in InputHub listeners/device polling, marks `PLAYER` dirty and conditionally publishes `PLAYER / DETACHED`. Existing attach path continues to publish `ATTACHED`.
- Framework `r6/scripts/G-RedRuntime/State/StateCache.reds`: `HasAuthoritativePlayer()`, `GetAttachedPlayer()`, `GetPlayerSignalGeneration()`. Attach sets known weak ref, detach sets UNKNOWN and increments a **runtime-local** generation; shutdown also invalidates. The existing `GetPlayer()` retains an uncached original `PlayerSystem.GetLocalPlayerMainGameObject` fallback on UNKNOWN, but never adopts that lookup into the shared authoritative handle.
- Framework `r6/scripts/G-RedRuntime/Input/InputHub.reds`: `OnPlayerUnavailable()` detaches all engine listener bridges and stops device polling while retaining source subscriptions. `RefreshRegistration` and `PublishDeviceState` refuse to rearm during detach. Only authoritative attach clears that gate.

Framework contract: [`documentation/PLAYER_STATE_SIGNAL_CONTRACT.md`](https://github.com/giorgioherma/G-REDruntime_Framework/blob/main/documentation/PLAYER_STATE_SIGNAL_CONTRACT.md). Framework remains v0.6.0-pass6; no new standalone framework runtime/service or requirement for all mods to integrate.

## Safety and compatibility

- Known player is **the current weak reference provided by attach**, not any player found by an unrelated getter. Generation describes lifetime within one `Runtime`, not a save ID, monotonic global engine epoch or a permission to cache mutable game values.
- **UNKNOWN must fail open through the original source getter where required.** A subscriber must check framework readiness and known state; never disable original callbacks solely because a shared signal is unavailable.
- Original `GetPlayer()` fallback remains intentionally uncached after detach. InputHub must **not** re-register through that fallback even if the engine still returns the old player during transitions.
- `ContextService`'s 50ms combat/vehicle reads are still lazy and **not exact event-backed live state**. No assertions about combat, mounted vehicle, world position, UI animation, district/menu, NEAR/FAR native ABI, or other mutable state.
- No extra every-frame observer, native hook, additional scheduler, new registration loop, duplicate implementation, mod-owned source rewrite or deployment output.

## Real source, tests and CI

- GCO branch `dev/redscript/resolver_handoff` uses the **existing** `tests/GTotalOptimizer.Core.Tests/Verify-RealGrspResolverReplay.ps1` to validate framework owner checkout, not a copied source stub. Added `test_framework_player_signal.py` with **seven** owner-source/negative mutation contracts: actual lifecycle, absent listener cleanup, stale detach owner, missing generation, untrusted shared re-adoption, removed live fallback, and accidental InputHub re-registration.
- **[GCO CI #593 SUCCESS](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/44073412408)** on source `6ea38042343f4a508263be792ccdecbfbdc686ed`: **89/89** managed .NET10 Resolver tests; **7/7** framework player-signal source contracts; real sealed source replay over **2,319 unchanged private REDscript files**, **22 generic callback/blackboard preview files** and all **10 framework owner files** compiled by the **official SCC 0.5.31** (framework-all-generics-preview exit 0); **323/323** GCO packaging/installer contracts; combined native CET/GRSP/PresentMon reporting passed. GCO app artifact `Gs-Cyberpunk2077_Optimizer-v1.2.5-run593-a1`, ID **14323554476** (not optimized mod overlay).
- Previous #590 and #592 negative-contract failures were **test assertion defects**, corrected at source before #593. Do not turn conditional/static checks or successful SCC into game-load/behavior or speedup proof.
- Separate prior #587 blackboard same-file composition remains intact: **22 eligible statements, 16 files, 14 disjoint + 2 safely composed, 0 deferred**. No installed game files, original captured data, stable GCO main, CET or GRSP owner code changed.

## F11 pressure and remaining work

The genuine 384.517s F11 capture reported outgoing `GetPlayer` edges approximately **228,313 / 359.4ms exclusive**, `GetQuestsSystem` **633,161 / 301.3ms**. These are broad **direct-cost exposures, not eliminated calls**, and their savings are likely smaller than high-cost UI/geometry/recurring callback-body work. This lifecycle producer changes no existing mod consumers and claims **zero measured savings**.

Still required: (1) user-controlled in-game load/save/reload/player replacement and InputHub input-consumption regression; (2) source-wide measured consumer matching of receiver, identity, action/side-effect and complete invalidation before any `G-STATE-SIGNALS` generic adapter; (3) framework-owner event backed combat/vehicle/menu/district **only if** native REDscript producers with exact value/version/UNKNOWN/lifecycle can be proven; (4) dirty UI/observer interest and optional typed REDscript NEAR/FAR, each with unchanged-value/cleanup/fallback proof; (5) real before/after F11/PresentMon, source-state/idempotence, game-relative ZIP + rollback before production.

**Next implementation decision:** prioritize a read-only, source-wide joined inventory of *high-cost work conditioned on shared player lifecycle*, including clear refusal reasons `LIFETIME_UNKNOWN`, `SIDE_EFFECT_UNPROVEN`, `MUTATOR_INCOMPLETE`, `RETURN_CONSUMPTION_UNPROVEN`. Do not produce a low-return `GetPlayer` global cache pass simply to increase pass count; no speculative location or broad scheduler migration.
