import importlib.util
from pathlib import Path
import sys
import tempfile
import unittest

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

if __name__ == "__main__":
    unittest.main()

class AdvancedCloneSafetyTests(unittest.TestCase):
    def test_sector_mismatch_and_same_majmin_are_rejected(self):
        a=clone.Disk('/dev/sda',1000,'SOURCE','src',False,False,512,4096,'8:0')
        b=clone.Disk('/dev/sdb',1000,'TARGET','dst',False,False,4096,4096,'8:16')
        with self.assertRaises(ValueError):
            clone.inspect({'/dev/sda':a,'/dev/sdb':b},a.path,b.path)
        b=clone.Disk('/dev/sdb',1000,'TARGET','dst',False,False,512,512,'8:0')
        with self.assertRaises(ValueError):
            clone.inspect({'/dev/sda':a,'/dev/sdb':b},a.path,b.path)

    def test_dmcrypt_lvm_raid_blocked(self):
        for kind, fstype in [('crypt','crypto_LUKS'),('lvm','ext4'),('part','LVM2_member')]:
            data={'blockdevices':[{'path':'/dev/sda','type':'disk','size':1000,'ro':0,
                 'children':[{'path':'/dev/sda1','type':'part','fstype':'ntfs',
                              'children':[{'path':'/dev/dm-0','type':kind,'fstype':fstype}]}]},
                {'path':'/dev/sdb','type':'disk','size':2000,'ro':0}]}
            with self.subTest(kind=kind,fstype=fstype):
                records=clone.parse_disks(data)
                self.assertTrue(records['/dev/sda'].layered)
                with self.assertRaises(ValueError):
                    clone.inspect(records,'/dev/sda','/dev/sdb')

    def test_sysfs_holders_on_disk_and_partition_fail_closed(self):
        import types
        import os
        with tempfile.TemporaryDirectory() as base:
            root=Path(base)
            device=root/'device'
            (device/'holders').mkdir(parents=True)
            (device/'dev').write_text('8:0')
            part=device/'sda1'
            (part/'holders').mkdir(parents=True)
            (part/'partition').write_text('1')
            link=root/'8:0'
            link.symlink_to(device, target_is_directory=True)
            st=types.SimpleNamespace(st_rdev=os.makedev(8,0))
            self.assertEqual(clone.sysfs_safety(st,sysfs_root=root),'8:0')
            (part/'holders'/'dm-0').touch()
            with self.assertRaises(ValueError):
                clone.sysfs_safety(st,sysfs_root=root)
            (part/'holders'/'dm-0').unlink()
            (part/'holders').rmdir()
            with self.assertRaises(ValueError):
                clone.sysfs_safety(st,sysfs_root=root)
