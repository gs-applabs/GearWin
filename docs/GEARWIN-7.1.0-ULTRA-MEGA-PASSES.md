# GearWin 7.1.0 — Ultra Mega Passes: cumulative progress and findings

Baseline: `0d36ba06ffd5b1f90676d546d8abdfafe1062fbf`; review branch `release/7.1.0-ultra`; draft PR #6. All counts below refer to distinct, explicitly listed issues, not lexical matches or test counts. "Source mitigated" means committed, NOT runtime verified.

## Pass summary (2026-11-27)

| Pass | Distinct recorded findings | Source mitigations | Runtime-certified fixes | Status |
|---|---:|---:|---:|---|
| UMP01 source audit | 3 test/measurement coverage gaps | 1 infrastructure mitigation (measurement workflow added) | 0 | open |
| UMP02 safety, batches 1–12 | 107 safety/correctness findings | 107 addressed at least in part in source | 0 operation-level certifications | open |
| UMP03 core/resources, batches 1–11 | 83 findings | 83 addressed at least in part in source | 0 | open |
| UMP04 unified maintenance, batches 1–2 | 24 findings | 24 addressed at least in part in source | 0 | open |
| UMP05 central operation core, batches 1–7 | 39 findings | 39 addressed at least in part in source | 0 | open |
| UMP06 status and navigation, batch 1 | 9 findings | 9 addressed at least in part in source | 0 | open |
| UMP07 early artwork fix | 8 distinct icon/UI findings | 8 addressed at least in part in source | 0 visual certifications | open |
| Total | 273 | 271 at least partly addressed | 0 operation-level certifications | open |

The earlier manual review also identified deferred issues in profiles, resources, backups and navigation. These will be assigned stable IDs in their owning passes; do not inflate totals by counting them again. Full tracked-source lexical inventory completed; manual semantic review remains pending. The 7.0.0 baseline Windows build reported 391 desktop + 42 migration tests on 2026-11-27; the Linux job logs available are from 2026-11-23 (5 synthetic clone tests). Six CI jobs reported success in the run; an opt-in test can report pass without doing clicks. Baseline success is not a new branch test result.

## UMP01 — audit coverage gaps

| ID | Evidence | Severity | State |
|---|---|---|---|
| A01 | UIAutomationClickThroughTests returns early unless GEARWIN_UI_CLICKTHROUGH=1; routine CI does not set it | high | open |
| A02 | Click-through factory covers eight windows, omits main UI and excludes multiple actions; smoke construction is not action verification | high | open |
| A03 | Existing installer E2E starts process and checks tier logs, but does not measure CPU/RAM/I/O/handles | medium | baseline workflow completed, raw samples archived; numeric results not retrieved |

Audit infrastructure commits b42ffe2ba6e63c060f17952de9b4559a27ec259f and 5ecb3db04b7e11285200842ba0d24a45da03f5c0. Workflow reports process transfer bytes rather than physical SSD wear. Source inventory is lexical, not a semantic audit.

## UMP02 — initial Windows safety corrections

| ID | Existing finding | Severity | Initial source mitigation |
|---|---|---|---|
| S01 | Health Check preselected bulk restore of personal tweaks | critical | removed bulk restore from Optimize, review route to Tweaks; safe manual bulk restore still needed |
| S02 | Health Check preselected all registry candidates | high | registry findings review only, dedicated manual cleaner |
| S03 | Recycle Bin preselected for Quick Clean | high | opt-in |
| S04 | recursive size scan did not reject reparse roots | high | reject reparse roots during scan and cleanup; nested paths/TOCTOU remain |
| S05 | unquoted executable commands split on first space | high | conservative parser; ambiguous paths skipped |
| S06 | inaccessible/nonlocal/offline paths treated as absent | high | only local ready fixed volumes, ancestor checks, fail closed |
| S07 | registry export names could collide across sessions | high | unique session GUID prefix |
| S08 | stale registry findings deleted without refreshing | high | fresh scan before backup/delete; race and value identity still need stronger validation |
| S09 | missing keys/values could be silently treated as successfully deleted | medium | fail if key missing or deletion fails |
| S10 | broken shortcut tool removed .lnk without checking hotkeys or target availability | high | scan-only interim containment; restore explicit review/quarantine and deletion later |
| S11 | restore point failure ignored before risky registry operations | high | Health Check no mutation; dedicated cleaner aborts on Failed |
| S12 | registry UI removed all selected findings even after partial failure | medium | rescan remaining entries instead |

Changes: HealthCheckWindow.xaml.cs, HealthCleanupService.cs, QuickCleanService.cs, HealthView.xaml(.cs); MaintenanceSafetyTests.cs adds 10 non-destructive theory cases. None of these fixes is yet runtime-certified. No new end-user features. S01/S10 are temporary safety containment, not complete preservation of original bulk actions.

## Integration and remaining blockers

- The baseline validation branch advanced three commits to 80ca071092c2613a489b5160cd709d36b9d444b0 (15 files; backup and offline clone enhancements, including Partclone adapter). PR #6 is mergeable as last checked, but branch must incorporate those commits and be retested before release. Do not overwrite or reset the validation branch.
- Run 45986429644 completed source inventory and Windows baseline successfully. Batch 2 run 45987821418 succeeded (407 desktop + 42 migration tests; process measurements below). Batch 3 run 45989300814 succeeded (412 desktop + 42 migration tests). Batch 4 and early UMP07 require fresh CI. No operation-level safety certification yet.
- Whole-key registry export/restore can overwrite unrelated subsequent changes, and stale scan matching does not guarantee unchanged registry value identity. Need precise value snapshots, transaction boundaries, revalidation and failure-injection tests.
- Further safety work: exact Gaming/Office profile rollback, registry backup persistence, shortcut per-item review and restore, scheduled maintenance using shared file safety, accurate deleted-byte accounting, backup restore verification, least privilege, app settings/profile persistence, external command security.
- Remaining passes: 03 core/resources; 04 unified maintenance; 05 consolidation; 06 status/navigation; 07 six skins and twenty themes; 08 functional regressions; 09 endurance/performance; 10 release. No new release or merge authorized by these audit commits.

## UMP01 evidence, run 45986429644 (PR merge checkout b5fd2962c7c896301010654a102bebd937792323)

380 tracked paths, 296 text source files, 88,655 scanned lines, zero missing paths. Lexical categories: registry 124, file mutation 200, subprocess 205, error handling 867, UI commands 418, timers/lifecycle 45, input 80, privilege 85, status 168. These overlap and are NOT counted as defects. Windows test report: 402 desktop / 42 migration passed, build and normal/tray sampling step successful. The PR merge checkout includes changes from the advancing base; this is not a pristine 7.0.0 performance baseline. Raw sample artifact 15191928758; inventory artifact 15191363717. Numeric metrics not yet read; latest workflow adds a printed root-process summary, not physical SSD wear. Full GUI clicks remain unverified.

## UMP02 — second safety batch

| ID | Finding | Source mitigation | Remaining validation |
|---|---|---|---|
| S13 | game-save backup deletes old archive before new archive is complete | stage on same directory, replace only after verification | failure-injection tests |
| S14 | locked/unreadable saves silently skipped and reported as success | strict enumeration/reads, fail without replacing | coverage of missing folders/cloud files |
| S15 | no archive read-back verification | SHA-256 per entry from streamed read and ZIP read-back | actual restore |
| S16 | output inside input could be included in archive | refuse destination under source root | canonical/reparse ancestor cases |
| S17 | backup root and file reparse points unchecked | reject redirected directories/files | reparse ancestor/TOCTOU |
| S18 | disabling profiles guessed defaults instead of restoring previous values | durable allowlisted snapshot before mutation, compare before restore | process interruption and legacy installs |
| S19 | profiles changed unrelated SmartScreen, HAGS, capture, desktop, network/priority settings | remove unproven/unrelated writes; Office power plan only; Gaming power plan and Game Mode | measure benefits; prior settings cannot be reconstructed |
| S20 | profile disable killed Explorer processes | no Explorer restart | Windows/OPSIS verification |
| S21 | requested restore point failure ignored before Gaming, Office lacked guard | abort manual transition on Failed | automatic flow review |
| S22 | profile error concealed and UI could apply concurrent switches | surface error, disable both controls, reconcile persisted ownership on failure | async and rollback failure tests |
| S23 | automatic Gaming could undo or override manual profiles | manual settings take precedence and auto ownership relinquished | transition races and crash recovery |
| S24 | fullscreen heuristic also matched ordinary apps | require explicit saved game profile in interim | broaden via existing game library after safe detection |

Changes: GameSaveBackupService.cs, PowerModeService.cs, AutoGamingModeService.cs, OptimizationView.xaml.cs, MaintenanceSafetyTests.cs (5 additional theory/fact cases), audit workflow summary. New batch source commits 6115615581c1650b437122c2edc3cbc093ed6d99 through f22a14c7de7ea37c2309d4a097a940ecc1619240. No new batch test results yet. Profile snapshot applies to future activation only. It cannot infer pre-existing values from 7.0.0, and compare-before-restore cannot detect a later independent write of the same value. Old profile tweaks deliberately excluded until justified by evidence. FolderLock destructive flow, schedule cleanup and registry transaction safety still open. No merge or release.

## UMP02 — batch 2 CI confirmation and resource reference

Run 45987821418: both source inventory and Windows job succeeded, 407 desktop and 42 migration tests passed, installer build and samples succeeded. This is the intermediate PR merge build, not a pure 7.0.0 baseline. 13 samples/mode over ~60 s after warmup:
- normal launch: 2.365% machine-normalized mean root-process CPU, maximum working set 272.04 MiB, maximum private memory 158.01 MiB, read transfer 654 B, write transfer 2388 B, handles 989 -> 1104.
- tray launch: 0.065% CPU, maximum working set 175.09 MiB, maximum private memory 87.01 MiB, read transfer 217 B, write transfer 0 B, handles 936 -> 1029.
Short-run handle growth is not proof of leakage; longer tests needed. Transfer counters are not physical SSD writes.

## UMP02 — third safety batch (16 distinct findings)

| ID | Finding | Source mitigation | Outstanding validation |
|---|---|---|---|
| S25 | Scheduled maintenance recursively deleted arbitrary environment TEMP content, bypassing normal guards | same checked cleanup engine; verified user temp, old top-level files only | scheduler scenario |
| S26 | Scheduled maintenance disable always reported true even when task deletion failed | return actual scheduler result | task state verification |
| S27 | Scheduled maintenance log grew without limit | rotate at 1 MiB | concurrent write test |
| S28 | Cleanup removed recently modified and potentially active temporary files | age gates and exclusive-access probe | installer/browser concurrency and race tests |
| S29 | Reported freed size was pre-scan size or unrelated free-space delta | report observed logical removed bytes instead | exact allocated bytes and partial failure reporting |
| S30 | Health Check duplicated browser cache scan | reuse existing scan result | timing comparison |
| S31 | Separate browser recursive deletion lacked ordinary-path checks | share conservative checked file cleanup | reparse ancestor/TOCTOU |
| S32 | Vault creation allocated several whole-folder buffers without limits | 64 MiB input cap, bounded entry count, background operation | memory measurements and streaming format later |
| S33 | Folder Lock automatically destroyed originals despite incomplete filesystem snapshot | verified encrypted copy, preserve originals; explicitly disclose to user | safe full lock/restore semantics before release |
| S34 | Vault extraction overwrote existing files | restore to new staging directory then move; no overwrite | restore failure injection |
| S35 | Malformed vault header and expansion size unchecked | header and available RAM/disk checks; old vault format retained | large legacy vault compatibility |
| S36 | Shredder could overwrite shared hard-linked file contents or protected paths | check link count on opened handle and refuse protected/reparse paths | hardlink and handle identity tests |
| S37 | Vault progress called SetBusy repeatedly, leaving stale active entries | one existing StatusService scope per operation | UI failure and progress tests |
| S38 | Windows Temp was a recommended automatic cleanup category | make opt-in, use canonical Windows directory and age gates | Windows servicing compatibility |
| S39 | Default shredder performed three overwrites even where secure erase is not guaranteed | default one pass, retain SSD caveat | SSD wear and truthful UI labels |
| S40 | Opera/Opera GX cache paths were whole profile roots, risking bookmarks/login data | shared explicit cache-subdirectory allowlist, regression fixtures | real Opera/GX installed-profile tests |

New batch tests: 5 cases (aged/locked cleanup, vault round trip, malformed vault, Opera and Opera GX). Total new cases recorded across UMP02: 20. Source commits 9670cb004ba068ef6fb8fc3191dac8309bf8ee3d through e37791f3a747f5b2a58b26075eb44b2b72da1917. Batch 3 run 45989300814 passed; the five additional fixture tests are included in the 412 desktop tests. Folder Lock interim deliberately produces an encrypted copy and does NOT lock the original; new archive creation is capped at 64 MiB. Legacy archives can be read with adequate RAM and disk space, but full compatibility is not yet certified. English fallback for new safety strings in non-Greek languages until localization pass. Failed staged extraction can leave partial output, not overwrite original data. Path checks/probes do not eliminate adversarial substitution races. Registry precise restoration, full rollback of historical tweaks, hardlink tests, and safe scheduled cleanup integration tests remain open. No merge or release.

## Batch 3 CI confirmation

Run 45989300814: both jobs successful, 412 desktop + 42 migration tests. Opera/GX, temporary file and vault regression fixtures passed. Normal/tray root CPU 6.338% / 0.207%; max working set 251.76 / 163.07 MiB. Short samples are not a controlled improvement comparison; UMP03 must investigate normal mode CPU, and UMP09 must validate sustained workloads. I/O transfer counters are not physical SSD writes.

## UMP02 — fourth batch, 3 additional process/identity findings

| ID | Finding | Source mitigation |
|---|---|---|
| S41 | Scheduler CLI could wait indefinitely | 30-second bounded wait; failure on timeout |
| S42 | Recommended installed-package scan could wait indefinitely | bounded read-only winget checks; no interactivity |
| S43 | Installed detection accepted exit zero plus absence of English error string | require exact package ID in output; title match no longer substring-based |

Timeouts must not be interpreted as confirmed absence. The old boolean installed status contract still needs a distinct Unknown state before certification. No mutation-level test yet.

## UMP07 — early requested correction of recommended application icons, 8 findings

| ID | Finding | Source mitigation |
|---|---|---|
| I01 | Website/publisher favicon did not reliably identify the product | per-package pinned product PNG catalog (29 recommendations) |
| I02 | Persistently cached incorrect favicon survived upgrades | ignore legacy LogoCache completely |
| I03 | Byte-length heuristic accepted unrelated/invalid icons | build-time PNG/dimension checks and upstream SHA-256 where available; decode tests |
| I04 | Asynchronous network logo could replace local icon | one authoritative embedded asset, shared frozen-image task per ID |
| I05 | Substring title lookup could identify a different application | exact title or narrow version/architecture suffix; no heuristic replacement artwork |
| I06 | Opera GX and Vivaldi lacked dedicated cache-card icon mapping | two extra package-specific assets and aliases |
| I07 | Generic package glyph showed through transparent logo | glyph visible only when logo absent |
| I08 | Parallel favicon downloads, retries and cache writes on use | no runtime icon networking, small normalized embedded images |

Asset catalog: 31 sources total (29 existing recommendations + 2 browser-only). 27 have SHA-256 from the pinned upstream manifest; four have immutable commit URLs but still need independently reviewed/pinned source hashes. Build preparation script writes source and normalized hashes, contact sheet and normalized icon pack. No user-visible feature additions. New RecommendedIconTests contains 20 case instances; cumulative newly authored cases 40. Updated existing Edge/Teams test to check distinct package-specific assets. New changes not yet tested on the final commit. Catalog metadata and source mapping do NOT prove all assets visually correct. Build and visual review must succeed before release; source ZIP must include normalized assets, plus notices and applicable source rights review. Registry precise restoration remains open; earlier temporary safety restrictions are not final.

## Icon verification (run 45991483809)

Windows icon preparation passed, 31/31 PNG assets downloaded, checked and normalized. Desktop 432/432 and migration 42/42 tests passed with required icons enabled. Installer stage was canceled by newer commits (not a failure or a completed installer run). Four previously unpinned source hashes were captured from the VM logs and added to the catalog in 53d2d93d8fb43fc4ecdc11f44ca7bf591f8c8722. All 31 now have expected SHA-256, 27 upstream manifest hashes and four observed hashes. This verifies decoding and package mapping, not human visual review or trademark clearance. The final pinned build remains to be tested.

## UMP02 — fifth batch (3 new findings)

| ID | Finding | Source mitigation |
|---|---|---|
| S44 | Whole-key .reg restoration overwrites unrelated/current values | per-value JSON backups; restore only absent or already identical values |
| S45 | Elevated import accepted arbitrary legacy .reg content from user-writable storage | new restricted backup directory, allowlisted typed records; old .reg files remain visible but not executable by cleaner |
| S46 | Deleting entire uninstall/App Paths trees, startup values or SharedDLL refs can break valid apps or shortcuts | review-only containment; only confirmed missing HKCU MuiCache string values eligible for deletion |

S08 existing stale-findings mitigation strengthened: recheck actual value and missing path immediately before delete; not counted again. This is not a transactional guarantee against concurrent registry writers. Legacy backups remain on disk. Full safe cleanup for the other four categories, precise rollback testing, ACL verification and UI localization remain open.

## UMP03 — first core/resources batch (12 new findings)

| ID | Finding | Source mitigation |
|---|---|---|
| R01 | Save rewrote settings even when unchanged | serialized content dirty check |
| R02 | Concurrent saves shared a fixed .tmp path | persistence lock and unique staging |
| R03 | Failed settings read could replace original with defaults | retain original and refuse automatic saves until valid import |
| R04 | Export could copy stale settings after failed save | return failure and export current serialized content only on successful save |
| R05 | Import accepted unrelated JSON objects | require recognized Language property and typed object; more schema checks pending |
| R06 | Clipboard capture serialized/encrypted/wrote full history per event on UI thread | 1.5-second coalescing timer and worker write; explicit removal/exit flush |
| R07 | Clipboard entries and load size unbounded | 65,536-character entries, 50 entries, read cap |
| R08 | Failed clipboard listener registration still marked service running | unregister and detach on failure, track hotkey registration |
| R09 | RAM Boost flushed modified page list causing avoidable disk I/O | remove global flush |
| R10 | RAM Boost purged standby cache and caused potential re-reading/page faults | remove global standby purge |
| R11 | RAM cleanup indiscriminately trimmed processes regardless of available memory | pressure gate; report available RAM instead of free-only improvement |
| R12 | Concurrent Boost calls could overlap | serialized memory cleanup gate |

## UMP06 — early status fix

N01: failed hotkey registration incorrectly called SetIdle and removed another active operation. New informational Report method retains active operations. Full identity-based migration of legacy calls remains open.

Ten additional test cases: 5 registry allowlist, 1 settings no-op write, 4 RAM-pressure fixtures. Cumulative 50 newly authored cases; 40 covered by prior successful test stages, last 10 pending. New safety and resource commits not yet compiled on the final revision. Clipboard write errors are traced, not yet user-visible; coalesced history can lose recent entries on abrupt process termination. The RAM pressure threshold is provisional (max of 512 MiB or 10% physical memory) and separate SystemView direct trim still needs integration. Current working-set trim is not yet a performance improvement certification. Existing 6 skins and 14 languages need review, with English fallback for new strings. No merge or release.

## Prior batch CI confirmation

Run 45992282312: 31/31 icon assets prepared and verified against all pinned SHA-256 values; 442 desktop and 42 migration tests passed. Installer build canceled by subsequent commits; no new installed-app measurement or release certification. Previous ten fixture cases are included in these counts.

## UMP03 — second core/resources batch (17 additional distinct findings)

| ID | Finding | Source mitigation |
|---|---|---|
| R13 | Sensor singleton initialization and hardware updates accessed concurrently | serialize access through SensorGate |
| R14 | CPU/GPU/RAM/disk getters refreshed the same hardware repeatedly | reuse sensor snapshot for 3 seconds |
| R15 | Hardware monitor session lacked explicit shutdown | close shared Computer at exit and after failed open |
| R16 | Disk sensor fell back to first physical disk even if several exist | unknown when no match and multiple disks; model ambiguity still needs serial identity |
| R17 | Home attached activation handlers in constructor before parent was available | attach/detach on Loaded/Unloaded |
| R18 | Home fast and disk timers remained active while hidden/minimized | suspend both; visible active minimum 2s, visible inactive minimum 8s |
| R19 | Progress bars animated unchanged values and ignored low tier | skip unchanged values and respect animation flag |
| R20 | Home worker could publish counters after unload disposed prior state | publish on dispatcher, dispose late results |
| R21 | Home internet-status polling could overlap | single-flight guard and discard unloaded results |
| R22 | Desktop widget left GPU counters undisposed on close and late initialization | dispose both on close and on late completion |
| R23 | Desktop widget pinged at each 1.5s refresh | throttle network to 20s; voltage to 3s; skip hidden refresh |
| R24 | Alert timer and manual checks could overlap | atomic single-flight guard |
| R25 | Late background hardware alert could show after service shutdown | generation check |
| R26 | Manual update check could duplicate existing startup/periodic scan | shared in-flight task; duplicate caller awaits real result |
| R27 | Update log truncated its own lazily enumerated input | synchronized rotation to previous file |
| R28 | Chrome status spinner restarted on each update and attention pulse ignored hidden/low-tier state | motion lifecycle and only toggle storyboard when state changes |
| R29 | Failed GPU counter priming leaked initialized counter | dispose on exception |

Seven additional fixture cases (4 Home polling floor and 3 sensor refresh coalescing), 57 cumulative new cases authored, 50 included in completed prior test stages. Actual runtime visibility, all six skin identities, long soak, hardware driver compatibility, concurrent sensor closure, repeated unload/reload behavior, GPU usage accuracy, scheduler and scan cancellation still need integration tests. A single shared sensor refresh lock reduces concurrent work but cannot guarantee a stuck vendor driver returns; shutdown timeout remains open. Manual scan joining does not re-run the announcement option. No guarantee of CPU/RAM reduction until comparable VM samples. Prior operation-level safety restrictions remain interim; no merge or release. 

## UMP03 — third batch (7 additional findings)

| ID | Finding | Source mitigation |
|---|---|---|
| R30 | Home, PC Manager hero and shell health badge repeated the same WMI checks | shared 15-second language-aware health snapshot, fresh explicit scans, nonblocking invalidation |
| R31 | HealthScoreService PLINQ disregarded configured parallel-scan tier | degree bounded by existing performance profile |
| R32 | Pending reboot registry handles not disposed | using declarations |
| R33 | Security Center WMI collection not disposed | using collection |
| R34 | Routing to a subpage of the current composite tab reconstructed the entire composite | reuse active composite and select existing subpage |
| R35 | Composite cache retained detached live views that could have disposed their resources on unload | retain only current page, reconstruct on genuine page change |
| R36 | Tab fade animation retained a completed animation clock, including across profile changes | remove prior clock and use FillBehavior.Stop |

This is a targeted lifecycle change, not complete lazy construction: the selected composite page still builds original views before filtering sections. Losing detached view-local state is a possible regression; preserve user selection/input where needed in subsequent UI work. Health snapshots are not long-running historical records; WMI calls that hang and partial/unknown individual checks are not yet fully handled.

## UMP04 — first unified maintenance batch (12 additional findings)

| ID | Finding | Source mitigation |
|---|---|---|
| M01 | Health Check, Home Fix All and tray Quick Clean selected categories independently | shared SystemMaintenanceService plan |
| M02 | Browser cache implicitly included in Health Check storage consent despite optional category | separate unchecked browser card |
| M03 | SystemView Free RAM bypassed pressure-gated MemoryCleanerService | call same service as Boost |
| M04 | Full Health Check lacked RAM condition/selection within the unified workflow | available-memory measurement and optional pressure-gated RAM card |
| M05 | Concurrent QuickClean requests could mutate same paths simultaneously | common semaphore for QuickCleanService entry points |
| M06 | Duplicate category IDs could be applied repeatedly | copy and deduplicate plan |
| M07 | Artificial 650ms per-stage delay | remove minimum delay, retain actual stage completion |
| M08 | Failed overall health score calculation rendered 100/100 | unavailable label and report rather than invented score |
| M09 | Registry/tweak review route closed dialog twice | single close after navigation |
| M10 | Scan/apply lifecycle did not guard overlapping invocations or closing during mutation | scan/apply flags, block closing during apply, stop later stage presentation after close |
| M11 | Home Fix All left a busy status entry unmatched | existing per-operation scopes in Home and Health Check |
| M12 | Result headline ignored health-score issues | include actual score findings and memory pressure |

Shared plan automatic allowlist: UserTemp, ThumbnailCache only when recommended and nonzero. BrowserCache requires explicit opt-in; recycle bin, Windows servicing caches, registry, personal tweaks and startup are outside this plan. Existing dedicated tools remain separate. The existing scheduled maintenance and manual per-browser cleanup are not yet under the same operation gate; no claim of global coordination. Preview sizes still represent scanned logical bytes, not precisely reclaimable bytes; per-file manifest and per-stage cancellation/partial-failure semantics remain pending. Individual failed health checks still need explicit Unknown status, not merely aggregate exception handling. Seven new fixture cases (3 cache freshness, 4 plan selections); 64 cumulative authored cases. Latest previously verified stages: 442 desktop +42 migration. Run 45993105064 was pending and returned no jobs during this pass. New commits through 2be7990482b15ae346ce7f2777fb93d8112e4107 need compile/tests. New labels currently Greek/English fallback. No merge or release.

## Completed intermediate CI

Run 45993640140 (commit 2f5080388e8d41dc5d02cc9433045a7b0df5aeea): inventory, icon preparation (31/31), desktop tests 456/456, migration tests 42/42, installer build and clean-installed process samples all successful. The previous 14 fixture cases (UMP03 batch 2 and UMP03/04 batch 3/1) are included. Normal/tray respectively: CPU 5.073% / 0.078%; maximum WS 249.48 / 152.49 MiB; maximum private 139.05 / 68.80 MiB; write transfer 2226 / 0 bytes; handles 1014→1099 / 937→910. 13 samples across ~60s per mode. Earlier intermediate private memory was 158.01 / 87.01 MiB. CPU remains above normal-mode target, not a controlled comparison. No sustained leakage/SSD-wear certification.

## UMP04 — second batch (12 additional distinct findings)

| ID | Finding | Source mitigation |
|---|---|---|
| M13 | Individual failed health checks silently counted as healthy | explicit Unknown issues; suppress numeric score in Home/hero/health dialog/preview and show attention |
| M14 | Partial health scores could contaminate daily and sparkline history | record only complete snapshots |
| M15 | Empty storage card could be selected | disable selection at zero |
| M16 | Manual per-browser cleanup bypassed common cleanup gate | same gate, catch contention/error in UI |
| M17 | Scheduled headless cleanup bypassed GUI process-only gate | per-user named mutex shared in interactive session; seven-day top-level policy retained |
| M18 | Scan estimates included recent files that deletion policy skips | same iterative path and age-filtered traversal; no guarantee of exact reclaimable bytes |
| M19 | Tray/Fix All scanned optional categories never executed | recommended-only scan |
| M20 | Separate Advanced Full Maintenance recursively deleted unverified TEMP | existing common conservative cleaner |
| M21 | Detached repair command did not report outcome and SFC ran before component repair | awaited DISM then SFC; report real output and failure |
| M22 | Advanced Full Maintenance lacked explicit preview/confirmation and restore guard | confirmation, existing RestoreGuard, operation scope and button lifecycle |
| M23 | Partial firewall profile readings could report all enabled | three-state aggregation: confirmed off, confirmed all on, unknown |
| M24 | Security summary could state all good with unknown firewall/UAC data | incomplete status for unavailable readings; known warnings preserved |

Six new fixture cases (partial health, three firewall cases, age estimate, concurrent cleanup workers), plus one corrected existing firewall expectation; 70 cumulative newly authored cases. All changes through c4902cc3b076cfac1887cdcaa47dacc1047482e5 pending compile/test on current revision. Named mutex only covers same interactive Windows session/user and this app, not third-party tools or other sessions; fixture tests verify worker serialization, not cross-process scheduler integration. Per-file exact preview manifest, hard-link/reparse substitution race, precise allocated bytes and cancellation of long scans remain open. No automatic repair scheduled; DISM/SFC repair only after explicit user consent. Standalone repair buttons still need shared servicing coordination. New labels Greek/English; full translations, actual visual screenshots, and all prior interim restrictions remain release gates. No merge or release.

## UMP05 — unified GearWinCore, first integration batch (explicit user requirement)

The existing PerformanceProfileService is the sole adaptive governor. GearWinCore is an event-driven admission layer, not a second monitoring engine or process. One OperationCoordinator owns active/pending leases, max 32 queued, admits 1–3 concurrent compatible operations according to current tier. User requests take priority over conflicting queued background work; running work is not forcibly interrupted. Lease release is idempotent and cancellation removes queued work. Shutdown stops new admission and cancels pending requests. No additional polling timers or activity disk writes are introduced. Service-boundary leases, never nested: composite workflows invoke independently coordinated leaf services. Existing safety checks, restore guards, exact-value registry backup, path validation and user confirmation stay in place.

| ID | Finding | Source mitigation |
|---|---|---|
| C01 | Independent service gates allowed conflicting disk/memory/servicing/backup work to coexist | one conflict-aware admission coordinator across connected services |
| C02 | No shared priority for user operations versus startup/periodic scans | user-first queued admission; background scans tagged |
| C03 | Existing tier limited some internal scans but not aggregate admitted operations | 1–3 compatible operation slots driven by existing governor |
| C04 | No central bounded, cancellable waiting queue | 32 pending limit, cancellation and idempotent release |
| C05 | No central shutdown admission policy | stop admission and cancel waiting leases; do not forcibly abort running repairs/backups |

### Integration matrix (source only)
- Connected: QuickClean scans/cleanup (including manual browser and scheduled cleanup through shared engine), MemoryCleaner, standalone SFC/DISM/CHKDSK/WinSxS command capture, Registry Cleaner selected-value delete/restore, BackupService system image creation, HealthScore async snapshots from Home/PC Manager hero/full Health Check/preview/shell/scheduled maintenance, UpdateNotificationService startup/periodic/manual scan.
- Existing cross-process named mutex for cleanup remains. Other domains currently process-local only. Headless auto-update, individual Winget/driver/Windows Update install/scan callers, user tweak/profile mutations, migration/clone, game saves, vault, shredder, startup delay, network resets, diagnostics, clipboard and hardware monitoring require a separate boundary mapping before migration. Hardware safety alerts must not wait behind multi-hour backup. No blanket replacement of existing services; no second governor. Do not remove independent transaction/safety guards.
- Core policy limitations: a running background job is not preempted; manual caller joining an existing queued background update does not yet raise its priority; central cancellation only controls waiting, not already running tools; tier cap limits admitted workflows, not their internal threads. Cross-session/interprocess conflict policy remains open beyond cleanup. A bounded queue rejection must be surfaced in every UI caller before release. Validate close/reopen, queued status, headless processes, lock ordering, and sustained background fairness. Keep user preferences and confirmation semantics intact. No new UI feature.

## UMP03 — fourth batch
R37: cached battery/power-saver state could remain stale until manual reevaluation — refresh on existing governor tick every 30 ticks, no new timer. R38: governor restart retained old CPU samples/counters — clear on stop. R39: performance startup log grew without bound — rotate at 256 KiB. No new measured reduction yet.

## UMP06 — early second finding
N02: startup/periodic update notification paired legacy FIFO SetBusy/SetIdle — use per-operation scopes to avoid completing unrelated operations.

Seven new isolated coordinator fixtures (conflicts, user priority, dynamic capacity, cancellation, blocked user, shutdown, bounded queue), 77 cumulative newly authored cases. Latest fully verified intermediate remains 498 tests and installer from run 45993640140. Run 45995423289 for previous batch had no jobs available during this turn. Central core changes through 645fac275796219207bf5c2685155c06d0a048fd need fresh compilation and real integration testing. No merge or release; all interim safety restrictions and existing release gates retained.

## Intermediate core verification

Run 45996232379: 31 icon assets verified, 469/469 desktop and 42/42 migration tests passed, including the first seven isolated coordinator cases and six prior maintenance fixtures. Installer build succeeded; process measurement stage was canceled by newer commits. This is 511 successful tests, not a fully completed new end-to-end run. Last completed installer + measurement run remains 45993640140.

## UMP05 — unified GearWinCore, second integration batch (7 new findings)

| ID | Finding | Source mitigation |
|---|---|---|
| C06 | Direct provider scans bypassed the notification workflow's coordinator | coordinate Winget, Windows Update, driver, recommended package checks and UWP scan at service boundaries |
| C07 | Installation/uninstall and driver backup/restore operations bypassed shared heavy-operation policy | SoftwareChange and Backup domains, including Winget install/upgrade/uninstall/import/pin, driver installation/backup/restore/delete and Bloatware command helpers |
| C08 | Static global scan cancellation could affect unrelated external commands | explicit manual scan token, AsyncLocal operation context and compatibility property |
| C09 | Automatic update notification ran Windows Update inside Winget scan and then again separately | notification scans application providers without WUA and calls WUA once; manual combined scan retains all providers |
| C10 | Redirected output could wait after process timeout | stdout and stderr drain included in timeout; propagate real scan cancellation |
| C11 | Profile mutations bypassed shared heavy-work coordination | ProfileChange domain for manual Office/Gaming changes and publication of flags; release before optional RAM maintenance |
| C12 | Optional periodic profile changes had no nonqueued admission policy | TryAcquireBackground skips while busy or user waiting, no queued periodic backlog |

### Safety follow-up (5 new findings, UMP02)
S47: WUA ResultCode 3 (completed with errors) previously returned full success; report failure/partial result. S48: driver WUA script treated ResultCode 3 as full success; only 2 succeeds. S49: nighttime update task disable returned true even if scheduler deletion failed; return real result. S50: headless nighttime job ran without checking persisted opt-in; honor NightlyUpdatesEnabled. S51: unattended Windows installs continued after failure/reboot required; stop batch, no automatic reboot.

Eight additional fixture cases (five conflict domain pairs, nonqueued background, waiting-user preference, scan token isolation). 85 cumulative new cases authored, 77 included in the preceding successful test stages; current changes through 562903e9088cbc5907973fffebc9b3d639a48d09 not yet compiled. Direct leaf scans now use same update domain; notification orchestration must not acquire outer UpdateScan lease. The manual combined Winget scan calls WUA within its existing lease explicitly. No nested leases in these connected paths. Current core is process-local; existing cleanup mutex only, no global cross-process coordination for software changes, repair, backups or profile writes. Legacy direct detached driver install, catalog/vendor scans, other startup/tweak/network tools, deep uninstall command, rollback callbacks and explicit auto-profile stop/exit restoration still require integration. Driver export and install are individually coordinated, not a single atomic transaction. 29 recommendation checks are still separate calls; plan single inventory scan and distinct unknown status next. Recheck installed-state on success, provider partial results, cancellation of native COM work, shutdown and queue refusal at every UI boundary; no hard kill of servicing as a new policy. No runtime measurement or final operation-level certification of the new changes. No merge or release.

## Intermediate verification

Run 45997561301: 477 desktop and 42 migration tests passed (519 total), including eight new core fixtures from batch 2; 31/31 icon assets verified. Installer build canceled by subsequent commits. No new resource measurement.

## UMP05 — third integration batch (6 new findings)

| ID | Finding | Source mitigation |
|---|---|---|
| C13 | Initial recommendation inventory ran 29 separate winget processes and repeated registry enumeration | one bounded list command and one traversal of uninstall views |
| C14 | Lifetime recommendation cache could become stale after software changes outside recommendation UI | two-minute session cache and software mutation epoch from coordinator |
| C15 | Multiple views could duplicate initial recommendation scans | shared in-flight task, per-view operation guard |
| C16 | Network reset, DNS and firewall command helpers bypassed core | NetworkChange domain |
| C17 | Startup toggles, delayed startup and service start-mode mutation bypassed core | StartupChange domain; leaf operations avoid nested leases |
| C18 | File diagnostics and disk benchmark bypassed core resource policy | Diagnostics and exclusive Benchmark domains for SystemService entry points |

## UMP02 — seventh safety batch (8 new findings)

| ID | Finding | Source mitigation |
|---|---|---|
| S52 | Failed or truncated inventory interpreted as absence | three-state inventory and explicit targeted verify action |
| S53 | Recommendation UI toggled installed flag on exit code alone | re-probe targeted state, batch refresh, exclude unknowns from group install |
| S54 | DNS values interpolated into elevated script without IP validation | parse and normalize addresses |
| S55 | Restart Wi-Fi action restarted all adapters | restrict to physical wireless media; needs physical hardware verification |
| S56 | Network process redirected stdout without draining | remove unused redirect; stop reset chain on failure |
| S57 | Startup rename could overwrite another value or change ExpandString type | verify current command and absent destination; preserve type |
| S58 | Service optimization accepted arbitrary services and guessed restore value | allowlist, current mode check and existing backup requirement |
| S59 | Remove delay ignored task deletion failure | refuse restoration on failed deletion; full transactional rollback still pending |

Eleven newly authored test cases (epoch, four conflict pairs, two inventory tests, four probe cases); cumulative 96, 85 included in prior verified stages. Latest source not yet compiled. English header parsing fails closed on localized or truncated winget output, so unknown rows may require explicit one-package verification; registry positive evidence uses exact title and existing icon path, not authoritative package identity. A two-minute cache can lag external changes; refresh remains available. No claim of measured 96.6% CPU reduction: 29→1 is the initial CLI invocation count only. Direct hosts edits, tweak changes, detached Defender scan, alternate file diagnostics, startup-delay rollback/task identity collision, cross-process software admission and full UI state synchronization remain open. No merge or release.

## Intermediate verification

Run 45998332411: 488 desktop + 42 migration tests passed (530 total), including 11 additional inventory/coordinator fixtures. 31/31 icon assets validated. Installer step canceled by subsequent commits; no new process samples. Last complete installer/measurement run remains 45993640140.

## UMP05 — fourth GearWinCore integration batch (6 new findings)

| ID | Finding | Source mitigation |
|---|---|---|
| C19 | Game-save backup bypassed core Backup domain | existing verified ZIP service scheduled through GearWinCore |
| C20 | Vault create/restore and shredder competed independently with backup/cleanup | FileProtection heavy-operation domain |
| C21 | Transfer preflight and migration app export bypassed shared admission | TransferScan plus Backup lease for export commit |
| C22 | Main and pinned tweak switches/profile imports/undo applied system changes outside core | common verified per-row TweakChange path, explicit state detection |
| C23 | Deferred tweak state detection bypassed resource scheduling | Diagnostics lease for existing batched detection |
| C24 | Heavy work in headless scheduled GearWin process did not share GUI coordinator | per-user Global kernel mutex with thread-correct acquisition/release; local policy retained |

CoreProcessLease: one kernel mutex per SID across sessions; no idle thread, new timer, persistent file or polling. Async ownership requires one temporary owner thread while a heavy job waits/runs. Optional auto-gaming uses zero-wait synchronous acquisition/disposal on dispatcher thread. Cross-process wait bounded to 30 seconds; caller sees timeout rather than force-killing a backup/repair. Local lease acquired first, process lease second, existing cleanup session mutex last. No recursive acquisition permitted. New releases cover participating same-user heavy operations only; read-only scans, other user accounts, previous GearWin versions, offline Linux, external installers and unconnected paths are outside this policy. Default kernel object ACL and cross-session/elevation behavior need VM verification. The process mutex does not terminate external commands orphaned by abrupt GUI death. Active leases are not forcibly revoked on shutdown; graceful exit needs dedicated validation.

## UMP02 — eighth safety batch (10 new findings)

| ID | Finding | Source mitigation |
|---|---|---|
| S60 | Game-save backup Directory.Exists filter could silently skip inaccessible roots | distinguish missing roots from unreadable roots; abort other errors |
| S61 | Game-save backup destination/source ancestors were not checked for redirection | validate existing ancestors and target; explicitly support fixed/removable local destinations |
| S62 | Transfer preflight only checked final root path, not redirected ancestors | inspect ancestor chain and recheck traversal directories |
| S63 | Shared filesystem predicate accepted relative paths after normalization and root containment appended duplicate separator | require absolute paths and normalize volume-root prefix |
| S64 | Profile imports continued after failed restore-point creation | abort on RestoreGuardResult.Failed, retain explicit Disabled preference |
| S65 | Journal undo ignored subsequent change in tweak state | require current detected state to match recorded state, verify resulting state |
| S66 | Profile import selected changes based on potentially stale UI toggle | re-read current state within coordinated mutation |
| S67 | Migration JSON export could replace destination with partial content | same-directory unique staged atomic replacement |
| S68 | Main and pinned tweak UI reported success without verifying postcondition | readback; unknown outcome throws instead of journaling success |
| S69 | Vault compressed plaintext MemoryStream backing buffer retained after copy | zero used segment after successful compression; exception paths and other managed buffers still require audit |

## UMP03 — fifth resource batch (1 new finding)
R40: per-file large buffer allocation during game-save/vault compression produced unnecessary allocations/GC pressure — reuse one buffer per archive. No measured impact yet.

Six additional regression cases (3 conflict pairs, mutex contention/release, cancellation, separate Windows child process exit). 102 cumulative authored cases, 96 included in previous successful stages. Current changes through 1cf160a923364afb3a898b54df0207a08f28ca90 need fresh compile/tests. New tweak pathway intentionally refuses unknown state; existing simple tweaks with localized powercfg detection and asymmetric Off behavior need compatibility review. Legacy direct tweaks (visual presets, custom context menus, low resource bundle, reset on uninstall, gaming tweaks and hosts), direct restore-point calls, automatic profile stop, native driver detached operations, offline clone kit export and actual offline clone remain outside full coordination. Precise typed durable tweak snapshots, rollback on partial mutation, original vault lock semantics, file-shred TOCTOU and backup consistency remain open. No user-facing feature added, no merge or release.

## Intermediate verification

Run 45999348910: 494 desktop + 42 migration tests passed (536 total). Includes all six previous recovery/core cases, including real separate Windows process mutex contention and release after child exit. 31/31 icon assets validated. Installer canceled by later commits. The mutex test uses an isolated Local name, so production Global name/default ACL across Windows sessions/elevation is still not certified. No new resource samples.

## UMP06 — first status and navigation batch (7 new findings)

| ID | Finding | Source mitigation |
|---|---|---|
| N03 | Legacy FIFO SetIdle could remove a different active operation, including a scoped entry | AsyncLocal legacy identity stack; explicit scopes independent; health BusyScope wrapper uses actual scoped identity |
| N04 | Three-hour age pruning could remove a valid long-running scoped backup | expire legacy entries only, never active explicit scopes |
| N05 | Worker-thread status events could be delivered out of order and queue redundant dispatcher updates | MainWindow coalesces dispatch and rereads latest StatusService snapshot |
| N06 | Faulting status subscriber could prevent other subscribers and operation lifecycle from completing | isolate individual subscribers and trace errors |
| N07 | Core queued work and actual admission were not reflected directly in status bar | each awaited core lease owns a waiting/running scope through release or exception |
| N08 | Empty final scoped status could leave the shell blank instead of Ready | translated Ready fallback at rendering |
| N09 | Ctrl+3 and Ctrl+8 matched composite subpage IDs as literal radio tags | route all numeric shortcuts via shared SelectTab resolver, preserve existing destinations |

## UMP05 — fifth core batch (1 new finding)

C25: ordinary tray/menu Exit called Shutdown even during an active heavy operation. Refuse normal exit while active heavy lease exists; atomically close coordinator admission when safe and cancel pending work. Existing close-to-tray semantics unchanged. Windows session ending, forced process termination, external process orphaning, detached/native unconnected work and asynchronous read-only work are not protected by this gate. A queued request not yet admitted may be canceled on deliberate exit. No claim of global OS shutdown protection.

Twelve additional authored fixtures (five lifecycle cases, seven numeric shortcut mappings); 114 cumulative new tests, 102 included in prior verified stages. Current changes through f402c6007935ef8ef25c23230a653c987b327c7b need compile and real UI checks. Legacy async-flow pairing is a compatibility bridge, not a proof all old calls are correctly paired; inherited execution contexts, nested operations and unmatched legacy notifications require migration to explicit scopes. Language fallback for core work labels is Greek/English, existing scope messages still may be in previous language until refreshed. Nonqueued optional background work does not create status scopes; duplicate legacy + core labels and long text need per-skin visual review. Numeric shortcut fixtures prove destination mapping, not actual key event delivery under all skins/focused controls. No merge or release.

## Intermediate CI verification

Run 46000330016: 506 desktop and 42 migration tests passed (548 total), including 12 prior lifecycle/shortcut fixtures. Icon preparation 31/31; installer build succeeded. Normal-mode measurement summary was emitted (CPU 4.142%, writes 635 bytes, handles 1170→1150), but process measurement step canceled before the full normal/tray cycle completed. Do not compare this as a certified complete baseline. Last complete measurement run remains 45993640140.

## UMP03 — sixth resource batch (9 findings)

| ID | Finding | Source mitigation |
|---|---|---|
| R41 | Home and widget opened separate CPU counters despite existing governor | shared native sampler, 750ms freshness |
| R42 | Home and widget independently allocated GPU engine counters | common SharedTelemetryService with in-memory samples |
| R43 | Widget opened GPU counters outside games; hidden Home retained GPU demand | visible consumers subscribe only when needed; last subscription releases counters |
| R44 | GPU engine sampling ran on dispatcher | shared single-flight worker sampling |
| R45 | Independent connectivity pings from simultaneous consumers | shared 10-second task/snapshot |
| R46 | GPU engine instance list could remain stale during process changes | refresh list after 30 seconds when demand exists |
| R47 | Network ETW accumulated PID entries indefinitely | bounded session dictionary (8192 IDs); new IDs beyond cap ignored until session restart |
| R48 | Network monitoring could remain active when window was hidden but not unloaded | stop on hidden, user may explicitly restart on return |
| R49 | Process objects from network/foreground lookups not explicitly disposed | using declarations |

## UMP02 — ninth safety/correctness batch (7 findings)

| ID | Finding | Source mitigation |
|---|---|---|
| S70 | Kernel network trace could replace another tool's session | precheck existing logger and NoRestartOnCreate |
| S71 | DXGI overlay explicitly stopped existing named session regardless of owner | no takeover, fail closed |
| S72 | ETW startup failure could leave partial session | assign session before provider setup and dispose on failure |
| S73 | ETW consumer exit could leave false running flag | session-identity guarded cleanup; network UI reports unavailable |
| S74 | Traffic rate assumed fixed one-second dispatcher tick | monotonic actual elapsed duration |
| S75 | No Present events displayed zero FPS | unavailable instead of invented zero |
| S76 | Failed overlay startup never retried for same PID | bounded retry after 30 seconds |

TraceEvent option reference: https://github.com/microsoft/perfview/blob/main/src/TraceEvent/TraceEventSession.cs . Verify against pinned package/runtime in CI. Separate ETW kernel and DXGI sessions are retained because they are distinct providers and purposes; no claim of unified native ETW session. Shared CPU/GPU service has no new timer. GPU renewal can briefly display unavailable, and 30-second enumeration cost/accuracy (multi-adapter, process churn, 3D only) must be measured. Shared GPU background work still needs fault/lifecycle and physical GPU testing; sensor alerts, foreground checks and other timers remain to integrate. No guarantee of FPS accuracy; DXGI event-name heuristic is not PresentMon. Network cap deliberately bounds memory but may omit new PIDs after prolonged high churn. Existing named ETW sessions after crash require explicit owner/operator recovery, not auto takeover. Shared ping cache may lag changes by 10 seconds and does not prove general internet reachability. Fifteen deterministic new fixtures (freshness, CPU delta and elapsed rate); cumulative 129 authored, 114 included in previous successful stages. Current commits through bc52e8f7223716160061e29b322f9e131134dbc9 need new compile/tests and real runtime validation. No merge or release.

## Intermediate telemetry verification

Run 46001340793, Windows job 140513493775: 521 desktop and 42 migration tests passed (563 total), including 15 telemetry fixtures; 31/31 icon assets and installer build successful. Normal-mode CPU summary 5.056%, full normal/tray measurement cycle canceled by newer commits. No new complete performance baseline.

## UMP02 — tenth safety/correctness batch: temperature acquisition (9 findings)

| ID | Finding | Source mitigation |
|---|---|---|
| S77 | Disk temperature depended on model name/first storage device, ambiguous for identical or hidden disks | read selected volume's single physical extent; match LHM v0.9.6 StorageDevice identifier by actual DriveNumber |
| S78 | Unsupported sensor API left disk without alternative | read-only Windows device temperature query, LHM SMART, exact MSFT_Disk association fallback |
| S79 | Late disk query could overwrite new selection | generation guard and requery current selection |
| S80 | One hardware Update exception aborted all temperature reads | recursive per-hardware update with failure isolation |
| S81 | DIMM thresholds and warning/critical temperature sensors could be displayed as live temperature | filter limit/threshold/resolution/warning/critical names; prefer package/core/composite names |
| S82 | Invalid/nonfinite/out-of-range temperatures could reach UI | validate >1 and <=150 C, reject sentinel and malformed native descriptors |
| S83 | Missing reading labeled locked based solely on HVCI registry configuration | neutral unavailable indicator, official PawnIO version metadata and diagnostic guidance |
| S84 | Vendor native libraries could resolve outside trusted system directory | System32-only DLL search (older NVIDIA layouts outside System32 may remain unsupported) |
| S85 | NVML fallback queried only device 0 | bounded enumeration of NVML devices and maximum valid reading |

## UMP03 — seventh resource batch (4 findings)

R50: Home read temperatures only at construction, no retry while visible; batch CPU/GPU/RAM reads on existing 20-second slow timer and initial visibility, no extra timer. R51: sensor open failure permanently disabled monitoring; retry after 60 seconds when requested, not continuous polling. R52: GPU vendor fallback duplicated work between alerts and Home; shared 3-second cache under sensor gate. R53: disk queries duplicated and could delay publication of CPU/GPU/RAM results; separate single-flight read and generation-aware reselection. Shared LHM lock can still delay both if vendor hardware update itself hangs; no claim of hard native timeout.

19 new test cases (7 valid-range, 6 sensor-name, 4 descriptor value, 1 malformed descriptor, 1 volume extent). 148 cumulative newly authored cases; 129 included in previously passed tests. New sensor changes through 5bddbc4c28f27e5c203b60fac5ecafcea0c0ce0e require compile and physical-device validation.

References: Microsoft Windows IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS, IOCTL_STORAGE_QUERY_PROPERTY, STORAGE_TEMPERATURE_DATA_DESCRIPTOR/INFO, MSFT_DiskToStorageReliabilityCounter; upstream LibreHardwareMonitor v0.9.6 StorageDevice.cs (identifier constructed from DriveNumber, SMART composite/threshold sensor names) and PawnIo.cs (installation registry metadata). Official driver https://pawnio.eu/ . No driver installation, unsigned modules, protection disablement, privileged write commands or new bundled dependencies. Driver version metadata does not prove service is functional. Official signed PawnIO can be installed separately with explicit consent and GearWin restart, but not guaranteed compatible with every machine/security policy. LHM package remains v0.9.6. Source license review: LHM upstream MPL-2.0 plus third-party components; do not describe entire dependency as MIT. No actual temperature measurements from physical hardware available in GitHub VM; test matrix must include Intel/AMD CPU, NVIDIA/AMD/Intel GPU, DIMM with/without thermal sensor, NVMe/SATA/USB, two identical disks, spanned volume, HVCI on and missing/installed official PawnIO. GPU temperature remains a representative single tile, not guaranteed every GPU measured. ADL legacy enumeration, vendor-specific CPU SDKs, newer unsupported CPU families, native I/O hangs, and disk hotplug remapping remain open. No merge or release.

## UMP03 — eighth resource batch: anti-pile-up RAM and CPU

| ID | Finding | Source mitigation |
|---|---|---|
| R54 | Concurrent automatic health requests from recreated views each entered the core queue | one shared pending/active non-forced health snapshot |
| R55 | Process history bounded samples per name but not number of names | 512 identities, 20 samples each, LRU eviction and synchronized snapshots |
| R56 | Repeated GPU consumer disposal could enqueue workers behind a slow driver | one coalesced disposal request with demand recheck |
| R57 | Repeated Undo clicks could enqueue duplicate mutations and successful undo retained closure | atomic per-entry in-flight guard, release successful closure |
| R58 | Repeated status publication created duplicate history and queued UI callbacks | consecutive dedup and one pending dispatcher refresh |
| R59 | Old automatic update sequence could continue after service stop or disable | generation invalidation and checks between providers |
| R60 | Per-view sensor guard did not protect across Home recreation | process-wide shared CPU/GPU/RAM read and one disk read; different disk retries on existing timer |
| R61 | Tweak undo closure directly retained detached row | weak row reference, retain model only |

Three new regression cases: 2048+ process identities, 100 duplicate undo requests, 10,000 optional background attempts during backup. Cumulative 151 authored cases; 129 included in previously confirmed CI (563 tests). The sensor CI run 46002677976 was canceled during tests by later commits. Do not claim sensor or anti-pile-up tests passed yet.

Independent endurance workflow .github/workflows/gearwin-endurance.yml, introduced at 968f15069805b879e07dab00845091ed137a9313, measures 61 samples each (15 minutes after one minute warmup) in normal and tray mode, CPU machine-normalized, private/working-set change, handles, threads, process I/O, raw process tree. Separate concurrency group does not cancel when subsequent ordinary commits arrive; push triggers only when workflow file changes, otherwise manual dispatch. This is a short endurance screening, not 24-hour certification, active UI churn, real hardware or physical SSD endurance. No forced memory trimming, no new always-on monitoring, no interruption of user backup/repair; existing queue limit 32 and optional nonqueued admission remain. Driver calls may still hang and retain waiting view continuations until completion; bounded work is not proof of zero leaks. Explicit user refresh is not coalesced into stale data. New threshold 512 process names and 15-minute windows are design choices. No merge or release.

## UMP03 — ninth resource batch: navigation lifecycle and anti-pile-up

| ID | Finding | Source mitigation |
|---|---|---|
| R62 | Device-summary cache invalidation could race ongoing publication, and translated snapshot stayed indefinitely | generation-aware publication, 120s cache, language check, existing single-flight coordinated via Diagnostics |
| R63 | Home/security-center concurrent security reads each created a task | one shared pending/active security read |
| R64 | Process enumeration started independently on each SystemView opening/refresh | one coordinated pending/active process scan |
| R65 | Process enumeration exception could skip final handle disposal; ranked rows reread working set | dispose entire enumeration in finally, use measured ranking value, tolerate process exit |
| R66 | Detached SystemView continued awaiting device and process scans | view lifetime token, cancellation of consumer waits, initial work deferred to Loaded |
| R67 | Concurrent process refresh could append duplicate rows after both cleared before awaiting | per-view guard, clear after snapshot arrives; ignore repeated device refresh while busy |
| R68 | GPU name WMI query repeated at each Home construction | shared in-flight metadata task with 120s freshness |
| R69 | GPU name and storage WMI collection/selected-object handles not disposed deterministically | using declarations in affected providers |
| R70 | Rapid disk-selection changes or Home reconstruction could queue multiple model/type reads | one process-wide metadata slot, cache limited to 26 letter keys, different selection retries on existing slow timer |
| R71 | Home performed expensive initial reads before visibility/attachment, including tray launch | first-visible initialization, reattach lifecycle on Loaded |
| R72 | Detached Home awaiting shared health/sensor/GPU/network/metadata tasks retained view continuations | cancel obsolete consumer waits using per-load token; shared underlying task is not terminated |
| R73 | Hidden/minimized Home retained live action/update/impact event subscriptions | detach while hidden, resubscribe and refresh once when visible |
| R74 | Tweaks constructor launched four independent state scans | one batched detection at Loaded |
| R75 | Detached Tweaks/pinned rows could leave queued detection work | cancel queued admission and stop between model detections; worker captures models rather than view rows |
| R76 | Rapid subpage switching scheduled repeated deferred accessibility tree traversals | one pending dispatcher operation, abort on unload, traverse current page only |
| R77 | Repeated Home health views triggered independent SMART WMI scans | shared system SMART task with 60s freshness |
| R78 | Repeated performance-policy events queued redundant Home polling callbacks | coalesce one dispatcher refresh |

Ten new deterministic fixture cases in DriveMetadataSlotTests: eight key normalization cases, one 1000-iteration same/different drive contention with canceled consumer, one 26-key cache/1000 invalid identities/expiry case. 161 cumulative newly authored cases; 129 included in previously confirmed CI (563 tests). The preceding sensor/anti-pile-up run 46003291557 canceled during build/test, so no additional verified count yet. New source through b5b8c8cc05499091e268e99291f4774dc2a063a8 is not yet compiled. CI audit concurrency changed to cancel-in-progress false to preserve active Windows validation when new commits arrive; no verification of new endurance run yet (available connector returns PR-triggered workflow runs only). No installer release.

Remaining: SystemView still constructs fonts and may initialize both process/device groups even if a composite removes sections; further section-demand mapping needed. Metadata TTL can temporarily retain a stale name after drive hotplug; physical temperature identity uses separate path. A refresh requested while an existing shared device summary is in flight can join that existing read; invalidated results do not repopulate cache. A native WMI/driver call may still hang; cancellation detaches the UI consumer, does not kill the native call. Timers, external process supervision, UI churn, all skins and long endurance remain unverified. New 120/60-second metadata TTLs and drive-letter bound are design choices; no forced memory trims. No new user-visible feature, no merge or release.

## UMP05 — sixth core batch: activation by explicit feature demand

| ID | Finding | Source mitigation |
|---|---|---|
| C26 | Optional services initialized from scattered application/UI entry points | CoreFeatureLifecycle + pure FeatureDemand from persisted options; common startup and preference reconciliation |
| C27 | Disabled update notifications still started periodic timer and startup sequence | guard activation; no disabled startup log or scan; stop shared timer when both consumers disabled |
| C28 | Low disk alerts implicitly depended on enabled software update scans | independent low-space check on existing shared timer, 5-minute monotonic check throttle, 24-hour existing alert cooldown |
| C29 | Disabled release checks still woke a timer; late result could announce after disable | start only on preference, stop/invalidate generation and suppress old results |
| C30 | Both hardware alerts disabled still left timer running | activate timer only if temperature or SMART selected; suppress late disabled alert |
| C31 | Clipboard monitoring and Win+Shift+V were always active, including unused installations | explicit ClipboardHistoryEnabled flag, existing history popup checkbox, no listener/hotkey until opt-in, stored history remains accessible |
| C32 | Stale scheduled maintenance task could run after saved opt-out | check saved opt-in before headless work and before cleanup decision |
| C33 | Widget and automatic gaming toggles bypassed central activation policy | settings and tray use common reconcile, auto gaming rechecks opt-in at service boundary |
| C34 | Import of preferences did not reconcile optional runtime services | reconcile on successful import; other imported UI settings may still require restart |

## UMP03 — tenth resource batch (3 findings)

R79: MainWindow constructor unconditionally requested health attention even for a hidden tray launch; defer until visible, loaded and not minimized.
R80: Clipboard history remained in RAM when background capture stopped; release in-memory entries on successful flush and after closing on-demand inactive history popup, without deleting persisted history.
R81: Optional service startup occurred on critical shell startup path; defer central activation to dispatcher ApplicationIdle; stop only requested services; bound auto-gaming consecutive tick counters.

Eleven newly authored FeatureDemandTests cases (all disabled, four low-disk/update combinations, four alert combinations, legacy clipboard opt-in, widget isolation). 172 cumulative newly authored cases; 129 included in previously confirmed CI (563 tests). The run for 934a027fb76389f0679c3ff91b66c582d500d4e4 was pending when inspected; subsequent runs were pending/canceled; no new verified count. Current source through e11410798ad8dd0f16863d582132cf80af863f30 not yet compiled. No confirmed endurance run. Existing notification defaults and previously saved preferences preserved: "enabled" is persisted configuration, not evidence that user manually toggled it. Clipboard defaults off for legacy/new settings because no prior explicit flag existed; Win+Shift+V is inactive until enabled in the existing history popup, which still opens through tray/main menu. Greek/English checkbox labels pending full localization and skin UI checks. Existing base governor/tray shell and diagnostics for the currently visible view remain intentional infrastructure. Running native scan may finish after disable, generation checks stop later stages and publication; no forced kill of Windows servicing. In-flight release/alert shutdown, direct widget close behavior, failed start retry, optional service UI integration and actual CPU/RAM measurements require verification. Settings cache remains process-local; a running headless task cannot observe a separate process's subsequent change without rereading persisted settings. No new standalone feature, no merge or release.

## UMP05 — seventh core batch: compatible concurrent user tools

User requirement: demand-driven activation must NOT become a blanket prohibition on simultaneous user tools. The coordinator governs participating GearWin tasks only, not arbitrary programs running in Windows.

| ID | Finding | Source mitigation |
|---|---|---|
| C35 | Process and device inventory used generic Diagnostics (which can traverse files), so unrelated mutations blocked inspection | explicit ProcessSnapshot/SystemSnapshot domains; distinguish observational work from file scans |
| C36 | Low adaptive tier globally serialized even compatible user-requested scans | user regular admission floor two, maximum three; separate snapshot lane up to two, automatic snapshot limit one; still respect conflict policy |
| C37 | Explicitly opened device inventory was classified as background and queued automatic work could consume capacity needed by waiting user work | user priority for device inventory; reserve same-lane capacity when user requests are queued |
| C38 | Later conflicting user scans could repeatedly pass an earlier blocked mutation | preserve arrival order among conflicting user requests; independent snapshots can pass |
| C39 | Benchmark admitted hardware-scan domain despite need for quiet storage activity | benchmark blocks hardware/system inventory while permitting process inspection; no claim that uncoordinated external I/O is eliminated |

Ten new regression cases: six compatible pairs at low tier, bounded observational lane, independent inspection while mutation is queued, benchmark isolation, and anti-starvation ordering. Reworked one existing governor test for the new contract. 182 cumulative newly authored cases; 129 included in previously confirmed CI (563 tests). The workflow 46005543469 for prior feature-lifecycle head had queued jobs when inspected; no new compiled/test result or endurance result. Latest source through 9f31dee8349b251249ad6d6dd6b734bacd2a0f38 requires CI. The design budgets (2–3 regular user leases plus up to 2 snapshots) are initial bounded choices, not measured optimum. Background work still yields to user requests and running work is not revoked. Conservative cross-process heavy mutex is retained: mutations with unknown overlapping file/registry/servicing/power/network scope remain serialized, including potentially independent writes, until explicit per-resource identity is verified. This is not yet a complete resource-specific reader/writer lock manager. Not all tools use the coordinator, hardware provider delays remain possible, and GUI modal behavior has not been validated. No change to Windows priorities of unrelated user processes, no new always-on scheduler, no forced RAM trimming. No merge or release.

## P0.01 — completed intermediate CI evidence (not final candidate)

Run 46005543469, Windows job 140531018633: 564 desktop + 42 migration tests = 606 passed, installer build and clean-installed normal/tray sampling succeeded. 172 newly authored cases included at this intermediate stage; 10 later concurrency cases are not verified by this run. Root process, 13 samples each, normal 60.52s: CPU 5.977%, max WS 257.88 MiB, max private 146.62 MiB, write transfer 2348 bytes, handles 946→1092. Tray 60.41s: CPU 0.071%, max WS 130.12 MiB, max private 46.20 MiB, write transfer 0 bytes, handles 529→669. Tray CPU/private memory met provisional targets for this short window, WS did not; normal CPU far above target. Handle rise demands extended workload/warmup investigation, not proof of leak. No physical SSD-wear or 24-hour claim. Latest head requires separate validation. Roadmap revision commit 58e394b98b9fc0e88935e26b6f6ef09d6cbbdaf5.

## P0.02/P0.03 — UMP02 safety batch 11 (10 distinct findings)

| ID | Finding | Source mitigation |
|---|---|---|
| S86 | Registry uninstall string passed verbatim to elevated cmd.exe | no shell execution of registry-provided commands; fixed system msiexec path, reconstructed argument list |
| S87 | Arbitrary registry identity/name could be trusted as installed machine MSI | require matching GUID registration, WindowsInstaller flag, machine context, Windows Installer installed name and state; otherwise Windows Settings |
| S88 | Deep uninstall ignored failed configured restore-point protection | stop direct MSI uninstall on guard failure |
| S89 | Success/restart code removed row without verifying product absence | separate restart/failure/settings/unverified outcomes; only unknown product after exit zero confirms removal |
| S90 | Residual folders matched by substring and included shared ProgramData | exact name candidates in local/roaming AppData with ordinary-path checks; advisory only |
| S91 | Elevated recursive residual deletion accepted arbitrary path and could erase shared/user data | remove destructive API and automatic deletion flow; manual review until ownership/rollback proof |
| S92 | UWP removal interpolated unvalidated package identity, without eligibility recheck | bounded identity grammar, exact current-user Store package, exclude framework/resource/nonremovable, verify absence |
| S93 | UWP read-only inventory could hang indefinitely | trusted system PowerShell, concurrent stream drains, 30s cancellation/termination for read-only scan only |
| S94 | UWP scan swallowed parsing/provider failure as empty list; leaked JSON document | propagate scan errors, preserve previous UI snapshot on failure, dispose document |
| S95 | Deep uninstall/UWP refresh and action could overlap, unhandled exceptions/stuck status | per-view guard, scope/finally, explicit outcomes and restore guard for UWP |

27 added tests (19 MSI registration/command/residual name, 8 exact UWP identity). 209 cumulative authored cases, 172 included in confirmed intermediate CI; latest source changes through 51d90aa7f612550f91b6e42c562b42739e3156d7 not yet compiled. Source commits fe627be, 8660cb2, 5489a78, 7b0e9ba, ed8e454, 846b10c, 51d90aa. Native MSI APIs and actual uninstall transaction require disposable integration test. Non-MSI/per-user or unusual command registrations temporarily delegate to Windows Settings; do not claim feature parity. Restore safe direct workflows later only with verified authority and package identity. Advisory folder matches are not ownership proof, and no data folders are deleted. Built-in Appx wildcard operations, package-manager executable trust, mutation supervision, installer success/readback and path race hardening remain P0.02–P0.05 work. No forced timeout kill of MSI/servicing. No merge or release.

## P0.03–P0.05 — UMP02 safety batch 12 (12 findings)

| ID | Finding | Source mitigation |
|---|---|---|
| S96 | Cancellation of active SFC/DISM/CHKDSK/WinSxS killed entire process tree | cancel admission only, keep repair lease until transaction process exits |
| S97 | UI reported cancellation even if operation actually completed | distinguish pending cancellation exception and actual outcome, explain safe semantics |
| S98 | Repair and schtasks executable resolution used bare names | use Windows SystemDirectory |
| S99 | Secure-directory ACL check considered only four identities; owner/null DACL not checked | trusted owner and all effective allowed write identities, reject null DACL |
| S100 | Existing secure directory could be redirected or blindly re-ACL'd | verify existing local path and protected directory, atomically create with protected ACL, preserve questionable old data |
| S101 | Temporary purge used age alone and could recursively remove live job files | remove age-only purge, clean owned empty staging directories in task/script paths |
| S102 | Temporary prefix/extension not bounded to safe simple names | strict syntax before directory creation |
| S103 | Scheduled elevated application registration checked directory only, not executable ACL | check actual exe and directory owner/access rules and ordinary local path |
| S104 | Scheduler XML staging overwrote file by default and recursive cleanup could remove unexpected children | CreateNew, durable flush, delete owned file and empty directory |
| S105 | File.Exists hid access failures when loading settings | distinguish missing/read errors, verify existing ancestors and settings file, block normal save after load failure |
| S106 | Settings stage wrote by path without exclusive creation/durable flush | unique CreateNew and Flush(true), same-directory replacement |
| S107 | Import loaded arbitrarily large document and accepted unsupported language | 16 MiB file limit, supported-language check |

## UMP03 resource batch 11
R82: unbounded repair output retention and single stdout capture; drain stdout/stderr concurrently with 32768-character bound per stream. R83: repeated SecureStorage.EnsureRoot ACL writes; check existing ACL rather than rewriting each time.

16 authored regression cases (7 writer SIDs, 4 invalid temporary extensions, 3 bounded stream cases, 1 complete atomic staging, 1 oversized settings). 225 cumulative authored cases. Last confirmed count remains 606 (172 new cases at that stage). Run 46066762587 (job 140733883198) has successful build/tests and installer stages; resource measurements still in progress when checked, detailed test count not yet retrieved. New changes through 8c4afee07b2d26ac999067c955f36d3c5a8e6f8b require separate CI.

Important limitations: this is defense-in-depth, not full proof of ACL/ancestor/hardlink/TOCTOU security. Existing GearWin directories with nontrusted owner/ACL will fail closed and need explicit safe migration (no blind ownership changes); child file ACLs and DLL/dependency trust need separate audit. SecureStorage existing read permission for Users retained, not a secret store. Removing age-only purge can leave orphaned temporary files after crashes; ownership-aware bounded reclamation remains open, not permission to accumulate indefinitely. Repair cancel now only cancels pending work, not active servicing; native process/pipe hangs, localized CHKDSK scheduling, task scheduler timeout outcome verification, restore transaction atomicity and cross-process settings reconciliation remain open. No claims of completed P0 or release.
