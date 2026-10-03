#!/usr/bin/env python3
"""Independent exact-package and fixed-input stream evidence validation; no environment or raw error output."""
import hashlib
import json
from pathlib import Path
import re
import sys
import uuid


def require(value):
    if not value:
        raise ValueError('invalid_t17_envelope')


def blob_hash():
    # Independent arithmetic reference, bounded one-buffer update; never hold the 64MiB logical value.
    cycle = bytes((17 + 131 * index) % 256 for index in range(256))
    chunk = cycle * 128
    digest = hashlib.sha256()
    length = 64 * 1024 * 1024 + 1024
    full, tail = divmod(length, len(chunk))
    for _ in range(full):
        digest.update(chunk)
    digest.update(chunk[:tail])
    return digest.hexdigest()


def main():
    result = json.loads(Path(sys.argv[1]).read_text())
    manifest = json.loads(Path(sys.argv[2]).read_text())
    mode = sys.argv[4]
    fixture_path = Path(__file__).resolve().parents[2] / 'tests/fixtures/r3-lob/vectors.json'
    fixture_raw = fixture_path.read_bytes()
    vector = json.loads(fixture_raw)['out_row_profile_large']
    text_hash = hashlib.sha256()
    for cycle in range(10):
        text_hash.update(f'[CYCLE:{cycle:04d}]\n'.encode('utf-8'))
        text_hash.update(vector['value'].encode('utf-8'))
    expected_text_hash = text_hash.hexdigest()
    chars = (vector['utf16_count'] + 13) * 10
    runes = (vector['rune_count'] + 13) * 10
    binary_length = 64 * 1024 * 1024 + 1024
    expected_blob_hash = blob_hash()
    require(mode in ('offline', 'shared', 'tls') and result.get('schema_version') == 1 and result.get('task') == 'T17' and
            result.get('mode') == mode and result.get('implementation') == 'W-package' and result.get('accepted') is True and result.get('exit_code') == 0 and
            result.get('package_version') == manifest['version'] and result.get('package_sha256') == manifest['package_sha256'] and
            result.get('loaded_assembly_sha256') == hashlib.sha256(Path(sys.argv[3]).read_bytes()).hexdigest() == manifest['assets']['lib/net10.0/W.DmProvider.dll'] and
            result.get('reference_kind') == 'exact_PackageReference' and result.get('fixture_sha256') == hashlib.sha256(fixture_raw).hexdigest() and
            result.get('measured_reused_sessions') == 0 and result.get('session_reset_verified') is False and result.get('reuse_policy') == 'discard')
    uuid.UUID(result['loaded_assembly_mvid'])
    inputs = result.get('input_manifest', {})
    require(inputs == {'blob_bytes': binary_length, 'blob_seed': 17, 'blob_stride': 131, 'blob_buffer_bytes': 32768,
            'materialized_blob_limit_bytes': 67108864, 'expected_blob_sha256': expected_blob_hash,
            'clob_utf16_chars': chars, 'nclob_utf16_chars': chars, 'text_cycles': 10, 'expected_text_runes': runes,
            'expected_text_utf8_sha256': expected_text_hash, 'text_read_buffer_chars': 4093,
            'command_total_seconds': 300, 'connect_seconds': 15, 'process_total_seconds': 1800,
            'inputs_unknown_length': True, 'inputs_nonseekable': True, 'caller_owns_inputs': True,
            'one_gib_database_claimed': False, 'one_gib_scope': 'independent_offline_LobTests',
            'malformed_reply_and_truncation_scope': 'independent_offline_LobTests_not_injected_database_packets'})
    cases = result.get('cases', {})
    require(all(value.get('status') == 'passed' and value.get('category') == 'Contract' and value.get('feature') == 'LobStreaming'
                for value in cases.values()))
    require(cases.get('offline_public_contract', {}).get('evidence') == {'public_stream_overrides_verified': True,
            'independent_generators_defined': True, 'network_invoked': False, 'database_streaming_claimed': False})
    if mode == 'offline':
        require(result.get('status') == 'offline_verified' and result.get('integration') == 'integration_pending' and
                set(cases) == {'offline_public_contract'} and 'server_identity' not in result)
    else:
        required = {'offline_public_contract', 'LOB05_blob_input_repeat', 'LOB05_C_input', 'LOB05_N_input', 'LOB05_empty_exact_short',
                    'LOB01_materialization_guard', 'LOB01_blob_roundtrip', 'LOB02_C_roundtrip', 'LOB02_N_roundtrip',
                    'LOB03_forward_ranges', 'legacy_40k_array_string', 'LOB04_parent_lifetime', 'LOB07_explicit_type',
                    'LOB06_input_exception', 'LOB06_upload_cancel', 'LOB06_read_cancel'}
        require(set(cases) == required and result.get('status') == 'streaming_verified' and result.get('integration') == 'real_test_schema' and
                result.get('server_identity') == result.get('server_schema') == result.get('final_verification_identity') ==
                result.get('final_verification_schema') == 'WDM_PROVIDER_TEST' and result.get('cleanup_verified') is True and
                result.get('final_database_state') == 'unique_owned_objects_absent' and
                result.get('server_version') == ('8.1.4.6' if mode == 'tls' else '8.1.5.60') and
                result.get('server_encoding') == ('gb18030' if mode == 'tls' else 'utf-8') and result.get('message_version') == (11 if mode == 'tls' else 21) and
                result.get('explicit_transport') == ('RequireTls' if mode == 'tls' else 'PlaintextAllowed'))
        evidence = lambda name: cases[name]['evidence']
        require(evidence('LOB05_blob_input_repeat') == {'uploaded_bytes': binary_length, 'unknown_length': True, 'forbidden_accesses': 0,
                'caller_owned': True, 'repeated_execution_empty_current_position': True})
        for column in ('C', 'N'):
            upload = evidence('LOB05_' + column + '_input')
            require(upload.get('uploaded_utf16_chars') == chars and upload.get('sync_reads') == 0 and upload.get('split_surrogate_reads', 0) > 0 and
                    upload.get('caller_owned') is True and upload.get('unknown_length') is True and upload.get('typed_parameter') == 'Clob')
            value = evidence('LOB02_' + column + '_roundtrip')
            require(value.get('utf16_chars') == chars and value.get('runes') == runes and value.get('utf8_sha256') == expected_text_hash and
                    value.get('lazy_get_requests') == 0 and 0 < value.get('first_read_chars', 0) <= 17 and value.get('caller_buffer_chars') == 4093 and
                    value.get('second_stream_refused') is True and value.get('length_query_preserved_stream_position') is True and
                    0 < value.get('first_get_request_count', 0) <= 2 and 0 < value.get('first_get_reply_body_bytes', 0) < 1048576 and
                    value.get('zero_length_no_io') is True and value.get('whole_value_string_materialized') is False)
        binary = evidence('LOB01_blob_roundtrip')
        require(binary.get('bytes') == binary_length and binary.get('sha256') == expected_blob_hash and binary.get('lazy_get_requests') == 0 and
                0 < binary.get('first_read_bytes', 0) <= 1031 and binary.get('caller_buffer_bytes') == 32768 and
                binary.get('second_stream_refused') is True and binary.get('length_query_preserved_stream_position') is True and
                0 < binary.get('first_get_request_count', 0) <= 2 and 0 < binary.get('first_get_reply_body_bytes', 0) < 1048576 and
                binary.get('zero_length_no_io') is True and binary.get('full_array_allocated') is False)
        require(evidence('LOB01_materialization_guard') == {'field_bytes': binary_length, 'rejected_before_get_lob_data': True,
                'exact_cap_exception_matched': True, 'same_reader_stream_positive': True,
                'execution_mode': 'explicit_sync_public_api', 'stream_followup_case': 'LOB01_blob_roundtrip'})
        small = evidence('LOB05_empty_exact_short')
        require(small.get('positive_caller_short_reads_filled_to_capacity') is True and small.get('actual_ack_opcode') == 261 and
                small.get('actual_ack_status') == 0 and small.get('actual_ack_body_length') == 21 and len(small.get('inputs', [])) == 6)
        for index, item in enumerate(small['inputs']):
            chunk = item['effective_chunk']
            require(type(chunk) is int and 0 < chunk <= 32768 and item.get('caller_owned') is True and item.get('kind') == ('Blob' if index < 3 else 'Clob_ASCII'))
            expected = [0] if index % 3 == 0 else [chunk, 0] if index % 3 == 1 else [chunk, 17]
            require(item.get('observed_chunk_sizes') == expected and item.get('length') == (0 if index % 3 == 0 else chunk if index % 3 == 1 else chunk + 17))
        require(evidence('LOB03_forward_ranges') == {'execution_mode': 'explicit_sync_public_api', 'byte_offsets_verified': True,
                'utf16_offsets_verified': True, 'backward_before_io': True, 'buffers_bounded': True})
        require(evidence('legacy_40k_array_string') == {'blob_bytes': 40960, 'text_utf16_chars': 40960,
                'actual_legacy_array_string_roundtrip': True, 'replaces_large_stream_gate': False})
        require(evidence('LOB04_parent_lifetime') == {'next_row_invalidates': True, 'next_result_invalidates': True, 'reader_close_invalidates': True,
                'old_flow_cannot_touch_new_physical_lease': True, 'invalid_reads_before_io': True})
        require(evidence('LOB07_explicit_type') == {'before_network': True, 'before_input_read': True, 'caller_owned': True})
        failure = evidence('LOB06_input_exception')
        require(failure.get('after_upload_send') is True and failure.get('source_throw_count') == 1 and failure.get('caller_owned') is True and
                failure.get('connection_broken_or_closed') is True and failure.get('socket_replay_count') == 0 and failure.get('fresh_rows') == 0)
        for name, flag in (('LOB06_upload_cancel', 'canceled_after_opcode26'), ('LOB06_read_cancel', 'canceled_after_opcode32')):
            cancel = evidence(name)
            require(cancel.get(flag) is True and cancel.get('sent_frames') == 1 and cancel.get('no_replay') is True and
                    cancel.get('error', {}).get('failure_kind') == 'Canceled' and cancel['error'].get('operation_outcome') == 'Unknown' and
                    cancel['error'].get('connection_reusable') is False)
        require(evidence('LOB06_upload_cancel').get('caller_owned') is True and evidence('LOB06_upload_cancel').get('fresh_rows') == 0)
        created = result.get('physical_connections_created')
        modes = result.get('negotiated_encrypt_modes', [])
        require(type(created) is int and created > 0 and created == result.get('physical_connections_disposed') == len(modes) and
                all(type(item) is int and item == (1 if mode == 'tls' else 0) for item in modes))
        counts = result.get('network_io_counts', {})
        require(set(counts) == {prefix + operation for prefix in ('async_', 'sync_') for operation in ('connect', 'tls', 'send', 'receive')} and
                all(type(value) is int and value >= 0 for value in counts.values()) and
                all(counts['sync_' + operation] == 0 for operation in ('connect', 'tls', 'send', 'receive')) and
                all(counts['async_' + operation] > 0 for operation in ('connect', 'send', 'receive')) and
                (counts['async_tls'] > 0 if mode == 'tls' else counts['async_tls'] == 0))
        require(set(result.get('explicit_sync_api_network_io_counts', {})) == {'connect', 'tls', 'send', 'receive'})
        stats = result.get('measured_buffer_stats', {})
        require(stats.get('input_chunk_observations', 0) > 0 and 0 < stats.get('input_owned_cursor_peak_bytes', 0) <= 131072 and
                stats.get('output_public_read_samples', 0) > 0 and 0 < stats.get('output_sampled_retained_cursor_peak_bytes', 0) < 1048576 and
                0 < stats.get('output_max_get_lob_reply_body_bytes', 0) < 1048576 and
                stats.get('scope') == 'cursor_owned_and_sampled_retained_buffers_plus_observed_reply_body_not_process_RSS')
        opcodes = result.get('lob_opcode_counts', {})
        require(opcodes.get('get_lob_data', 0) > 0 and opcodes.get('put_data2_sent', 0) > 0 and opcodes.get('put_data2_ack', 0) > 0 and
                opcodes.get('ack_opcode') == 261 and opcodes.get('ack_status') == 0 and opcodes.get('ack_body_bytes') == 21 and opcodes.get('invalid_ack_seen') is False)
        require(result.get('pool_final_snapshot') == {'creating': 0, 'leased': 0, 'closing': 0, 'waiting': 0, 'idle': 0, 'resetting': 0})
        objects = result.get('owned_objects', [])
        require(len(objects) == 1 and re.fullmatch(r'T17S_[A-F0-9]{20}', objects[0]) is not None)
        if mode == 'tls':
            require(result.get('tls_revocation_policy') == 'NoCheck_for_isolated_ephemeral_CA_without_CRL')
    print(json.dumps({'schema_version': 1, 'task': 'T17', 'status': 'accepted', 'mode': mode,
                      'integration': result['integration'], 'package_version': manifest['version'], 'package_sha256': manifest['package_sha256'],
                      'blob_bytes': binary_length, 'text_utf16_chars': chars, 'reuse_policy': 'discard'}, separators=(',', ':')))


if __name__ == '__main__':
    try:
        main()
    except Exception:
        print('{"schema_version":1,"task":"T17","status":"rejected","classification":"envelope_validation_failed"}')
        sys.exit(1)
