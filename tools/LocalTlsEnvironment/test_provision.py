#!/usr/bin/env python3
"""Offline scope tests; never contact Docker or a database."""

from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

import provision


class OwnedModeRestoreTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary = tempfile.TemporaryDirectory(prefix="wdm-t07-restore-test-")
        self.addCleanup(self.temporary.cleanup)
        self.data = Path(self.temporary.name) / "data"
        (self.data / "DAMENG").mkdir(parents=True)
        self.ini = self.data / "DAMENG" / "dm.ini"
        self.ini.write_text("PAGE_SIZE = 32\nENABLE_ENCRYPT = 2\nMIN_SSL_VERSION = 771\n", encoding="utf-8")
        data_patch = patch.object(provision, "DATA", self.data)
        data_patch.start()
        self.addCleanup(data_patch.stop)

    def test_replacement_changes_only_one_allowlisted_assignment(self) -> None:
        provision.replace_host_ini_mode(self.ini, 1)
        self.assertEqual("PAGE_SIZE = 32\nENABLE_ENCRYPT = 1\nMIN_SSL_VERSION = 771\n",
                         self.ini.read_text(encoding="utf-8"))
        self.assertEqual(1, provision.read_host_ini_mode(self.ini))

    def test_stopped_owned_container_uses_direct_ini_restore_and_start(self) -> None:
        commands: list[list[str]] = []
        with patch.object(provision, "container_status", return_value={"status": "exited", "id": "owned"}), \
             patch.object(provision, "run", side_effect=lambda _stage, command: commands.append(command) or ""), \
             patch.object(provision, "wait_port"), \
             patch.object(provision, "wait_official_test_login"):
            result = provision.switch_mode(1)
        self.assertEqual(1, result["enable_encrypt"])
        self.assertEqual(1, provision.read_host_ini_mode(self.ini))
        self.assertEqual([["docker", "start", provision.CONTAINER]], commands)

    def test_stopped_container_cannot_be_switched_to_nonrecovery_mode(self) -> None:
        with patch.object(provision, "container_status", return_value={"status": "exited", "id": "owned"}):
            with self.assertRaises(provision.ProvisionError):
                provision.switch_mode(4)
        self.assertEqual(2, provision.read_host_ini_mode(self.ini))


if __name__ == "__main__":
    unittest.main()
