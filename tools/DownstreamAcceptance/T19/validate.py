#!/usr/bin/env python3
"""Validate rich per-lane identity tuples and preserve pending profiles; status alone never closes T19."""
import argparse
import json
import re
import sys
from common import *
from run import verify, check_identity, retained_asset_root, verify_retained_asset, verify_execution_overlay, strict_trx


def require(value, reason):
    if not value: raise ValueError(reason)


def reread_lane_record(root, item):
    matching = []
    for path in sorted(root.glob('results-*/lane.json')):
        if json.loads(path.read_text()) == item: matching.append(path)
    require(len(matching) == 1, 'immutable_lane_record_summary_mismatch')
    return matching[0].parent, sha(matching[0])


def reread_host_proofs(evidence, item, candidate, retain):
    host_paths = sorted(evidence.glob('*-driver.json'))
    nested_paths = sorted((evidence / 'hosts').glob('*.json'))
    require(all(not path.is_symlink() and path.is_file() for path in host_paths + nested_paths), 'host_proof_file_path_invalid')
    host = [json.loads(path.read_text()) for path in host_paths]
    nested = [json.loads(path.read_text()) for path in nested_paths]
    require(host == item['testhost_proofs'] and nested == item['nested_cli_proofs'] and bool(host), 'host_proof_file_summary_mismatch')
    require(all(check_identity(proof, candidate) for proof in host + nested), 'actual_host_file_identity_failed')
    if retain:
        assets = retained_asset_root(evidence.parent, item.get('retained_asset_root'))
        names = [proof.get('retained_driver_asset') for proof in host + nested]
        require(len(names) == len(set(names)), 'retained_driver_asset_reused_by_host')
        for proof in host + nested: verify_retained_asset(proof, candidate, assets)
    else:
        require(all(proof.get('schema_version') == 1 for proof in host + nested), 'original_host_proof_schema_mismatch')
    return [{'path': str(path.relative_to(evidence)), 'sha256': sha(path)} for path in host_paths + nested_paths]


def reread_audits(evidence, item):
    if item['lane'] == 'unit': return
    if item['lane'] == 'audit':
        require(json.loads((evidence / 'audit-permissions.json').read_text()) == item['before_audit'] == item['after_audit'], 'audit_file_summary_mismatch')
    else:
        require(json.loads((evidence / 'audit-before.json').read_text()) == item['before_audit'] and
                json.loads((evidence / 'audit-after.json').read_text()) == item['after_audit'], 'audit_file_summary_mismatch')


def main():
    parser = argparse.ArgumentParser(); parser.add_argument('--output', required=True); parser.add_argument('--profile', choices=('shared', 'tls'), required=True)
    parser.add_argument('--base-output'); parser.add_argument('--execution-overlay')
    args = parser.parse_args(); run = bounded(args.output); manifest = json.loads((run / 'manifest.json').read_text()); summary = json.loads((run / 'summary.json').read_text())
    require(bool(args.base_output) == bool(args.execution_overlay), 'execution_overlay_and_base_required_together')
    overlay = None; base_run = None; base_manifest = None; base_summary = None
    if args.execution_overlay:
        overlay = verify_execution_overlay(run, manifest, Path(args.execution_overlay))
        base_run = bounded(args.base_output); require(base_run != run, 'independent_cli_run_required')
        frozen_base = manifest['execution_overlay'].get('base_evidence')
        require(isinstance(frozen_base, dict) and Path(frozen_base.get('output', '')).resolve() == base_run and
                frozen_base.get('manifest_sha256') == sha(base_run / 'manifest.json') and frozen_base.get('summary_sha256') == sha(base_run / 'summary.json') and
                frozen_base.get('legacy_case_baseline_sha256') == sha(base_run / 'legacy-case-baseline.json'), 'base_evidence_frozen_hash_mismatch')
        base_manifest = json.loads((base_run / 'manifest.json').read_text()); base_summary = json.loads((base_run / 'summary.json').read_text())
        require(base_manifest['candidate'] == manifest['candidate'] == base_summary['candidate'] and not base_manifest.get('execution_overlay'), 'base_producer_identity_mismatch')
        require(sha(base_run / 'source-manifest.json') == manifest['candidate']['source_manifest_sha256'] and
                base_summary.get('budget_policy') == base_manifest.get('budget_policy') == budget_policy(), 'base_producer_source_policy_mismatch')
        require(base_summary.get('schema_version') == 1 and base_summary.get('task') == 'T19' and base_summary.get('ef_commit') == base_manifest.get('ef_commit') == EF_COMMIT and
                base_summary.get('ef_archive_sha256') == base_manifest.get('ef_archive_sha256') == ARCHIVE_SHA and
                base_summary.get('efcore_version') == base_manifest.get('efcore_version') == EFCORE and base_summary.get('sdk') == base_manifest.get('sdk') == SDK and
                base_summary.get('production_release_accepted') is False and base_summary.get('upstream_pending') == base_manifest.get('upstream_pending'),
                'base_summary_identity_or_scope_mismatch')
    require(summary['schema_version'] == 1 and summary['task'] == 'T19' and summary['candidate'] == manifest['candidate'] and
        summary['ef_commit'] == EF_COMMIT and summary['ef_archive_sha256'] == ARCHIVE_SHA and summary['efcore_version'] == EFCORE and summary['sdk'] == SDK and
        summary['production_release_accepted'] is False and summary['upstream_pending'] == manifest['upstream_pending'], 'summary_identity_or_scope_mismatch')
    require(is_frozen_budget_policy(summary.get('budget_policy')) and summary.get('budget_policy') == manifest.get('budget_policy'), 'summary_host_budget_policy_mismatch')
    require(sha(run / 'legacy-case-baseline.json') == manifest['legacy_case_baseline_sha256'], 'legacy_baseline_drift')
    if overlay:
        require(sha(base_run / 'legacy-case-baseline.json') == base_manifest.get('legacy_case_baseline_sha256') == sha(run / 'legacy-case-baseline.json'),
                'base_legacy_baseline_drift')
    baseline = json.loads((run / 'legacy-case-baseline.json').read_text())['lanes']
    expected_lanes = set(SHARED_LANES if args.profile == 'shared' else TLS_LANES)
    records = [item for item in summary['lanes'] if item['profile'] == args.profile]
    if overlay:
        inherited = [item for item in base_summary['lanes'] if item['profile'] == args.profile and item['lane'] != 'cli']
        fresh_cli = [item for item in summary['lanes'] if item['profile'] == args.profile and item['lane'] == 'cli']
        records = inherited + fresh_cli
    require({item['lane'] for item in records} == expected_lanes and len(records) == len(expected_lanes), 'required_profile_lane_missing')
    lineage_records = []
    for item in records:
        inherited = overlay is not None and item['lane'] != 'cli'
        lane_run, lane_manifest = (base_run, base_manifest) if inherited else (run, manifest)
        tool_root = Path(overlay['producer_tool_root'] if inherited else overlay['execution_tool_root']) if overlay else None
        key = item['profile'] + '-' + item['lane']; root, folder, entry = verify(lane_run, lane_manifest, key, tool_root=tool_root)
        verify_lane_execution_record(lane_manifest, entry, item)
        require(item.get('status') == 'accepted' and item.get('exit_code') == 0 and item['loaded_dll_identity'] == lane_manifest['candidate'] and
                item['binary_hashes_before'] == item['binary_hashes_after'] and bool(item['binary_hashes_before']) and bool(item['locks']), 'lane_tuple_or_immutable_binary_failed')
        require(item['testhost_proofs'] and all(check_identity(proof, manifest['candidate']) for proof in item['testhost_proofs']), 'actual_testhost_proof_failed')
        evidence, record_sha = reread_lane_record(root, item)
        proofs = reread_host_proofs(evidence, item, lane_manifest['candidate'], retain=not inherited)
        reread_audits(evidence, item)
        lineage_records.append({'profile': item['profile'], 'lane': item['lane'], 'origin': 'v6_original_tools' if inherited else 't19-cli-retention-v1' if overlay else 'producer_tools',
            'lane_record_sha256': record_sha, 'host_proof_files': proofs, 'tool_hashes': lane_manifest['tool_hashes'], 'record': item})
        if item['lane'] == 'audit':
            facts = item['before_audit']; require(facts and facts['accepted'] and facts.get('identity_verified') is True and facts.get('current_schema_verified') is True and
                facts.get('history_catalog_readable') is True and facts.get('original_lock_acquired') is True and facts.get('original_lock_released') is True, 'finite_permission_case_failed')
            continue
        count = item['strict_trx_counts']
        actual_count, actual_cases = strict_trx(evidence / 'tests.trx')
        require(actual_count == count and actual_cases == item['case_ids'], 'actual_TRX_summary_mismatch')
        require(count and count['total'] > 0 and count['total'] == count['executed'] == count['passed'] == len(item['case_ids']) and
            all(count[key] == 0 for key in COUNTERS if key not in ('total', 'executed', 'passed')), 'strict_TRX_failed')
        require(len(set(item['case_ids'])) == len(item['case_ids']), 'duplicate_case_identity')
        if item['lane'] in baseline:
            require(set(baseline[item['lane']]['case_ids']) <= set(item['case_ids']), 'legacy_case_identity_subset_failed')
        if item['lane'] != 'unit':
            before, after = item['before_audit'], item['after_audit']
            require(before and after and before['accepted'] and after['accepted'] and before['identity_verified'] and after['identity_verified'] and
                before['current_schema_verified'] and after['current_schema_verified'] and after.get('independent_final_inventory_matches') is True and
                before['inventory_sha256'] == after['inventory_sha256'], 'fresh_identity_or_exact_inventory_failed')
        if item['lane'] == 'cli':
            require(item['nested_cli_proofs'] and all(check_identity(proof, manifest['candidate']) for proof in item['nested_cli_proofs']) and
                any(proof.get('host_assembly') == 'CliRoundtrip' for proof in item['nested_cli_proofs']), 'nested_CLI_actual_loaded_identity_failed')
            # CLI owns and removes its temporary projects; each actual host retains its loaded asset before removal.
            for proof in item['nested_cli_proofs']:
                verify_retained_asset(proof, manifest['candidate'], retained_asset_root(root, item.get('retained_asset_root')))
        if item['lane'] in ('r3_shared', 'r3_tls'):
            finals = item['r3_contract_evidence']; ledgers = item['pending_owned_ledgers']
            expected = {'shared_crud_lob_concurrency', 'shared_transaction_savepoint', 'shared_datasource_ownership'} if args.profile == 'shared' else \
                {'tls_crud_lob_concurrency_supported_subset', 'tls_savepoint_explicitly_unsupported'}
            require({record['record']['case'] for record in finals} == expected and len(finals) == len(expected) and len(ledgers) == len(expected), 'R3_case_or_owned_ledger_cardinality_failed')
            for wrapper in finals:
                path = root / wrapper['path']; record = wrapper['record']
                require(sha(path) == wrapper['sha256'] and record['accepted'] is True and record['cleanup_verified'] is True and
                    record['final_database_state'] == 'exact_owned_table_absent' and record['profile'] == args.profile and record['pooling'] is True and
                    record['owner_kind'] == 'explicit_datasource' and record['automatic_savepoints_disabled'] is False and
                    record['pending_ownership_ledger_written_before_create'] is True and record['fresh_test_identity_checks'] >= 3 and
                    re.fullmatch(r'T19R3_[A-F0-9]{16}', record['owned_table']) is not None, 'R3_case_facts_or_cleanup_failed')
            for ledger in ledgers:
                record = ledger['record']; require(sha(root / ledger['path']) == ledger['sha256'] and record['accepted'] is False and record['status'] == 'pending_create' and
                    record['owner_schema'] == 'WDM_PROVIDER_TEST' and record['fresh_test_identity_verified'] is True and record['observed_fresh_object_count'] == 0,
                    'immutable_owned_ledger_failed')
        if item['lane'] == 'unit':
            require(sum('R3ExecutionStrategyTests.' in name for name in item['case_ids']) == 3, 'R3_actual_strategy_no_replay_units_missing')
    if overlay:
        aggregate = {'schema_version': 1, 'task': 'T19', 'status': 'accepted_profile_scope', 'profile': args.profile, 'candidate': manifest['candidate'],
            'producer_source_manifest_sha256': manifest['candidate']['source_manifest_sha256'], 'execution_revision': 't19-cli-retention-v1',
            'execution_overlay_verification': overlay, 'base_manifest_sha256': sha(base_run / 'manifest.json'), 'base_summary_sha256': sha(base_run / 'summary.json'),
            'new_cli_manifest_sha256': sha(run / 'manifest.json'), 'new_cli_summary_sha256': sha(run / 'summary.json'), 'lane_lineage': lineage_records,
            'upstream_pending': manifest['upstream_pending'], 'production_release_accepted': False}
        destination = run / ('aggregate-cli-retention-' + args.profile + '.json')
        with destination.open('x') as stream:
            destination.chmod(0o600); json.dump(aggregate, stream, sort_keys=True, indent=2); stream.write('\n')
    emit({'status': 'accepted_profile_scope', 'profile': args.profile, 'candidate': manifest['candidate'], 'ef_commit': EF_COMMIT,
          'ef_archive_sha256': ARCHIVE_SHA, 'efcore_version': EFCORE, 'sdk': SDK, 'production_release_accepted': False,
          'upstream_pending': manifest['upstream_pending'], 'shared_long_resource': 'not_verified_not_required_by_T18_TLS_design'})


if __name__ == '__main__':
    try: main()
    except Exception as error:
        emit({'status': 'pending', 'reason': str(error) if isinstance(error, ValueError) and re.fullmatch(r'[A-Za-z0-9_]+', str(error)) else 'rich_evidence_validation_failed'})
        sys.exit(1)
