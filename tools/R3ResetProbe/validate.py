#!/usr/bin/env python3
"""Independently reject fabricated full-reset claims and incomplete discard evidence."""
import hashlib
import json
from pathlib import Path
import re
import sys
import uuid


def require(condition: bool) -> None:
    if not condition:
        raise ValueError('invalid_t15_envelope')


def main() -> None:
    result = json.loads(Path(sys.argv[1]).read_text())
    manifest = json.loads(Path(sys.argv[2]).read_text())
    loaded_hash = hashlib.sha256(Path(sys.argv[3]).read_bytes()).hexdigest()
    mode = sys.argv[4]
    require(mode in ('offline', 'shared', 'tls'))
    require(result.get('schema_version') == 1 and result.get('task') == 'T15' and result.get('mode') == mode and
            result.get('implementation') == 'W-package' and result.get('accepted') is True and result.get('exit_code') == 0 and
            result.get('package_version') == manifest['version'] and result.get('package_sha256') == manifest['package_sha256'] and
            result.get('loaded_assembly_sha256') == loaded_hash == manifest['assets']['lib/net10.0/W.DmProvider.dll'] and
            result.get('reference_kind') == 'exact_PackageReference' and result.get('session_reset_verified') is False and
            result.get('reuse_policy') == 'discard' and result.get('handshake_optimization_claimed') is False and
            isinstance(result.get('runtime'), str) and isinstance(result.get('runtime_identifier'), str))
    uuid.UUID(result['loaded_assembly_mvid'])
    require(result.get('public_api_verified') == ['DmConnection.OpenAsync', 'DmConnection.CloseAsync',
                                                'DmCommand.PrepareAsync', 'DmDataReader.ReadAsync'])
    ledger = result.get('capability_ledger', {})
    names = {'transaction', 'autocommit', 'isolation', 'schema', 'role_authorization', 'nls_language', 'timezone',
             'temporary_objects', 'session_variables', 'session_locks', 'open_reader', 'prepared_statement', 'package_procedure_state'}
    require(set(ledger) == names)
    for capability in ledger.values():
        require(capability.get('same_session_reset_proven') is False and
                capability.get('status') in ('not_proven', 'fresh_baseline_observed', 'discard_verified'))
    audit = result.get('reset_source_audit', {})
    require(audit.get('status') == 'no_verified_session_reset_found' and audit.get('full_server_reset_invoked') is False and
            audit.get('guessed_opcode') is False and audit.get('elevated_procedure_invoked') is False)
    if mode == 'offline':
        require(result.get('status') == 'offline_verified' and result.get('integration') == 'integration_pending' and
                all(value['status'] == 'not_proven' for value in ledger.values()) and 'server_identity' not in result)
    else:
        require(result.get('status') == 'discard_verified' and result.get('integration') == 'real_test_schema' and
                result.get('server_identity') == result.get('server_schema') == result.get('final_verification_identity') ==
                result.get('final_verification_schema') == 'WDM_PROVIDER_TEST' and
                re.fullmatch(r'\d+(\.\d+){1,5}', result.get('server_version', '')) is not None and
                result.get('cleanup_verified') is True and result.get('final_database_state') == 'unique_owned_objects_absent' and
                result.get('pooling_enabled') is False and result.get('persist_security_info') is False and
                result.get('explicit_transport') == ('RequireTls' if mode == 'tls' else 'PlaintextAllowed') and
                result.get('discard_decision') == 'full_reset_not_proven_discard_all_sessions' and result.get('measured_reused_sessions') == 0)
        modes = result.get('negotiated_encrypt_modes', [])
        require(len(modes) >= 10 and all(type(value) is int and value == (1 if mode == 'tls' else 0) for value in modes))
        require(type(result.get('physical_connections_created')) is int and result['physical_connections_created'] >= 10 and
                result['physical_connections_created'] == result.get('physical_connections_disposed') == len(modes))
        require(ledger['transaction'] == {'status': 'discard_verified', 'same_session_reset_proven': False,
                'old_uncommitted_rows': 1, 'fresh_rows_before_close': 0, 'fresh_rows_after_close': 0,
                'disposed_tcp_sockets': 1, 'transaction_lock_released': True})
        require(ledger['autocommit'] == {'status': 'fresh_baseline_observed', 'same_session_reset_proven': False, 'committed_rows': 1})
        require(ledger['isolation'] == {'status': 'fresh_baseline_observed', 'same_session_reset_proven': False, 'requested_isolation': 'ReadCommitted'})
        require(ledger['schema'] == {'status': 'fresh_baseline_observed', 'same_session_reset_proven': False,
                'expected': 'WDM_PROVIDER_TEST', 'cross_schema_mutation_attempted': False})
        for name in ('prepared_statement', 'open_reader'):
            require(ledger[name] == {'status': 'discard_verified', 'same_session_reset_proven': False, 'positive_control': True,
                    'old_object_rejected': True, 'post_close_new_sends': 0, 'post_close_new_sockets': 0})
        temporary = ledger['temporary_objects']
        if temporary['status'] == 'discard_verified':
            require(temporary == {'status': 'discard_verified', 'same_session_reset_proven': False,
                    'object_kind': 'global_temporary_table_preserve_rows', 'old_rows': 1, 'fresh_rows': 0,
                    'definition_persists_until_owned_cleanup': True, 'disposed_tcp_sockets': 1})
        else:
            require(temporary['status'] == 'not_proven' and temporary.get('reason') == 'own_schema_capability_attempt_rejected' and
                    temporary.get('error', {}).get('classification') == 'provider_error' and
                    temporary['error'].get('failure_kind') == 'Server' and temporary['error'].get('connection_reusable') is True and
                    type(temporary['error'].get('server_number')) is int)
        for name in ('timezone', 'nls_language'):
            setting = ledger[name]
            if setting['status'] == 'discard_verified':
                require(name == 'timezone' and setting == {'status': 'discard_verified', 'same_session_reset_proven': False,
                        'mutation_effect_observed': True, 'fresh_matches_baseline': True, 'disposed_tcp_sockets': 1})
            else:
                require(setting['status'] == 'not_proven')
                if setting.get('reason') == 'language_not_probed':
                    require(name == 'nls_language' and setting.get('date_format_subset') == {'status': 'discard_verified',
                            'mutation_effect_observed': True, 'fresh_matches_baseline': True, 'disposed_tcp_sockets': 1})
                elif setting.get('reason') == 'mutation_effect_not_observed':
                    require(setting.get('documented_set_executed') is True)
                else:
                    require(setting.get('reason') == 'documented_session_capability_attempt_rejected' and
                            setting.get('error', {}).get('failure_kind') == 'Server' and
                            setting['error'].get('connection_reusable') is True and type(setting['error'].get('server_number')) is int)
        require(all(re.fullmatch(r'T15_[TG]_[A-F0-9]{20}', value) for value in result.get('owned_objects', [])) and
                len(result.get('owned_objects', [])) == 2)
        if mode == 'tls':
            require(result.get('tls_revocation_policy') == 'NoCheck_for_isolated_ephemeral_CA_without_CRL')
    print(json.dumps({'schema_version': 1, 'task': 'T15', 'status': 'accepted', 'mode': mode,
                      'reuse_policy': 'discard', 'session_reset_verified': False, 'integration': result['integration'],
                      'package_version': manifest['version'], 'package_sha256': manifest['package_sha256']}, separators=(',', ':')))


if __name__ == '__main__':
    try:
        main()
    except Exception:
        print('{"schema_version":1,"task":"T15","status":"rejected","classification":"envelope_validation_failed"}')
        sys.exit(1)
