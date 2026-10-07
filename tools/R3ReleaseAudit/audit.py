#!/usr/bin/env python3
"""T18 exact twelve-suite offline gate and exact-package resource consumer. Runtime output is safe JSON only."""
import argparse
import datetime
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import platform
import re
import shutil
import signal
import subprocess
import sys
import time
import uuid

ROOT = Path(__file__).resolve().parents[2]
FLAGS = ['-m:1', '/nodeReuse:false', '/p:UseSharedCompilation=false', '--disable-build-servers']
MANIFEST = ROOT / 'tools/R3ReleaseAudit/offline-manifest.json'
EXPECTED = ('Configuration', 'Session', 'Transport', 'Security', 'Command', 'Type', 'Transaction', 'Isolation', 'Async', 'Pool', 'Lob', 'Diagnostics')
MARKERS = ('SYNTHETIC_SQL_PASSWORD_TYPE_SECRET', 'SYNTHETIC_CONTEXT_SQL_SCHEMA_ENDPOINT_SECRET', 'SYNTHETIC_CALLBACK_SQL_PASSWORD_SECRET',
           'SYNTHETIC_ASYNCLOCAL_SECRET', 'SYNTHETIC_USER_SECRET', 'SYNTHETIC_PASSWORD_SECRET')
# Only pure TRX validation/sanitization helpers are reused. Old runner entrypoints and evidence are not executed or modified.
spec = importlib.util.spec_from_file_location('r1_trx_readonly_helpers', ROOT / 'tools/R1ReleaseAudit/audit.py')
TRX = importlib.util.module_from_spec(spec)
sys.dont_write_bytecode = True
spec.loader.exec_module(TRX)


class Reject(Exception):
    def __init__(self, reason, code=1): self.reason, self.code = reason, code


def emit(value):
    print(json.dumps({'schema_version': 1, 'task': 'T18', **value}, separators=(',', ':')))


def sha(path): return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def write(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2, sort_keys=True) + '\n')


def new_run():
    base = Path(os.environ.get('R3_RESULTS_DIR', str(ROOT / '.local/verification/r3/t18/runs'))).resolve()
    run = base / (datetime.datetime.now(datetime.timezone.utc).strftime('%Y%m%dT%H%M%SZ') + '-' + uuid.uuid4().hex[:10])
    for suffix in ('public', '.private', '.private/cli-home', '.private/cache'): (run / suffix).mkdir(parents=True, mode=0o700)
    return run


def environment(run, database=False):
    env = os.environ.copy()
    if not database:
        for key in list(env):
            if key.startswith('DAMENG'): env.pop(key)
    env.update(DOTNET_CLI_HOME=str(run / '.private/cli-home'), DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1', DOTNET_CLI_TELEMETRY_OPTOUT='1',
               NUGET_PACKAGES=str(run / '.private/cache'))
    return env


def command(argv, run, stage, env, timeout=600):
    log = run / '.private' / (stage + '.log')
    began = time.monotonic()
    with log.open('wb') as stream:
        process = subprocess.Popen(argv, cwd=ROOT, env=env, stdout=stream, stderr=subprocess.STDOUT, start_new_session=True)
        try: code = process.wait(timeout)
        except subprocess.TimeoutExpired:
            os.killpg(process.pid, signal.SIGKILL)
            process.wait()
            code = 124
    row = {'stage': stage, 'argv': [str(arg) for arg in argv], 'exit_code': code, 'elapsed_seconds': time.monotonic() - began}
    write(run / 'public' / (stage + '-command.json'), row)
    if code != 0: raise Reject(stage + '_failed', code if 0 < code < 256 else 1)
    return log, row


def sdk(run, env):
    dotnet = shutil.which(os.environ.get('DOTNET_COMMAND', 'dotnet'))
    if not dotnet: raise Reject('dotnet_missing')
    pin = json.loads((ROOT / 'global.json').read_text())['sdk']
    if pin != {'version': '10.0.203', 'rollForward': 'disable'}: raise Reject('sdk_pin_changed')
    log, _ = command([dotnet, '--version'], run, 'SDK', env, 30)
    if log.read_text().strip() != pin['version']: raise Reject('sdk_version_mismatch')
    write(run / 'public/SDK.json', {'sdk': pin['version'], 'os': platform.system(), 'architecture': platform.machine()})
    return dotnet


def inputs():
    roots = [ROOT / 'src/W.DmProvider', ROOT / 'tests/fixtures', ROOT / 'tools/R3ReleaseAudit', ROOT / 'tools/R3ResourceProbe',
             ROOT / 'eng/t18.sh', ROOT / 'eng/T18.md', ROOT / '.github/workflows/r3-offline.yml', ROOT / 'global.json']
    roots += [ROOT / f'tests/W.DmProvider.{name}Tests' for name in EXPECTED]
    rows = []
    for root in roots:
        for path in sorted(root.rglob('*') if root.is_dir() else [root]):
            if path.is_file() and not any(part in ('bin', 'obj', '__pycache__', '.local') for part in path.relative_to(ROOT).parts):
                rows.append({'path': str(path.relative_to(ROOT)), 'sha256': sha(path)})
    return sorted(rows, key=lambda row: row['path'])


def safe_artifacts(run):
    for path in sorted((run / 'public').rglob('*')):
        if path.is_file():
            raw = path.read_text(errors='replace')
            if any(marker in raw for marker in MARKERS): raise Reject('safe_artifact_marker_leak')
    write(run / 'public/artifact-hashes.json', [{'path': str(path.relative_to(run / 'public')), 'sha256': sha(path)}
          for path in sorted((run / 'public').rglob('*')) if path.is_file() and path.name != 'artifact-hashes.json'])


def resource(run, mode, version, feed, dotnet, env):
    if not re.fullmatch(r'[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?', version): raise Reject('version_invalid', 64)
    feed = Path(feed).resolve(); package = feed / f'W.DmProvider.{version}.nupkg'
    if not package.is_file(): raise Reject('package_missing', 66)
    package_manifest = run / 'public/package-manifest.json'
    command([sys.executable, str(ROOT / 'tools/R2AsyncProbe/package_manifest.py'), str(package), version, str(package_manifest)], run, 'package_inspection', env)
    app = run / '.private/app'; obj = run / '.private/consumer-obj'; app.mkdir(); obj.mkdir()
    command([dotnet, 'build', str(ROOT / 'tools/R3ResourceProbe/R3ResourceProbe.csproj'), '-c', 'Release', '--output', str(app),
             f'/p:R3PackageVersion={version}', f'/p:R3PackageSource={feed}', f'/p:RestoreSources={feed}',
             f'/p:BaseIntermediateOutputPath={obj}/', f'/p:MSBuildProjectExtensionsPath={obj}/', *FLAGS], run, 'consumer_build', env)
    command([sys.executable, str(ROOT / 'tools/R3ResourceProbe/source_identity.py'), str(ROOT), str(run / 'public/consumer-source-manifest.json')], run, 'consumer_source_identity', env)
    probe = run / 'public/probe.json'; stdout = run / 'public/probe-stdout.json'
    argv = [sys.executable, str(ROOT / 'tools/R3ResourceProbe/run_probe.py'), str(stdout), dotnet, str(app / 'R3ResourceProbe.dll'), mode,
            str(ROOT), str(package_manifest), str(probe)]
    if mode == 'tls': argv = [str(ROOT / 'scripts/with-dameng-tls-test.sh'), *argv]
    command(argv, run, 'runtime_' + mode, env, 930)
    command([sys.executable, str(ROOT / 'tools/R3ResourceProbe/validate.py'), str(probe), str(package_manifest), str(app / 'W.DmProvider.dll'), mode], run, 'consumer_validation', env)
    final = run / 'public/package-manifest-final.json'
    command([sys.executable, str(ROOT / 'tools/R2AsyncProbe/package_manifest.py'), str(package), version, str(final)], run, 'package_reinspection', env)
    if package_manifest.read_bytes() != final.read_bytes(): raise Reject('immutable_package_changed')
    safe_artifacts(run)


def offline(run):
    env = environment(run); dotnet = sdk(run, env)
    definition = json.loads(MANIFEST.read_text())
    if tuple(suite['name'] for suite in definition['suites']) != EXPECTED or len(definition['suites']) != 12: raise Reject('suite_manifest_changed')
    write(run / 'public/offline-manifest.json', definition)
    before = inputs(); write(run / 'public/source-before.json', before)
    version = os.environ.get('R3_CANDIDATE_VERSION', '0.1.0-r3.t18.' + datetime.datetime.now(datetime.timezone.utc).strftime('%Y%m%d%H%M%S'))
    if not re.fullmatch(r'0\.1\.0-r3\.[0-9A-Za-z.-]+', version): raise Reject('candidate_version_invalid')
    properties = ['/p:Configuration=Release', '/p:Version=' + version]
    suites = []
    for item in definition['suites']:
        project = ROOT / item['project']; name = item['name']; results = run / '.private/trx' / name; results.mkdir(parents=True)
        command([dotnet, 'restore', str(project), *properties, *FLAGS], run, name + '_restore', env)
        _, host = command([dotnet, 'test', str(project), '--no-restore', '--logger', 'trx;LogFileName=contracts.trx',
                           '--results-directory', str(results), *properties, *FLAGS], run, name + '_test', env)
        root, counts = TRX.strict_trx(results / 'contracts.trx', {'host_exit_code': host['exit_code'], 'timed_out': False})
        TRX.safe_trx(root, run / 'public' / (name + '.trx'))
        assets = json.loads((project.parent / 'obj/project.assets.json').read_text())
        write(run / 'public' / (name + '-dependencies.json'), {'project': item['project'], 'libraries':
              [{'identity': key, 'type': value.get('type'), 'sha512': value.get('sha512')} for key, value in sorted(assets['libraries'].items())]})
        product = project.parent / 'bin/Release/net10.0/W.DmProvider.dll'
        suites.append({'suite': name, 'counts': counts, 'product_sha256': sha(product), 'status': 'passed'})
        write(run / 'public/suites.json', suites)
    product = ROOT / 'src/W.DmProvider/bin/Release/net10.0/W.DmProvider.dll'
    if len(suites) != 12 or any(row['product_sha256'] != sha(product) for row in suites): raise Reject('suite_product_identity_mismatch')
    feed = run / '.private/feed'; feed.mkdir()
    command([dotnet, 'pack', str(ROOT / 'src/W.DmProvider/W.DmProvider.csproj'), '--no-build', '--no-restore', '-c', 'Release',
             '--output', str(feed), '/p:Version=' + version, *FLAGS], run, 'exact_package_pack', env)
    resource(run, 'offline', version, feed, dotnet, env)
    after = inputs(); write(run / 'public/source-after.json', after)
    if before != after: raise Reject('frozen_source_changed')
    summary = {'status': 'offline_verified', 'scope': 'exact_twelve_suites_plus_isolated_first_init_package_probe', 'integration': 'integration_pending',
               'production_release_accepted': False, 'candidate_version': version, 'suites': suites, 'total_passed': sum(row['counts']['passed'] for row in suites),
               'product_sha256': sha(product), 'run_dir': str(run), 'upstream_pending': definition['upstream_pending']}
    write(run / 'public/summary.json', summary); safe_artifacts(run); emit(summary)


def main():
    if len(sys.argv) < 2: raise Reject('usage', 64)
    mode = sys.argv[1]
    if mode == 'release-environment':
        if any(not os.environ.get(key, '').strip() for key in ('DAMENG_TEST_CONNECTION_STRING', 'DAMENG_TLS_TEST_CONNECTION_STRING')):
            raise Reject('required_TEST_or_TLS_environment_missing', 66)
        if len(sys.argv) != 4: raise Reject('present_environment_requires_exact_package_preflight', 64)
    if mode not in ('offline', 'tls', 'release-environment'): raise Reject('usage', 64)
    run = new_run()
    try:
        if mode == 'offline':
            if len(sys.argv) != 2: raise Reject('usage', 64)
            offline(run)
        else:
            if len(sys.argv) != 4: raise Reject('usage', 64)
            env = environment(run, database=True); dotnet = sdk(run, env)
            resource(run, mode, sys.argv[2], sys.argv[3], dotnet, env)
            emit({'status': 'scoped_gate_verified', 'mode': mode, 'run_dir': str(run), 'production_release_accepted': False})
    except Exception as error:
        write(run / 'public/summary.json', {'status': 'rejected', 'reason': error.reason if isinstance(error, Reject) else 'strict_gate_failed', 'production_release_accepted': False})
        raise


if __name__ == '__main__':
    try: main()
    except Exception as error:
        emit({'status': 'rejected', 'reason': error.reason if isinstance(error, Reject) else 'strict_gate_failed', 'production_release_accepted': False})
        sys.exit(error.code if isinstance(error, Reject) else 1)
