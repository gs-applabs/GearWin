#!/usr/bin/env python3
"""GearWin optional Partclone adapter: used-block PARTITION backup, not bootable-disk cloning.

Runs an independently installed GPL Partclone executable as a separate process;
no upstream source or binaries are copied into GearWin. This script does not
restore partitions or repartition disks. Read README.md before attempting use.
"""
from __future__ import annotations
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import stat
import subprocess
import sys
import tempfile

SUPPORTED = {
    "ntfs": "partclone.ntfs", "ext2": "partclone.ext2", "ext3": "partclone.ext3",
    "ext4": "partclone.ext4", "vfat": "partclone.fat", "exfat": "partclone.exfat",
    "xfs": "partclone.xfs", "btrfs": "partclone.btrfs", "f2fs": "partclone.f2fs",
}


def flatten(blocks):
    for item in blocks:
        yield item
        yield from flatten(item.get("children") or [])


def choose_partition(data, device):
    matches = [x for x in flatten(data.get("blockdevices") or []) if x.get("path") == device]
    if len(matches) != 1 or matches[0].get("type") != "part":
        raise ValueError("Source must be one exact, existing physical partition.")
    item = matches[0]
    fs = str(item.get("fstype") or "").lower()
    if fs not in SUPPORTED:
        raise ValueError("Unsupported/unknown filesystem; do not fall back to raw copying silently.")
    if item.get("ro") not in (0, "0", False):
        raise ValueError("Read-only/unknown source state requires separate review.")
    if item.get("mountpoint") or any(x for x in (item.get("mountpoints") or [])):
        raise ValueError("Source partition must not be mounted.")
    if item.get("children"):
        raise ValueError("Partition is part of an active mapper/storage topology.")
    parent = str(item.get("pkname") or "").strip()
    if not parent:
        raise ValueError("Unable to identify the source physical disk.")
    if not parent.startswith("/dev/"):
        parent = "/dev/" + parent
    if parent.startswith("/dev/mapper/") or Path(parent).name.startswith(("dm-", "md")):
        raise ValueError("Mapper and RAID stacks are not supported.")
    return {**item, "pkname": parent}, SUPPORTED[fs]


def inventory():
    result = subprocess.run(
        ["lsblk", "--json", "--bytes", "-o",
         "PATH,TYPE,SIZE,FSTYPE,MOUNTPOINTS,PKNAME,RO,MAJ:MIN"],
        check=True, text=True, capture_output=True, timeout=25)
    return json.loads(result.stdout)


def destination_is_separate(source_parent, output_dir):
    """Only support a separately mounted, block-backed destination, fail closed."""
    result = subprocess.run(["findmnt", "--target", str(output_dir), "--noheadings", "--output", "SOURCE"],
                            check=True, capture_output=True, text=True, timeout=20)
    filesystem = result.stdout.strip().splitlines()
    if len(filesystem) != 1 or not filesystem[0].startswith("/dev/"):
        raise ValueError("Output must be on a separately mounted physical disk, not tmpfs/overlay/network.")
    mount_device = filesystem[0]
    if mount_device.startswith("/dev/mapper/") or Path(mount_device).name.startswith(("dm-", "md")):
        raise ValueError("Destination mapper/RAID topology is unsupported.")
    record = subprocess.run(["lsblk", "--nodeps", "--noheadings", "--output", "TYPE,PKNAME", mount_device],
                            check=True, capture_output=True, text=True, timeout=20)
    columns = record.stdout.strip().split()
    if len(columns) != 2 or columns[0] != "part":
        raise ValueError("Output must reside on a partition of another ordinary disk.")
    name = columns[1]
    target_disk = name if name.startswith("/dev/") else "/dev/" + name
    if target_disk == source_parent or target_disk.startswith("/dev/mapper/"):
        raise ValueError("Backup image destination is on the source disk or unsupported storage.")
    return mount_device


def sha256(filename):
    h = hashlib.sha256()
    with open(filename, "rb") as stream:
        for block in iter(lambda: stream.read(4 * 1024 * 1024), b""):
            h.update(block)
    return h.hexdigest()


def plan(device, output):
    if not sys.platform.startswith("linux"):
        raise ValueError("Run only in a Linux Live recovery environment.")
    if not os.path.isabs(output) or os.path.islink(output):
        raise ValueError("Output must be a new absolute, non-symlink image path.")
    image = Path(output)
    parent = image.parent
    if not parent.is_dir() or image.exists() or image.is_symlink() or Path(output + ".json").exists():
        raise ValueError("Choose a new filename with no existing image or manifest.")
    items = inventory()
    partition, tool = choose_partition(items, device)
    destination_is_separate(partition["pkname"], parent)
    return partition, tool


def backup(device, output):
    partition, tool = plan(device, output)
    if os.geteuid() != 0 or not sys.stdin.isatty() or not sys.stdout.isatty():
        raise PermissionError("Interactive root permission is required on offline Linux Live media.")
    from gearwin_clone import is_live_boot, sysfs_safety
    if not is_live_boot():
        raise PermissionError("Independently booted Live media is required.")
    mode = os.stat(device, follow_symlinks=False)
    if not stat.S_ISBLK(mode.st_mode):
        raise ValueError("Source must be a block device, not a symlink or regular file.")
    sysfs_safety(mode)
    program = shutil.which(tool)
    checker = shutil.which("partclone.chkimg")
    if not program or not checker:
        raise ValueError("Compatible Partclone backup and verification executables are not installed.")
    if input(f"Type SAVE {device} to create a used-block image: ") != f"SAVE {device}":
        raise PermissionError("Confirmation failed.")
    # Refresh device and mount facts immediately before writing the image.
    again, _ = plan(device, output)
    if (again.get("size"), again.get("maj:min"), again.get("fstype")) != (
            partition.get("size"), partition.get("maj:min"), partition.get("fstype")):
        raise ValueError("Partition identity changed before backup.")
    parent = Path(output).parent
    with tempfile.TemporaryDirectory(dir=parent, prefix=".gearwin-image-") as temp:
        temporary = Path(temp) / "partition.pcl"
        log = Path(temp) / "partclone.log"
        with open(log, "wb") as stdout:
            subprocess.run([program, "--clone", "--source", device, "--output", str(temporary)],
                           check=True, stdout=stdout, stderr=subprocess.STDOUT, timeout=12 * 3600)
            subprocess.run([checker, "--source", str(temporary)],
                           check=True, stdout=stdout, stderr=subprocess.STDOUT, timeout=12 * 3600)
        image_hash = sha256(temporary)
        manifest = {"schema": 1, "engine": tool, "partition": device,
                    "filesystem": partition["fstype"], "bytes": temporary.stat().st_size,
                    "sha256": image_hash, "verification": "partclone.chkimg"}
        manifest_temp = Path(temp) / "image.json"
        manifest_temp.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
        # Hard-link transfer is atomic and fails rather than replacing existing files.
        os.link(temporary, output, follow_symlinks=False)
        try:
            os.link(manifest_temp, output + ".json", follow_symlinks=False)
        except OSError:
            os.unlink(output)  # Do not leave an apparently complete image without its manifest.
            raise
        print(f"Verified partition image saved: {output} (SHA256 {image_hash}).")
        print("NOT a complete disk image: GPT/EFI/boot/recovery layout must be backed up separately.")


def verify_image(image):
    """Read-only integrity check of an image created by `backup`: manifest SHA-256 plus partclone.chkimg.
    Writes nothing and works on any machine that has the image and (for the second check) Partclone."""
    image = Path(image)
    manifest_path = Path(str(image) + ".json")
    if not image.is_file() or image.is_symlink() or not manifest_path.is_file():
        raise ValueError("Image and its .json manifest must both exist as regular files.")
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    if manifest.get("schema") != 1 or not isinstance(manifest.get("sha256"), str):
        raise ValueError("Unsupported or malformed manifest.")
    if image.stat().st_size != manifest.get("bytes"):
        raise ValueError("Image size differs from the manifest.")
    if sha256(image) != manifest["sha256"].lower():
        raise ValueError("Image SHA-256 differs from the manifest: the image is damaged or modified.")
    checker = shutil.which("partclone.chkimg")
    if checker:
        subprocess.run([checker, "--source", str(image)], check=True, capture_output=True, timeout=12 * 3600)
        return "sha256+partclone.chkimg"
    return "sha256-only (partclone.chkimg not installed)"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=("plan", "backup", "verify"))
    parser.add_argument("--device")
    parser.add_argument("--output", required=True)
    args = parser.parse_args()
    if args.action == "verify":
        print("Image verified:", verify_image(args.output))
        return
    if not args.device:
        parser.error("--device is required for plan and backup.")
    if args.action == "plan":
        partition, tool = plan(args.device, args.output)
        print(json.dumps({"device": partition["path"], "filesystem": partition["fstype"],
                          "size": partition["size"], "engine": tool,
                          "note": "Partition image only, not bootable whole-disk restore"}, indent=2))
    else:
        backup(args.device, args.output)


if __name__ == "__main__":
    try:
        main()
    except (OSError, ValueError, PermissionError, subprocess.SubprocessError) as err:
        print("Partclone adapter stopped:", err, file=sys.stderr)
        raise SystemExit(1)
