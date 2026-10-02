#!/usr/bin/env python3
import hashlib
import json
from pathlib import Path
import sys

OLD = '3eaa6a45872304d07489e713d3e913b92547367f78e2dd86327de6407a50e36c'
NEW = 'fdea777db6150aeac6ecbe8c69a17329d3089b1364ee5c7f9b2cf4637e52f431'
NAMES = ['unit_literal', 'unit_int32_parameter', 'catalog_literal', 'catalog_varchar_parameter',
         'scalar_unit_literal', 'scalar_catalog_literal', 'scalar_catalog_varchar_parameter']


def require(condition):
    if not condition:
        raise ValueError('diagnostic_envelope_invalid')


def validate(result):
    lane, mode = result['lane'], result.get('mode')
    require(mode in ('health', 'readers') and lane in ('old-before', 'new', 'old-after') and
            result.get('schema_version') == 1 and result.get('task') == 'T14-diagnostics' and
            result.get('accepted') is True and result.get('exit_code') == 0 and result.get('read_only') is True and
            result.get('ddl_performed') is False and result.get('objects_created') == 0 and
            result.get('recovered_table') == 'T14_921060586F914E658A7FD736' and
            result.get('connect_timeout_seconds') == 20 and result.get('command_timeout_seconds') == 15 and
            result.get('explicit_transport') == 'PlaintextAllowed' and
            result.get('loaded_assembly_sha256') == (NEW if lane == 'new' else OLD) and
            result.get('sent_opcode_hook_supported') is (lane == 'new') and
            result.get('status') == ('diagnostic_health_verified' if mode == 'health' else 'diagnostic_readers_verified'))
    cases = result.get('cases', [])
    require([case.get('case') for case in cases] == (NAMES if mode == 'health' else ['reader_baseline', 'reader_old_execute_token_canceled']))
    if mode == 'readers':
        require(result.get('three_health_lanes_verified') is True and len(result.get('health_gate_sha256', '')) == 64 and
                result.get('health_proof_sha256') == [
                    'a34f350d0f7d01613c1870fad1161fb6bb3bd4d513c2518338b69e5080fbbe10',
                    'ae6c3ccb0822224db0a989601f45197309d512f831a960823d48e9943481a99e',
                    '32039426eeadb8c61ae06ebc55703bf7cecff7c967da79a0f54f651a2798d5ec'])
    for case in cases:
        scalar = case['case'].startswith('scalar_')
        require(case.get('status') == 'pass' and case.get('substage') == 'Connection.Close' and
                case.get('identity_verified') is True and case.get('schema_verified') is True and
                case.get('negotiated_encrypt_mode') == 0 and case.get('execute_returned') is True and
                case.get('elapsed_milliseconds', -1) >= 0 and case.get('numeric_frames') and
                not case.get('failure') and not case.get('failure_substage'))
        if mode == 'health':
            require(case.get('api') == ('ExecuteScalarAsync' if scalar else 'ExecuteReaderAsync_first_row_then_Close') and
                    case.get('observed_integer') == (1 if 'unit_' in case['case'] else 0))
        else:
            require(case.get('api') == 'ExecuteReaderAsync_new_Read_token' and case.get('rows_read') == case.get('rows_validated') == 2048 and
                    case.get('actual_fetch_frames', 0) > 0 and type(case.get('cached_first_read')) is bool and
                    case.get('old_execute_token_canceled') is (case['case'] == 'reader_old_execute_token_canceled'))
        require(all(set(frame) == {'RequestOpcode', 'ResponseOpcode', 'SqlCode', 'BodyLength'} and
                    all(type(value) is int for value in frame.values()) for frame in case['numeric_frames']))
        require(all(type(opcode) is int for opcode in case.get('numeric_sent_opcodes', [])))
        require(case.get('invocation_samples'))
        if lane != 'new':
            require(case.get('numeric_sent_opcodes') == [] and all(sample.get('RequestOpcode') is None for sample in case['invocation_samples']))
        if mode == 'readers':
            continue
        if scalar:
            require(case.get('implicit_reader_progress_observable') is False)
        else:
            require(case.get('rows_read') == case.get('rows_validated') == 1)


def main():
    result = json.loads(Path(sys.argv[1]).read_text())
    manifest = json.loads(Path(sys.argv[2]).read_text())
    loaded = hashlib.sha256(Path(sys.argv[3]).read_bytes()).hexdigest()
    require(result.get('package_version') == manifest['version'] and result.get('package_sha256') == manifest['package_sha256'] and
            result.get('loaded_assembly_sha256') == loaded == manifest['assets']['lib/net10.0/W.DmProvider.dll'] and
            len(result.get('loaded_assembly_mvid', '')) == 36 and result.get('reference_kind') == 'exact_PackageReference')
    validate(result)
    print(json.dumps({'task': 'T14-diagnostics', 'mode': result['mode'], 'lane': result['lane'], 'status': result['status'],
                      'exit_code': 0, 'loaded_assembly_sha256': loaded, 'cases': len(result['cases'])}))


if __name__ == '__main__':
    try:
        main()
    except Exception as error:
        print(json.dumps({'status': 'rejected', 'classification': 'diagnostic_validation_failed', 'type': type(error).__name__}))
        sys.exit(1)
