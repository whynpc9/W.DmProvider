"""Pure-Python overlay contract tests; fixtures never build/connect/read secrets."""
import json
from pathlib import Path
import shutil
import tarfile
import tempfile
import unittest
from unittest.mock import patch

import execution_overlay as overlay

TOOL = overlay.PREFIX + 'run.py'
MODULE = overlay.PREFIX + 'execution_overlay.py'


class ExecutionOverlayTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.base = Path(self.temporary.name).resolve()
        self.producer = self.base / 'producer'
        self.execution = self.base / 'execution'
        self.output = self.base / 'artifacts'
        for directory in (self.producer, self.execution, self.output):
            directory.mkdir(mode=0o700)
        for path, value in [('src/provider.cs', b'original-product\n'), ('tests/original.cs', b'original-test\n'),
                            ('global.json', b'{"sdk":{"version":"10.0.203"}}\n'),
                            ('docs/report.md', b'frozen-report\n'), (TOOL, b'old-runner\n')]:
            target = self.producer / path
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(value)
            target.chmod(0o644)
        shutil.copytree(self.producer, self.execution, dirs_exist_ok=True)
        full = overlay.inventory(self.producer)
        files = [{'path': row['path'], 'size': row['size'], 'sha256': row['sha256'], 'executable': False} for row in full]
        value = {'schema_version': 1, 'task': 'T19', 'files': files, 'tree_sha256': overlay.digest(overlay.canonical(files))}
        self.manifest = self.base / 'source-manifest.json'
        self.manifest.write_bytes(overlay.canonical(value))
        # Only this fixture's manifest digest is substituted. Revision, exact tool
        # allowlist and candidate package/DLL/MVID hard pins remain production values.
        self.bindings = dict(overlay.CANDIDATE)
        self.bindings['source_manifest_sha256'] = overlay.file_sha(self.manifest)
        self.pin_patch = patch.object(overlay, 'CANDIDATE', self.bindings)
        self.pin_patch.start()
        (self.execution / TOOL).write_bytes(b'explicit-new-runner\n')
        self.declared = [{'path': TOOL, 'action': 'modify'}]

    def tearDown(self):
        self.pin_patch.stop()
        self.temporary.cleanup()

    def create(self, declarations=None):
        return overlay.create_manifest(producer_manifest_path=self.manifest, producer_source_dir=self.producer,
            execution_source_dir=self.execution, candidate_bindings=self.bindings,
            declared_changes=self.declared if declarations is None else declarations, output_dir=self.output)

    def verify(self):
        return overlay.validate_manifest(manifest_path=self.output / overlay.OVERLAY_NAME,
            producer_manifest_path=self.manifest, producer_source_dir=self.producer, execution_source_dir=self.execution,
            expected_candidate_bindings=self.bindings)

    def rewrite_overlay(self, mutate):
        path = self.output / overlay.OVERLAY_NAME
        value = json.loads(path.read_bytes())
        mutate(value)
        path.write_bytes(overlay.canonical(value))

    def test_explicit_tools_only_archive_round_trip_and_relocation(self):
        result = self.create()
        self.assertEqual(overlay.REVISION, result['revision'])
        self.assertEqual(str(self.execution / overlay.PREFIX), result['execution_tool_root'])
        self.assertEqual(overlay.file_sha(self.output / overlay.OVERLAY_NAME), result['execution_overlay_sha256'])
        relocated = self.base / 'relocated'
        shutil.copytree(self.execution, relocated)
        value = overlay.validate_manifest(manifest_path=self.output / overlay.OVERLAY_NAME,
            producer_manifest_path=self.manifest, producer_source_dir=self.producer, execution_source_dir=relocated,
            expected_candidate_bindings=self.bindings)
        self.assertEqual(str(relocated), value['execution_source_dir'])

    def test_explicit_add_records_no_old_file(self):
        (self.execution / MODULE).write_bytes(b'new-declared-module\n')
        (self.execution / MODULE).chmod(0o644)
        self.create(self.declared + [{'path': MODULE, 'action': 'add'}])
        value = json.loads((self.output / overlay.OVERLAY_NAME).read_bytes())
        row = next(row for row in value['changes'] if row['path'] == MODULE)
        self.assertIsNone(row['old'])
        self.assertEqual('add', row['action'])
        self.assertEqual(0o644, row['new']['mode'])

    def test_product_change_cannot_be_declared_or_hidden(self):
        (self.execution / 'src/provider.cs').write_bytes(b'changed-product\n')
        with self.assertRaisesRegex(overlay.OverlayError, 'undeclared_difference'):
            self.create()
        with self.assertRaisesRegex(overlay.OverlayError, 'frozen_allowlist'):
            self.create(self.declared + [{'path': 'src/provider.cs', 'action': 'modify'}])

    def test_test_build_and_docs_changes_never_whitelisted(self):
        for path in ['tests/original.cs', 'global.json', 'docs/report.md']:
            with self.subTest(path=path), self.assertRaisesRegex(overlay.OverlayError, 'frozen_allowlist'):
                overlay.declared_map([{'path': path, 'action': 'modify'}])

    def test_missing_declaration_and_unused_declaration_fail(self):
        with self.assertRaisesRegex(overlay.OverlayError, 'explicit_declaration'):
            self.create([])
        with self.assertRaisesRegex(overlay.OverlayError, 'undeclared_difference_or_unused'):
            self.create(self.declared + [{'path': MODULE, 'action': 'add'}])

    def test_undeclared_added_source_and_deletion_fail(self):
        path = self.execution / 'tools/extra.py'
        path.write_bytes(b'undeclared-tool\n')
        with self.assertRaisesRegex(overlay.OverlayError, 'undeclared_difference'):
            self.create()
        path.unlink()
        (self.execution / 'tests/original.cs').unlink()
        with self.assertRaisesRegex(overlay.OverlayError, 'source_deletion'):
            self.create()

    def test_nonallowlisted_mode_change_fails_without_byte_changes(self):
        (self.execution / 'src/provider.cs').chmod(0o600)
        with self.assertRaisesRegex(overlay.OverlayError, 'undeclared_difference'):
            self.create()

    def test_postfreeze_hash_and_mode_changes_fail_validation(self):
        self.create()
        path = self.execution / TOOL
        old = path.read_bytes()
        path.write_bytes(b'late-modification\n')
        with self.assertRaisesRegex(overlay.OverlayError, 'execution_source_bytes_or_modes'):
            self.verify()
        path.write_bytes(old)
        path.chmod(0o755)
        with self.assertRaisesRegex(overlay.OverlayError, 'execution_source_bytes_or_modes'):
            self.verify()

    def test_declared_old_hash_and_new_mode_tamper_fail(self):
        self.create()
        original = (self.output / overlay.OVERLAY_NAME).read_bytes()
        for location, key, value in [('old', 'sha256', '0' * 64), ('new', 'mode', 0o777)]:
            (self.output / overlay.OVERLAY_NAME).write_bytes(original)
            self.rewrite_overlay(lambda manifest: manifest['changes'][0][location].__setitem__(key, value))
            with self.subTest(key=key), self.assertRaisesRegex(overlay.OverlayError, 'declared_old_new_hash_or_mode'):
                self.verify()

    def test_manifest_candidate_and_allowlist_tamper_fail(self):
        self.create()
        original = (self.output / overlay.OVERLAY_NAME).read_bytes()
        for mutation in [lambda value: value['candidate'].__setitem__('driver_mvid', '0' * 36),
                         lambda value: value['allowlist'].append('src/provider.cs')]:
            (self.output / overlay.OVERLAY_NAME).write_bytes(original)
            self.rewrite_overlay(mutation)
            with self.assertRaisesRegex(overlay.OverlayError, 'pins_or_allowlist'):
                self.verify()

    def test_paths_escape_absolute_noncanonical_and_symlink_fail(self):
        for name in ['../outside.py', '/tmp/outside.py', 'tools/../outside.py', 'tools//other.py', 'tools\\other.py']:
            with self.subTest(name=name), self.assertRaises(overlay.OverlayError):
                overlay.declared_map([{'path': name, 'action': 'add'}])
        (self.execution / 'tools/symlink.py').symlink_to(self.producer / TOOL)
        with self.assertRaisesRegex(overlay.OverlayError, 'symlink_rejected'):
            self.create()

    def test_archive_corruption_and_artifact_path_escape_fail(self):
        self.create()
        archive = self.output / overlay.ARCHIVE_NAME
        original = archive.read_bytes()
        archive.write_bytes(b'corrupt-archive')
        with self.assertRaisesRegex(overlay.OverlayError, 'artifact_hash'):
            self.verify()
        archive.write_bytes(original)
        self.rewrite_overlay(lambda value: value.__setitem__('execution_source_archive', '../elsewhere.tar.gz'))
        with self.assertRaisesRegex(overlay.OverlayError, 'artifact_path_escape'):
            self.verify()

    def test_archive_symlink_member_rejected_even_with_updated_archive_hash(self):
        self.create()
        archive = self.output / overlay.ARCHIVE_NAME
        with tarfile.open(archive, 'w:gz') as stream:
            link = tarfile.TarInfo(TOOL)
            link.type, link.linkname = tarfile.SYMTYPE, '../outside'
            stream.addfile(link)
        self.rewrite_overlay(lambda value: value.__setitem__('execution_source_archive_sha256', overlay.file_sha(archive)))
        with self.assertRaisesRegex(overlay.OverlayError, 'archive_unsafe'):
            self.verify()

    def test_version_candidate_binding_and_producer_hash_fail(self):
        wrong = dict(self.bindings, version='0.1.0-r7.invalid')
        with self.assertRaisesRegex(overlay.OverlayError, 'candidate_tuple'):
            overlay.pins(wrong)
        self.manifest.write_bytes(b'changed-producer-manifest')
        with self.assertRaisesRegex(overlay.OverlayError, 'producer_manifest_hash'):
            self.create()

    def test_output_is_create_new_never_overwritten(self):
        self.create()
        before = (self.output / overlay.OVERLAY_NAME).read_bytes()
        with self.assertRaisesRegex(overlay.OverlayError, 'new_output'):
            self.create()
        self.assertEqual(before, (self.output / overlay.OVERLAY_NAME).read_bytes())

    def test_output_cannot_mutate_producer_or_frozen_execution_inputs(self):
        for tree in [self.producer, self.execution]:
            output = tree / 'docs/artifacts'
            output.mkdir()
            with self.subTest(tree=tree.name), self.assertRaisesRegex(overlay.OverlayError, 'artifact_output_inside'):
                overlay.create_manifest(producer_manifest_path=self.manifest, producer_source_dir=self.producer,
                    execution_source_dir=self.execution, candidate_bindings=self.bindings,
                    declared_changes=self.declared, output_dir=output)
            self.assertFalse(list(output.iterdir()))


if __name__ == '__main__':
    unittest.main()
