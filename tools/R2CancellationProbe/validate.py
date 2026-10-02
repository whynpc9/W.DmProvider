#!/usr/bin/env python3
"""Validate exact package identity and explicit T14 outcomes; never echo raw errors."""
import hashlib
import json
from pathlib import Path
import sys


def require(value):
    if not value:
        raise ValueError('t14_envelope_invalid')


def failure(value, kind, source, transaction='OutcomeUnknown', phase=None):
    expected_type = 'W.Dm.DmTimeoutException' if kind == 'Timeout' else 'W.Dm.DmOperationCanceledException'
    require(value.get('type') == expected_type and value.get('number') == (0 if kind == 'Timeout' else None))
    info = value.get('failure_info', {})
    require(info.get('error_kind') == kind and info.get('cancel_source') == source and
            info.get('error_code') == ('WDM_TIMEOUT' if kind == 'Timeout' else 'WDM_CANCELED') and
            info.get('operation_outcome') == 'Unknown' and info.get('transaction_outcome') == transaction and
            info.get('connection_reusable') is False and info.get('server_error_number') is None and
            info.get('phase') in (phase or ('Send', 'Receive', 'Fetch')))


def validate_cases(report):
    cases = report['cases']
    expected = {'pre_cancel_and_idle_noop', 'rowlock_user', 'rowlock_command', 'rowlock_deadline',
                'returned_reader_token_scope', 'idle_reader_cancel_captured_session', 'commit_sent_unknown',
                'commit_validated_ack_late_cancel'}
    if report['mode'] == 'shared':
        expected.update(('performance_8', 'performance_16'))
    require(set(cases) == expected)
    pre = cases['pre_cancel_and_idle_noop']
    require(pre.get('sends') == 0 and pre.get('active_preserved') is True and pre.get('connection_reusable') is True and
            pre.get('no_active_cancel_noop') is True and pre.get('new_token_query') == 1)
    for key in ('command_error', 'commit_error'):
        require(pre[key]['type'] in ('System.OperationCanceledException', 'W.Dm.DmOperationCanceledException'))
    for name, kind, source in (('user', 'Canceled', 'User'), ('command', 'Canceled', 'Command'), ('deadline', 'Timeout', 'TotalDeadline')):
        row = cases['rowlock_' + name]
        require(row.get('pending_after_numeric_send') is True and row.get('execution_frames') == 1 and
                row.get('automatic_reconnections') == 0 and row.get('connection_state') == 'Broken' and
                row.get('transaction_outcome') == 'OutcomeUnknown' and row.get('fresh_final_value') == 0 and
                row.get('token_contract_verified') is True and row.get('server_immediate_stop_claimed') is False)
        failure(row['failure'], kind, source, phase=('Send', 'Receive'))
    reader = cases['returned_reader_token_scope']
    require(reader.get('rows') == 2048 and reader.get('cached_first_read') is True and reader.get('actual_fetch_frames', 0) > 0 and
            reader.get('old_execute_token_canceled') is True and reader.get('new_read_token_usable') is True)
    idle = cases['idle_reader_cancel_captured_session']
    require(idle.get('future_read_failed') is True and idle.get('old_session_replaced') is True and
            idle.get('stale_cancel_and_dispose_new_session_usable') is True)
    failure(idle['failure'], 'Canceled', 'Command', transaction=None, phase=('Fetch',))
    unknown = cases['commit_sent_unknown']
    require(unknown.get('boundary') == 'sent_before_response_read' and unknown.get('commit_frames') == 1 and
            unknown.get('automatic_reconnections') == 0 and unknown.get('automatic_replay_performed') is False and
            unknown.get('transaction_outcome') == 'OutcomeUnknown' and unknown.get('connection_state') == 'Broken' and
            type(unknown.get('fresh_final_value')) is int and unknown['fresh_final_value'] in (0, 1) and
            unknown.get('failure', {}).get('type') == 'W.Dm.DmCommitOutcomeUnknownException')
    info = unknown['failure'].get('failure_info', {})
    require(info.get('error_kind') == 'OutcomeUnknown' and info.get('error_code') == 'WDM_COMMIT_UNKNOWN' and
            info.get('phase') == 'Commit' and info.get('operation_outcome') == 'Unknown' and
            info.get('transaction_outcome') == 'OutcomeUnknown' and info.get('connection_reusable') is False and
            info.get('cancel_source') == 'User' and info.get('server_error_number') is None)
    ack = cases['commit_validated_ack_late_cancel']
    require(ack.get('boundary') == 'validated_success_before_ack_confirmation' and ack.get('commit_frames') == 1 and
            ack.get('automatic_reconnections') == 0 and ack.get('automatic_replay_performed') is False and
            ack.get('transaction_outcome') == 'Committed' and ack.get('fresh_final_value') == 1 and ack.get('failure') is None)
    if report['mode'] == 'shared':
        for n in (8, 16):
            perf = cases['performance_' + str(n)]
            require(perf.get('Connections') == n and perf.get('AllPendingAfterNumericSend') is True and
                    0 <= perf.get('BusyWorkerGrowth', n) < n and perf.get('ExecutionFrames') == n and
                    perf.get('UnknownCanceledOperations') == n and perf.get('FreshNonzeroRows') == 0 and
                    perf.get('ManagedAllocatedBytes', -1) >= 0 and perf.get('WaitingElapsedMilliseconds', 0) >= 250 and
                    perf.get('CompletionElapsedMilliseconds', 0) >= perf['WaitingElapsedMilliseconds'] and
                    perf.get('CompletedOperationsPerSecond', 0) > 0 and
                    perf.get('ThreadCountWaiting', 0) >= perf.get('ThreadCountBefore', 0) and
                    perf.get('AvailableWorkersWaiting', -1) >= 0)
        require(cases['performance_16']['BusyWorkerGrowth'] - cases['performance_8']['BusyWorkerGrowth'] < 8 and
                report.get('performance_scope') == 'measured_8_and_16_independent_connections')
    else:
        require(report.get('performance_scope') == 'shared_profile_only')


def main():
    report = json.loads(Path(sys.argv[1]).read_text())
    package = json.loads(Path(sys.argv[2]).read_text())
    loaded = hashlib.sha256(Path(sys.argv[3]).read_bytes()).hexdigest()
    mode = sys.argv[4]
    require(mode in ('shared', 'tls') and report.get('mode') == mode and report.get('schema_version') == 1 and
            report.get('task') == 'T14' and report.get('implementation') == 'W-package' and report.get('accepted') is True and
            report.get('exit_code') == 0 and report.get('status') == 'package_cancellation_verified' and
            report.get('package_version') == package['version'] and report.get('package_sha256') == package['package_sha256'] and
            report.get('loaded_assembly_sha256') == loaded == package['assets']['lib/net10.0/W.DmProvider.dll'] and
            len(report.get('loaded_assembly_mvid', '')) == 36 and report.get('reference_kind') == 'exact_PackageReference' and
            report.get('server_identity') == report.get('server_schema') == 'WDM_PROVIDER_TEST' and
            report.get('final_verification_identity') == report.get('final_verification_schema') == 'WDM_PROVIDER_TEST' and
            report.get('server_version') == ('8.1.4.6' if mode == 'tls' else '8.1.5.60') and
            report.get('explicit_transport') == ('RequireTls' if mode == 'tls' else 'PlaintextAllowed') and
            report.get('cleanup_verified') is True and report.get('final_database_state') == 'random_object_absent' and
            report.get('fresh_nonzero_rows_before_cleanup') == 0 and report.get('native_cancel_claimed') is False and
            report.get('server_immediate_stop_claimed') is False and report.get('native_cancel_frames') == 0)
    if mode == 'tls':
        require(report.get('tls_revocation_policy') == 'NoCheck_for_isolated_ephemeral_CA_without_CRL')
    validate_cases(report)
    print(json.dumps({'task': 'T14', 'mode': mode, 'status': report['status'], 'exit_code': 0,
                      'package_sha256': package['package_sha256'], 'loaded_assembly_sha256': loaded,
                      'cases': len(report['cases']), 'final_database_state': report['final_database_state']}))


if __name__ == '__main__':
    try:
        main()
    except Exception as error:
        print(json.dumps({'status': 'rejected', 'classification': 't14_validation_failed', 'type': type(error).__name__}))
        sys.exit(1)
