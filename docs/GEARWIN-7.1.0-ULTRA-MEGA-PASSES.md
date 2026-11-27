# GearWin 7.1.0 — Ultra Mega Passes: cumulative progress and findings

Baseline: `0d36ba06ffd5b1f90676d546d8abdfafe1062fbf`; review branch `release/7.1.0-ultra`; draft PR #6. All counts below refer to distinct, explicitly listed issues, not lexical matches or test counts. "Source mitigated" means committed, NOT runtime verified.

## Pass summary (2026-11-27)

| Pass | Distinct recorded findings | Source mitigations | Runtime-certified fixes | Status |
|---|---:|---:|---:|---|
| UMP01 source audit | 3 test/measurement coverage gaps | 1 infrastructure mitigation (measurement workflow added) | 0 | open |
| UMP02 safety, batches 1–2 | 24 safety/correctness findings | 24 addressed at least in part in source | 0 | open |
| Total | 27 | 25 at least partly addressed | 0 | open |

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
- Run 45986429644 completed source inventory and Windows baseline successfully. Newer batch 2 commits require fresh CI. No operation-level safety certification yet.
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
