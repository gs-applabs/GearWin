# ROADMAP

## Ενεργό roadmap 7.1.0 — αναθεώρηση 2026-11-27

**Αποφάσεις έκδοσης:** σταθεροποίηση και ολοκλήρωση των υπαρχουσών δυνατοτήτων, όχι νέα ανεξάρτητα εργαλεία. Ασφάλεια Windows/δεδομένων πάνω από την ταχύτητα. Demand-driven ενεργοποίηση, ασφαλής παράλληλη χρήση, χαμηλό αποτύπωμα και καμία τεχνητή εκκαθάριση RAM για καλύτερους αριθμούς. Δεν απενεργοποιούνται Defender, Secure Boot, HVCI ή μηχανισμοί ασφάλειας για να περάσουν δοκιμές ή να εμφανιστούν αισθητήρες. Δεν αλλάζουν προτεραιότητες ανεξάρτητων εφαρμογών του χρήστη.

**Βάση προγραμματισμού:** branch `release/7.1.0-ultra`, draft PR #6, head πριν από την αναθεώρηση `8962ad3808d489a4748631364b6485d59353aa10`, base `80ca071092c2613a489b5160cd709d36b9d444b0`. Το αρχικό RC 7.0.0 είναι `0d36ba06ffd5b1f90676d546d8abdfafe1062fbf`. Ο [σωρευτικός κατάλογος ευρημάτων](docs/GEARWIN-7.1.0-ULTRA-MEGA-PASSES.md) διατηρεί τα σταθερά IDs A/S/R/M/C/N/I και τις αποδείξεις των προηγούμενων περασμάτων. Δεν επαναριθμούνται και δεν καταμετρώνται διπλά.

### Αναδρομικός έλεγχος απαιτήσεων 2026-11-27 — προτεραιότητα P0.R (πριν από νέα λειτουργικά batches)

Audit snapshot: candidate `ebc2ec2e8788eddcb0f94af2a08e60217a1cb8c1` vs master `c025e12d3e4cc8d3dc2be63d27f30b0aa8f34132`. GitHub compare reports **diverged**: 22 commits reachable from master only, 332 candidate only. Tree comparison: 49 master-only paths (including screenshots and generated Python bytecode), 41 candidate-only paths, 98 common paths with different blobs. Counts describe Git state, not missing features. Do NOT reset, overwrite candidate files wholesale, or blindly merge. Reconcile intent and tests per file, preserve candidate safety fixes; avoid tracking __pycache__ or other generated files. Verify a combined SHA before release. See [comparison](https://github.com/gs-applabs/GearWin/compare/ebc2ec2e8788eddcb0f94af2a08e60217a1cb8c1...c025e12d3e4cc8d3dc2be63d27f30b0aa8f34132).

| ID | Recovered requirement / verified gap | Priority / status |
|---|---|---|
| RQ01 | .NET 11 migration (master 744c2e6); candidate still net10.0-windows, SDK 10 and no global.json. Align desktop, migration, tests, CI and installer; verify official SDK and build on integrated head. | P0.R BLOCKING |
| RQ02 | Opsis.UI 6 resources and smoke CI (master 980438a) absent on candidate; restore opt-in keys and document, then actual screen adoption with no conflicts or cross-app breakage. | P0.R sync / P3 adoption |
| RQ03 | Partclone adapter, partition-aware copy/verify, fixture and disposable loop-device tests, GUI export/instructions; absent candidate adapter. Preserve latest exclusive raw-device safety; never claim full boot recovery. | P0.R / P2.05 |
| RQ04 | Restore recovery-vm and linux-distros workflows, adapt branch triggers/SDK; Debian 12/13, Fedora, Arch, openSUSE, Alpine containers and Windows 2022/2025 coverage. Containers test userland, not full distro boot. | P0.R / P4 |
| RQ05 | Catalog driver Microsoft signer check and DriverSignatureTests absent candidate (master 60d4843); reconcile with candidate download/privilege safeguards, verify actual trust chain and identity. | P0.R BLOCKING |
| RQ06 | TransferPreflight drive-root overlap fix (master 5ff529b) absent candidate; verify ancestor/descendant, junction, device identity cases. | P0.R SOURCE COVERED / regression pending |
| RQ07 | Backup before/after catalog new-version check and BackupCatalogTests present master, absent candidate. Combine with candidate volume GUID safeguards, localized/empty/error catalog tests; real restore separately. | P0.R BLOCKING |
| RQ08 | Startup-impact brush Freeze() fix (master 10315a7), TabLayout/recovery export and Help/history language diffs need reconciliation. | P0.R / P3 |
| RQ09 | Inno Setup dynamic dark mode (master c8dea01; candidate WizardStyle=modern), updater upgrade/self-update/uninstall, changed-files-only behavior and preservation of settings. | P0.R / P5 |
| RQ10 | Product README bilingual gallery, screenshot workflow, FAQ, issue forms, showcase and support links on master; preserve relevant docs, no unnecessary generated duplication. | P3 / P5 |
| RQ11 | Independent free Windows VM requested 2026-11-23: AppVeyor config on master/base not proof of run; adapt .NET 11/branch and verify actual accessible run, record BLOCKED if not accessible. | P4.03 |
| RQ12 | Full Windows↔Linux migration (2026-11-20): inventory/plan/verify CLI exists; Store/MSIX/portable apps, files/settings/games/drivers and GUI workflows remain partial; no blanket compatibility guarantee for all distros. | P2.05 / P4 |
| RQ13 | REQ-590-05 usage-based bloatware suggestions: old backlog NOT STARTED; no implementation identified in reviewed current source. Only opt-in local metadata, no background monitoring by default, no automatic uninstall. | P3 deferred feature |
| RQ14 | REQ-590-06 per-game profile exists as explicit Save/Apply; AutoGamingMode checks configured executable but automatic per-game apply/restore not demonstrated. Resolve ownership with manual/automatic profiles; no unrelated registry edits. | P2.04 partial |
| RQ15 | REQ-635-11/6.7.1 update manager: ignore list present, need functional verification of real install outcomes, progress/cancel/filters, Windows/Store/driver status, reboot and retries; no duplicate implementation. | P2.03 partial |
| RQ16 | Health/Memory/Cleaning/Registry shared plan, protection of Opsis/PowerToys/user shortcuts, actual PC Manager/CCleaner-like presentation; verify specific exclusion and no destructive false positives. | P0/P2/P3 partial |
| RQ17 | SSD/NVMe distinct icons and identity (2026-09-08), full sensor and driver provider coverage including Intel, HVCI-compatible sources; visual and actual hardware evidence needed. | P2.01/P3/P4 |
| RQ18 | Actual UI click-through, 14 languages, RTL, skins, palettes, DPI, custom-chrome drag/resize/transparency, minimize/maximize/snap behavior (Windows 11 snap flyout known historical limitation), accessibility and navigation. | P3/P4 partial |
| RQ19 | Historical 6.7.1: keep-alive pages versus demand-driven lifecycle, unified guides, status/diagnostics and source hygiene; audit present implementation, do not retain hidden active timers. | P1/P3 verify |
| RQ20 | Final release package: current assembly/installer 7.0.0; synchronize target 7.1.0, splash/help/history 14 languages, Source ZIP, Inno installer ZIP, portable where required, consolidated Claude Code handoff, hashes and working download links. No release without gates. | P5 blocked |
| RQ21 | Code signing postponed; only free, identity-preserving option if available, no paid certificate without user instruction. Not a reason to block all development. | P5 deferred |
| RQ22 | Historical multimedia conversion requirement superseded by v6.7.0 user decision: Opsis Media Player Pro/Waveframe own conversion. Keep Games & Multimedia codec/device diagnostics and suite link, do not duplicate conversion/downloader. Remaining photo-specific requests only assess against suite scope. | EXCLUDED conversion / verify diagnostics |
| RQ23 | 30-minute and sustained endurance, cold/warm startup, handles/threads/RAM/CPU, physical SSD wear vs process writes, actual source-to-target UEFI boot and BitLocker/recovery. Do not equate short VM baseline with these tests. | P4 mandatory |
| RQ24 | Historical build script missing scripts/ai_build_agent.py / automation frequency from October cross-project audit: verify whether still present and applicable before changing; not confirmed on current candidate. | P1 verify |

Latest confirmed Windows CI at audit: run 46070758350, 716 passed (674 desktop + 42 migration), installer and short measurements success. 328 authored .NET fixtures and 9 additional Python fixtures in subsequent cumulative record; no certification of newer candidate by that run. Use the cumulative pass log for exact test evidence, not the older numbers below. Audit based on accessible prior project context and repository history, not a guarantee of access to every past conversation. This audit changes documentation only; implementation/sync/verification are pending. P0.R is now first; new feature proposals deferred until existing release scope is integrated and G0 stable.


### P0.R — integration batch A (2026-11-28)

- RQ02: restored uniquely keyed opt-in Opsis.UI 6 Midnight resources, registration, adoption doc and compile workflow. Checked 19 distinct keys all prefixed Opsis; no existing style replaced. Actual screen-by-screen visual adoption still P3, compile pending.
- RQ06: rechecked candidate MaintenancePaths.IsWithin: root separator fix already present, so retained candidate implementation (absolute paths, ancestor reparse checks, core lease) and ported missing root overlap regression test. Source covered, CI pending.
- RQ08: ported immutable Startup Impact brush fix only, preserving candidate async startup changes; Help/history and export reconciliation remain pending.
- RQ09: restored Inno Setup dynamic dark mode setting; upgrade/changed-file and dark-mode visual checks pending.
- CI: confirmed run 46071174933 (head cc80720c) job 140749655057, 706 desktop + 42 migration = 748 passed; installer and short measurements success. 314 authored .NET fixtures covered by that SHA; latest cumulative recovery/backup changes not covered. Normal CPU 4.284%, WS 314.54 MiB, private 203.57 MiB, writes 635 bytes, handles 985→1093; tray CPU .077%, WS 129.97 MiB, private 46.38 MiB, writes 0, handles 532→667. Normal private RAM worsened in this short run; no controlled performance or physical SSD conclusion.
- Remaining RQ01/RQ03–05/RQ07/RQ10–24 tracked below/above; no bulk reset or master merge. SDK stays 10 until coordinated RQ01 migration. 2 newly authored .NET cases (transfer root and resource packaged), 330 cumulative authored. No merge or release.


### P0.R — integration batch B (2026-11-28)

- RQ05: ported Microsoft signer requirement, but replaced master comma-split DN parsing with DER certificate organization attribute parsing to reject quoted-CN spoofing. Require Authenticode Valid, process exit zero, clean stderr; use absolute system PowerShell path and bounded output/45 s read-only verification timeout. Preserve candidate secured staging and core admission. Real trust chain/WHQL sample and install acceptance remain unverified; organization alone is not certificate pinning or proof of intended driver identity.
- RQ07: combine candidate target GUID/identity checks with before/after catalog identifiers, not master string-difference heuristic. Require readable recognized baseline and an actual new canonical MM/DD/YYYY-HH:MM identifier. Unknown/empty/first backup baseline deliberately yields unverified result, not claim of failure to create image; manual inspection needed. Version identifier reference: https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/wbadmin-get-versions and https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/wbadmin-start-recovery . No physical restore certification. 18 new .NET fixture cases, 348 cumulative authored, pending CI. No merge or release.

### Κατάσταση RQ μετά τα batches A–B (πηγαίος κώδικας, όχι release)

| RQ | Τρέχουσα κατάσταση / επόμενο βήμα |
|---|---|
| RQ01 | VERIFIED HOSTED CI — .NET 11 build/tests/installer at 7960011c |
| RQ02 | PORTED — opt-in resources, compile/visual gates pending |
| RQ03–04 | VERIFIED LOOP / DISTRO CI — physical boot and full interactive acceptance pending |
| RQ05 | PORTED — signer guard; real package trust/install test pending |
| RQ06 | SOURCE COVERED — root logic already fixed; regression test restored |
| RQ07 | PORTED — catalog ID check with volume identity; native backup/restore pending |
| RQ08–09 | PORTED — history/export/brush; E2E passed at 7960011c, latest history and asset assertion pending; visual/settings checks open |
| RQ10–11 | RQ10 restored historical gallery/docs + artifact workflow; new visual capture pending; RQ11 independent provider OPEN |
| RQ12 | PARTIAL — CLI planning; GUI/data migration gaps |
| RQ13 | OPEN/deferred — usage-based suggestions |
| RQ14–19 | PARTIAL — existing functionality; targeted runtime and lifecycle validation |
| RQ20–21 | BLOCKED/deferred — release packaging/signing |
| RQ22 | SUPERSEDED — no duplicate media converter; verify diagnostics |
| RQ23–24 | OPEN — endurance and historical scripts audit |


### P0.R — integration batch C / continuity checkpoint (2026-11-28)

RQ01: coordinated target-framework port from master 744c2e6/0e5853c, source commit 006cd09ed0defc43a47b11864e8cec1e45d84f1c. All five desktop/migration projects now target net11.0(-windows); SDK global.json 11.0.100 (latestFeature roll-forward), System.Management and Microsoft.Data.Sqlite 11.0.0, four build/test workflows, security-scan SDK setup, two fallback launchers, build-facing README/CONTRIBUTING/migration docs. Inno Setup remains unchanged apart from prior dynamic theme port; self-contained publish already uses project target. AppVeyor config adapted to active branch and official SDK bootstrap (configuration only; no verified provider run). Candidate version metadata intentionally still 7.0.0 until final release synchronization RQ20. No compilation or installation yet claimed for this .NET 11 change. Microsoft SDK source: https://dotnet.microsoft.com/en-us/download/dotnet/11.0 .

CI evidence correction: list all GitHub Actions events, not just pull_request results. Push run 46073432667 at d813e9391e2303b7575850a16204624f066a67ac: 719 desktop + 42 migration = 761 passed; 14 Python fixtures passed (source-inventory); installer and short measurements success. Normal CPU 1.263%, WS 276.90 MiB, private 160.68 MiB, I/O write transfer 2476 B, handles 947→1155; tray 0.039%, WS 132.14 MiB, private 47.32 MiB, write transfer 0 B, handles 713→862. Short measurements not a controlled regression comparison or physical SSD result. Also Opsis UI compile run 46100694066 at ec0beed8 passed (.NET 10). Push run 46071167202 at cc80720c had an intermittent GracefulExitRefusesHeavyWorkThenClosesAdmission timeout (line 99, 2 s); other builds passed. Investigate scheduler/queue timing, do not erase or merely label it resolved. Parent 08806a95 run 46100747389 was pending and 46100694073 was in progress at inspection; query again before adding new work. 348 authored .NET cases recorded (20 beyond 328 from A–B), 9 new Python cases within 14 total. New SDK commit adds no test fixtures. Counts of authored fixtures and test-run totals are separate; newer changes not validated by older results.

RQ03/RQ04 deliberately not blindly ported: master Partclone adapter imports sysfs_safety and real loop harness calls get_geometry/clone(exclusive=True), interfaces not present in the candidate raw cloner. Next integrate explicit partition identity/geometry checks, secure destination and descriptor semantics, then adapt loop tests and matrix. Never add unsupported backup/restore success claims. See START_HERE.md for exact next actions, user constraints and evidence.


## P0.R — recovery CI integration (2026-11-28)

RQ04: authored Linux loop-device harness with four tests (actual geometry/copy and trailing bytes, mismatched sectors/capacity, mounted target refusal, Partclone held-source fd create/check/CI-only restore/tamper). Guards require root + GITHUB_ACTIONS, verify each freshly allocated loop backing file before any mutation; production rejects loops. Two Ubuntu runner versions plus Windows 2022/2025 tests; six distro containers (Debian 12/13, Fedora, Arch, openSUSE, Alpine) and migration CLI, shared kernel not independent VM provider. Required Partclone missing is a failure, not skip. Fixed post-image plan to omit initial conservative space check before publish (all identity checks remain); lsblk fixed system dirs for usrmerge variants. 32 regular/mock Python cases plus four loop methods authored; not yet certified. Container and real loop checks not executed locally. No UEFI boot or physical SSD evidence. RQ03 is source implemented (experimental), RQ04 CI configured, not validated.


## P0.R — history, product documentation and verified CI (2026-11-28)

Exact validated source SHA 7960011c034d9e5a49605b2d34e73a0e37f6ca61. UMP01 46101805741 success: job 140845869672 739 desktop + 42 migration = 781 passed (.NET 11), installer and normal/tray baseline; 140845869860 32 Python fixtures passed. Normal/tray CPU 4.414/0.058%, peak WS 274.34/134.28 MiB, private 162.25/48.57 MiB, process write transfer 635/0 B, handles 1143→1270/698→832. Not a physical SSD measurement or endurance proof. Recovery 46101805697 all four jobs passed (Windows 2022/2025, Ubuntu 22.04/24.04; four actual loop methods including held-fd Partclone create/check/CI-only restore/tamper). Linux distro 46101805674 all six jobs green; shared host kernel, not independent distro kernels. Installer 46101805761 all six jobs green including fresh, from-latest and in-app scenario; artifact reports version 7.0.0, NOT final 7.1.0. No proof of real physical WinRE/UEFI/BitLocker boot or full production adapter interactive acceptance. Endurance 46101279059 remains in progress at inspection. RQ01 validated on hosted Windows; RQ03/RQ04 loop/userland gates passed; RQ05/RQ07 fixtures passed but actual driver/backup remain pending. RQ09 installer scenarios passed, visual dark-mode and changed-file/settings preservation review still open.

RQ08: restored master Help history 6.9.12/7.0.0 translations: full LanguageService blobs compared; removing exactly 56 new history lines yields identical candidate file; all 4 keys each occur 14 times. No unrelated translation changes. Help XAML and dynamic newest-version test aligned; 2 new fixture cases for translation counts (350 cumulative authored .NET, current test count still 781). Export-app error handling already exists; TabLayout identical. Added missing installed Partclone asset assertion to installer E2E.
RQ10: restored historical 26 screenshot blobs, gallery provenance, FAQ, two issue templates, reference audit doc, bilingual README links and read-only screenshot CI (artifacts only, no automatic master push). Existing screenshot previews are historical; new capture and visual review pending. No new release or merge. RQ11 independent provider still unverified. Next check new CI and continue P0/P1 (shutdown timeout, process safety, resource lifecycle), with P3 visual/translation acceptance and RQ20 final version pending.


## P0 — bound recovery metadata clients (2026-11-28)

Added RecoveryMetadataProbe with explicit read-only wbadmin get versions (90 s) and reagentc /info (25 s), absolute system paths, argument lists, bounded concurrent stream drains and deadline for both process and pipe completion. Timeout kills only the metadata client, not the service or backup process tree; result unknown. Backup operation remains intentionally without arbitrary deadline. Standalone catalog query now uses the same nonblocking BackupGate as actual backup to prevent duplicate catalog/backup access within process. WinRE uses the shared read-only probe, replacing unbounded ReadToEndAsync. Six new .NET fixture cases (356 cumulative authored), test only argument construction/refusal, not real timeout behavior or physical backup. Latest validated SHA remains 7960011c; new code pending CI. P0 shutdown scheduler timeout remains open; no blanket timeout inflation. Next investigate safe tweak restore, process deadlines, and sustained workload results.


## P0 — journaled legacy registry groups (2026-11-28)

Mouse acceleration (three string values), Game Bar (HKCU + HKLM DWORDs), classic menu (default InprocServer32 value) now use existing typed per-value journals and group preflight/mutex. Restore selects only verified owned members, refuses conflicted/corrupt journals, no hardcoded mouse defaults or GameDVR/CLSID subtree deletion, no forced Explorer restart for classic menu. Sign-out may be needed; empty key containers intentionally retained, actual shell behavior requires real UI validation. Group writes are not atomic against external registry edits: on partial failure retain journals, use explicit tracked restore; mixed states may require bulk recovery. Legacy unjournaled settings deliberately not reset on bulk/uninstall: removed presence-only shell verb/telemetry/CLSID cleanup; dedicated explicit actions remain but their ownership paths still require review. No real registry mutation tests. Six new .NET fixture methods (362 cumulative authored); pending CI. Next P0: background-app per-package journaling (avoid deleting policy trees), SysMain/hibernate/visual presets and explicit shell verbs; P1 daily history and process resource review. No merge/release.


## P1 — bounded daily health history, no idle I/O (2026-11-28)

HealthScoreDailyHistoryService and DiskHealthHistoryService now share DailyHistoryStorage: same LocalAppData filenames/schema, strict bounded 2 MiB reads, 60 distinct sorted dates, 128 disk identities, validation of score/date/capacity, explicit refusal to overwrite malformed/oversized history. Per-process same-day memo avoids repeated reads for already recorded dates/drives; no timer, permanent thread or opened file. Cross-process per-user nonblocking history mutex and atomic write-if-changed helper. Cache only after successful persistence; new drives can be recorded that day. AppSettingsService.WriteIfChanged deletes only its owned staging file. Defense in depth path checks not handle-level anti-reparse guarantee; another process outside GearWin may still race, and drive-letter identity remains an existing limitation. Legacy invalid histories are left intact; reads return unavailable (empty) rather than reset. 16 new deterministic fixture cases (378 cumulative authored); CI pending. No claim of physical SSD write savings yet. Product snapshot workflow 46102812135 at aeb173e7 success; artifact 15245849208 (4,351,913 bytes, expires 2027-02-26), visual content still requires review. Latest full verified baseline remains 781 .NET/32 Python at 7960011c. New grouped registry and history changes pending. No release/merge.


## P1 — synchronous admission evidence / CI checkpoint (2026-11-28)

OperationCoordinator.AcquireAsync now returns the actual admission task directly for noncancellable requests, avoiding redundant async continuation/registration. Cancellable requests retain existing cancellation and lease-release path; no concurrency lane/conflict-policy change. Previous GracefulExitRefusesHeavyWorkThenClosesAdmission timeout test now asserts actual synchronous admission and counts after release instead of a 2 s thread-pool deadline. Added 250 repeated handoffs within one fixture, compatible two user reads on Low tier, queued cancellation slot release (3 new methods; 381 cumulative authored .NET cases). This addresses the observed timing-sensitive test path, not proof of all real shutdown issues; latest code CI pending. Daily history per-day drive memo explicitly capped even if external history deletion occurs; generic bounded reader restricted to reference types. 
Latest full verified run 46102812144 at aeb173e7d7c1e9a795db1db57e85f8354dcb2d1e: job 140848861260 741 desktop + 42 migration = 783 passed, installer and normal/tray baseline; 140848861133 32 Python fixtures passed. Normal/tray CPU 4.958/0.052%, WS 258.75/131.72 MiB, private 144.16/46.85 MiB, process write transfer 635/0 B, handles 1148→1140/515→651. Not controlled endurance or SSD proof. Snapshot CI at same SHA successful; actual images not reviewed. Endurance 46101279059 still running at inspection; installer 46103415352 and baseline 46103415266 on grouped-registry source in progress. Latest unvalidated .NET fixtures after aeb173e7: 6 metadata + 6 group + 16 daily + 3 coordinator =31. Preserve prior recovery matrix evidence separately. Next: safe background app policies, SysMain/hibernate/visual presets and explicit shell verbs; actual resource endurance, per-resource claim coverage and UI validation. No merge/release.


## P0 — background app and SysMain recovery (2026-11-28)

Replaced unbounded Appx inventory and destructive background permission tree deletion with BackgroundAppJournal: current-user removable non-system package selection, max 256 canonical names, bounded 45s read-only inventory, durable protected per-user plan before first write, typed individual Disabled/DisabledByUser value journals. Restore exact owned values even after uninstall; no re-enumeration, no subtree deletion. Legacy/unowned values deliberately left for manual review. Partial failures retain plan/journals; bulk restore only with verified ownership. SysMain now uses SCM journal on same resource lock and path as manual-start optimizer, separate validated disabled-mode snapshot, supports non-delayed automatic/manual original, refuses unknown/other journal or delayed startup. No force stop/start; effect on next normal boot. Low Resource Profile runs via core background worker, disabled button/caught errors; not all-or-nothing, existing prior successful changes remain journaled. 18 new .NET fixture cases, 399 cumulative authored; no real package/service mutation verified. Previous full CI 783 .NET/32 Python; newer pending. Remaining P0: hibernate and visual preset safety, explicit shell verbs, user-facing partial recovery; endurance and independent VM pending. No merge/release.

### Αποδεδειγμένη κατάσταση

- 283 διακριτά ευρήματα, 281 πλήρως ή μερικώς αντιμετωπισμένα σε πηγαίο κώδικα. Όχι 281 πιστοποιημένες διορθώσεις.
- 239 νέες περιπτώσεις δοκιμών έχουν γραφτεί. 225 περιλαμβάνονται στην τελευταία αριθμητικά επιβεβαιωμένη επιτυχή ομάδα: 617 desktop + 42 migration = 659 (run 46067510791, job 140736470806). Νεότερες 14 περιπτώσεις αναμένουν CI.
- Run 46005543469: όλα τα στάδια ολοκληρώθηκαν επιτυχώς, συμπεριλαμβανομένων installer και σύντομης μέτρησης normal/tray. P0.02/P0.03 batch 1: MSI/Windows Settings fallback, ασφαλέστερη UWP διαχείριση και advisory residual review, 10 ευρήματα και 27 νέες περιπτώσεις δοκιμών. Νεότερο SHA παραμένει υπό επαλήθευση.
- Τελευταία πλήρης σύντομη μέτρηση normal/tray: run 46067510791 (13 δείγματα/περίπου 60 s ανά mode). CPU normal 4.458%, tray 0.052%, peak private 155.71/47.30 MiB, peak WS 269.41/132.17 MiB. Handles 1164→1287 / 715→852. Απαιτείται endurance, όχι πιστοποίηση του νεότερου SHA.
- Η δημιουργία του endurance workflow δεν αποδεικνύει εκτέλεση. Δεν υπάρχει ακόμη επιβεβαιωμένο 30λεπτο ή 24ωρο αποτέλεσμα για τον νέο πυρήνα.
- Η 7.1.0 παραμένει υπό ανάπτυξη. Δεν έχει δημοσιευθεί τελικός installer ή release.

- P0.03–P0.05 batch 2: ασφαλής ακύρωση αναμονής επισκευών, bounded output, προστασία storage/ACL και executable, staged settings/task XML. 14 νέα ευρήματα, 16 νέες δοκιμές. Απαιτείται CI και ασφαλής migration παλιού storage όπου τα δικαιώματα δεν επαρκούν.

- P0.03–P0.05 batch 3: μετακίνηση απενεργοποιημένων startup values εκτός Run, least-privilege delayed tasks, scoped task identity, pending/committed profile journal. 10 ευρήματα, 14 δοκιμές. Επόμενο: typed tweak snapshots, ασφαλής ανάκτηση legacy delayed tasks και ownership-aware orphan cleanup.

### Σειρά προτεραιοτήτων

Οι κωδικοί P0–P5 δηλώνουν **σειρά υλοποίησης και εξαρτήσεις**, όχι ποσοστό ολοκλήρωσης. Τα παλαιά UMP παραμένουν κατηγορίες καταγραφής. Καμία αλλαγή σε main ή δημοσίευση τελικής έκδοσης πριν από τις πύλες ποιότητας. Εργασίες ασφαλείς και ανεξάρτητες μπορούν να εκτελούνται παράλληλα σε ξεχωριστά branches· όχι ανεξέλεγκτες διαδοχικές αλλαγές στο ίδιο validation head.

| Προτεραιότητα | Παραδοτέο | Κατάσταση | Εξάρτηση |
|---|---|---|---|
| P0 | Σταθερό, μεταγλωττιζόμενο και ασφαλές validation candidate | ACTIVE | καμία |
| P1 | Καθολικός πυρήνας, παράλληλη λειτουργία και anti-pile-up | PARTIAL | πράσινο P0 CI |
| P2 | Ορθότητα όλων των υπαρχόντων εργαλείων, αισθητήρων και ανάκτησης | PARTIAL | P0, συμβόλαια P1 |
| P3 | Πλοήγηση, skins, προσβασιμότητα και OpsisUI συμβατότητα | PARTIAL | σταθερά συμβόλαια P1/P2 |
| P4 | Πιστοποίηση λειτουργίας, απόδοσης και αντοχής | NOT STARTED ως πλήρης πύλη | σταθερό candidate P1–P3 |
| P5 | Έκδοση 7.1.0, πακέτα και τεκμηρίωση | BLOCKED | όλες οι υποχρεωτικές πύλες |

### P0 — Ασφάλεια και αποκατάσταση αξιόπιστου CI (UMP01, UMP02, UMP08)

- **P0.01** Πάγωμα SHA για validation. Έλεγχος διαφορών από base, διατήρηση των νεότερων αλλαγών recovery/installer. Επιβεβαίωση ακριβούς SHA σε logs και artifacts. Διόρθωση κάθε compile/test regression πριν από νέα μεγάλη δέσμη. Μηδενική ακύρωση ενεργών CI λόγω συνεχών commits.
- **P0.02** Επανέλεγχος elevated uninstall strings, cmd/PowerShell argument injection, ασφαλών executable paths, υπογραφών/ακεραιότητας downloads, μη ασφαλών residual-folder ενεργειών. Όχι εκτέλεση μη επαληθευμένων εντολών χρήστη με αυξημένα δικαιώματα. Διατήρηση ασφαλούς τρόπου απεγκατάστασης.
- **P0.03** Προστασία αρχείων, μητρώου, συντομεύσεων και εκκίνησης: reparse points, hardlinks, TOCTOU, ακριβής ταυτότητα στόχου, allowlists και επαληθευμένη αναίρεση. Διατήρηση προσωπικών tweaks/shortcuts. Καμία μαζική επαναφορά χωρίς ρητή επιλογή. Αποκατάσταση ασφαλούς χειροκίνητης λειτουργικότητας όπου προηγούμενο containment περιόρισε δυνατότητα.
- **P0.04** Εξωτερικές διεργασίες και τερματισμός: κοινό συμβόλαιο start/exit/partial/timeout/unknown, οριοθετημένη έξοδος, disposal, ασφαλή argument lists. Ακύρωση αναμονής δεν ισοδυναμεί με βίαιη διακοπή servicing ή backup. Έλεγχος child/orphan διεργασιών και υπολειπόμενων ενεργειών.
- **P0.05** Συνεπείς αποθηκευμένες ρυθμίσεις, ACL, ατομικές εγγραφές, ελεγχόμενο rollback, επιβεβαίωση πραγματικής κατάστασης μετά από κάθε προνομιακή αλλαγή. Έλεγχος dependencies και άδειες τρίτων.

**Gate G0:** όλα τα tests του παγωμένου SHA ολοκληρώνονται και δεν υπάρχουν γνωστά κρίσιμα θέματα απώλειας δεδομένων, privilege escalation ή ακεραιότητας Windows χωρίς ασφαλή αντιμετώπιση. Κάθε test που δεν εκτελέστηκε καταγράφεται ως NOT RUN, ποτέ ως PASS.

### P1 — Ενιαίος πυρήνας, demand activation και παράλληλη χρήση (UMP03, UMP05, UMP06)

- **P1.01** Κατάλογος κάθε υπάρχουσας υπηρεσίας/εργαλείου: trigger, opt-in, dependencies, timers/events, cancellation, ownership, lifecycle, πραγματική χρήση πόρων και κανόνες παραλληλισμού. Service boundary ως μοναδική πύλη, χωρίς nested leases. Συμφιλίωση πραγματικής και επιθυμητής κατάστασης, retry μετά από failed start, ασφαλές stop/re-enable και imports. Μην αλλάζουν σιωπηλά ήδη αποθηκευμένες επιλογές.
- **P1.02** Demand-driven loading μέχρι επίπεδο υποκαρτέλας/ενότητας: απενεργοποιημένα εργαλεία χωρίς timers, listeners, scans ή helper processes· χειροκίνητες λειτουργίες αρχικοποιούνται στο πρώτο αίτημα. Όχι eager fonts/process/device scans σε κρυμμένες ενότητες. Βασικός πυρήνας/GUI/tray παραμένουν διαθέσιμα. Προσαρμοστικός governor μόνο με τεκμηριωμένο κόστος.
- **P1.03** Resource-aware admission: σταδιακή αντικατάσταση γενικού αποκλεισμού με σαφή resource claims για τόμο/φυσικό δίσκο, κανονικοποιημένες διαδρομές και πραγματική ταυτότητα, servicing, registry scope, network/power/startup. Reader/writer compatibility, σταθερή σειρά κλειδωμάτων, cross-process consistency. Άγνωστο scope παραμένει συντηρητικό· δεν χαλαρώνει επικίνδυνα. Πρώτα συμβατές αναγνώσεις και ασφαλή ανεξάρτητα scopes, μετά επαληθευμένες μεταβολές. Δεν επιβάλλεται global single-task mode.
- **P1.04** User priority, fairness και bounded backpressure: διακριτά όρια για user tools και background scans, no preemption ενεργού κρίσιμου έργου, no starvation, no periodic catch-up bursts. Αναμονή/εκτέλεση/ακύρωση/αποτυχία εμφανίζονται με πραγματικό ID και αιτία. Πολλά παράθυρα/εργαλεία μπορούν να συνυπάρχουν· modal περιορισμοί να ελέγχονται.
- **P1.05** Anti-pile-up: shared telemetry, one-flight ανά πηγή, bounded caches/queues/logs, αποδέσμευση WMI/PDH/ETW handles, subscriptions, timers και views. Lifetime cancellation χωρίς πολλαπλασιασμό native requests. Sleep/resume, hibernation, αλλαγή δικτύου/τροφοδοσίας, hotplug: επανασυγχρονισμός αντί replay. Native timeout/isolation όπου είναι ασφαλές.
- **P1.06** Ασφάλεια cross-process και shutdown, opt-in headless jobs με φρέσκια αποθηκευμένη ρύθμιση, διατήρηση πραγματικών exit codes. Μέτρηση queue depth, active operations, thread/handle counts με opt-in diagnostics μόνο, όχι νέο μόνιμο logging.

**Gate G1:** disabled features έχουν μηδενική περιοδική δραστηριότητα που τους ανήκει· επιλεγμένες υπηρεσίες ενεργοποιούνται ανεξάρτητα και σταματούν σωστά. Δοκιμές συμβατών/ασύμβατων ζευγών, παράλληλων παραθύρων, cross-process scopes, fairness, cancellation, 10.000 periodic attempts και 1.000 navigation requests χωρίς ουρά που αυξάνεται ανεξέλεγκτα. Τα προσωρινά όρια 32 pending, 2–3 regular user leases και έως 2 snapshot leases αξιολογούνται με μετρήσεις, όχι ως απαράβατα τελικά όρια.

### P2 — Ολοκλήρωση υπαρχουσών δυνατοτήτων (UMP02, UMP03, UMP04, UMP05)

- **P2.01** Αρχική/θερμοκρασίες: επαλήθευση CPU package/die, GPU core, DIMM sensors, πραγματικής ταυτότητας φυσικού δίσκου, NVMe/SATA/USB fallbacks, multi-GPU και hotplug. LibreHardwareMonitor/PawnIO/Windows Storage/NVML/ADL μόνο από πραγματικές υποστηριζόμενες πηγές. Χωρίς αυθαίρετες θερμοκρασίες, λάθος δίσκο ή bypass ασφάλειας. Άγνωστο + αιτία όταν δεν υπάρχει διαθέσιμη μέτρηση. Ανεξάρτητα από αργό driver να παραμένει αποκρίσιμο το UI.
- **P2.02** Ενοποιημένη συντήρηση/health check/RAM boost: κοινό scan → plan → explicit selection → apply → verify, ακριβή λογικά bytes, ασφαλείς προεπιλογές, όχι σκόπιμα επιλεγμένες ρυθμίσεις ως βλάβες. Πλήρης έλεγχος browser cache, startup, shortcuts και registry restore. Σύγκριση ποιότητας με PC Manager/CCleaner χωρίς αντιγραφή επιθετικής συμπεριφοράς.
- **P2.03** Updates/apps/drivers: πραγματική κατάσταση installed/unknown, ενιαίες απογραφές, MSI/WinGet/Store/WUA/driver outcomes, safe retries/partial/reboot. Έλεγχος των 29 προτεινόμενων εφαρμογών και 31 embedded icons· άδειες/attribution πριν από διανομή.
- **P2.04** Tweaks, δίκτυο, power profiles και υπηρεσίες: typed durable snapshots, readback, rollback και ανάκαμψη μετά από crash/partial activation. Αποκατάσταση ακριβώς προηγούμενων τιμών, όχι γενικών defaults. Συμβατότητα auto/manual και διατήρηση επιλογών χρήστη.
- **P2.05** Backup/recovery/transfer: επαληθευμένο system image, catalog, bare-metal restore σε disposable VM, offline Linux clone kit, μεταφορά Windows↔Linux, εφαρμογές/αρχεία και dry-run preflight. Διαχωρισμός source/destination physical disks, κρυπτογραφημένα volumes/UEFI/GPT, σαφείς περιορισμοί μη υποστηριζόμενων περιπτώσεων. Χωρίς clone σε πραγματικό δίσκο χρήστη για tests.
- **P2.06** Vault, shredder, password tools και clipboard: ακεραιότητα και rollback, όρια RAM/I/O, προστασία μυστικών, σαφής διάκριση encrypted copy και πραγματικού lock. SSD secure deletion δεν εγγυάται φυσική διαγραφή. Επαναφορά της ασφαλούς αρχικής συμπεριφοράς όπου είναι εφικτή.
- **P2.07** Πλήρης απογραφή και λειτουργική δοκιμή των υπόλοιπων υφιστάμενων εργαλείων (codecs, multimedia, fonts, συσκευές, εργαλεία συστήματος, benchmarks). Καμία σιωπηλή κατάργηση ή διπλοεγγραφή.

**Gate G2:** operation-level αποτελέσματα και δοκιμές ανά εργαλείο. Όλες οι κρίσιμες επεμβάσεις δοκιμάζονται σε disposable περιβάλλον με επιτυχία/αποτυχία/rollback. Φυσικό hardware που δεν υπάρχει στο VM δηλώνεται NOT RUN, όχι ψευδώς πιστοποιημένο.

### P3 — UI/UX και συμβατότητα (UMP06, UMP07)

- **P3.01** Ενιαίος μηχανισμός πλοήγησης, κατάσταση εργαλείων, status bar, έλεγχος Ctrl+1–9, καρφιτσωμένων συντομεύσεων, tray/menu, επιλογών χρήστη και επιστροφής σε προηγούμενη ενότητα. Αποφυγή δημιουργίας διπλών προβολών.
- **P3.02** Normal + 5 ειδικά skins με διατήρηση της ταυτότητάς τους· 25 καταχωρίσεις παλετών × light/dark (50 συνδυασμοί) και 20 πρόσθετα themes όπως καταγράφονται στον κατάλογο. Έλεγχος πραγματικού καταλόγου πριν από την τελική μήτρα, όχι τεχνητός πολλαπλασιασμός μετρικών. DPI 100/125/150/200%, μικρή οθόνη, αλλαγή μεγέθους, scroll, αντίθεση, animations, overlays, focus, icons, status overflow.
- **P3.03** 14 γλώσσες/RTL, accessibility names, πλήρης μετάφραση νεότερων EL/EN fallback strings, σωστό keyboard navigation. Κοινές συμβάσεις OpsisUI χωρίς αλλαγές που σπάνε άλλες εφαρμογές ή ισοπεδώνουν τα ξεχωριστά skins.
- **P3.04** Πραγματικές UI automation δοκιμές και screenshots, όχι μόνο window construction. Όλα τα κουμπιά/ρυθμίσεις με ασφαλή mock ή disposable test, destructive flows με ρητή επιβεβαίωση.

**Gate G3:** επαληθευμένα screenshots και test matrix, κανένα κρίσιμο clipping/contrast/πλοήγηση, καμία απώλεια αποθηκευμένων επιλογών και καμία αποτυχία των βασικών user workflows.

### P4 — Πλήρης επαλήθευση απόδοσης, σταθερότητας και αντοχής (UMP08, UMP09)

Η μέτρηση αρχίζει νωρίς με σταθερό baseline, αλλά η τελική πιστοποίηση γίνεται στο ίδιο ακριβές release candidate.

| Μετρήσιμος στόχος (προσωρινό engineering budget) | Κριτήριο |
|---|---|
| Cold/warm startup | 10 επαναλήψεις ανά mode στην ίδια VM/ρύθμιση με 7.0.0, median/p95 time-to-first-interactive και χρόνος έτοιμου tray· στόχος τουλάχιστον 20% βελτίωση median χωρίς χειροτέρευση p95 |
| Idle CPU | στόχος μέσου machine-normalized <0.1% tray, <0.5% ορατή αδρανής εφαρμογή, με δηλωμένα enabled features και core count |
| Idle RAM | στόχος tray working set <100 MiB και private bytes <50 MiB, αξιολόγηση εφικτότητας σε ίδιο περιβάλλον· όχι forced trimming |
| Anti-pile-up | μετά warm-up, σύγκριση πρώτου/τελευταίου σταθερού παραθύρου private bytes, WS, handles, threads, CPU, queues· όχι συνεχιζόμενη ανοδική τάση μετά από επανειλημμένο ίδιο workload |
| Προσωρινά αρχεία/SSD | καταγραφή process I/O και αλλαγών αρχείων, καμία επαναλαμβανόμενη άσκοπη εγγραφή σε idle/disabled features· process transfer bytes δεν είναι physical NAND wear |
| Απόκριση με ταυτόχρονα εργαλεία | p95 UI latency και πραγματική παράλληλη πρόοδος συμβατών εργασιών, χωρίς deadlock ή starvation· αδρανής CPU να επιστρέφει κοντά στο baseline μετά το έργο |

- **P4.01** GitHub Windows Server 2022/2025: πλήρες unit/integration/migration/installer E2E στο ίδιο SHA· clean install, upgrade, uninstall, offline assets και επανεγκατάσταση. Linux synthetic clone tests και disposable recovery targets.
- **P4.02** Πλήρης εκτέλεση ανεξάρτητου endurance workflow (15 λεπτά normal + 15 λεπτά tray). Έπειτα 1–2 ώρες ενεργού navigation/workload churn, sleep/resume και πολλαπλά εργαλεία. 24ωρη δοκιμή σε κατάλληλο περιβάλλον με επιτρεπόμενο χρόνο εκτέλεσης. Δεν παρουσιάζεται 30λεπτος έλεγχος ως 24ωρη πιστοποίηση.
- **P4.03** Δεύτερο ανεξάρτητο Windows VM, κατά προτίμηση Windows 11 client, εφόσον υπάρχει νόμιμη δωρεάν δυνατότητα πρόσβασης. Δοκιμή σε φυσικό hardware για αισθητήρες, πραγματικές θερμοκρασίες, GPU/FPS, USB/NVMe και HVCI. Αν υποδομή δεν είναι προσβάσιμη, καταγραφή BLOCKED με ακριβή περιορισμό· όχι ισχυρισμός εκτέλεσης.
- **P4.04** Μετρήσεις έναντι 7.0.0 σε συγκρίσιμο workload (ίδιες ρυθμίσεις, hardware, διάρκεια και warm-up). Raw CSV/TRX/screenshots, SHA, OS, tier, enabled features, αριθμός samples, process tree, median/p95 και σφάλματα. Ρητή διάκριση regressions από φυσιολογική διακύμανση cache/GC. Εάν στόχος δεν επιτευχθεί, εντοπισμός αιτίας και διόρθωση ή τεκμηριωμένη αποδοχή από χρήστη, όχι αλλοίωση μέτρησης.

**Gate G4:** πλήρη αποτελέσματα συμβατότητας/αντοχής στο τελικό SHA, καμία γνωστή κρίσιμη αστοχία, αποδεδειγμένη σταθεροποίηση πόρων και αποδεκτοί μετρήσιμοι στόχοι. Τυχόν μη διαθέσιμη υποδομή ή φυσικό hardware παραμένει ρητό κενό πιστοποίησης· κρατάμε RC μέχρι να συμφωνηθεί διαφορετική αποδοχή.

### P5 — Τελικό πακέτο και παράδοση (UMP10)

- **P5.01** Ενοποίηση έκδοσης 7.1.0 σε app/assemblies/Help/History/splash/installer, αναθεωρημένο changelog, MIT για ίδιο κώδικα και πραγματικές άδειες/attribution τρίτων. Καμία ψευδής δήλωση υπογραφής.
- **P5.02** Υφιστάμενος Inno Setup μηχανισμός, source ZIP με όλα τα αναγκαία assets και build scripts, installer ZIP, checksums SHA-256, ακριβές commit και reproducible build instructions. Διατήρηση ενός ενιαίου source snapshot· κανένα περιττό προσωρινό πακέτο στο τελικό ZIP.
- **P5.03** Ένα συγκεντρωτικό handoff για Claude Code: αρχιτεκτονική, κατάλογος εργαλείων, tests, known limitations, dependency notices, evidence και rollback instructions. Το ιστορικό ευρημάτων διατηρείται ως ξεχωριστό ledger· όχι πολλαπλά ανταγωνιστικά roadmap.
- **P5.04** Τελικό έλεγχο release artifacts, εγκατάσταση από το πραγματικό ZIP, επαλήθευση λήψεων και δημοσίευση μόνο μετά τις πύλες. Χωρίς έγκριση δεν γίνεται merge/release.

**Gate G5:** πηγαίος κώδικας, installer και αποδείξεις αντιστοιχούν στο ίδιο πιστοποιημένο SHA. Εάν G0–G4 δεν έχουν κλείσει, το αποτέλεσμα είναι RC, όχι «τελική 7.1.0».

### Κανόνες επόμενων Ultra Mega Passes

1. **Άμεσο επόμενο πέρασμα:** P0.01, επιβεβαίωση υπάρχοντος CI και fix compile/regression, έλεγχος P0.02 και P0.03. Προτεραιότητα στη σταθεροποίηση αντί νέων μεγάλων αθροιστικών commits.
2. Μετά το πράσινο validation, P1.01–P1.04 και P0.04/P0.05 ανά ανεξάρτητη ασφαλή δέσμη. P1.05/P1.06 συνδέονται με endurance. P2.01 αισθητήρες υψηλής προτεραιότητας παράλληλα όπου δεν δημιουργεί σύγκρουση. P2.05 recovery παραμένει κρίσιμη πύλη ασφαλείας, όχι αναβολή για νέα features.
3. Κάθε πέρασμα περιλαμβάνει μικρό αριθμό συνεκτικών αλλαγών και test cases, commit SHA, πραγματικά αποτελέσματα και σταθερά IDs. Αναφορά πόσα νέα διακριτά ευρήματα εντοπίστηκαν, πόσα αντιμετωπίστηκαν σε source και πόσα πιστοποιήθηκαν. Όχι αυθαίρετο ποσοστό συνολικής ολοκλήρωσης.
4. Ένα ενεργό validation head κάθε φορά, ανεξάρτητα workflows endurance/UI όπου χρειάζεται, χωρίς ακύρωση ενεργών μετρήσεων. Μηδενικές destructive δοκιμές σε σύστημα χρήστη. Διατήρηση όλων των ήδη υπαρχουσών δυνατοτήτων και συμβατών ταυτόχρονων ενεργειών.

### Αντιστοίχιση παλιών UMP

| Παλαιά κατηγορία | Νέα προτεραιότητα |
|---|---|
| UMP01 απογραφή | P0/P1 |
| UMP02 ασφάλεια | P0/P2 |
| UMP03 απόδοση | P1/P2/P4 |
| UMP04 συντήρηση | P2 |
| UMP05 πυρήνας | P1/P2 |
| UMP06 κατάσταση/πλοήγηση | P1/P3 |
| UMP07 εμφάνιση | P3 |
| UMP08 regression | P0/P3/P4 |
| UMP09 endurance | P4 |
| UMP10 έκδοση | P5 |

---

## Ιστορικό roadmap — προηγούμενες εκδόσεις

Το παρακάτω ιστορικό διατηρείται αυτούσιο για ιχνηλασιμότητα. Οι παλιές σημάνσεις DONE δεν αποτελούν νέα πιστοποίηση της 7.1.0.


Source: user-authored backlog (GEARWIN.MD, pasted 2026-09-29). Requirement format per CLAUDE.md §8.
Status values: DONE / PARTIAL / BLOCKED / NOT STARTED.

## v5.7.0

- REQ-570-01 DONE (2026-09-29) — Root cause: the "msstore" upgrade source only *opens* the Store's Downloads & Updates page (`cmd /c start ms-windows-store://...`), which returns exit code 0 almost instantly — long before any real update could happen — and this was being counted as a genuine success. `OptimizationView.BtnUpgradeSelected_Click` now buckets msstore items separately ("opened in Store", not counted as succeeded/failed, not removed from the list).
- REQ-570-02 DONE (2026-09-29) — Implemented together with REQ-570-12's new "launch to tray" toggle: `App.xaml.cs` now runs `UpdateNotificationService.RunCheckAsync()` immediately when started with `--tray`, instead of waiting for the periodic timer. Home tab already updates reactively via `UpdatesHubService.Changed`.
- REQ-570-03 DONE (2026-09-29) — "Ultimate Performance" toggle moved from the generic Tweaks list into the Optimization tab, directly between Gaming Mode and Auto Gaming Mode; Scheduled Maintenance moved to the end.
- REQ-570-04 DONE (2026-09-29) — Found and fixed: the Registry Cleaner card in HealthView.xaml was the one card in the whole app missing its standard `Margin="0,16,0,0"` (verified by auditing every `CardStyle` Border in Views/*.xaml — no other instances found).
- REQ-570-05 DONE (2026-09-29) — Chrome/Edge/Brave/Opera/Vivaldi/Firefox were already supported; added the one clearly-missing variant, Opera GX (separate profile folder from regular Opera).
- REQ-570-06 DONE (2026-09-29) — Unified browser password manager, Chromium-family only (Chrome/Edge/Brave/Vivaldi/Opera/Opera GX): DPAPI + AES-GCM decrypt of each browser's local "Login Data" SQLite file (Microsoft.Data.Sqlite), all local, no network transmission. New Views/PasswordManagerWindow.xaml(.cs) + Services/PasswordVaultService.cs, opened from a new card in Network & Security (next to the existing HIBP breach check). Firefox (NSS-based store) intentionally out of scope - much bigger lift (nss3.dll interop), can be a separate future item if requested.
- REQ-570-07 DONE (2026-09-29) — Added Brave, OBS Studio, Telegram Desktop, Everything, ShareX, LibreOffice to the Recommended list.
- REQ-570-08 DONE (2026-09-29) — Root cause: `ThemedMessageBox`'s icon TextBlock had no explicit Foreground at all (inherited a dark default) — the "ℹ" (Information) glyph in particular renders as a squared-off "i", nearly invisible in dark mode. Now colored per icon type (red/orange/accent), visible in both themes.
- REQ-570-09 DONE (2026-09-29) — `SidebarShortcuts.cs`'s Settings entry: `GlyphKind.Palette` → `GlyphKind.Gear` (shared by both the sidebar and the horizontal modern menu strip, same data source).
- REQ-570-10 DONE (2026-09-29) — No official feed exists for ViVeTool IDs (confirmed - none does, anywhere, this is a third-party community tool); "sync" implemented as a periodic manual refresh of the curated list from a reputable public source (pureinfotech.com/vivetool-codes-enable-features-windows-11, checked 2026-09-29) rather than a live feed. Added 5 new GA-track features (Taskbar positioning, smaller taskbar buttons, customizable right-click menu, Low Latency Profile, modern sign-in/shutdown animation) to Services/ViveToolService.cs's CuratedFeatures, bringing the list from 10 to 15.
- REQ-570-11 DONE (2026-09-29) — Skins tab content merged into the Theme Settings tab (radio buttons + description moved in as-is, same `Skin_Changed` handler); the separate Skins TabItem removed.
- REQ-570-12 DONE (2026-09-29) — New first tab (reuses the "Settings" window-title string) with Desktop Widget, Update Notifications, Low Disk Notifications (all moved from Theme tab, unchanged logic), plus a new "Launch with Windows to Tray" toggle (`SystemService.SetLaunchWithWindowsToTray`, registry Run key + `--tray` launch arg).
- REQ-570-13 NOT STARTED — Code signing is not a code change (needs a purchased certificate + signing step in the release pipeline) — flagged for user decision, not something to implement blindly.

## v5.8.0

- REQ-580-01 PARTIAL (2026-09-30) — Extended beyond the initial PARTIAL: `OptimizerWpf.Tests/UIAutomationClickThroughTests.cs` now does genuine click-through using the real `System.Windows.Automation` client APIs (UIAutomationClient/UIAutomationTypes - part of the net8.0-windows desktop shared framework, no extra package), in-process (avoids the Administrator-elevation UAC-hang risk of launching GearWin.exe as a separate process). Opt-in only (`GEARWIN_UI_CLICKTHROUGH=1`, not part of the default `dotnet test` battery) since it shows real windows and clicks on the live desktop. Getting this working safely took several live-debugging iterations, each a genuine finding: (1) an initial cross-thread Invoke()-with-timeout design corrupted WPF's native message loop and crashed the test host after ~13 minutes - redesigned to invoke synchronously on the window's own thread; (2) the automation tree included native title-bar chrome (Minimize/Maximize/System Menu) alongside the app's own controls - clicking "System Menu" opened a real blocking Win32 menu; filtered by `NativeWindowHandle != 0`; (3) `ThemedMessageBox.Show()` genuinely calls `ShowDialog()` (~108 call sites app-wide) - added a generic auto-dismiss watcher (logs full text, then closes) instead of guessing which of 108 sites to skip; (4) `UwpAppManagerWindow`'s "Scan" (real WinRT/PackageManager enumeration) reproducibly crashed the test host 2 more times even with a settle delay - excluded that one control, scoped to that window. Final verified state: clean pass, ~19s, 0 real findings (2 known/logged test-harness artifacts correctly excluded from failures: `AddContextMenuDialog` needs `ShowDialog()` not `Show()`, and a stale pre-click control snapshot after a window closes itself). Still does NOT cover `HealthCheckWindow`'s real "Optimize Now" cleanup or `ViveToolWindow`'s real feature-toggle switches (excluded on purpose - genuinely destructive/system-modifying actions must never be blindly triggered by an automated pass) and does not do memory-leak detection - those remain out of scope.
- REQ-580-02 DONE (2026-09-30) — Font management: new "Fonts" tab in Settings (added last, so existing `initialTabIndex` call sites for other tabs stay valid). Installed-fonts section lists every system font (`Fonts.SystemFontFamilies`) with a live preview of user-typed text in each font. Suggested-fonts section lists 12 well-known free/open-license fonts (Inter, Roboto, Open Sans, Montserrat, Poppins, Lato, Nunito, Source Sans 3, Merriweather, Playfair Display, JetBrains Mono, Fira Code) with a button opening each font's official Google Fonts preview/download page - no font files hosted/redistributed by us, so licensing (all OFL/Apache 2.0) is handled by Google Fonts itself, not something we need to track. New `Services/FontSuggestionService.cs`.
- REQ-580-03 DROPPED (2026-09-29) — A Spotify/YouTube downloader was proposed by the user, explicitly including "incorporate all [GitHub downloaders/encoders]". Declined: there is no legitimate way to bulk-download Spotify audio (DRM-protected; every such tool works by circumventing DRM, illegal under DMCA §1201 and equivalents, not just a ToS issue), and bundling a YouTube downloader as a one-click feature in a distributed installer is a materially different (and unacceptable) risk from a user running a CLI tool for personal use. User confirmed dropping the item entirely rather than narrowing scope.
- REQ-580-04 DONE (2026-09-29) — Help window was stale in two ways: the browser password manager (new this session) was completely missing, and the "Secondary Features" summary still said "Appearance Settings" (renamed to "Settings" back in v5.7.0) with no mention of update/low-disk notifications, launch-to-tray, or the password manager. Added a full Network & Security entry for the password manager, and refreshed the Secondary Features summary line, in all 14 languages.
- REQ-580-05 DONE (2026-09-30) — Ported MotionDeskStudio's opacity mechanism (separate project, `<MotionDeskStudio repo>\src\UI\MainWindow.cs`'s `BuildOpacityRow`: a continuous 60-100% TrackBar with live preview as you drag, persisted via `AppSettings.WindowOpacity`). Replaced GearWin's previous 3-option radio-button mechanism (`WindowOpacityMode`: None/Light/Medium) with a WPF `Slider` (`Minimum=60 Maximum=100`, same lower bound as MotionDeskStudio - below that text becomes hard to read) in `AppearanceSettingsWindow.xaml`'s Settings tab, applying live to `MainWindow.Opacity` on every `ValueChanged`. New `AppSettingsService.WindowOpacityPercent` (int) replaces the old string field; the 3 now-dead `Appr_OpacityNone/Light/Medium` language keys were removed from all 14 languages. Caught and fixed one real bug during implementation: wiring `ValueChanged` directly in XAML fired during `InitializeComponent()` itself (as `Minimum="60"` coerced the initial `Value` from 0), before a sibling label element existed yet, causing a `NullReferenceException` - fixed by wiring the handler in code-behind after construction instead (caught by the existing `WindowSmokeTests.cs`).
  - **Follow-up (2026-09-30, v5.10.1)**: user reported the slider produced no real transparency ("δεν υπάρχει διαφάνεια, σκουραίνει ή γίνεται πιο ανοιχτό το χρώμα"). Root cause confirmed via git history: WPF's `Window.Opacity` only produces genuine see-through transparency when `AllowsTransparency="True"`, which itself requires `WindowStyle="None"` (no native OS chrome) - without it, `Opacity` just fades the window's own opaque surface toward black. The *original* `Optimizer.ps1` (WinForms) had real transparency natively via `$mainForm.Opacity` (WinForms handles this via native layered windows even with normal chrome) - this was silently lost during the original PowerShell→WPF port, before this session, and the v5.10.0 slider just extended the already-broken mechanism with nicer UI. Fixed properly per explicit user confirmation (asked first given the tradeoffs): `MainWindow.xaml` is now `WindowStyle="None" AllowsTransparency="True"` with `shell:WindowChrome` (native .NET, `System.Windows.Shell`, no extra package) restoring move/resize/maximize-on-doublelick/edge-snap and fixing the classic "maximized borderless window covers the taskbar" bug automatically. Added custom-drawn Minimize/Maximize-Restore/Close buttons (Segoe MDL2 Assets glyphs, Close gets red hover per Windows convention) since removing native chrome removes the OS-provided ones. Known, documented limitation: Windows 11's "hover over maximize to see snap-layout flyout" doesn't appear (requires reporting the button via `WM_NCHITTEST HTMAXBUTTON`, out of scope) - drag-to-edge snap and Win+arrow-key snap still work normally (OS-level, independent of custom chrome). **Not visually verified by the assistant** - no screenshot/GUI-automation capability exists in this session for a native, Administrator-elevated desktop app; build and passing tests confirm compilation and construction only, not actual drag/resize/transparency behavior. Explicitly flagged to the user to verify by hand after installing.
- REQ-580-06 DONE (2026-09-29) — Flags are already vector (no resolution issue) but 3 had a genuine fidelity gap vs. the real flag: Chinese flag had 1 dot instead of the 4 minor stars, Korean flag was missing all 4 trigrams (☰☵☲☷), Indian flag's Ashoka Chakra was a plain ring with no spokes. All 3 fixed in AppearanceSettingsWindow.xaml. Audited the remaining 11 language flags - already accurate simplified representations, no canonical element missing.

## v5.11.0+ (user-proposed features, 2026-09-30)

User asked for feature proposals; then asked to implement all of them, plus "for photos/images, all available tools/capabilities from free & paid applications & GitHub". The photo/image part is unbounded as stated - scoping it to a concrete, legitimate, non-infringing feature set (real .NET-native or well-established libraries, no wholesale copying of code from other projects/repos without individual license review) rather than attempting literal parity with every professional photo app. Tracked as REQ-590-xx.

- REQ-590-01 DONE (2026-09-30) — Moved the Fonts section (installed-font preview + suggested free fonts, REQ-580-02) from Settings to the System tab, per explicit user request: it's system inventory, not an app preference, matching System's existing inventory-style items (Startup, Processes, Services, Disk analysis).
- REQ-590-02 DONE (2026-09-30) — Saved Wi-Fi password viewer, Network & Security tab, next to the existing browser password manager (same legitimacy: local-only, user's own machine). New `Services/WifiPasswordService.cs` uses the native WLAN API (`wlanapi.dll` P/Invoke, `WLAN_PROFILE_GET_PLAINTEXT_KEY`) rather than parsing `netsh wlan show profile key=clear` text output - netsh's field labels ("Key Content" etc.) are OS-display-language-dependent and would silently break parsing on a non-English Windows install (this machine is Greek-locale) where the XML profile format's tag names are constant regardless of OS language. New `Views/WifiPasswordWindow.xaml(.cs)`, added to `WindowSmokeTests.cs`.
- REQ-590-03 DONE (2026-09-30) — GearWin settings backup/restore (export/import `AppSettings.json`) in the Settings tab - useful before reinstalling Windows or moving to a new PC. `AppSettingsService.Export/Import` validates the file actually deserializes as `AppSettings` before ever overwriting the real settings file, so an invalid/corrupt file can't destroy existing settings. Import is confirmed via dialog and tells the user a restart is needed for full effect (honest - not all settings apply live to already-open windows).
- REQ-590-04 DONE (2026-10-02, compile + 3 unit tests; NOT click-tested on Windows) — System health timeline (Event Log crash/BSOD correlation with recent driver/app installs), System tab.
- REQ-590-05 NOT STARTED — App-usage-based bloatware removal suggestions (track which installed apps are actually opened vs. dormant), Bloatware tab.
- REQ-590-06 NOT STARTED — Per-game Tweaks profiles (extend Auto Gaming Mode to remember settings per game instead of one global profile), Optimization tab.
- REQ-590-07 NOT STARTED — New Multimedia tab: media library dashboard, batch photo/video converter & resizer, duplicate/near-duplicate photo finder, EXIF viewer, codec/hardware-decode diagnostics, screen recorder shortcut, audio tag editor/renamer, wallpaper slideshow manager, plus a curated (not literally-everything) set of additional photo tools (batch watermarking, basic crop/rotate/brightness-contrast, HEIC/RAW-preview conversion, contact-sheet/collage generator).

## v6.3.5 (GEARWIN.MD, pasted 2026-10-05, branch `claude/full-audit`, NOT yet committed)

Source: a second GEARWIN.MD backlog (13 Greek-language items, unnumbered in the original - numbered here in the order they appeared).

- REQ-635-01 DONE (2026-10-06) — Games and Multimedia tabs merged into one ("Games & Multimedia"), `Views/GamesMediaView.xaml(.cs)` replaces `GamesView`/`MultimediaView`. `NavItem.cs`, `OnboardingWindow.xaml.cs`, `MainWindow.xaml(.cs)`, and all 14 language blocks (`TabGamesMedia`/`Onb_GamesMediaDesc` keys) updated to match.
- REQ-635-02 DONE (2026-10-06) — Per-game settings card: exe-picker button, GPU-preference combo, and the "disable fullscreen optimizations" checkbox are now on one row (were 3 stacked rows).
- REQ-635-03 DONE (2026-10-06) — Shield icon (`Views/HeaderGlyphIcon.xaml`'s `PartShield`) was the only "closed" icon shape with `Fill="Transparent"` (outline-only); given a `CardBgBrush` fill like the other closed icons (Tools/Lock/Gear), fixing poor legibility in dark mode.
- REQ-635-04 DONE (2026-10-06) — Audited Multimedia's card structure against System tab's convention (`CardStyle`, header `Grid Margin="0,0,0,10"`, `HeaderGlyphIcon` 16x16 + `CardTitleStyle`); found and fixed two cards (Codecs, Converter) using `0,0,0,6` instead of the standard `0,0,0,10`.
- REQ-635-05 DONE (2026-10-06) — Codec list now also detects third-party codec packs (K-Lite via Program Files folder check, plus a DirectShow `HKEY_CLASSES_ROOT\Filter` registry scan filtered to known third-party names), not just the 7 Microsoft Store Media Feature Pack extensions. Shown in a 3-column grid.
- REQ-635-06 DONE (2026-10-06) — Converter (`Services/MultimediaServices.cs`'s `MediaConverterService`) gained WebM (VP9), MKV (H.264), OGG, AAC, WAV, FLAC presets (was 5 presets, now 11) plus a `MediaQuality` enum (Low/Medium/High) threaded through CRF/bitrate per preset, with a new `CmbQuality` combo in the UI.
- REQ-635-07 DONE (2026-10-06) — Tab strip (`MainWindow.xaml`'s `TabStripScroll`) now scrolls horizontally on mouse wheel when it overflows (was drag-only via the thin scrollbar).
- REQ-635-08 DONE (2026-10-06) — Searchbar modernized: wrapped in a rounded `Border` (matches `AppCornerRadius` used everywhere else instead of square TextBox chrome), added a magnifying-glass glyph and a clear (X) button shown only when there's text, plus a tooltip placeholder (`Main_SearchPlaceholder`, 14 languages).
- REQ-635-09 DONE (2026-10-06) — "Cumulative Benefit" removed as its own Home card; the same text (bytes-freed total + boot-time trend) now appends to the health-score issue list (`HomeView.xaml.cs`'s `RenderHealthIssues`/`GetImpactSummaryLines`) instead.
- REQ-635-10 DONE (2026-10-06) — Added a Boost button to `HomeView` next to "Full Health Check" (same function as `PcManagerHomeView`'s existing Boost: free background memory + clean only the `Recommended`-flagged quick-clean items) - previously only the PC-Manager skin had this button.
- REQ-635-11 PARTIAL (2026-10-06) — Update-manager scan button now shows "Installing"/`Opt_InstallingCaps` instead of "Scan" during an install (`AppUpdateScanButton.SetScanning` gained a `captionKey` param). Root cause of the msstore-popup-on-failure found and fixed: `WingetService.ParseWingetTable`/`ScanAsync` were discarding the real winget `Source` column and hardcoding `"winget"`, so genuinely msstore-sourced rows skipped the already-correct "open Store, don't fail" handling and instead ran a plain `winget upgrade --id` that winget itself can fail/pop the Store for. The Maintenance & Security Center light/dark part is covered by REQ-635-12's tab-style fix below (same root cause/fix).
- REQ-635-12 DONE (2026-10-06) — Three sub-items from one item: (a) ESET/Defender wording bug - `MyDeviceService` produced "Disabled (ESET Security)" when a third-party AV was active; now "Active via {0}" (`Sys_DefenderActiveViaFormat`, 14 languages) with `SecurityStatusService`'s severity level corrected from always-Info to Good/Warning. (b) `MaintenanceCenterWindow`'s `TabControl` had no `ItemContainerStyle`, rendering default white Windows tab chrome instead of the app's pill style - fixed with `PillTabItemStyle` + `Background="Transparent"`. (c) Pill button padding increased app-wide (`TabPillStyle` 11,8→18,9; `RailPillStyle` 14,10→18,10; `PillTabItemStyle` 14,8→18,9) - text was cramped against the pill edges on every tab/rail/secondary-window-tab in the app.
- REQ-635-13 DONE (2026-10-06, estimated) — `MainWindow.xaml`'s `MinWidth`/fallback `Width` lowered (1016→946, 1376→1306) to match the 9-tab strip (was hardcoded for the old 10-tab layout) - `FitWidthToTabs()` already recomputes the exact width at startup from the live measured strip; the old `MinWidth` was acting as a floor that would have silently kept the window at the old, now-too-wide size. Estimated reduction, not runtime-measured (no GUI capability this session) - worth a visual check.

## Notes

- REQ-570-13 and REQ-580-01 are worded almost identically to items already given for the MotionDeskStudio project (separate app, separate repo) — track them independently per project, don't assume one fix covers both.
- v5.7.0 shipped 2026-09-29 (REQ-570-01,02,03,04,05,07,08,09,11,12). REQ-570-06/10/13 remain, each blocked on a user decision (see PROJECT_STATE.md "Blocked"). v5.8.0 not started.
