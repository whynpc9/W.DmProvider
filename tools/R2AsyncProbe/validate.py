#!/usr/bin/env python3
"""Fail closed against actual package hashes and explicit server outcomes."""
import hashlib
import json
from pathlib import Path
import sys


def require(condition: bool) -> None:
    if not condition:
        raise ValueError('probe_envelope_invalid')


def validate_savepoints(result: dict, cases: dict, mode: str) -> None:
    savepoints = cases['transaction_savepoints_async']
    version = result.get('server_version')
    if version == '8.1.5.60':
        require(savepoints == {'status': 'supported_for_server_profile', 'server_version': version,
                               'supports_savepoints': True, 'fresh_retained_rows': 1, 'fresh_rolled_back_rows': 0})
    else:
        require(mode == 'tls' and version == '8.1.4.6')
        expected_rejection = {'exception_type': 'System.NotSupportedException', 'new_frames': 0,
                              'transaction_outcome': 'Active', 'connection_state': 'Open'}
        require(savepoints == {'status': 'unsupported_for_server_profile', 'server_version': version,
                               'supports_savepoints': False,
                               'rejections': {name: expected_rejection for name in ('SaveAsync', 'RollbackAsync', 'ReleaseAsync')},
                               'post_rollback_outcome': 'RolledBack', 'fresh_candidate_rows': 0, 'fresh_rolled_back_rows': 0})
        require(all(type(rejection['new_frames']) is int for rejection in savepoints['rejections'].values()))


def validate_security(result: dict, mode: str) -> None:
    tls = mode == 'security-tls'
    negotiated_mode = 1 if tls else 0
    require(result.get('status') == 'security_rejection_verified' and result.get('integration') == 'real_test_security' and
            result.get('final_database_state') == 'no_objects_created' and result.get('persist_security_info') is False and
            result.get('handshake_hook') == 'DmWireTestHooks.AfterHandshakeExchangeEntered_via_HandshakeFrameEncoded' and
            'cases' not in result)
    for phase in ('before', 'after'):
        identity = result.get(phase, {})
        require(identity.get('identity') == identity.get('schema') == 'WDM_PROVIDER_TEST' and
                identity.get('negotiated_encrypt_mode') == negotiated_mode and identity.get('login_encoded_count') == 1 and
                identity.get('server_version') == ('8.1.4.6' if tls else '8.1.5.60'))
    require(result.get('security_case') == {
        'classification': 'unknown_ca_rejected_before_login' if tls else 'require_tls_rejected_plaintext_before_login',
        'exception_type': 'System.Security.Authentication.AuthenticationException' if tls else 'System.NotSupportedException',
        'connection_state': 'Closed', 'handshake_opcodes': [200], 'login_encoded_count': 0,
        'negotiated_encrypt_modes': [negotiated_mode], 'created_tcp_sockets': 1, 'disposed_tcp_sockets': 1,
        'successful_tls_upgrades': 0, 'failed_tls_upgrades': 1 if tls else 0, 'explicit_transport': 'RequireTls'})
    security_case = result['security_case']
    require(all(type(security_case[name]) is int for name in ('login_encoded_count', 'created_tcp_sockets',
                'disposed_tcp_sockets', 'successful_tls_upgrades', 'failed_tls_upgrades')) and
            all(type(opcode) is int for opcode in security_case['handshake_opcodes']))
    frames = result.get('negative_numeric_frame_observations', [])
    require(len(frames) == 1 and frames[0].get('Stage') == 'security_negative_open_async' and frames[0].get('RequestOpcode') == 200)
    if tls:
        require(result.get('tls_revocation_policy') == 'NoCheck_for_isolated_ephemeral_CA_without_CRL' and
                result.get('temporary_public_ca_removed') is True and result.get('unrelated_ca') == {
                    'public_certificate_only': True, 'certificate_mode': '0600', 'directory_mode': '0700'})


def main() -> None:
    result = json.loads(Path(sys.argv[1]).read_text())
    manifest = json.loads(Path(sys.argv[2]).read_text())
    loaded_hash = hashlib.sha256(Path(sys.argv[3]).read_bytes()).hexdigest()
    mode = sys.argv[4]
    require(mode in ('offline', 'shared', 'tls', 'security-shared', 'security-tls'))
    require(result.get('accepted') is True and result.get('exit_code') == 0 and result.get('mode') == mode and
            result.get('schema_version') == 1 and result.get('task') == 'T13' and result.get('implementation') == 'W-package' and
            result.get('package_version') == manifest['version'] and result.get('package_sha256') == manifest['package_sha256'] and
            result.get('loaded_assembly_sha256') == loaded_hash == manifest['assets']['lib/net10.0/W.DmProvider.dll'] and
            len(result.get('loaded_assembly_mvid', '')) == 36 and result.get('reference_kind') == 'exact_PackageReference' and
            len(result.get('api_override_audit', [])) == 26 and result.get('streaming_lob_claimed') is False and
            result.get('cancellation_acceptance') == 'T14_pending')
    if mode == 'offline':
        require(result.get('status') == 'offline_verified' and result.get('integration') == 'integration_pending')
    elif mode in ('security-shared', 'security-tls'):
        validate_security(result, mode)
    else:
        cases = result.get('cases', {})
        require(result.get('status') == 'package_async_verified' and result.get('integration') == 'real_test_schema' and
                result.get('server_identity') == result.get('server_schema') == 'WDM_PROVIDER_TEST' and
                result.get('final_verification_identity') == result.get('final_verification_schema') == 'WDM_PROVIDER_TEST' and
                result.get('cleanup_verified') is True and result.get('final_database_state') == 'random_object_absent' and
                result.get('server_final_rows_before_cleanup') == 0 and result.get('persist_security_info') is False and
                result.get('explicit_transport') == ('RequireTls' if mode == 'tls' else 'PlaintextAllowed') and
                result.get('negotiated_encrypt_modes') and all(value == (1 if mode == 'tls' else 0) for value in result['negotiated_encrypt_modes']))
        expected_cases = {'open_async', 'prepare_execute_nonquery_async', 'execute_scalar_async', 'read_async_cross_page_fetch',
                          'next_result_async', 'transaction_begin_commit_async', 'transaction_rollback_async', 'transaction_savepoints_async',
                          'transaction_dispose_async_rollback', 'lob_parameter_upload_async', 'lob_get_field_value_async',
                          'lob_execute_scalar_async', 'connection_close_dispose_async'}
        require(set(cases) == expected_cases and cases['open_async'] is True and cases['prepare_execute_nonquery_async'] is True and
                cases['execute_scalar_async'] is True and cases['connection_close_dispose_async'] is True)
        fetch = cases['read_async_cross_page_fetch']
        require(fetch.get('rows') == 2048 and fetch.get('fetch_frames', 0) > 0 and 0 <= fetch.get('rows_before_first_actual_fetch', -1) < 2048)
        require(cases['next_result_async'].get('rowsets') == 2 and cases['next_result_async'].get('more_result_frames', 0) > 0 and
                cases['transaction_begin_commit_async'].get('fresh_committed_rows') == 1 and
                cases['transaction_rollback_async'].get('fresh_rolled_back_rows') == 0 and
                cases['transaction_dispose_async_rollback'].get('fresh_rolled_back_rows') == 0 and
                cases['lob_get_field_value_async'].get('get_lob_data_frames', 0) > 0 and
                cases['lob_execute_scalar_async'].get('get_lob_data_frames', 0) > 0)
        validate_savepoints(result, cases, mode)
        frames = result.get('numeric_frame_observations', [])
        require(any(f.get('Stage') == 'read_async_cross_page_fetch' and f.get('RequestOpcode') == 7 for f in frames) and
                any(f.get('Stage') == 'next_result_async' and f.get('RequestOpcode') == 44 for f in frames) and
                any(f.get('Stage') == 'lob_get_field_value_async' and f.get('RequestOpcode') == 32 for f in frames))
        if mode == 'tls':
            require(result.get('tls_revocation_policy') == 'NoCheck_for_isolated_ephemeral_CA_without_CRL')
    print(json.dumps({'schema_version': 1, 'task': 'T13', 'mode': mode, 'status': result['status'], 'exit_code': 0,
                      'package_sha256': manifest['package_sha256'], 'loaded_assembly_sha256': loaded_hash,
                      'loaded_assembly_mvid': result['loaded_assembly_mvid'], 'api_overrides': 26,
                      'integration': result['integration'], 'server_version': result.get('server_version'),
                      'savepoint_status': result.get('cases', {}).get('transaction_savepoints_async', {}).get('status'),
                      'security_classification': result.get('security_case', {}).get('classification'),
                      'final_database_state': result.get('final_database_state')}, separators=(',', ':')))


if __name__ == '__main__':
    try:
        main()
    except Exception as error:
        print(json.dumps({'status': 'rejected', 'classification': 'result_validation_failed', 'error_type': type(error).__name__}))
        sys.exit(1)
