"""Failure evidence contracts; no builds, credentials or database calls."""
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

import gate


class AuditFailureEvidenceTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.run = Path(self.temp.name)
        (self.run / 'public').mkdir()
        self.audit = self.run / 'private/audit/unique/public'
        self.audit.mkdir(parents=True)

    def tearDown(self):
        self.temp.cleanup()

    def summary(self, reason='Configuration_test_failed'):
        gate.write(self.audit / 'summary.json', {'status': 'rejected', 'reason': reason, 'production_release_accepted': False})

    def command(self, stage='Configuration_test', code=1):
        gate.write(self.audit / (stage + '-command.json'), {'stage': stage, 'argv': ['dotnet', 'test', 'frozen.csproj'],
            'exit_code': code, 'elapsed_seconds': 2.0})

    def run_failed(self, code=1):
        with patch.object(gate, 'run_command', side_effect=gate.Reject('full_twelve_suites_no_build_pack_seven_children_failed', code)):
            return gate.run_audit_with_evidence(['offline'], self.run, {}, self.run)

    def test_nonzero_keeps_exact_safe_inner_reason_and_stays_failed(self):
        self.summary('Configuration_restore_failed'); self.command('Configuration_restore', 17)
        with self.assertRaises(gate.Reject) as caught:
            self.run_failed(17)
        self.assertEqual(17, caught.exception.code)
        self.assertEqual('inner_audit_Configuration_restore_failed', caught.exception.reason)
        self.assertEqual('Configuration_restore_failed', caught.exception.details['reject_reason'])
        self.assertEqual([{'stage': 'Configuration_restore', 'exit_code': 17}], caught.exception.details['failed_commands'])
        self.assertTrue((self.run / 'public/offline-summary.json').is_file())
        self.assertTrue((self.run / 'public/offline-Configuration_restore-command.json').is_file())

    def test_safe_trx_retained_but_private_logs_and_unselected_controls_never_copied(self):
        self.summary(); self.command()
        (self.audit / 'Session.trx').write_text('<TestRun><Results><UnitTestResult outcome="Passed"/></Results></TestRun>')
        (self.audit / 'raw-output.log').write_text('not for publishing')
        (self.audit.parent / '.private').mkdir()
        (self.audit.parent / '.private/auth.log').write_text('never for publishing')
        (self.audit / 'arbitrary-source.json').write_text(json.dumps({'control': gate.MARKERS[0]}))
        with self.assertRaises(gate.Reject): self.run_failed()
        self.assertEqual({'offline-summary.json', 'offline-Configuration_test-command.json', 'offline-Session.trx'},
                         {path.name for path in (self.run / 'public').iterdir()})

    def test_synthetic_marker_batch_is_rejected_before_any_publication(self):
        self.summary(); self.command()
        (self.audit / 'Transport.trx').write_text('<TestRun><Results>' + gate.MARKERS[0] + '</Results></TestRun>')
        with self.assertRaisesRegex(gate.Reject, 'inner_audit_public_evidence_rejected'):
            self.run_failed()
        self.assertFalse(list((self.run / 'public').iterdir()))

    def test_escaped_marker_and_unsanitized_trx_output_rejected(self):
        self.summary(); self.command()
        path = self.audit / 'Session.trx'
        for xml in ['<TestRun><Results>SYN&#84;HETIC_USER_SECRET</Results></TestRun>',
                    '<TestRun><Output><StdOut>arbitrary failure output</StdOut></Output></TestRun>']:
            path.write_text(xml)
            with self.subTest(xml=xml), self.assertRaisesRegex(gate.Reject, 'inner_audit_public_evidence_rejected'):
                self.run_failed()
            self.assertFalse(list((self.run / 'public').iterdir()))

    def test_no_inner_report_is_stable_safe_fallback_and_not_success(self):
        self.audit.rmdir()
        with self.assertRaises(gate.Reject) as caught: self.run_failed(124)
        self.assertEqual('inner_audit_report_missing', caught.exception.reason)
        self.assertEqual(124, caught.exception.code)
        self.assertEqual('report_missing', caught.exception.details['evidence_status'])

    def test_missing_summary_preserves_known_failed_command_stage(self):
        self.command('exact_package_pack', 1)
        with self.assertRaises(gate.Reject) as caught: self.run_failed()
        self.assertEqual('inner_audit_exact_package_pack_failed', caught.exception.reason)
        self.assertEqual('summary_missing', caught.exception.details['evidence_status'])

    def test_generic_inner_failure_keeps_actual_last_recorded_suite_without_guessing_cause(self):
        self.summary('strict_gate_failed'); self.command('Configuration_test', 0); self.command('Pool_test', 0)
        with self.assertRaises(gate.Reject) as caught: self.run_failed()
        self.assertEqual('inner_audit_strict_gate_failed', caught.exception.reason)
        self.assertEqual('Pool_test', caught.exception.details['last_recorded_stage'])
        self.assertEqual([], caught.exception.details['failed_commands'])

    def test_unknown_reason_credentials_and_symlink_are_not_exported(self):
        self.summary('arbitrary_untrusted_reason')
        with self.assertRaisesRegex(gate.Reject, 'inner_audit_public_evidence_rejected'): self.run_failed()
        self.summary(); self.command()
        command = self.audit / 'Configuration_test-command.json'
        value = json.loads(command.read_text()); value['argv'].append('/p:ConnectionString=Password=untrusted')
        command.write_text(json.dumps(value))
        with self.assertRaisesRegex(gate.Reject, 'inner_audit_public_evidence_rejected'): self.run_failed()
        self.assertFalse(list((self.run / 'public').iterdir()))
        command.unlink(); command.symlink_to(self.audit / 'summary.json')
        with self.assertRaisesRegex(gate.Reject, 'inner_audit_public_evidence_rejected'): self.run_failed()

    def test_false_inner_success_cannot_override_nonzero_host(self):
        gate.write(self.audit / 'summary.json', {'status': 'offline_verified', 'production_release_accepted': False})
        self.command('runtime_offline', 1)
        with self.assertRaises(gate.Reject) as caught: self.run_failed()
        self.assertEqual('inner_audit_runtime_offline_failed', caught.exception.reason)

    def test_summary_copy_is_exact_and_verified_success_keeps_parent_success_route(self):
        gate.write(self.audit / 'summary.json', {'status': 'offline_verified', 'production_release_accepted': False,
                                               'candidate_version': '0.1.0-r3.20261003000000', 'suites': []})
        with patch.object(gate, 'run_command', return_value=self.run / 'private/log'):
            result = gate.run_audit_with_evidence(['offline'], self.run, {}, self.run)
        self.assertEqual('safe_published', result['evidence_status'])
        self.assertEqual((self.audit / 'summary.json').read_bytes(), (self.run / 'public/offline-summary.json').read_bytes())


if __name__ == '__main__':
    unittest.main()
