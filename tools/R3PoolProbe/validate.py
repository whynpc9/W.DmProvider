#!/usr/bin/env python3
"""Reject incomplete package, physical-capacity, identity, and discard evidence."""
import hashlib
import json
from pathlib import Path
import re
import sys
import uuid


def require(value):
    if not value:
        raise ValueError('invalid_t16_envelope')


def main():
    result = json.loads(Path(sys.argv[1]).read_text())
    manifest = json.loads(Path(sys.argv[2]).read_text())
    loaded = hashlib.sha256(Path(sys.argv[3]).read_bytes()).hexdigest()
    mode = sys.argv[4]
    require(mode in ('offline', 'shared', 'tls'))
    require(result.get('schema_version') == 1 and result.get('task') == 'T16' and result.get('mode') == mode and
            result.get('accepted') is True and result.get('exit_code') == 0 and result.get('implementation') == 'W-package' and
            result.get('package_version') == manifest['version'] and result.get('package_sha256') == manifest['package_sha256'] and
            result.get('loaded_assembly_sha256') == loaded == manifest['assets']['lib/net10.0/W.DmProvider.dll'] and
            result.get('reference_kind') == 'exact_PackageReference' and result.get('session_reset_verified') is False and
            result.get('reuse_policy') == 'discard' and result.get('handshake_optimization_claimed') is False and
            isinstance(result.get('runtime'), str) and isinstance(result.get('runtime_identifier'), str))
    uuid.UUID(result['loaded_assembly_mvid'])
    categorized = result.get('cases', {})
    require(all(item.get('category') == ('Improvement' if name == 'offline_public_contract' else 'Contract') and
                item.get('feature') == 'Pooling' for name, item in categorized.items()))
    cases = {name: {key: value for key, value in item.items() if key not in ('category', 'feature')}
             for name, item in categorized.items()}
    require(cases.get('offline_public_contract') == {'status': 'passed', 'construction_sockets': 0, 'configuration_redacted': True})
    require(cases.get('direct_explicit_certificate_rejected') == {'status': 'passed', 'sockets': 0})
    require(result.get('offline_core_scheduler_scope') == 'separate_PoolTests_not_fabricated_database_work')
    if mode == 'offline':
        require(result.get('status') == 'offline_verified' and result.get('integration') == 'integration_pending' and
                set(cases) == {'offline_public_contract', 'direct_explicit_certificate_rejected'} and
                'server_identity' not in result and 'owned_objects' not in result)
    else:
        require(result.get('status') == 'pool_discard_verified' and result.get('integration') == 'real_test_schema' and
                result.get('server_identity') == result.get('server_schema') == result.get('final_verification_identity') ==
                result.get('final_verification_schema') == 'WDM_PROVIDER_TEST' and
                re.fullmatch(r'\d+(\.\d+){1,5}', result.get('server_version', '')) is not None and
                result.get('cleanup_verified') is True and result.get('final_database_state') == 'unique_owned_objects_absent' and
                result.get('pooling_enabled') is True and result.get('persist_security_info') is False and
                result.get('maximum_physical_connections_per_owner') == 1 and result.get('maximum_waiters_per_owner') == 4 and
                result.get('pool_acquire_timeout_milliseconds') == 5000 and result.get('measured_reused_sessions') == 0 and
                result.get('explicit_transport') == ('RequireTls' if mode == 'tls' else 'PlaintextAllowed'))
        required = {'offline_public_contract', 'direct_explicit_certificate_rejected', 'datasource_capacity',
                    'shortcut_command_ownership', 'transaction_and_stale_objects', 'datasource_dispose_waiting'}
        if mode == 'shared':
            required.add('direct_registry_capacity')
        require(set(cases) == required)
        for name in ('datasource_capacity', 'direct_registry_capacity'):
            if name not in required:
                continue
            require(cases[name] == {'status': 'passed', 'waiting_cancel_not_sent': True, 'waiting_deadline_not_sent': True,
                    'clear_kept_capacity_domain': True, 'transport_closed_before_replacement_connect': True,
                    'physical_replacement_authenticated': True,
                    'reused_sessions': 0, 'final_state_zero': True})
        require(cases['shortcut_command_ownership'] == {'status': 'passed', 'nonreader_success_returned': True,
                'nonreader_server_failure_returned': True, 'scalar_returned': True, 'reader_held_until_close': True,
                'implicit_identity_checked_by_sql': True, 'final_state_zero': True})
        require(cases['transaction_and_stale_objects'] == {'status': 'passed', 'old_transaction_rows': 1,
                'independent_rows_before_close': 0, 'fresh_uncommitted_rows': 0,
                'old_reader_rejected_without_io': True, 'stale_cancel_and_transaction_did_not_touch_fresh': True, 'lock_released': True})
        require(cases['datasource_dispose_waiting'] == {'status': 'passed', 'waiting_terminated_without_socket': True,
                'existing_business_lease_completed': True, 'new_admissions_rejected': True, 'final_state_zero': True})
        created = result.get('physical_connections_created')
        modes = result.get('negotiated_encrypt_modes', [])
        require(type(created) is int and created == (15 if mode == 'shared' else 13) and created == result.get('physical_connections_disposed') ==
                result.get('authenticated_successful_open_count') == len(modes) and
                all(type(value) is int and value == (1 if mode == 'tls' else 0) for value in modes))
        counts = result.get('network_io_counts', {})
        require(set(counts) == {prefix + operation for prefix in ('async_', 'sync_') for operation in ('connect', 'tls', 'send', 'receive')} and
                all(type(value) is int and value >= 0 for value in counts.values()) and
                all(counts['sync_' + operation] == 0 for operation in ('connect', 'tls', 'send', 'receive')) and
                all(counts['async_' + operation] > 0 for operation in ('connect', 'send', 'receive')) and
                (counts['async_tls'] > 0 if mode == 'tls' else counts['async_tls'] == 0))
        snapshots = result.get('owner_final_snapshots', [])
        require(result.get('state_evidence_category') == 'state_evidence')
        require(len(snapshots) == (4 if mode == 'shared' else 3) and
                all(item == {'creating': 0, 'leased': 0, 'closing': 0, 'waiting': 0, 'idle': 0, 'resetting': 0} for item in snapshots))
        objects = result.get('owned_objects', [])
        require(len(objects) == 1 and re.fullmatch(r'T16_T_[A-F0-9]{20}', objects[0]) is not None)
        if mode == 'tls':
            require(result.get('tls_revocation_policy') == 'NoCheck_for_isolated_ephemeral_CA_without_CRL')
    print(json.dumps({'schema_version': 1, 'task': 'T16', 'status': 'accepted', 'mode': mode,
                      'integration': result['integration'], 'reuse_policy': 'discard', 'session_reset_verified': False,
                      'package_version': manifest['version'], 'package_sha256': manifest['package_sha256']}, separators=(',', ':')))


if __name__ == '__main__':
    try:
        main()
    except Exception:
        print('{"schema_version":1,"task":"T16","status":"rejected","classification":"envelope_validation_failed"}')
        sys.exit(1)
