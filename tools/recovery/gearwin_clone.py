#!/usr/bin/env python3
"""GearWin expert offline raw-disk clone; NO filesystem-aware optimizations.

Raw mode writes all source bytes, including unused sectors. Normal SSD migrations
should use the independently installed Clonezilla/Partclone used-block engine.
Physical execution is only allowed in an interactive Linux Live boot, with root.
"""
import argparse
from dataclasses import dataclass
import fcntl
import hashlib
import json
import os
from pathlib import Path
import stat
import struct
import subprocess
import sys

BLOCK_SIZE = 4 * 1024 * 1024
BLKSSZGET = 0x1268
BLKPBSZGET = 0x127B
BLKGETSIZE64 = 0x80081272


@dataclass(frozen=True)
class Disk:
    path: str
    size: int
    serial: str
    model: str
    readonly: bool
    busy: bool
    logical_sector: int = 0
    physical_sector: int = 0
    majmin: str = ""
    layered: bool = False


def parse_disks(data):
    """Inspect all recursive descendants: lsblk may put dm/crypt under partitions."""
    def unsafe_tree(item):
        points = item.get("mountpoints") or []
        mounted = bool(item.get("mountpoint")) or any(bool(p) for p in points)
        swap = str(item.get("fstype") or "").lower() == "swap"
        return mounted or swap or any(unsafe_tree(child) for child in item.get("children") or [])

    def layered_tree(item):
        kind = str(item.get("type") or "").lower()
        fstype = str(item.get("fstype") or "").lower()
        unsafe = kind not in {"disk", "part"} or fstype in {
            "linux_raid_member", "lvm2_member", "zfs_member", "crypto_luks", "linux_raid"
        }
        return unsafe or any(layered_tree(child) for child in item.get("children") or [])

    result = {}
    for item in data.get("blockdevices") or []:
        if item.get("type") != "disk" or not str(item.get("path") or "").startswith("/dev/"):
            continue
        path = str(item["path"])
        result[path] = Disk(
            path, int(item.get("size") or 0), str(item.get("serial") or "").strip(),
            str(item.get("model") or "").strip(), bool(int(item.get("ro") or 0)),
            unsafe_tree(item), int(item.get("log-sec") or 0),
            int(item.get("phy-sec") or 0), str(item.get("maj:min") or ""),
            layered_tree(item))
    return result


def list_disks():
    result = subprocess.run(
        ["lsblk", "-J", "-b", "-o",
         "PATH,TYPE,SIZE,SERIAL,MODEL,RO,MOUNTPOINTS,FSTYPE,LOG-SEC,PHY-SEC,MAJ:MIN"],
        check=True, capture_output=True, text=True, timeout=30)
    return parse_disks(json.loads(result.stdout))


def inspect(disks, source, target):
    if source not in disks or target not in disks:
        raise ValueError("Choose two whole physical disks listed by 'list'.")
    a, b = disks[source], disks[target]
    if os.path.realpath(a.path) == os.path.realpath(b.path):
        raise ValueError("Source and destination refer to the same device.")
    if a.majmin and b.majmin and a.majmin == b.majmin:
        raise ValueError("Source and destination have the same kernel device identity.")
    if a.size <= 0 or b.size < a.size:
        raise ValueError("Target capacity is smaller than source capacity.")
    if a.busy or b.busy:
        raise ValueError("Source/target has a mounted partition or active swap.")
    if a.layered or b.layered:
        raise ValueError("Software RAID, LVM, device-mapper or encrypted layered volumes require a specialized workflow.")
    if b.readonly:
        raise ValueError("Target disk is read-only.")
    if any(os.path.basename(p).startswith(("loop", "ram", "zram", "sr", "dm-", "md"))
           for p in (a.path, b.path)):
        raise ValueError("Virtual, RAID, optical or mapper devices are not supported by raw mode.")
    if a.logical_sector and b.logical_sector and a.logical_sector != b.logical_sector:
        raise ValueError("Logical sector sizes differ: raw cloning is not supported.")
    return a, b


def is_live_boot():
    try:
        args = Path("/proc/cmdline").read_text(encoding="utf-8").split()
        return "boot=live" in args or "boot=casper" in args or Path("/run/live/medium").is_dir()
    except OSError:
        return False


def sysfs_safety(device, sysfs_root="/sys/dev/block"):
    """Refuse active holders and hidden partitions whose mount/swap status is unknown.

    On Linux, holders represent active dm-crypt/LVM/md/etc references. An unreadable
    sysfs graph must stop the destructive workflow, never count as 'safe'.
    """
    major_minor = f"{os.major(device.st_rdev)}:{os.minor(device.st_rdev)}"
    node = (Path(sysfs_root) / major_minor).resolve(strict=True)
    if not (node / "dev").is_file():
        raise ValueError("Cannot inspect block-device sysfs topology.")
    inspected = [node]
    for child in node.iterdir():
        if child.is_dir() and (child / "partition").exists():
            inspected.append(child)
    for current in inspected:
        holders = current / "holders"
        if not holders.is_dir():
            raise ValueError("Missing sysfs holders metadata; cannot prove disk is inactive.")
        if any(holders.iterdir()):
            raise ValueError(f"Disk has active kernel holders ({current.name}); refusing clone.")
    return major_minor


def get_geometry(fd):
    """Kernel-reported capacity and sector sizes from the open device, not stale lsblk."""
    size = struct.unpack("Q", fcntl.ioctl(fd, BLKGETSIZE64, bytes(8)))[0]
    logical = struct.unpack("I", fcntl.ioctl(fd, BLKSSZGET, bytes(4)))[0]
    physical = struct.unpack("I", fcntl.ioctl(fd, BLKPBSZGET, bytes(4)))[0]
    if not size or logical not in (512, 1024, 2048, 4096) or physical < logical:
        raise ValueError("Disk geometry is unavailable or unsupported.")
    return size, logical, physical


def copy_bytes(src, dest, size, chunk_size=BLOCK_SIZE):
    remaining = size
    while remaining:
        block = src.read(min(remaining, chunk_size))
        if not block:
            raise OSError("Unexpected end of source; clone not valid.")
        view = memoryview(block)
        while view:
            n = dest.write(view)
            if n is None or n <= 0:
                raise OSError("Destination write error; clone not valid.")
            view = view[n:]
        remaining -= len(block)
    dest.flush()
    os.fsync(dest.fileno())


def digest(filename, size):
    sha = hashlib.sha256()
    with open(filename, "rb", buffering=0) as stream:
        remaining = size
        while remaining:
            block = stream.read(min(remaining, BLOCK_SIZE))
            if not block:
                raise OSError("Unexpected end during SHA-256 verification.")
            sha.update(block)
            remaining -= len(block)
    return sha.hexdigest()


def clone(src, dst, size, *, exclusive=False, expected=None):
    """Raw byte copy for temporary-file tests or strongly gated offline block mode.

    O_EXCL prevents opening mounted/busy block devices on Linux. Re-stat / re-check
    each opened fd. It does not replace testing the bootability of cloned Windows.
    """
    flags = (os.O_EXCL | os.O_NOFOLLOW) if exclusive else 0
    reader = os.open(src, os.O_RDONLY | flags | os.O_CLOEXEC)
    try:
        writer = os.open(dst, os.O_WRONLY | flags | os.O_SYNC | os.O_CLOEXEC)
        try:
            if exclusive:
                a, b = os.fstat(reader), os.fstat(writer)
                if not stat.S_ISBLK(a.st_mode) or not stat.S_ISBLK(b.st_mode):
                    raise ValueError("Not actual Linux block devices.")
                if a.st_rdev == b.st_rdev:
                    raise ValueError("Both file descriptors refer to the same disk.")
                source_geom, target_geom = get_geometry(reader), get_geometry(writer)
                if source_geom[0] != size or target_geom[0] < size:
                    raise ValueError("Device geometry changed while opening disks.")
                if source_geom[1] != target_geom[1]:
                    raise ValueError("Source/destination logical sector sizes differ.")
                if expected and (source_geom != expected[0] or target_geom != expected[1]):
                    raise ValueError("Disk geometry changed after confirmation.")
                sysfs_safety(a)
                sysfs_safety(b)
            with os.fdopen(reader, "rb", buffering=0) as inp:
                reader = -1
                with os.fdopen(writer, "wb", buffering=0) as out:
                    writer = -1
                    copy_bytes(inp, out, size)
        finally:
            if writer != -1:
                os.close(writer)
    finally:
        if reader != -1:
            os.close(reader)
    return digest(src, size) == digest(dst, size)


def describe(d):
    return (f"{d.path} | {d.size:,} bytes | model={d.model or '?'} | "
            f"serial={d.serial or '?'} | major:minor={d.majmin or '?'} | "
            f"logical={d.logical_sector or '?'} | physical={d.physical_sector or '?'} | "
            f"busy={d.busy} | layered={d.layered}")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=("list", "plan", "execute"))
    parser.add_argument("--source")
    parser.add_argument("--target")
    args = parser.parse_args()
    if not sys.platform.startswith("linux"):
        parser.error("Boot into independent Linux Live media first.")
    disks = list_disks()
    if args.action == "list":
        for disk in disks.values():
            print(describe(disk))
        return 0
    if not args.source or not args.target:
        parser.error("Select both --source and --target.")
    src, dst = inspect(disks, args.source, args.target)
    print("SOURCE:     ", describe(src))
    print("DESTINATION:", describe(dst))
    print("RAW MODE writes the ENTIRE source disk capacity, including unused SSD sectors.")
    print("ALL DATA ON THE DESTINATION DISK WILL BE DESTROYED.")
    print("Use filesystem-aware Clonezilla/Partclone for ordinary SSD migrations.")
    if args.action == "plan":
        print("Read-only preflight complete. No data was written.")
        return 0
    if os.geteuid() != 0 or not is_live_boot():
        raise PermissionError("Destructive action requires root in independent Linux Live media.")
    if not sys.stdin.isatty() or not sys.stdout.isatty():
        raise PermissionError("Destructive action is forbidden without an interactive terminal.")
    for disk in (src, dst):
        status = os.stat(disk.path, follow_symlinks=False)
        if not stat.S_ISBLK(status.st_mode):
            raise ValueError("Only whole block devices (not symlink aliases/files) may be cloned.")
        identity = sysfs_safety(status)
        if disk.majmin and disk.majmin != identity:
            raise ValueError("Disk identity changed since lsblk discovery.")
    latest = list_disks()
    updated_src, updated_dst = inspect(latest, src.path, dst.path)
    if (updated_src.size, updated_src.serial, updated_src.majmin,
            updated_dst.size, updated_dst.serial, updated_dst.majmin) != (
            src.size, src.serial, src.majmin, dst.size, dst.serial, dst.majmin):
        raise ValueError("Disk identity changed immediately before execution.")
    # Read-only geometry probe; O_EXCL fails if other kernel users hold a block device.
    descriptors = []
    try:
        for path in (src.path, dst.path):
            descriptors.append(os.open(path, os.O_RDONLY | os.O_EXCL | os.O_NOFOLLOW | os.O_CLOEXEC))
        geometry = tuple(get_geometry(fd) for fd in descriptors)
    finally:
        for fd in descriptors:
            os.close(fd)
    if geometry[0][0] != src.size or geometry[1][0] != dst.size or geometry[0][1] != geometry[1][1]:
        raise ValueError("Disks changed or have incompatible logical sectors.")
    if input(f"Type exact TARGET path {dst.path}: ").strip() != dst.path:
        raise PermissionError("Target confirmation did not match.")
    if input(f"Type ERASE {dst.path} to start: ").strip() != f"ERASE {dst.path}":
        raise PermissionError("Erasure confirmation did not match.")
    print("Copying; do not disconnect power or either disk. A verified clone is NOT a boot test.")
    if not clone(src.path, dst.path, src.size, exclusive=True, expected=geometry):
        raise OSError("Source/target SHA-256 mismatch; clone FAILED.")
    print("SHA-256 match confirmed. Shut down and detach the original source before first boot.")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (OSError, ValueError, PermissionError, subprocess.SubprocessError) as exc:
        print("GearWin clone stopped:", exc, file=sys.stderr)
        sys.exit(1)
