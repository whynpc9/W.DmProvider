"""Pure offline host-budget contract; no dotnet, credential reads, or database connections."""
import copy
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import Mock, patch

import common
import run


POLICY = 't19-shared-functional-host-v2'


def lane_manifest(profile='shared', lane='functional', timeout=1500):
    entry = {'profile': profile, 'lane': lane, 'root': '/pure-offline/lanes/' + profile + '-' + lane,
             'policy_id': POLICY, 'host_timeout_seconds': timeout}
    return {'budget_policy': common.budget_policy(), 'lanes': {profile + '-' + lane: entry}}, entry


def execution_record():
    manifest, entry = lane_manifest()
    row = {'policy_id': POLICY, 'profile': 'shared', 'lane': 'functional', 'execution_phase': 'test',
           'host_timeout_seconds': 1500, 'timed_out': False, 'host_exit_code': 0, 'exit_code': 0, 'actual_elapsed_seconds': 25.0}
    item = {**entry, 'commands': [row], 'host_exit_code': 0, 'exit_code': 0, 'timed_out': False, 'actual_elapsed_seconds': 25.0}
    return manifest, entry, item


class HostBudgetContractTests(unittest.TestCase):
    def test_complete_profile_matrix_and_unchanged_neighbor_budgets(self):
        # The actual declared matrix must match this approved scope: TLS cannot inherit the shared exception.
        expected = {('shared', 'unit'): 600, ('shared', 'audit'): 90, ('shared', 'functional'): 1500,
                    ('shared', 'cli'): 900, ('shared', 'scripts'): 900, ('shared', 'specification'): 900,
                    ('shared', 'r3_shared'): 900, ('tls', 'unit'): 600, ('tls', 'r3_tls'): 900}
        declared = {('shared', lane) for lane in common.SHARED_LANES} | {('tls', lane) for lane in common.TLS_LANES}
        self.assertEqual(declared, set(expected))
        for (profile, lane), timeout in expected.items():
            with self.subTest(profile=profile, lane=lane):
                manifest, entry = lane_manifest(profile, lane, timeout)
                self.assertEqual(common.verify_lane_budget(manifest, entry, profile + '-' + lane), timeout)
                self.assertEqual(common.command_timeout_seconds(profile, lane, 'audit'), 90)
                self.assertEqual(common.command_timeout_seconds(profile, lane, 'build'), 600)
        self.assertEqual(common.budget_policy()['nested_cli_timeout_seconds'], 300)

    def test_unknown_profile_lane_and_cross_profile_lane_are_rejected(self):
        for profile, lane in [('unknown', 'functional'), ('shared', 'unknown'), ('tls', 'functional'), ('shared', 'r3_tls'), ('tls', 'r3_shared')]:
            with self.subTest(profile=profile, lane=lane), self.assertRaises(ValueError):
                common.host_timeout_seconds(profile, lane)

    def test_tampered_frozen_manifest_fails_before_environment_or_process(self):
        manifest, entry = lane_manifest()
        mutations = [lambda m, e: e.update(host_timeout_seconds=900),
                     lambda m, e: e.update(host_timeout_seconds=1500.0),
                     lambda m, e: e.update(policy_id='old-policy'),
                     lambda m, e: m['budget_policy'].update(shared_functional_host_timeout_seconds=1800),
                     lambda m, e: m['budget_policy'].update(shared_functional_host_timeout_seconds=1500.0),
                     lambda m, e: m.pop('budget_policy')]
        for mutation in mutations:
            current = copy.deepcopy(manifest); lane = current['lanes']['shared-functional']; mutation(current, lane)
            with self.subTest(mutation=mutation), patch.object(run, 'environment') as environment, patch.object(run, 'secrets') as secrets, patch.object(run.subprocess, 'Popen') as popen:
                with self.assertRaises(ValueError):
                    run.worker(Path('/pure-offline'), current, 'shared-functional', Path('/pure-offline/results'))
                environment.assert_not_called(); secrets.assert_not_called(); popen.assert_not_called()

    def test_runtime_timeout_override_rejected_before_process_launch(self):
        for timeout, policy, phase in [(900, POLICY, 'test'), (1800, POLICY, 'test'), (1500.0, POLICY, 'test'),
                                       (1500, 'old-policy', 'test'), (1500, POLICY, 'unknown')]:
            with self.subTest(timeout=timeout, policy=policy, phase=phase), patch.object(run.subprocess, 'Popen') as popen:
                with self.assertRaises(ValueError):
                    run.capture(['must-not-start'], Path('/pure-offline'), {}, Path('/pure-offline/no-log'), timeout,
                                profile='shared', lane='functional', execution_phase=phase, policy_id=policy)
                popen.assert_not_called()

    def test_capture_applies_budget_and_records_actual_process_timing(self):
        process = Mock(returncode=0)
        process.communicate.return_value = (b'offline synthetic stdout\n', b'')
        with tempfile.TemporaryDirectory() as temporary, patch.object(run.subprocess, 'Popen', return_value=process), patch.object(run.time, 'monotonic', side_effect=[10.0, 13.25]):
            row = run.capture(['synthetic-offline-child'], Path(temporary), {}, Path(temporary) / 'safe.log', 1500,
                              profile='shared', lane='functional', execution_phase='test', policy_id=POLICY)
        process.communicate.assert_called_once_with(timeout=1500)
        self.assertEqual((row['policy_id'], row['profile'], row['lane'], row['host_timeout_seconds']), (POLICY, 'shared', 'functional', 1500))
        self.assertEqual((row['host_exit_code'], row['exit_code'], row['timed_out'], row['actual_elapsed_seconds']), (0, 0, False, 3.25))

    def test_timeout_preserves_actual_host_code_and_remains_rejected(self):
        process = Mock(pid=12345, returncode=-9)
        process.communicate.side_effect = [subprocess.TimeoutExpired(['synthetic-offline-child'], 1500), (b'', b'')]
        with tempfile.TemporaryDirectory() as temporary, patch.object(run.subprocess, 'Popen', return_value=process), patch.object(run.os, 'killpg') as kill, patch.object(run.time, 'monotonic', side_effect=[10.0, 1510.1]):
            row = run.capture(['synthetic-offline-child'], Path(temporary), {}, Path(temporary) / 'safe.log', 1500,
                              profile='shared', lane='functional', execution_phase='test', policy_id=POLICY)
        kill.assert_called_once_with(12345, run.signal.SIGKILL)
        self.assertTrue(row['timed_out']); self.assertEqual(row['host_exit_code'], -9); self.assertEqual(row['exit_code'], 124)
        manifest, entry, item = execution_record(); item['commands'] = [row]
        item.update(host_exit_code=-9, exit_code=124, timed_out=True, actual_elapsed_seconds=row['actual_elapsed_seconds'])
        with self.assertRaises(ValueError): common.verify_lane_execution_record(manifest, entry, item)

    def test_validator_reconciles_execution_budget_and_host_outcome(self):
        manifest, entry, item = execution_record()
        common.verify_lane_execution_record(manifest, entry, item)
        mutations = [lambda x: x['commands'][0].update(host_timeout_seconds=900),
                     lambda x: x['commands'][0].update(policy_id='old-policy'),
                     lambda x: x['commands'][0].update(profile='tls'),
                     lambda x: x.update(host_timeout_seconds=900),
                     lambda x: x.update(host_exit_code=7),
                     lambda x: x.update(actual_elapsed_seconds=24.0),
                     lambda x: x['commands'][0].update(actual_elapsed_seconds=float('nan')),
                     lambda x: x['commands'][0].update(timed_out=True),
                     lambda x: x['commands'].append(copy.deepcopy(x['commands'][0])),
                     lambda x: x.update(commands=[])]
        for mutation in mutations:
            current = copy.deepcopy(item); mutation(current)
            with self.subTest(mutation=mutation), self.assertRaises(ValueError):
                common.verify_lane_execution_record(manifest, entry, current)


if __name__ == '__main__':
    unittest.main()
