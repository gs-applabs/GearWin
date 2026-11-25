#!/usr/bin/env python3
"""Real-kernel tests for the offline clone / Partclone tools, using LOOP devices on a disposable CI VM.

Run as root on a throw-away Linux VM only (GitHub-hosted ubuntu runner). It creates sparse files, attaches them
as loop devices and exercises gearwin_clone.clone() with exclusive=True (O_EXCL, BLKGETSIZE64/BLKSSZGET ioctls,
sysfs holders) and the partclone_adapter verify path plus a Partclone round trip. It never touches real disks:
every device it uses was created here by `losetup`. It refuses to run outside CI unless GEARWIN_LOOP_TEST=1.
"""
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))


def load(name):
    spec = importlib.util.spec_from_file_location(name, HERE / f"{name}.py")
    mod = importlib.util.module_from_spec(spec)
    sys.modules[name] = mod
    spec.loader.exec_module(mod)
    return mod


clone = load("gearwin_clone")
adapter = load("partclone_adapter")


def run(*cmd, **kw):
    return subprocess.run(cmd, check=True, capture_output=True, text=True, **kw).stdout.strip()


def sha(path, size=None):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        left = size
        while True:
            n = 4 << 20 if left is None else min(4 << 20, left)
            if n == 0:
                break
            b = f.read(n)
            if not b:
                break
            h.update(b)
            if left is not None:
                left -= len(b)
    return h.hexdigest()


@unittest.skipUnless(os.geteuid() == 0 and sys.platform.startswith("linux")
                     and (os.environ.get("CI") or os.environ.get("GEARWIN_LOOP_TEST") == "1"),
                     "needs root on a disposable Linux CI VM")
class LoopDeviceTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.mkdtemp(prefix="gwloop-")
        self.loops = []
        self.mounts = []

    def tearDown(self):
        for m in self.mounts:
            subprocess.run(["umount", m], capture_output=True)
        for d in self.loops:
            subprocess.run(["losetup", "-d", d], capture_output=True)
        shutil.rmtree(self.tmp, ignore_errors=True)

    def loop(self, size_mb, sector=None, fill=False):
        img = Path(self.tmp) / f"d{len(self.loops)}.img"
        with open(img, "wb") as f:
            if fill:
                f.write(os.urandom(size_mb << 20))
            else:
                f.truncate(size_mb << 20)
        cmd = ["losetup", "-f", "--show"]
        if sector:
            cmd += ["--sector-size", str(sector)]
        dev = run(*cmd, str(img))
        self.loops.append(dev)
        return dev, img

    def test_real_device_clone_matches_and_leaves_extra_space_untouched(self):
        src, _ = self.loop(32, fill=True)
        dst, _ = self.loop(48)
        gs = clone.get_geometry(os.open(src, os.O_RDONLY))
        gd = clone.get_geometry(os.open(dst, os.O_RDONLY))
        self.assertEqual(gs[0], 32 << 20)
        self.assertTrue(clone.clone(src, dst, gs[0], exclusive=True, expected=(gs, gd)))
        self.assertEqual(sha(src, gs[0]), sha(dst, gs[0]))
        # bytes beyond the source size must stay untouched (zeros)
        with open(dst, "rb") as f:
            f.seek(gs[0])
            self.assertEqual(f.read(1 << 20), b"\0" * (1 << 20))

    def test_mounted_target_is_refused_by_exclusive_open(self):
        src, _ = self.loop(16, fill=True)
        dst, _ = self.loop(16)
        run("mkfs.ext4", "-q", dst)
        mnt = Path(self.tmp) / "mnt"
        mnt.mkdir()
        run("mount", dst, str(mnt))
        self.mounts.append(str(mnt))
        g = clone.get_geometry(os.open(src, os.O_RDONLY))
        with self.assertRaises(OSError):
            clone.clone(src, dst, g[0], exclusive=True)

    def test_logical_sector_mismatch_is_refused(self):
        src, _ = self.loop(16, fill=True)
        dst, _ = self.loop(16, sector=4096)
        with self.assertRaises(ValueError):
            clone.clone(src, dst, 16 << 20, exclusive=True)

    def test_smaller_target_is_refused(self):
        src, _ = self.loop(16, fill=True)
        dst, _ = self.loop(8)
        with self.assertRaises(ValueError):
            clone.clone(src, dst, 16 << 20, exclusive=True)

    def test_same_device_is_refused(self):
        src, _ = self.loop(8, fill=True)
        # real kernel: the reader's O_EXCL claim makes the second open fail with EBUSY (ValueError if the identity check runs first)
        with self.assertRaises((OSError, ValueError)):
            clone.clone(src, src, 8 << 20, exclusive=True)

    def test_sysfs_holder_blocks_clone(self):
        src, _ = self.loop(16, fill=True)
        dst, _ = self.loop(16)
        run("pvcreate", "-ff", "-y", dst) if shutil.which("pvcreate") else self.skipTest("lvm2 not installed")
        run("vgcreate", "gwvg", dst)
        try:
            try:
                run("lvcreate", "-y", "-L", "4M", "-n", "gwlv", "gwvg")
            except subprocess.CalledProcessError as exc:
                if os.environ.get("GEARWIN_CONTAINER"):  # containers have no udev/device-mapper node creation
                    self.skipTest("device-mapper unavailable inside this container: " + (exc.stderr or "")[:120])
                raise
            g = clone.get_geometry(os.open(src, os.O_RDONLY))
            with self.assertRaises((OSError, ValueError)):
                clone.clone(src, dst, g[0], exclusive=True)
        finally:
            subprocess.run(["vgremove", "-f", "gwvg"], capture_output=True)

    def test_list_disks_runs_on_real_lsblk(self):
        self.assertIsInstance(clone.list_disks(), dict)

    def test_partclone_roundtrip_and_verify(self):
        for tool in ("partclone.ext4", "partclone.chkimg", "partclone.restore"):
            if not shutil.which(tool):
                self.skipTest(f"{tool} not installed")
        src, _ = self.loop(64)
        run("mkfs.ext4", "-q", src)
        mnt = Path(self.tmp) / "m"
        mnt.mkdir()
        run("mount", src, str(mnt))
        self.mounts.append(str(mnt))
        payload = os.urandom(3 << 20)
        (mnt / "file.bin").write_bytes(payload)
        run("sync")
        run("umount", str(mnt))
        self.mounts.remove(str(mnt))
        image = Path(self.tmp) / "p.pcl"
        run("partclone.ext4", "--clone", "--source", src, "--output", str(image))
        run("partclone.chkimg", "--source", str(image))
        manifest = {"schema": 1, "engine": "partclone.ext4", "partition": src, "filesystem": "ext4",
                    "bytes": image.stat().st_size, "sha256": adapter.sha256(image),
                    "verification": "partclone.chkimg"}
        Path(str(image) + ".json").write_text(json.dumps(manifest))
        self.assertIn("partclone.chkimg", adapter.verify_image(image))
        # used-block image must be much smaller than the 64 MB partition
        self.assertLess(image.stat().st_size, 40 << 20)
        # restore into a second loop device (test-only; the tool itself does not restore) and compare files
        dst, _ = self.loop(64)
        run("partclone.restore", "--source", str(image), "--output", dst)
        run("e2fsck", "-fn", dst)
        run("mount", "-o", "ro", dst, str(mnt))
        self.mounts.append(str(mnt))
        self.assertEqual((mnt / "file.bin").read_bytes(), payload)
        # tampering is detected
        with open(image, "r+b") as f:
            f.seek(image.stat().st_size // 2)
            f.write(b"\xff\xff\xff\xff")
        with self.assertRaises(ValueError):
            adapter.verify_image(image)


if __name__ == "__main__":
    unittest.main(verbosity=2)
