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

## 2026-11-07 — Current WPF sizing and typography status (supersedes prototype sizes)

- User selected **1280×760 DIPs normal**, **1080×640 DIPs minimum** for enough room; replaces 1220×680 and 1040×600 historical design/prototype values.
- Source fix: explicitly bound Game Root to its contents (108 DIPs; decorative Path must not expand the StackPanel); separate larger WHITE `/// GAME ROOT / CYBERPUNK 2077` above border; align frame with end of blue header. Bigger yellow **GOT LAG?**, more spacing, remove tab 1/2, steep angled tabs, center all three workflow icons, tighten cyan ///, dark background/yellow-line scrollbar. Buttons remain inside their cards, Continue remains distinct. GCO frontend only; no source-owner backend workflow changes.
- Font experiment continues: Rajdhani body and Oxanium headings/buttons stay temporarily; the user has **not finalized typography**.

## 2026-11-07 — Neon frame direction, first implementation

- User requested a **thicker glowing outer frame** at the client edge, a tight 2nd border inset with #060D0F gap joined visually to the under-logo rule, accented/pimped corners, and an original V image inset slightly into its own cut frame. All major page boxes use the same **crisp 45°** corner cut, scaled consistently with window resizing rather than being skewed by a stretched WPF Path.
- Current chosen first pass: shared actual-size StreamGeometry, **14-DIP base corner cut**, ~3.6-DIP outer stroke plus static translucent glow/corner fins, ~1.8-DIP inner stroke inset 10 DIP, ~2-DIP individual box strokes. Standardized rendering for cards, Game Root, console, Continue, Page 2 and action buttons; original image pair remains unchanged. Final glow/art intensity awaits actual screenshot.
- Capture/Analyze tabs **equal size** and labeled only **CAPTURE / ANALYZE**; preserve existing evidence-locked navigation. This is a WPF presentation change, not an authorization or owner restore change; independent CET/GRSP backend and 0-Engine version checks are still pending.

## 2026-11-07 — Explicit unified GCO package version v1.1.6

- The owner explicitly ordered **one version increment for the whole unified GCO package**: `1.1.5 → 1.1.6`. This supersedes the older `Keep v1.1.5` historical statements, without changing the explicit-only version policy.
- Synchronize `Directory.Build.props` product/assembly/file/informational versions, WPF window title and header, source-owned integrated builder/version-check, `VERSION.txt`, `START-HERE.txt`, native launcher Windows resources (generated from the unified version), and the single canonical Windows workflow contract. Preserve the branded artifact/executable names, app folder structure, all existing user PNGs and the original two-page workflow.
- **CET v1.3.4, GRSP v1.0.0 and PresentMon v2.6.0** remain independently versioned and are not promoted or modified. The version bump does **not** certify gameplay, profiler capture/restore, border visual acceptance or optimizer generation. Do not bump GCO again without another explicit owner request.

## 2026-11-07 — Single border canvas / no resize flicker

Owner selected the Regular 1280×760 PNG as the only full-window master at all sizes, continuing to use `assets/ui/elements/GCO_Border_Regular_1280x760_FINAL.png`. The owner will replace this file with corrected art and remove Small. The app must not switch raster sources while resizing; use the single image loaded at startup with high-quality `Stretch=Fill`, accepting the negligible aspect ratio difference. The Small PNG is not packaged. Preserve app v1.1.6 and all other visuals/native functionality.

## 2026-11-07 — Explicit unified package v1.1.7

The owner explicitly ordered **rebuild + one version increment**, `v1.1.6 → v1.1.7`; no unrelated fixes or design changes authorized. Maintain single .NET10 runtime, current V geometry, sole Regular PNG, independent CET/GRSP version ownership and one Windows CI workflow. A browser malware warning on the ZIP prompted the request, but bumping the version alone cannot prove the binary safe or clear a security detection.

## 2026-11-08 — Explicit unified package v1.1.8

- Owner ordered one version increment `v1.1.7 → v1.1.8`. This authorizes a GCO product/package metadata change only; it does not authorize image/layout or profiler behavior changes.
- Keep product, assembly, file and informational versions, WPF title and header, Windows CI checks, builder and generated `VERSION.txt` / `START-HERE.txt` aligned. Preserve `Gs-Cyberpunk2077_Optimizer` package and launcher filenames, one .NET10 runtime and one canonical Windows workflow. CET v1.3.4, GRSP v1.0.0 and PresentMon v2.6.0 are independently owned and unchanged.
- Pre-bump v1.1.7 CI [#42438748376](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/42438748376) SUCCESS; new v1.1.8 CI must be checked independently. Do not bump again without the next explicit request.

## 2026-11-09 — Pure black and owner-painted V transition (explicit user decision)

- The page, Game Root background and skyline fade endpoint are pure #000000. The owner replaced assets/ui/images/TrueHeader_GCO_page1.png in place; it is now 1870x652 RGB and keeps the 174-DIP skyline slot.
- New owner V pair: V_background_1_color.png and V_background_1_black.png, both 482x928 RGBA. Retain the old left/top anchor and six-DIP bottom overlap. Reserve the same old narrow sidebar width at 743:2117; independently scale the new full pair by height at 482:928 so the painted transition extends rightward instead of moving controls or cropping the source. Preserve source registration and capture-stage black/color animations.
- Remove the former generated alpha mask, six-layer Round-6 WPF halo/arch/skirt/fade and old right-edge portrait outline; the PNG pair now owns the transition. Keep single Regular outer border, crisp inner chassis, branding/ICO, same one-runtime/two-page UI, source-owned profilers and GCO v1.1.8. Verify integrated Windows WPF render; no gameplay or screenshot acceptance is implied.

## 2026-11-09 — Owner-approved freer Page 1 composition

- Owner approved the new true-black/color V art and blue skyline after native Windows screenshot. Keep those bitmap bytes and their current height/left registrations.
- Allow the original full-width portrait/puddle to visually **cover only the thinner inner-chassis frame**, never the single outer border artwork; the right-side UI still paints on top of V's wider fade. The application and profiler workflow remain unchanged.
- Float the two equal-width CAPTURE/ANALYZE state tabs in the clear header band **above the inner chassis**, with a modest steeper slant, preserving their state cues and Page 2 gate. Move GOT LAG to the first content row at the top of the inner area. Slightly move the Game Root heading/field panel left toward the buildings in V rather than starting every section at one vertical line.
- Eliminate the console's decorative box and second vector slash; place its status text on the transparent background aligned near the slash painted in the V image, while retaining the hidden-until-needed RESTORE control and Continue separately. Preserve focus/hit-test and minimum/responsive layout. No version bump or native profiler/installer edits.

## 2026-11-09 — Owner bottom puddle-only overflow, correcting full-V overpaint

- User supplied exact owner image `assets/ui/images/The overflow.png` (**365×49, RGBA**) containing the bottom transparent/black puddle with some yellow highlights. Preserve it unchanged and package at the same path.
- Move the full V by **+2 DIPs** horizontally (from x=26 to x=28 at default drawing size) to expose the thin inner border at x=27, leaving 1 DIP clearance. Preserve V top, height, 482×928 art, two-stage black/color animation, skyline, right content and the entire outer border. Do **not** duplicate or algorithmically recolor V.
- Put **complete V underneath** the thin inner chassis so its yellow lines continue uninterrupted, then overlay only the owner 365×49 puddle strip **above** the thin chassis and **beneath** the outer frame. Align strip to the same left and bottom and scale by V height/928: width=365×scale, height=49×scale. Root z-order: ContentRegion 12 (skyline behind V, controls above V inside it), inner frame 13, puddle 14, upper tabs 22, outer raster 30. No hit testing on strip. Supersedes the previous decision allowing the complete V to paint over the inner line; the owner now explicitly wants only the puddle crossing.
- Preserve product **v1.1.8**, canonical Windows CI, one .NET10 runtime and current CET/GRSP/PresentMon payloads. The user's later Windows screenshot will decide pixel alignment acceptance independently of passing CI.

## 2026-11-09 — Cleaner Page 1 headline, game path and header controls

- Owner requests the **GOT LAG? heading offset right**, with the **Game Root heading and box returned to original right-content X alignment** (undo the earlier -11-DIP left shift). Maintain the V, owner puddle strip, outer/inner border, skyline, tab positions, console and titlebar as in verified build 177.
- The actual game-path input is a **normal rectangular WPF TextBox**, not a chamfer. Inset it to start past the 14-DIP corner cut, and trim its right edge so the separate OPEN button has more breathing room. Set Game Root interior side margins 22 DIPs and OPEN gap 16 DIPs, preserving input caret/focus, path change, browse/refresh logic and status rows.
- Move REFRESH out of Game Root to the **top-right header band**, retain the v1.1.8 version label, and remove the Reduce flashing checkbox and its event handler. Keep the existing V animation/state behavior (only remove the user switch). Remove both visible **MO2 LATER** / **MO2 deferred** placeholders, without removing future MO2 architecture or dynamic game inspection support.
- User queried artwork policy for the existing three cyan step icons and proposed yellow folder/path/button graphics. Recommended native WPF vectors (sharp at all sizes, state tinting) for simple icons, PNG only for bespoke detailed game art. No unapproved icon redesign or PNG modification this pass. GCO v1.1.8, owner profilers, runtime and single Windows CI unchanged.

## 2026-11-09 — Owner's latest header/hero/card/spatial-neon adjustments

- Keep owner art files, V/puddle z-order, logo reservation and all profiler code unchanged. The header's temporary `GCO` logo lettering is removed; the blank left slot remains for owner's real logo. PresentMon is removed from **header tagline only**, which now reads **CET and REDscript Optimization** in cyan. Align header title and subtitle together, adjust REFRESH + v1.1.8 side by side in upper-right space.
- GOT LAG heading **and its description** are an indivisible section, inset together +62 DIPs from right-panel content X (previously heading only +22, so +40 additional). Center Game Root's real content vertically without changing its frame/height or its original X. Path becomes legible via smaller font, less padding, 32-DIP edit height and restoring visible root prefix after focus lost (no input edits or forced caret reset while typing). Prerequisite yellow must match `#FFF000`.
- Workflow card step numbers remain LEFT, but icon, title and body text are CENTER. Reposition Continue +25 up / +10 left with existing workflow safety. For neon **box/frame strokes**, use single scene-position-aware V yellow → skyline cyan field, weighted lower-left yellow vs upper-right cyan, not identical local 50/50 gradient stops on each box. Outer owner border stays original. No version bump; no new icons until further owner decision.


## 2026-11-10 — Owner-approved GCO v1.1.9; central version authority

- Explicitly bump **unified GCO v1.1.8 → v1.1.9**. Centralize the product version in `Directory.Build.props` and derive assembly/file metadata, the WPF window/header, package checks and GitHub artifact naming from the same source. No duplicate hardcoded product-version expectations in CI or the build script. Do not bump component-owned CET/GRSP or PresentMon versions, nor change unrelated behavior or styling.

## 2026-11-10 — Final owner Capture page design refinement

- Retain official owner logo placement and existing metallic buttons/tabs. Raise GOT LAG headline+subtitle together about 14 DIP without shifting downstream vertical positions. Move Game Root x+30 DIP and shorten its available width, aligning Capture Workflow to the same region **once**, not double translating individual cards. Narrow each card by 12 DIP total (6 per side) and retain the original card spacing or greater.
- Game Root has no folder symbol. Three workflow cards use a single-color face instead of metallic shading, simple bold cyan-glowing WPF vectors (download tray, gamepad, layers), and yellow page-style 1/2/3 numeral markers. Start instructions explicitly avoid menus/inventory/pauses; Collect copy references continuing to Analyze. All original capture actions, owner assets, two-page system and v1.1.9 remain unchanged.


## 2026-11-10 — Explicit unified app v1.2.0 version bump

- Owner explicitly requested **GCO v1.1.9 → v1.2.0**. Change only the canonical `VersionPrefix` in `Directory.Build.props`; retain derived assembly/file/WPF/package/CI identity without redundant version literals. No component-owner version or app UI/backend change authorized.


## 2026-11-10 — Capture screenshot refinement after v1.2.0

- User requested three workflow cards **20 DIP narrower each (10 per side), 24 DIP taller**, still centered beneath Game Root, with title **immediately above** its corresponding description and a deliberate gap **below the icon**. Raise the Game Root/workflow content about 10–20 DIP to make room, while leaving sufficient console space for two rows of notifications and a clear margin near Continue. Retry the Start Cyberpunk WPF game controller as a readable bold cyan outline; preserve the accepted download/data icons. No user logo, theme, external binary, profile or version change.


## 2026-11-10 — Owner screenshot revision: outer chamfer bleed and gamepad shape

- Remove the rectangular dark backing exposed outside the Game Root chamfer; retain the established actual outline and editable path field. The Start Cyberpunk gamepad must have a **broad, soft-rounded PlayStation-style body and handles** with four distinct face buttons; keep the download/data symbols and all other approved layout, profiler integration and product version unchanged.


## 2026-11-10 — Correct Start controller shape at its actual WPF render size

- The middle workflow icon must match the supplied reference's recognizable broad **PlayStation-style rounded controller** with deep curved grips and **four clearly separate buttons**. Correct the icon's Viewbox/element aspect sizing together with vector geometry; retain the other two accepted icons, cyan glow, page layout, current Game Root corner fix and v1.2.0. Visual acceptance is from the real Windows app.


## 2026-11-10 — Lock GCO normal workflow to Cyberpunk 2077 2.31 framework stack

- **Owner decision:** GCO's current normal Capture/Analyze product is **Cyberpunk 2077 patch 2.31 only**. Game patch 2.5 is not yet supported; never silently attempt its instrumentation.
- Require **exact** supported framework versions **Cyber Engine Tweaks (CET) 1.37.1**, **RED4ext 1.30.0** and **redscript 0.5.31**. **Exception: 0-Engine accepts v0.18.0 or newer automatically by version**, including future releases; do not maintain a release whitelist or freeze it to 0.18.6. Detect the installed version and validate the source structure/injection entrypoints the owner profiler actually uses; if a newer release changes those interfaces, refuse unsafe instrumentation with clear diagnostics rather than blocking the version preemptively. The minimum deliberately excludes problematic 0.17.x variants. This later owner choice supersedes the prior CET-only/GRSP-only *normal UI admission* requirement, but never imposes a mod inventory, fixed source hash or arbitrary mod-version lock.
- A valid 2.31 installation with **zero additional CET mods and/or zero additional REDscript mods** may still profile; mod count must never be a prerequisite. 0-Engine is mandatory profiling infrastructure, although it appears in a CET mod directory.
- User installs/maintains the game frameworks; GCO **detects actual versions from trustworthy evidence**, checks exact pins except the open-ended 0-Engine minimum, fails closed on missing/unverifiable versions or required injection contracts, and explains the required release with official links. Do **not** silently overwrite, update, download or replace any user's framework files. Existing authoritative CET and GRSP owner-managed profiler install/restore transactions remain unchanged.
- **PresentMon** remains bundled, starts automatically when possible, and is non-blocking; PresentMon-only data cannot authorize Continue. **G-REDruntime** remains conditional: profiler-side only if proved necessary for measurement, otherwise include in generated optimization overlay only when a proven accepted transform requires it. Neither is a universal game-framework prerequisite.
- Always install/restore both managed profilers in the standard supported stack, but retain safe missing-capture diagnostics, retry/session identity and recovery of *all actually modified originals*. A failed/partial profiling result is not a reason to bypass cleanup. Do not reinterpret the support restriction as permission to invent profiler binaries or alter their owner code.
- Implementation remains **pending**: current `GameInspector` checks file/folder presence and `UnifiedProfilerWorkflow` does not verify the game patch or CET/0-Engine/RED4ext/redscript version compatibility. Implement genuine source-of-truth version checks and meaningful mismatch/refusal contracts before gating Install. Do not label this documentation decision as a tested installer capability. GCO app version remains **v1.2.0**.


## 2026-11-10 — Two-stage admission: framework preflight then verified profiler install

- **INSTALL PROFILERS gate:** require verified Cyberpunk 2077 **2.31**, CET **1.37.1**, RED4ext **1.30.0**, redscript **0.5.31**, and 0-Engine **>=0.18.0** with compatible required injection points. The presence of 0-Engine is **mandatory** for the normal GCO profiler workflow; no CET-only fallback without 0-Engine. Zero *additional* CET or REDscript mods is still a valid source state. Game must be closed and no recovery-required owner session may be overwritten.
- **Consistent missing/incompatible prerequisites:** run all framework/version checks and list every failing requirement in the yellow Game Root status and console. Any missing, unsupported or unverifiable prerequisite **disables INSTALL PROFILERS** and neither owner installer may run. Exact user-facing missing messages:
  - `Cyberpunk 2077 2.31 is required` (when the selected game is missing or its version is not 2.31; include detected version when known).
  - `CET 1.37.1 is missing`.
  - `0-Engine 0.18.0 or greater is missing`.
  - `RED4ext 1.30.0 is missing`.
  - `redscript 0.5.31 is missing`.
  When a component is present but is an unsupported version, report `<component> requires <version>; found <detected version>` rather than falsely calling it missing. When files exist but version/required interfaces cannot be established, report `<component> compatibility could not be verified` and keep installation blocked. **0-Engine accepts future >=0.18.0 versions** without a whitelist, subject to structural injection contracts. Do not auto-install, upgrade, overwrite or modify any of these game frameworks; user corrects the setup, then Refresh rechecks. Mod counts remain informational, never a blocker.
- **INSTALL PROFILERS action:** invoke existing source-owner CET and GRSP installation transactions, preserving current authoritative journaling and rollback/recovery. After install, **reinspect both owner-managed installations independently** and verify the same GCO session/recovery journal. An install command's exit code alone does not grant readiness. A failure or partial install keeps START CYBERPUNK disabled, provides actionable console diagnostics and retains recovery.
- **START CYBERPUNK gate:** enable only after both G-CET and G-REDscript profiler installations are positively verified, alongside valid preflight and no unresolved recovery conflict. Status text under Game Root: `G-CET Profiler · INSTALLED | G-REDscript Profiler · INSTALLED | PresentMon · AUTOMATIC`. Console: `G-CET and G-REDscript profilers successfully installed. Ready to start Cyberpunk 2077.`. Preserve actual game path text and allow subsequent reinspection to revoke readiness if files drift or recovery becomes necessary.
- **CONTINUE remains separate:** do not confuse profiler installation with captured evidence. Require current-session native capture proof and safe owner-manager restoration; PresentMon-only never unlocks it. PresentMon remains automatic but non-blocking.
- **Status:** owner-approved behavior specification, **not yet implemented**. Source-code readiness logic and live 2.31 framework checks must be validated in functional CI and real-game tests before claiming enforcement. GCO version **v1.2.0** unchanged.


## 2026-11-10 — Collect click owns PresentMon stop and three-domain evidence check

- **Capture start:** after verified installation of both native profilers, START CYBERPUNK launches GCO-owned PresentMon *without any F11 binding* and then the game. Game F11 toggles CET/GRSP profiling; PresentMon records continuously as a frame-time reference, including before/after the native capture windows. Preserve the tested owner timestamps and verify alignment when using actual CSV; do not claim simply overlapping raw CET/GRSP CPU equals total CPU.
- **Next action:** as soon as a launch attempt is recorded, COLLECT RESULTS is visibly available **regardless of CET/GRSP capture-ready flags, PresentMon process state, or already-validated results**. Its enabled/availability status must not wait for PresentMon to stop independently, or for any of the three profilers to have produced a capture. If Cyberpunk is still running, clicking COLLECT RESULTS reports exactly `Close Cyberpunk before collecting results.` and makes no collection/PresentMon shutdown changes; the button stays available for another click after the game closes.
- **Collection button owns shutdown:** when Cyberpunk is closed and the user **clicks COLLECT RESULTS**, GCO first gracefully stops any **GCO-owned** PresentMon session and waits for writer flush/exit (never kills an unrelated process), then gathers current-session CET and GRSP owner captures and validates the current attempt's PresentMon CSV. A missing PresentMon process, missing tracking record, missing capture, or failed F11 does not preemptively prevent the user from pressing Collect. Any PresentMon stop/flush error must be surfaced and recovered safely; it cannot silently be treated as success.
- **Post-click diagnosis and Continue:** after the collection attempt, report each missing/unusable domain separately in the yellow status/console: `G-CET Profiler results missing`, `G-REDscript Profiler results missing`, and/or `PresentMon results missing`. Distinguish failed stop or invalid/corrupt capture from absent evidence with specific diagnostics. **CONTINUE is enabled only if CET and GRSP current-attempt native captures are BOTH verified**, the user has clicked COLLECT RESULTS, and any GCO-owned PresentMon recording has been safely stopped/verified absent; **PresentMon CSV is optional**. Missing or unusable PresentMon frames still produce a separate warning but do not block Continue or source-proven optimization. Missing CET or GRSP native evidence blocks Continue; no skip/acknowledgment bypass for either. This supersedes the earlier all-three-mandatory policy and former native partial-results Continue behavior. Never claim frame-time correlation or FPS/frame-time improvement from absent PresentMon evidence. CONTINUE still performs authoritative source-owned restoration of **both actually installed profilers** before entering Page 2. Keep separate explicit recovery/restore safe for failed captures; users must not be trapped with modified game files.
- **Repeated START CYBERPUNK / rapid clicks:** a second legitimate launch after the game closes must gracefully stop any previous **GCO-owned** PresentMon instance, verify exit/flush, then start **exactly one** fresh PresentMon instance and launch the game; each recording attempt uses a new unique identity/CSV so old evidence is retained but never mixed into a new attempt. Protect concurrent/double-clicks with the existing single-controller gate and busy state. If Cyberpunk is currently running, do not spawn a duplicate game or collector; report the active game. Do not touch unrelated PresentMon processes.
- **Implementation status:** approved target behavior **not yet implemented**. Current WPF `readyToCollect` waits on native readiness/PM tracking, `ContinueButton` uses `HasNative`, and the old partial-results dialog permits skip; these must be changed at their source alongside current-attempt proof and tests. Current PresentMon controller already has GCO-only graceful restart and no hotkey; preserve that implementation rather than duplicate it. Existing GCO v1.2.0 and bundled source-owner binaries unchanged.


## 2026-11-10 — Frame-time capture non-blocking for Continue (latest owner correction)

- **Confirmed final capture admission:** both **G-CET and G-REDscript** profiling captures must be verified from the *same current attempt* after **COLLECT RESULTS**. PresentMon is automatic but **optional for Continue**, because CET + GRSP profiling can still inform a combined optimization pass without frame-time correlation. Do not treat it as a prerequisite and do not offer a bypass for missing native captures.
- The **COLLECT RESULTS click** remains responsible for stopping any GCO-owned PresentMon process first and for collecting/verifying all three domains with explicit status for each (`RESULTS VERIFIED`, `RESULTS MISSING`, or a specific failure). A missing/nonfunctional CSV by itself does not block Continue, but a **still-active or unverified-stop GCO-owned PresentMon session is a cleanup blocker** that must be resolved before advancing. The game must be closed.
- When CET+GRSP are valid but PresentMon CSV is absent, display `PresentMon: RESULTS MISSING` and `Frame-time correlation unavailable. Native profiling complete; CONTINUE enabled.` Continue still invokes source-owner verified original-file restoration before Page 2. With no PresentMon, reports must avoid unsupported frame-time/FPS/hitch-causality claims; CET and GRSP measurements remain separately attributed.
- **Status:** owner decision only. GCO runtime admission logic and safety tests are not yet implemented. No code or packaging changes; v1.2.0 remains.


## 2026-11-10 — Console-first capture warnings and independent RESTORE PROFILERS

- **PresentMon remains automatic in normal copy** (`PresentMon · AUTOMATIC`), with **no optional setting, checkbox, skip prompt or "optional" label**. The automatic collector is attempted on Start and stopped on **COLLECT RESULTS**. Its CSV is not required to unlock Continue when both native CET and GRSP captures from the current attempt are verified and the collector has been safely stopped. Only after collection, if missing, report `PresentMon: RESULTS MISSING` and `Frame-time data unavailable. CONTINUE enabled.` in the console; don't conceal the missing evidence or claim FPS/hitch correlation.
- **Use the console for operational feedback, not modal popups.** After Collect, report all three capture domains as VERIFIED/MISSING, plus precise errors for missing/invalid or failed-to-stop cases, and maintain diagnostic visibility instead of overwriting it immediately during automatic refresh. No incomplete-capture popup or Continue-bypass dialog: CET and GRSP are both mandatory for Continue; PresentMon is non-blocking only when safe collector shutdown is established. On attempted collection while game is running, console says `Close Cyberpunk before collecting results.` and Collect remains available.
- **Independent RESTORE PROFILERS:** preserve the existing RESTORE action but make it clear and accessible whenever a GCO-owned profiler transaction/journal requires cleanup, **including when Continue is locked or all three captures are missing**. It is an explicit user-triggered recovery action, never an automatic consequence of failed results. If the game is running, report `Close Cyberpunk before restoring profilers.` in the console and change nothing. Otherwise gracefully stop/reconcile any **GCO-owned** PresentMon first (do not touch unrelated processes), preserve copied capture files and crash diagnostics, invoke the original CET/GRSP owner-managed restoration/verification, and report success or recovery-required detail in the console. No generic confirmation `MessageBox` on the normal recovery path; the deliberate RESTORE PROFILERS click is the action. Safely refuse or warn on ambiguous ownership and never claim restoration after a failed verification. Keep the journal/recovery path when restoration fails.
- **Restoration does not equal Continue.** Manual RESTORE PROFILERS ends/cleans up the instrumented game session without unlocking ANALYZE; to retry the normal workflow, users reinstall profilers through the approved preflight and create a fresh capture attempt. Continue still invokes owner restoration itself after a valid dual-native capture, then unlocks Page 2. The restore action must never make stale or incomplete data look complete.
- **Implementation state:** owner-approved UX/behavior, **source still pending**. Currently `MainWindow.Warn()`, `ConfirmPartialResults()` and the RESTORE confirmation use modal dialogs; `RestoreIncompleteSessionAsync` currently stops PresentMon after owner restore. The WPF status, recovery order and state gates must be fixed at source with integrated functional tests and real-game recovery validation. Preserve the existing source-owned owner managers, single Windows CI, two pages, approved visuals and GCO **v1.2.0**.


## 2026-11-10 — Actual redscript compiler location (corrected implementation)

- Source of truth: redscript 0.5.31 ships standalone under `engine/tools/scc.exe`, `engine/tools/scc_lib.dll` and `engine/config/base/scripts.ini`, **not** under `red4ext/plugins/redscript`. `engine/tools/scc.dll` on its own is merely partial/alternate evidence and cannot satisfy the official compiler contract. This later discovery supersedes any hardcoded assumption that redscript is a RED4ext native plugin. Do not confuse redscript *scripts* in `r6/scripts` with the compiler framework installation.
- GCO `FrameworkPreflight.InspectRedscript` is read-only and reports absent vs incomplete vs version-unverifiable compiler. The exact **0.5.31** requirement remains, and only trustworthy version evidence can enable installation. Existing PE file version metadata is consulted but may be unavailable for Rust-built binaries; never infer 0.5.31 from the name or mere presence of `scc.exe`, nor invoke a compiler against live game files simply to check its version.
- Implemented and integration-tested through [Windows CI #242](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/42853637516) **SUCCESS 205/205**, code `816730fc`. Product stays v1.2.0. Genuine user-install binary version identification, game-start/capture alignment and safe end-to-end restore still need live testing; green CI does not certify those behaviors.


## 2026-11-10 — GCO does not assess 0-Engine injection compatibility (owner correction)

- GCO's **INSTALL PROFILERS** gate checks that 0-Engine is installed with an identifiable version **>=0.18.0**. It does **not** inspect scheduler/module/injection entrypoint shapes, demand a `modules/` directory or predict whether an optimization can be generated. It leaves any valid-version 0-Engine through the preflight. Unknown version and versions below 0.18.0 remain rejected with version-specific diagnostics.
- **CET Resolver/Generator owns** live source recognition, current-version variations, partial source compatibility, generation decisions and safe refusal when injection is not supportable. GCO must not duplicate that work or make the initial install a speculative optimizer compatibility gate. This explicitly **supersedes** the structural-preflight language in the earlier 2026-11-10 framework decisions.
- Implemented in `FrameworkPreflight` with a version-only 0-Engine admission path. End-to-end installed-game verification remains pending; no product version change, owner manager modification or new pipeline.


## 2026-11-10 — redscript fixed-release identity (owner approved)

- GCO supports **one redscript compiler release, 0.5.31**. Admit only the upstream Windows v0.5.31 executable/library pair with SHA-256 `scc.exe: DA2C0DE580173010C907A05FDD2F3CD2430D6E62BF748339D65060118AE32EDD`, `scc_lib.dll: 49EB60A3613C990CD13B6136E827A74F9F64AD68E2F911D8C93E4E3D3F53C100`. Both were verified against the official release ZIP whose published checksum was validated in GCO Windows CI #244, and against the owner's uploaded/installed samples. This narrowly scoped framework binary identity is an intentional exception to the prohibition on fixed **mod-source** hashes, not a REDscript optimizer-version catalog.
- No redscript version fallback from PE metadata or a user-provided version label; require the documented upstream compiler files and config. Reject unknown/mixed binaries safely. Do not compile on live user files for detection. **0-Engine remains >=0.18.0, version-only admission; CET Resolver owns optimization source decisions.** No change to pinned CET, RED4ext or game versions.


## 2026-11-10 — Owner-approved diagnostic-only redscript hash labels

- Exact **0.5.31** admission remains unchanged and requires the verified upstream executable+library pair. An additional **small read-only version-label map for known `scc.exe` binaries** (0.5.28, 0.5.30 and 0.5.32) is permitted solely for actionable console reporting: GCO explains the rejected version instead of showing only `compatibility could not be verified`. Unknown builds and self-compiled binaries remain unverified. Never treat a label as permission to load another REDscript Resolver version. Companion DLL mismatch for 0.5.31 must fail explicitly. No new settings, UI, gameplay modifications, or expansion of 0-Engine/GCO version rules.


## 2026-11-10 — Final 0-Engine preflight correction: presence only

- **Supersedes** all earlier 0-Engine minimum-version (`>=0.18.0`), recognizable-version, and source-shape preflight requirements. GCO checks **only** that `<gameRoot>/bin/x64/plugins/cyber_engine_tweaks/mods/0-Engine/` is a directory containing a file named `init.lua`. Any version, unknown version and any source layout pass. A missing folder or `init.lua` fails with the expected path; the check does not read `init.lua`, demand `modules/` or inspect versions/APIs.
- **Ownership stays with CET:** the source-aware CET Resolver/Generator determines later whether its modules can safely be injected, whether already installed work is compatible, and whether generation must refuse. GCO profiler setup must not block capture merely because optimizations might be unavailable. The owner's `0-Engine.zip` contains `0-Engine/init.lua` and `engineVersion = "0.18.6"` (a layout the previous version parser failed to recognize); parsing was removed rather than expanded.
- The fixed game/CET/RED4ext/redscript versions, two-page layout, .NET 10, authoritative install/restore, and GCO v1.2.0 remain unchanged.

## 2026-11-10 — Owner-approved GCO v1.2.1 version assignment

- Owner explicitly requested promoting the **unified GCO application** from **v1.2.0 to v1.2.1**, using successful integrated Windows CI **#249** as the functional baseline. Change only `Directory.Build.props` (`VersionPrefix`), which drives assembly/file metadata, `VERSION.txt`, the guide and artifact naming; no duplicate version constants.
- This is a **version-only source change**: no optimizer logic, 0-Engine admission, installed frameworks, CET/GRSP native or managed component versions, PresentMon, or CI workflow changes. Completed CI #249 and its v1.2.0 artifact remain historical and are not retroactively relabeled. New v1.2.1 CI/package verification is required before declaring a downloadable v1.2.1 build verified; no release tag or gameplay acceptance is implied.
- **CI completed:** [Windows CI #250](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/42889417236), source `4db2607b711b67679e2d447ef2a3d26346593cb4`, **SUCCESS 231/231**, artifact `Gs-Cyberpunk2077_Optimizer-v1.2.1-run250-a1` (ID **13796858579**); versioned executable/package identity passed. This satisfies the new-build verification requirement but not in-game gameplay/restore or optimization ZIP acceptance.

## 2026-11-11 — Owner-approved capture folder readability

- New GCO capture folders use local `YYYYMMDD-HHmmss` plus the existing immutable 32-hex session ID. Child evidence directories are named `PresentMon/`, `G-CET/` and `G-RED/` as the corresponding artifacts are collected. Saved capture schema/provenance, owner install/restore and current-session gating are unchanged. Preserve legacy ID-only layouts without reorganization or data loss; do not merge captures from different attempts. The date/time is a human-readable label, not identity or proof of a valid capture. PresentMon native CSV path length safety and verified output remain required. Unified GCO remains v1.2.1.
- **CI verification:** [Windows CI #257](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/42924716336), **SUCCESS 252/252**, source `04f5eae69580835db4081d74040fb25aac087b44`, integrated ZIP artifact **13812514859**. Folder-layout regression contracts cover new and legacy evidence retrieval, verified owner archives and PresentMon, refusal of ambiguous session IDs, and unchanged restore validity. Windows gameplay capture acceptance is still pending.

## 2026-11-11 — Explicit user lifecycle and results folder simplification

- **Never discard upon COLLECT RESULTS.** After game closes, Collect stops GCO-owned PresentMon, collects whatever CET/GRSP/PresentMon evidence exists and reports the precise result; missing/zero native results do not cause a fake completed capture or unlock Continue.
- **Implicitly abandon only if Collect was never used:** starting Cyberpunk again while GCO is running, either by GCO or a detected external game launch, gracefully stops previously owned PresentMon, discards only uncollected GCO temporary output and opens a new session. A collected index is immutable. Normal GCO exit uses the same cleanup, but may not act if the process crashes. CET/GRSP native game files and source-owner results are never deleted directly by GCO; stale native output cannot be treated as new-session proof without source-owner confirmation. GRSP currently has no source-owner reset CLI, therefore conservative native attribution refusal may be necessary.
- GCO Restore must remain usable even if PresentMon cannot stop; native restoration uses original authoritative owner managers. Console messages only, no new recovery dialogs. One game-root lock, two pages and unchanged version.
- New capture directory format `GCO-Capture-DDMMYYYY_HHMMSS` (no hash suffix). `collection.json` internally maps to immutable session ID/game root. Direct children `PresentMon/PresentMon.csv` + controller JSON, `G-CET/`, `G-REDscript/`. Colliding timestamps refuse safely; no overwrite. Previous captures stay readable unmodified. Crash/incomplete diagnostic snapshots belong under `RESULTS/Diagnostics`, never impersonate valid capture.
- **Verified source/build:** [Windows CI #258](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/42929600948), source `2e3b1d82502430eaca97e83d1d3f9743e6e232c8`, **SUCCESS 261/261**, integrated v1.2.1 artifact **13815448253**. Capture storage, legacy lookup and safe GCO-only temporary discard contracts passed. Interactive game-transition handling and native-owner cleanup limitations still need live acceptance.

## 2026-11-11 — Automatic PresentMon flush at game exit, manual collection retained

- GCO must **stop and flush its own PresentMon immediately after Cyberpunk terminates normally or crashes**, using an event-driven process-exit notification rather than a continuous 2-second process scan. The tracked CSV must remain available, uncollected and unmodified until user **Collect** (permanent preservation), launches Cyberpunk again (discard uncollected), closes GCO (discard uncollected) or restores native profiler files without collecting (discard uncollected). Stopping PresentMon alone is not collection and must never authorize Continue.
- Prefer Windows process-start event monitoring for external game launches and `Process.Exited` for actual termination; retain a slower fallback if Windows notifications are unavailable. A late exit from an old PID cannot stop the next attempt. No extra UI controls, version bump or new CI workflow. Native CET/GRSP interrupted-F11 recovery remains separately owner-controlled and is not implied by PresentMon auto-flush.
- Verification: integrated Windows CI #259 SUCCESS, 261/261 contracts and real OS Process.Exited / WMI StartTrace event test PASS. https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/42936857477 ; downloaded GCO v1.2.1 build https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/42936857477/artifacts/13819171987 . No version bump; live Cyberpunk crash/relaunch and interrupted native F11 recovery are still pending verification.

## 2026-11-11 — Latest owner correction: window close is not capture abandonment

- **Supersedes the earlier instruction that GCO exit deletes the active uncollected capture.** Exit GCO and reopen the *same* package: current GCO ownership journal, game root, profiler install/restore state, pending capture ID and available COLLECT RESULTS action must survive unchanged. Native CET and GRSP live owner files remain source-owned and untouched. Do not reset profilers, mark capture collected or discard the pending capture on window close.
- **Only uncollected PresentMon data is disposable on GCO exit.** Stop the GCO-owned ETW session gracefully, delete only the indexed/current-session temporary `PresentMon.csv` and controller output (subject to provenance and no-link checks), clear PresentMon tracking, and retain the capture index/journal. While GCO stays open, a game exit still stops/flushed PresentMon and keeps its CSV only until explicit Collect, app close, new game or Restore. Explicitly collected data is permanent even if other domains are missing.
- **Reset/abandon triggers:** verified *new Cyberpunk launch* (start fresh), explicit source-owner RESTORE without prior collection (restore originals), or an explicit user-triggered fresh install/session transition; **closing GCO is not a trigger**. A successfully completed/restored Analyze workspace also resumes across GCO restart using a verified game-bound last-workspace index. No `CaptureCache`, no native CET checkpoint/crash recovery, no speculative partial CET optimization input.

- **Implemented/CI verified:** unified Windows [CI #260](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/42941321082) SUCCESS, **267/267 contracts**, source [8ce2298](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/commit/8ce229813f943ee338fc4e3ad4ee93b77fd19a30), packaged [GCO v1.2.1 artifact #13820658189](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/42941321082/artifacts/13820658189). Actual game-close / app-close / reopen and source-owner F11 collection are not yet user-verified.

## 2026-11-11 — Owner-approved v1.2.2 promotion and unified results destination direction

- **Version authorization:** Promote GCO from **v1.2.1** to **v1.2.2** based on last verified integrated capture-lifecycle CI #260, version-only change in `Directory.Build.props`; the source-owner CET/GRSP versions and GCO capture logic are unchanged. **[Integrated Windows CI #261](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/42947295136) SUCCESS, 267/267 contracts**, [download GCO v1.2.2 artifact #13823073993](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/42947295136/artifacts/13823073993). This is a CI build, not unverified gameplay acceptance or a release tag.
- **Future results organization (not part of this version-only change):** GCO is the single user-facing lifecycle/storage authority. Source-native CET/GRSP measurements in game folders are temporary, and only explicit Collect should preserve full verified results under the GCO root's `RESULTS`. Owner-managed collectors may still perform safe transformations/reports; do not create additional separate permanent component archives or duplicate copies. Non-collected outputs remain temporary and should be reset only by GCO's approved triggers (new game or Restore), never simply by closing GCO. Direct-destination support and atomic verification/cleanup still require implementation and actual output tests.

## 2026-11-11 — Short publication-ready user-facing Capture console

- The footer console is a **short status/control instruction**, not a technical report. After verified Restore show `Restore successful. Original game files restored.` (brief warning only when necessary). Remove long source-owner manager text and paths from success messages. Also shorten installation, game exit, collection, missing capture, failed startup and recovery errors. Keep accurate missing-domain and safety messages, particularly when Restore did not verify original files.
- Preserve complete backend details through hover/tooltips and diagnostic tracing. Unexpected technical messages may be shortened on screen, but **never** reinterpret shortened text as success, change safety gates, or lose evidence. Keep screenshot-art/layout unchanged and GCO version **v1.2.2**.

- **Implementation verification:** [GCO Windows CI #262](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/42950860568) **SUCCESS 272/272**, tested source [6e8eaa0](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/commit/6e8eaa0ffa4a9d40da1263208a40660ac69f50b1). Verified v1.2.2 package [artifact 13825510033](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/42950860568/artifacts/13825510033). Gameplay screenshot confirmation outstanding.

## 2026-11-11 — Owner override: no hovering in Capture

- **Supersedes** the earlier Capture console policy that exposed full manager diagnostics on hover. The Capture footer is a short, non-hoverable status indicator. Remove its tooltip and related UI plumbing; the Game Root input and dependency row must also have no hover tooltips. Optional technical traces may retain original error details without creating a hidden interaction requirement.
- Preserve brief, accurate visible Restore/Collect/error instructions and all existing safety gates. Resolver/Analyze owns the substantive performance explanations and optimization reports. Product stays **v1.2.2**; do not disturb UI art or native components.

- **Implemented and verified:** [Windows CI #263](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/42952158232) SUCCESS, 272/272 contracts plus real packaged WPF no-tooltip assertion on Capture status, game path and dependency text. [v1.2.2 artifact #13825601726](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/42952158232/artifacts/13825601726). No new version, UI geometry changes or native component changes.

## 2026-11-13 — locked Normal UI and isolated Maximized-only existing-property layout

- Owner approved a separate maximized presentation mode after repeated default-size regressions. Original 1280×760 XAML, original four Capture elements and parent structure, shared scene-neon, graphics, 49-DIP Continue, native icon/text/button dimensions and application behavior are locked. Maximized may modify only saved and restored margins, existing card-container height and fullscreen console clearance. No injected UI spacer controls, Grid reparenting, Viewbox scaling, duplicate controls, new workflow or version bump.
- Implemented at source `15562eee`; canonical [Windows CI #291](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/43333049012) **SUCCESS, 278/278 contracts**, native shown WPF Normal→Maximized→Normal pixel-identical at runner DPI, packaged artifact ID **13996520838**, v1.2.4. Real Windows owner screenshot confirmation, alternate DPI and gameplay remain unverified.
## 2026-11-13 — maximized-only proportions after owner screenshot

- In Maximized, owner requests significantly smaller and narrower step cards, wider inter-card gaps, more vertical space between major sections, a modestly taller Game Root **heading-and-box group**, a tighter workflow heading/card relationship and a slight console shift left. Normal layout remains **untouchable**, and original properties must restore exactly. Implemented only in original `MainWindow.xaml.cs` on `main`, code SHA `046e519`, v1.2.4.
- Canonical [Windows CI #292](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/43335794567) SUCCESS: 278/278 tests and real shown WPF Normal→Maximized→Normal byte-identical pixels at runner DPI; downloadable artifact ID 13997008356. User screenshot approval still pending.

## 2026-11-13 — framework status indicators and card state visuals (CI #298)

- Framework checks below Game Root display the **owner's original** `GcoStatusSuccessIcon` green check or `GcoStatusFailureIcon` red X as explicit 16×16 WPF `Image Source="{StaticResource ...}"` elements; both hidden while preflight is unknown. Existing framework-readiness result is the single authority. Avoid new icon generators and separate state logic.
- Two original owner PNG backgrounds from `b565031` must reflect the original button state: active yellow vs inactive cyan; Restore Profilers uses cyan. Render proportional center crop with original card chamfer clip; hover outline follows the centered SVG shell, not fullscreen button-row width. The original Normal and maximizing layout must remain stable.
- Canonical [CI #298](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/43352879881) **SUCCESS** on `5713b2b`: 278/278 contracts, native WPF Normal→Maximized→Normal pixel identity, owner CET/GRSP smoke and one private .NET 10.0.15 runtime. Artifact ID 14004921634. User screenshot and gameplay remain pending.

## 2026-11-13 — framework-by-framework readiness and exact-bound card artwork

- Display **four independently sourced icon/label pairs** under the Game Root framework line, with existing owner-supplied 16×16 WPF green check/red X resources: CET, 0-Engine, RED4ext and redscript. Their verdicts must originate from `FrameworkPreflight.Evaluate`'s one authoritative component checks; game version stays an independent prerequisite. Show detected versions/required version or MISSING/UNVERIFIED; never infer permission from icon state. Keep 0-Engine version-unlocked.
- The two owner card PNGs fill the **exact** original card bounds, center-cropped proportionally in their own `Rectangle` `ImageBrush` surfaces, clipped to the original 45° chamfer geometry. Existing stage button state drives active/inactive and blue Restore; never use `Image` UniformToFill if it can arrange outside card bounds. The console text uses all available horizontal space up to Continue and WPF wraps verified preflight blockers naturally. Do not alter the stable 1280×760 Normal geometry, fullscreen-only margins, or source owner profiler behavior.
- Implemented with [Windows CI #303](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/43357652326) **SUCCESS**, **282/282** tests, WPF native Normal→Maximized→Normal pixel proof, CET+GRSP owner integration and one .NET 10.0.15 runtime. Source `6606d7d`, artifact **14006752092**, v1.2.4. Owner screenshots and game behavior require separate approval.

## 2026-11-13 — v1.2.5 promotion approved

- Explicit owner version-only promotion on unified `main`; `Directory.Build.props` is the sole product version source. **[Integrated Windows CI #304](https://github.com/giorgioherma/Gs-Cyberpunk2077_Optimizations/actions/runs/43486488225) SUCCESS, 282/282**, exact source `492477541db6490b39f2598bb0513e7c09967dd7`; artifact **14067103464**, `Gs-Cyberpunk2077_Optimizer-v1.2.5-run304-a1`. UI and CET/GRSP source components unchanged; no release tag or new workflow. User visual/gameplay acceptance still pending.
