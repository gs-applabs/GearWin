#!/usr/bin/env python3
"""GearWin optional used-block PARTITION image via independently installed Partclone.
No bundled GPL binary, no partition restore, no repartitioning, no boot guarantee.
Offline Linux Live only for backup. See README.md; fail closed on unknown topology.
"""
from __future__ import annotations

import argparse
from contextlib import ExitStack
import hashlib
import json
import os
from pathlib import Path
import re
import stat
import subprocess
import sys
import tempfile

from gearwin_clone import BLOCK_SIZE, get_geometry, is_live_boot, sysfs_safety

SUPPORTED = {
    "ntfs": "partclone.ntfs", "ext2": "partclone.ext2", "ext3": "partclone.ext3",
    "ext4": "partclone.ext4", "vfat": "partclone.fat", "exfat": "partclone.exfat",
    "xfs": "partclone.xfs", "btrfs": "partclone.btrfs", "f2fs": "partclone.f2fs",
}
SYSTEM_DIRS = ("/usr/sbin", "/usr/bin", "/sbin", "/bin")
ENV = {"PATH": ":".join(SYSTEM_DIRS), "LANG": "C", "LC_ALL": "C", "HOME": "/root"}
MANIFEST_LIMIT = 16384
TIMEOUT = 12 * 3600


def trusted_tool(name):
    for directory in SYSTEM_DIRS:
        path = Path(directory) / name
        try:
            actual = path.resolve(strict=True)
            info = actual.stat()
            if stat.S_ISREG(info.st_mode) and info.st_uid == 0 and not info.st_mode & 0o022 and os.access(actual, os.X_OK):
                for parent in actual.parents:
                    facts = parent.stat()
                    if facts.st_uid != 0 or facts.st_mode & 0o022:
                        break
                else:
                    return str(actual)
        except OSError:
            continue
    return None


def query(name, args):
    program = trusted_tool(name)
    if not program:
        raise ValueError(f"Trusted system tool unavailable: {name}")
    result = subprocess.run([program, *args], check=True, capture_output=True,
                            text=True, timeout=30, env=ENV)
    if len(result.stdout) > 4 * 1024 * 1024:
        raise ValueError("Device inventory too large.")
    return result.stdout


def inventory():
    return json.loads(query("lsblk", ["--json", "--bytes", "-o",
        "NAME,PATH,TYPE,SIZE,FSTYPE,MOUNTPOINTS,PKNAME,RO,MAJ:MIN,SERIAL,WWN,MODEL,LOG-SEC"]))


def flatten(blocks):
    for item in blocks:
        yield item
        yield from flatten(item.get("children") or [])


def exact(items, field, value):
    matches = [item for item in items if item.get(field) == value]
    if len(matches) != 1:
        raise ValueError("Device identity missing or ambiguous.")
    return matches[0]


def parent_disk(items, partition):
    parent = str(partition.get("pkname") or "")
    if not parent.startswith("/dev/"):
        parent = "/dev/" + parent
    disk = exact(items, "path", parent)
    if disk.get("type") != "disk" or Path(parent).name.startswith(("loop", "ram", "zram", "dm-", "md", "sr")):
        raise ValueError("Only ordinary physical disk partitions are supported.")
    if not disk.get("maj:min") or not (str(disk.get("serial") or "").strip() or str(disk.get("wwn") or "").strip()):
        raise ValueError("Stable parent disk identity unavailable.")
    return disk


def busy(item):
    return bool(item.get("mountpoint") or any(item.get("mountpoints") or []) or
                str(item.get("fstype") or "").lower() == "swap" or
                any(busy(child) for child in item.get("children") or []))


def choose_partition(data, device):
    items = list(flatten(data.get("blockdevices") or []))
    part = exact(items, "path", device)
    if part.get("type") != "part" or not str(device).startswith("/dev/"):
        raise ValueError("Select one physical partition, not a whole disk.")
    fs = str(part.get("fstype") or "").lower()
    if fs not in SUPPORTED:
        raise ValueError("Unsupported filesystem; no automatic raw fallback.")
    if part.get("ro") not in (0, "0", False) or not part.get("maj:min") or not part.get("size") or not part.get("log-sec"):
        raise ValueError("Unknown or unsupported partition geometry/state.")
    if busy(part) or part.get("children"):
        raise ValueError("Source partition is mounted or has active children.")
    disk = parent_disk(items, part)
    if busy(disk):
        raise ValueError("All partitions of the source disk must be offline.")
    return part, disk, SUPPORTED[fs]


def disk_identity(disk):
    return tuple(disk.get(key) for key in ("path", "maj:min", "size", "serial", "wwn", "model", "log-sec"))


def partition_identity(part):
    return tuple(part.get(key) for key in ("path", "maj:min", "pkname", "size", "log-sec", "fstype", "ro"))


def separate_disks(source, target):
    if (source.get("maj:min") == target.get("maj:min") or source.get("path") == target.get("path") or
        (source.get("wwn") and source["wwn"] == target.get("wwn")) or
        (source.get("serial") and source["serial"] == target.get("serial"))):
        raise ValueError("Destination is the source disk or a possible hardware alias.")


def output_parent(output):
    image = Path(output)
    if not image.is_absolute() or image.name in ("", ".", "..") or image.parent.resolve(strict=True) != image.parent:
        raise ValueError("Use an existing absolute output directory without symlink/relative ancestors.")
    if os.path.lexists(image) or os.path.lexists(str(image) + ".json"):
        raise ValueError("Image or manifest already exists.")
    # Conservative root-only output directory ancestry. Avoid untrusted path swaps.
    for directory in (image.parent, *image.parent.parents):
        facts = directory.stat()
        if not stat.S_ISDIR(facts.st_mode) or facts.st_uid != 0 or facts.st_mode & 0o022:
            raise ValueError("Output directory and ancestors must be root-owned and not group/world writable.")
    return image


def destination(data, source_disk, directory):
    line = query("findmnt", ["--target", str(directory), "--noheadings", "--output", "MAJ:MIN,FSTYPE"]).split()
    if len(line) != 2 or line[1] not in ("ext2", "ext3", "ext4", "xfs"):
        raise ValueError("Use a separate mounted ext2/3/4 or XFS physical destination partition.")
    info = directory.stat()
    if line[0] != f"{os.major(info.st_dev)}:{os.minor(info.st_dev)}":
        raise ValueError("Destination mount identity is inconsistent.")
    items = list(flatten(data.get("blockdevices") or []))
    target = exact(items, "maj:min", line[0])
    if target.get("type") != "part" or target.get("ro") not in (0, "0", False) or target.get("children"):
        raise ValueError("Unsupported destination topology or read-only partition.")
    disk = parent_disk(items, target)
    separate_disks(source_disk, disk)
    return target, disk


def plan(device, output, require_capacity=True):
    if not sys.platform.startswith("linux"):
        raise ValueError("Linux only.")
    image = output_parent(output)
    data = inventory()
    part, source_disk, tool = choose_partition(data, device)
    target, target_disk = destination(data, source_disk, image.parent)
    free = os.statvfs(image.parent)
    # Conservative admission: do not fill an existing backup volume based on
    # an unverified estimate of filesystem used blocks.
    if require_capacity and free.f_bavail * free.f_frsize < int(part["size"]) + 64 * 1024 * 1024:
        raise ValueError("Insufficient conservative free-space budget (partition capacity plus 64 MiB).")
    return part, source_disk, target, target_disk, tool


def plan_identity(p):
    part, disk, target, target_disk, tool = p
    return partition_identity(part), disk_identity(disk), partition_identity(target), disk_identity(target_disk), tool


def sha256_stream(stream):
    stream.seek(0)
    h = hashlib.sha256()
    for block in iter(lambda: stream.read(BLOCK_SIZE), b""):
        h.update(block)
    return h.hexdigest()


def regular_open(path):
    fd = os.open(path, os.O_RDONLY | os.O_CLOEXEC | os.O_NOFOLLOW | os.O_NONBLOCK)
    try:
        if not stat.S_ISREG(os.fstat(fd).st_mode):
            raise ValueError("Expected a regular file.")
        return os.fdopen(fd, "rb")
    except BaseException:
        os.close(fd)
        raise


def sha256(filename):
    with regular_open(filename) as stream:
        return sha256_stream(stream)


def unique_pairs(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError("Duplicate manifest key.")
        result[key] = value
    return result


def read_manifest(path):
    with regular_open(path) as stream:
        raw = stream.read(MANIFEST_LIMIT + 1)
    if len(raw) > MANIFEST_LIMIT:
        raise ValueError("Manifest exceeds size limit.")
    obj = json.loads(raw, object_pairs_hook=unique_pairs)
    if not isinstance(obj, dict) or type(obj.get("schema")) is not int or obj["schema"] != 1:
        raise ValueError("Unknown manifest schema.")
    if type(obj.get("bytes")) is not int or obj["bytes"] <= 0 or not isinstance(obj.get("sha256"), str) or not re.fullmatch(r"[a-fA-F0-9]{64}", obj["sha256"]):
        raise ValueError("Invalid image size/hash manifest.")
    return obj


def check_engine(checker, image_fd):
    subprocess.run([checker, "--source", f"/proc/self/fd/{image_fd}", "--logfile", "/dev/null"],
                   pass_fds=(image_fd,), check=True, stdout=subprocess.DEVNULL,
                   stderr=subprocess.DEVNULL, timeout=TIMEOUT, env=ENV)


def verify_image(image):
    manifest = read_manifest(str(image) + ".json")
    with regular_open(image) as stream:
        if os.fstat(stream.fileno()).st_size != manifest["bytes"] or sha256_stream(stream) != manifest["sha256"].lower():
            raise ValueError("Image size or SHA-256 differs from manifest.")
        checker = trusted_tool("partclone.chkimg")
        if not checker:
            return "sha256-only (Partclone internal integrity NOT checked)"
        check_engine(checker, stream.fileno())
        if sha256_stream(stream) != manifest["sha256"].lower():
            raise ValueError("Image changed during verification.")
        return "sha256+partclone.chkimg"


def backup(device, output):
    if os.geteuid() != 0 or not is_live_boot() or not sys.stdin.isatty() or not sys.stdout.isatty():
        raise PermissionError("Requires interactive root on separately booted Linux Live media.")
    original = plan(device, output)
    part, source_disk, target, _, tool = original
    program, checker = trusted_tool(tool), trusted_tool("partclone.chkimg")
    if not program or not checker:
        raise ValueError("Install the matching Partclone and checker packages from the distro.")
    print(json.dumps({"source": partition_identity(part), "disk": disk_identity(source_disk),
                      "destination": output, "engine": tool}, indent=2))
    if input(f"Type SAVE {device}: ") != f"SAVE {device}":
        raise PermissionError("Confirmation failed.")
    image = output_parent(output)
    with ExitStack() as stack:
        source_fd = os.open(device, os.O_RDONLY | os.O_EXCL | os.O_NOFOLLOW | os.O_CLOEXEC)
        stack.callback(os.close, source_fd)
        facts = os.fstat(source_fd)
        if not stat.S_ISBLK(facts.st_mode) or sysfs_safety(facts, partition=True) != part["maj:min"]:
            raise ValueError("Opened source is not the selected partition.")
        if get_geometry(source_fd) != (int(part["size"]), int(part["log-sec"])):
            raise ValueError("Opened partition geometry changed.")
        dir_fd = os.open(image.parent, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW | os.O_CLOEXEC)
        stack.callback(os.close, dir_fd)
        target_dev = os.fstat(dir_fd).st_dev
        if f"{os.major(target_dev)}:{os.minor(target_dev)}" != target["maj:min"]:
            raise ValueError("Opened output directory is not the confirmed volume.")
        if plan_identity(plan(device, output)) != plan_identity(original) or not os.path.samestat(os.fstat(dir_fd), image.parent.stat()):
            raise ValueError("Source or destination changed after confirmation.")
        # Keep directory and source claims for entire run. No pathname reopen of
        # the source outside the held descriptor. If a Partclone build rejects
        # the fd path or exclusive claim, stop; never retry without safeguards.
        root = f"/proc/self/fd/{dir_fd}"
        with tempfile.TemporaryDirectory(dir=root, prefix=".gearwin-image-") as stage:
            stage_name = Path(stage).name
            stage_fd = os.open(stage_name, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW | os.O_CLOEXEC, dir_fd=dir_fd)
            try:
                stage_path = f"/proc/self/fd/{stage_fd}/partition.pcl"
                subprocess.run([program, "--clone", "--source", f"/proc/self/fd/{source_fd}",
                                "--output", stage_path, "--logfile", "/dev/null"],
                               pass_fds=(source_fd, stage_fd), check=True, stdout=subprocess.DEVNULL,
                               stderr=subprocess.DEVNULL, timeout=TIMEOUT, env=ENV)
                with regular_open(stage_path) as stream:
                    check_engine(checker, stream.fileno())
                    digest = sha256_stream(stream)
                    length = os.fstat(stream.fileno()).st_size
                    if length <= 0:
                        raise ValueError("Empty partition image.")
                    os.fsync(stream.fileno())
                manifest = {"schema": 1, "engine": tool, "partition": device,
                            "filesystem": part["fstype"], "bytes": length, "sha256": digest,
                            "verification": "partclone.chkimg"}
                with open(f"/proc/self/fd/{stage_fd}/image.json", "x", encoding="utf-8") as out:
                    json.dump(manifest, out, indent=2)
                    out.flush()
                    os.fsync(out.fileno())
                if plan_identity(plan(device, output, require_capacity=False)) != plan_identity(original) or not os.path.samestat(os.fstat(dir_fd), image.parent.stat()):
                    raise ValueError("Source or output identity changed before publication.")
                # No replace: image first, manifest last. If manifest publication
                # fails, keep incomplete image for explicit review, never unlink
                # a potentially independently replaced pathname.
                os.link("partition.pcl", image.name, src_dir_fd=stage_fd, dst_dir_fd=dir_fd, follow_symlinks=False)
                os.fsync(dir_fd)
                os.link("image.json", image.name + ".json", src_dir_fd=stage_fd, dst_dir_fd=dir_fd, follow_symlinks=False)
                os.fsync(dir_fd)
            finally:
                os.close(stage_fd)
    print(f"Verified partition image: {output} (SHA-256 {digest}). NOT a bootable disk backup.")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=("plan", "backup", "verify"))
    parser.add_argument("--device")
    parser.add_argument("--output", required=True)
    args = parser.parse_args()
    if args.action == "verify":
        print(verify_image(args.output))
        return
    if not args.device:
        parser.error("--device is required for plan and backup")
    if args.action == "plan":
        part, disk, target, target_disk, tool = plan(args.device, args.output)
        print(json.dumps({"source": partition_identity(part), "source_disk": disk_identity(disk),
                          "target": partition_identity(target), "target_disk": disk_identity(target_disk),
                          "engine": tool, "note": "Partition image only; no data written."}, indent=2))
    else:
        backup(args.device, args.output)


if __name__ == "__main__":
    try:
        main()
    except (OSError, ValueError, subprocess.SubprocessError) as err:
        print("Partclone adapter stopped:", err, file=sys.stderr)
        print("Any image without its verified manifest is incomplete; do not restore it.", file=sys.stderr)
        raise SystemExit(1)
