# GearWin 7.0.0 — Claude Code Handoff / Τεχνική Παράδοση

**Generated:** 2026-11-23  
**Repository:** https://github.com/gs-applabs/GearWin  
**Development branch:** `release/7.0.0-validation`  
**Target version:** 7.0.0 (candidate — NOT a published GitHub Release as of this handoff)  
**Previous published version:** `v6.9.11`  
**Source of truth:** GitHub commit SHA in `PACKAGE_PROVENANCE.txt` and the SHA-256 files bundled with the run artifacts.  
**Intended reviewer:** Claude Code. Review code and docs in the source ZIP; do not substitute stale main/master or PR #2 files.

## START HERE

1. Read `CLAUDE.md`, `README.md`, `PROJECT_STATE.md`, `ROADMAP.md`, `HANDOFF.md`, `CHANGELOG.md`.
2. Read `docs/disk-backup-and-recovery.md`, `migration/README.md`, and this handoff.
3. Inspect `wpf/OptimizerWpf/TabLayout.cs`, `Views/RecoveryCards.xaml(.cs)`, `Services/BackupService.cs`, `Services/TransferPreflight.cs`, `migration/GearWin.Migration.Core/`, and `migration/GearWin.Migration.Cli/`.
4. Compare `release/7.0.0-validation` to `master` and review draft PR #2 separately; the PR remains independent and is **not merged**.
5. Verify all CI claims against GitHub Actions logs and actual binary hashes, not only changelog text.

## Implementation status / Υφιστάμενη κατάσταση

- Windows GUI: WPF, `.NET 10`, Windows 11 x64; installed as `GearWin.exe` with administrative privileges.
- Setup: Inno Setup 7, `installer/OptimizerWpf.iss`; self-contained `win-x64` publish, including migration CLI in a `migration` subdirectory.
- GUI placement: **Σύστημα & Εργαλεία → Εργαλεία**, immediately below **Αντίγραφο ασφαλείας συστήματος**. The recovery section contains restore points, Windows image backup, and the new pre-transfer/inventory card. Section mapping maintained in `TabLayout.cs`.
- Windows image backup: `wbadmin start backup -allCritical` with selected target volume; preflight rejects a destination volume on the same physical disk as critical boot/system partitions. Version listing uses `wbadmin get versions`.
- Disk clone: **NOT an implemented native disk-clone engine.** GUI links to the official Clonezilla Live offline disk-to-disk guide. Recovery GUI is a guide to Windows RE, not a tested in-app bare-metal restore.
- New folder transfer preflight: read-only, bounded at 100,000 filesystem entries, rejects overlapping directories, symbolic-link/junction roots and traversals, checks destination volume free space. The preflight does **not** copy files, check source-file contents, test destination write permission, preserve ACLs, compare hashes or verify post-copy integrity.
- New application inventory export: JSON produced by `ApplicationDiscovery.InspectHost()`. Windows reads uninstall registrations from HKCU/HKLM, both views. **Not exhaustive:** MSIX/Store apps without uninstall registrations, portable apps, CLI-only packages may be absent.
- Migration CLI: `inspect`, `inventory`, `plan` commands for Windows/Linux; package plan is **read-only** and intended for review, not automatic migration or installation. Exact verifier availability differs by package manager.
- Existing fallback help is English for some new UI strings when language is not Greek; assess coverage for the complete set of 14 languages.
- Program continuously runs existing maintenance features; no new background folder/SSD scanning or write loop was added by these changes.

## Validation and explicit unverified features

GitHub Actions workflow: `.github/workflows/installer-e2e.yml`.

Required success criteria for this commit's own workflow run:
- `dotnet test wpf/OptimizerWpf.Tests -c Release` (including `TransferPreflightTests`).
- `dotnet test migration/GearWin.Migration.Tests -c Release`.
- Self-contained WPF and CLI publish; Inno Setup 7 installer compilation.
- Windows Server 2022: fresh install, upgrade from published latest, in-app upgrade.
- Windows Server 2025: fresh install.
- Installer artifact `setup` (the `GearWin-Setup.exe` plus SHA-256), source artifact `gearwin-source-7.0.0`.
- See run URL shown in the package manifest; validate exact SHA. Do not claim success for newer runs without checking.

**Not tested / not proven:** real UAC and SmartScreen UX; real GPU/multi-monitor/DPI; Windows image restore and reboot on replacement disk; BitLocker recovery handling; boot after Clonezilla clone; true SSD host write amplification/wear; full GUI end-to-end clicks for each new workflow on a user's hardware.

## Priority security/correctness review for Claude Code

1. Review `BackupService.IsSeparateBackupDisk` on dynamic disks, Storage Spaces, USB disks, BitLocker and offline/removable media; ensure the source/target safety rule fails closed.
2. Check `wbadmin` availability and edition support on Windows 11; capture stdout/stderr and protect against indefinite jobs, hangs, and cancellation semantics.
3. Inspect UI async exception handling and dispatcher-affinity, including language-switch and control unload/reload lifecycle. New language event handler dispatches onto owning WPF dispatcher to prevent cross-thread test failures.
4. Strengthen `TransferPreflight` junction/symlink checks; consider parent-directory reparse points, network UNC paths and changing directory contents (TOCTOU) while retaining fail-closed semantics.
5. Assess Windows inventory keys colliding across registry hives/views (currently deduplicated by key ID), missing app types, and sensitive export handling.
6. Audit all elevated child process invocation paths and trusted executable resolution, registry writes, installer upgrades and cleanup/rollback.
7. Benchmark idle disk writes using Process Monitor, Event Tracing and physical SMART/NVMe host-write counters on representative hardware; separate source-image backup output from GearWin's own metadata/log writes.
8. Audit localization and text clipping in all 14 languages; ensure no new card inaccessible to screen readers.
9. Do not present the backup + manual Clonezilla link as full native disk cloning. A real cloning implementation requires a distinct design, destructive-action safeguards, privilege and volume-lock management, offline/recovery environment and bare-metal test matrix.
10. Check unchanged packaged binaries against the committed files, CI and SHA-256 before approving a public GitHub Release.

## Build instructions / Αναπαραγωγή

From repo root (Windows 11 x64, .NET 10 SDK, Inno Setup 7 installed):

```powershell
dotnet test wpf/OptimizerWpf.Tests -c Release
dotnet test migration/GearWin.Migration.Tests -c Release
./tools/ci-install-inno.ps1
./tools/build-setup-ci.ps1
```

The standalone Windows build script creates `installer/Output/GearWin-Setup-7.0.0.exe` and a CI copy `GearWin-Setup.exe`. On GitHub runners, download the `setup` artifact for the exact run. PowerShell `Get-FileHash -Algorithm SHA256` should match `GearWin-Setup.exe.sha256`.

```powershell
Get-FileHash .\GearWin-Setup.exe -Algorithm SHA256
```

**Never execute destructive backup/clone/restore tests on personal disks.** Use a disposable UEFI Windows VM with a source disk, separate image destination, and blank replacement disk as described in `docs/disk-backup-and-recovery.md`.

## Package contents

- `GearWin-7.0.0-source.zip`: all files tracked at the exact Git commit including all `.md` files, .NET projects, WPF resources, installer assets/scripts, unit tests, CI workflows.
- `GearWin-7.0.0-markdown.zip`: copies of all tracked Markdown files, preserving the repo's relative paths; also included inside the source ZIP.
- `GearWin-7.0.0-Installer.zip`: independent artifact with `GearWin-Setup.exe` and SHA-256.
- `PACKAGE_PROVENANCE.txt`, `SOURCE_SHA256SUMS.txt`: traceability for the source/Markdown ZIPs; installer checksum accompanies the installer.
- The GitHub Actions downloaded artifact may itself be a ZIP containing the above ZIPs. Extract the outer artifact first.

## Release decision

The CI-tested candidate can be installed for evaluation. **Do not declare disk cloning or actual Windows image restore complete** without disposable VM/physical validation. Public GitHub Release publication, merge to `master`, and closure of draft PR #2 are separate release-management actions, not implied by the build.

## Offline clone status addendum

The 7.0.0 branch now ships `tools/recovery/gearwin_clone.py` (expert raw disk
cloning from Linux Live, two confirmations, full raw-byte SHA-256 verification).
The GUI can export a ZIP and inspect WinRE via `reagentc /info`.
`tools/recovery/tests/test_clone.py` exercises only temporary synthetic disk
files and safety-policy inputs. `tools/build-setup-ci.ps1` asserts these
recovery assets land in the published WPF app; `tools/e2e-installer.ps1`
asserts they are installed under Program Files.

Real device clone, system-image recovery from WinRE, UEFI replacement-disk
boot, BitLocker/Storage Spaces and hardware SSD-write measurement are
**still unsupported as test claims**. Do not label the feature production
validated until separately tested on an isolated disposable UEFI VM/hardware.
