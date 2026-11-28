#!/usr/bin/env python3
"""GearWin expert raw disk clone. Run ONLY from an offline Linux Live environment.

Caution: the entire source capacity is written, even unused sectors.
For routine SSD migration prefer Clonezilla's filesystem-aware workflow.
"""
import argparse
from dataclasses import dataclass
import hashlib
import fcntl
import struct
from contextlib import ExitStack
import json
import os
from pathlib import Path
import stat
import subprocess
import sys

BLOCK_SIZE = 4 * 1024 * 1024

@dataclass(frozen=True)
class Disk:
    path: str
    size: int
    serial: str
    model: str
    readonly: bool
    busy: bool
    major_minor: str = ""
    wwn: str = ""
    logical_sector: int = 0

def parse_disks(data):
    def busy(item):
        points = item.get("mountpoints") or []
        return (bool(item.get("mountpoint"))
                or any(bool(p) for p in points)
                or str(item.get("fstype") or "").lower() == "swap"
                or any(busy(child) for child in item.get("children") or []))
    result = {}
    for obj in data.get("blockdevices", []):
        if obj.get("type") != "disk" or not str(obj.get("path") or "").startswith("/dev/"):
            continue
        path = str(obj["path"])
        result[path] = Disk(path, int(obj.get("size") or 0),
                            str(obj.get("serial") or "").strip(),
                            str(obj.get("model") or "").strip(),
                            bool(int(obj.get("ro") or 0)), busy(obj),
                            str(obj.get("maj:min") or ""), str(obj.get("wwn") or "").strip(),
                            int(obj.get("log-sec") or 0))
    return result

def list_disks():
    tool = next((p for p in ("/usr/bin/lsblk", "/bin/lsblk", "/usr/sbin/lsblk", "/sbin/lsblk") if os.path.isfile(p)), None)
    if not tool:
        raise ValueError("lsblk unavailable in system directories.")
    result = subprocess.run(
        [tool, "-J", "-b", "-o", "NAME,PATH,TYPE,SIZE,SERIAL,MODEL,RO,MOUNTPOINTS,FSTYPE,MAJ:MIN,WWN,LOG-SEC"],
        check=True, capture_output=True, text=True, timeout=30)
    return parse_disks(json.loads(result.stdout))

def inspect(disks, source, target):
    if source not in disks or target not in disks:
        raise ValueError("Select two different whole disks from the list.")
    a, b = disks[source], disks[target]
    if os.path.realpath(a.path) == os.path.realpath(b.path):
        raise ValueError("The source and target refer to the same device.")
    if a.size <= 0 or b.size < a.size:
        raise ValueError("Target disk is smaller than source disk.")
    if a.busy or b.busy:
        raise ValueError("Source/target has mounted partitions or active swap.")
    if b.readonly:
        raise ValueError("Target disk is read-only.")
    if any(os.path.basename(p).startswith(("loop", "ram", "zram", "sr")) for p in (a.path, b.path)):
        raise ValueError("Virtual/optical devices are not supported.")
    if a.logical_sector and b.logical_sector and a.logical_sector != b.logical_sector:
        raise ValueError("Logical sector sizes differ; raw clone cannot safely preserve partition layout.")
    return a, b

def is_live_boot():
    try:
        args = Path("/proc/cmdline").read_text(encoding="utf-8").split()
        return "boot=live" in args or "boot=casper" in args or Path("/run/live/medium").exists()
    except OSError:
        return False

def copy_bytes(src, dest, size, chunk_size=BLOCK_SIZE):
    """Sequential bounded-memory copy, used by production and virtual-image tests."""
    if size <= 0 or chunk_size <= 0 or chunk_size > BLOCK_SIZE:
        raise ValueError("Invalid copy size.")
    sha = hashlib.sha256()
    left = size
    while left:
        block = src.read(min(left, chunk_size))
        if not block:
            raise OSError("Unexpected end of source.")
        sha.update(block)
        view = memoryview(block)
        while len(view):
            amount = dest.write(view)
            if amount is None or amount <= 0 or amount > len(view):
                raise OSError("Destination write failed.")
            view = view[amount:]
        left -= len(block)
    dest.flush()
    os.fsync(dest.fileno())
    return sha.hexdigest()

def digest(filename, size):
    sha = hashlib.sha256()
    with open(filename, "rb", buffering=0) as stream:
        left = size
        while left:
            block = stream.read(min(left, BLOCK_SIZE))
            if not block:
                raise OSError("Unexpected end during verification.")
            sha.update(block)
            left -= len(block)
    return sha.hexdigest()

def digest_stream(stream, size):
    sha = hashlib.sha256()
    stream.seek(0)
    left = size
    while left:
        block = stream.read(min(left, BLOCK_SIZE))
        if not block:
            raise OSError("Unexpected end during verification.")
        sha.update(block)
        left -= len(block)
    return sha.hexdigest()


def clone(src, dst, size):
    """Regular-file fixture only. Production uses clone_block_devices."""
    with open(src, "rb", buffering=0) as inp, open(dst, "r+b", buffering=0) as out:
        first, second = os.fstat(inp.fileno()), os.fstat(out.fileno())
        if not stat.S_ISREG(first.st_mode) or not stat.S_ISREG(second.st_mode):
            raise ValueError("Fixture copy accepts regular files only.")
        if (first.st_dev, first.st_ino) == (second.st_dev, second.st_ino):
            raise ValueError("Fixture files refer to the same inode.")
        if size <= 0 or first.st_size < size or second.st_size < size:
            raise OSError("Fixture capacity is insufficient.")
        copied = copy_bytes(inp, out, size)
        return copied == digest_stream(out, size) == digest_stream(inp, size)


def identity(disk):
    # A stable hardware identifier plus kernel device identity is mandatory.
    if not disk.major_minor or not (disk.serial or disk.wwn):
        raise ValueError("Disk identity is incomplete; use a different verified recovery method.")
    return (disk.path, disk.size, disk.serial, disk.wwn, disk.model, disk.major_minor, disk.logical_sector)


def confirm_identity(before, after):
    if identity(before) != identity(after):
        raise ValueError("Disk identity changed; no copy started.")


def get_geometry(fd):
    size = struct.unpack("=Q", fcntl.ioctl(fd, 0x80081272, bytes(8)))[0]  # BLKGETSIZE64
    sector = struct.unpack("=I", fcntl.ioctl(fd, 0x1268, bytes(4)))[0]  # BLKSSZGET
    if size <= 0 or sector < 512 or sector > 65536 or sector & (sector - 1) or size % sector:
        raise ValueError("Unsupported or inconsistent block geometry.")
    return size, sector


def validate_geometry(source, target, expected_size):
    size, sector = source
    capacity, target_sector = target
    if expected_size != size or capacity < size or sector != target_sector:
        raise ValueError("Source/target capacity or logical sector size is incompatible.")


def sysfs_safety(info, partition=False):
    actual = f"{os.major(info.st_rdev)}:{os.minor(info.st_rdev)}"
    base = Path("/sys/dev/block") / actual
    if not base.exists() or (base / "partition").exists() != partition:
        raise ValueError("Unexpected block device topology.")
    # Check partition plus its parent, or whole disk plus its partitions.
    root = base.resolve(strict=True).parent if partition else base
    if not (root / "dev").is_file() or (root / "partition").exists():
        raise ValueError("Unable to identify a whole parent disk.")
    for node in [root, *(p for p in root.iterdir() if (p / "partition").exists())]:
        holders = node / "holders"
        if not holders.is_dir() or any(holders.iterdir()):
            raise ValueError("Disk or partition has active or unknown kernel holders.")
    return actual


def validate_open_device(fd, disk, target=False):
    info = os.fstat(fd)
    if not stat.S_ISBLK(info.st_mode):
        raise ValueError("Opened object is not a block device.")
    actual = f"{os.major(info.st_rdev)}:{os.minor(info.st_rdev)}"
    if actual != disk.major_minor:
        raise ValueError("Opened kernel device differs from confirmed disk.")
    sysfs_safety(info)
    size, sector = get_geometry(fd)
    if size != disk.size or sector != disk.logical_sector:
        raise ValueError("Opened device geometry differs from confirmed disk.")
    if target and struct.unpack("=i", fcntl.ioctl(fd, 0x125e, bytes(4)))[0]:  # BLKROGET
        raise ValueError("Opened target is read-only.")
    return info.st_rdev


def clone_block_devices(src, dst):
    # Linux block-device O_EXCL rejects claimed/mounted devices; keep both
    # descriptors through copying and verification, never reopen by pathname.
    flags = os.O_EXCL | os.O_CLOEXEC | os.O_NOFOLLOW
    with ExitStack() as stack:
        reader = os.open(src.path, os.O_RDONLY | flags)
        stack.callback(os.close, reader)
        source_id = validate_open_device(reader, src)
        writer = os.open(dst.path, os.O_RDWR | flags | os.O_SYNC)
        stack.callback(os.close, writer)
        if validate_open_device(writer, dst, target=True) == source_id:
            raise ValueError("Source and destination are the same kernel device.")
        # Re-read inventory after both claims and before the first write.
        a, b = inspect(list_disks(), src.path, dst.path)
        confirm_identity(src, a)
        confirm_identity(dst, b)
        validate_geometry(get_geometry(reader), get_geometry(writer), src.size)
        return copy_verified_handles(reader, writer, src.size)


def copy_verified_handles(reader, writer, size):
    """Use only previously claimed and checked handles; never opens a device by name."""
    with os.fdopen(reader, "rb", buffering=0, closefd=False) as inp, \
            os.fdopen(writer, "r+b", buffering=0, closefd=False) as out:
        inp.seek(0)
        out.seek(0)
        copied = copy_bytes(inp, out, size)
        return copied == digest_stream(out, size) == digest_stream(inp, size)


def describe(d):
    return (f"{d.path} | {d.size:,} bytes | model={d.model or '?'} | "
            f"serial={d.serial or '?'} | WWN={d.wwn or '?'} | device={d.major_minor or '?'} | sector={d.logical_sector or '?'} | busy={d.busy} | read-only={d.readonly}")

def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("action", choices=("list", "plan", "execute"))
    p.add_argument("--source")
    p.add_argument("--target")
    args = p.parse_args()
    if not sys.platform.startswith("linux"):
        p.error("Boot into Linux Live media first.")
    disks = list_disks()
    if args.action == "list":
        for disk in disks.values():
            print(describe(disk))
        return 0
    if not args.source or not args.target:
        p.error("Choose both --source and --target.")
    src, dst = inspect(disks, args.source, args.target)
    print("SOURCE:", describe(src))
    print("TARGET:", describe(dst))
    print("WARNING: RAW sector copying WRITES the entire source capacity to the target.")
    print("Existing target partitions and data within the source-sized range WILL BE overwritten.")
    print("Extra sectors on a larger target are not erased.")
    print("For most SSDs prefer Clonezilla's filesystem-aware disk-to-disk cloning.")
    if args.action == "plan":
        print("Plan complete; no data has been written.")
        return 0
    if os.geteuid() != 0 or not is_live_boot():
        raise PermissionError("Destructive cloning requires root in an offline Linux Live boot.")
    if not sys.stdin.isatty() or not sys.stdout.isatty():
        raise PermissionError("Noninteractive destructive cloning is forbidden.")
    for disk in (src, dst):
        if not stat.S_ISBLK(os.stat(disk.path).st_mode):
            raise ValueError("Only physical whole-disk block devices can be cloned.")
    identity(src)
    identity(dst)
    if not src.logical_sector or not dst.logical_sector:
        raise ValueError("Logical sector sizes must be known before execution.")
    validate_geometry((src.size, src.logical_sector), (dst.size, dst.logical_sector), src.size)
    if src.major_minor == dst.major_minor or (src.wwn and src.wwn == dst.wwn):
        raise ValueError("Disk identities are not distinct.")
    if input(f"Type EXACT target path {dst.path}: ").strip() != dst.path:
        raise PermissionError("Target path confirmation failed.")
    if input(f"Type ERASE {dst.path} to overwrite it: ").strip() != f"ERASE {dst.path}":
        raise PermissionError("Second destructive-action confirmation failed.")
    print("Copying and SHA-256 verifying all source bytes. Never disconnect power.")
    latest_src, latest_dst = inspect(list_disks(), src.path, dst.path)
    confirm_identity(src, latest_src)
    confirm_identity(dst, latest_dst)
    if not clone_block_devices(src, dst):
        raise OSError("Cloned data verification FAILED. Do not attempt to boot the target.")
    print("Byte-for-byte clone verified. Power off and disconnect SOURCE before first boot.")
    return 0

if __name__ == "__main__":
    try:
        sys.exit(main())
    except (OSError, ValueError, PermissionError, subprocess.SubprocessError) as error:
        print("GearWin clone failed:", error, file=sys.stderr)
        print("If copying started, the target may be incomplete. Do not boot it.", file=sys.stderr)
        sys.exit(1)
