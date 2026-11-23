#!/usr/bin/env python3
"""GearWin expert raw disk clone. Run ONLY from an offline Linux Live environment.

Caution: the entire source capacity is written, even unused sectors.
For routine SSD migration prefer Clonezilla's filesystem-aware workflow.
"""
import argparse
from dataclasses import dataclass
import hashlib
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
                            bool(int(obj.get("ro") or 0)), busy(obj))
    return result

def list_disks():
    result = subprocess.run(
        ["lsblk", "-J", "-b", "-o", "PATH,TYPE,SIZE,SERIAL,MODEL,RO,MOUNTPOINTS,FSTYPE"],
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
    return a, b

def is_live_boot():
    try:
        args = Path("/proc/cmdline").read_text(encoding="utf-8").split()
        return "boot=live" in args or "boot=casper" in args or Path("/run/live/medium").exists()
    except OSError:
        return False

def copy_bytes(src, dest, size, chunk_size=BLOCK_SIZE):
    """Sequential bounded-memory copy, used by production and virtual-image tests."""
    left = size
    while left:
        block = src.read(min(left, chunk_size))
        if not block:
            raise OSError("Unexpected end of source.")
        view = memoryview(block)
        while len(view):
            amount = dest.write(view)
            if amount is None or amount <= 0:
                raise OSError("Destination write failed.")
            view = view[amount:]
        left -= len(block)
    dest.flush()
    os.fsync(dest.fileno())

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

def clone(src, dst, size):
    # Open destination without O_TRUNC to avoid altering disk geometry; fail on IO errors.
    reader = os.open(src, os.O_RDONLY)
    try:
        writer = os.open(dst, os.O_WRONLY | os.O_SYNC)
        try:
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
            f"serial={d.serial or '?'} | busy={d.busy} | read-only={d.readonly}")

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
    print("ALL existing target partitions and contents WILL BE ERASED.")
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
    latest = list_disks()
    new_src, new_dst = inspect(latest, src.path, dst.path)
    if (src.size, src.serial, dst.size, dst.serial) != (
            new_src.size, new_src.serial, new_dst.size, new_dst.serial):
        raise ValueError("Disk identity changed before execution.")
    if input(f"Type EXACT target path {dst.path}: ").strip() != dst.path:
        raise PermissionError("Target path confirmation failed.")
    if input(f"Type ERASE {dst.path} to overwrite it: ").strip() != f"ERASE {dst.path}":
        raise PermissionError("Second destructive-action confirmation failed.")
    print("Copying and SHA-256 verifying all source bytes. Never disconnect power.")
    if not clone(src.path, dst.path, src.size):
        raise OSError("Cloned data verification FAILED. Do not attempt to boot the target.")
    print("Byte-for-byte clone verified. Power off and disconnect SOURCE before first boot.")
    return 0

if __name__ == "__main__":
    try:
        sys.exit(main())
    except (OSError, ValueError, PermissionError, subprocess.SubprocessError) as error:
        print("GearWin clone failed:", error, file=sys.stderr)
        sys.exit(1)
