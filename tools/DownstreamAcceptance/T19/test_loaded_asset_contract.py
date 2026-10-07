"""Offline retained-asset facts; no driver execution, credentials, dotnet, or database."""
import hashlib
from pathlib import Path
import shutil
import struct
import tempfile
import unittest
import uuid

import run
import validate


def module_pe(identifier):
    # Independent tiny ECMA-335 Module metadata vector, not a runnable consumer or cached driver DLL.
    raw = bytearray(1024)
    def u16(offset, value): struct.pack_into('<H', raw, offset, value)
    def u32(offset, value): struct.pack_into('<I', raw, offset, value)
    u32(0x3c, 0x80); raw[0x80:0x84] = b'PE\0\0'; u16(0x86, 1); u16(0x94, 224)
    u16(0x98, 0x10b); u32(0x168, 0x2000)
    u32(0x180, 512); u32(0x184, 0x2000); u32(0x188, 512); u32(0x18c, 0x200)
    u32(0x208, 0x2060)
    root = 0x260; raw[root:root+4] = b'BSJB'; u32(root+12, 12)
    raw[root+16:root+28] = b'v4.0.30319\0\0'
    u16(root+30, 2)
    u32(root+32, 80); u32(root+36, 38); raw[root+40:root+43] = b'#~\0'
    u32(root+44, 128); u32(root+48, 16); raw[root+52:root+58] = b'#GUID\0'
    tables = root+80; struct.pack_into('<Q', raw, tables+8, 1); u32(tables+24, 1)
    u16(tables+32, 1)
    raw[root+128:root+144] = uuid.UUID(identifier).bytes_le
    return bytes(raw)


class LoadedAssetContractTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.asset_root = self.root / 'loaded-drivers'
        run.create_asset_root(self.asset_root)
        self.name = 'loaded-' + 'a' * 32 + '.dll'
        self.identifier = '00112233-4455-6677-8899-aabbccddeeff'
        self.raw = module_pe(self.identifier)
        self.asset = self.asset_root / self.name
        with self.asset.open('xb') as stream:
            self.asset.chmod(0o600); stream.write(self.raw)
        digest = hashlib.sha256(self.raw).hexdigest()
        self.candidate = {'version': '0.1.0-offline-synthetic', 'driver_dll_sha256': digest, 'driver_mvid': self.identifier}
        self.proof = {'schema_version': 2, 'driver_dll_sha256': digest, 'driver_dll_mvid': self.identifier,
                      'retained_driver_asset': self.name, 'retained_driver_sha256': digest,
                      'retained_driver_mvid': self.identifier, 'retained_driver_source': 'loaded_driver_location',
                      'driver_id': 'W.DmProvider', 'driver_version': self.candidate['version'], 'package_library_type': 'package',
                      'loaded_in_host_directory': True}

    def tearDown(self):
        self.temp.cleanup()

    def error(self, code):
        return self.assertRaisesRegex(ValueError, '^' + code + '$')

    def test_cli_temporary_tree_can_be_deleted_while_specific_loaded_asset_remains(self):
        cli = self.root / 'artifacts/t19-cli/generated/bin'
        cli.mkdir(parents=True); (cli / 'W.DmProvider.dll').write_bytes(self.raw)
        shutil.rmtree(self.root / 'artifacts')
        result = run.verify_retained_asset(self.proof, self.candidate, self.asset_root)
        self.assertEqual(result['retained_driver_mvid'], self.identifier)
        self.assertEqual(result['retained_driver_asset'], self.name)
        self.assertFalse((self.root / 'artifacts/t19-cli').exists())

    def test_missing_asset_is_distinct_failure(self):
        self.asset.unlink()
        with self.error('retained_driver_asset_missing'):
            run.verify_retained_asset(self.proof, self.candidate, self.asset_root)

    def test_tampered_bytes_are_rejected_by_independent_sha(self):
        raw = bytearray(self.raw); raw[-1] = 1; self.asset.write_bytes(raw)
        with self.error('retained_driver_asset_hash_mismatch'):
            run.verify_retained_asset(self.proof, self.candidate, self.asset_root)

    def test_claimed_mvid_rejected_by_actual_pe_metadata(self):
        wrong = '11112233-4455-6677-8899-aabbccddeeff'
        self.proof.update(driver_dll_mvid=wrong, retained_driver_mvid=wrong)
        self.candidate['driver_mvid'] = wrong
        with self.error('retained_driver_asset_mvid_mismatch'):
            run.verify_retained_asset(self.proof, self.candidate, self.asset_root)

    def test_path_escape_absolute_and_nested_assets_are_rejected(self):
        for name in ['../' + self.name, str(self.asset), 'nested/' + self.name, 'C:\\' + self.name]:
            with self.subTest(name=name), self.error('retained_driver_asset_path_invalid'):
                run.verify_retained_asset({**self.proof, 'retained_driver_asset': name}, self.candidate, self.asset_root)

    def test_symlink_asset_and_root_are_rejected(self):
        outside = self.root / 'other.dll'; outside.write_bytes(self.raw); outside.chmod(0o600)
        self.asset.unlink(); self.asset.symlink_to(outside)
        with self.error('retained_driver_asset_path_invalid'):
            run.verify_retained_asset(self.proof, self.candidate, self.asset_root)
        link = self.root / 'root-link'; link.symlink_to(self.asset_root, target_is_directory=True)
        with self.error('retained_driver_asset_root_invalid'):
            run.retained_asset_root(self.root, 'root-link')

    def test_existing_asset_root_is_not_reused_or_overwritten(self):
        before = self.asset.read_bytes()
        with self.error('retained_driver_asset_root_exists'):
            run.create_asset_root(self.asset_root)
        self.assertEqual(self.asset.read_bytes(), before)

    def test_schema_or_non_loaded_location_provenance_cannot_replace_loaded_asset(self):
        for changed in [{'schema_version': 1}, {'retained_driver_source': 'nuget_cache'}]:
            with self.subTest(changed=changed), self.error('retained_driver_asset_source_mismatch'):
                run.verify_retained_asset({**self.proof, **changed}, self.candidate, self.asset_root)

    def test_actual_proof_files_must_match_summary_before_retained_asset_validation(self):
        import json
        evidence = self.root / 'results-offline'; evidence.mkdir(mode=0o700)
        self.asset_root.rename(evidence / 'loaded-drivers')
        raw_path = evidence / 'testhost-driver.json'; raw_path.write_text(json.dumps(self.proof)); raw_path.chmod(0o600)
        (evidence / 'hosts').mkdir(mode=0o700)
        item = {'testhost_proofs': [self.proof], 'nested_cli_proofs': [], 'retained_asset_root': 'results-offline/loaded-drivers'}
        files = validate.reread_host_proofs(evidence, item, self.candidate, retain=True)
        self.assertEqual(len(files), 1)
        self.assertEqual(files[0]['sha256'], hashlib.sha256(raw_path.read_bytes()).hexdigest())
        item['testhost_proofs'] = [{**self.proof, 'retained_driver_asset': 'loaded-' + 'b'*32 + '.dll'}]
        with self.error('host_proof_file_summary_mismatch'):
            validate.reread_host_proofs(evidence, item, self.candidate, retain=True)

    def test_original_schema_one_proof_is_reread_without_fabricating_retained_asset(self):
        import json
        evidence = self.root / 'results-original'; evidence.mkdir(mode=0o700)
        legacy = {key: value for key, value in self.proof.items() if not key.startswith('retained_driver_')}
        legacy['schema_version'] = 1
        raw_path = evidence / 'testhost-driver.json'; raw_path.write_text(json.dumps(legacy)); raw_path.chmod(0o600)
        (evidence / 'hosts').mkdir(mode=0o700)
        item = {'testhost_proofs': [legacy], 'nested_cli_proofs': []}
        self.assertEqual(len(validate.reread_host_proofs(evidence, item, self.candidate, retain=False)), 1)
        self.assertFalse((evidence / 'loaded-drivers').exists())
        altered = {**legacy, 'driver_dll_sha256': '0'*64}
        raw_path.write_text(json.dumps(altered))
        with self.error('host_proof_file_summary_mismatch'):
            validate.reread_host_proofs(evidence, item, self.candidate, retain=False)

    def test_actual_nested_host_specific_asset_is_mandatory_after_temp_deletion(self):
        import json
        evidence = self.root / 'results-nested'; evidence.mkdir(mode=0o700)
        self.asset_root.rename(evidence / 'loaded-drivers')
        assets = evidence / 'loaded-drivers'
        nested_name = 'loaded-' + 'b'*32 + '.dll'
        nested_asset = assets / nested_name; nested_asset.write_bytes(self.raw); nested_asset.chmod(0o600)
        host = {**self.proof, 'host_assembly': 'FunctionalTests'}
        nested = {**self.proof, 'host_assembly': 'CliRoundtrip', 'retained_driver_asset': nested_name}
        path = evidence / 'testhost-driver.json'; path.write_text(json.dumps(host)); path.chmod(0o600)
        (evidence / 'hosts').mkdir(mode=0o700)
        path = evidence / 'hosts/cli-offline.json'; path.write_text(json.dumps(nested)); path.chmod(0o600)
        item = {'testhost_proofs': [host], 'nested_cli_proofs': [nested], 'retained_asset_root': 'results-nested/loaded-drivers'}
        self.assertEqual(len(validate.reread_host_proofs(evidence, item, self.candidate, retain=True)), 2)
        nested_asset.unlink()
        with self.error('retained_driver_asset_missing'):
            validate.reread_host_proofs(evidence, item, self.candidate, retain=True)

    def test_summary_record_cannot_replace_original_lane_file(self):
        import json
        evidence = self.root / 'results-original'; evidence.mkdir(mode=0o700)
        item = {'profile': 'shared', 'lane': 'unit', 'status': 'accepted', 'exit_code': 0}
        path = evidence / 'lane.json'; path.write_text(json.dumps(item)); path.chmod(0o600)
        found, digest = validate.reread_lane_record(self.root, item)
        self.assertEqual(found, evidence)
        self.assertEqual(digest, hashlib.sha256(path.read_bytes()).hexdigest())
        with self.error('immutable_lane_record_summary_mismatch'):
            validate.reread_lane_record(self.root, {**item, 'exit_code': 1})


if __name__ == '__main__':
    unittest.main()
