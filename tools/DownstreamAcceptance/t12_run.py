#!/usr/bin/env python3
"""Strict per-lane T12 build/test. DB workers execute only under the TEST wrapper."""
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
import xml.etree.ElementTree as ET

from t12_prepare import ROOT, HERE, bounded, sha, sources

DOTNET = Path(os.environ.get('DAMENG_T12_DOTNET_HOST', '/Users/wanghongyi/.dotnet/dotnet')).resolve()
FLAGS = ['-m:1', '/nodeReuse:false', '/p:UseSharedCompilation=false', '--disable-build-servers']
PROJECTS = {
    'audit': 'T12Audit/T12Audit.csproj',
    'unit': 'test/W.EntityFrameworkCore.Dameng.Tests/W.EntityFrameworkCore.Dameng.Tests.csproj',
    'functional': 'test/W.EntityFrameworkCore.Dameng.FunctionalTests/W.EntityFrameworkCore.Dameng.FunctionalTests.csproj',
    'specification': 'test/W.EntityFrameworkCore.Dameng.Specification.Tests/W.EntityFrameworkCore.Dameng.Specification.Tests.csproj',
}
FILTERS = {
    'functional': 'FullyQualifiedName!~DamengMigrationScriptFunctionalTests&Category!~CapabilityProbe&FullyQualifiedName!~DamengDotNetEfCliFunctionalTests&FullyQualifiedName!~DamengCurrentSchemaScriptFunctionalTests&Category!~OfficialCharacterization',
    'reverse': 'FullyQualifiedName~DamengReverseEngineeringFunctionalTests',
    'migrations': 'FullyQualifiedName~DamengMigrationsFunctionalTests',
    'cli': 'FullyQualifiedName~DamengDotNetEfCliFunctionalTests',
    'scripts': 'FullyQualifiedName~DamengCurrentSchemaScriptFunctionalTests',
    'queries': 'FullyQualifiedName!~DamengMigrationScriptFunctionalTests&Category!~CapabilityProbe&FullyQualifiedName!~DamengDotNetEfCliFunctionalTests&FullyQualifiedName!~DamengCurrentSchemaScriptFunctionalTests&FullyQualifiedName!~DamengMigrationsFunctionalTests&FullyQualifiedName!~DamengReverseEngineeringFunctionalTests&Category!~OfficialCharacterization',
}
REQUIRED_COUNTERS = ('total', 'executed', 'passed', 'failed', 'error', 'timeout', 'aborted', 'inconclusive',
                     'passedButRunAborted', 'notRunnable', 'notExecuted', 'disconnected', 'warning', 'completed', 'inProgress', 'pending')


def stamp():
    return datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%S%fZ')


def config(run, lane):
    candidate = json.loads((run / 'manifest.json').read_text())['candidate']
    env = os.environ.copy()
    env.pop('DAMENG_TEST_CONNECTION_STRING', None)
    env.pop('ConnectionStrings__T12Cli', None)
    env.update({'DOTNET_CLI_HOME': str(run / '.cli'), 'DOTNET_SKIP_FIRST_TIME_EXPERIENCE': '1',
                'DOTNET_CLI_TELEMETRY_OPTOUT': '1', 'DOTNET_CLI_UI_LANGUAGE': 'en-US',
                'NUGET_PACKAGES': str(run / '.packages'), 'NUGET_HTTP_CACHE_PATH': str(run / '.http-cache'),
                'DOTNET_HOST_PATH': str(DOTNET), 'DOTNET_ROOT': str(DOTNET.parent),
                'DAMENG_T12_DOTNET_HOST': str(DOTNET),
                'DAMENG_T12_CANDIDATE_VERSION': candidate['version'],
                'DAMENG_T12_DRIVER_DLL_SHA256': candidate['driver_dll_sha256'],
                'DAMENG_T12_NUGET_CONFIG': str(run / 'W/NuGet.Config'),
                'DAMENG_T12_CLI_ROOT': str(run / 'W/artifacts/t12-cli')})
    Path(env['DOTNET_CLI_HOME']).mkdir(parents=True, exist_ok=True)
    cli_root = Path(env['DAMENG_T12_CLI_ROOT']).resolve()
    if not cli_root.is_relative_to((run / 'W').resolve()):
        raise ValueError('cli_root_outside_isolated_archive')
    cli_root.mkdir(parents=True, exist_ok=True)
    return env


def verify(run, manifest):
    if manifest['ef_commit'] != '113014cc74dd1f751ef97226a78d2ec855b32c8c':
        raise ValueError('ef_pin_mismatch')
    if sha(Path(manifest['candidate']['package_path']).read_bytes()) != manifest['candidate']['package_sha256']:
        raise ValueError('candidate_package_drift')
    actual, expected = sources(run / 'W'), manifest['source_hashes']
    drift = [k for k in actual.keys() | expected.keys() if actual.get(k) != expected.get(k)]
    if any(not k.endswith('packages.lock.json') for k in drift):
        raise ValueError('frozen_source_drift')
    for name, expected_hash in manifest['tool_hashes'].items():
        if sha((HERE / name).read_bytes()) != expected_hash:
            raise ValueError('frozen_runner_drift')
    locks = {k: v for k, v in expected.items() if k.endswith('packages.lock.json')}
    for record in sorted(run.glob('build-*.json'), key=lambda p: p.stat().st_mtime_ns):
        locks.update(json.loads(record.read_text()).get('locks', {}))
    actual_locks = {k: v for k, v in actual.items() if k.endswith('packages.lock.json')}
    if actual_locks != locks:
        raise ValueError('dependency_lock_drift')


def environment_secrets():
    raw = os.environ.get('DAMENG_TEST_CONNECTION_STRING', '')
    values = [raw] if raw else []
    # Parse quoted connection fields without assuming passwords cannot contain semicolons.
    fields = re.findall(r'(?:[^;"\']|"(?:[^"]|"")*"|\'(?:[^\']|\'\')*\')+', raw)
    for field in fields:
        if '=' not in field:
            continue
        key, value = field.split('=', 1)
        if any(word in key.lower() for word in ('password', 'pwd', 'user', 'uid', 'server', 'host', 'data source')):
            value = value.strip()
            if len(value) >= 2 and value[0] == value[-1] and value[0] in '\'"':
                value = value[1:-1].replace(value[0] * 2, value[0])
            if value and value != 'WDM_PROVIDER_TEST':
                values.append(value)
    variants = set(values)
    for value in values:
        variants.add(html.escape(value, quote=True))
        variants.add(json.dumps(value, ensure_ascii=False)[1:-1])
        variants.add(json.dumps(value, ensure_ascii=True)[1:-1])
    return sorted(variants, key=len, reverse=True)


def redact(text, values):
    for value in values:
        text = re.sub(re.escape(value), '[redacted]', text, flags=re.IGNORECASE)
    return text


def capture(command, cwd, env, log, timeout, secrets=()):
    process = subprocess.Popen(command, cwd=cwd, env=env, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                               start_new_session=os.name == 'posix')
    try:
        stdout, stderr = process.communicate(timeout=timeout)
        code = process.returncode
        raw = stdout + stderr
    except subprocess.TimeoutExpired:
        if os.name == 'posix':
            os.killpg(process.pid, signal.SIGKILL)
        else:
            subprocess.run(['taskkill', '/PID', str(process.pid), '/T', '/F'], capture_output=True)
        stdout, stderr = process.communicate(timeout=10)
        code = 124
        raw = stdout + stderr + b'\ncontrolled_deadline_exceeded\n'
    # Raw process output stays in memory and is redacted before any log file write.
    safe = redact(raw.decode('utf-8', errors='replace'), secrets)
    log.write_text(safe)
    return code


def counts(trx):
    root = ET.fromstring(trx.read_text())
    counter = next((n for n in root.iter() if n.tag.split('}')[-1] == 'Counters'), None)
    summary = next((n for n in root.iter() if n.tag.split('}')[-1] == 'ResultSummary'), None)
    if counter is None or summary is None:
        raise ValueError('trx_missing_counters')
    if any(name not in counter.attrib for name in REQUIRED_COUNTERS):
        raise ValueError('trx_incomplete_counters')
    values = {name: int(counter.attrib[name]) for name in REQUIRED_COUNTERS}
    if any(value < 0 for value in values.values()):
        raise ValueError('trx_negative_counters')
    values['outcome'] = summary.attrib.get('outcome')
    return values


def accepted_counts(values):
    return values is not None and values['outcome'] == 'Completed' and values['total'] > 0 \
        and values['total'] == values['executed'] == values['passed'] \
        and all(values[k] == 0 for k in REQUIRED_COUNTERS if k not in ('total', 'executed', 'passed'))


def verify_binaries(run, lanes):
    for lane in lanes:
        state = json.loads((run / ('build-' + lane + '.json')).read_text())
        for path, expected in state.get('binaries', {}).items():
            if sha((run / 'W' / path).read_bytes()) != expected:
                raise ValueError('post_run_binary_drift')


def build(run, manifest, lane):
    actual_lane = lane if lane in PROJECTS else 'functional'
    if lane not in manifest['lanes_ready']:
        raise ValueError('lane_not_ready')
    path = run / ('build-' + actual_lane + '.json')
    if path.exists():
        raise ValueError('build_record_exists_use_fresh_snapshot')
    folder = run / 'W'
    env = config(run, lane)
    env['DAMENG_T12_CANDIDATE_VERSION'] = manifest['candidate']['version']
    result = {'lane': actual_lane, 'accepted': False, 'candidate': manifest['candidate'], 'commands': [], 'dotnet_host': str(DOTNET)}
    sdk = subprocess.run([str(DOTNET), '--version'], cwd=folder, env=env, capture_output=True, text=True)
    if sdk.returncode or sdk.stdout.strip() != '10.0.401':
        raise ValueError('sdk_pin_unsatisfied')
    result['sdk'] = sdk.stdout.strip()
    project = PROJECTS[actual_lane]
    commands = [
        [str(DOTNET), 'restore', project, '--force-evaluate', '--disable-parallel', '--configfile', str(folder / 'NuGet.Config'), *FLAGS],
        [str(DOTNET), 'restore', project, '--locked-mode', '--disable-parallel', '--configfile', str(folder / 'NuGet.Config'), *FLAGS],
        [str(DOTNET), 'build', project, '--no-restore', *FLAGS],
    ]
    code = 0
    for index, command in enumerate(commands):
        log = run / ('build-' + actual_lane + '-' + str(index) + '.log')
        code = capture(command, folder, env, log, 600)
        result['commands'].append({'argv': command, 'exit_code': code, 'log_sha256': sha(log.read_bytes())})
        print('T12 build ' + actual_lane + ' step=' + str(index) + ' exit=' + str(code))
        if code:
            break
    if not code:
        lock = json.loads((folder / 'src/W.EntityFrameworkCore.Dameng/packages.lock.json').read_text())['dependencies']['net10.0']
        if 'DM.DmProvider' in lock or lock['W.DmProvider']['resolved'] != manifest['candidate']['version'] or lock['Microsoft.EntityFrameworkCore.Relational']['resolved'] != '10.0.12':
            raise ValueError('candidate_dependency_mismatch')
        output = folder / Path(project).parent / 'bin/Debug/net10.0'
        if sha((output / 'W.DmProvider.dll').read_bytes()) != manifest['candidate']['driver_dll_sha256']:
            raise ValueError('loaded_driver_hash_mismatch')
        result['driver_dll_sha256'] = manifest['candidate']['driver_dll_sha256']
        result['binaries'] = {str(p.relative_to(folder)): sha(p.read_bytes()) for p in output.iterdir() if p.is_file()}
        result['accepted'] = True
    graph_locks = [folder / 'src/W.EntityFrameworkCore.Dameng/packages.lock.json', folder / Path(project).parent / 'packages.lock.json']
    result['locks'] = {str(p.relative_to(folder)): sha(p.read_bytes()) for p in graph_locks if p.exists()}
    path.write_text(json.dumps(result, indent=2) + '\n')
    verify(run, manifest)
    return code


def worker(run, manifest, lane, mode, batch):
    raw = os.environ.get('DAMENG_TEST_CONNECTION_STRING')
    if not raw:
        raise ValueError('integration_pending_release_failed')
    values = environment_secrets()
    folder = run / 'W'
    env = config(run, lane)
    env['DAMENG_TEST_CONNECTION_STRING'] = raw
    env['DAMENG_T12_CANDIDATE_VERSION'] = manifest['candidate']['version']
    result_dir = run / ('results-' + lane + '-' + batch)
    result_dir.mkdir(exist_ok=True)
    if mode in ('before', 'after', 'permissions'):
        command = [str(DOTNET), str(folder / 'T12Audit/bin/Debug/net10.0/T12Audit.dll'), mode,
                   str(result_dir / ('audit-' + mode + '.json')), str(result_dir / 'inventory.json'), manifest['candidate']['driver_dll_sha256']]
        return capture(command, folder, env, result_dir / ('audit-' + mode + '.log'), 90, values)
    if lane == 'unit':
        raise ValueError('unit_does_not_load_db_secret')
    project = PROJECTS['specification'] if lane == 'specification' else PROJECTS['functional']
    env['DAMENG_T12_DRIVER_LOAD_RECORD'] = str(result_dir / 'loaded-driver.json')
    command = [str(DOTNET), 'test', project, '--no-build', '--no-restore', *FLAGS,
               '--logger', 'trx;LogFileName=tests.trx', '--results-directory', str(result_dir)]
    if lane in FILTERS:
        command += ['--filter', FILTERS[lane]]
    code = capture(command, folder, env, result_dir / 'tests.log', 900, values)
    # Reject sensitive artifacts; publish only sanitized text. A leak remains a failed gate.
    sensitive = False
    for file in result_dir.rglob('*'):
        if file.is_file():
            text = file.read_text(errors='replace')
            safe = redact(text, values)
            if safe != text:
                sensitive = True
                file.write_text(safe)
    trx = result_dir / 'tests.trx'
    accepted = False
    c = None
    if trx.exists():
        c = counts(trx)
        proof = result_dir / 'loaded-driver.json'
        loaded = proof.exists() and json.loads(proof.read_text())['driver_dll_sha256'] == manifest['candidate']['driver_dll_sha256']
        accepted = code == 0 and not sensitive and loaded and accepted_counts(c)
    (result_dir / 'test-summary.json').write_text(json.dumps({'argv': command, 'exit_code': code, 'counts': c,
        'sensitive_artifact_detected': sensitive, 'accepted': accepted, 'candidate': manifest['candidate']}, indent=2) + '\n')
    return 0 if accepted else code or 3


def run_lane(run, manifest, lane, mode):
    actual_lane = lane if lane in PROJECTS else 'functional'
    if lane not in manifest['lanes_ready']:
        raise ValueError('lane_not_ready')
    needed = [actual_lane] if lane == 'unit' else ['audit', actual_lane] if lane != 'audit' else ['audit']
    for name in needed:
        state = json.loads((run / ('build-' + name + '.json')).read_text())
        if not state['accepted']:
            raise ValueError('lane_build_unaccepted')
        for path, expected in state['binaries'].items():
            if sha((run / 'W' / path).read_bytes()) != expected:
                raise ValueError('compiled_binary_drift')
    batch = stamp()
    env = config(run, lane)
    if lane == 'unit':
        result_dir = run / ('results-unit-' + batch); result_dir.mkdir()
        env['DAMENG_T12_DRIVER_LOAD_RECORD'] = str(result_dir / 'loaded-driver.json')
        command = [str(DOTNET), 'test', PROJECTS['unit'], '--no-build', '--no-restore', *FLAGS,
                   '--logger', 'trx;LogFileName=tests.trx', '--results-directory', str(result_dir)]
        code = capture(command, run / 'W', env, result_dir / 'tests.log', 600)
        c = counts(result_dir / 'tests.trx') if (result_dir / 'tests.trx').exists() else None
        proof = result_dir / 'loaded-driver.json'
        loaded = proof.exists() and json.loads(proof.read_text())['driver_dll_sha256'] == manifest['candidate']['driver_dll_sha256']
        accepted = code == 0 and loaded and accepted_counts(c)
        (result_dir / 'test-summary.json').write_text(json.dumps({'argv': command, 'exit_code': code, 'counts': c, 'accepted': accepted}, indent=2) + '\n')
        verify(run, manifest)
        for path, expected in state['binaries'].items():
            if sha((run / 'W' / path).read_bytes()) != expected:
                raise ValueError('post_run_binary_drift')
        return 0 if accepted else code or 3
    prefix = [str(ROOT / 'scripts/with-dameng-test.sh'), sys.executable, '-B', str(HERE / 't12_run.py'), 'worker',
              '--output', str(run), '--lane', lane, '--batch', batch, '--mode']
    if lane == 'audit':
        code = subprocess.run(prefix + ['permissions'], cwd=run / 'W', env=env).returncode
        verify(run, manifest)
        verify_binaries(run, needed)
        return code
    before = subprocess.run(prefix + ['before'], cwd=run / 'W', env=env).returncode
    if before:
        verify(run, manifest)
        verify_binaries(run, needed)
        return before
    work = subprocess.run(prefix + ['test'], cwd=run / 'W', env=env).returncode
    after = subprocess.run(prefix + ['after'], cwd=run / 'W', env=env).returncode
    (run / ('lane-' + lane + '-' + batch + '.json')).write_text(json.dumps({'lane': lane, 'before_exit': before,
        'test_exit': work, 'after_exit': after, 'accepted': work == after == 0, 'candidate': manifest['candidate']}, indent=2) + '\n')
    verify(run, manifest)
    for name in needed:
        state = json.loads((run / ('build-' + name + '.json')).read_text())
        for path, expected in state['binaries'].items():
            if sha((run / 'W' / path).read_bytes()) != expected:
                raise ValueError('post_run_binary_drift')
    return work or after


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('action', choices=('build', 'run', 'worker'))
    parser.add_argument('--output', required=True)
    parser.add_argument('--lane', choices=('audit', 'unit', 'functional', 'specification', 'reverse', 'migrations', 'cli', 'scripts', 'queries'), required=True)
    parser.add_argument('--mode', choices=('test', 'before', 'after', 'permissions'), default='test')
    parser.add_argument('--batch', default='')
    args = parser.parse_args()
    run = bounded(args.output)
    manifest = json.loads((run / 'manifest.json').read_text())
    verify(run, manifest)
    if args.action == 'build': return build(run, manifest, args.lane)
    if args.action == 'worker':
        code = worker(run, manifest, args.lane, args.mode, args.batch)
        verify(run, manifest)
        actual = args.lane if args.lane in PROJECTS else 'functional'
        verify_binaries(run, ['audit'] if args.mode != 'test' else [actual])
        return code
    return run_lane(run, manifest, args.lane, args.mode)


if __name__ == '__main__':
    try:
        sys.exit(main())
    except Exception as error:
        reason = str(error) if isinstance(error, ValueError) and re.fullmatch('[a-zA-Z0-9_]+', str(error)) else type(error).__name__
        print('t12_runner_failed ' + reason)
        sys.exit(1)
