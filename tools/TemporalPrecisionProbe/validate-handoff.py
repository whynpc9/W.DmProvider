#!/usr/bin/env python3
"""Validate O typed seed -> W cross-decode -> exact O cleanup. CAST literals stay characterization."""
import argparse
import json
from pathlib import Path


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('seed')
    parser.add_argument('read')
    parser.add_argument('cleanup')
    parser.add_argument('ownership')
    parser.add_argument('--output', required=True)
    args = parser.parse_args()
    expected = json.loads(Path(__file__).with_name('expected-identities.json').read_text())
    seed, read, cleanup, owner = [json.loads(Path(getattr(args, key)).read_text()) for key in ('seed', 'read', 'cleanup', 'ownership')]
    issues = []
    samples = {'utc', 'plus08', 'minus05', 'plus0530'}
    for label, lane, report, status in [('seed', 'O', seed, 'seed_ready'), ('read', 'W-frozen-R1', read, 'cross_decode_complete'), ('cleanup', 'O', cleanup, 'cleanup_complete')]:
        identity = expected[lane]
        if report.get('lane') != lane or report.get('status') != status or report.get('issues') != []:
            issues.append({'phase': label, 'reason': 'phase_not_complete'})
        for key in ('package_version', 'package_sha256', 'assembly_sha256', 'assembly_mvid', 'asset'):
            if report.get(key) != identity[key]:
                issues.append({'phase': label, 'reason': 'frozen_identity_mismatch', 'field': key})
        if not report.get('package_identity_verified') or not report.get('server_identity_verified') or report.get('server_version') != '8.1.5.60':
            issues.append({'phase': label, 'reason': 'target_identity_or_profile_mismatch'})
        if report.get('public_W_seven_digit_acceptance') is not False:
            issues.append({'phase': label, 'reason': 'public_W_write_claim_invalid'})
    nonce = owner.get('Nonce') or ''
    table = owner.get('Table')
    if len(nonce) != 32 or any(char not in '0123456789ABCDEF' for char in nonce) or table != 'TP7H_' + nonce[:24]:
        issues.append({'reason': 'ownership_nonce_or_name_mismatch'})
    if owner.get('State') != 'cleanup_verified' or not owner.get('AbsentBeforeCreate') or not owner.get('IdentityVerified') or owner.get('ServerVersion') != '8.1.5.60':
        issues.append({'reason': 'ownership_final_state_mismatch'})
    if any(owner.get(field) != expected['O'][key] for field, key in [('OwnerPackageHash', 'package_sha256'), ('OwnerDllHash', 'assembly_sha256'), ('OwnerMvid', 'assembly_mvid')]):
        issues.append({'reason': 'owner_package_binding_mismatch'})
    if not all(report.get('owned_table') == table for report in (seed, read, cleanup)):
        issues.append({'reason': 'cross_process_table_mismatch'})
    if seed.get('ownership_manifest_sha256') != read.get('ownership_manifest_sha256'):
        issues.append({'reason': 'manifest_changed_between_seed_and_read'})
    if not seed.get('handoff_ready') or not seed.get('cleanup_required') or not read.get('cleanup_required'):
        issues.append({'reason': 'handoff_cleanup_contract_missing'})
    if not cleanup.get('cleanup_verified') or cleanup.get('final_database_state') != 'unique_owned_table_absent':
        issues.append({'reason': 'final_independent_absence_unverified'})
    parameters = seed.get('typed_parameter_cases', [])
    if len(parameters) != 4 or {case.get('sample') for case in parameters} != samples:
        issues.append({'reason': 'official_seed_case_set_mismatch'})
    for case in parameters:
        metadata = case.get('metadata', [])
        if not case.get('prepared') or not case.get('written') or case.get('error') is not None or len(metadata) != 2 or [item.get('CType') for item in metadata] != [26, 27] or any(item.get('Scale') != 7 or item.get('TypeFlag') != 1 for item in metadata):
            issues.append({'reason': 'official_typed_seed_not_verified', 'case': case.get('sample')})
    for label, cases in [('official', seed.get('typed_readback_cases', [])), ('W', read.get('cross_typed_readback_cases', []))]:
        if len(cases) != 4 or {case.get('sample') for case in cases} != samples:
            issues.append({'phase': label, 'reason': 'read_case_set_mismatch'})
        for case in cases:
            metadata = case.get('metadata', [])
            if len(metadata) != 2 or [item.get('CType') for item in metadata] != [26, 27] or any(item.get('Scale') != 7 or item.get('Mask') != 0 for item in metadata):
                issues.append({'phase': label, 'reason': 'extended_read_metadata_mismatch', 'case': case.get('sample')})
            if not case.get('server_date_exact') or not case.get('server_offset_exact'):
                issues.append({'phase': label, 'reason': 'server_FF7_offset_mismatch', 'case': case.get('sample')})
            if label == 'W':
                ticks, offset = case.get('expected_ticks'), case.get('expected_offset_minutes')
                for getter in ('typed_date', 'generic_date'):
                    value = case.get(getter, {})
                    if value.get('Ticks') != ticks or value.get('Kind') != 'Unspecified' or value.get('Type') != 'System.DateTime':
                        issues.append({'reason': 'W_date_cross_decode_mismatch', 'case': case.get('sample'), 'getter': getter})
                for getter in ('typed_offset', 'generic_offset'):
                    value = case.get(getter, {})
                    if value.get('Ticks') != ticks or value.get('OffsetMinutes') != offset or value.get('Type') != 'System.DateTimeOffset':
                        issues.append({'reason': 'W_DTO_cross_decode_mismatch', 'case': case.get('sample'), 'getter': getter})
                if not case.get('client_text_parse_exact'):
                    issues.append({'reason': 'W_EF_GetString_cross_decode_mismatch', 'case': case.get('sample')})
    if seed.get('rows_before_handoff') != read.get('rows_after_read'):
        issues.append({'reason': 'cross_read_changed_row_state'})
    literal_cases = seed.get('literal_characterization', [])
    if len(literal_cases) != 4 or {case.get('sample') for case in literal_cases} != samples:
        issues.append({'reason': 'literal_characterization_missing'})
    # Read literal differences for comparison; never treat a rounded CAST literal as the decoder oracle.
    literal_summary = [{'sample': case.get('sample'), 'server_date_exact': (case.get('read') or {}).get('server_date_exact'),
                        'server_offset_exact': (case.get('read') or {}).get('server_offset_exact'),
                        'error_kind': (case.get('error') or {}).get('kind')} for case in literal_cases]
    encoders = [{case['sample']: case for case in report.get('pure_encoders', [])} for report in (seed, read)]
    encoder_evidence = 'not_available'
    if all(set(values) == samples for values in encoders):
        encoder_evidence = 'independent_frozen_helpers_equal_and_one_tick'
        for name in sorted(samples):
            for field in ('date_hex', 'offset_hex', 'date_length', 'offset_length', 'date_nanoseconds', 'offset_nanoseconds'):
                if encoders[0][name].get(field) != encoders[1][name].get(field):
                    issues.append({'reason': 'pure_encoder_comparison_mismatch', 'case': name, 'field': field})
            if any(values[name].get('date_nanoseconds') != 100 or values[name].get('offset_nanoseconds') != 100 for values in encoders):
                issues.append({'reason': 'pure_encoder_one_tick_missing', 'case': name})
    result = {'schema_version': 1, 'task': 'T12', 'status': 'cross_decode_verified' if not issues else 'cross_decode_failed',
              'scope': 'O_public_typed_seven_digit_seed_W_decode_and_exact_cleanup',
              'public_W_seven_digit_acceptance': False, 'literal_evidence': 'official_CAST_characterization_only',
              'literal_characterization': literal_summary, 'encoder_evidence': encoder_evidence, 'issues': issues}
    Path(args.output).write_text(json.dumps(result, indent=2) + '\n')
    print(json.dumps({'status': result['status'], 'issue_count': len(issues), 'public_W_seven_digit_acceptance': False}))
    return 0 if not issues else 1


if __name__ == '__main__':
    raise SystemExit(main())
