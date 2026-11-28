#!/usr/bin/env python3
"""Real-kernel recovery tests. Only loop devices allocated from our own temp files.
Never run on a personal machine. GitHub-hosted disposable runners only.
Production CLI continues to reject loop devices.
"""
from contextlib import ExitStack
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import stat
import subprocess
import sys
import tempfile
import unittest

import gearwin_clone as raw
import partclone_adapter as partclone


def run(*args):
    return subprocess.run(args, check=True, capture_output=True, text=True, timeout=180).stdout.strip()


@unittest.skipUnless(os.geteuid() == 0 and os.environ.get("GITHUB_ACTIONS") == "true",
                     "Requires a disposable GitHub Linux runner and root")
class RecoveryLoopTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="gearwin-ci-")
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.devices = {}

    def loop(self, size, sector=512, fill=False):
        file = self.root / f"{len(self.devices)}.img"
        with file.open("xb") as stream:
            if fill:
                for _ in range(size):
                    stream.write(os.urandom(1024 * 1024))
            else:
                stream.truncate(size * 1024 * 1024)
        device = run("losetup", "--find", "--show", "--sector-size", str(sector), str(file))
        # Never pass another device to a mutating command, even if losetup output
        # is unexpected. Validate the backing file every time.
        if not re.fullmatch(r"/dev/loop\d+", device):
            raise RuntimeError("Unexpected loop device path.")
        self.devices[device] = file
        self.addCleanup(self.detach, device)
        self.assert_owned(device)
        return device, file

    def assert_owned(self, device):
        file = self.devices[device]
        if not stat.S_ISBLK(os.stat(device).st_mode):
            raise RuntimeError("Not a loop block device.")
        backing = run("losetup", "--noheadings", "--output", "BACK-FILE", device)
        if Path(backing).resolve() != file.resolve():
            raise RuntimeError("Loop backing file differs from fixture.")
        return device

    def detach(self, device):
        self.assert_owned(device)
        run("losetup", "--detach", device)

    def mount(self, device, folder, readonly=False):
        self.assert_owned(device)
        run("mount", *(["-o", "ro"] if readonly else []), device, str(folder))
        self.addCleanup(self.unmount, folder)

    @staticmethod
    def unmount(folder):
        if subprocess.run(["mountpoint", "-q", str(folder)], timeout=15).returncode == 0:
            run("umount", str(folder))

    def test_native_geometry_and_verified_copy(self):
        a, source = self.loop(8, fill=True)
        b, target = self.loop(12)
        with ExitStack() as stack:
            reader = os.open(self.assert_owned(a), os.O_RDONLY | os.O_EXCL)
            stack.callback(os.close, reader)
            writer = os.open(self.assert_owned(b), os.O_RDWR | os.O_EXCL)
            stack.callback(os.close, writer)
            raw.sysfs_safety(os.fstat(reader))
            raw.sysfs_safety(os.fstat(writer))
            size = raw.get_geometry(reader)[0]
            raw.validate_geometry(raw.get_geometry(reader), raw.get_geometry(writer), size)
            self.assertTrue(raw.copy_verified_handles(reader, writer, size))
        with source.open("rb") as src, target.open("rb") as dst:
            self.assertEqual(raw.digest_stream(src, size), raw.digest_stream(dst, size))
            dst.seek(size)
            self.assertEqual(dst.read(4096), bytes(4096))

    def test_native_sector_mismatch_and_smaller_capacity(self):
        a, _ = self.loop(8)
        b, _ = self.loop(8, sector=4096)
        c, _ = self.loop(4)
        with ExitStack() as stack:
            fds = []
            for device in (a, b, c):
                fd = os.open(self.assert_owned(device), os.O_RDONLY | os.O_EXCL)
                stack.callback(os.close, fd)
                fds.append(fd)
            size = raw.get_geometry(fds[0])[0]
            for other in fds[1:]:
                with self.assertRaises(ValueError):
                    raw.validate_geometry(raw.get_geometry(fds[0]), raw.get_geometry(other), size)

    def test_exclusive_claim_rejects_mounted_target(self):
        device, _ = self.loop(32)
        run("mkfs.ext4", "-q", self.assert_owned(device))
        folder = self.root / "mnt"
        folder.mkdir()
        self.mount(device, folder)
        with self.assertRaises(OSError):
            fd = os.open(self.assert_owned(device), os.O_RDWR | os.O_EXCL)
            os.close(fd)

    def test_partclone_fd_image_check_restore_and_tamper(self):
        for tool in ("partclone.ext4", "partclone.chkimg", "partclone.restore"):
            self.assertIsNotNone(partclone.trusted_tool(tool), f"Required tool missing: {tool}")
        device, _ = self.loop(64)
        run("mkfs.ext4", "-q", self.assert_owned(device))
        folder = self.root / "mnt"
        folder.mkdir()
        self.mount(device, folder)
        payload = os.urandom(2 * 1024 * 1024)
        (folder / "test.bin").write_bytes(payload)
        self.unmount(folder)
        image = self.root / "partition.pcl"
        # Use the same held-source fd path convention and checker as production.
        fd = os.open(self.assert_owned(device), os.O_RDONLY | os.O_EXCL)
        try:
            subprocess.run([partclone.trusted_tool("partclone.ext4"), "--clone", "--source",
                            f"/proc/self/fd/{fd}", "--output", str(image), "--logfile", "/dev/null"],
                           pass_fds=(fd,), check=True, timeout=180, env=partclone.ENV)
        finally:
            os.close(fd)
        self.assertLess(image.stat().st_size, 40 * 1024 * 1024)
        manifest = {"schema": 1, "bytes": image.stat().st_size, "sha256": partclone.sha256(image)}
        Path(str(image) + ".json").write_text(json.dumps(manifest))
        self.assertEqual(partclone.verify_image(image), "sha256+partclone.chkimg")
        # Restore is CI-only, to a second freshly created loop. Not a user feature.
        target, _ = self.loop(64)
        run(partclone.trusted_tool("partclone.restore"), "--source", str(image),
            "--output", self.assert_owned(target), "--logfile", "/dev/null")
        run("e2fsck", "-fn", self.assert_owned(target))
        self.mount(target, folder, readonly=True)
        self.assertEqual((folder / "test.bin").read_bytes(), payload)
        self.unmount(folder)
        with image.open("r+b") as stream:
            stream.seek(image.stat().st_size // 2)
            byte = stream.read(1)
            stream.seek(-1, os.SEEK_CUR)
            stream.write(bytes([byte[0] ^ 0xff]))
        with self.assertRaises(ValueError):
            partclone.verify_image(image)


if __name__ == "__main__":
    if os.geteuid() != 0 or os.environ.get("GITHUB_ACTIONS") != "true":
        raise SystemExit("Refusing to run real-device fixtures outside a disposable GitHub runner.")
    unittest.main(verbosity=2)
