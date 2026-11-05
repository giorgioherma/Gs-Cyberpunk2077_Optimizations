# Latest work / evidence status

**Date:** 2026-11-04. **Repository:** `giorgioherma/Gs-Cyberpunk2077_total_optimization_generator_v2.31`. **Branch:** `main`.

## Completed — documentation and planning only

- Located the actual **2026-10-16 user-approved design** on CET `dev/native-static-anchors-experimental` at `docs/product/UNIFIED-TWO-PAGE-WORKFLOW.md`. Source Git blob: `f4925759fc612311b72432b329708b84b9dd279e`. It prescribes one game-root input, two pages, three first-page actions, current-session capture gating, reversible profiling, one deployable ZIP, Vortex/rollback safety, MO2 disclaimer and generic-first high-value optimizations.
- The unified product's initial repository was created and a README committed. The user then explicitly decided **this repo works on `main`, not a separate handoff branch**.
- Reviewed the CET and GRSP source headless entry points and project ownership. CET code remains in CET repo; GRSP instrumentation in GRSP; G-REDruntime in its own repo; new REDscript generic generator and unified UI here.
- Added authoritative original design, explicit delta, architecture, exact CLI/summary contracts, milestone roadmap, mandatory agent startup and documentation index.

## App foundation — source and Windows CI VERIFIED, gameplay/GUI still not validated

- Selected **.NET 10 WinForms** for the new unified app; no downgrade to older CapFrameX. Verified current CET Profiler/CET Resolver/GRSP manager projects target .NET 8 and that CapFrameX 1.9.1.2 Beta requires .NET 10 Desktop. Shared private runtime across **all** processes remains unproven; see [runtime contract](technical/SHARED-NET-RUNTIME.md).
- Added a two-page read-only desktop shell, one game-root input, basic source presence states, optional executable-path detection and distinct CET/GRSP CLI status JSON handling. Install, capture, continue and ZIP generation are disabled. The shell cannot proceed without validated current-session capture proof.
- Added Windows CI with .NET 10 self-contained build, one-runtime-in-shell check and a no-dependency test harness. **COMPLETED SUCCESS: [Windows CI #41859261728](https://github.com/giorgioherma/Gs-Cyberpunk2077_total_optimization_generator_v2.31/actions/runs/41859261728)**, source SHA `fa9309de92fa2022b784e78b0cd4e7ae61e1f1b1`, **14 read-only core contracts PASS**, WinForms win-x64 publish PASS, package checked for exactly one `coreclr.dll` and no unverified CET/GRSP/CapFrameX binaries. Uploaded artifact `G-Total-Optimizer-Shell-win-x64` **13339416350**. This proves the build and contract tests, **not** an actual UI launch, profiler status response from real current binaries, any game runtime or shared .NET packaging of all components.

## Not implemented / not validated

- The .NET 10 shell build and offline tests are CI-verified; **interactive UI launch on user's Windows PC remains unverified**. No verified component-bundle manifest, unified session controller, synchronized three-producer capture, combined report or combined optimization ZIP exists.
- No new REDscript generic source transformation or compiler validation implemented.
- No test of install/restore across both real current native profilers; no new verified game world-load/gameplay acceptance.
- No approved transfer of old TOTAL binary components. Unused old experimental branch was created before the user clarified main-only ownership; this branch is **not** the intended development target.

## Next work (after user acceptance of documentation)

**Milestone 1 continuation:** Windows CI and 14 offline contracts are green. Next verify interactive shell launch and actual standalone CET/GRSP status against real packages, then stage independently verified managers, without starting installation or rewriting until restore safety is complete.

**Active blockers before implementation claims:** actual current standalone component artifacts and API schema under installed builds must be checked against source; CapFrameX packaging licensing/provenance and cross-profiler session synchronization have not been validated. MO2 effective mod discovery is unsupported until a separate proven provider exists.

When implementation starts, append exact commits, build jobs, test output and gameplay evidence to this file; preserve separate completion labels for implementation, CI/source, compilation and gameplay.


## 2026-11-04 — CET-style two-page redesign implementation (CI pending at commit)

- User authorized replacing the original light shell with CET v1.3.4 colors and an updated **two-page / no-tab** workflow: Source + Capture → Analytics & Pass Generation. Page 1 displays one root-or-CET-mods chooser, instructions and live key evidence; Page 2 uses separate CET and REDscript scrollable text reports and explicitly requested **Analyze / Generate / Open Results** buttons.
- Added exact physical root recognition and immediate \`init.lua\` CET owner-count detection. Direct canonical CET mods paths resolve the same game exe; external/MO2 source roots are **source-only**, never a claim of resolved VFS, game executable, profiler readiness or deployable generation.
- Added non-destructive CET F11 / custom-key inspection, REDscript F11 availability hint and CapFrameX \`portable.json\`/AppSettings \`CaptureHotKey\` inspection; mismatch/absence is not green or silently edited.
- Added contract tests for direct/external CET source, missing init.lua, F11/custom/corrupt bindings, CapFrameX F11/custom/missing. Kept real install/collect/restore, page-2 unlock and generation safely gated: no verified component provenance, current-session evidence or REDscript compiler integration yet. This milestone **does not** claim those actions work, nor a real UI launch.
- Original October 16 workflow remains preserved verbatim. New current-decision delta authorizes visible Page 2 Analyze and distinct Page 1 collect/restore steps.
- **CI VERIFIED:** [Windows CI #41898838784](https://github.com/giorgioherma/Gs-Cyberpunk2077_total_optimization_generator_v2.31/actions/runs/41898838784), source `76a226bc67136dbfa626fed3372c53705d9937dd`, **27/27 offline read-only contracts passed**, .NET 10 win-x64 shell built and verified one bundled coreclr, **artifact 13356603686**. No interactive GUI smoke, cross-component installation, capture, restoration or gameplay validation.


### Follow-up — optional CapFrameX linking (CI VERIFIED)
- Added a browsable, persisted CapFrameX executable path on Page 1, with bundled-known-path fallback and reinspection of actual portable capture key.
- Does not ship a third-party binary, install CapFrameX, alter its configuration or claim the F11 key is confirmed when its actual JSON differs.

- **CI VERIFIED:** [Windows CI #41899293106](https://github.com/giorgioherma/Gs-Cyberpunk2077_total_optimization_generator_v2.31/actions/runs/41899293106), commit \`9ef29b7a5d957b9c0490e706b62bf2b4dd36fabf\`. .NET 10 publish and 27 core contracts passed, all shell package checks green.

### CLI command / component provenance foundation (CI VERIFIED)
- Added bounded standalone process invocation with explicit CET versus GRSP JSON result parsing, collected archive path requirements and failure preservation. Does not itself install anything.
- Added SHA-256 exact executable checks against a future per-component source commit / CI run / artifact manifest. Manifest missing or drifted => blocking status. This is an **admission prerequisite**, not proof of all native payloads or a complete transactional combined install.
- Regression tests verify CET/GRSP collect schema, failure/restore refusal, missing bundle and executable tamper rejection.
- **Still unimplemented:** verified CET/GRSP staged artifacts and all payload hashes; actual install/partial rollback; game launch/current-session capture recognition; linked CapFrameX F11 preset writing; reliable collection/recovery; REDscript compilation; combined ZIP. No new gameplay outcome is claimed.

- **CI VERIFIED:** [Windows CI #41899996796](https://github.com/giorgioherma/Gs-Cyberpunk2077_total_optimization_generator_v2.31/actions/runs/41899996796), code commit \`6423056de3493a4dbe0f9886290633462d99a83f\`. **37/37 read-only/JSON/provenance contracts PASS**, self-contained .NET 10 Windows publish PASS, packaged exactly one private .NET runtime and no unverified profiler binaries; Windows artifact **13357816626**. GUI launch, real profiler lifecycle, live capture, CapFrameX launch, REDscript compilation and in-game results remain **NOT TESTED**.

## 2026-11-04 — unified app v1.1.0 version assignment

- Explicit user request: identify the **unified application** as version **1.1.0**, without altering CET or GRSP standalone version numbers.
- Central `Directory.Build.props` sets managed `Version/VersionPrefix` to `1.1.0`, `FileVersion/AssemblyVersion` to `1.1.0.0`, and a predictable informational version. WinForms title reads assembly product version.
- Windows build checks the executable's file version, stamps `publish/VERSION.txt`, and uploads a `G-Total-Optimizer-v1.1.0-Shell-win-x64` artifact.
- **No release tag** and no production-ready claim: the actual install/capture/restore/REDscript generator remains gated pending integration tests and verified components.
- **CI VERIFIED:** [Windows CI #41902135541](https://github.com/giorgioherma/Gs-Cyberpunk2077_total_optimization_generator_v2.31/actions/runs/41902135541), build source commit `e3bf733d7c273f8dd6d614c5b24acca7c720f8bc`. **37/37 core contracts passed**, .NET 10 Windows publish passed, single private runtime check passed, executable metadata verified **1.1.0.0** and display version **1.1.0**; artifact **`G-Total-Optimizer-v1.1.0-Shell-win-x64` ID 13359386102** uploaded. No standalone component integration or gameplay acceptance claimed.


## 2026-11-04 — Page 1 CET UI redesign / MO2 read-only source projection

- User supplied screenshot comparison and explicitly requested a new Page 1 layout: **Game folder / Cyberpunk2077** picker with correctly drawn larger Browse button; inline **Normal / MO2** selector and MO2 instance/profile inputs; white \`0-Engine\`, \`r6/scripts\`, and \`red4ext/plugins\` checks, with the public Nexus 0-Engine link only when missing; large cyan/yellow/green capture instructions; readable disabled action buttons matching CET; pink enabled Continue. Keep two pages.
- CapFrameX will be **bundled** rather than user-selected. Removed its executable path picker from the normal UI. The F11 status still reads exact bundled \`components/capframex/CapFrameX.exe\` configuration if/when verified payload is packaged. No unverified binary/config or distribution rights claim.
- Added independent read-only \`Mo2SourceInspector\`: parse \`ModOrganizer.ini\` directories and selected/explicit profile \`modlist.txt\`; inspect enabled package physical \`bin/x64/plugins/cyber_engine_tweaks/mods\`, \`r6/scripts\`, \`red4ext/plugins\` paths; resolve same-path priority using later enabled package and Overwrite; count only CET owner \`init.lua\`. No package copying, no game writes, no requirement to run MO2 for source inspection.
- **MO2 limitations:** source projection does NOT verify loaded runtime virtual tree, plugin priorities, RootBuilder's virtual game-root files, all separator/root layouts, or full mixed physical/MO2 winner rules. The app keeps destructive install and pass generation gated until these are proven. Any unclear profile/directory fails closed; missing sources are not presumed installed. Profile chooser offers local instance profiles, while configured external profiles can still resolve from MO2 settings.
- Added tests for relocated MO2 dirs, enabled/disabled mods, winning mod, overwrite, missing instance and invalid variable path. Physical game root remains required for profiling.
- The workflow's **v1.1.0 version stays unchanged**; this is a source revision, not a standalone CET or GRSP version bump.
- **CI VERIFIED:** [Windows CI #41917381621](https://github.com/giorgioherma/Gs-Cyberpunk2077_total_optimization_generator_v2.31/actions/runs/41917381621) source commit `ae979cf08f56e158cc340a4ebcd3459eef764df5`. **43/43 read-only core/CLI/MO2 tests passed**, .NET 10 Windows publish passed, v1.1.0 executable metadata verified, one private runtime verified, artifact **`G-Total-Optimizer-v1.1.0-Shell-win-x64` ID 13364529091**. One known nonblocking CS0649 warning for unassigned future-results-folder field. No GUI runtime smoke test or gameplay acceptance; no native profiler or CapFrameX binaries bundled.


## 2026-11-04 — Page 1 UI revision and read-only MO2 source provider (CI pending)
- Correct CET visual defects from v1.1.0 screenshot: taller Game folder field, full Browse border, gray disabled text, cyan status and magenta enabled Continue.
- Normal/MO2 selector inside Game folder box; in MO2 mode choose its instance and active profile explicitly.
- New read-only MO2 source inventory: ModOrganizer.ini Settings paths including %BASE_DIR%; profile modlist.txt (+ enabled/- disabled); highest-priority loose-file winner; physical game fallback; MO2 Overwrite; CET init.lua counts, r6/scripts and red4ext/plugins discovery.
- Does not copy mods or alter sources. Not yet runtime USVFS/RootBuilder/archives/exceptions verified; not yet wired to CET Resolver source-provider contract or ZIP generation.
- Larger colored gameplay instructions: CET/GRSP/CapFrameX F11 status, collisions warning, 5–10 minute city stress, avoid UI/menus and stop on death before loading.
- Per-user decision CapFrameX will be bundled: removed manual executable-path UI, fixed inspection location components/capframex/CapFrameX.exe. Vendor licensing, binary provenance, .NET 10 runtime tests and preset deployment still pending.
- Added MO2 mixed-source, priority, profile, disabled-mod and refusal tests. Combined profiler lifecycle remains gated.
- CI result pending; record run and artifact before claiming verified.


### 2026-11-04 — final CET/MO2 Page 1 CI verification
- SUCCESS: https://github.com/giorgioherma/Gs-Cyberpunk2077_total_optimization_generator_v2.31/actions/runs/41918316512; source commit 6abb960f98bc2b8d4e8079e5c31d1b020e311e2a.
- 52/52 core read-only/MO2 contracts passed; .NET 10 Windows publish passed; v1.1.0.0 metadata and single private runtime verified; artifact 13365971513.
- Previous failed runs 41917561978, 41917813927, 41917984334 exposed WinForms CheckState ambiguity and WFO1000 property-serialization issues. Fixed at source, latest CI green.
- Actual GUI display and gameplay untested; verified MO2 live USVFS/RootBuilder, cross-component profiling lifecycle, CapFrameX vendor packaging and combined optimization output remain pending.


## 2026-11-04 — compact non-scrollable Page 1 / full Normal–MO2 tabs

- User supplied screenshots showing the CET reference is significantly cleaner than the v1.1.0 build. Explicit requested correction: reduce game path height, use **full-width Normal/MO2 tabs with mutually exclusive content**, and **remove the Page 1 scrollbar**.
- Replaced the Page 1 scrollable FlowLayoutPanel with a fixed three-row TableLayoutPanel; the source group switches between compact game-folder input and the MO2 instance/profile view. Page 2 remains scrollable.
- Preserved the single saved game-root source of truth: MO2 view reuses the verified normal game root and will request it if not set; MO2 filesystem profile mapping still does **not** prove virtual runtime/RootBuilder behavior.
- Kept CET colors, cyan/yellow/green instruction hierarchy, dependency checks, visible missing 0-Engine link, gray disabled workflow controls and pink Continue once legitimately unlocked.
- Added a shared size contract with tests for both modes at the reference client height, including a minimum workflow-area threshold. Set minimum window size to prevent accidental clipping instead of making Page 1 scroll. User must visually verify on Windows at their chosen DPI.
- **No backend changes, no profiler binaries, no installation/generation enabled.**
- **CI VERIFIED:** [Windows CI #41921303918](https://github.com/giorgioherma/Gs-Cyberpunk2077_total_optimization_generator_v2.31/actions/runs/41921303918), source SHA `405aa2831eb34f70269210d22bdf4ccf3e1b9964`. **56/56 read-only and layout contracts passed**, .NET 10 win-x64 self-contained publish passed, Windows file version **1.1.0.0** verified, artifact **13367296627**. Windows runtime UI visual/interactive check and gameplay remain unverified.


## 2026-11-05 — CET-sized path controls; MO2 arbitrary-folder and REDscript ownership fixes

- User's screenshot exposed a malformed/oversized game path row and requested CET's exact compact 26px input + 92x30 Browse outline, white normal instructions/checks and green only the 5–10-minute combat sequence. Changed the browse control to a normal flat WinForms button with absolute size/position rather than nesting it in a clipping row.
- Normal and MO2 tabs remain mutually exclusive inside the first box, and Page 1 remains non-scrollable. MO2 view now has its **own game-root input** synchronized with the Normal view plus separate **MO2 instance input and active profile dropdown**. These are source selectors; no copies of mods are required.
- Added structural REDscript owner grouping for the header count (not a raw .reds file count). Normal source identifies top-level script folders and standalone .reds sources; MO2 counts effective winning package owners/physical sources. **Estimated count**, not semantically authoritative mod inventory.
- For MO2 instances in arbitrary locations, separate INI-configured Mods/Profiles/Overwrite storage on arbitrary directories and a game root with zero directly installed mods, added explicit read-only regression tests. A physical game executable is still needed for launching/profiling; plugin VFS runtime and RootBuilder behavior remain unverified and unsafe for generation.
- Preserved F11 key inspection without yellow unavailable warnings in the visible capture instructions. The UI calls absent payloads **F11 (preset)**, never a verified checkmark. Standalone CET and GRSP source versions unchanged.
- No new profiler binaries, no gameplay installation/restore or optimizer ZIP enabled. CI pending; log the exact run once it completes.


## 2026-11-05 — progressive MO2 file-source selectors (CI pending)
- User requested that MO2 not require conventional instance/profile layout: **select one through four arbitrary physical folders**; the app recognizes CET ASI, CET mods/init.lua, r6/scripts .reds, RED4ext plugin DLLs by files and ticks four read-only checkboxes.
- UI now shows slot 1 by default, reveals slot 2/3/4 only until all four components are covered, and hides surplus slots when one/two/three folders suffice. Existing Normal game-root setup remains separate. Each selected folder can cover multiple types.
- Read-only, bounded depth/file-count walking skips symlinks/reparse points and hidden MO2 entries, refuses unreadable/oversized inventories rather than reporting a false complete source. Deduplicates identical physical mod and REDscript files across overlapping folder paths. Tested one/two/three/four-folder cases, duplicates and missing sources.
- **Limitation:** Presence in arbitrary physical folder does not prove MO2 active-profile priority, USVFS, RootBuilder, archive injection or deployment. No direct path from this heuristic inventory to CET Resolver/generation. Profilers still require actual game root to be proven; install and Continue remain gated.
- **CI VERIFIED:** [Windows CI #41936579433](https://github.com/giorgioherma/Gs-Cyberpunk2077_total_optimization_generator_v2.31/actions/runs/41936579433), source commit `781806687153172f1e93c0ca25445867018e5b2e`, **75/75 read-only/MO2/UI contracts PASS**, .NET 10 win-x64 publish PASS, version 1.1.0.0 and single bundled runtime checks PASS, artifact `G-Total-Optimizer-v1.1.0-Shell-win-x64` **13373772655**. Prior run #41936387189 failed duplicate-source contract; fixed in authoritative source by refusing to consume hidden later rows after invalid/duplicate selection. No runtime game/virtual profile/install/restore validation claimed.


## 2026-11-05 — portable/instance MO2 discovery and profile-gated checks (CI pending)
- Added Mo2InstallLocator for portable ModOrganizer.ini or global instances under %LOCALAPPDATA%/ModOrganizer; ambiguous instances require selection, and manually located instance folders can be used.
- MO2 setup uses compact fields for MO2 folder, game root, detected Mods folder (manual override when necessary), instance and profile. The selected INI resolves independently configured Mods, Profiles and Overwrite paths.
- Added effective CET ASI winner support, configured preferred profile, and fail-closed missing enabled-package handling to Mo2SourceInspector. The five UI checkboxes (MO2 profile plus four component types) only activate after profile/modlist and winning-file inventory are resolved.
- Removed the obsolete arbitrary-file folder scanner and replaced its tests with portable/instance/autodiscovery/manual mapping, profile preference, incomplete enabled package, ASI winner and mods-directory recovery tests.
- Critical unresolved: actual USVFS launch/RootBuilder/file-exception/archive loading, component installer/restore safety, CapFrameX packaging and unified REDscript generator. CI not yet checked for this source revision; no in-game performance claims.


### 2026-11-05 — verified MO2 portable/instance source discovery
- **CI PASSED:** https://github.com/giorgioherma/Gs-Cyberpunk2077_total_optimization_generator_v2.31/actions/runs/41941112037 — source commit 245d9a1bb5b90be1a8c683be9cc24c3a5b080b9d. 76/76 core read-only/CLI/MO2 contracts passed, .NET 10 win-x64 publish passed, version 1.1.0.0 verified and one private runtime packaging checked. Artifact G-Total-Optimizer-v1.1.0-Shell-win-x64, ID **13376042492**.
- Previous run #41940986432 passed the 76 core contracts but failed Windows compilation due to missing read-only SourceCheck UI helper; fixed at source in commit 245d9a1. Latest Windows build is green.
- UI/runtime visuals and actual MO2 injected file inventory are not game-tested. Component discovery is logical loose-file precedence, not a claim about full USVFS/RootBuilder/file-level exception behavior. Profiler install/collect/restore and combined optimizer ZIP remain disabled.


## 2026-11-05 — compact capture directions, status glyphs and MO2 warning order (CI VERIFIED)
- Updated CET-style path picker with a magenta outline on unresolved Normal and MO2 path fields, reverting to subdued border only when source is verified. Source guidance is yellow on missing paths.
- Split Normal source check labels from their icons: white paths and missing text, only red X / green tick glyphs in color. Five MO2 checkboxes now use an owner-drawn red X or green tick to avoid OS theme black checkboxes.
- Removed all three standalone F11 status badges from Page 1 instructions. Text starts with live-game-files path instruction; then launch+F11, temporarily unbind F11 from other mods/game settings, and ensure Cyberpunk alone is selected in CapFrameX.
- Kept recording headline green and changed idle/movement/driving/combat to amber. Avoid-menus and final instructions remain white.
- Rebalanced non-scrollable Page 1: instructions area 250px, header/buttons/status slightly reduced, leaving minimum 132px for three profiler steps in MO2 at 1010×790 reference. Adjusted last three-line label to 55px and moved the MO2 missing 0-Engine warning above the five checks. Added layout assertions for text fit, warning/check order and no clipping.
- Corrected step 2 (all profilers) and step 3 (close game, collect results and continue). Unchanged: disabled install/capture/restore/generation operations, MO2 source admission and component ownership. Actual WinForms rendered appearance still requires user inspection; no gameplay/profiler integration evidence claimed.
- **CI VERIFIED:** [Windows CI #41943506876](https://github.com/giorgioherma/Gs-Cyberpunk2077_total_optimization_generator_v2.31/actions/runs/41943506876), source commit `df04d6e39d71199863f11de14652e0208cee82b1`; 81/81 read-only, MO2 and layout contracts PASS, self-contained .NET 10 Windows x64 publish PASS, v1.1.0.0 metadata and one runtime check PASS. Artifact `G-Total-Optimizer-v1.1.0-Shell-win-x64` ID **13378135292**. A known unassigned resultsFolder warning remains because the real backend is still gated.

## 2026-11-05 — v1.1.1 unified app version bump (CI pending)
- User requested a version-only bump from v1.1.0 to v1.1.1 for the unified .NET 10 shell; no changes to CET, GRSP, framework or game version.
- Updated shared managed assembly/file/product version and the Windows CI version assertions, package stamp and artifact name. The GUI title reads the assembly product version.
- No new runtime, profiler, MO2, or gameplay integration is implied.
- **CI VERIFIED:** [Windows CI #41943882133](https://github.com/giorgioherma/Gs-Cyberpunk2077_total_optimization_generator_v2.31/actions/runs/41943882133), code commit `a0cc867cdf8c2a5fe4b925b3c05612e3a08fe552`. **81/81** core read-only contracts passed; .NET 10 win-x64 self-contained publish passed, exactly one bundled runtime checked, app display version **1.1.1**, file version **1.1.1.0**; artifact **G-Total-Optimizer-v1.1.1-Shell-win-x64** ID **13377591563** uploaded. No GUI/gameplay test or functional install/restore/generator claim.


## 2026-11-05 — v1.1.1 path outlines, visible source checks and manual Refresh (CI pending)
- Corrected v1.1.1 screenshots from the user: path selector magenta frame is now 1px instead of a thick solid magenta band; verified paths use the muted border.
- Introduced a single owner-drawn Normal source status control that displays white path text and NOT FOUND alongside a red X or green tick. This replaces separate labels whose colored glyphs were absent in the user's screenshot.
- Added `SetupSourcePresentation` (read-only UI state): missing game path => amber selection prompt; valid game without 0-Engine => amber `Game found; 0-ENGINE MISSING!!` beside Nexus link; valid game plus 0-Engine => white game and mod counts. Unit tests cover all three.
- Added green tick to the instructions' 'Provide the path to the live Cyberpunk 2077 game files' row after executable-root verification. This tick is only for the physical game path, not proof of MO2 virtual loading.
- Added top-right REFRESH to rerun real path/status inspection and reload MO2 INI/profile discovery when applicable; corrected step 3 to include restoring original files before continuing.
- Preserved non-scrollable Page 1, two-page product, CET dark palette, MO2 owner/source admission and existing disabled workflow gates.
- **CI VERIFIED:** [Windows CI #41946052345](https://github.com/giorgioherma/Gs-Cyberpunk2077_total_optimization_generator_v2.31/actions/runs/41946052345), code commit `19102f5c3a8c42bd2f1dc4b43751195f95d1a4bf`. **84/84** core read-only, source and presentation contracts passed; self-contained .NET 10 win-x64 publish passed; version **1.1.1.0** checked; artifact `G-Total-Optimizer-v1.1.1-Shell-win-x64` ID **13378338300** uploaded. User Windows UI visual validation is outstanding; profiler installation/restore/ZIP generation still gated.


## 2026-11-05 — live-game-first MO2 resolution and CET-style portable packaging (CI pending)

- User explicitly set **physical live game as the baseline on every profile**, including when MO2 `modlist.txt` is missing, empty, all disabled or has active packages. Without a usable modlist, **no MO2 packages or Overwrite** may enter the source map; physical game scripts remain available. Missing/unreadable/unsafe profiles must not silently enable any MO2 package.
- Extended authoritative `Mo2SourceInspector` with `ProfileVerified` versus `GameOnlyFallback`: physical-game indexing stays available while profile check stays unconfirmed. Zero-enabled **present** modlist is valid: physical files remain and Overwrite is included as a separate MO2 layer; enabled mod files are excluded. Active packages override only identical game-relative files according to MO2 left-panel order (highest first); disabled packages remain excluded, Overwrite takes the final loose-file layer when a verified modlist exists.
- Missing or corrupt/incomplete enabled package inventory fails closed for MO2 overlays, while preserving physical game-only evidence; invalid enabled mod names now explicitly refuse MO2. No source writes or source-copying; runtime USVFS, RootBuilder, archives and file exceptions remain unverified, and no gameplay/ZIP generation is authorized.
- Tests include direct CET/REDscript owners coexisting with enabled MO2 owners, high-priority file winner provenance, disabled/empty/missing profile, Overwrite isolation and malformed/missing enabled packages.
- CET-style portable Windows layout: a small native launcher `G-Total-Optimizer.exe` at the public root, optional `Start G-Total-Optimizer.lnk`, managed `.NET 10` app and single runtime inside `app/`, `RESULTS/` workspace and version stamp. Package-root lookup now resolves from the nested managed executable, preserving component path correctness. Root launcher has no bundled CLR; source-built in CI.
- Profiler binaries, CapFrameX licensing/bundling and install/capture/restore remain gated. Shortcut portability must be tested on extraction to a different directory; the native root launcher itself is relocatable. **CI VERIFIED:** [Windows CI #41950289302](https://github.com/giorgioherma/Gs-Cyberpunk2077_total_optimization_generator_v2.31/actions/runs/41950289302), code SHA `d13c79c99148ec27d54cf47a8ed4c6e791acb2a9`. **101/101 read-only/source/CLI contracts PASS**. .NET 10 win-x64 managed publish PASS; native CET-style launcher compiled, version **1.1.1.0** confirmed for root launcher and internal app; only one private runtime; clean portable folder and root `.lnk` shortcut verified structurally. Artifact **G-Total-Optimizer-v1.1.1-Portable-win-x64** ID **13380467867**, ~51.5 MB. Failed runs #41950089034 and #41950188044 were obsolete test expectations after intentional game-only fallback changes; corrected tests at source. The shortcut has not been runtime-tested after moving/extracting to another PC; root launcher resolves internal `app/` relative to its own executable. No actual game/UI/capture/install/restore/optimization testing.
