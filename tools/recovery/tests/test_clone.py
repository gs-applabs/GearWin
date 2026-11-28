import importlib.util
from pathlib import Path
import sys
import tempfile
import unittest
from dataclasses import replace
from unittest.mock import patch
import os

path = Path(__file__).resolve().parents[1] / "gearwin_clone.py"
spec = importlib.util.spec_from_file_location("gearwin_clone", path)
clone = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = clone
spec.loader.exec_module(clone)

class CloneSafetyTests(unittest.TestCase):
    def setUp(self):
        self.a = clone.Disk("/dev/sda", 4096, "SRC", "Source", False, False)
        self.b = clone.Disk("/dev/sdb", 8192, "TGT", "Target", False, False)

    def test_valid_plan(self):
        self.assertEqual(clone.inspect({self.a.path: self.a, self.b.path: self.b},
                                       self.a.path, self.b.path), (self.a, self.b))

    def test_same_smaller_readonly_and_mounted_target_rejected(self):
        disks = {self.a.path: self.a, self.b.path: self.b}
        with self.assertRaises(ValueError):
            clone.inspect(disks, self.a.path, self.a.path)
        for replacement in (
            clone.Disk(self.b.path, 2048, "TGT", "Target", False, False),
            clone.Disk(self.b.path, 8192, "TGT", "Target", True, False),
            clone.Disk(self.b.path, 8192, "TGT", "Target", False, True),
        ):
            with self.assertRaises(ValueError):
                clone.inspect({self.a.path: self.a, self.b.path: replacement},
                              self.a.path, self.b.path)

    def test_unsafe_mounted_child_is_detected(self):
        data = {"blockdevices": [
            {"type": "disk", "path": "/dev/sda", "size": 4096, "children": [
                {"type": "part", "mountpoints": ["/boot"]}]},
            {"type": "disk", "path": "/dev/sdb", "size": 8192, "children": [
                {"type": "part", "fstype": "swap"}]}
        ]}
        disks = clone.parse_disks(data)
        self.assertTrue(disks["/dev/sda"].busy)
        self.assertTrue(disks["/dev/sdb"].busy)

    def test_virtual_file_clone_and_hash_validation(self):
        with tempfile.TemporaryDirectory() as root:
            source = Path(root) / "src.img"
            target = Path(root) / "dst.img"
            payload = bytes(range(253)) * 13
            source.write_bytes(payload)
            target.write_bytes(b"\0" * (len(payload) + 20))
            self.assertTrue(clone.clone(str(source), str(target), len(payload)))
            self.assertEqual(target.read_bytes()[:len(payload)], payload)
            self.assertEqual(target.read_bytes()[len(payload):], b"\0" * 20)

    def test_truncated_input_fails(self):
        with tempfile.TemporaryDirectory() as root:
            source = Path(root) / "src.img"
            target = Path(root) / "dst.img"
            source.write_bytes(b"x")
            target.write_bytes(b"z" * 100)
            with self.assertRaises(OSError):
                clone.clone(str(source), str(target), 100)

    def test_identity_change_after_confirmation_is_rejected(self):
        disk = replace(self.a, major_minor="8:0", wwn="wwn-a")
        for changed in (
            replace(disk, serial="different"), replace(disk, model="different"),
            replace(disk, major_minor="8:16"), replace(disk, size=8192),
            replace(disk, wwn="wwn-b")
        ):
            with self.subTest(changed=changed), self.assertRaises(ValueError):
                clone.confirm_identity(disk, changed)
        clone.confirm_identity(disk, disk)

    def test_missing_stable_identifier_rejected(self):
        with self.assertRaises(ValueError):
            clone.identity(replace(self.a, major_minor="8:0", serial=""))

    def test_missing_kernel_identity_rejected(self):
        with self.assertRaises(ValueError):
            clone.identity(self.a)

    def test_parse_tree_and_kernel_identity(self):
        data = {"blockdevices": [{"type": "disk", "path": "/dev/sda",
                "size": 4096, "maj:min": "8:0", "wwn": "wwn-a",
                "children": [{"type": "part", "mountpoints": ["/"]}]}]}
        disk = clone.parse_disks(data)["/dev/sda"]
        self.assertTrue(disk.busy)
        self.assertEqual(disk.major_minor, "8:0")
        self.assertEqual(disk.wwn, "wwn-a")

    def test_block_validator_rejects_regular_file(self):
        with tempfile.TemporaryFile() as f, self.assertRaises(ValueError):
            clone.validate_open_device(f.fileno(), self.a)

    def test_fixture_hardlink_rejected_without_writing(self):
        with tempfile.TemporaryDirectory() as root:
            a = Path(root) / "a"
            b = Path(root) / "b"
            a.write_bytes(b"keep")
            os.link(a, b)
            with self.assertRaises(ValueError):
                clone.clone(a, b, 4)
            self.assertEqual(a.read_bytes(), b"keep")

    def test_short_fixture_does_not_change_destination(self):
        with tempfile.TemporaryDirectory() as root:
            a = Path(root) / "a"
            b = Path(root) / "b"
            a.write_bytes(b"a")
            b.write_bytes(b"keep")
            with self.assertRaises(OSError):
                clone.clone(a, b, 4)
            self.assertEqual(b.read_bytes(), b"keep")

    def test_failed_second_claim_closes_first(self):
        a = replace(self.a, major_minor="8:0")
        b = replace(self.b, major_minor="8:16")
        with patch.object(clone.os, "open", side_effect=[99, OSError("busy")]), \
             patch.object(clone, "validate_open_device", return_value=8), \
             patch.object(clone.os, "close") as close:
            with self.assertRaises(OSError):
                clone.clone_block_devices(a, b)
            close.assert_called_once_with(99)

    def test_no_copy_when_inventory_changes_after_claim(self):
        a = replace(self.a, major_minor="8:0")
        b = replace(self.b, major_minor="8:16")
        with patch.object(clone.os, "open", side_effect=[98, 99]), \
             patch.object(clone.os, "close") as close, \
             patch.object(clone, "validate_open_device", side_effect=[8, 16]), \
             patch.object(clone, "list_disks", return_value={a.path:a, b.path:replace(b, serial="other")}), \
             patch.object(clone, "copy_bytes") as copy:
            with self.assertRaises(ValueError):
                clone.clone_block_devices(a, b)
            copy.assert_not_called()
            self.assertEqual(close.call_count, 2)

    def test_geometry_rejects_mismatched_sectors_or_small_target(self):
        for source, target, size in (
            ((4096, 512), (8192, 4096), 4096),
            ((8192, 512), (4096, 512), 8192),
            ((8192, 512), (8192, 512), 4096),
        ):
            with self.subTest(source=source, target=target), self.assertRaises(ValueError):
                clone.validate_geometry(source, target, size)
        clone.validate_geometry((4096, 512), (8192, 512), 4096)

    def test_plan_rejects_incompatible_sector_sizes(self):
        a = replace(self.a, logical_sector=512)
        b = replace(self.b, logical_sector=4096)
        with self.assertRaises(ValueError):
            clone.inspect({a.path: a, b.path: b}, a.path, b.path)

    def test_geometry_changed_after_confirmation(self):
        a = replace(self.a, major_minor="8:0", logical_sector=512)
        with self.assertRaises(ValueError):
            clone.confirm_identity(a, replace(a, logical_sector=4096))

    def test_geometry_uses_native_ioctl_and_rejects_invalid_values(self):
        import struct
        with patch.object(clone.fcntl, "ioctl", side_effect=[struct.pack("=Q", 8192), struct.pack("=I", 4096)]):
            self.assertEqual(clone.get_geometry(1), (8192, 4096))
        with patch.object(clone.fcntl, "ioctl", side_effect=[struct.pack("=Q", 8192), struct.pack("=I", 1000)]):
            with self.assertRaises(ValueError):
                clone.get_geometry(1)

    def test_claimed_handles_verify_and_preserve_extra_bytes(self):
        with tempfile.TemporaryFile() as a, tempfile.TemporaryFile() as b:
            a.write(b"source")
            b.write(b"olddata-tail")
            a.flush()
            b.flush()
            self.assertTrue(clone.copy_verified_handles(a.fileno(), b.fileno(), 6))
            b.seek(0)
            self.assertEqual(b.read(), b"sourcea-tail")

if __name__ == "__main__":
    unittest.main()
