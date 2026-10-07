#!/usr/bin/env python3
"""Validate observed opcode26 response headers; never guess the response opcode or inspect the opaque ACK body."""
import hashlib
import json
from pathlib import Path
import re
import sys


def require(value):
    if not value:
        raise ValueError('upload_profile_envelope')


def main():
    result = json.loads(Path(sys.argv[1]).read_text())
    manifest = json.loads(Path(sys.argv[2]).read_text())
    mode = sys.argv[4]
    require(result.get('task') == 'T17-upload-profile' and result.get('phase') == 'capability_characterization' and
            result.get('mode') == mode and mode in ('offline', 'shared', 'tls') and result.get('accepted') is True and result.get('exit_code') == 0 and
            result.get('streaming_api_acceptance_claimed') is False and result.get('reference_kind') == 'exact_PackageReference' and
            result.get('package_version') == manifest['version'] == '0.1.0-r3.t16.20261002143244' and
            result.get('package_sha256') == manifest['package_sha256'] == 'a15d5009659e1f78f00a49efe7c1caf0cd43974b69169e405dfc794ef5275a44' and
            result.get('loaded_assembly_sha256') == hashlib.sha256(Path(sys.argv[3]).read_bytes()).hexdigest() ==
            manifest['assets']['lib/net10.0/W.DmProvider.dll'] == 'f70627e5d957f3de86ec33070474ab1543c162c5f6321f93312ad87c4535290f' and
            result.get('loaded_assembly_mvid') == '654b6ddd-b0ff-4408-8e1b-b93bd075c1da')
    inputs = result.get('input_manifest', {})
    require(inputs.get('blob_bytes') == 131089 and inputs.get('blob_seed') == 17 and inputs.get('blob_stride') == 131 and
            inputs.get('clob_utf8_bytes', 0) >= 131072 and inputs.get('parameter_inputs') == 'existing_byte_array_and_string' and
            inputs.get('command_seconds') == 30 and inputs.get('connect_seconds') == 15 and inputs.get('process_seconds') == 90)
    if mode == 'offline':
        require(result.get('status') == 'offline_verified' and result.get('integration') == 'integration_pending' and 'server_identity' not in result)
    else:
        require(result.get('status') == 'upload_ack_characterized' and result.get('integration') == 'real_test_schema' and
                result.get('server_identity') == result.get('server_schema') == result.get('final_verification_identity') ==
                result.get('final_verification_schema') == 'WDM_PROVIDER_TEST' and result.get('cleanup_verified') is True and
                result.get('final_database_state') == 'unique_owned_objects_absent' and
                re.fullmatch(r'\d+(\.\d+){1,5}', result.get('server_version', '')) is not None)
        require(result.get('case') == {'category': 'state_evidence', 'status': 'characterized', 'request_opcode': 26,
                'one_ack_header_per_sent_chunk': True, 'before_decode_coverage': True, 'opaque_body_read': False,
                'response_opcode_policy': 'observed_not_assumed', 'streaming_input_claimed': False})
        sent = result.get('opcode26_sent_count')
        headers = result.get('ack_headers', [])
        require(type(sent) is int and 0 < sent <= 1024 and sent == result.get('opcode26_ack_header_count') == result.get('opcode26_before_decode_count') == len(headers))
        require(all(item.get('RequestOpcode') == 26 and type(item.get('ResponseOpcode')) is int and
                    type(item.get('SqlStatus')) is int and item['SqlStatus'] >= 0 and type(item.get('BodyLength')) is int and item['BodyLength'] >= 0 for item in headers))
        require(result.get('response_opcodes_observed') == sorted({item['ResponseOpcode'] for item in headers}) and
                result.get('body_lengths_observed') == sorted({item['BodyLength'] for item in headers}) and result.get('positive_statuses_observed') is True)
        count = result.get('physical_connections_created')
        modes = result.get('negotiated_encrypt_modes', [])
        require(type(count) is int and count >= 3 and count == result.get('physical_connections_disposed') == len(modes) and
                all(type(item) is int and item == (1 if mode == 'tls' else 0) for item in modes))
        counts = result.get('network_io_counts', {})
        require(set(counts) == {prefix + operation for prefix in ('async_', 'sync_') for operation in ('connect', 'tls', 'send', 'receive')} and
                all(type(value) is int and value >= 0 for value in counts.values()) and
                all(counts['sync_' + operation] == 0 for operation in ('connect', 'tls', 'send', 'receive')) and
                all(counts['async_' + operation] > 0 for operation in ('connect', 'send', 'receive')) and
                (counts['async_tls'] > 0 if mode == 'tls' else counts['async_tls'] == 0))
        objects = result.get('owned_objects', [])
        require(len(objects) == 1 and re.fullmatch(r'T17U_[A-F0-9]{20}', objects[0]) is not None)
        if mode == 'tls':
            require(result.get('tls_revocation_policy') == 'NoCheck_for_isolated_ephemeral_CA_without_CRL')
    print(json.dumps({'task': 'T17-upload-profile', 'status': 'accepted', 'mode': mode, 'phase': 'capability_characterization',
                      'package_version': manifest['version'], 'integration': result['integration'],
                      'streaming_api_acceptance_claimed': False}, separators=(',', ':')))


if __name__ == '__main__':
    try:
        main()
    except Exception:
        print('{"task":"T17-upload-profile","status":"rejected","classification":"envelope_validation_failed"}')
        sys.exit(1)
