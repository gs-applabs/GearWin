# CURRENT CHECKPOINT — 2026-11-28

M01/Store native CLI third pass: Microsoft Windows Developer Blog (2026-02-11, https://blogs.windows.com/windowsdeveloper/2026/02/11/enhanced-developer-tools-on-the-microsoft-store/) documents targeted store update <product-id>. Use current-user WindowsApps store.exe alias and local update --help capability check for --apply before exact selected-product mutation; input closed; no bulk command, no install substitution. If unavailable, existing exact WinGet route then guarded UI fallback. Native started but unverifiable outcome stays unconfirmed instead of starting duplicate mutation. Readback uses WinGet exact ID and optional native installed table with explicit unique product ID; do not infer identity from display title. Shared runner now supports redirected input; read-only Store scan declines bulk prompt, preserves explicit ID and blank columns and rejects headerless output, merge keeps distinct product IDs with same title. Seven additional deterministic cases (748 cumulative authored, not passed on this candidate). Preview CLI availability/flags/output, live Store deployment and localized output/automation require native acceptance. No new timers/background services; actual direct installer timeout remains bounded.

Exact parent ef5e418566630afb7d8c57952da48cb729af592e installer 46125769707 and audit 46125769708 pending at inspection. ad09f60 build 1083 desktop +55 migration PASS; 5/6 installer jobs success, in-app running. fbe9406 six distro jobs and four recovery jobs SUCCESS. No merge or release.

M01/Store second pass: synthetic title-only rows now attempt exact installed WinGet msstore name resolution (unique explicit source/product ID; unsafe argument characters rejected); exact direct update also supports unknown installed versions on explicitly selected rows. Known target version must be reached, not merely increased; unknown target requires observed increase. Successful direct attempt uses bounded readback retry, no repeat install. UI fallback recognizes row-level UpdateButton and exact EL/EN Update labels, rejects bulk/open/pause/cancel/install labels, can observe terminal rows without buttons, marks invocation attempted before UIA dispatch to avoid duplicate action. Fixed local variable shadowing in view before build. Seventeen additional deterministic cases (741 cumulative authored including previous pass, not passed on this candidate). Actual live Store consumer update, modern UI tree/locale validation and package identity/permissions still need acceptance. No new timers/background service.

Exact parent dd6c9ca10a1a3800d82193efc1d491b9acbfc830 installer 46125684269 and audit 46125684323 pending at inspection. ad09f60 build 140918307222 1083 desktop +55 migration PASS, offline clone success; installer E2E running. fbe9406 recovery 46125310415 all four jobs SUCCESS. No merge or release.

M01/RQ15 NEW USER BLOCKER — Microsoft Store selected updates: removed legacy URI-launch-as-success route, source-restricted exact WinGet upgrade first for distinct Store product IDs, compare installed version before/after, accepted/timeout without evidence stays unconfirmed. Name-only rows use existing interactive fallback, now unique exact title and explicit update/active status only, no fuzzy matching or disappearance success. No bulk MDM trigger or auto-closing user Store in selected flow; actual cancel token reaches core admission/UIA/readback, running direct installation allowed to finish (bounded runner). Keep unconfirmed rows visible and report explicitly. Store button routes to existing in-app scan; missing WinGet no longer suppresses other providers, merge exact Store titles and dispose/cancel Store CLI scan; nightly upgrade restricts winget source. Fifteen deterministic cases (724 cumulative authored, not passed on this candidate). User request prioritized ahead of general backlog. Real consumer Windows Store integration, localized Store UI states beyond EL/EN, direct readback in non-English WinGet and live package/account policies remain acceptance blockers; no claims of all apps updating silently.

Exact parent fbe9406d4b05864c67eb1ad7b0a8b1b9c9adc8fa installer 46125310416 and audit 46125310457 pending; linux-distros 46125310440 queued, recovery-vm 46125310415 running. ad09f60 six distro jobs SUCCESS, recovery 46125207827 and installer 46125207884 running; f1339c0 six distro and four recovery jobs SUCCESS; 4fe4df4 installer six jobs SUCCESS, 1083 desktop +42 migration PASS. No release or merge.

H01/M05/E03: broaden Windows GUI read-only migration inventory to current-user non-framework/non-resource packaged app registrations, including nonremovable and non-Store registrations. Reuses one existing UWP PowerShell capture path; fixed narrow Store-removable filter and removal authorization remain unchanged. Separate Store/non-Store family identity kinds, explicit catalog match only; parser reports skipped/invalid records. Bound retained process stdout/stderr (4 Mi/64 Ki characters), drain both pipes under existing 30-second budget. Four deterministic cases (709 cumulative authored, not passed on this candidate). CLI still registry-only on Windows, other users/provisioning/optional package types, actual portable discovery, local verification and full localization remain open. No new timers or background service; no executable runs or installations by migration.

Exact parent ad09f604c40eebbf87008dfa45f700a00ead7f9e Linux 46125207822 and recovery 46125207827 queued at inspection; installer 46125207884 and audit 46125207821 pending. f1339c0 six distro jobs success, two Linux recovery success, Windows recovery pending. 4fe4df4 build 1083 desktop +42 migration PASS; offline clone, Windows 2022 fresh/from-latest and 2025 fresh success; in-app still running at last inspection. No merge or release.

H01/M04/M05: common planner/CLI/GUI now compare an optional imported destination inventory against explicit manifest identities, preserving found/multiple/not-confirmed state separately from local package verification. Destination OS/schema must match target, selected executable entries cannot prove installation. Windows export adds explicit user-selected EXE names (max 100, no path/content export, no execution, always manual even if catalog matches); no autonomous disk scan or guessed mapping. GUI compare checkbox and bounded preview statuses, output collision checks include new input. CLI exports enforce existing 16 MiB limit. Six deterministic cases (705 cumulative authored, not passed on this candidate). Imported inventory comparison is not live package verification; actual MSIX/portable discovery, CLI Store parity, local GUI verification and 14-language coverage remain open. No new timers.

Exact parent f1339c06d3e71fb99373244ad433cf132c99e0df linux-distros 46124967608 all six success; recovery-vm 46124967597 two Linux jobs success, Windows running; installer/audit pending. 7f5198a recovery-vm 46124742482 success, linux-distros success, installer cancelled pending. 4fe4df4 installer 46124552587 build 140916416321 1083 desktop +42 migration PASS, offline clone success; installer E2E running. No merge or release.

H01/M04/M05: Windows GUI export reuses existing BloatwareService current-user removable Store scan outside TransferScan lease, adds explicit stable package family/version identities to common inventory; scan failure and limited scope are visible warnings, no inferred package mappings. Registry duplicate handling preserves distinct names/publishers/versions for same registration ID. Common planner supports explicit Store identity. GUI shows bounded 200-entry plan preview with full JSON preserved, separate warnings count. Common bounded reader counts actual input bytes including growth; file serializer rejects exports above 16 MiB. Four deterministic migration cases (699 cumulative authored, not passed on this candidate). No new background service, process runner or timer. Native Store coverage, CLI Store parity, nonremovable/MSIX/portable apps, GUI local verification, complete 14-language strings and migration acceptance remain open. No auto installation.

Exact parent 7f5198a0a07f0fa738fd60396a7e9f1cd9fc5d70 linux-distros 46124742446 all six jobs SUCCESS; recovery-vm 46124742482 running, installer 46124742454 and audit 46124742503 pending at inspection. 4fe4df4 installer 46124552587 offline clone success; build running; audit cancelled. e68d192 installer and audit SUCCESS, 1078 desktop +42 migration PASS. No merge or release.

H01/M04: Windows migration card now exports target computer profile and builds a read-only application plan from three explicit JSON inputs (mapping manifest, source inventory, destination profile); writes only explicitly chosen report through existing core/file writer. Preview/manual/unresolved/warnings counts displayed; never executes commands. Common strict JSON document contract extracted into existing migration core and reused by CLI and existing GUI inventory export, no duplicate planner. Rejects same normalized input/output path. Three new deterministic tests (695 cumulative authored, not passed on this candidate). GUI local verification, richer visual report, complete localization, broader application discovery and actual migration remain open. No new timers.

Exact parent 4fe4df43de453e3941441cc1c122cb867551205f installer 46124552587 running; audit 46124552597 pending at inspection. 616426d installer cancelled pending; audit in progress. e68d192 installer 46123696123 all success; audit 46123696166 success; 1078 desktop + 42 migration PASS. No release or merge.

H01/M04/E05: existing read-only transfer preflight now accepts cancellation through core admission and enumeration, checks token at directory/entry boundaries, rechecks destination ancestors after scan, and cannot publish success after cancellation. Existing button toggles Cancel; folder pickers locked during scan, result cleared on selection, unload cancels passive preflight, app inventory export uses independent status (not blocked by the scan UI; core still governs compatible work). Skipped-link count shown; language reloaded on load. Help tab index guarded for existing three tabs. Verified FAQ links exist in source tree. Two deterministic cases (692 cumulative authored, not passed on this candidate). Native blocking filesystem calls cannot be interrupted mid-call, no path identity guarantee against all races, complete 14-language transfer strings and GUI plan/verify remain open. No new timer or disk writes by preflight.

Exact parent 616426dd6e05c8e39042a18d0281698f6e050295 installer 46124249855 pending, audit 46124249887 running at inspection. e68d192 audit 46123696166 SUCCESS; installer 46123696123 running. 81d7a56 installer 46123505536 all six jobs SUCCESS; 1072 desktop + 42 migration PASS; audit cancelled pending. No merge or release.

E05/M04: suite offer is persisted only after successful visible Show, queued transitions recheck owner visibility/dispatcher shutdown and persisted offer; failed/aborted Show releases tracked slot. Guide and suite ignore queued language updates after closing; stop guide pulse and suite icon animations at close/unload. Existing passive suite catalog/release refresh uses cancellation token through existing network APIs and installed-app scan, once per window; no continuation UI update after close. Explicit user-initiated installer download unchanged. Three deterministic show-gate cases (690 cumulative authored, not passed on this candidate). Actual WPF lifecycle and network cancellation acceptance open; no new timers or service. Manual gaming override audit confirms existing journal restored under shared power lease before manual global GameMode/DirectX or Office/Gaming changes; no duplicate implementation, foreground reentry/manual override policy and dispatcher latency still open.

Exact parent e68d192b375b6bbcb606c3df05f65a1fb7265b4d installer 46123696123 pending; audit 46123696166 in progress at inspection. 81d7a56 installer 46123505536 in progress, audit cancelled pending. b2d143d installer 46123145532 and audit 46123145537 completed SUCCESS; 1050 desktop + 42 migration PASS. No release or merge.

M02/E03: AutoGamingModeService now marks recovery required on any failed apply/restore and reconciles protected journal under existing profile lease before subsequent application. Failed recovery does not clear pending state; successful reconciliation resets candidate/active debounce before another application. Stop attempts recovery for a started session even before first tick and leaves retry state on failure, without starting a disabled timer. Manual Office/Gaming mode bypasses foreground exit debounce. Existing journal comparison/readback, power lease, five-second opt-in timer and 30-second failure throttle retained; no new registry scope, no new timer. Six deterministic cases (687 cumulative authored, not passed on this candidate). Native registry fault injection, UI dispatcher latency, external edits and full manual override/concurrent testing remain open. No credential collection changes.

Exact parent 81d7a56aad01417e433fe3a9cfa1402b151639f6 installer 46123505536 build/offline jobs in progress at inspection; audit 46123505518 not completed. Intermediate 8a171df installer 46123343923 and audit 46123343924 cancelled while pending. b2d143d installer 46123145532 all six jobs SUCCESS; build 140911795684 logs 1050 desktop + 42 migration PASS, zero failures/skips. Audit 46123145537 source inventory success, baseline still running. No merge or release.

M03/M04: system SMART badge refresh separated from full health score/history; uses existing visible 20-second tick and existing 60-second SMART cache/single-flight, plus immediate resume refresh. Publication requires current topology, visible loaded view and uncanceled lifetime; stale SMART hidden on invalidation. Drive switch resets model/type/temperature/capacity until current read. Capacity read once per total/free value, rejects zero/negative/inconsistent values, clears stale display on failure and retries icon. Twelve deterministic cases (681 cumulative authored, not passed on this candidate). No new timer or extra health-history writes. Native notification coverage, physical identity reuse and visual/physical acceptance remain open. No new unrelated background service.

Exact parent 8a171df42da679ae07efdc54f47e69fb71fe84d0 installer 46123343923 and audit 46123343924 pending at initial inspection. Prior b2d143d installer 46123145532 offline clone passed; build-setup still in progress; audit source inventory passed, baseline in progress. Last fully verified dcd6b8f: 1042 desktop + 42 migration PASS, six installer jobs and audit success. No release or merge.

M03/M04: existing Home metadata slot has generation invalidation, clears all cached letters and discards stale in-flight results without opening another native request. SMART/temperature single-flight also reject old generation. Home attaches a device/power broadcast hook during Loaded, removes on Unloaded, invalidates on visibility resume and coalesces drive-list/placeholder refresh on existing visible 20-second cadence; retains selected drive where available, cancels and rejects outdated analysis. Ten new deterministic cases (669 cumulative authored, not passed on this candidate). No new timer/worker watcher/disk writes. Native notification coverage, UI refresh latency, physical disk-number reuse and complete SMART badge refresh after topology change remain unverified/open. Other backlog rows retained.

Exact parent b2d143d6da2ea187dd7067a9b3a7d835e3c10593 installer 46123145532 build-setup 140911795684 still running at inspection; offline clone passed. Audit 46123145537 in progress. Prior dcd6b8f installer 46121212415 six jobs success, 1042 desktop + 42 migration PASS; audit success. No release or merge.

M03: Home disk metadata now reads one volume mapping/descriptor and optional WMI model instead of independent GetDiskModel and Detect calls; checks disk number again after model query, refuses virtual/RAID and contradictory NVMe/seek model override. WMI model failure retains native type. Standalone model and SMART results also recheck mapping before publish. Eight deterministic cases (659 cumulative authored, not passed on this candidate). No new timer or disk writes; existing cache and concurrent admission retained. Disk-number reuse, cache invalidation/hotplug, native and physical acceptance remain open. No full stable-device identity claim.

Exact parent dcd6b8f1bc520ea34cfc0235cecf87a9fdc59783: installer 46121212415 six jobs SUCCESS; build 140905920416 logs 1042 desktop + 42 migration PASS, zero failures/skips. Audit 46121212413 SUCCESS. PR #6 not mergeable, no release.

E05/M04/M01: OncePerVersionStore no longer holds stale per-instance dictionaries; existing guide, suite and release notification consumers read current bounded 64 KiB state under one in-process gate. Reuses existing atomic WriteIfChanged helper with unique staging; no idle I/O, timer or path cache. ReleaseCheckService batches four related cache fields into at most one replacement, uses coherent request snapshot for 304 and current notification flags after HTTP. Unchanged values cause no write, oversized/inaccessible files not overwritten. Existing malformed JSON reset policy retained. Five new deterministic cases (651 cumulative authored, not passed on this candidate). Cross-process/external-writer atomicity, actual SSD performance and release-check race after final notification decision remain open. No credential code touched. E05 FAQ now describes actual migration location and limitations; all other backlog rows preserved.

Exact parent 532b7d987fae1ed998935bd53ee73e3604247fa2: installer 46110764992 all six jobs SUCCESS; build 140873557625 logs 1037 desktop + 42 migration PASS, zero failures/skips. Audit 46110764953 both source inventory and Windows baseline SUCCESS. No final native or physical certification; PR #6 not mergeable.

E05: TabLayout now validates every nested XAML section before removing any card; mismatches return false with the entire source view unchanged, as the existing contract specifies. Four deterministic authored cases (646 cumulative authored, not passed on this candidate). E04: ignore generated Python bytecode; exact parent tree contains no tracked bytecode or ai_build_agent script. Icon catalog notices already record 31 pinned entries; distribution rights and visual review remain open. Installer workflow now preserves running jobs, matching audit concurrency; pending jobs may still be superseded. E03 password/Wi-Fi collection and authorization untouched; presentation/lifetime review remains open. No new service, timer, version bump or merge.

Exact parent b11b87724935b0b85ce64885fe0af404fe1a7ad0: installer run 46110058317 all six jobs success; build job 140871427909 logs 1033 desktop + 42 migration PASS, zero failures/skips. Audit source-inventory passed; windows-baseline in progress at inspection (46110058321). Not physical or final acceptance.

Current authority: .NET 11 WPF, active branch release/7.1.0-ultra, draft PR #6; version metadata 7.0.0. Older technology, test totals and branches below are historical.

Read [START_HERE.md](START_HERE.md) and SINGLE ACTIVE BACKLOG in [ROADMAP.md](ROADMAP.md). E02 Bloatware usage and safe uninstall localization across 14 languages; other strings and all remaining categories stay open. E01 build succeeded. New CI pending.

---

# PROJECT STATE

## Project
Name: GearWin - Complete PC Care (WPF port of the original Optimizer.ps1)

## Technology
- .NET: net10.0-windows
- C#: WPF (OptimizerWpf.csproj), assembly/exe name `GearWin.exe`
- UI: WPF, `wpf/OptimizerWpf/` — Views (XAML + code-behind, no MVVM/ViewModels — direct code-behind is the established pattern here), Services (static classes), Themes/Styles.xaml. WPF-UI 4.3.0 nuget also in use for the standalone `DashboardPreviewWindow` pilot only (normal app/MainWindow untouched).
- Mobile: none
- Backend: none (fully local desktop app)
- Tests: `wpf/OptimizerWpf.Tests/` (xUnit-style, run via `dotnet test`), 221 tests in the default battery (all passing as of last verification), plus one opt-in UI Automation click-through test (`GEARWIN_UI_CLICKTHROUGH=1`, shows real windows - not run by default)
- Installer: Inno Setup, `installer/OptimizerWpf.iss` → `installer/Output/GearWin-Setup-<version>.exe`. Build: `dotnet publish -c Release -r win-x64 --self-contained true -o wpf/OptimizerWpf/publish/win-x64` then `ISCC.exe installer/OptimizerWpf.iss`. ISCC lives at `AppData\Local\Programs\Inno Setup 6\ISCC.exe` (not Program Files).
- 14 supported UI languages, all strings in `Services/LanguageService.cs` (huge file, ~18k lines, one dictionary block per language in fixed order: el, en, de, fr, es, ko, zh, it, ru, ja, pt, tr, ar, hi). New keys must be added to all 14 blocks (use a small Python script to insert by anchor-key line number, descending order, rather than hand-editing).
- Repo: `gs-applabs/GearWin` (renamed from giorstergiopoulos-design in 6.9.11) on GitHub. `gh release create` works from this environment (has previously needed a retry after an auto-mode permission block).

## Windows system image backup
- The recovery card confirms the selected backup volume, checks its disk against the Windows and boot disks, and queries the selected volume's image catalog. [Recovery and offline cloning workflow](docs/disk-backup-and-recovery.md). Native backup, replacement-disk boot and SSD measurements are pending.

## Current Objective
v6.3.5 (2026-10-06, branch `claude/full-audit` - this is the ACTIVE branch, NOT master; a separate
cloud session created it, this session verified it builds/runs on Windows for the first time and
continued work on it) implements the full 13-item GEARWIN.MD backlog: Games+Multimedia merged into
one tab (`Views/GamesMediaView`, replaces GamesView/MultimediaView), GPU-pref/FSO/exe-picker on one
row, shield icon dark-mode fill fix, third-party codec detection (K-Lite + DirectShow filter registry
scan, `Services/MultimediaServices.cs`'s `ThirdPartyCodecService`) + 3-column codec grid, converter
gained WebM/MKV/OGG/AAC/WAV/FLAC + Low/Medium/High quality (`MediaQuality` enum), tab strip scrolls
with mouse wheel when it overflows, searchbar modernized (rounded, icon, clear button), "Cumulative
Benefit" folded into the health-score message list instead of its own Home card, new Boost button on
Home (next to Full Health Check, mirrors PcManagerHomeView's), update-manager button now shows
"Installing" instead of "Scan" during installs, fixed a real bug where msstore-sourced winget rows
were mislabeled "winget" (causing failures + an unexplained Store popup), ESET/Defender status wording
fixed, Maintenance Center tabs now use the app's pill style (was default white Windows tabs), and pill
button padding increased app-wide (TabPillStyle/RailPillStyle/PillTabItemStyle). Built: `dotnet build`
0 errors, `dotnet test` 156/156, installer `GearWin-Setup-6.3.5.exe` built and sent to the user.
**NOT YET committed to git** (working tree only) - awaiting the user's decision on whether to commit/
push to `claude/full-audit` and whether/when to merge to `master` or create a GitHub release.

## Current Phase
Shipped, both branches kept in sync by repeated fast-forward merges (`claude/full-audit` -> `master`)
after every commit, latest 2026-10-07 (`e5d5f65`). Since the v6.3.5 ship (2026-10-06): .NET 10 upgrade,
title-bar icon/text enlargement, a `DashboardPreviewWindow` WPF-UI pilot (opens only via
`--dashboard-preview`, normal app untouched), two rounds of app-wide button-padding/sizing fixes (plain
tight-padding sweep, then a separate Grid Auto-column-compression fix), winget App-Execution-Alias +
onboarding-wizard autostart fixes with new diagnostic logging + status-bar visibility, and the
Maintenance Center one-line-tabs sizing fix - see Completed for details. Published binary
(`publish/win-x64`) re-built after every change (lesson: the `.bat` launchers prefer that folder over
`bin/Release`, going stale silently otherwise). Governance docs (CLAUDE.md, PROJECT_STATE.md,
ROADMAP.md) adopted 2026-09-29 at user's request.

## Completed
- 6.6.0 (2026-10-27): welcome guide (`WelcomeGuideWindow`, once per major.minor via `OncePerVersionStore`), suite showcase (`SuiteShowcaseWindow`, catalogue `SuiteCatalogService` + embedded `Assets/suite-apps.json`, SHA-256 verified downloads only after confirmation, never run), own new-version balloon (`ReleaseCheckService`, ETag, 30 s/24 h, once per version; removed from `UpdateNotificationService`). `OverlayCardHost` = shared minimise-into-window effect. Tests 327 (new `SuiteAndReleaseTests`, `WelcomeSuiteWindowTests` incl. 14-language fit). NOT verified live: real installer download, real newer release, balloon rendering by Windows, the startup flow in the real app (not launched: user's live PC). Queued for 6.7.0 (from the Opsis session): remove video/audio converter (conversion moved to Media Player Pro/Waveframe) + pointer to showcase, updater progress/cancel/filters/ignore list, keep sub-pages alive, accessibility names, README screenshots, tighter Home.
- 6.5.4/6.5.5 (2026-10-23): card regrouping (Games|Media, System>Devices; HAGS single switch; startup-delay button). Lesson: the WPF tests that open real windows (`NestedScrollTests`, `UiClippingProbeTests`, `GamingStartupGroupingTests`) failed once in a full run while other sessions were using the desktop and passed on rerun and in isolation - rerun before assuming a regression. `ReplaceUpdates` is tested through the pure `DeduplicateUpdates` (the static collection is bound to views of dead test threads).
- 6.5.3 (2026-10-23): updater state moved to shared `ScanState` (no more lost state / double scans / duplicate rows), `NestedScrollHelper` fix (wheel over inner lists), adaptive tab strip (`AdaptTabStripToWidth`), wrapped labels in the wide rail, themed TextBox/PasswordBox, status bar de-duplication. Probe-verified on the final build: 0 clipped button texts and 0 cut tabs in 4 layouts (default 946 and 1100 px, PC Manager 1000 px, Windows 11 Settings 1100 px), 14 languages, all 16 sub-pages (~5000-7700 button texts per layout); Classic/Office Ribbon/Gaming Hub were probed before the last two edits only. NOT verified live: a real winget scan/install across a tab switch (logic covered by unit tests only). Open proposals: keep views alive across tab switches, per-source scan progress/cancel, move Games-tab device cards (displays/audio/camera) to System, review the converter and fonts cards.
- 6.5.2 (2026-10-21): Help rewritten per new tab (old per-tab section keys removed), logos for all 29 recommended apps (LogoCacheService domains + test), static event handlers marshal to the owning thread, `LocalizationCoverageTests`. Help per-card texts that were not touched may still describe card internals in the old wording; README screenshots still pre-6.5.
- 6.5.1 (2026-10-21): header theme toggle button sized to its text (was clipped in el/ru); `HeaderLayoutTests` guards all 14 languages. Lesson: a FormattedText measure is needed in layout tests - TextBlock.DesiredSize is clamped by the available width and hides clipping.
- 6.5.0 (2026-10-21): 9-tab regroup done via composition, NOT by moving XAML: `TabLayout.cs` maps each view's root children to sections, `CompositeTabView` hosts filtered view instances per sub-page (tags unchanged; labels/order/icons in `NavItem.cs`/`MainWindow.xaml`). `TabLayoutTests` fails if a card is added without a section. Backup/Reports hub = Advanced tab; startup merged on Optimization#2 (the Maintenance Center startup-delay tab remains a separate window). Self-update from GitHub in Settings (`SelfUpdateService`, SHA-256 digest mandatory). `MemoryCleanerService` (RAMMap equivalent; elevated purge path not verified live). Health score buttons differentiated (Boost=RAM only). Known: Games save backup and Tweaks profiles stay in their own tabs; README screenshots still show the pre-6.5 layout; 14-language Help section texts were not rewritten per new tab (titles + tab map only).
- 6.4.0 "do all" proposals pass (2026-10-20): ROOT CAUSE of the months-long "button text touches edges" found by rendering views to PNG (RenderTargetBitmap probe, kept out of the repo): FlatButtonStyle's template ignored Padding - now `Padding="{TemplateBinding Padding}"` on the Border. Done: install icon (down arrow + line), Performance Modes merged into one card + persisted Office/Gaming state + restore point/journal, Reports hub (`ReportsCard`), Home security card, card-level global search (`MainWindow.BuildSearchIndex`), nightly winget updates (`AutoUpdateService`, `--auto-updates`), problem bundle (`ProblemReportService`), section headers in Optimization. NOT done (decided, documented): re-organising the 9 tabs into a new 9-tab layout (touches MainWindow nav, classic menu, PC Manager rail, onboarding, help, 14-language labels - needs the Dashboard Preview path and live review), Backup & Restore hub (would only be shortcuts), merging Startup (System) with the Maintenance Center startup-delay tab, Gaming Mode before/after "measurement" (the mode does not free RAM, a number would be misleading), install size/time estimates (winget does not expose them). Live click-through last passed before the final additions; later runs hang only because the user's screen was locked (foreground = lock screen). 221 tests.
- 6.3.9 Windows Update + test isolation (2026-10-15, user: "Windows updates exist but are not detected, see how WMT handles it"): REAL cause was the live click-through test persisting toggles into the user's real AppSettings.json (UpdateNotificationsEnabled=false -> background check skipped; I restored it to true in the user's file). Tests now isolated (`TestIsolation.cs` ModuleInitializer -> `GEARWIN_DATA_DIR` temp copy + `GEARWIN_TEST_HOST=1` -> App.OnStartup returns early with OnExplicitShutdown); the click-through skips native-file-dialog buttons. WMT mechanism ported: `WindowsUpdateService.ScanDetailedAsync/InstallAsync` (IsInstalled=0 and IsHidden=0, Online=true, history suppression, optional flag), rows (source `windowsupdate`, id `UpdateID|Revision`, whitelist-validated) in the Optimization update list, restart-required note after install. A real scan returned the 5 pending updates. Popup close-on-deactivate made idempotent. 219 tests. NOT verified: the install path (`InstallAsync`) was not run (it would really install updates).
- 6.3.8 correctness + performance pass (2026-10-14): Tweaks tab / Home no longer run every `DetectState` (registry + powercfg.exe) synchronously on the UI thread (`TweakRowVm.CreateDeferred/DetectAllAsync`, Home detects only pinned rows); vulnerable transitive SQLitePCLRaw 2.1.6 removed by Microsoft.Data.Sqlite 10.0.13 (`dotnet list package --vulnerable` clean); `CrashLogService` writes background-thread/unobserved exceptions to `%LocalAppData%\OptimizerWpf\crash.log`; autostart refusal for non-admin-only folders has its own message. Live UI-Automation click-through (opt-in, `GEARWIN_UI_CLICKTHROUGH=1`) re-run clean. Measured but NOT changed: eager 14-language dictionaries (~22.5k strings, ~90 ms JIT in tests, less under R2R) - MainWindow iterates all languages at startup anyway, so lazy loading would not pay off. 216 tests.
- 6.3.7 security + RAM audit (2026-10-14, user: "ελεγχος για ram leak, bugs, ασφάλεια - μην δημιουργούμε ευπάθεια στα Windows"): FIXED a leak I had introduced (ButtonAutoFit `AddValueChanged` never removed -> every recreated view leaked all its buttons; now Loaded/Unloaded symmetric); ActionLogService bounded(500)+locked+UI-thread events; update-check.log capped 256KB. SECURITY (app runs elevated): all PowerShell via `PowerShellArgs` `-EncodedCommand` (no quoting injection, no temp .ps1 for elevated run); `SecureStorage` = `%ProgramData%\GearWin` admin-only ACL for anything an elevated tool consumes (task XML, driver packages, assoc XML, ViVeTool - extracted from memory); `WingetService.IsSafePackageId` whitelist before `cmd.exe /c`; junction-safe QuickClean/save-backup; autostart task only if exe dir is admin-only-writable (so dev/Downloads copies no longer get a no-UAC elevated logon task); Gaming Mode no longer disables VBS/HVCI or force-enables it on exit (machines that used the old Gaming Mode may still have HVCI off - user can re-enable in Windows Security > Core isolation). Residual risk (documented in CHANGELOG): tools resolved via user PATH (ffmpeg/python/npm) run with app elevation. 212 tests. NOT visually verified.
- WMT sync 6.5 -> 6.7 + fixes (2026-10-13): msstore updates now use WMT's Store GUI automation (`StoreGuiUpdateService.cs`: UI Automation on Downloads & updates, MDM UpdateScanMethod first; unconfirmed result = "opened in Store", never a fake success - Greek status strings are GUESSED, verify on a real Greek Store); tray/clipboard/widget popups placed DPI-safely (`PopupPlacement.cs`, fixes wrong position at scaling > 100%); Clean Old Drivers ranks in-use > version > date (`DriverService.SelectOldDrivers`) and shows the kept package; winget failures show the real reason (`WingetService.DescribeExitCode`). 190 tests passing. NOT visually verified.
- Games & Multimedia additions (2026-10-07, user asked for suggestions then "κανε τα ολα" - do them
  all): checked each idea against existing code first (Game Mode toggle turned out to already exist
  in `GamingTweaksService.GlobalTweaks()` - skipped, not duplicated). Games: per-game tweak profiles
  (REQ-590-06, now DONE - `GameProfileService.cs`, stores which of the 4 machine-global gaming
  tweaks should be on for a given exe; GPU pref/FSO were already per-exe via registry so didn't need
  a new store; Save/Apply are explicit user actions, no process-watching/auto-apply since that's not
  verifiable here), launcher cache cleanup (new QuickClean "LauncherCache" category - Steam
  httpcache + Epic webcache only, never depotcache/downloading), GPU driver freshness hint in the
  Games library card (reads `OptimizationView.CachedVendorScan`, never triggers its own scan), game
  save backup (`GameSaveBackupService.cs` - zips ONLY the two documented generic Windows save
  folders, "Saved Games" + "Documents\My Games" - UI text says exactly that, not "all saves", since
  per-game paths can't be reliably guessed). Multimedia: batch conversion (Multiselect + loop),
  drag-and-drop onto the converter, live mic/camera test (opens the real Windows Camera app +
  Sound control panel's Recording tab instead of building unverifiable custom capture code),
  one-click codec pack (links to the official download page, not an unconfirmed winget id), HDR
  hint (same heuristic style as the existing high-refresh hint, BitsPerPixel>=30 - not a real
  DXGI HDR-active check). Added `GamesMediaViewConstructsWithoutThrowing` - this UserControl had
  ZERO smoke-test coverage before despite being the tab most changed here; a real gap, not a
  deliberate exclusion. 23 new language keys x 14 languages. 182 tests passing.
  **NOT visually verified** (no GUI automation in this session).
- Universal button text-touches-edge fix (2026-10-07, user explicitly demanded a systemic rule
  instead of more per-button guessing - "δεν μπορεις να βαλεις εναν universal kanona"): new
  `Services/ButtonAutoFit.cs` attached behavior, wired once onto `FlatButtonStyle` in `Styles.xaml`
  (`services:ButtonAutoFit.Enabled="True"`, inherited automatically by `AccentButtonStyle`). On
  `Loaded` and on every `Content` change (covers live language switching via `{tr:Tr}`'s binding),
  it measures the button's OWN text with `FormattedText` using its actual `FontFamily`/`FontSize`/
  `Padding`/`BorderThickness` and raises `MinWidth` to fit - automatically, per button, per language,
  for every button that exists today AND every button written from now on, with zero manual
  measurement. Never shrinks an existing larger `MinWidth` (the ~85 hand-picked values from the two
  earlier rounds below still work as floors; nothing needs removing, and nothing like them needs
  adding again). This is the actual root-cause fix for the Grid Auto-column-compression bug - the
  two rounds below were the stopgap, button-by-button version of what this now does for the whole
  app in one place. One real bug caught during implementation: `DependencyProperty.Register`
  requires the owner type to derive from `DependencyObject`; `RegisterAttached` is the correct API
  for an attached property on a static helper class - threw `TypeInitializationException` at
  startup until fixed (caught immediately by the existing `WindowSmokeTests`, not shipped).
  New `ButtonAutoFitTests.cs` (6 tests: grows to fit text+padding, longer text needs more width,
  never shrinks an existing larger MinWidth, raises an existing too-small one, ignores non-string
  Content, wider padding needs more width) locks the behavior in. 175 -> 181 tests, all passing.
  Build + tests verified; **NOT visually verified** (no GUI automation in this session).
- Maintenance Center tabs forced onto one line (2026-10-07, explicit user request: "οι καρτελες να
  ειναι σε μια ευθεια αρα προσαρμοσε το μεγεθος του παραθυρου αναλογα"): fixed `Width="760"` didn't
  always fit all 5 `PillTabItemStyle` tabs in a single row for longer-language labels. Added
  `MaintenanceCenterWindow.FitWidthToTabs()` (called from the constructor) - measures the widest
  5-tab-label sum across all 14 languages via `FormattedText` (using the style's known fixed
  Padding/Margin/FontSize/FontWeight constants - `TabControl` doesn't expose its internal tab strip
  as a named element the way `MainWindow`'s hand-built `StackPanel` strip does) and sets `Width`
  accordingly (clamped between `MinWidth` and 90% of the work-area width). Build + full test suite
  (175/175) verified; **NOT visually verified** (no GUI automation in this session).
- Button text-touches-edge fix, second root cause (2026-10-07, user screenshot of
  "Αναβάθμιση Επιλεγμένων" vs "Έλεγχος Ενημερώσεων μέσω Microsoft Store" - one had margin, the other
  didn't despite both having the same kind of `Padding`): this is a DIFFERENT bug from the plain-
  padding sweep below. Root cause: a `Button` sitting in a `Grid.Column` next to a sibling with a
  hardcoded `Width` inside a `Grid` that also has a `*` column - when the row's total desired width
  exceeds available width, WPF compresses `Auto` columns below their desired size as a last resort,
  visually "eating" the button's own correct `Padding` with no error anywhere. Fixed the reported
  button (`OptimizationView.xaml`'s `BtnUpgradeSelected`: `Width="180"` -> `MinWidth="180"`;
  `BtnCheckMsStore` gained `MinWidth="300"`) plus 17 more buttons across 8 files sharing the identical
  structural risk pattern (found via grep: `Grid.Column` child + `Padding`-only + sibling `*` column),
  each given an individually-sized `MinWidth` (not `Width`, to preserve room for longer translations).
  Build + full test suite (175/175) verified; **NOT visually verified**.
- Button padding, app-wide (2026-10-07, user flagged this repeatedly across the session): the 6.3.5
  fix only widened the Styles.xaml DEFAULT Padding on `FlatButtonStyle`/`AccentButtonStyle`/pill
  styles - it didn't help most real buttons, which set their OWN explicit (tighter) `Padding` per
  View, overriding that default entirely. Found 65+ instances of `Padding="8,4"`/`"12,6"`/`"14,6"`/
  `"10,5"`/`"10,6"` etc. across 16 files. Scripted sweep: every `Button` using
  `FlatButtonStyle`/`AccentButtonStyle` with horizontal padding < 16 got it bumped to 16 (vertical
  padding left untouched, so button heights/layout rhythm didn't shift) - 68 buttons fixed via regex
  sweep, +1 manual fix for a `<Button.Style>` property-element case the regex couldn't reach
  (`OptimizationView.xaml`'s pin button). Left alone on purpose: `TextBox`/`PasswordBox` `Padding="8,4"`
  (input fields, not the "text touches the button edge" complaint) and icon-only square buttons
  (`Padding="0"`, e.g. window chrome). Build + full test suite (175/175) verified.
- v6.3.5 polish (2026-10-07): fixed the real "no update check/balloon tip on autostart" bug - winget.exe
  is an App Execution Alias (reparse-point stub in `%LOCALAPPDATA%\Microsoft\WindowsApps`) whose PATH
  entry can lag right after logon, especially when launched via Task Scheduler's LogonTrigger (the
  `--tray` autostart) rather than explorer.exe; the bare `Process.Start("winget.exe", ...)` failed
  silently (`Win32Exception`), `WingetService.ScanAsync()` read that as "0 updates" (honest-by-design,
  but indistinguishable from a real empty result), so `UpdateNotificationService.StartupCheckAsync()`
  never had anything to announce - no error anywhere. Fixed with an absolute-path fallback in
  `WingetService.RunToolAsync` (covers every winget call site: scan/pin/export/import/upgrade). Also
  found and fixed a related bug while investigating: the first-run onboarding wizard
  (`App.xaml.cs`) could block the entire `--tray` silent-startup sequence (tray icon, update check)
  since it wasn't gated on `--tray` - now skipped on autostart.
  Expanded automated test coverage: `WindowSmokeTests.cs`/`UIAutomationClickThroughTests.cs` gained
  `MaintenanceCenterWindow`/`HealthTimelineWindow` (both real gaps, not prior exclusions);
  `ParseWingetTable` made `internal` (same `InternalsVisibleTo` pattern as `ThemedMessageBox`) and
  covered by new `WingetServiceTests.cs`; `GamesMultimediaServicesTests.cs` extended with the 6 new
  6.3.5 converter presets, a quality-level-actually-changes-the-args test, and a
  `ThirdPartyCodecService` never-throws smoke test. 156 -> 175 tests, all passing. **Honest limit**:
  this is unit/construction-level coverage, not literally "every tool verified" - genuinely
  destructive/slow actions (Optimize Now, ViVeTool toggles, real driver/winget installs) remain
  deliberately excluded from automated click-through (documented in that file), and the opt-in live
  GUI click-through suite itself could not be run by this session (no elevation/display available,
  same constraint as the Dashboard preview below).
- WPF-UI pilot: `Views/DashboardPreviewWindow.xaml(.cs)` (2026-10-06) - a standalone, additive "Πίνακας
  Ελέγχου" window built with the WPF-UI 4.3.0 nuget (ui:FluentWindow/TitleBar/Card/InfoBar/Button/
  TextBox), reusing the SidebarShortcuts/NavItems/RailPillStyle/PcManagerHomeView.BuildArc/TweakRowVm
  patterns already proven in the real app, wired to real data (HealthScoreService, UpdatesHubService,
  WingetService, pinned tweaks) - no fabricated numbers/names. Opens ONLY via `--dashboard-preview`
  (App.xaml.cs), the normal app/MainWindow is 100% untouched. Visual reference: user's screenshot of
  "PC Performance & Maintenance Suite v5.1.0" (see `[[project_optimizer_shell_v510_reference]]` memory).
  Build + full test suite (156/156) verified; **NOT runtime/visually verified** - this session cannot
  elevate (app.manifest requires Administrator, `Start-Process -Verb RunAs` would hang with no user to
  click UAC, see `[[feedback_avoid_runas_hangs]]` memory) - user needs to run
  `GearWin.exe --dashboard-preview` themselves and report back.
- Installer gained a "Start with Windows" task on the Additional Tasks page (2026-10-06, unchecked by default) - writes `LaunchWithWindowsToTray=true` into `AppSettings.json` via a bundled `installer/SetAutostart.ps1` (PS 5.1-compatible, merges rather than overwrites so upgrades keep existing settings); the app's own already-tested `SystemService.EnsureLaunchTaskAsync()` (runs every launch) then creates the real Task Scheduler task - no duplicated logic in the installer script. 13 installer languages (same set as `ResetPromptText`/`UpdateDetected` - the installer wizard doesn't have a Turkish translation, pre-existing gap, not part of this change).
- v6.3.5 shipped (2026-10-06) - see Current Objective for the full 13-item GEARWIN.MD list, plus the .NET 10 upgrade and title-bar enlargement. Merged `claude/full-audit` into `master`, both pushed. First time this branch's WPF Views were actually built+run-verified on Windows (prior `claude/full-audit` commits, per the "Audit pass" section below, were compile-checked Services-only on Linux).
- v5.11.0 shipped (2026-09-30): REQ-590-01 (Fonts moved from Settings to System tab - it's inventory, not a preference), REQ-590-02 (saved Wi-Fi password viewer in Network & Security, using native WLAN API rather than locale-fragile netsh text parsing - this machine is Greek-locale, English-string parsing would have silently broken), REQ-590-03 (settings backup/restore - export/import AppSettings.json, validates before overwriting).
- v5.10.1 shipped (2026-09-30): fixed v5.10.0's opacity slider, which the user correctly reported produced no real transparency (just darkened/lightened the window's own color). Root cause: WPF's `Window.Opacity` requires `AllowsTransparency="True"` for genuine see-through transparency, which itself requires giving up native OS window chrome (`WindowStyle="None"`). Confirmed via git history that the *original* `Optimizer.ps1` (WinForms) had real transparency natively (WinForms doesn't have this restriction) - lost silently during the pre-session PowerShell→WPF port. Fixed properly, after explicitly asking the user given the real tradeoffs (forced software rendering, losing native chrome): `MainWindow` is now borderless with `shell:WindowChrome` restoring move/resize/maximize/edge-snap and the "maximized covers taskbar" fix automatically, plus new custom-drawn Minimize/Maximize/Close buttons. **Not visually verified by the assistant** (no GUI automation for a native, admin-elevated desktop app in this session) - explicitly flagged for the user to test by hand. Known limitation: Windows 11's snap-layout hover flyout on the maximize button doesn't appear (basic edge/keyboard snapping still works).
- v5.10.0 shipped (2026-09-30): REQ-580-05 — ported MotionDeskStudio's opacity mechanism (separate project at `<MotionDeskStudio repo>`, per user's explicit request after providing that path). Replaced the old 3-option radio-button opacity (None/Light/Medium) with a continuous 60-100% `Slider` with live drag preview, matching MotionDeskStudio's own `BuildOpacityRow` UX exactly. New `AppSettingsService.WindowOpacityPercent` (int) replaces the old string `WindowOpacityMode`; dead language keys removed. One real bug found and fixed during implementation (XAML-wired `ValueChanged` firing mid-`InitializeComponent()` before a sibling element existed) — caught by the existing passive smoke test.
- v5.9.0 shipped (2026-09-30): REQ-580-02 (Font management — new "Fonts" tab in Settings: live preview of every installed system font with user-typed text, plus 12 suggested free/open-license fonts linking to their official Google Fonts page — no font files hosted by us, licensing handled by Google Fonts). REQ-580-01 extended significantly: `UIAutomationClickThroughTests.cs` now does genuine UI-Automation-driven click-through (real `System.Windows.Automation` APIs, in-process, opt-in via `GEARWIN_UI_CLICKTHROUGH=1` — NOT part of default `dotnet test`, since it shows real windows and clicks on the live desktop). Getting this safe took several live-debugging rounds, each a real finding: a cross-thread Invoke()-with-timeout design corrupted WPF's native message loop and crashed the test host (~13 min); the automation tree included native title-bar chrome (clicking "System Menu" opened a real blocking Win32 menu); `ThemedMessageBox.Show()` genuinely calls `ShowDialog()` (~108 call sites app-wide) requiring a generic auto-dismiss watcher instead of guessing which sites to skip; `UwpAppManagerWindow`'s "Scan" (real WinRT `PackageManager` enumeration) reproducibly crashed the test host and had to be excluded. Final state: clean ~19s pass, 0 real findings. REQ-580-03 (Spotify/YouTube downloader) dropped entirely per user after declining to build it (DRM circumvention / distributed-piracy-tool concerns) — see ROADMAP.md.
- Committed + pushed + released on GitHub as v5.9.0 with installer attached.
- v5.8.0 shipped (2026-09-29): REQ-570-06 (unified Chromium-family browser password manager — Chrome/Edge/Brave/Vivaldi/Opera/Opera GX, DPAPI+AES-GCM decrypt of local "Login Data" SQLite, new `Services/PasswordVaultService.cs` + `Views/PasswordManagerWindow.xaml`, opened from Network & Security), REQ-570-10 (ViVeTool curated list expanded 10→15 features), a full pop-up text-truncation audit across all 14 secondary windows, REQ-580-06 (Chinese/Korean/Indian flag icons fixed — were missing canonical elements), REQ-580-04 (Help window refreshed), REQ-580-01 first pass (`WindowSmokeTests.cs` — passive construction-only smoke test, still kept alongside the newer click-through test).
- v5.7.0 shipped (2026-09-29): REQ-570-01 (msstore "false success" fix — bucketed as opened-in-Store, not counted success/fail), REQ-570-02 (immediate update check on `--tray` launch), REQ-570-03 (Ultimate Performance toggle moved into Optimization tab), REQ-570-04 (Registry Cleaner card missing top margin, HealthView.xaml), REQ-570-05 (Opera GX browser-cache support added), REQ-570-07 (Brave/OBS/Telegram/Everything/ShareX/LibreOffice added to Recommended apps), REQ-570-08 (ThemedMessageBox icon Foreground now set per icon type — dark-mode visibility fix), REQ-570-09 (Settings sidebar icon Palette → Gear), REQ-570-11 (Skins tab merged into Theme Settings tab), REQ-570-12 (new first "Settings" tab: Desktop Widget + Update/Low-disk notifications + new "Launch with Windows to Tray" toggle, `SystemService.SetLaunchWithWindowsToTray`).
- v5.6.0 shipped (2026-09-29): Windows-toast update notifications, low-disk-space alerts, cumulative "bytes freed" + boot-time-trend card on Home, Health Check PDF export historical trend, "Appearance Settings" renamed to "Settings" (first pass), Run_OptimizerWpf.bat build-path fix.

## In Progress
Nothing mid-flight.

## Blocked
Nothing currently blocked.

## Next Actions
1. Decide with the user: create a GitHub release for v6.3.5? (merge/push already done, see Current Phase)
2. REQ-590-05 (app-usage bloatware suggestions) and REQ-590-06 (per-game Tweaks profiles) remain NOT STARTED (carried over, not part of GEARWIN.MD v6.3.5).
3. Manually verify by hand (no GUI automation available in this session): the new Boost button's visual placement, the modernized searchbar, the tab-strip wheel-scroll, the enlarged title-bar icon/text, and that the window's new size constants (estimated, not runtime-measured) actually look right for the 9-tab strip in every language.

## MotionDeskStudio note
Separate project, separate repo, at `<MotionDeskStudio repo>` (own `.sln`, own git repo, `MOTIONDESK_MASTER_PROPOSALS_AND_ROADMAP.md`) — path confirmed by the user 2026-09-30 when asked. REQ-580-05 read its opacity mechanism (`src/UI/MainWindow.cs`) and ported the UX into GearWin (see Completed). No other MotionDeskStudio work has been requested or done from this session — still a distinct project with its own state, don't mix contexts without an explicit ask each time.

## Important Decisions
- 2026-09-29/30: MotionDeskStudio is a **separate project** — never mix its context with GearWin by default. Its path is now known (see above) but that doesn't imply standing permission to act on its own backlog; each cross-project task (like REQ-580-05) should still be a specific, confirmed ask.
- New service files must be checked against existing ones before creation (`Grep` for the class name) — see Known Bugs below for why.

## Known Bugs
- (User-reported, v5.7.0 backlog, not yet investigated) Software Update Manager can show an "installed" success message without the winget install actually having succeeded — check exit-code handling in the winget install call sites (OptimizationView.xaml.cs / BloatwareView.xaml.cs).
- (User-reported, v5.7.0 backlog) Dark-mode rendering issue with the square icon in the install-success popup.

## Known Limitations
- Windows Update scan (`Services/WindowsUpdateService.cs`) uses the `Microsoft.Update.Session` COM API — search only, no install; can fail under WSUS/Group-Policy-managed environments (best-effort, silently returns unsuccessful).
- Startup-item "impact" (High/Medium/Low, System tab) is a real but session-observed estimate (time since explorer.exe start), not a guaranteed prediction for the next boot — see `SystemService.GetStartupDelayEstimates`.

## Relevant Modules
- `Services/UpdateNotificationService.cs`, `Services/UpdatesHubService.cs`, `Services/TrayIconService.cs` — background update/low-disk notifications.
- `Services/AppSettingsService.cs` — all persisted user settings (JSON, `%LocalAppData%\OptimizerWpf\AppSettings.json`).
- `Services/LanguageService.cs` — all UI strings, 14 languages.
- `Views/HomeView.xaml(.cs)`, `Views/OptimizationView.xaml(.cs)`, `Views/SystemView.xaml(.cs)`, `Views/HealthCheckWindow.xaml(.cs)` — largest/most frequently touched views.

## Last Verification
Build: `dotnet build -c Release` (net10.0-windows) — succeeded, 0 errors.
Tests: `dotnet test` (OptimizerWpf.Tests) — 182/182 passed, including the 14-language key-parity test, `ButtonAutoFitTests`, and the new `GamesMediaViewConstructsWithoutThrowing`.
Publish: `dotnet publish -c Release -r win-x64 --self-contained true -o publish/win-x64` (run from `wpf/OptimizerWpf` - NOT `../../publish/win-x64`, a path mistake made once this session that left `Run_OptimizerWpf.bat` launching a stale build; fixed, confirmed via `publish/win-x64/GearWin.exe` timestamp matching the latest build).
Installer: `ISCC.exe installer/OptimizerWpf.iss` — succeeded, `GearWin-Setup-6.3.6.exe` (v6.3.6 released 2026-10-14), sent to user.
Date: 2026-10-07.
**Still NOT visually/manually verified** (no GUI-automation/elevation capability in this session, all UI verification depends on the user testing and reporting back): v6.3.5's UI changes (Boost button, searchbar, tab wheel-scroll, shield icon, codec grid, converter quality combo), the Dashboard Preview pilot window, both rounds of button-padding/sizing fixes, the Maintenance Center one-line-tabs fix, and the v5.10.1 borderless-window rebuild (drag, resize, maximize, edge-snap, real transparency).

## Last Updated
2026-10-07

## Audit pass (2026-10-01, cloud session, branch `claude/full-audit`, NOT merged, NOT verified on Windows)
Compile-checked only (Services/* via scratch project on Linux; Views/XAML not buildable there). Needs `dotnet build` + `dotnet test` + manual click-through before release.
- Data-loss guards: FolderLock verifies the vault (GCM tag) before shredding the original and refuses drive roots/system/profile folders; duplicate delete always keeps one copy per group and skips junctions; registry cleaner aborts if `reg export` fails and uses unique backup names; hosts file written as UTF-8 (ASCII broke the Greek telemetry markers); TweakBackup entries removed after restore; service restore uses `Automatic` (not `Auto`).
- Hangs/leaks: redirected pipes drained (winget/store/driver/backup/wsl/system/vivetool), `Kill(true)`; HomeView PerformanceCounters disposed; Process objects disposed; ViveTool now installs into %LocalAppData%\OptimizerWpf\tools\vivetool (never into the app dir).
- App: single-instance mutex (+ installer AppMutex), error dialog throttled (30s), atomic AppSettings/TweakBackup saves, passwords/Wi-Fi keys auto-cleared from clipboard (30s), residual-folder search needs >=4 chars.
- Locale: added missing `Tweaks_ConfirmTitle`/`Tweaks_VisualEffectsConfirm` (14 languages). Parity script: all 14 languages have identical key sets (License_* intentionally el-only).
- Round 2 (same branch): powercfg — Ultimate Performance now really activates (/setactive) and is detected/removed via the GUID we created + previous scheme restored (old code searched the template GUID / English plan name; never activated); P1/P2 power tweaks apply with `/S SCHEME_CURRENT`. Tray — popup no longer reopens on the same click that closed it, ping throttled (10s, non-overlapping), quick-clean exceptions caught. Views — Gaming/Office mode + Ultimate toggles off the UI thread; winget upgrade loop / driver scan / driver backup+restore wrapped in try/finally (buttons no longer stuck); deep-uninstall only offers residual-folder deletion after exit code 0/3010; driver-store delete no longer uses `/uninstall /force` (could strip the active driver of a device); tweak toggle reverts on failure; firewall toggle reverts on failure; temp cleanup in Home 'Fix all' off the UI thread; process kill verifies name (PID reuse); Process objects disposed. Privacy — clipboard history now honours `ExcludeClipboardContentFromMonitorProcessing`/`CanIncludeInClipboardHistory=0` and is DPAPI-encrypted at rest (was plaintext JSON incl. copied passwords).
- Round 3 (same branch, compile-checked Services only): tweak profiles/pins now language-independent — `SimpleTweak.Id` (RegTweak.BackupKey or explicit id), `TweakRowVm.PinKey = list:Id`, legacy label-based keys still accepted/migrated (pins, `ApplyProfile`). Old exported profiles keep working. AppearanceSettings: language combo selected by Tag (was resetting es/it/ru/zh/ja/pt/ko/tr/ar/hi to Greek on open), `_loading` guard for XAML-wired handlers, opacity save debounced, tray/widget try/catch. HealthView: `BusyScope` guarantees SetIdle, tool re-entrancy guard, try/catch on refreshes. HealthCheckWindow: close via X cancels scan, optimize try/finally.
- Still not audited: Optimizer.ps1 (legacy). NOT verified on Windows (Views need build + click-through).

- 6.2.5 follow-up (2026-10-02): window transparency with a border. Layered-alpha on the native frame (WindowOpacityService) was never confirmed on Windows, so the DEFAULT is now own frame: AllowsTransparency + WindowChrome + own min/max/close buttons + 1px CardBorderBrush outline + work-area WM_GETMINMAXINFO hook; opacity = Window.Opacity (always live). `NativeWindowFrame` (new setting, Appearance checkbox, restart) restores the native frame with best-effort layered alpha. Old `BorderlessWindow` setting unused. NOT verified on Windows (compile + 90 tests only).

- REQ-590-04 (2026-10-02): System tab card "System health timeline" -> `Views/HealthTimelineWindow` + `Services/HealthTimelineService` (Event Log, last 30 days: Kernel-Power 41 / EventLog 6008 / BugCheck 1001 / Application Error 1000 + MsiInstaller 11707 / UserPnp 20001; installs <=48h before a BSOD/unexpected shutdown are flagged as suspect). 10 `Timeline_*` strings x 14 languages. Read-only. NOT verified on Windows (real Event Log queries/XPath). Still NOT STARTED: REQ-590-05 (usage-based bloatware suggestions), REQ-590-06 (per-game tweak profiles).

## 6.9.10 / 6.9.11 (re-created 2026-11-18 from the written specs; the original local commits were lost)
- 6.9.10: ViVeTool exact GitHub host check + SHA-256 digest check, driver catalog GUID check, Help version history without the duplicate 6.9.0 card (newest first).
- 6.9.11: GitHub account renamed giorstergiopoulos-design -> gs-applabs everywhere (trusted-URL checks accept both names via `SuiteCatalogService.LegacyOwner`); consistency test in `Release6911Tests`.
- Test isolation: `App()` sets `ShutdownMode.OnExplicitShutdown` under `GEARWIN_TEST_HOST` (the default OnLastWindowClose shut the shared Application down after the first window test closed its last window, so later WelcomeSuiteWindowTests failed with "Cannot set Owner"). Cause inferred from code, confirmed only if the full suite passes.
