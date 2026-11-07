# Product decisions and explicit delta to the October 16 design

**Authority model.** The original [UNIFIED-TWO-PAGE-WORKFLOW](UNIFIED-TWO-PAGE-WORKFLOW.md) is copied **unchanged** from the accepted 2026-10-16 user/agent design. This file records **later explicit user decisions and implementation interpretation only**. If a suggestion here conflicts with original behavior without a direct user authorization, the original wins; ask the user before changing it.

## Explicitly approved since the original specification

1. **New repository, `main`.** User created [Gs-Cyberpunk2077_total_optimization_generator_v2.31](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations) on 2026-11-04 and explicitly rejected using its temporary `dev/resolver-profiler-handoff` as the development branch. Keep this product on `main`. This does not change existing CET/GRSP branch choices.
2. **Current components, not old TOTAL.** The historical TOTAL application was based on completely different, obsolete CET profiler code. It is only a reference for interface and correlation concepts. New product integrates current CET, GRSP and REDscript framework components.
3. **REDscript generic-first.** The user explicitly **does not intend a large REDscript semantic library**; apply generally safe/reusable generic rules, with rare optional stack-specific semantics only when the user decides. This tightens the October 16 provisional REDscript scope without prohibiting agreed exceptions.
4. **One combined optimization plan with clear ownership.** GCO owns the single user-facing analysis and cross-domain coordination, while **CET retains its existing Lua Resolver/Generator** and GCO develops the **new REDscript Resolver/Generator**. Select compatible work using both profilers' evidence and current live sources. Preserve CET's complete generation order, authority and existing work, plus REDscript compilation/source-safety. Do not duplicate generators or create a second ordinary Resolver UI.
5. **GRSP can evolve.** Extend existing GRSP native instrumentation/reporting when the REDscript generic backend needs new, provable measurements. Changes stay in the GRSP repository, and standalone GRSP remains functional.
6. **Documentation first, then app.** User asked to locate and preserve the original design and prepare a complete handoff before writing the unified GUI. Documentation alone does not satisfy app milestones.

7. **Shared managed runtime / CapFrameX:** user wants one bundled .NET runtime for all normal users. New app targets **.NET 10**, retains current CapFrameX 1.9.1.2 Beta (requires .NET 10 Desktop), and plans verified framework-dependent CET/GRSP manager builds with a private Desktop Runtime. No downgrade to earlier CapFrameX; private-runtime compatibility is not yet proven. See [runtime contract](../technical/SHARED-NET-RUNTIME.md).

## Existing original decisions retained

- **Two pages / three page-1 actions, one game-folder input.** Normal users are not confronted with profiler internals, extra path fields or an extra analysis button. Analysis starts automatically upon entry to page 2.
- The suggested **~5-minute gameplay capture** samples idle, running, jumping, driving and combat. It is **not** an enforced duration. Avoid menus, shops and vendors for representative gameplay.
- Both profilers' temporary native changes are reversibly installed and **restored before normal optimization generation**. Recovery is explicit and cannot be bypassed by a stale capture.
- **Vortex-managed one-ZIP overlay** is recommended, installed **after** original mods via user-defined conflict rules. Original mods remain installed. A manual install requires full backups before applying the ZIP. Owner exclusion/regeneration is an advanced safety feature.
- User's deep research stack demonstrates capability; other stacks should get **high-return-first**, generic-first best-effort optimizations. No universal performance/coverage claims or mandatory new semantic rules for modest mods.
- REDscript and CET inputs come from the current *effective* installed sources, not captures or past author ZIPs. Unsupported MO2 virtual source resolution must be identified instead of guessing.
- Package only game-root-relative runtime files; keep reports, source proofs, origin hashes and restore plans in the app workspace.

## Open / not user-approved implementation choices

- **UI toolkit**: .NET 10 Windows desktop is the selected target; CET and GRSP standalone managers still currently target .NET 8. WinForms is a likely low-risk default, but final UI visual design can evolve **without changing the two-page UX contract**.
- **Component binary delivery**: verified current standalone GitHub Actions artifacts or locally built, checksum-verified identical sources. No blind pin of stale TOTAL binaries. Prove legal and technical availability of optional CapFrameX bundling; linking existing copy remains supported.
- **REDscript optimizer algorithm**: rule families are hypotheses until source- and compile-proven. Do not claim a general generic rule works merely because GRSP flags a candidate.
- **Capture orchestration**: coordinate existing CET F10 (120s benchmark), user-defined F11, GRSP F11 and optional CapFrameX, but no verified one-key three-way session yet. Never overwrite bindings broadly to pretend synchronization.
- **MO2 and other virtual installs**, and every dependency-dependent native optimization path, remain unsupported until positively proven.
- **Gameplay validation**: none for the new unified app at documentation stage. CI, source validation, REDscript compile and in-game results are different evidence levels.

## Precedence / historical documentation caveat

The 2026-11-04 CET `docs/integration/UNIFIED-OPTIMIZER-MIGRATION.md` was drafted **before rediscovering** the October 16 original UX document. It documents useful component boundaries but its broader "Setup → Measure → Optimize → Results" concept must **not replace** the original accepted two-page workflow. This repo's unchanged original contract plus explicit decisions above are the correct product authority. The original document's wording about an established CET semantic library describes CET, **not a mandate to build a comparable REDscript library**.


## 2026-11-04 — explicitly approved CET-style UI refinement (implementation target)

The user explicitly selected the current **CET Runtime Profiler v1.3.4** look: nearly black (#080D12), dark blue panels, cyan (#36F4F4), green verified statuses, magenta accent, and compact source/capture sections. This replaces the initial light UI shell; it does not authorize copying component-owned installer or generator logic.

- **Two pages, no visible tabs.** Page 1 has source selection, brief capture/keybind instructions and buttons: **Install Profilers**, **Start Cyberpunk and Record**, **Collect Results and Restore**, then a **Continue** gate. Page 2 shows **Analyze**, **Generate Optimization Pass**, **Open Results Folder**, with separate scrollable CET and REDscript findings. Page 2 Analyze is now **explicitly user-approved instead of auto-analyze on arrival**.
- The main source chooser accepts a physical game root or **direct live CET \`mods\` directory**; installed CET owner identity requires immediate \`<mod>/init.lua\`. Direct physical CET path may infer game root only through exact known game-relative parent chain and executable proof. An external/MO2 mods folder may be inspected but must not be misrepresented as the effective game tree or a valid target for profiler install, REDscript scan or pass generation until its VFS precedence and game association are proven.
- Key status is **read-only evidence**, not static green checkboxes: CET \`CETProfilerControls/CETProfiler_Toggle\` F11 or existing custom, GRSP's native F11 start/stop, and CapFrameX \`CaptureHotKey\` from its actual portable/config settings. Preserve custom CET/user CapFrameX controls; do not silently rewrite them. CET's component-owned installer seeds missing F11; bundled CapFrameX with an approved F11 preset still requires exact package and distribution validation.
- The capture instructions specify 5+ minutes as **guidance**, stress idle, movement, driving, heavy combat; avoid menus, map, inventory, CET UI and missions, remain in-world. After STOP, **close game, collect verified current evidence and restore both profiler layers before Continue**.
- The combined ZIP is named \`G-Cyberpunk2077-{timestamp}_Optimization_Pass.zip\`. Display a Vortex-winning overlay instruction. Owner exclusions must be **dependency-aware**: removing one owner's files is not guaranteed safe if remaining generated owners depend on its scheduler/runtime or cross-owner glue. Required 0-Engine/G-REdruntime files remain only when corresponding accepted optimizations require them.
- CapFrameX is an optional frame-time companion, **not a game profiler installed into red4ext/CET**; new app can launch its approved portable deployment once provenance/private .NET compatibility is proven, otherwise link the existing copy.


## 2026-11-04 — CET-style Page 1 source picker and capture instructions

- **Game folder / Cyberpunk 2077** is the ordinary mandatory picker; inside that same box Normal/MO2 toggles source mode. MO2 mode adds instance directory and profile selection and inspects the enabled physical mod files from its configured locations. It does not require copying the MO2 stack.
- Show source presence for **0-Engine, r6/scripts, red4ext/plugins** across known physical and selected MO2 sources, with a direct public link if 0-Engine is missing. A source-path existence check is **not** proof of 0-Engine runtime compatibility.
- The large capture instruction panel uses cyan heading, yellow operational instructions and green 5–10-minute gameplay stress emphasis; F11 checks must reflect real provenance. Shorter completed captures are permissible; do not reject solely by duration. Avoid menus/UI and loading screens. The capture collection/restore remains evidence-gated.
- Display readable disabled workflow buttons and an enabled pink Continue button only after verified current-session capture archive **and both owner restores**. No premature enablement.
- **CapFrameX:** explicitly bundled, no ordinary path selector; only exact verified upstream artifact, runtime/rights and reversible config checks authorize packaging. Current development shell has no CapFrameX binary and reports that honestly.


## 2026-11-04 — refined Page 1 and MO2 mode
- Normal setup shows only Game folder /Cyberpunk2077; the Normal/MO2 selector is inside the game source box, not a new page.
- MO2 additionally selects instance and actual profile; read-only physical source map tracks game files, enabled mod packages, Overwrite and loose-file precedence. Never infer runtime VFS equivalence, RootBuilder behavior or verified game launch from files alone.
- Show 0-Engine path/mandatory Nexus link when missing, r6/scripts and red4ext/plugins statuses. Preserve owner/dependency evidence; no source copying.
- Use CET dark/cyan colors, larger yellow/green instructions, legible gray disabled controls, and magenta Continue only after verified capture plus restore.
- CapFrameX will be bundled, so remove ordinary external executable path chooser. Do not claim binary staged before actual vendor provenance, licensing, F11 settings and .NET 10 compatibility checks.


## 2026-11-04 — final CET-style first-page layout correction

- User rejected the narrow inline Normal/MO2 radio pair and the tall scrollable first page shown in the v1.1.0 build. **Normal and MO2 are now equal-width full tabs inside the Game folder box**; selecting MO2 replaces the normal game-root input with the MO2 instance/profile controls. They are views of one source-selection step, not additional pages.
- **Page 1 must not scroll at the reference 1010 × 790 client size**. Compact the path field back to CET-like height, remove excess space/margins, and allocate three fixed screen sections above the workflow buttons. Page 2 keeps its scrollable reports.
- Keep 0-Engine, r6/scripts and red4ext checks beneath the active tab; keep mandatory missing 0-Engine link, F11 source-status truthfulness and the existing read-only safety gates. No cosmetic change authorizes installing or modifying user mods.
- Preserve version **v1.1.0**; this is a design correction, not a new product version.


## 2026-11-05 — CET-path cleanup, source counts and MO2 arbitrary storage

- Match the CET Setup path control exactly in scale: compact 26px textbox and separate 92x30 flat Browse button with an intact outline; avoid nested TableLayoutPanel clipping.
- Make setup text white except the green REC0RD 5–10 MINUTES and IDLE–HEAVY COMBAT emphasis. Keep the AVOID MENUS warning uppercase but white; make F11 key rows informational without unavailable/warning badges. Do not misstate bundled presets as verified actual installations.
- Rename the first workflow status to **PROFILER NOT INSTALLED**, while keeping actual installation and capture actions gated until component provenance and lifecycle tests are complete.
- Show **Game found · N CET mods · M REDscript mods** in Normal and MO2 source views. CET counts installed immediate owners with init.lua. REDscript counts are *estimated source-owner groups*, not .reds file counts: physical top-level r6/scripts owner directories and standalone root .reds sources; in MO2, winning enabled package owners plus physical owner groups. No claim of authoritative author identity when files have been flattened in a manual install.
- With no valid path, say only **Select the Cyberpunk 2077 Game root - /Cyberpunk2077** in source status. Keep the 0-Engine, r6/scripts and red4ext/plugins check lines plain white even when missing; keep the dedicated 0-Engine dependency link.
- MO2's **game executable path must be visible inside MO2 mode as an independent first input**; no user should need to return to Normal mode to set it. MO2 instance and its configured Mods/Profiles/Overwrite directories may be entirely unrelated paths/drives to each other and to the game. Read arbitrary configured paths from ModOrganizer.ini and profile modlist.txt. A physical game without installed mods is valid when the selected MO2 profile supplies them. Do not presume successful runtime USVFS/RootBuilder integration.


## 2026-11-05 — MO2 source folders by content, not fixed layout
- Explicit user correction: replace MO2 instance/profile setup fields with **up to four arbitrary physical folder selectors** and four read-only checkboxes: CET ASI (`cyber_engine_tweaks.asi` / `CET.asi`), CET Lua mods (`mods/<owner>/init.lua`), REDscript (`r6/scripts/*.reds`), and RED4ext native plugins (`red4ext/plugins/*.dll`).
- One selected folder may satisfy all four; if two satisfy all, hide slots 3–4; similarly 3/4. Expose the next slot only while something is missing; no fixed requirement to split components into four directories or place them under the game root.
- Detection is based on bounded physical file walking (no modification, no copying). Read-only checkboxes reflect discovered content, **not** that the files belong to MO2's enabled profile or that RootBuilder and VFS inject them at runtime. This provenance remains unverified and generation stays gated.
- Existing MO2 INI/modlist-based source reader remains available as a separate code service but is not silently substituted for arbitrary-folder discovery. Game root is still a future prerequisite for profiler launch/install; four source fields are not redefined as a game-executable selector.
- Normal mode and Page 2 stay unchanged; Page 1 stays non-scrollable; v1.1.0 remains the requested display version.


## 2026-11-05 — MO2 configuration and profile replace arbitrary-folder check workflow
- User superseded the standalone 1–4 folder-only checks. Require MO2 application folder first. Portable uses ModOrganizer.ini in that folder. Global instance config uses %LOCALAPPDATA%/ModOrganizer/<instance>/ModOrganizer.ini. Multiple instances require explicit selection; manually selected custom instance paths are supported.
- Read configured Mods, Profiles and Overwrite paths from the chosen INI regardless of drive. Show actual game root separately, auto-resolved Mods folder with an explicit Browse recovery override, and selected active profile dropdown. Only honor selected_profile if that profile exists.
- Do NOT check CET ASI, CET mods, REDscript or RED4ext from loose files/init.lua alone. Require selected MO2 instance and profile, modlist enabled list, all enabled packages, and the winning game-relative file map first. MO2 profile is a fifth prerequisite check.
- Current MO2 loose-file evidence is not proof that Cyberpunk launched through MO2 or of RootBuilder/USVFS, archive precedence and file exceptions. No profiler install or optimization generation enabled until source equivalence and restore are verified.
- Removed superseded independent folder-only scanner to avoid parallel source inventories. The active profile resolver is authoritative for the UI. Normal mode and two-page layout retained; version stays v1.1.0.


## 2026-11-05 — Page 1 status colors and capture instructions refinement
- Normal and MO2 paths use a distinct magenta outline when the selected location is missing or invalid. Confirmed paths revert to CET's subdued outline; no source is admitted based on cosmetics.
- Source-selection prompt directly under the path is amber/yellow until a valid source is selected. Normal component paths and 'NOT FOUND' text stay white; only independent X / check glyphs are red / green. Five MO2 component checks are owner-drawn with the same glyph colors rather than native OS black-gray checkboxes.
- Page 1 instructions remove the redundant CET, REDscript and CapFrameX F11 status rows entirely; retain the human-readable F11 start/stop instruction. Start with 'Provide the path to the live Cyberpunk 2077 game files', then start game, unbind F11 collisions, and confirm Cyberpunk is the only process selected in CapFrameX.
- Keep 'RECORD 5–10 MINUTES IN THE CITY' green; idle/movement/driving/heavy-combat line becomes yellow; the uppercase avoid-menus instruction and all remaining explanatory text stay white. Increase instruction section to display all three bottom lines without clipping.
- The profiler status remains 'PROFILER NOT INSTALLED'. Step two explains recording with all profilers; step three reads 'COLLECT RESULTS / RESTORE — Close game, collect results and continue'.
- MO2's missing 0-Engine notice appears above the five source checks, not below them. Page 1 stays non-scrollable at the reference 1010×790 client size, while Page 2 remains scrollable. Version 1.1.0 retained; no backend authorization implied.


## 2026-11-05 — Page 1 post-v1.1.1 visual corrections
- The unresolved Normal/MO2 path outline must be a genuine **one-pixel** magenta border, not a four-pixel magenta-filled panel. Confirmed source paths use CET's subdued border.
- A valid game root with missing 0-Engine must display `Game found; 0-ENGINE MISSING!!` in amber next to the 0-Engine Nexus link. The normal game status changes to white with mod counts only after 0-Engine is present; a missing game root remains amber and has no 0-Engine link.
- Normal path checks for 0-Engine, r6/scripts and red4ext/plugins must show their path/missing text **white** and the actual status glyph **red X** or **green tick**. Render the full mixed-color result together to prevent hidden glyphs or clipping.
- The first line of the instructions receives a green tick whenever a physical Cyberpunk game root is verified. This indicates the game path only, not profiler or MO2 VFS runtime validation.
- Add a **REFRESH** button at the top right of Page 1 to reread physical paths and MO2 configuration/profile, with no writes to game files.
- Complete step 3 wording with explicit restoration before continuation. Keep v1.1.1 and component boundaries; do not unlock profiler install/capture/restore/generation from these visual changes.


## 2026-11-05 — physical baseline, MO2 absence and portable distribution

- **Every MO2 source projection includes the live physical game as lowest precedence**, whether MO2 is empty or enabled. Enabled MO2 package files override same game-relative paths based on profile priority; preserve winning owner and file path for source drift/optimizer protection. Disabled MO2 packages never enter.
- **Missing `modlist.txt` is not proof of active MO2 packages**: exclude all MO2 packages and MO2 Overwrite; physical game remains analyzable as its own source. An existing readable list with zero enabled packages is a valid 0-enabled profile. Corrupt/unreadable/incomplete enabled lists also exclude all MO2 layers (no guessing); show missing verification separately from directly found game files. MO2 VFS/runtime winner proof is still pending before MO2 optimization.
- Package unified `v1.1.1` as one clean folder like the CET profiler: root native launcher plus Windows shortcut, managed application and exactly one private .NET 10 runtime under `app/`, `RESULTS/` folder and `VERSION.txt`. The root launcher is a source-built small Windows executable, not another profiler or managed runtime. Never ship third-party binaries before approval/provenance. No new release tag or change to CET/GRSP component versions.


## 2026-11-05 — superseding MO2 launch decision: defer actual integration

- The first usable unified optimizer **supports directly installed game sources first**. Disable the MO2 tab in the shipped shell, with "COMING LATER" status; do not create an independent MO2 profiler/Resolver workflow before direct-install integration is complete.
- Preserve only **passive read-only MO2 directory discovery** for future work: user-selected game folder + MO2 application folder; auto-locate portable or LocalAppData instance `ModOrganizer.ini`; offer instance/profile dropdowns only when MO2 mode is eventually enabled. INI supplies Mods/Profiles/Overwrite, profile supplies `modlist.txt`; no ordinary manual path for either INI, modlist or Mods folder.
- The current MO2 code can still prepare a candidate file-winner map for tests, but **that is not a supported installed-source provider in the initial app**. Keep profiling, restore, Resolver and ZIP generation limited to physically installed game sources until MO2 injection/runtime equivalence has explicit tests and approval.
- This supersedes earlier November 5 UI plans for active MO2 checks and manual Mods-directory override. No version bump for the shell; v1.1.1 remains the current development version.


## 2026-11-05 — Page 1 final copy and verified profiler status rows

- Instruction copy: “Make sure Cyberpunk 2077 is detected in CapX and is the ONLY process.” Use **heavy combat**, with fighting police as a fallback when longer combat captures are difficult; “If you die, stop recording with F11”; “return to this screen.”
- Last box heading is **PROFILERS NOT INSTALLED** until authoritatively verified and **PROFILERS INSTALLED** when the two required native profilers (CET+GRSP) are installed; CapX is optional and has its own independent verification state. Remove scheduler/install/restore workflow prose from this box. Rows only **CET**, **REDscript**, **CapX** and their detected/preset hotkeys. No green tick for mere filesystem presence, game executable, settings, or unverified backend response; a tick requires verified installed payload.
- Existing GRSP fixed F11, CET stored capture binding, CapFrameX portable/configured CaptureHotKey are independent key sources. The initial portable shell lacks staged profiler/CapFrameX binaries, so key display may show the expected F11 preset *without claiming detection*. When CET or CapX has a verified non-F11 setting, show a **yellow advisory** that REDscript currently hardcodes F11. **Do not block install/capture/gameplay or controlled CET F10 tests for a key mismatch** and never overwrite user keybinds.


## 2026-11-05 — explicit correction of Page 1 changes

- **Do not replace the three original cyan/gray install, start and collect workflow descriptions.** Keep them on the **left** of the third box. Place the already-approved three CET/REDscript/CapX installation checks and capture keys on the **right**. Leave status heading and keybind-warning semantics intact.
- Add one line to the existing F11 instruction: **“You will hear Start/Stop in CapX and RED. CET Start displays a UI timer in the upper left corner.”** Enlarge the second box by one text row instead of shrinking its contents. Restore **“Shorter captures also work.”** No other capture copy changes.
- MO2 remains disabled and will need a separate fit review before reactivation; do not adjust its backend/source handling as part of this UI correction.

## 2026-11-05 — one root launcher; artifact-based integration

- Portable root contains only the `G-Total-Optimizer.exe` launcher, not an additional `.lnk` file. The shortcut was redundant and may contain the Actions runner absolute target. Existing user's local shortcut may be deleted manually; future packages must not stage it.
- CET and CapFrameX reported ready; GRSP remains paused by user. Prefer reproducible GitHub Actions import/verification of each component's own exact upstream artifact, manifest, hashes and payload, not manually copied build outputs. Only the unified orchestrator coordinates lifecycle. Do not claim components integrated until verified end-to-end.


## 2026-11-05 — one-runtime CapFrameX test explicitly authorized

- User rejected any default system-wide .NET or VC++ installation and requested a runnable CapFrameX test linked to the unified application's **private .NET10 runtime**. Preserve one shared runtime design and a portable ZIP. No switch to system-only framework-dependent release.
- Build source-owned CapFrameX dependency-check assembly against exact pinned vendor 1.9.1.2 beta source, preserving vendor original `CapFrameX.exe` and all native capture binaries; recognize private framework tree and app-local CRT, preserve original behavior without the unified private-runtime environment. Use separate test-only CI, not the ordinary shell release workflow. CapFrameX already requires UAC; use an elevated native CapFrameX launcher before setting the private process environment.
- Accept test only after genuine Windows launch/capture and restored source safety, not from compile or file topology alone. CET/GRSP integration and MO2 still deferred or independently staged.


## 2026-11-05 — PresentMon compact default, all-three test

- User explicitly requests **CET + GRSP + PresentMon in the same first gameplay test**; no CET-only/PresentMon-only validation as evidence of integrated readiness. CapFrameX full suite is not required in the normal ZIP when PresentMon yields sufficient frametime data. CapFrameX can remain an optional external comparison/advanced provider.
- Continue targeting **one private .NET 10 Windows Desktop Runtime** (no mandatory system .NET or Visual C++ install). In a disposable CI test workspace the original CET and GRSP managers can be source-retargeted and rebuilt against net10.0-windows, sharing the runtime with the unified app. Native CET ASI and GRSP RED4ext DLL remain intact. Do not claim standalone product repos have been retargeted or change their release policy without independent evidence.
- PresentMon CLI 2.6.0 is pinned by release SHA and packaged unmodified. User F11 remains authoritative and unchanged. Before normal UI install is enabled, verify the interaction of PresentMon's `RegisterHotKey`, CET's game keybind and GRSP's `GetAsyncKeyState`; run real simultaneous capture and recovery. Generate graphs from collected per-frame CSV in the unified reporting layer only after validated process/time alignment.
- Test-only workflow and developer script are **not** a substitute for the original two-page UI nor proof of game/profile/restore correctness. Ordinary UI remains fail-closed pending actual capture/restore, current-session evidence, and cross-profiler contracts.

## 2026-11-05 — unified app test-bundle version v1.1.2

- User explicitly chose **v1.1.2** after confirming the main unified application was already v1.1.1. Apply version to the unified shell and triple-profiler test, including embedded executable metadata and CI artifact names; retain CET v1.3.4 and GRSP v1.0.0 independently. No gameplay/restore acceptance is implied by the packaging version bump.

## 2026-11-05 — stop standalone harness loop, implement real app

- User explicitly ordered integration of **both CET and GRSP now** into the unified application's existing two-page workflow; PresentMon is the initial lightweight frame provider and may be replaced if it fails real-game validation. No separate test-only GUI. Rebuild managed CET/GRSP managers for **.NET 10** using authoritative source in disposable CI; retain original native binary identity and owner-managed install/restore. Existing CapFrameX-heavy Page 1 wording can be updated later, after the core lifecycle is functional.
- Real GUI installation, launch and collection are authorized, with strict hash/owner-status preflight, session journal and owner-controlled restore. **Only** completed CET+GRSP captures with confirmed restoration unlock Page 2. No optimization ZIP button until CET Resolver and REDscript source generator, compilation, dependencies and actual output have been validated. No MO2 until its separate integration milestone.

## 2026-11-05 — explicit-only GCO version policy

- The user explicitly authorized GCO v1.1.5. **Never increment the GCO version again unless the user explicitly orders it.** Bug fixes, CI runs, packaging corrections and ordinary development retain their current version. Do not alter CET, GRSP or PresentMon versions implicitly.


## 2026-11-05 — explicit PresentMon collection boundary

User clarified the first-page three-button workflow: bundled optional PresentMon starts on button 2 without its own F11 hotkey, continuously records Cyberpunk, and stops/collects on button 3 COLLECT RESULTS. It requires no separate config or setup step. CET/GRSP remain instrumented after collection until CONTINUE, which invokes their authoritative original-file restore. A missing PresentMon CSV is explicitly skippable and never silently treated as successful frametime analysis. Do not make native profiler restoration responsible for PresentMon shutdown. Keep one game root, two pages, no unrequested version bump or unmeasured performance claims.


## 2026-11-06 — GCO identity, first-run simplification and design resources

- User approved a branded ZIP artifact named `Gs-Cyberpunk2077_Optimizer.zip` with one adjacent `START-HERE.txt` and an app folder named `G's Cyberpunk 2077 Optimization`, whose root executable is `Gs-Cyberpunk2077_Optimizer.exe`. Retain exactly one private shared .NET 10 Desktop runtime and existing internal owner managers. Existing GCO product version remains 1.1.5 until explicitly bumped. The GitHub repository was **renamed by the owner**, verified at `giorgioherma/Gs-Cyberpunk2077_Optimizations` on 2026-11-06. Public exe/ZIP continue using the previously approved `Gs-Cyberpunk2077_Optimizer` filenames; no duplicate repo.
- Approved visual direction: yellow (#FFF000) primary, near-black (#070B0D) content, cyan (#00E2F2) limited to outlines/technical accents, official user's hybrid GOT LAG ICO for public app icon, Night City header and V sidebar where space allows. Less instructional text in the application: compact task-oriented captions, explicit current action, truthful native/tool status, full instructions in the outer artifact guide and optional Help. No fake metrics, functionality claims or nonworking art-button images.
- Keep WinForms for incremental refresh as a risk-minimizing UI choice; evaluate WPF only if an actual measured/implemented WinForms design requirement cannot be achieved. Preserve one source path, two pages, source-owner CET/GRSP transactions, optional automatic PresentMon, Collect before Continue, and current-source optimizers.


## 2026-11-06 — Completed capture navigation is one-way until deliberate new-session UX

- Once Continue has verified source-owned CET/GRSP restoration and committed a `Restored=true` collection index, Page 2 is the active workspace. Remove the misleading Back-to-Setup control that loses the owner journal and strands the user after refresh. Refresh completed evidence from its verified session ID, not the deleted owner install journal. Original owner restoration and evidence integrity checks remain mandatory.
- Do **not** add a speculative, dangerous reset-stack button as a quick workaround. A user-facing new-session/reset control will be designed separately with explicit scope and source-owner recovery checks. Do not claim the CET Resolver or REDscript generic generator are integrated merely because Page 2 now reliably displays collected archives. GCO version remains v1.1.5.

## 2026-11-06 — approved Page 1 and independent CET/GRSP profiling (supersedes earlier both-required gating)

[Full implementation contract](../design/PAGE1-APPROVED-IMPLEMENTATION-PLAN.md). **DECIDED, NOT IMPLEMENTED.**

- Use taller selected Page 1 concept with full original V long ink trail and **keep BLUE skyline** upper-right (do not replace with yellow). Original aligned hand-edited black/full-color 743×2117 pair, no regeneration; real three capture-stage buttons. Add small cyan Refresh in Game Root and **global** CET/REDscript owner counts for Direct and MO2, never claim exhaustive detection.
- V black before Start; after Start irregular bursts, occasional **30ms microflashes**, longer forced black pauses; keep flashing and increase intensity throughout actual Collect; full color permanently when CET-or-GRSP verified capture is collected and Continue available. No fake loading percentage or timer-driven success.
- CET and GRSP admitted **independently**. GCO CET route requires actual 0-Engine validation (standalone CET core-only is optional); GRSP-only requires RED4ext/REDscript-side environment but not CET/0-Engine. Both supported; neither blocked. No other framework or mod count universally required. PresentMon optional and frame-only capture cannot unlock Continue.
- Continue available after at least one verified current-session CET **or** GRSP result. Missing **expected** domains are listed in **Return / Continue popup only on Continue click**, not immediately on collection. Return stays Page 1 with valid proof and Continue for safe retry. Continue accepts partial domain results but must verify all installed profiler original-file restoration before Page 2.
- MO2 integration **deferred**. Standard future UI needs game root + one MO2 app path; discover portable/LocalAppData INI, profile `modlist.txt`, arbitrary configured Mods/Profiles/Overwrite. If extra external categorized folders exist, offer separate **Custom/Advanced source-path repair window**, prove active profile/priority and show incomplete inventory rather than assuming it sees all user mods. Runtime capture location/USVFS/restore must be proven. Preserve MO2 feature gate disabled.
- Source work pending: `UnifiedProfilerWorkflow` both-owner assumptions, `CollectedCaptureStore.HasAny` including PresentMon, conditional per-domain prerequisites/partial-retry UI, animation and tests. Keep GCO v1.1.5, sole integrated workflow and owner logic.


## 2026-11-06 — User-edited project Rule 4: authoritative component ownership

The user explicitly confirmed the following final ownership wording, including the `0-Engine v0.17.2+` prerequisite and that G-REDruntime's shared services are **for REDscript**. This supersedes any earlier suggestion that the REDscript generator should move into GRSP.

4. **Respect component ownership.**

- **CET:** Owns its profiler, existing Lua Resolver/Generator, native optimizations and G-GC. Preserve all existing work. CET profiling requires 0-Engine v0.17.2+.
- **GRSP:** Owns REDscript profiling and instrumentation.
- **GCO:** Owns unified analysis, cross-domain coordination, new REDscript Resolver/Generator, UI, reporting and final optimization ZIP.
- **G-REDruntime:** Owns shared framework services for REDscript, included only when required.
- **One combined optimization plan**, using CET and GRSP evidence and live sources. Preserve CET's authoritative generation pipeline and REDscript compilation/source-safety checks. Never duplicate implementations.

**Implementation interpretation:** GCO is the central **cross-domain decision and plan coordinator**, not a replacement CET Resolver. CET continues its source-owned candidate selection and code generation; any additional CET integration interface must be added at CET's source, not recreated or patched downstream in GCO. GCO owns the NEW REDscript optimizer and full REDscript source safety, optional G-REDruntime dependencies and final package compatibility. Independent CET-only and GRSP-only captures remain valid; PresentMon-only remains insufficient. The supported GCO CET route checks the required 0-Engine version; standalone CET core-only operation without 0-Engine is a separate capability and does not weaken this user-approved GCO requirement. No code changes in this documentation update.

## 2026-11-06 — WPF selected for GCO interface (explicitly supersedes WinForms preference)

- The user elected to **replace GCO's WinForms UI with .NET 10 WPF now**, rather than styling a frontend that would later be migrated. The old artifact need not be preserved in full; reuse the **same existing** `main` Windows workflow and bundled .NET 10 Desktop Runtime. CET/GRSP standalone managers, native payloads, GCO Core, PresentMon and all source-owner boundaries stay intact.
- WPF provides native layout, real controls, original layered V black/color glitch and BLUE skyline. No fabricated progress or results. The old `MainForm.cs` is excluded from build; WPF window is authoritative. `UseWindowsForms` remains only for the built-in folder picker during transition, without shipping a second runtime.
- A passing [CI #42277190679](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/42277190679) verifies app compilation, original owner managers, 179 contract tests and packaged CLI, **not** WPF gameplay/visual acceptance. Original user images are still not source-committed. Existing CET-only/GRSP-only backend changes and MO2 remain pending. No version bump; GCO v1.1.5.

## 2026-11-07 — Compact Capture Page sizing and type policy (first test baseline)

- **WPF normal window 1220×680 DIPs; minimum 1040×600 DIPs.** Smaller usable content scrolls; keep console/Continue visible, full portrait width and aligned original color/black V images. Final size may be refined following actual screenshots.
- **Rajdhani** body/instructions/status, **Oxanium** title/tabs/buttons, both OFL-licensed and embedded into GCO without a second runtime; original full blue-to-dark skyline retained and lowered below reserved main logo title band.
- Section order: center sloped tabs → GOT LAG? headline/subtitle → Game Root/readiness/counts → CAPTURE WORKFLOW label → three tall image/title/instruction/interactive-button cards → concise bottom console and distinctive cut-corner yellow Continue. Remove duplicate instructions/status boxes; WPF Path + gradient brushes provide irregular cut-corner borders and three cyan slashes (no PNG border requirement). No non-visual backend behavior changed by this decision.
