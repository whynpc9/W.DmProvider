#!/usr/bin/env python3
"""Validate fixed resource work, all numeric budgets and trends independently of producer claims."""
import hashlib
import json
from pathlib import Path
import statistics
import sys
import uuid


def require(value):
    if not value:
        raise ValueError('invalid_t18_envelope')


def slope_per_100(samples, key):
    pairs = [(sample['CompletedCycles'], sample[key]) for sample in samples if sample.get(key) is not None]
    if not pairs:
        return None
    require(len(pairs) >= 30)
    mean_x = statistics.mean(item[0] for item in pairs)
    mean_y = statistics.mean(item[1] for item in pairs)
    variance = sum((x - mean_x) ** 2 for x, _ in pairs)
    require(variance > 0)
    return 100 * sum((x - mean_x) * (y - mean_y) for x, y in pairs) / variance


def main():
    result = json.loads(Path(sys.argv[1]).read_text())
    manifest = json.loads(Path(sys.argv[2]).read_text())
    mode = sys.argv[4]
    require(mode in ('offline', 'tls', 'release-environment') and result.get('task') == 'T18' and result.get('schema_version') == 1 and
            result.get('mode') == mode and result.get('accepted') is True and result.get('exit_code') == 0 and
            result.get('production_release_accepted') is False and result.get('reference_kind') == 'exact_PackageReference' and
            result.get('package_version') == manifest['version'] and result.get('package_sha256') == manifest['package_sha256'] and
            result.get('loaded_assembly_sha256') == hashlib.sha256(Path(sys.argv[3]).read_bytes()).hexdigest() == manifest['assets']['lib/net10.0/W.DmProvider.dll'] and
            result.get('upstream_pending') == ['T17_shared_large_value', 'shared_cleanup_recovery', 'complete_R3_matrix'])
    uuid.UUID(result['loaded_assembly_mvid'])
    budget = result.get('resource_budget', {})
    require(budget == {'declared_before_measurement': True, 'warmup_seconds': 60, 'measurement_seconds': 600,
            'minimum_completed_cycles': 500, 'maximum_concurrency': 4, 'sample_interval_seconds': 5,
            'minimum_measurement_samples': 121, 'quiescent_seconds': 30, 'minimum_quiescent_samples': 3,
            'total_process_seconds': 900, 'force_gc': False, 'fixed_blob_bytes': 40960,
            'rss_growth_bytes': 268435456, 'gc_heap_growth_bytes': 134217728, 'thread_growth': 16, 'fd_growth': 16,
            'quiescent_fd_growth': 8, 'window_seconds': 60, 'rss_half_window_median_growth_bytes': 67108864,
            'gc_heap_half_window_median_growth_bytes': 33554432, 'second_half_thread_growth_per_100_cycles': 1.0,
            'second_half_fd_growth_per_100_cycles': 1.0, 'scope': 'TLS_resource_soak_not_T17_large_value_or_full_R3_matrix'})
    if mode == 'offline':
        require(result.get('status') == 'offline_verified' and result.get('integration') == 'integration_pending')
        cases = result.get('first_init_cases', [])
        points = {'normal', 'should_listen', 'instrument_published', 'sample', 'started', 'stopped', 'measurement'}
        require(len(cases) == 7 and {item.get('point') for item in cases} == points)
        for case in cases:
            require(case.get('category') == 'Contract' and case.get('feature') == 'Diagnostics' and
                    case.get('actual_public_begin_commit') is True and case.get('transaction_outcome') == 'Committed' and
                    case.get('commit_frames') == 1 and case.get('scripted_fixed_ack') is True and case.get('real_database_claimed') is False and
                    case.get('caller_context_leaked') is False and case.get('final_queue_depth') == 0 and case.get('worker_starts') == 1 and
                    (case.get('initialization_state') == 1 and case.get('callback_failures') == 0 if case['point'] == 'normal' else case.get('callback_failures', 0) > 0))
    elif mode == 'release-environment':
        require(result.get('status') == 'release_environment_preflight_only' and result.get('server_identity_verified') is False)
    else:
        require(result.get('status') == 'tls_resource_scope_verified' and result.get('integration') == 'TLS_only_upstream_pending' and
                result.get('resource_measurement_complete') is True and result.get('explicit_transport') == 'RequireTls' and
                result.get('server_identity') == result.get('server_schema') == result.get('final_verification_identity') ==
                result.get('final_verification_schema') == 'WDM_PROVIDER_TEST' and result.get('cleanup_verified') is True and
                result.get('final_database_state') == 'unique_owned_objects_absent' and result.get('measurement_completed_cycles', 0) >= 500 and
                0 < result.get('peak_concurrency', 0) <= 4 and result.get('confirmed_commits', 0) > 0 and result.get('confirmed_rollbacks', 0) > 0)
        samples = result.get('samples', [])
        measured = [sample for sample in samples if sample.get('Phase') == 'measurement']
        quiet = [sample for sample in samples if sample.get('Phase') == 'quiescent']
        baseline = result.get('measurement_baseline', {})
        require(len(measured) >= 121 and len(quiet) >= 3 and baseline.get('Phase') == 'baseline' and baseline.get('ElapsedSeconds', 0) >= 60 and
                measured[-1]['ElapsedSeconds'] - measured[0]['ElapsedSeconds'] >= 599.5 and
                quiet[0]['ElapsedSeconds'] - measured[-1]['ElapsedSeconds'] >= 29.5 and
                quiet[-1]['ElapsedSeconds'] - quiet[0]['ElapsedSeconds'] >= 9.5)
        require(all(b['ElapsedSeconds'] - a['ElapsedSeconds'] >= 4.5 for a, b in zip(measured, measured[1:])))
        require(all(sample.get('QueueDepth', -1) in range(4097) and sample.get('QueuePeak', -1) in range(4097) and
                    sample.get('RegistryEntries', -1) in range(129) for sample in samples))
        unavailable = []
        for key, growth in (('RssBytes', 268435456), ('GcHeapBytes', 134217728), ('Threads', 16), ('FileDescriptors', 16)):
            if baseline.get(key) is None:
                require(key != 'GcHeapBytes' and all(sample.get(key) is None for sample in measured + quiet))
                unavailable.append(key)
            else:
                require(all(sample.get(key) is not None and sample[key] - baseline[key] <= growth for sample in measured))
        if baseline.get('FileDescriptors') is not None:
            require(all(sample.get('FileDescriptors') is not None and sample['FileDescriptors'] <= baseline['FileDescriptors'] + 8 for sample in quiet))
        windows = []
        first_time = measured[0]['ElapsedSeconds']
        for start in range(0, 600, 60):
            window = [sample for sample in measured if start <= sample['ElapsedSeconds'] - first_time < start + 60]
            first = [sample for sample in window if sample['ElapsedSeconds'] - first_time < start + 30]
            second = [sample for sample in window if sample['ElapsedSeconds'] - first_time >= start + 30]
            require(len(first) >= 4 and len(second) >= 4)
            row = {'start_seconds': start}
            for key, growth in (('RssBytes', 67108864), ('GcHeapBytes', 33554432)):
                if baseline.get(key) is None:
                    row[key] = 'unavailable'
                else:
                    difference = statistics.median(sample[key] for sample in second) - statistics.median(sample[key] for sample in first)
                    require(difference <= growth)
                    row[key] = difference
            windows.append(row)
        half = [sample for sample in measured if sample['ElapsedSeconds'] - first_time >= 300]
        thread_slope = slope_per_100(half, 'Threads')
        fd_slope = slope_per_100(half, 'FileDescriptors')
        require((thread_slope is None or thread_slope <= 1.0) and (fd_slope is None or fd_slope <= 1.0))
        final = result.get('final_diagnostics_snapshot', {})
        require(all(final.get(key) == 0 for key in ('PoolCreating', 'PoolLeased', 'PoolClosing', 'PoolWaiting', 'QueueDepth')) and
                final.get('QueueCapacity') == 4096 and final.get('QueuePeak', 4097) <= 4096 and final.get('RegistryEntries', 129) <= 128 and
                result.get('dropped_delta') == 0 and result.get('physical_connections_created', 0) > 0 and
                result.get('physical_connections_created') == result.get('physical_connections_disposed') == result.get('diagnostic_created_delta') == result.get('diagnostic_closed_delta'))
        listener = result.get('diagnostic_listener', {})
        require(listener.get('activities', 0) > 0 and listener.get('measurements', 0) > 0 and listener.get('invalid_tag') is False and
                listener.get('caller_context_leaked') is False and listener.get('finite_tag_combinations', 129) <= 128)
        cumulative = result.get('final_public_diagnostics', {}).get('cumulative_counts', {})
        def total(name, tag=None):
            return sum(value for key, value in cumulative.items() if key.startswith(name + '|') and (tag is None or tag in key))
        require(total('wdm.connection.created') == final.get('ConnectionsCreated') and total('wdm.connection.closed') == final.get('ConnectionsClosed') and
                total('wdm.pool.acquire.total') == final.get('Acquires') and total('wdm.operation.total') == final.get('Operations') and
                total('wdm.network.bytes', 'direction=sent') == final.get('SentBytes') and total('wdm.network.bytes', 'direction=received') == final.get('ReceivedBytes') and
                total('wdm.lob.chunks', 'direction=read') == final.get('LobReadChunks') and total('wdm.lob.chunks', 'direction=write') == final.get('LobWriteChunks'))
        counts = result.get('network_io_counts', {})
        require(set(counts) == {prefix + operation for prefix in ('async_', 'sync_') for operation in ('connect', 'tls', 'send', 'receive')} and
                all(type(value) is int and value >= 0 for value in counts.values()) and
                all(counts['sync_' + operation] == 0 for operation in ('connect', 'tls', 'send', 'receive')) and
                all(counts['async_' + operation] > 0 for operation in ('connect', 'tls', 'send', 'receive')))
        result_summary = {'unavailable_native_metrics': unavailable, 'window_median_deltas': windows,
                          'second_half_threads_per_100_cycles': thread_slope, 'second_half_fds_per_100_cycles': fd_slope}
        print(json.dumps({'task': 'T18', 'status': 'resource_budget_verified', 'mode': mode, 'package_version': manifest['version'],
                          'scoped_acceptance': 'TLS_only_upstream_pending', 'budgets': result_summary}, separators=(',', ':')))
        return
    print(json.dumps({'task': 'T18', 'status': 'accepted', 'mode': mode, 'package_version': manifest['version'],
                      'production_release_accepted': False}, separators=(',', ':')))


if __name__ == '__main__':
    try:
        main()
    except Exception:
        print('{"task":"T18","status":"rejected","classification":"envelope_or_resource_budget_failed"}')
        sys.exit(1)
