# G's Cyberpunk 2077 Total Optimization Generator

This is the **new unified-product repository** for Cyberpunk 2077 v2.31. It is **not** a continuation of the historical TOTAL Profiler binary stack.

The current production systems are maintained in their own authoritative repositories:

- [G-CET Runtime Profiler / Resolver](https://github.com/giorgioherma/G-CET_Runtime_Profiler), development branch `dev/resolver-profiler-handoff`: CET/Lua profiling, 0-Engine/native integration, source-aware generic/semantic Resolver and G-GC.
- [GRSP G-REDscript Profiler](https://github.com/giorgioherma/GRSP-G-REDscript-Profiler), development branch `dev/resolver-profiler-handoff`: REDscript native profiling and future measurement extensions.
- [G-REDruntime Framework](https://github.com/giorgioherma/G-REDruntime_Framework): REDscript scheduling, state cache, event/input/hook services.
- CapFrameX: optional, independently distributed frame-time capture companion.

**Goal:** one accessible Windows application for setup, synchronized CET + REDscript + optional CapFrameX captures, combined (non-double-counted) analysis, generic-first optimization of eligible installed source, and transparent safe generation/deployment/restore. Keep native profiler implementations and domain-specific source generators authoritative in their respective projects.

`main` is the clean product baseline. Development begins on `dev/resolver-profiler-handoff`; no tested release or complete migration is claimed here.
