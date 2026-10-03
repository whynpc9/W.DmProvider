#!/usr/bin/env python3
"""One fresh T19 lane at a time. TEST wrappers alone authorize DB environment injection."""
import argparse
from datetime import datetime, timezone
import html
import json
import os
from pathlib import Path
import re
import signal
import subprocess
import sys
import time
import xml.etree.ElementTree as ET
from common import *


def verify(run, manifest, key, tool_root=None):
    entry = manifest['lanes'][key]; root = Path(entry['root']); folder = root / 'W'
    verify_lane_budget(manifest, entry, key)
    if not root.is_relative_to(run / 'lanes') or manifest['ef_commit'] != EF_COMMIT or manifest['ef_archive_sha256'] != ARCHIVE_SHA or manifest['sdk'] != SDK:
        raise ValueError('lane_pin_or_path_drift')
    if sha(entry['candidate_path']) != manifest['candidate']['package_sha256'] or sha(run / 'source-manifest.json') != manifest['candidate']['source_manifest_sha256']:
        raise ValueError('candidate_source_drift')
    actual = sources(folder); expected = entry['sources']
    changed = [name for name in actual.keys() | expected.keys() if actual.get(name) != expected.get(name)]
    if any(not name.endswith('packages.lock.json') for name in changed): raise ValueError('frozen_lane_source_drift')
    for name, digest in manifest['tool_hashes'].items():
        if sha((tool_root or HERE) / name) != digest: raise ValueError('frozen_T19_tool_drift')
    build = root / 'build.json'
    if build.exists():
        record = json.loads(build.read_text())
        if {name: digest for name, digest in actual.items() if name.endswith('packages.lock.json')} != record['locks']: raise ValueError('locked_dependency_drift')
        for name, digest in record['binary_hashes'].items():
            if sha(folder / name) != digest: raise ValueError('compiled_lane_binary_drift')
    return root, folder, entry


def environment(root, manifest, profile, evidence):
    env = os.environ.copy()
    for key in list(env):
        if key.startswith('DAMENG') or key.startswith('ConnectionStrings__T12Cli'): env.pop(key)
    dotnet = str(Path(os.environ.get('DAMENG_T19_DOTNET_HOST', '/Users/wanghongyi/.dotnet/dotnet')).resolve())
    candidate = manifest['candidate']
    env.update(DOTNET_CLI_HOME=str(root / '.cli'), DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1', DOTNET_CLI_TELEMETRY_OPTOUT='1',
               DOTNET_CLI_UI_LANGUAGE='en-US', NUGET_PACKAGES=str(root / '.packages'), NUGET_HTTP_CACHE_PATH=str(root / '.http-cache'),
               DOTNET_HOST_PATH=dotnet, DOTNET_ROOT=str(Path(dotnet).parent),
               DAMENG_T19_PROFILE=profile, DAMENG_T19_REPO_ROOT=str(ROOT),
               DAMENG_T19_DRIVER_VERSION=candidate['version'], DAMENG_T19_DRIVER_DLL_SHA256=candidate['driver_dll_sha256'],
               DAMENG_T19_DRIVER_DLL_MVID=candidate['driver_mvid'], DAMENG_T19_DRIVER_LOAD_RECORD=str(evidence / 'testhost-driver.json'),
               DAMENG_T19_DRIVER_LOAD_ROOT=str(evidence / 'hosts'), DAMENG_T19_R3_EVIDENCE_DIR=str(evidence / 'contracts'),
               DAMENG_T19_DRIVER_ASSET_ROOT=str(evidence / 'loaded-drivers'),
               DAMENG_T12_CANDIDATE_VERSION=candidate['version'], DAMENG_T12_DRIVER_DLL_SHA256=candidate['driver_dll_sha256'],
               DAMENG_T12_DOTNET_HOST=dotnet, DAMENG_T12_NUGET_CONFIG=str(root / 'W/NuGet.Config'),
               DAMENG_T12_CLI_ROOT=str(root / 'W/artifacts/t19-cli'))
    create_asset_root(Path(env['DAMENG_T19_DRIVER_ASSET_ROOT']))
    for key in ('DOTNET_CLI_HOME', 'NUGET_PACKAGES', 'NUGET_HTTP_CACHE_PATH', 'DAMENG_T19_DRIVER_LOAD_ROOT', 'DAMENG_T19_R3_EVIDENCE_DIR', 'DAMENG_T12_CLI_ROOT'):
        Path(env[key]).mkdir(parents=True, mode=0o700, exist_ok=True)
    return env, dotnet


def secrets():
    # Runtime only, after the TEST wrapper. Values stay in memory and are never written as evidence.
    raw = os.environ.get('DAMENG_TEST_CONNECTION_STRING', '') or os.environ.get('DAMENG_TLS_TEST_CONNECTION_STRING', '')
    values = [raw] if raw else []
    for field in re.findall(r'''(?:[^;"']|"(?:[^"]|"")*"|'(?:[^']|'')*')+''', raw):
        if '=' not in field: continue
        key, value = field.split('=', 1)
        if any(word in key.lower() for word in ('password', 'pwd', 'user', 'uid', 'server', 'host', 'data source')):
            value = value.strip()
            if len(value) >= 2 and value[0] == value[-1] and value[0] in "'\"": value = value[1:-1].replace(value[0] * 2, value[0])
            if value and value != 'WDM_PROVIDER_TEST': values.append(value)
    variants = set(values)
    for value in values: variants.update((html.escape(value, quote=True), json.dumps(value, ensure_ascii=False)[1:-1], json.dumps(value, ensure_ascii=True)[1:-1]))
    return sorted(variants, key=len, reverse=True)


def redact(text, values):
    for value in values: text = re.sub(re.escape(value), '[redacted]', text, flags=re.IGNORECASE)
    return text


def capture(argv, folder, env, log, timeout, sensitive=(), *, profile, lane, execution_phase, policy_id):
    # Reject a changed runtime timeout before starting any process or reading TEST settings.
    verify_execution_budget(profile, lane, execution_phase, policy_id, timeout)
    begin = datetime.now(timezone.utc).isoformat()
    started = time.monotonic(); timed_out = False
    process = subprocess.Popen(argv, cwd=folder, env=env, stdout=subprocess.PIPE, stderr=subprocess.PIPE, start_new_session=True)
    try: stdout, stderr = process.communicate(timeout=timeout); code = process.returncode
    except subprocess.TimeoutExpired:
        timed_out = True
        os.killpg(process.pid, signal.SIGKILL); stdout, stderr = process.communicate(); code = 124
    elapsed = time.monotonic() - started
    text = (stdout + stderr).decode('utf-8', errors='replace'); safe = redact(text, sensitive)
    log.parent.mkdir(parents=True, exist_ok=True); log.write_text(safe)
    return {'argv': argv, 'exit_code': code, 'started_utc': begin, 'log_sha256': sha(log), 'redaction_applied': safe != text,
            'policy_id': policy_id, 'profile': profile, 'lane': lane, 'execution_phase': execution_phase,
            'host_timeout_seconds': timeout, 'timed_out': timed_out, 'host_exit_code': process.returncode, 'actual_elapsed_seconds': elapsed}


def build(run, manifest, key):
    verify_overlay_action(run, manifest, key)
    root, folder, entry = verify(run, manifest, key)
    if (root / 'build.json').exists(): raise ValueError('build_record_exists_use_fresh_lane')
    evidence = root / '.private/build'; evidence.mkdir(parents=True, mode=0o700)
    env, dotnet = environment(root, manifest, entry['profile'], evidence)
    commands = []
    sdk = subprocess.run([dotnet, '--version'], cwd=folder, env=env, capture_output=True, text=True)
    if sdk.returncode or sdk.stdout.strip() != SDK: raise ValueError('downstream_SDK_10_0_401_required')
    audit = PROJECTS['audit']; projects = [audit] if entry['lane'] == 'audit' else [audit, entry['project']]
    for index, project in enumerate(dict.fromkeys(projects)):
        for stage, argv in [('evaluate', [dotnet, 'restore', project, '--force-evaluate', '--disable-parallel', '--configfile', str(folder / 'NuGet.Config'), *FLAGS]),
                            ('locked', [dotnet, 'restore', project, '--locked-mode', '--disable-parallel', '--configfile', str(folder / 'NuGet.Config'), *FLAGS]),
                            ('build', [dotnet, 'build', project, '--no-restore', *FLAGS])]:
            row = capture(argv, folder, env, evidence / f'{index}-{stage}.log', 600, profile=entry['profile'], lane=entry['lane'], execution_phase='build', policy_id=entry['policy_id']); commands.append(row)
            if row['exit_code']:
                write(root / 'build.json', {'accepted': False, 'commands': commands, 'locks': {}, 'binary_hashes': {}}); return row['exit_code']
    binaries = {}; libraries = []
    for project in projects:
        project_path = folder / project; output = project_path.parent / 'bin/Debug/net10.0'; driver = output / 'W.DmProvider.dll'
        if sha(driver) != manifest['candidate']['driver_dll_sha256'] or mvid(driver.read_bytes()) != manifest['candidate']['driver_mvid']: raise ValueError('built_driver_tuple_mismatch')
        for path in output.iterdir():
            if path.is_file(): binaries[str(path.relative_to(folder))] = sha(path)
        assets = json.loads((project_path.parent / 'obj/project.assets.json').read_text())
        for name, data in assets['libraries'].items():
            if name.startswith('DM.DmProvider/'): raise ValueError('official_dependency_rejected')
            if name.startswith('W.DmProvider/') and (name != 'W.DmProvider/' + manifest['candidate']['version'] or data['type'] != 'package'): raise ValueError('driver_package_reference_mismatch')
            if name.startswith('Microsoft.EntityFrameworkCore.') and name.rsplit('/', 1)[1] != EFCORE: raise ValueError('EFCore_exact_version_mismatch')
            libraries.append({'project': project, 'identity': name, 'type': data['type'], 'sha512': data.get('sha512')})
    locks = {name: digest for name, digest in sources(folder).items() if name.endswith('packages.lock.json')}
    write(root / 'build.json', {'accepted': True, 'sdk': SDK, 'commands': commands, 'locks': locks, 'binary_hashes': binaries, 'libraries': libraries})
    verify(run, manifest, key); return 0


def strict_trx(path):
    document = ET.parse(path); root = document.getroot(); summary = root.find('t:ResultSummary', NS); counters = root.find('t:ResultSummary/t:Counters', NS)
    if summary is None or counters is None or any(name not in counters.attrib for name in COUNTERS): raise ValueError('TRX_incomplete')
    numbers = {name: int(counters.attrib[name]) for name in COUNTERS}; results = root.findall('t:Results/t:UnitTestResult', NS)
    if summary.get('outcome') not in ('Completed', 'Passed') or numbers['total'] <= 0 or numbers['total'] != numbers['executed'] or numbers['total'] != numbers['passed'] or \
        any(value for name, value in numbers.items() if name not in ('total', 'executed', 'passed')) or len(results) != numbers['total'] or any(item.get('outcome') != 'Passed' for item in results):
        raise ValueError('TRX_failure_skip_abort')
    cases = sorted(item.attrib['testName'] for item in results)
    if len(set(cases)) != len(cases): raise ValueError('duplicate_case_identity')
    return numbers, cases


def check_identity(proof, candidate):
    return proof.get('driver_id') == 'W.DmProvider' and proof.get('driver_version') == candidate['version'] and \
        proof.get('driver_dll_sha256') == candidate['driver_dll_sha256'] and proof.get('driver_dll_mvid') == candidate['driver_mvid'] and \
        proof.get('package_library_type') == 'package' and proof.get('loaded_in_host_directory') is True


def create_asset_root(path):
    if path.exists() or path.is_symlink(): raise ValueError('retained_driver_asset_root_exists')
    if path.parent.is_symlink() or not path.parent.is_dir(): raise ValueError('retained_driver_asset_root_invalid')
    path.mkdir(mode=0o700)
    if path.stat().st_mode & 0o077: raise ValueError('retained_driver_asset_root_private_required')


def retained_asset_root(lane_root, relative):
    if not isinstance(relative, str) or not relative or Path(relative).is_absolute() or '..' in Path(relative).parts or Path(relative).name != 'loaded-drivers':
        raise ValueError('retained_driver_asset_root_invalid')
    path = lane_root / relative
    current = lane_root
    if current.is_symlink(): raise ValueError('retained_driver_asset_root_invalid')
    for part in Path(relative).parts:
        current = current / part
        if current.is_symlink(): raise ValueError('retained_driver_asset_root_invalid')
    if not path.is_dir() or not path.resolve().is_relative_to(lane_root.resolve()) or path.stat().st_mode & 0o077:
        raise ValueError('retained_driver_asset_root_invalid')
    return path


def verify_retained_asset(proof, candidate, asset_root):
    name = proof.get('retained_driver_asset')
    if not isinstance(name, str) or re.fullmatch(r'loaded-[0-9a-f]{32}\.dll', name) is None:
        raise ValueError('retained_driver_asset_path_invalid')
    if asset_root.is_symlink() or not asset_root.is_dir() or asset_root.stat().st_mode & 0o077:
        raise ValueError('retained_driver_asset_root_invalid')
    path = asset_root / name
    if path.is_symlink(): raise ValueError('retained_driver_asset_path_invalid')
    if not path.is_file(): raise ValueError('retained_driver_asset_missing')
    if path.stat().st_mode & 0o077: raise ValueError('retained_driver_asset_private_required')
    if proof.get('schema_version') != 2 or proof.get('retained_driver_source') != 'loaded_driver_location':
        raise ValueError('retained_driver_asset_source_mismatch')
    if proof.get('retained_driver_sha256') != proof.get('driver_dll_sha256') or proof.get('retained_driver_sha256') != candidate['driver_dll_sha256'] or sha(path) != candidate['driver_dll_sha256']:
        raise ValueError('retained_driver_asset_hash_mismatch')
    try: actual_mvid = mvid(path.read_bytes())
    except Exception: raise ValueError('retained_driver_asset_mvid_mismatch') from None
    if proof.get('retained_driver_mvid') != proof.get('driver_dll_mvid') or proof.get('retained_driver_mvid') != candidate['driver_mvid'] or actual_mvid != candidate['driver_mvid']:
        raise ValueError('retained_driver_asset_mvid_mismatch')
    return {'retained_driver_asset': name, 'retained_driver_sha256': sha(path), 'retained_driver_mvid': actual_mvid,
            'retained_driver_source': 'loaded_driver_location'}


def verify_execution_overlay(run, manifest, manifest_path=None):
    descriptor = manifest.get('execution_overlay')
    if not isinstance(descriptor, dict): raise ValueError('execution_overlay_descriptor_missing')
    frozen = run / 'execution-overlay.json'
    if sha(frozen) != descriptor.get('manifest_sha256') or manifest_path is not None and sha(manifest_path) != sha(frozen):
        raise ValueError('execution_overlay_manifest_mismatch')
    from execution_overlay import validate_manifest
    verified = validate_manifest(manifest_path=frozen, producer_manifest_path=run / 'source-manifest.json',
        producer_source_dir=Path(descriptor['producer_source_dir']), execution_source_dir=Path(descriptor['execution_source_dir']),
        expected_candidate_bindings=manifest['candidate'])
    return verified


def verify_overlay_action(run, manifest, key):
    if not manifest.get('execution_overlay'): return
    if key != 'shared-cli': raise ValueError('execution_overlay_only_shared_cli')
    verified = verify_execution_overlay(run, manifest)
    for name, digest in manifest['tool_hashes'].items():
        if sha(Path(verified['execution_tool_root']) / name) != digest or sha(HERE / name) != digest:
            raise ValueError('execution_overlay_running_tool_mismatch')


def worker(run, manifest, key, evidence):
    verify_overlay_action(run, manifest, key)
    root, folder, entry = verify(run, manifest, key); env, dotnet = environment(root, manifest, entry['profile'], evidence); sensitive = secrets()
    raw = os.environ.get('DAMENG_TLS_TEST_CONNECTION_STRING' if entry['profile'] == 'tls' else 'DAMENG_TEST_CONNECTION_STRING', '')
    if entry['lane'] != 'unit':
        if not raw: raise ValueError('integration_pending_TEST_environment_required')
        env['DAMENG_TEST_CONNECTION_STRING'] = raw
    candidate = manifest['candidate']; state = json.loads((root / 'build.json').read_text())
    if not state['accepted']: raise ValueError('unaccepted_lane_build')
    result = {'lane': entry['lane'], 'profile': entry['profile'], 'status': 'pending', 'exit_code': 1, 'strict_trx_counts': None, 'case_ids': [],
              'before_audit': None, 'after_audit': None, 'loaded_dll_identity': candidate, 'testhost_proofs': [], 'nested_cli_proofs': [],
              'binary_hashes_before': state['binary_hashes'], 'binary_hashes_after': {}, 'locks': state['locks'], 'commands': []}
    result.update(policy_id=entry['policy_id'], host_timeout_seconds=entry['host_timeout_seconds'], host_exit_code=None, timed_out=None, actual_elapsed_seconds=None)
    result['retained_asset_root'] = str((evidence / 'loaded-drivers').relative_to(root))
    result['r3_contract_evidence'] = []; result['pending_owned_ledgers'] = []
    write(evidence / 'lane.json', result)
    def audit(mode):
        env['DAMENG_T19_DRIVER_LOAD_RECORD'] = str(evidence / f'audit-{mode}-driver.json')
        output = evidence / f'audit-{mode}.json'
        row = capture([dotnet, str(folder / 'T19Audit/bin/Debug/net10.0/T19Audit.dll'), mode, str(output), str(evidence / 'inventory.json'), candidate['driver_dll_sha256']],
                      folder, env, evidence / f'audit-{mode}.log', 90, sensitive,
                      profile=entry['profile'], lane=entry['lane'], execution_phase='audit', policy_id=entry['policy_id'])
        result['commands'].append(row)
        if entry['lane'] == 'audit':
            result.update({name: row[name] for name in ('host_exit_code', 'timed_out', 'actual_elapsed_seconds')})
            result['exit_code'] = row['exit_code']
        if row['exit_code'] or not output.exists(): return None
        return json.loads(output.read_text())
    try:
        if entry['lane'] == 'audit':
            result['before_audit'] = audit('permissions'); result['after_audit'] = result['before_audit']
            if not result['before_audit'] or not result['before_audit']['accepted']: raise ValueError('finite_permission_audit_failed')
        else:
            if entry['lane'] != 'unit':
                result['before_audit'] = audit('before')
                if not result['before_audit'] or not result['before_audit']['accepted']: raise ValueError('fresh_TEST_identity_inventory_before_failed')
            env['DAMENG_T19_DRIVER_LOAD_RECORD'] = str(evidence / 'testhost-driver.json')
            argv = [dotnet, 'test', entry['project'], '--no-build', '--no-restore', *FLAGS, '--logger', 'trx;LogFileName=tests.trx', '--results-directory', str(evidence)]
            if entry['filter']: argv += ['--filter', entry['filter']]
            row = capture(argv, folder, env, evidence / 'tests.log', entry['host_timeout_seconds'], sensitive,
                          profile=entry['profile'], lane=entry['lane'], execution_phase='test', policy_id=entry['policy_id']); result['commands'].append(row)
            result['exit_code'] = row['exit_code']
            result.update({name: row[name] for name in ('host_exit_code', 'timed_out', 'actual_elapsed_seconds')})
            if entry['lane'] != 'unit': result['after_audit'] = audit('after')
            if row['exit_code']: raise ValueError('test_host_nonzero')
            result['strict_trx_counts'], result['case_ids'] = strict_trx(evidence / 'tests.trx')
            baseline = json.loads((run / 'legacy-case-baseline.json').read_text())['lanes'].get(entry['lane'])
            if baseline and not set(baseline['case_ids']) <= set(result['case_ids']): raise ValueError('legacy_case_identity_missing')
            if entry['lane'] != 'unit' and (not result['after_audit'] or not result['after_audit']['accepted'] or not result['after_audit'].get('independent_final_inventory_matches')):
                raise ValueError('fresh_inventory_cleanup_not_proven')
        for proof in sorted(evidence.glob('*-driver.json')):
            record = json.loads(proof.read_text())
            if not check_identity(record, candidate): raise ValueError('testhost_loaded_tuple_mismatch')
            verify_retained_asset(record, candidate, evidence / 'loaded-drivers')
            result['testhost_proofs'].append(record)
        for proof in sorted((evidence / 'hosts').glob('*.json')):
            record = json.loads(proof.read_text())
            if not check_identity(record, candidate): raise ValueError('nested_CLI_loaded_tuple_mismatch')
            verify_retained_asset(record, candidate, evidence / 'loaded-drivers')
            result['nested_cli_proofs'].append(record)
        if not result['testhost_proofs'] or entry['lane'] == 'cli' and not result['nested_cli_proofs']: raise ValueError('actual_host_proof_missing')
        if entry['lane'] in ('r3_shared', 'r3_tls'):
            expected = {'shared_crud_lob_concurrency', 'shared_transaction_savepoint', 'shared_datasource_ownership'} if entry['profile'] == 'shared' else \
                {'tls_crud_lob_concurrency_supported_subset', 'tls_savepoint_explicitly_unsupported'}
            for path in sorted((evidence / 'contracts').glob('*.json')):
                record = json.loads(path.read_text())
                if path.name.endswith('.owned.json'):
                    result['pending_owned_ledgers'].append({'path': str(path.relative_to(root)), 'sha256': sha(path), 'record': record})
                else:
                    if not record.get('accepted') or not record.get('cleanup_verified') or record.get('final_database_state') != 'exact_owned_table_absent': raise ValueError('R3_contract_or_cleanup_failed')
                    result['r3_contract_evidence'].append({'path': str(path.relative_to(root)), 'sha256': sha(path), 'record': record})
            if {item['record']['case'] for item in result['r3_contract_evidence']} != expected or len(result['r3_contract_evidence']) != len(expected): raise ValueError('R3_required_case_evidence_missing')
        for path in evidence.rglob('*'):
            if path.is_file():
                # Retained PE assets are independently verified binary evidence, never decoded/redacted as logs.
                if path.parent == evidence / 'loaded-drivers': continue
                text = path.read_text(errors='replace'); safe = redact(text, sensitive)
                if safe != text: path.write_text(safe); raise ValueError('sensitive_artifact_detected')
        result['status'] = 'accepted'; result['exit_code'] = 0
        verify_lane_execution_record(manifest, entry, result)
    except Exception as error:
        result['status'] = 'pending'; result['reason'] = str(error) if isinstance(error, ValueError) and re.fullmatch(r'[A-Za-z0-9_]+', str(error)) else 'lane_failed'
        result['exit_code'] = result['exit_code'] or 1
    finally:
        # Pending ownership files are immutable recovery facts, never a passed case or silently deleted after failure.
        result['pending_owned_ledgers'] = [{'path': str(path.relative_to(root)), 'sha256': sha(path), 'record': json.loads(path.read_text())}
            for path in sorted((evidence / 'contracts').glob('*.owned.json'))]
        result['binary_hashes_after'] = {name: sha(folder / name) for name in state['binary_hashes']}
        if result['binary_hashes_after'] != result['binary_hashes_before']: result['status'] = 'pending'; result['exit_code'] = 1; result['reason'] = 'compiled_binary_drift'
        write(evidence / 'lane.json', result)
    return result['exit_code']


def summarize(run, manifest):
    records = []
    for entry in manifest['lanes'].values():
        verify_lane_budget(manifest, entry)
        root = Path(entry['root']); found = sorted(root.glob('results-*/lane.json'))
        if found: records.append(json.loads(found[-1].read_text()))
        else: records.append({'lane': entry['lane'], 'profile': entry['profile'], 'policy_id': entry['policy_id'], 'host_timeout_seconds': entry['host_timeout_seconds'],
                              'host_exit_code': None, 'timed_out': None, 'actual_elapsed_seconds': None, 'status': 'pending', 'exit_code': None})
    complete = all(item['status'] == 'accepted' for item in records)
    summary = {'schema_version': 1, 'task': 'T19', 'candidate': manifest['candidate'], 'ef_commit': EF_COMMIT, 'ef_archive_sha256': ARCHIVE_SHA,
               'efcore_version': EFCORE, 'sdk': SDK, 'lanes': records, 'status': 'accepted_declared_scope' if complete else 'pending',
               'upstream_pending': manifest['upstream_pending'], 'production_release_accepted': False,
               'shared_long_resource': 'not_verified_not_required_by_T18_TLS_design', 'budget_policy': manifest['budget_policy']}
    write(run / 'summary.json', summary)


def main():
    parser = argparse.ArgumentParser(); parser.add_argument('action', choices=('build', 'run', 'worker', 'summary'))
    parser.add_argument('--output', required=True); parser.add_argument('--profile', choices=('shared', 'tls')); parser.add_argument('--lane'); parser.add_argument('--evidence')
    args = parser.parse_args(); run = bounded(args.output); manifest = json.loads((run / 'manifest.json').read_text())
    if args.action == 'summary': summarize(run, manifest); return 0
    key = args.profile + '-' + args.lane
    if key not in manifest['lanes']: raise ValueError('undeclared_lane')
    if args.action == 'build': return build(run, manifest, key)
    if args.action == 'worker':
        evidence = bounded(args.evidence)
        if not evidence.is_relative_to(Path(manifest['lanes'][key]['root'])): raise ValueError('evidence_outside_lane')
        return worker(run, manifest, key, evidence)
    root, _, entry = verify(run, manifest, key); evidence = root / ('results-' + datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%S%fZ'))
    evidence.mkdir(mode=0o700)
    if entry['lane'] == 'unit': code = worker(run, manifest, key, evidence)
    else:
        wrapper = ROOT / ('scripts/with-dameng-tls-test.sh' if args.profile == 'tls' else 'scripts/with-dameng-test.sh')
        env = os.environ.copy()
        for name in list(env):
            if name.startswith('DAMENG') and name != 'DAMENG_T19_DOTNET_HOST': env.pop(name)
        code = subprocess.run([str(wrapper), sys.executable, '-B', str(HERE / 'run.py'), 'worker', '--output', str(run), '--profile', args.profile,
                               '--lane', args.lane, '--evidence', str(evidence)], env=env).returncode
    verify(run, manifest, key); summarize(run, manifest); emit({'status': 'accepted_lane' if code == 0 else 'pending', 'profile': args.profile, 'lane': args.lane, 'exit_code': code})
    return code


if __name__ == '__main__':
    try: sys.exit(main())
    except Exception as error:
        emit({'status': 'rejected', 'reason': str(error) if isinstance(error, ValueError) and re.fullmatch(r'[A-Za-z0-9_]+', str(error)) else 'runner_failed'})
        sys.exit(1)
