import importlib.util
from pathlib import Path
import sys
import unittest
from unittest.mock import patch

path = Path(__file__).resolve().parents[1] / 'partclone_adapter.py'
spec = importlib.util.spec_from_file_location('partclone_adapter', path)
m = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = m
spec.loader.exec_module(m)

class PartcloneAdapterTests(unittest.TestCase):
    def setUp(self):
        self.data = {'blockdevices': [{
            'path': '/dev/sda', 'type': 'disk', 'children': [
                {'path': '/dev/sda3', 'type': 'part', 'fstype': 'ntfs',
                 'pkname': '/dev/sda', 'size': 1000, 'maj:min':'8:3',
                 'mountpoints':[None], 'ro': 0}
            ]
        }]}

    def test_known_unmounted_partclone_ntfs(self):
        item, tool = m.choose_partition(self.data, '/dev/sda3')
        self.assertEqual(item['path'], '/dev/sda3')
        self.assertEqual(tool, 'partclone.ntfs')
        self.assertEqual(item['pkname'], '/dev/sda')

    def test_reject_mounts_and_unknown_fs_and_whole_disk(self):
        with self.assertRaises(ValueError):
            m.choose_partition(self.data, '/dev/sda')
        x = self.data['blockdevices'][0]['children'][0]
        x['mountpoints']=['/mnt/win']
        with self.assertRaises(ValueError):
            m.choose_partition(self.data, '/dev/sda3')
        x['mountpoints']=[None]
        x['fstype']='crypto_LUKS'
        with self.assertRaises(ValueError):
            m.choose_partition(self.data, '/dev/sda3')

    def test_no_overwrite_and_exact_program_whitelist(self):
        self.assertEqual(set(m.SUPPORTED['ntfs']), set('partclone.ntfs'))
        self.assertNotIn('raw', m.SUPPORTED)

    def test_fail_closed_if_destination_is_source_disk(self):
        from subprocess import CompletedProcess
        def mock_run(args, **kwargs):
            if args[0]=='findmnt': return CompletedProcess(args, 0, '/dev/sda1\n','')
            return CompletedProcess(args, 0, 'part sda\n','')
        with patch.object(m.subprocess, 'run', side_effect=mock_run):
            with self.assertRaises(ValueError):
                m.destination_is_separate('/dev/sda', '/media/backup')

if __name__ == '__main__':
    unittest.main()
