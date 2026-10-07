"""Small numeric final-snapshot fixtures; no subprocess, filesystem writes, dotnet, or DB."""
import json
import unittest

import validate as validator


# Declare the public contract independently so an omitted producer/validator bucket cannot hide here.
OPERATIONS = ('unknown', 'connect', 'execute', 'prepare', 'fetch', 'commit', 'rollback',
              'reader_close', 'metadata', 'transaction_begin')
RESULTS = ('unknown', 'success', 'server_error', 'canceled', 'timeout', 'outcome_unknown',
           'transport_error', 'rejected')


def operation_key(operation, outcome):
    return 'wdm.operation.total|operation=' + operation + ';result=' + outcome


def healthy_result():
    counts = {operation_key(operation, outcome): 0 for operation in OPERATIONS for outcome in RESULTS}
    counts[operation_key('connect', 'success')] = 4
    for operation in ('execute', 'fetch', 'commit', 'rollback', 'transaction_begin'):
        counts[operation_key(operation, 'success')] = 8
    counts.update({'wdm.pool.acquire.total|result=success': 4,
                   'wdm.pool.acquire.total|result=canceled': 1,
                   'wdm.pool.acquire.total|result=rejected': 1})
    return {'physical_connections_created': 4,
            'final_public_diagnostics': {'cumulative_counts': counts, 'invalid_tag': False,
                                         'caller_context_leaked': False, 'finite_tag_combinations': 24,
                                         'activities': 8, 'measurements': 16}}


class DiagnosticHealthTests(unittest.TestCase):
    def setUp(self):
        self.result = healthy_result()
        self.counts = self.result['final_public_diagnostics']['cumulative_counts']

    def reject(self):
        with self.assertRaisesRegex(validator.DiagnosticHealthError, '^healthy_operation_diagnostics_failed$'):
            validator.validate_diagnostic_health(self.result)

    def test_complete_healthy_final_matrix_passes(self):
        validator.validate_diagnostic_health(self.result)

    def test_pool_canceled_and_rejected_control_counts_remain_legal(self):
        self.counts['wdm.pool.acquire.total|result=canceled'] = 2
        self.counts['wdm.pool.acquire.total|result=rejected'] = 3
        validator.validate_diagnostic_health(self.result)

    def test_duplicate_connect_success_count_rejected(self):
        self.counts[operation_key('connect', 'success')] = 8
        self.reject()

    def test_false_execute_transport_error_rejected(self):
        self.counts[operation_key('execute', 'transport_error')] = 1
        self.reject()

    def test_false_fetch_transport_error_rejected(self):
        self.counts[operation_key('fetch', 'transport_error')] = 1
        self.reject()

    def test_every_operation_transport_error_rejected(self):
        for operation in OPERATIONS:
            with self.subTest(operation=operation):
                self.counts[operation_key(operation, 'transport_error')] = 1
                self.reject()
                self.counts[operation_key(operation, 'transport_error')] = 0

    def test_every_non_success_connect_outcome_rejected(self):
        for outcome in RESULTS:
            if outcome != 'success':
                with self.subTest(outcome=outcome):
                    self.counts[operation_key('connect', outcome)] = 1
                    self.reject()
                    self.counts[operation_key('connect', outcome)] = 0

    def test_missing_zero_execute_error_bucket_rejected(self):
        del self.counts[operation_key('execute', 'transport_error')]
        self.reject()

    def test_missing_connect_success_bucket_rejected(self):
        del self.counts[operation_key('connect', 'success')]
        self.reject()

    def test_missing_final_snapshot_rejected(self):
        del self.result['final_public_diagnostics']
        self.reject()

    def test_missing_cumulative_counts_rejected(self):
        del self.result['final_public_diagnostics']['cumulative_counts']
        self.reject()

    def test_negative_bucket_rejected(self):
        self.counts[operation_key('execute', 'success')] = -1
        self.reject()

    def test_boolean_bucket_rejected(self):
        self.counts[operation_key('execute', 'transport_error')] = False
        self.reject()

    def test_float_bucket_rejected(self):
        self.counts[operation_key('execute', 'transport_error')] = 0.0
        self.reject()

    def test_string_bucket_rejected(self):
        self.counts[operation_key('execute', 'transport_error')] = '0'
        self.reject()

    def test_null_bucket_rejected(self):
        self.counts[operation_key('execute', 'transport_error')] = None
        self.reject()

    def test_int64_overflow_bucket_rejected(self):
        self.counts[operation_key('execute', 'success')] = 9223372036854775808
        self.reject()

    def test_duplicate_operation_metadata_rejected(self):
        self.counts['wdm.operation.total|operation=connect;operation=connect;result=success'] = 4
        self.reject()

    def test_duplicate_result_metadata_rejected(self):
        self.counts['wdm.operation.total|operation=execute;result=success;result=success'] = 8
        self.reject()

    def test_noncanonical_tag_order_rejected(self):
        self.counts['wdm.operation.total|result=success;operation=connect'] = 4
        self.reject()

    def test_extra_metadata_rejected(self):
        self.counts[operation_key('connect', 'success') + ';terminal=True'] = 4
        self.reject()

    def test_unknown_operation_or_outcome_rejected(self):
        for key in (operation_key('pool_acquire', 'success'), operation_key('execute', 'other')):
            with self.subTest(key=key):
                self.counts[key] = 0
                self.reject()
                del self.counts[key]

    def test_malformed_operation_instrument_rejected(self):
        self.counts['wdm.operation.totaloperation=connect;result=success'] = 4
        self.reject()

    def test_nonstring_cumulative_key_rejected(self):
        self.counts[1] = 0
        self.reject()

    def test_invalid_physical_created_rejected(self):
        for value in (None, -1, 0, False, True, 4.0, '4', 9223372036854775808):
            with self.subTest(value=value):
                self.result['physical_connections_created'] = value
                self.reject()

    def test_missing_physical_created_rejected(self):
        del self.result['physical_connections_created']
        self.reject()

    def test_invalid_final_container_types_rejected(self):
        for value in (None, [], 0):
            with self.subTest(value=value):
                self.result['final_public_diagnostics'] = value
                self.reject()

    def test_invalid_cumulative_container_types_rejected(self):
        for value in (None, [], 0):
            with self.subTest(value=value):
                self.result['final_public_diagnostics']['cumulative_counts'] = value
                self.reject()

    def test_invalid_pool_counter_type_rejected(self):
        self.counts['wdm.pool.acquire.total|result=canceled'] = True
        self.reject()

    def test_strict_json_healthy_roundtrip_passes(self):
        validator.validate_diagnostic_health(validator.strict_json(json.dumps(self.result)))

    def test_duplicate_json_operation_bucket_rejected_before_overwrite(self):
        key = json.dumps(operation_key('connect', 'success'))
        with self.assertRaisesRegex(ValueError, '^duplicate_json_key$'):
            validator.strict_json('{"cumulative_counts":{' + key + ':4,' + key + ':4}}')

    def test_duplicate_json_snapshot_metadata_rejected_before_overwrite(self):
        with self.assertRaisesRegex(ValueError, '^duplicate_json_key$'):
            validator.strict_json('{"final_public_diagnostics":{},"final_public_diagnostics":{}}')

    def test_missing_final_false_flags_rejected(self):
        final = self.result['final_public_diagnostics']
        for name in ('invalid_tag', 'caller_context_leaked'):
            with self.subTest(field=name):
                del final[name]
                self.reject()
                final[name] = False

    def test_invalid_final_false_flags_rejected(self):
        final = self.result['final_public_diagnostics']
        for name in ('invalid_tag', 'caller_context_leaked'):
            for index, value in enumerate((True, None, 'false', 0, [])):
                with self.subTest(field=name, case=index):
                    final[name] = value
                    self.reject()
            final[name] = False

    def test_missing_final_count_metadata_rejected(self):
        final = self.result['final_public_diagnostics']
        for name in ('finite_tag_combinations', 'activities', 'measurements'):
            with self.subTest(field=name):
                original = final.pop(name)
                self.reject()
                final[name] = original

    def test_invalid_final_positive_count_metadata_rejected(self):
        final = self.result['final_public_diagnostics']
        for name in ('activities', 'measurements'):
            for index, value in enumerate((0, -1, False, True, 1.0, '1', None, 9223372036854775808)):
                with self.subTest(field=name, case=index):
                    final[name] = value
                    self.reject()
            final[name] = 8

    def test_invalid_final_finite_count_metadata_rejected(self):
        final = self.result['final_public_diagnostics']
        for index, value in enumerate((-1, 129, False, True, 0.0, '0', None, 9223372036854775808)):
            with self.subTest(case=index):
                final['finite_tag_combinations'] = value
                self.reject()

    def test_final_finite_count_exact_boundaries_pass(self):
        final = self.result['final_public_diagnostics']
        for value in (0, 128):
            with self.subTest(case=value):
                final['finite_tag_combinations'] = value
                validator.validate_diagnostic_health(self.result)


if __name__ == '__main__':
    unittest.main()
