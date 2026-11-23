# GearWin 7.0.0 — Comparative GitHub Research: Backup, Cloning, Recovery
**Date:** 2026-11-23  
**Target:** \`gs-applabs/GearWin\`, branch \`release/7.0.0-validation\` (research/audit only)  
**Review status:** Architecture guidance; **not** a claim that bare-metal recovery or source-to-target boot has passed real tests.

## Executive decision

**Do not develop another general-purpose disk filesystem parser or consider raw disk streaming a substitute for a production recovery engine.** Use separate, well-tested mechanisms for (a) online Windows image backup, (b) offline whole-disk imaging/cloning and WinRE recovery, (c) versioned data backups. GearWin should own the UI, preflight, workload/orchestration, manifests, verified downloads, and acceptance testing.

Recommended near-term: Windows \`wbadmin\` + WinRE image recovery with a supported offline Clonezilla Live/Rescuezilla workflow. Longer-term: an **independently booted** GearWin Recovery environment with a Partclone-based engine and separately audited licensing. Kopia/restic are optional **file-level** backup modules, not replacements for bare-metal OS imaging.

## Technical benchmarks (verified public sources)

| Project | GitHub / docs | What to learn | Limitation / licensing |
|---|---|---|---|
| **Clonezilla Live** | https://github.com/stevenshiau/clonezilla ; https://clonezilla.org/ | Disk/partition images, offline device-to-device clone, filesystem-aware used-block copy, optional fallback raw copy, image checking, network repositories | GPL-2.0; external Linux Live environment, UX is expert-oriented |
| **Partclone** | https://github.com/Thomas-Tsai/partclone ; https://partclone.org/ | Copy only in-use NTFS/FAT/ext/... filesystem blocks; narrower and faster SSD writes; restore supported partitions | GPL-2.0; filesystem support/health/sector-size must be validated, partition engine only, unmounted filesystem required |
| **Rescuezilla** | https://github.com/rescuezilla/rescuezilla ; https://rescuezilla.com/download | Beginner-oriented Live UI, backup/restore/clone, browsing disk images, interoperability with Clonezilla image formats | GPL-3.0; compatibility/feature changes require specific version testing before relying on interchangeability |
| **FOG Project** | https://github.com/FOGProject/fogproject ; https://docs.fogproject.org | PXE imaging, inventory/deployment management, complex UEFI boot flow, BitLocker and post-deploy boot fixes | GPL-3.0; network imaging infrastructure is overkill for ordinary single-PC users |
| **UrBackup** | https://github.com/uroni/urbackup_backend ; https://www.urbackup.org | Client/server scheduled file/image backups, Windows VSS, incremental generations, bootable restore media | AGPL-3.0; large service/server architecture, not a direct simple desktop disk cloner |
| **Kopia** | https://github.com/kopia/kopia ; https://kopia.io/docs/ | Data snapshots, deduplication, compression, encryption, retention, repository consistency checks, GUI/CLI | Apache-2.0; **file-level**, not whole-OS bare-metal imaging |
| **restic** | https://github.com/restic/restic ; https://restic.readthedocs.io/ | Encrypted/deduplicated file backup, snapshot/check/restore, Windows VSS snapshot option | BSD-2-Clause; **file-level**, not an OS image recovery engine |
| **Macrium Reflect** (design reference, not reusable code) | https://kbx.macrium.com/macrium-reflect-ltsc/cloning-options | Intelligent Sector Copy, filesystem verification, SSD trim, scheduling UX | Proprietary/commercial; **do not copy source or redistribute** |
| **Microsoft WinRE / wbadmin** | https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/wbadmin-start-backup ; https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/wbadmin-start-sysrecovery | Online backup with \`-allCritical\` and VSS; full restore in Windows Recovery Environment | A catalog query does not prove recoverability. \`start sysrecovery\` requires WinRE; test by actually restoring and booting |

Sources on licenses: https://clonezilla.org/ (GPLv2), https://github.com/Thomas-Tsai/partclone (GPLv2), https://github.com/rescuezilla/rescuezilla (GPLv3), https://github.com/FOGProject/fogproject (GPLv3), https://github.com/uroni/urbackup_backend (AGPLv3), https://github.com/kopia/kopia (Apache-2.0), https://github.com/restic/restic (BSD-2-Clause). A permissively licensed GearWin project **cannot simply paste GPL/AGPL code into its MIT source** without analysing the resulting distribution, derivative-work and reciprocal obligations. External program execution does not automatically settle all legal questions: obtain a license review, provide licenses/notices/source as required, and respect individual component terms.

## Findings in GearWin 7.0.0 source (confirmed by source inspection)

| Priority | File | Gap / risk | Proposed remedy and acceptance |
|---|---|---|---|
| P0 | \`tools/recovery/gearwin_clone.py\` | Raw clone copies **every byte of source capacity**, including free space. This may increase SSD writes substantially and always rejects a smaller target even where used space would fit | Prefer Partclone/Clonezilla used-block engine for supported unmounted filesystems; preserve expert raw mode only as a clearly disclosed fallback. Record logical and host writes; compare test scenarios |
| P0 | \`tools/recovery/gearwin_clone.py\` | \`lsblk\` mount/swap check alone does not establish that devices have no active LVM, MD RAID, dm-crypt, multipath, mounted host dependency, or open handles. Physical disk size and serial comparison may miss changes | Fail-closed for holders, complex device stacks, source and target \`major:minor\` aliases, geometry/logical sector mismatch, and suspicious/incomplete telemetry. Re-check immediately before opening destination; use robust device access/lock semantics |
| P0 | \`tools/recovery/gearwin_clone.py\` | No explicit logical/physical sector size comparison, GPT backup-header fixup, damaged-disk strategy or BitLocker preflight | Capture disk geometry, GPT/MBR, EFI/MSR/Recovery mapping, BitLocker status/recovery-key readiness, firmware compatibility. Block unsupported cases before destructive execution |
| P0 | \`tools/recovery/gearwin_clone.py\` | SHA-256 on cloned byte range verifies equal bytes at the time of reading; **not** proof that Windows starts, EFI boot entries exist, or data remain readable over time | Require a separate *boot with source disconnected* and recovery partition checks as release gates |
| P0 | \`wpf/OptimizerWpf/Services/BackupService.cs\` | Success + \`wbadmin get versions\` with nonempty output may be an older backup; not proof the last backup is restorable | Snapshot target catalog before/after and correlate new version timestamp with operation. Record VSS writer results/errors. Validate actual restore in disposable WinRE UEFI VM |
| P1 | \`wpf/OptimizerWpf/Services/BackupService.cs\` | \`wbadmin\` is launched without timeout/cancellation and stdout/stderr are read into RAM without an output limit | Add cancellation state, bounded streaming logs, progress, reasoned timeout policy for inventory vs backup, graceful termination semantics. Do not silently kill a live backup |
| P1 | \`wpf/OptimizerWpf/Services/BackupService.cs\` | Physical disk mapping is based on partitions and \`DiskNumber\`, but dynamic disks, Storage Spaces, virtual disks and complex topologies have different safety characteristics | Determine actual Storage Stack disk identifiers and underlying media; fail closed when full separation cannot be demonstrated |
| P1 | \`wpf/OptimizerWpf/Views/RecoveryCards.xaml.cs\` | UI exports a Python kit, but **does not create verified bootable Live USB/ISO**, and WinRE status is query-only | Build an official-source recovery media wizard (verify signed download/ISO digest); avoid auto-wiping USB without explicit multi-step confirmation |
| P1 | Documentation/tests | GitHub Actions Windows 2022/2025 setup smoke tests and Python temporary-file clones do not exercise full Windows 11 UEFI restore or SSD endurance | Add QEMU/KVM + OVMF + swtpm and a controlled Windows 11 evaluation guest with three disposable virtual disks; record artifact hashes, boot result and file-level comparisons |
| P2 | UX/backup retention | No unified snapshot catalog/retention for incremental data backups | Consider a separate Kopia/restic-backed file backup panel with explicit encryption, retention, prune, verification, alerting and storage quota; do not conflate with system imaging |

## Recommended GearWin architecture

1. **GearWin GUI on Windows (control plane)**: discover Windows volumes + underlying physical disks, flags for UEFI/GPT, EFI/MSR/Windows RE partitions, BitLocker and VSS, known backup destinations. Provide preflight with explicit source/destination identity cards and a reversible *read-only plan*.
2. **Windows image backup**: keep \`wbadmin -allCritical -vssCopy\` for native online Windows image capture where supported. Report progress and failures; verify a **new** catalog entry; retain diagnostics. An actual recovery requires separately booted WinRE, not a running Windows process.
3. **Offline image/clone engine**: primary path Partclone/Clonezilla on unmounted supported filesystems. Check filesystem health and disk geometry; only fall back to raw copying in explicitly supported, warned expert cases. Prefer a versioned, signed **independent recovery ISO** rather than running disk destruction from the Windows GUI.
4. **Recovery and boot**: restore partition table, EFI/MSR/recovery layout, verify filesystem and GPT metadata, validate boot entries, require isolated source-detached first boot. WinRE/BCDBoot or Linux UEFI repair path selected according to firmware and exact restore method.
5. **File backup option**: Kopia/restic for deduplicated, encrypted, incremental *user data* snapshots; do not claim a file snapshot can restore the entire operating system.
6. **Security and distribution**: keep third-party engine binaries/ISOs as verified external components with own notices and license compliance. Pin checksum/signing provenance, validate HTTPS + signatures, never execute untrusted downloads.

## Practical staged delivery

**Phase A — next safe build (P0):** harden fail-closed disk checks and clarify supported operation matrix; disable unsupported topologies. Add new-image catalog correlation, bounded process logs and recovery readiness report. Ensure UI does not advertise “native clone of live Windows.”

**Phase B — robust offline recovery:** wizard to obtain/verify official Clonezilla Live or Rescuezilla images; source/destination preview; rescue instructions; primary used-block engine; partition layout manifest with versioned schema; explicit warning for BitLocker, Storage Spaces and smaller destination. Audit license and USB image-creation flow.

**Phase C — end-to-end proof:** disposable Windows 11 evaluation VM UEFI/GPT with source system disk, separate image destination and clean replacement. Sequence: capture while Windows live → verify metadata → boot offline recovery → restore to blank disk → detach source → boot Windows, verify EFI, WinRE, user data/hashes; repeat direct clone. Test a failed restore/short write, storage disconnection, dirty NTFS, BitLocker recovery, 512e↔4Kn and larger target. Use self-hosted KVM/QEMU if necessary: GitHub states nested virtualization on GitHub-hosted runners is *not officially supported*: https://docs.github.com/en/actions/concepts/runners/github-hosted-runners. QEMU + OVMF + swtpm docs: https://www.qemu.org/docs/master/specs/tpm.html.

**Phase D — SSD efficiency:** capture GearWin-idle write traffic, backup-source VSS writes, backup-target image writes separately; evaluate used-block vs raw clone. Real SSD **host write counters** require appropriate physical hardware, not just an emulated disk.

## Release gates: no misleading “complete” labels

- All GUI installers pass existing WPF/migration and Windows 2022/2025 smoke tests.
- Every supported destructive operation has negative tests for same disk, aliases, mounted/active mappings, insufficient capacity, unsupported geometry and incomplete metadata.
- Build provenance, license compliance, signed/hashed external recovery media and repeatable unit/integration tests documented.
- **A cold restored Windows 11 UEFI guest must boot from the restored replacement disk with the original disconnected.** That is a different, mandatory check from “clone() returned SHA-256 match.”
- Genuine physical hardware and SSD endurance remain separately qualified; neither is proven by a VM run.

## Scope and licensing caveat

This is a technical design and security review, not legal advice. Refer to exact versions and upstream licenses before redistribution. Project URLs and documentation were checked 2026-11-23. Do not copy proprietary Macrium code. Do not bundle unknown prebuilt Linux ISO releases without supply-chain verification.

## Claude Code suggested initial review prompt

"Audit GearWin release/7.0.0-validation using docs/GEARWIN_BACKUP_CLONING_GITHUB_REFERENCE_AUDIT.md. Compare its offline raw clone and wbadmin workflows with the official Clonezilla/Partclone/Rescuezilla designs. Prioritize P0 unsafe disk-topology detection and a bootable UEFI Windows 11 restore acceptance test. Do not run destructive disk commands on any host system; use disposable isolated virtual disks only. Produce code changes with tests and a traceable SHA-256 release manifest."
