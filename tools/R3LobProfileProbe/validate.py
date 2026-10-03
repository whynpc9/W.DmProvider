#!/usr/bin/env python3
"""Validate capability facts from a pinned, unchanged T16 package without claiming streaming acceptance."""
import hashlib
import json
from pathlib import Path
import re
import sys

VERSION = '0.1.0-r3.t16.20261002143244'
PACKAGE = 'a15d5009659e1f78f00a49efe7c1caf0cd43974b69169e405dfc794ef5275a44'
DLL = 'f70627e5d957f3de86ec33070474ab1543c162c5f6321f93312ad87c4535290f'
MVID = '654b6ddd-b0ff-4408-8e1b-b93bd075c1da'


def require(value):
    if not value:
        raise ValueError('invalid_t17_profile_envelope')


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    result = json.loads(Path(sys.argv[1]).read_text())
    manifest = json.loads(Path(sys.argv[2]).read_text())
    mode = sys.argv[4]
    repo = Path(__file__).resolve().parents[2]
    fixture_path = repo / 'tests/fixtures/r3-lob/vectors.json'
    vector = json.loads(fixture_path.read_text())['out_row_profile_large']
    require(mode in ('offline', 'shared', 'tls') and result.get('schema_version') == 1 and
            result.get('task') == 'T17-profile' and result.get('phase') == 'capability_characterization' and result.get('mode') == mode and
            result.get('implementation') == 'W-package' and result.get('accepted') is True and result.get('exit_code') == 0 and
            result.get('streaming_api_acceptance_claimed') is False and result.get('other_profile_charset_support_claimed') is False and
            result.get('execution_mode') == 'async_raw_single_frame_protocol' and
            result.get('package_version') == manifest['version'] == VERSION and result.get('package_sha256') == manifest['package_sha256'] == PACKAGE and
            result.get('loaded_assembly_sha256') == sha(Path(sys.argv[3])) == manifest['assets']['lib/net10.0/W.DmProvider.dll'] == DLL and
            result.get('loaded_assembly_mvid') == MVID and result.get('reference_kind') == 'exact_PackageReference' and
            result.get('fixture_sha256') == sha(fixture_path) and
            result.get('accepted_source_manifest_sha256') == sha(repo / 'docs/implementation/evidence/T16/accepted-v3/accepted-source-manifest.json') and
            result.get('expected_utf16_code_units') == vector['utf16_count'] and result.get('expected_rune_count') == vector['rune_count'] and
            result.get('expected_utf8_sha256') == vector['utf8_sha256'] and result.get('expected_utf8_byte_count') == vector['utf8_byte_count'] >= 131072 and
            result.get('independent_marker_count') == len(vector['markers']) >= 3)
    cases = result.get('cases', {})
    require(cases.get('offline_contract') == {'category': 'Contract', 'feature': 'LobProfile', 'status': 'passed',
            'exact_accepted_package': True, 'independent_vector_checked': True, 'raw_single_frame_async_surface_checked': True, 'network_invoked': False})
    if mode == 'offline':
        require(result.get('status') == 'offline_verified' and result.get('integration') == 'integration_pending' and
                set(cases) == {'offline_contract'} and result.get('requests') == [] and 'server_identity' not in result)
    else:
        require(result.get('status') == 'profile_characterized' and result.get('integration') == 'real_test_schema' and
                result.get('server_identity') == result.get('server_schema') == result.get('final_verification_identity') ==
                result.get('final_verification_schema') == 'WDM_PROVIDER_TEST' and result.get('cleanup_verified') is True and
                result.get('final_database_state') == 'unique_owned_objects_absent' and
                re.fullmatch(r'\d+(\.\d+){1,5}', result.get('server_version', '')) is not None and
                result.get('explicit_transport') == ('RequireTls' if mode == 'tls' else 'PlaintextAllowed') and
                result.get('pooling_enabled') is False and result.get('persist_security_info') is False and
                set(cases) == {'offline_contract', 'CLOB', 'NCLOB'})
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
        requests = result.get('requests', [])
        require(0 < len(requests) <= 4096)
        for declaration in ('CLOB', 'NCLOB'):
            case = cases[declaration]
            require(case.get('category') == 'state_evidence')
            if case.get('status') == 'unsupported_by_profile':
                require(declaration == 'NCLOB' and case.get('declaration') == 'NCLOB' and
                        case.get('error', {}).get('failure_kind') == 'Server' and case['error'].get('operation_outcome') == 'ServerReported' and
                        type(case['error'].get('server_number')) is int)
                continue
            require(case.get('status') == 'characterized' and case.get('declared_type') == declaration and
                    case.get('upload_path') == 'existing_string_Clob_parameter' and
                    case.get('protocol_path') == 'ReadLobAsync_AbstractLob_single_frame' and
                    case.get('scan_utf16_code_units') == vector['utf16_count'] and case.get('scan_rune_count') == vector['rune_count'] and
                    case.get('scan_utf8_sha256') == vector['utf8_sha256'] and case.get('terminal_read_over') is True and
                    case.get('complete_input_matches') is True and
                    case.get('progress_requires_updated_locator') is True and case.get('absolute_seek_proven') is False and
                    case.get('terminal_wire_length_missing') is False and case.get('wire_length_ledger_complete') is True and
                    case.get('wire_length_metadata_always_present') is True and case.get('wire_length_metadata_missing') is False and
                    case.get('streaming_support_decision') == 'same_profile_progress_and_codec_proven_requires_new_stream_implementation')
            metadata = case.get('column_metadata', {})
            require(metadata.get('local') is False and metadata.get('storage_type') in (2, 4) and metadata.get('lob_flag') == 1 and
                    type(metadata.get('c_type')) is int and type(metadata.get('scale')) is int and type(metadata.get('type_flag')) is int and
                    type(metadata.get('is_lob')) is bool and type(metadata.get('msg_version')) is int and
                    isinstance(metadata.get('server_encoding'), str) and type(metadata.get('encoding_code_page')) is int and
                    type(metadata.get('negotiated_max_lob_message_length')) is int and metadata['negotiated_max_lob_message_length'] > 0 and
                    type(case.get('get_lob_len')) is int and case['get_lob_len'] >= 0)
            candidates = case.get('independently_matched_offset_units', [])
            require(set(candidates) <= {'unicode_scalar', 'utf16_code_unit', 'encoded_byte'})
            require(case.get('progress_strategy') == 'nonnegative_Data_len' and case.get('offset_unit') == 'opaque_server_units')
            scan = [entry for entry in requests if entry.get('declared_type') == declaration and entry.get('purpose') == 'full_exact_content_scan']
            require(len(scan) > 1)
            offset = 0
            for index, entry in enumerate(scan):
                require(entry.get('request_offset') == offset and type(entry.get('request_length')) is int and
                        0 < entry['request_length'] <= min(4093, metadata['negotiated_max_lob_message_length']) and
                        type(entry.get('reply_byte_count')) is int and entry['reply_byte_count'] >= 0 and entry.get('strict_decode') is True and
                        entry.get('independent_content_matches') is True and type(entry.get('data_len')) is int and entry['data_len'] >= 0 and
                        type(entry.get('decoded_utf16_count')) is int and type(entry.get('decoded_rune_count')) is int)
                advance = entry['data_len']
                require(advance >= 0 and (entry.get('read_over') is True or advance > 0))
                require(entry.get('read_over') is True or (entry['reply_byte_count'] > 0 and entry['decoded_utf16_count'] > 0))
                offset += advance
                require(entry.get('read_over') is (index == len(scan) - 1))
            require(offset == case.get('final_wire_offset') == case.get('known_wire_advance_sum') == case.get('get_lob_len') and
                    case.get('get_lob_len_matches_final_wire_offset') is True and
                    sum(entry['decoded_utf16_count'] for entry in scan) == vector['utf16_count'] and
                    sum(entry['decoded_rune_count'] for entry in scan) == vector['rune_count'])
        objects = result.get('owned_objects', [])
        require(len(objects) == 2 and re.fullmatch(r'T17C_[A-F0-9]{20}', objects[0]) is not None and
                re.fullmatch(r'T17N_[A-F0-9]{20}', objects[1]) is not None)
        if mode == 'tls':
            require(result.get('tls_revocation_policy') == 'NoCheck_for_isolated_ephemeral_CA_without_CRL')
    print(json.dumps({'schema_version': 1, 'task': 'T17-profile', 'status': 'accepted', 'phase': 'capability_characterization',
                      'mode': mode, 'integration': result['integration'], 'package_version': VERSION, 'package_sha256': PACKAGE,
                      'streaming_api_acceptance_claimed': False}, separators=(',', ':')))


if __name__ == '__main__':
    try:
        main()
    except Exception:
        print('{"schema_version":1,"task":"T17-profile","status":"rejected","classification":"envelope_validation_failed"}')
        sys.exit(1)
