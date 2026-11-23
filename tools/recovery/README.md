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

## Optional used-block partition image — Partclone adapter (new)

The **partclone_adapter.py** module creates an image of one **unmounted,
supported filesystem partition**, using an independently installed Partclone
executable. It does not embed, modify, or fork Partclone. The code is a thin,
safety-checked external-command adapter, and Partclone itself has GPL licensing
which must be respected if distributed separately.

This differs from the raw whole-disk tool above: a Partclone image stores used
filesystem blocks only and generally makes substantially fewer SSD writes.
It does **not** include the GPT/MBR partition table, EFI System Partition,
Microsoft Reserved Partition, Windows Recovery partitions, or a UEFI boot
configuration unless each relevant partition is backed up and restored using
an additional, independently tested procedure. This utility currently **does
not provide destructive partition restore**, because safe, validated recovery
requires a complete disk layout and bootability workflow.

On separately booted Linux Live media with Python, Partclone, findmnt and lsblk:

```bash
python3 partclone_adapter.py plan --device /dev/nvme0n1p3 --output /media/backup/windows-ntfs.pcl
sudo python3 partclone_adapter.py backup --device /dev/nvme0n1p3 --output /media/backup/windows-ntfs.pcl
```

The adapter refuses mounted partitions, mapper/RAID topologies, unsupported
filesystems, output files that already exist, and an output filesystem on the
source disk. It runs `partclone.chkimg` to check the generated image and writes
a SHA-256 manifest in a separate JSON file. It will not start unattended.
It is not a substitute for a bootable disk image or Windows Recovery Environment.

**Licensing and distribution:** GearWin ships only its own adapter source and
links/instructions. It does not bundle Partclone or Clonezilla executables or
make any licensing claims on behalf of those upstream projects. Consult the
upstream projects before redistributing their binaries and licenses.

**Limitations:** Partclone compatibility varies by filesystem/version. This
workflow is currently tested with synthetic inventories and mocked commands,
not a restored real Windows partition. Always retain the original disk and
independent backups when testing a recovery scenario.
