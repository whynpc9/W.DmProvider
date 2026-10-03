"""Consumer provenance tests; no dotnet or database, explicit Git-free source fixtures."""
import hashlib
import importlib.util
import json
from pathlib import Path
import stat
import subprocess
import tempfile
import unittest
from unittest.mock import patch

import source_identity as identity


class SourceIdentityTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.base = Path(self.temp.name).resolve()
        self.repo = self.base / 'frozen'
        self.repo.mkdir()
        for name in identity.REQUIRED_FILES:
            path = self.repo / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(('fixture:' + name + '\n').encode())
            path.chmod(0o755 if name.endswith('.sh') else 0o644)
        self.manifest = self.base / 'producer.json'
        self.value = {'schema_version': 1, 'task': 'T19', 'head': 'a' * 40, 'source_link': False,
                      'validation_source_scope': 'all_current_validation_source_including_untracked', 'files': self.rows()}
        self.save()

    def tearDown(self):
        self.temp.cleanup()

    def rows(self):
        result = []
        for name in identity.REQUIRED_FILES:
            path = self.repo / name
            result.append({'path': name, 'sha256': identity.file_sha(path), 'size': path.stat().st_size,
                           'executable': bool(path.stat().st_mode & stat.S_IXUSR)})
        return result

    def save(self):
        self.value['tree_sha256'] = hashlib.sha256(identity.encoded(self.value['files'])).hexdigest()
        self.manifest.write_bytes(identity.encoded(self.value))
        self.sha = identity.file_sha(self.manifest)

    def record(self):
        return identity.record_sources(self.repo, producer_manifest=self.manifest, producer_manifest_sha256=self.sha)

    def test_git_free_manifest_records_bound_producer_head_without_observed_git_claim(self):
        with patch.object(identity.subprocess, 'run', side_effect=AssertionError('Git must not execute')):
            result = self.record()
        self.assertEqual('verified_producer_source_manifest', result['source_origin'])
        self.assertIsNone(result['observed_git_head'])
        self.assertEqual('a' * 40, result['producer_git_head'])
        self.assertEqual(self.sha, result['producer_source_manifest_sha256'])
        self.assertEqual(set(identity.REQUIRED_FILES), set(result['files']))
        for name in ('ResourceSoak.cs', 'DiagnosticListenerCapture.cs', 'Program.cs'):
            path = 'tools/R3ResourceProbe/' + name
            self.assertEqual(identity.file_sha(self.repo / path), result['files'][path])
            self.assertIn('size', result['file_metadata'][path])
            self.assertIn('executable', result['file_metadata'][path])

    def test_manifest_tamper_or_wrong_supplied_hash_fails_no_git_fallback(self):
        self.manifest.write_bytes(self.manifest.read_bytes() + b' ')
        with patch.object(identity.subprocess, 'run', side_effect=AssertionError('No fallback')):
            with self.assertRaisesRegex(identity.SourceIdentityError, 'manifest_hash_mismatch'): self.record()
        with self.assertRaisesRegex(identity.SourceIdentityError, 'manifest_hash_mismatch'):
            identity.record_sources(self.repo, producer_manifest=self.manifest, producer_manifest_sha256='0' * 64)

    def test_missing_manifest_or_missing_explicit_hash_is_rejected(self):
        with self.assertRaisesRegex(identity.SourceIdentityError, 'path_and_hash_required'):
            identity.record_sources(self.repo, producer_manifest=self.manifest)
        self.manifest.unlink()
        with self.assertRaisesRegex(identity.SourceIdentityError, 'manifest_missing'): self.record()

    def test_missing_or_tampered_compile_helper_never_silently_omitted(self):
        path = self.repo / 'tools/R3ResourceProbe/ResourceSoak.cs'
        path.write_bytes(b'changed helper')
        with self.assertRaisesRegex(identity.SourceIdentityError, 'source_not_bound'): self.record()
        path.unlink()
        with self.assertRaisesRegex(identity.SourceIdentityError, 'required_consumer_source_missing'): self.record()

    def test_manifest_size_hash_execute_and_missing_row_bindings_fail(self):
        original = json.loads(json.dumps(self.value))
        for key, value in [('size', 999999), ('sha256', '0' * 64), ('executable', True)]:
            self.value = json.loads(json.dumps(original))
            row = next(row for row in self.value['files'] if row['path'].endswith('DiagnosticListenerCapture.cs'))
            row[key] = value; self.save()
            with self.subTest(key=key), self.assertRaisesRegex(identity.SourceIdentityError, 'source_not_bound'): self.record()
        self.value = original
        self.value['files'] = [row for row in self.value['files'] if not row['path'].endswith('DiagnosticListenerCapture.cs')]
        self.save()
        with self.assertRaisesRegex(identity.SourceIdentityError, 'source_not_bound'): self.record()

    def test_actual_execute_bit_change_is_rejected(self):
        path = self.repo / 'tools/R3ResourceProbe/Program.cs'; path.chmod(0o755)
        with self.assertRaisesRegex(identity.SourceIdentityError, 'source_not_bound'): self.record()

    def test_schema_tree_and_head_must_validate_even_when_file_hash_is_explicit(self):
        original = json.loads(json.dumps(self.value))
        for key, value in [('task', 'Other'), ('head', 'unknown'), ('source_link', True), ('schema_version', 2), ('schema_version', True),
                           ('tree_sha256', '0' * 64)]:
            self.value = json.loads(json.dumps(original)); self.value[key] = value
            self.manifest.write_bytes(identity.encoded(self.value)); self.sha = identity.file_sha(self.manifest)
            with self.subTest(key=key), self.assertRaises(identity.SourceIdentityError): self.record()

    def test_manifest_path_escape_duplicate_and_noncanonical_paths_rejected(self):
        original = json.loads(json.dumps(self.value))
        for name in ['../outside.cs', '/outside.cs', 'tools/../outside.cs', 'tools//outside.cs', 'tools\\outside.cs']:
            self.value = json.loads(json.dumps(original)); self.value['files'][0]['path'] = name; self.save()
            with self.subTest(name=name), self.assertRaises(identity.SourceIdentityError): self.record()
        self.value = original; self.value['files'].append(dict(self.value['files'][0])); self.save()
        with self.assertRaisesRegex(identity.SourceIdentityError, 'duplicate_path'): self.record()

    def test_symlink_manifest_and_helper_rejected(self):
        other = self.base / 'other.json'; self.manifest.rename(other); self.manifest.symlink_to(other)
        with self.assertRaisesRegex(identity.SourceIdentityError, 'manifest_missing_or_symlink'): self.record()
        self.manifest.unlink(); other.rename(self.manifest)
        source = self.repo / 'tools/R3ResourceProbe/ResourceSoak.cs'; target = self.base / 'helper.cs'
        source.rename(target); source.symlink_to(target)
        with self.assertRaisesRegex(identity.SourceIdentityError, 'source_symlink_rejected'): self.record()

    def test_git_absence_without_explicit_manifest_is_strict_failure(self):
        with patch.object(identity.subprocess, 'run', side_effect=subprocess.CalledProcessError(128, ['git'])):
            with self.assertRaisesRegex(identity.SourceIdentityError, 'live_git_head_required'):
                identity.record_sources(self.repo)

    def test_live_git_mode_only_records_real_observed_head(self):
        with patch.object(identity.subprocess, 'run', return_value=subprocess.CompletedProcess(['git'], 0, stdout=b'b' * 40 + b'\n')) as git:
            result = identity.record_sources(self.repo)
        self.assertEqual('live_git', result['source_origin'])
        self.assertEqual('b' * 40, result['observed_git_head'])
        self.assertIsNone(result['producer_git_head'])
        self.assertIsNone(result['producer_source_manifest_sha256'])
        git.assert_called_once()

    def test_candidate_environment_passes_actual_manifest_path_and_hash_to_natural_child_inheritance(self):
        path = Path(__file__).resolve().parents[1] / 'R3CandidateGate/gate.py'
        spec = importlib.util.spec_from_file_location('candidate_environment_contract', path)
        gate = importlib.util.module_from_spec(spec); spec.loader.exec_module(gate)
        run = self.base / 'candidate'
        (run / 'private').mkdir(parents=True); (run / 'public').mkdir()
        copied = run / 'public/source-manifest.json'; copied.write_bytes(self.manifest.read_bytes())
        with patch.dict(gate.os.environ, {'DAMENG_FAKE_CREDENTIAL': 'synthetic', identity.PRODUCER_PATH_ENV: 'stale',
                                         identity.PRODUCER_SHA_ENV: '0' * 64}):
            env = gate.environment(run, '0.1.0-r3.20261003000000')
        self.assertEqual(str(copied.resolve()), env[identity.PRODUCER_PATH_ENV])
        self.assertEqual(identity.file_sha(copied), env[identity.PRODUCER_SHA_ENV])
        self.assertNotIn('DAMENG_FAKE_CREDENTIAL', env)
        # Release audit environment copies/inherits these R3_ values unchanged.
        result = identity.record_sources(self.repo, producer_manifest=env[identity.PRODUCER_PATH_ENV],
                                          producer_manifest_sha256=env[identity.PRODUCER_SHA_ENV])
        self.assertIsNone(result['observed_git_head'])


if __name__ == '__main__':
    unittest.main()
