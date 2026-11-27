# GearWin 7.1.0 — Ultra Mega Passes: cumulative progress and findings

Baseline: `0d36ba06ffd5b1f90676d546d8abdfafe1062fbf`; review branch `release/7.1.0-ultra`; draft PR #6. All counts below refer to distinct, explicitly listed issues, not lexical matches or test counts. "Source mitigated" means committed, NOT runtime verified.

## Pass summary (2026-11-27)

| Pass | Distinct recorded findings | Source mitigations | Runtime-certified fixes | Status |
|---|---:|---:|---:|---|
| UMP01 source audit | 3 test/measurement coverage gaps | 1 infrastructure mitigation (measurement workflow added) | 0 | open |
| UMP02 safety, batches 1–5 | 46 safety/correctness findings | 46 addressed at least in part in source | 0 operation-level certifications | open |
| UMP03 core/resources, batches 1–2 | 29 findings | 29 addressed at least in part in source | 0 | open |
| UMP06 early status fix | 1 finding | 1 addressed in source | 0 | open |
| UMP07 early artwork fix | 8 distinct icon/UI findings | 8 addressed at least in part in source | 0 visual certifications | open |
| Total | 87 | 85 at least partly addressed | 0 operation-level certifications | open |

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
