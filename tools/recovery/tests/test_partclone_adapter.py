import hashlib
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch
import os

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import partclone_adapter as m


class PartcloneTests(unittest.TestCase):
    def setUp(self):
        self.part = {"path": "/dev/sda1", "type": "part", "fstype": "ntfs", "pkname": "sda",
                     "size": 4096, "maj:min": "8:1", "log-sec": 512, "mountpoints": [None], "ro": 0}
        self.disk = {"path": "/dev/sda", "type": "disk", "serial": "A", "wwn": "one",
                     "maj:min": "8:0", "children": [self.part]}
        self.data = {"blockdevices": [self.disk]}

    def test_select_known_offline_partition(self):
        part, disk, tool = m.choose_partition(self.data, "/dev/sda1")
        self.assertEqual((part, disk, tool), (self.part, self.disk, "partclone.ntfs"))

    def test_whole_disk_unknown_fs_and_active_child_rejected(self):
        with self.assertRaises(ValueError):
            m.choose_partition(self.data, "/dev/sda")
        for key, value in (("fstype", "crypto_LUKS"), ("mountpoints", ["/mnt"]), ("children", [{}]),
                           ("ro", 1), ("log-sec", None), ("maj:min", None)):
            with self.subTest(key=key):
                old = dict(self.part)
                self.part[key] = value
                with self.assertRaises(ValueError):
                    m.choose_partition(self.data, "/dev/sda1")
                self.part.clear()
                self.part.update(old)

    def test_other_mounted_partition_on_source_disk_refused(self):
        self.disk["children"].append({"mountpoints": ["/"]})
        with self.assertRaises(ValueError):
            m.choose_partition(self.data, "/dev/sda1")

    def test_unknown_parent_identity_refused(self):
        self.disk.update(serial="", wwn="")
        with self.assertRaises(ValueError):
            m.choose_partition(self.data, "/dev/sda1")

    def test_hardware_alias_rejected(self):
        other = {"path": "/dev/sdb", "maj:min": "8:16", "serial": "B", "wwn": "two"}
        m.separate_disks(self.disk, other)
        for key in ("path", "maj:min", "serial", "wwn"):
            with self.subTest(key=key), self.assertRaises(ValueError):
                m.separate_disks(self.disk, {**other, key: self.disk[key]})

    def test_partition_identity_detects_geometry_change(self):
        self.assertNotEqual(m.partition_identity(self.part),
                            m.partition_identity({**self.part, "log-sec": 4096}))

    def test_backup_cannot_run_without_live_boot(self):
        with patch.object(m.os, "geteuid", return_value=0), patch.object(m, "is_live_boot", return_value=False), patch.object(m, "plan") as plan:
            with self.assertRaises(PermissionError):
                m.backup("/dev/sda1", "/unused/output")
            plan.assert_not_called()


class VerifyTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.image = Path(self.temp.name) / "image.pcl"
        self.image.write_bytes(b"fixture")
        self.manifest = Path(str(self.image) + ".json")
        self.valid = {"schema": 1, "bytes": 7, "sha256": hashlib.sha256(b"fixture").hexdigest()}
        self.manifest.write_text(json.dumps(self.valid))

    def test_hash_only_label_does_not_claim_engine_check(self):
        with patch.object(m, "trusted_tool", return_value=None):
            self.assertIn("NOT checked", m.verify_image(self.image))

    def test_engine_check_uses_open_descriptor(self):
        with patch.object(m, "trusted_tool", return_value="/usr/bin/partclone.chkimg"), patch.object(m, "check_engine") as check:
            self.assertEqual(m.verify_image(self.image), "sha256+partclone.chkimg")
            check.assert_called_once()
            self.assertIsInstance(check.call_args.args[1], int)

    def test_detects_tamper_and_missing_manifest(self):
        self.image.write_bytes(b"changed")
        with self.assertRaises(ValueError):
            m.verify_image(self.image)
        self.manifest.unlink()
        with self.assertRaises(OSError):
            m.verify_image(self.image)

    def test_rejects_bad_schema_and_oversized_manifest(self):
        for obj in ({**self.valid, "schema": True}, {**self.valid, "bytes": True},
                    {**self.valid, "sha256": "bad"}, []):
            self.manifest.write_text(json.dumps(obj))
            with self.assertRaises(ValueError):
                m.verify_image(self.image)
        self.manifest.write_text("x" * (m.MANIFEST_LIMIT + 1))
        with self.assertRaises(ValueError):
            m.verify_image(self.image)

    def test_rejects_duplicate_key_and_symlink(self):
        self.manifest.write_text('{"schema":1,"schema":1}')
        with self.assertRaises(ValueError):
            m.verify_image(self.image)
        self.manifest.unlink()
        real = Path(self.temp.name) / "real.json"
        real.write_text(json.dumps(self.valid))
        self.manifest.symlink_to(real)
        with self.assertRaises(OSError):
            m.verify_image(self.image)

    def test_existing_image_or_dangling_manifest_refused(self):
        with self.assertRaises(ValueError):
            m.output_parent(str(self.image))
        self.image.unlink()
        self.manifest.unlink()
        self.manifest.symlink_to("missing")
        with self.assertRaises(ValueError):
            m.output_parent(str(self.image))


if __name__ == "__main__":
    unittest.main()
