#!/usr/bin/env python3
"""Validate safe O/W probe reports; never infer public W write acceptance from decode."""
import argparse
import json
from pathlib import Path


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('official')
    parser.add_argument('candidate')
    parser.add_argument('--output', required=True)
    args = parser.parse_args()
    identities = json.loads(Path(__file__).with_name('expected-identities.json').read_text())
    reports = [json.loads(Path(args.official).read_text()), json.loads(Path(args.candidate).read_text())]
    issues = []
    required_samples = {'utc', 'plus08', 'minus05', 'plus0530'}
    for lane, report in zip(('O', 'W-frozen-R1'), reports):
        expected = identities[lane]
        for key in ('package_version', 'package_sha256', 'assembly_sha256', 'assembly_mvid', 'asset'):
            if report.get(key) != expected[key]:
                issues.append({'lane': lane, 'reason': 'frozen_identity_mismatch', 'field': key})
        if report.get('lane') != lane or report.get('status') != 'diagnostic_complete':
            issues.append({'lane': lane, 'reason': 'probe_not_complete'})
        if report.get('public_W_seven_digit_acceptance') is not False:
            issues.append({'lane': lane, 'reason': 'invalid_public_acceptance_claim'})
        if not report.get('package_identity_verified') or not report.get('server_identity_verified') or not report.get('cleanup_verified'):
            issues.append({'lane': lane, 'reason': 'identity_or_cleanup_unverified'})
        if report.get('server_version') != '8.1.5.60':
            issues.append({'lane': lane, 'reason': 'target_server_version_mismatch'})
        if report.get('final_database_state') != 'unique_owned_table_absent' or report.get('rows_before_cleanup') != 4:
            issues.append({'lane': lane, 'reason': 'final_state_mismatch'})
        if report.get('issues') != []:
            issues.append({'lane': lane, 'reason': 'probe_reported_issues'})
        cases = report.get('typed_parameter_cases', [])
        if {case.get('sample') for case in cases} != required_samples or len(cases) != 4:
            issues.append({'lane': lane, 'reason': 'parameter_case_set_mismatch'})
        for case in cases:
            if not case.get('prepared') or not case.get('execute_attempted'):
                issues.append({'lane': lane, 'reason': 'prepare_or_execute_not_attempted', 'case': case.get('sample')})
            metadata = case.get('metadata', [])
            if len(metadata) != 2 or [item.get('CType') for item in metadata] != [26, 27] or any(item.get('Scale') != 7 or item.get('TypeFlag') != 1 for item in metadata):
                issues.append({'lane': lane, 'reason': 'fixed_describe_not_extended_seven_digit', 'case': case.get('sample')})
            if lane == 'O' and (not case.get('written') or case.get('error') is not None):
                issues.append({'lane': lane, 'reason': 'official_public_write_failed', 'case': case.get('sample')})
            if lane != 'O' and (case.get('written') or (case.get('error') or {}).get('kind') not in ('System.InvalidOperationException', 'System.NotSupportedException')):
                issues.append({'lane': lane, 'reason': 'frozen_public_rejection_characterization_mismatch', 'case': case.get('sample')})
        reads = report.get('readback_cases', [])
        if {case.get('sample') for case in reads} != required_samples or len(reads) != 4:
            issues.append({'lane': lane, 'reason': 'read_case_set_mismatch'})
        for case in reads:
            if not case.get('server_date_exact') or not case.get('server_offset_exact'):
                issues.append({'lane': lane, 'reason': 'server_FF7_or_offset_mismatch', 'case': case.get('sample')})
            if lane != 'O':
                ticks = case.get('expected_ticks')
                offset = case.get('expected_offset_minutes')
                date = case.get('typed_date', {})
                generic_date = case.get('generic_date', {})
                if date.get('Ticks') != ticks or date.get('Kind') != 'Unspecified' or generic_date.get('Ticks') != ticks or generic_date.get('Kind') != 'Unspecified' or not case.get('client_text_parse_exact'):
                    issues.append({'lane': lane, 'reason': 'W_typed_date_or_EF_text_mismatch', 'case': case.get('sample')})
                for getter in ('typed_offset', 'generic_offset'):
                    value = case.get(getter, {})
                    if value.get('Ticks') != ticks or value.get('OffsetMinutes') != offset or value.get('Type') != 'System.DateTimeOffset':
                        issues.append({'lane': lane, 'reason': 'W_DTO_decode_mismatch', 'getter': getter, 'case': case.get('sample')})
    o_vectors = {item['sample']: item for item in reports[0].get('pure_encoders', [])}
    w_vectors = {item['sample']: item for item in reports[1].get('pure_encoders', [])}
    encoder_evidence = 'not_available'
    if set(o_vectors) == required_samples and set(w_vectors) == required_samples:
        encoder_evidence = 'independent_frozen_helper_comparison'
        for name in sorted(required_samples):
            for field in ('date_length', 'offset_length', 'date_hex', 'offset_hex', 'date_hash', 'offset_hash'):
                if o_vectors[name].get(field) != w_vectors[name].get(field):
                    issues.append({'reason': 'synthetic_encoder_mismatch', 'case': name, 'field': field})
    result = {
        'schema_version': 1, 'task': 'T12', 'status': 'diagnostic_verified' if not issues else 'diagnostic_failed',
        'verified_scope': 'official_public_seven_digit_write_and_frozen_W_extended_literal_decode',
        'public_W_seven_digit_acceptance': False, 'encoder_evidence': encoder_evidence, 'issues': issues
    }
    Path(args.output).write_text(json.dumps(result, indent=2) + '\n')
    print(json.dumps({'status': result['status'], 'issue_count': len(issues), 'public_W_seven_digit_acceptance': False}))
    return 0 if not issues else 1


if __name__ == '__main__':
    raise SystemExit(main())
