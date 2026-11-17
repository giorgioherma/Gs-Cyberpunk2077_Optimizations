# R5 — fact-value partial subscribers (NOT whole-method skipping)

**Owner:** GCO managed REDscript Resolver, branch `dev/redscript/resolver_handoff`.
**Implementation:** `8af047c0647da0bf6edfb7d6c511dc7485c0799f`.
**CI:** [#646 SUCCESS](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/44123182727), source `8af047c`.
**Status:** read-only source and sealed-F11 evidence. **Zero** game-source rewrites, getter substitutions, installed listeners, runtime A/B claims or generated optimization ZIP.

## Why the six-guard experiment was the wrong unit

The original 1,465 exact-named fact reader sites / 753 literal fact cohorts (GCO #638 SUCCESS) are **not** six subscribers. That six-count came from a narrow whole-guard scanner. The narrow guard detour was explicitly reverted at `6d7814e`; do not reintroduce its admission model.

A method may depend on a fact **and** time, widgets, mutable fields, current player state, callbacks, action arguments or another fact. Such a method can still consume a *shared fact input* while retaining the other reads and all original code. Its **entire body** is not safe to suppress.

## Implementation — input site and downstream value, not enclosing method

`CallbackSemanticResolver.QuestPartialValueInputs.cs` consumes the **existing** `InspectQuestSubscribers` read sites, exact token indices and the existing `ReadAllMethodSources` parser, source SHA admission and optional F11 source-match evidence. It does not create a new lexer, parser, transformation pipeline or mod catalog.

For each existing literal reader, `scan-quest-facts` exposes a `questPartialValueInputs` record:
- Owner/class/method, exact literal fact name and direct versus same-owner helper witness.
- Use shape: directly in a condition (with or without parentheses); direct return; local initializer; derived/mixed local initializer; instance state or other expression.
- If assigned to a local: downstream conditional/return/possible callee-argument uses, later mutation or shadowing. This is a bounded **lexical witness**, not a proven alias/escape graph.
- Unrelated method fields, helper calls and used arguments remain listed separately; a mixed method is **not eliminated** from partial-input research.
- Source-matched method outgoing edges and instrumented exclusive duration for research prioritization only. These are **not** per-fact cost or projected savings.
- Hard-false authorization for derived-value purity, complete engine/native fact writer coverage, callback/lifecycle parity, original nonfact-path preservation and source rewriting.

## What subscription tokens would eventually mean

G-REDruntime's existing `QuestFactSignals.AcquireFact` returns a distinct consumer interest token for an exact observed fact; multiple consumers can share one listener registration, and each releases its own interest. A consumer must still use the **original getter** when no observed value is known, on missing framework, attach/detach invalidation or any unproven signal coverage.

Acquiring a token does not prove that every engine/native or mod writer emits an equivalent notification. Do **not** automatically deploy listeners for each detected site. Preserve original callback order, short-circuit evaluation, wrappedMethod, UI updates, timers, and other data paths. Grouping readers by fact is a research relationship, not a blanket subscription authorization.

## Relevant live-source examples (never hardcoded rules)

- `CourierJobs/CJ_Runtime.reds` has a typed `GetFact(CName)` helper: tick and HUD paths read exact named facts while also managing progression, UI and timer state. The existing same-owner-wrapper witness discovers those partial inputs without removing the surrounding work.
- `VehicleMileage/UI/VMFuelGauge.reds` has fact-driven local visibility values, but the remaining vehicle UI/alarm/modal checks must still execute.
- `negotiableaffection/Main/NA_DialogueChoicePopup.reds` reads `na_active` in `OnAction`; any subscriber must preserve original `wrappedMethod` and action delivery.

In the Nov 16 sealed 384.517s capture, all measured `GetFact`-named outgoing callsites together had about **275.91ms** instrumented exclusive time; this is neither a complete attribution of downstream work nor a claim about potential savings. Focus future optimization on high-cost **pure derived computations**, not substituting cheap getter calls indiscriminately.

## Verified private-source admission numbers

**CI #646 SUCCESS (source `8af047c`):** 108 managed Resolver tests; 7 player + 8 quest framework source contracts; 2,319 live REDscript sources and official SCC 0.5.31 with 11 framework files; 323 GCO installation/package contracts. Bidirectional source rows: **1,465** literal read sites / **753** exact-name groups. New per-value evidence: **1,456** reads inside mixed-dependency methods, **287** source-measured method inputs, **493** local-value assignments, **855** direct or local downstream-condition inputs, including existing **145** same-owner helper reads. These are site counts, not distinct optimized methods; **0** authorized rewrites, **0** gameplay tests or measured savings. [Run #646](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/44123182727).

## Consumer admission gates

1. **Identify shared input:** source-typed exact fact identity / same-owner helper, and map its local/direct downstream uses, including methods with additional changing inputs.
2. **Close only the candidate subexpression:** show its exact input and alias dependencies, writer graph, effect-free computation, and unchanged evaluation order. Do not demand closure for unrelated method work, but do not let that unrelated work be skipped.
3. **Prove producer and lifecycle:** notification coverage for all relevant mod/native/engine changes, initial unknown fallback, repeated event behavior, reentrancy, save/load, player session identity, stale bridge and release.
4. **Authorize a single source-owned transformation only after proof:** original getter fallback and original method remain, no new duplicate generator, stable across untouched/partial/optimized installations and safe source drift.
5. **Validate real output:** official SCC 0.5.31 full stack, idempotence, isolated output, user-led game/lifecycle checks, and matched sealed GRSP/CET/PresentMon A/B. Never add overlapping timing domains.

**Next:** Use the private `questPartialValueInputs` output to group same-fact value slices across otherwise mixed methods, rank source-matched *downstream computation*, then find the first source-closed derived-data branch. Do not turn all detected readers into live subscriptions.
