# REDscript Phase 2 — Dark Future workload-first semantic audit

**Checkpoint:** 2026-11-20. **Status: reviewed sources, measured capture, and four historical pass revisions; no optimizer implementation.** Companion to [Phase 2 portfolio](REDSCRIPT-PHASE2-SEMANTIC-PLAN.md) and the [agreed candidate document](REDSCRIPT-RESOLVER-AGREED-OPTIMIZATIONS.md). This focused investigation changes the initial implementation priority **within Dark Future**; it does not imply that an entire tested historical pass can be copied into a different source version.

## 1. Provenance, versions, generic state

- **Current post-generic fixture:** user's `scriptsliveAFTERgeneric.zip`, `scripts/Dark Future/` — **43** `.reds` source files. Original-game-relative directory is `r6/scripts/Dark Future/`.
- **GitHub source cross-check:** `giorgioherma/original_cet_source/main/base_after_generic/r6/scripts/Dark Future/UI/DFBaseGameUIOverrides.reds` (Git blob `1b3f4c89ef98709b3399a2705f0004cccbbcfc4c`) is text-identical to the ZIP's `DFBaseGameUIOverrides.reds` after CRLF→LF normalization (ZIP 68,140 bytes; normalized 66,640 bytes, 1,501 lines). No `GCO-GENERIC` marker in this hot source file.
- **Generic overlap elsewhere:** `Gameplay/DFVehicleSleepSystem.reds` and `Services/DFNotificationService.reds` contain already-applied `BLACKBOARD_DEFINITION_STATEMENT_V1` / `BLACKBOARD_DEFINITION_SEQUENCE_V1`. Keep them unchanged. The current ten generic rules remain authoritative; source-only semantic work must compose *after* them.
- **Measurement:** user's sealed `GCO-Capture-16112026_132224.zip`, ~**384.517 s**, GRSP native `RSP_CallSites.csv`, `RSP_CallbackPressure.csv`, `RSP_Cadence.csv`. Source line mapping matches the above non-generic-edited UI file. This is an earlier capture and **not** post-semantic A/B; numbers are observed instrumented work, not proven reducible CPU or FPS.
- **Historical implementation:** `G-RED_optimizatios_seperate_passes.zip` pass 3.0, 5.0, 6.0, 7.0, with the final exact version in `G-REDruntime_Optimization_All_Mods.zip`. This legacy reference changed only `UI/DFBaseGameUIOverrides.reds`; there is no independent Dark Future adapter in those archives.

## 2. Workload concentration, not mod-wide guesswork

Source owner measured exclusive instrumented work ≈**10,151.7 ms** in the capture, historically reported as **26.401 ms/s**. The following four measured **source functions/wrapper** account for ~**94.8%** of that source-owned instrumented total when aggregated by their separate outgoing-callsite exclusive measurements. The fraction is within the GRSP accounting domain and must **never be summed with CET or interpreted as game CPU saved**.

| Function / wrapper | Exclusive instrumented work | Observed edge count | Active frames | Work being done |
|---|---:|---:|---:|---|
| `buffListGameController::UpdateBuffDebuffList` | 4,402.8 ms | 4,337,228 | 11,821 | Rebuild/push pooled buff widgets |
| `SingleCooldownManager::Update` | 2,522.9 ms | 527,130 | 11,823 | Timer, gauge and outro animation |
| `SingleCooldownManager::wrapper$204227` (`SetTimeRemaining` wrapper) | 2,007.7 ms | 176,166 | not applicable at wrapper total | Infinite-label handling and original finite-label setter |
| `gameuiCooldownGameController::OnEffectUpdate` | 692.5 ms | 420,511 | 11,823 | Reassemble effect menu and parse widgets |

Specific **separate callsites** (these costs are **subsets** of source-function work above, not additional sums):

| Source callsite | Calls | Instrumented exclusive work | Interpretation |
|---|---:|---:|---|
| `UpdateBuffDebuffList → buffListItemLogicController::SetData` | **38,318** | **3,394.6 ms** | Dominant buff-list render operation; ~3.24 per active frame, up to 13 |
| `SingleCooldownManager::Update → FillOutroAnimationStart` | **22,772** | **2,287.3 ms** | Strong repeated animation-start hotspot; active in 11,247 frames |
| `SetTimeRemaining wrapper → original proxy` | **13,348** | **1,889.2 ms** | Finite-label engine method, **not** the cheap -1 special branch |
| `OnEffectUpdate → ParseBuffList` | **12,007** | **371.4 ms** | Roughly one parse per frame; changing duration and pool state may be essential |
| `UpdateBuffDebuffList → GetController` | **592,252** | **271.8 ms** | ~**50.10** pooled widgets examined per frame |
| `UpdateBuffDebuffList → SetStatusEffectRecord(null)` | **550,922** | **143.1 ms** | ~**46.61** inactive pool slots cleared per frame |
| `UpdateBuffDebuffList → SetVisible(false)` on inactive slot | **550,922** | **49.9 ms** | Repeated inactive-visibility writes; excludes ~50.1-reads overhead |

**Critical structural insight:** `UpdateBuffDebuffList` walks **~50 pooled widgets per active frame**, but only renders **~3.24 effect entries per frame** on average. Most examined slots are in the empty tail. The code resets each empty slot's visibility and status record every time. This exposes an incremental **vacated-slot clearing** opportunity without deferring live updates for actual active effects. Pool replacement/reorder and external UI writers still require proof.

**Critical sentinel insight:** `SingleCooldownManager::Update` uses `timeLeft == -1.00` as infinite-duration display and calls `FillOutroAnimationStart()` whenever `timeLeft <= m_outroDuration`. The `SetTimeRemaining` wrapper recorded **22,687** visits to the `-1` branch (text/linear-wipe writes), almost the same as **22,772** outro starts. This is **strong source-and-call-count evidence** that infinite effects may repeatedly start the outro; because GRSP does not prove receiver/argument pairs, exact per-call overlap and avoidable saved time are **not measured**. The source branch is unguarded for infinite sentinel. Check initial animation semantics before suppressing anything.

The original code still updates dynamic `timeRemaining`, `timeTotal`, `stackCount`, icon, record, visibility and animations. **Count equality is not data equality**; arbitrary delay or first-N-frame skips change observable behavior.

## 3. Why blindly copying the historical passes is unsuitable

The four historical sources are cumulative. Exact evolution:

| Historical pass | Buff-list steady-state callback gate | Cooldown update gate | `OnEffectUpdate` gate |
|---|---|---|---|
| 3.0 | render every **3rd** same-count call | nonurgent every **3rd** | none |
| 5.0 | every **6th** | nonurgent every **6th** | none |
| 6.0 | every **6th** (comment says third) | nonurgent every **4th** | every **3rd** |
| **7.0 final** | every **10th** | nonurgent every **6th** | every **6th** |

The final `@addField(buffListGameController)` guards only **buff and debuff array counts**. An equal-count buff replacement, new record/icon, changed stack or changing duration can wait up to nine calls for rerender. `OnEffectUpdate` skips the first five callbacks by phase without an initial-complete/wake guard and returns `false` before the original visibility/mode logic. Both impose a **display cadence change**, even if acceptable during the historical gameplay test. They are not behavior-equivalent source substitutions.

The historical `SingleCooldownManager` cache keys on `timeLeft == -1` and equal stack count, but **does not reset `m_gredRenderInitialized` in `ActivateCooldown`**. Reused pooled controllers can therefore keep a stale snapshot when a different infinite effect activates with identical stack. The same source also changes finite timer draw cadence. **Retain verified intended behaviors (proper infinite rendering and bug fixes); do not copy the rate-limiting algorithm or its incorrect cache lifecycle into the new semantic rule.**

**Cross-mod collision check:** the live collection's `DigitalVixenCore/DVCore_StatusEffectVisibility.reds` declares `@replaceMethod` / `@wrapMethod` on the same `inkCooldownGameController`, `SingleCooldownManager` and buff-controller methods; however its potentially conflicting replacements carry `@if(!ModuleExists("DarkFuture.Main"))`. Retain that compile-time branch. A future DF generator must test **Dark Future + DVCore installed together**, DF-only and DVCore-only; never infer replacement uniqueness from one mod folder alone.

## 4. Ranked semantic candidates by measured work **and safety of proof**

### DF-S1 — infinite-duration stable-state no-op (**first implementation candidate**)

- **Target:** `SingleCooldownManager::Update`, precise `timeLeft=-1` branch. Preserve the existing first full update, tags, forced full gauge, alignment and any required first outro event. For subsequent *unchanged, same identity, same widget epoch* infinite updates, avoid redundant rendering; optionally gate the repeated outro start specifically if required.
- **Evidence:** 22,687 infinite-branch writes and 22,772 costly `FillOutroAnimationStart` calls. This is the strongest **small-source, high-work** match.
- **Proof blockers:** whether `FillOutroAnimationStart` has required repeated-event side effects; current effect ID, controller activation/pool reuse, stack identity, root widget/animation state, deactivation, mode changes, missing record, and invalidation. `ActivateCooldown` **must reset** cached state, even if the same `timeLeft` and stack recur. No call may be skipped if first render or runtime state is unknown.
- **Packaging preference:** new owner-local `@addField`/guard helper file if usable with existing REDscript APIs; a minimal source edit at the original `Update` method will probably still be necessary because the current method is a private implementation replacement. Do not fork the entire 1,501-line file.

### DF-S2 — bounded active/vacated buff-pool update (**second, high-confidence workload target after structural proof**)

- **Target:** `UpdateBuffDebuffList` full `while i < onScreenBuffsCount` loop; currently ~50 widget/controller iterations even for ~3 active entries, with 46.6 redundant null-record/hidden updates per frame.
- **Candidate:** walk current active slots and the previously occupied slots that genuinely need clearing. Preserve original ordering/visibility and untouched input data. Reinitialize or fall back to original full sweep if pool identity, size, widget binding, state or other owners may have changed. **Do not skip active `SetData` on a timer.**
- **Proof blockers:** all array/pool writers, external wrappers, controller recycling, slot reorder, source-permitted side effects of repeated null writes, transitions between empty/visible and game loading. Check pooled arrays beyond the first/last element; source-only proof may still reject a stateful shortcut. Lower direct measured per-call cost than S1, but very high redundant call volume.

### DF-S3 — **exact-tuple** `SetData` output suppression / static-icon specialization (**highest raw callsite cost, unproven stable proportion**)

- **Target:** 38,318 costly `SetData` invocations / 3,394.6 ms. The call arguments are `icon, timeRemaining, timeTotal, stackCount`; record, widget identity and activation epoch are also observable dependencies.
- **Safe candidate:** reuse only when the entire output-affecting tuple and target widget/record are structurally known unchanged, and `SetData` itself has no required per-call side effects; start by inspecting **infinite/static** icon rows rather than assuming finite countdown time is unchanged. Must preserve new effect with the same count, stack change, icon, countdown, type and recovery.
- **Proof blocker:** capture does **not** include per-invocation values/receivers. No assertion that most 38,318 calls are no-ops. A source-level helper/new file is viable only if the original callsite can use it safely; otherwise minimal original-path transformation.

### DF-S4 — finite cooldown text equivalence (**high direct work but needs original engine formatting**)

- **Target:** 13,348 `SetTimeRemaining → wrappedMethod` finite-duration calls / 1,889.2 ms.
- **Hypothesis:** avoid original text formatting/UI assignment when the **actual formatted label** would be unchanged. Need the original native/REDscript method's display precision, localization, clamping, animations and return/side effects first. A timer float changing every frame does not prove rendered text changes every frame; equally, rounded-to-second display must not be assumed without source.
- **Gate:** **research-only** until engine implementation and exact equality semantics are available. Do not implement an arbitrary 100/250ms update interval.

### DF-S5 — effect-list reparse only after proven meaningful change (**lower priority**)

- **Target:** `OnEffectUpdate → ParseBuffList` 12,007 calls / 371.4 ms. Detect full content and mode changes, not just count; honor visible-root and debuff stack fixes.
- **Gate:** current `Variant` argument is ignored by DF's replacement, but the source still calls live `GetBuffs/GetDebuffs`. A stable event/version/invalidation signal must be identified; otherwise retain every callback. No blanket every-sixth frame skip.

**Avoid separate optimization of low-work DF input/needs/vehicle/conditions functions on this capture:** source-attributed direct costs are small beside these UI loops. They remain discoverable if a future matched capture ranks them differently.

## 5. Integration and acceptance sequence

1. **Source admission:** only current installed DF source matching proven required methods/fields, not a version lock or hardcoded zip hash. No change to generic-touched DF files. Scan whole effective source for conflicting `@replaceMethod` and conditional `@if`, especially DVCore.
2. **One rule at a time:** implement S1 first, then S2 if safe. Re-evaluate S3 and S4 using the resulting residual F11 work; S3 may overlap S1's repeated stable-UI removals. Never add a second optimizer for the same redundant work or layer corrective passes on historical output.
3. **Preferred file mode:** per-mod new guard/field/helper file alongside existing `r6/scripts/Dark Future/UI`, **only if it actually owns safe state and avoids implementation duplication**. If needed, minimal source-aware rewrite at `DFBaseGameUIOverrides.reds` with generated helper, but never a blanket old-file copy. No new G-REDruntime core service, scheduler or new global listener required for a source-local UI optimization.
4. **Structural source-state contracts:** untouched post-generic, partial semantic, already semantic, user edits/drift, missing DF, DVCore present/absent, early/late buff event, effect pool reuse, changing identity with same stack, transient invalid/UNKNOWN state, reload/fast travel, language/localization. Exact no-op second generation, owner-isolated overlay and source-drift refusal.
5. **Official SCC only in CI** on current effective base + all ten generic rules + accepted semantic outputs; no SCC bundled/invoked by the player. Preserve combined CET/REDscript one-ZIP game-relative structure and rollback metadata.
6. **Real game acceptance:** active infinite effect, expiry/removal, new same-stack effect on recycled widget, finite timer under one second, reentry/reopen of status wheel, stack change, swapped buff with equal array count, visual animation/outro, combat/vehicle/fast travel, save/load and optional DVCore. Compare equal-scenario F11 before/after and PresentMon; only then report actual savings.

**Decision for owner discussion:** Is exact frame-for-frame cooldown/buff UI behavior mandatory, or are deliberate changes to the display rate acceptable? This determines whether the old cadence-based 7.0 pass is allowed at all. The default proposed S1/S2/S3 design aims to preserve outputs, not impose new 6/10-frame refresh policies.

**Next:** user discussion of S1 state/animation proof and S2 pool invalidation. No REDscript source rule, test or generated optimization artifact was changed during this audit.
