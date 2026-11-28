# GearWin offline clone (expert, raw disk-to-disk)

The Windows GUI can export this kit as a ZIP. The executable script is intended
to run from separately booted Linux Live media, not from the running Windows OS.
Required: Python 3.10+, lsblk, root privileges, boot=live or boot=casper.

Recommended for normal SSD upgrades: the official [Clonezilla Live guide](https://clonezilla.org/show-live-doc-content.php?topic=clonezilla-live/doc/03_Disk_to_disk_clone).
Clonezilla normally copies used filesystem data; this optional raw tool writes the
**entire source capacity** (including unused sectors) to the destination SSD.

Run from the Linux Live terminal:

    python3 gearwin_clone.py list
    python3 gearwin_clone.py plan --source /dev/nvme0n1 --target /dev/sdb
    sudo python3 gearwin_clone.py execute --source /dev/nvme0n1 --target /dev/sdb

Confirm source and target by model, serial and capacity before copying.
Unplug all unnecessary disks. The script prevents cloning from a mounted disk,
requires an offline Live boot and demands TWO independent typed confirmations.
**The entire destination disk is irreversibly destroyed.** Have independent backups
and BitLocker recovery keys. Both source and destination must be whole physical
disks, with target capacity equal or larger. This does not shrink partitions.

After SHA-256 verification completes, power off and disconnect the original
source disk before testing a boot from the clone. GPT partitions copied to a
larger disk may need a separate partition-table backup header relocation and
partition resize; no automatic expansion is implemented. Firmware, BitLocker,
Storage Spaces, defective sectors, hardware changes, and UEFI boot can complicate
real-world cloning.

Unit tests exercise virtual disk image files only. **Real hardware/UEFI boot
restoration has not been verified.** Do not use this on your only copy of data.
Windows image recovery should be performed in Windows Recovery Environment;
Microsoft documents wbadmin start sysrecovery as a Recovery Console command:
https://learn.microsoft.com/windows-server/administration/windows-commands/wbadmin-start-sysrecovery

Additional execution safeguards: require serial or WWN and kernel major/minor, recheck after both typed confirmations, claim both whole block devices exclusively, reject active kernel holders, verify actual device capacity/read-only state and keep descriptors open for the entire copy and SHA-256 readback. Missing identifiers or busy devices fail closed. These safeguards cannot prove all hardware aliases, hotplug races or offline conditions; do not bypass them. Verification covers the source-sized byte range, not any extra sectors on a larger target. A failed/interrupted copy may leave an unbootable target. No automatic retry or rollback is attempted.

Raw clone now also compares logical sector sizes from lsblk and native BLKSSZGET on the held descriptors. Unknown or different geometry is refused. Larger target trailing sectors remain unchanged; this is not a secure erase.
