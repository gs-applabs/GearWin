# Windows disk backup, recovery and cloning

## Back up Windows

1. Connect a backup volume on a separate physical disk. In GearWin, open the recovery card, select the volume and confirm the backup.
2. GearWin uses Windows Backup to capture the volumes Windows identifies as critical for system recovery. Keep a separate backup of files on other volumes you need to preserve.
3. With the backup volume still selected, view backup versions and confirm that Windows lists the newly created image on that volume. Record its date and source computer. Safely eject the backup disk when finished.

## Recover onto a replacement disk

Use Windows Recovery Environment and its system image recovery workflow. Boot from Windows installation or recovery media if the computer cannot reach recovery mode. Connect the image disk and the intended replacement disk, and disconnect other disks that are not needed for recovery. Select the recorded image and check the destination before confirming. Recovery may overwrite the destination. Keep the original disk disconnected for the first boot. Verify that Windows starts, expected partitions are present, and sample files open.

[Microsoft system recovery documentation](https://learn.microsoft.com/windows-server/administration/windows-commands/wbadmin-start-sysrecovery)

## Clone directly onto a second disk

Use the offline disk-to-disk workflow in Clonezilla Live. Keep a separate backup and have any BitLocker recovery keys available before starting. Power down Windows before booting the cloning media. Identify source and destination by model, capacity and serial number where available. Use a destination at least as large as the source. Every existing partition on the destination is subject to replacement. After cloning, shut down, disconnect the source, and boot from the destination alone. Check the Windows and recovery partitions and open sample files.

[Clonezilla Live disk-to-disk walkthrough](https://clonezilla.org/show-live-doc-content.php?topic=clonezilla-live/doc/03_Disk_to_disk_clone)

## Validation on Windows

Use a disposable UEFI Windows VM with a source disk, a distinct image disk and a blank replacement disk. Include a second volume on the source disk to check that GearWin rejects it as an image target. Confirm the selected image disk appears in the backup catalog and that invalid or disconnected targets surface an error. Restore the image to a blank replacement disk using recovery media and boot it with the source detached. Repeat with a fresh replacement disk for the direct clone. Record the Windows build, firmware mode, disk identities, result of each boot and sample file checks. Repeat on spare physical hardware for controller, firmware and driver coverage.

For SSD activity, collect Windows Process Monitor file-write events for GearWin and its child processes during 30 minutes of idle, an update scan and a backup. Record writes and bytes by path, with the image volume identified separately. Logical file writes and physical SSD host writes are different measurements. Use the SSD's host-write counter before and after each scenario when testing physical hardware.
