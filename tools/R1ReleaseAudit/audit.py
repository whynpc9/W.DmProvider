#!/usr/bin/env python3
"""Portable R1 gates. Build output stays private; published evidence is structured."""
import argparse
import contextlib
import io
import datetime
import hashlib
import importlib.util
import json
import os
import pathlib
import platform
import re
import shutil
import signal
import subprocess
import sys
import tempfile
import time
import uuid
import xml.etree.ElementTree as ET
from xml.sax.saxutils import escape

ROOT = pathlib.Path(__file__).resolve().parents[2]
SUITES = ('Configuration', 'Session', 'Transport', 'Security', 'Command', 'Type', 'Transaction', 'Isolation')
FLAGS = ['-m:1', '/nodeReuse:false', '/p:UseSharedCompilation=false', '--disable-build-servers']
NS = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
COUNTERS = ('total', 'executed', 'passed', 'failed', 'error', 'timeout', 'aborted', 'inconclusive',
            'passedButRunAborted', 'notRunnable', 'notExecuted', 'disconnected', 'warning', 'completed', 'inProgress', 'pending')

class SafeParser(argparse.ArgumentParser):
    def error(self, message):
        output({'status': 'rejected', 'reason': 'usage', 'release_accepted': False})
        raise SystemExit(64)

class Rejected(Exception):
    def __init__(self, reason, code=1):
        self.reason, self.code = reason, code


def sha(path):
    return hashlib.sha256(pathlib.Path(path).read_bytes()).hexdigest()


def write(path, value):
    path = pathlib.Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n')


def output(value):
    print(json.dumps({'schema_version': 1, 'task': 'R1', **value}, separators=(',', ':')))


def new_run():
    base = pathlib.Path(os.environ.get('R1_RESULTS_DIR', str(ROOT / '.local/t12-offline/runs'))).resolve()
    run = base / (datetime.datetime.now(datetime.timezone.utc).strftime('%Y%m%dT%H%M%SZ') + '-' + uuid.uuid4().hex[:12])
    run.mkdir(parents=True, mode=0o700)
    (run / '.private').mkdir(mode=0o700)
    (run / 'public').mkdir(mode=0o700)
    return run


def environment(run, database=False):
    env = os.environ.copy()
    if not database:
        env.pop('DAMENG_TEST_CONNECTION_STRING', None)
    env.update(DOTNET_CLI_HOME=str(run / '.private/cli-home'), DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1',
               DOTNET_CLI_TELEMETRY_OPTOUT='1', NUGET_PACKAGES=str(run / '.private/nuget-cache'))
    for path in (env['DOTNET_CLI_HOME'], env['NUGET_PACKAGES']): pathlib.Path(path).mkdir(mode=0o700)
    return env


def command(args, run, stage, env, timeout=600):
    log = run / '.private' / (stage + '.log')
    start = time.monotonic()
    with log.open('wb') as stream:
        process = subprocess.Popen(args, cwd=ROOT, env=env, stdout=stream, stderr=subprocess.STDOUT, start_new_session=True)
        timed_out = False
        try:
            code = process.wait(timeout=timeout)
        except subprocess.TimeoutExpired:
            timed_out = True
            os.killpg(process.pid, signal.SIGTERM)
            try: process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                os.killpg(process.pid, signal.SIGKILL)
                process.wait(timeout=5)
            code = 124
    record = {'stage': stage, 'host_exit_code': code, 'timed_out': timed_out,
              'elapsed_seconds': round(time.monotonic() - start, 3)}
    write(run / 'public' / (stage + '-host.json'), record)
    return record, log


def require_command(args, run, stage, env, timeout=600):
    record, log = command(args, run, stage, env, timeout)
    if record['host_exit_code'] != 0 or record['timed_out']:
        raise Rejected(stage + '_host_failed')
    return log


def candidate_properties(run):
    version = os.environ.get('R1_CANDIDATE_VERSION')
    if version is not None and not re.fullmatch(r'0\.1\.0-r1\.[0-9]{14}', version):
        raise Rejected('R1_build_version_invalid')
    properties = ['/p:Configuration=Release']
    if version is not None: properties.append('/p:Version=' + version)
    write(run / 'public/build-properties.json', {'configuration': 'Release', 'candidate_version': version,
        'properties': properties, 'package_version_requires_same_producer_version': True})
    return properties


def prepare_sdk(run, env):
    pin = json.loads((ROOT / 'global.json').read_text())['sdk']
    if pin.get('rollForward') != 'disable': raise Rejected('SDK_pin_not_exact')
    dotnet = shutil.which('dotnet')
    if not dotnet: raise Rejected('dotnet_unavailable')
    log = require_command([dotnet, '--version'], run, 'SDK_version', env, 30)
    version = log.read_text().strip()
    if version != pin['version']: raise Rejected('SDK_version_mismatch')
    write(run / 'public/SDK.json', {'sdk': version, 'roll_forward': pin['rollForward'],
          'os': platform.system(), 'architecture': platform.machine(), 'global_json_sha256': sha(ROOT / 'global.json')})
    return dotnet


def config(run, sources):
    path = run / '.private/NuGet.Config'
    entries = ''.join('<add key="source' + str(i) + '" value="' + escape(str(value), {'"': '&quot;'}) + '"/>'
                      for i, value in enumerate(sources))
    path.write_text('<configuration><packageSources><clear/>' + entries + '</packageSources></configuration>\n')
    return path


def inputs():
    roots = [ROOT / 'src/W.DmProvider', ROOT / 'tests/fixtures', ROOT / 'tools/R1ReleaseAudit',
             ROOT / 'tools/IsolationProbe/ProbeSafety.cs', ROOT / 'global.json', ROOT / 'THIRD-PARTY-NOTICES.md',
             ROOT / 'eng/t12-offline.sh', ROOT / 'eng/T12.md', ROOT / '.github/workflows/r1-offline.yml']
    roots += [ROOT / f'tests/W.DmProvider.{name}Tests' for name in SUITES]
    rows = []
    for root in roots:
        for path in sorted(root.rglob('*') if root.is_dir() else [root]):
            if path.is_file() and not any(part in ('bin', 'obj', '.local', '__pycache__') for part in path.relative_to(ROOT).parts) and path.name.lower() != 'nuget.config' and path.name != '.DS_Store':
                rows.append({'path': str(path.relative_to(ROOT)), 'sha256': sha(path)})
    return sorted(rows, key=lambda row: row['path'])


def strict_trx(path, host):
    if host['host_exit_code'] != 0 or host['timed_out']: raise Rejected('testhost_exit_rejected')
    if not path.is_file() or path.stat().st_size == 0: raise Rejected('TRX_missing')
    root = ET.parse(path).getroot()
    summary = root.find('t:ResultSummary', NS)
    counters = root.find('t:ResultSummary/t:Counters', NS)
    if summary is None or counters is None or any(counters.get(key) is None for key in COUNTERS): raise Rejected('TRX_incomplete')
    counts = {key: int(counters.get(key)) for key in COUNTERS}
    results = root.findall('t:Results/t:UnitTestResult', NS)
    bad_runs = root.findall('.//t:RunInfo', NS)
    if (summary.get('outcome') not in ('Completed', 'Passed') or counts['total'] <= 0 or
        counts['executed'] != counts['total'] or counts['passed'] != counts['total'] or
        any(value for key, value in counts.items() if key not in ('total', 'executed', 'passed')) or
        len(results) != counts['total'] or any(result.get('outcome') != 'Passed' for result in results) or
        any(info.get('outcome') in ('Error', 'Failed', 'Aborted') or info.get('severity') == 'Error' for info in bad_runs)):
        raise Rejected('TRX_failure_skip_or_abort')
    return root, counts


def safe_trx(root, out):
    # Validate the original first. Published TRX keeps IDs/outcomes/counters but no
    # arbitrary test output, attachments, exception messages or argument values.
    forbidden = {'Output', 'StdOut', 'StdErr', 'ErrorInfo', 'CollectorDataEntries', 'ResultFiles', 'Attachments', 'RunInfos'}
    for parent in list(root.iter()):
        for child in list(parent):
            if child.tag.split('}')[-1] in forbidden: parent.remove(child)
    for node in root.iter():
        for key in ('testName', 'name'):
            if key in node.attrib:
                node.set(key, node.get(key, '').split('(')[0].strip())
    ET.ElementTree(root).write(out, encoding='utf-8', xml_declaration=True)


def dependencies(project, output):
    assets = json.loads((project.parent / 'obj/project.assets.json').read_text())
    rows = []
    for name, entry in sorted(assets.get('libraries', {}).items()):
        rows.append({'identity': name, 'type': entry.get('type'), 'sha512': entry.get('sha512')})
    write(output, {'schema_version': 1, 'project': str(project.relative_to(ROOT)), 'libraries': rows,
                   'restore_sources': ['https://api.nuget.org/v3/index.json']})


def offline(run):
    env = environment(run)
    dotnet = prepare_sdk(run, env)
    properties = candidate_properties(run)
    cfg = config(run, ['https://api.nuget.org/v3/index.json'])
    before = inputs(); write(run / 'public/input-before.json', before)
    suites, failures = [], []
    for name in SUITES:
        project = ROOT / f'tests/W.DmProvider.{name}Tests/W.DmProvider.{name}Tests.csproj'
        result_dir = run / '.private/TRX' / name; result_dir.mkdir(parents=True)
        restored, _ = command([dotnet, 'restore', str(project), '--configfile', str(cfg), *properties, *FLAGS], run, name + '_restore', env)
        if restored['host_exit_code'] != 0 or restored['timed_out']:
            failures.append({'suite': name, 'reason': 'restore_host_failed'}); continue
        host, _ = command([dotnet, 'test', str(project), '--no-restore', '--logger', 'trx;LogFileName=contracts.trx',
                          '--results-directory', str(result_dir), *properties, *FLAGS], run, name + '_test', env)
        row = {'suite': name, 'host': host}
        try:
            root, counts = strict_trx(result_dir / 'contracts.trx', host)
            safe_trx(root, run / 'public' / (name + '.trx'))
            dependencies(project, run / 'public' / (name + '-dependencies.json'))
            row.update(status='offline_verified', counts=counts,
                       tested_driver_sha256=sha(project.parent / 'bin/Release/net10.0/W.DmProvider.dll'))
        except (Rejected, ValueError, ET.ParseError, OSError) as error:
            reason = error.reason if isinstance(error, Rejected) else 'result_shape_invalid'
            row.update(status='rejected', reason=reason); failures.append({'suite': name, 'reason': reason})
        suites.append(row)
    after = inputs(); write(run / 'public/input-after.json', after)
    if before != after: failures.append({'reason': 'implementation_inputs_changed'})
    product = ROOT / 'src/W.DmProvider/bin/Release/net10.0/W.DmProvider.dll'
    product_hash = sha(product) if product.is_file() else None
    if len(suites) != len(SUITES) or any(row.get('tested_driver_sha256') != product_hash for row in suites) or product_hash is None:
        failures.append({'reason': 'tested_product_DLL_identity_mismatch'})
    summary = {'status': 'offline_verified' if not failures and len(suites) == len(SUITES) else 'rejected',
               'scope': 'eight_offline_suites_only', 'integration': 'integration_pending', 'release_accepted': False,
               'database_environment_used': False, 'tested_driver_sha256': product_hash,
               'candidate_version': os.environ.get('R1_CANDIDATE_VERSION'), 'suites': suites, 'failures': failures,
               'run_dir': str(run), 'total_passed': sum(row.get('counts', {}).get('passed', 0) for row in suites)}
    write(run / 'public/summary.json', summary); output(summary)
    return 0 if summary['status'] == 'offline_verified' else 1


def release_environment(run):
    # Missing environment fails before SDK installation, build, restore or login.
    raw = os.environ.get('DAMENG_TEST_CONNECTION_STRING')
    if not raw or not raw.strip():
        result = {'status': 'rejected', 'reason': 'integration_environment_missing',
                  'missing': ['DAMENG_TEST_CONNECTION_STRING'], 'release_accepted': False}
        write(run / 'public/environment.json', result); output(result); return 66
    env = environment(run, database=False); dotnet = prepare_sdk(run, env)
    cfg = config(run, ['https://api.nuget.org/v3/index.json'])
    project = ROOT / 'tools/R1ReleaseAudit/R1ReleaseAudit.csproj'
    properties = candidate_properties(run) + ['/p:BaseOutputPath=' + str(run / '.private/environment-build') + '/']
    require_command([dotnet, 'restore', str(project), '--configfile', str(cfg), *properties, *FLAGS], run, 'environment_restore', env)
    require_command([dotnet, 'build', str(project), '--no-restore', *properties, *FLAGS], run, 'environment_build', env)
    check_env = env.copy(); check_env['DAMENG_TEST_CONNECTION_STRING'] = raw
    host, log = command([dotnet, str(run / '.private/environment-build/Release/net10.0/R1ReleaseAudit.dll'), 'release-environment'],
                        run, 'environment_check', check_env, 30)
    try: result = json.loads(log.read_text())
    except (ValueError, OSError): raise Rejected('environment_result_invalid')
    write(run / 'public/environment.json', result); output(result)
    return host['host_exit_code']


def existing_inspector():
    spec = importlib.util.spec_from_file_location('r1_existing_t03', ROOT / 'tools/ProductPackageProbe/inspect.py')
    module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module)
    return module


def r1_source_inventory():
    product = ROOT / 'src/W.DmProvider'
    included, excluded = [], []
    for path in sorted(product.rglob('*')):
        if not path.is_file(): continue
        relative = path.relative_to(product)
        if any(part in ('bin', 'obj', '.local', '__pycache__') for part in relative.parts): continue
        if path.name.lower() == 'nuget.config' or path.name == '.DS_Store':
            excluded.append({'path': str(path.relative_to(ROOT)), 'reason': 'non_source_restore_configuration_or_OS_metadata'})
            continue
        included.append(path)
    included.append(ROOT / 'THIRD-PARTY-NOTICES.md')
    digest = hashlib.sha256()
    for path in sorted(included, key=str):
        digest.update(str(path.relative_to(ROOT)).encode()); digest.update(b'\0'); digest.update(bytes.fromhex(sha(path)))
    return digest.hexdigest(), included, excluded


def filtered_source_snapshot(run):
    digest, included, excluded = r1_source_inventory()
    snapshot = run / '.private/source-snapshot'
    for path in included:
        target = snapshot / path.relative_to(ROOT); target.parent.mkdir(parents=True, exist_ok=True); shutil.copyfile(path, target)
    write(run / 'public/R1-source-selection.json', {'algorithm': 'T03 path-plus-file-SHA256; R1 explicit transient exclusions',
        'source_sha256': digest, 'included': [{'path': str(p.relative_to(ROOT)), 'sha256': sha(p)} for p in included],
        'excluded': excluded, 'always_excluded_directories': ['bin', 'obj', '.local', '__pycache__']})
    return snapshot / 'src/W.DmProvider', digest


def package_audit(args, run):
    if not re.fullmatch(r'0\.1\.0-(t11|t12|r1)\.[0-9]{14}', args.version): raise Rejected('unique_candidate_version_required')
    if any(not re.fullmatch('[0-9a-fA-F]{64}', value) for value in (args.package_sha256, args.dll_sha256, args.source_sha256)): raise Rejected('producer_hashes_required')
    args.package_sha256, args.dll_sha256, args.source_sha256 = (value.lower() for value in (args.package_sha256, args.dll_sha256, args.source_sha256))
    package = pathlib.Path(args.package).resolve()
    if package.name != f'W.DmProvider.{args.version}.nupkg' or sha(package) != args.package_sha256: raise Rejected('producer_package_pin_mismatch')
    before = inputs(); write(run / 'public/input-before.json', before)
    env = environment(run); dotnet = prepare_sdk(run, env)
    local_feed = run / '.private/candidate'; local_feed.mkdir()
    target = local_feed / package.name; shutil.copyfile(package, target)
    inspector = existing_inspector()
    manifest = local_feed / 'manifest.json'
    source_snapshot, expected_source = filtered_source_snapshot(run)
    if expected_source != args.source_sha256: raise Rejected('producer_source_pin_mismatch')
    try:
        with contextlib.redirect_stdout(io.StringIO()):
            inspector.package(target, manifest, args.version, source_snapshot)
    except SystemExit: raise Rejected('T03_package_inspection_failed')
    saved = json.loads(manifest.read_text())
    if saved['source_sha256'] != args.source_sha256: raise Rejected('producer_source_pin_mismatch')
    if saved['assets'].get('lib/net10.0/W.DmProvider.dll') != args.dll_sha256: raise Rejected('producer_DLL_pin_mismatch')
    # Preserve the existing exact-package consumer's directory and feed contract.
    env['NUGET_PACKAGES'] = str(local_feed / 'nuget-cache'); pathlib.Path(env['NUGET_PACKAGES']).mkdir()
    cfg = config(run, [local_feed])
    props = [f'/p:T03PackageVersion={args.version}', f'/p:BaseIntermediateOutputPath={local_feed}/obj/',
             f'/p:OutputPath={local_feed}/app/', '/p:AppendTargetFrameworkToOutputPath=false']
    consumer = ROOT / 'tools/ProductPackageProbe/ProductPackageProbe.csproj'
    require_command([dotnet, 'restore', str(consumer), '--configfile', str(cfg), '--source', str(local_feed), *props, *FLAGS], run, 'package_consumer_restore', env)
    require_command([dotnet, 'build', str(consumer), '--no-restore', *props, *FLAGS], run, 'package_consumer_build', env)
    try:
        with contextlib.redirect_stdout(io.StringIO()): inspector.consumer(local_feed, args.version)
    except SystemExit: raise Rejected('T03_consumer_graph_rejected')
    log = require_command([dotnet, str(local_feed / 'app/ProductPackageProbe.dll'), 'offline', str(ROOT), str(manifest)], run, 'package_consumer', env, 60)
    loaded = json.loads(log.read_text())
    if loaded.get('loaded_assembly_sha256') != args.dll_sha256: raise Rejected('actual_loaded_DLL_mismatch')
    # Generate current surface using the existing T03 Mono.Cecil exporter.
    env['NUGET_PACKAGES'] = str(run / '.private/tool-nuget'); pathlib.Path(env['NUGET_PACKAGES']).mkdir()
    cfg = config(run, ['https://api.nuget.org/v3/index.json'])
    exporter = ROOT / 'tools/RestoreBaseline/RestoreBaseline.csproj'
    require_command([dotnet, 'restore', str(exporter), '--configfile', str(cfg), *FLAGS], run, 'surface_restore', env)
    require_command([dotnet, 'build', str(exporter), '--no-restore', *FLAGS], run, 'surface_build', env)
    api_path = run / 'public/public-api-current.txt'
    require_command([dotnet, str(exporter.parent / 'bin/Debug/net10.0/RestoreBaseline.dll'), 'api',
                     str(local_feed / 'app/W.DmProvider.dll'), str(api_path)], run, 'surface_export', env, 60)
    current = api_path.read_text().splitlines()
    mapping = json.loads((ROOT / 'docs/compatibility/t03-api-map.json').read_text())
    previous = {entry.get('new_signature', entry.get('new_api')) for entry in mapping['entries']}
    previous.discard(None)
    for entry in mapping.get('new_api', mapping.get('added_api', [])):
        previous.add(entry['signature'] if isinstance(entry, dict) else entry)
    if len(current) != len(set(current)) or not current: raise Rejected('current_surface_invalid')
    delta = {'reference': 'T03 mapped static surface; not T04 API diff', 'current_api_sha256': sha(api_path),
             'current_count': len(current), 'added_since_T03': sorted(set(current) - previous),
             'removed_since_T03': sorted(previous - set(current)), 'behavior_acceptance': 'separate_T12_gate'}
    write(run / 'public/public-api-delta.json', delta)
    write(run / 'public/package-manifest.json', saved)
    write(run / 'public/actual-package-consumer.json', loaded)
    source_materials = []
    for path in ('THIRD-PARTY-NOTICES.md', 'docs/compatibility/t03-source-map.json', 'upstream/DM.DmProvider/8.3.1.47463/manifest.json'):
        source_materials.append({'path': path, 'sha256': sha(ROOT / path)})
    write(run / 'public/source-materials.json', {'source_sha256': saved['source_sha256'], 'materials': source_materials,
        'ancestry_only': 'DM.DmProvider 8.3.1.47463 source provenance, not a runtime dependency'})
    write(run / 'public/product-sbom.json', {'bomFormat': 'CycloneDX', 'specVersion': '1.6', 'version': 1,
        'metadata': {'component': {'type': 'library', 'name': 'W.DmProvider', 'version': args.version,
                                  'purl': f'pkg:nuget/W.DmProvider@{args.version}', 'bom-ref': f'pkg:nuget/W.DmProvider@{args.version}',
                                  'hashes': [{'alg': 'SHA-256', 'content': args.package_sha256}]}},
        'components': [{'type': 'file', 'name': name, 'bom-ref': 'sha256:' + value, 'hashes': [{'alg': 'SHA-256', 'content': value}]} for name,value in saved['assets'].items()],
        'dependencies': [{'ref': f'pkg:nuget/W.DmProvider@{args.version}', 'dependsOn': []}],
        'properties': [{'name': 'source-materials', 'value': 'source-materials.json'},
                       {'name': 'runtime-dependencies', 'value': 'empty nuspec/net10.0 dependency group; actual consumer references separately audited'}]})
    after = inputs(); write(run / 'public/input-after.json', after)
    if before != after: raise Rejected('implementation_inputs_changed')
    if sha(package) != args.package_sha256: raise Rejected('producer_package_changed_during_audit')
    result = {'status': 'candidate_package_audited', 'id': 'W.DmProvider', 'version': args.version,
              'package_sha256': args.package_sha256, 'dll_sha256': args.dll_sha256, 'source_sha256': args.source_sha256,
              'current_surface_sha256': sha(api_path), 'integration': 'not_executed', 'release_accepted': False,
              'run_dir': str(run)}
    write(run / 'public/package-summary.json', result); output(result); return 0


def main():
    parser = SafeParser()
    commands = parser.add_subparsers(dest='mode', required=True)
    commands.add_parser('offline')
    commands.add_parser('release-environment')
    commands.add_parser('source-hash')
    package = commands.add_parser('package-audit')
    package.add_argument('--package', required=True); package.add_argument('--version', required=True)
    package.add_argument('--package-sha256', required=True); package.add_argument('--dll-sha256', required=True)
    package.add_argument('--source-sha256', required=True)
    args = parser.parse_args()
    if args.mode == 'source-hash':
        digest, included, excluded = r1_source_inventory()
        output({'status': 'R1_source_hashed', 'source_sha256': digest, 'source_file_count': len(included),
                'excluded_non_source_count': len(excluded), 'history_evidence_modified': False})
        return 0
    run = new_run()
    try:
        if args.mode == 'offline': return offline(run)
        if args.mode == 'release-environment': return release_environment(run)
        return package_audit(args, run)
    except Exception as error:
        result = {'status': 'rejected', 'stage': args.mode, 'reason': error.reason if isinstance(error, Rejected) else 'gate_input_or_result_invalid',
                  'exception_kind': type(error).__name__, 'release_accepted': False, 'run_dir': str(run)}
        write(run / 'public/failure.json', result); output(result)
        return error.code if isinstance(error, Rejected) else 1

if __name__ == '__main__': sys.exit(main())
