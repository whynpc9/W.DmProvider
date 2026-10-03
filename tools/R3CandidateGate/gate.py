#!/usr/bin/env python3
"""Freeze, verify and audit one immutable R3 candidate; never publishes or hides missing release gates."""
import datetime
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import platform
import re
import shutil
import signal
import stat
import subprocess
import sys
import tarfile
import time
import uuid
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parents[2]
ROOTS = ('src', 'tests', 'tools', 'eng', 'scripts', 'docs', '.github')
ROOT_FILES = ('global.json', 'AGENTS.md', 'README.md', '.gitignore', 'LICENSE', 'THIRD-PARTY-NOTICES.md')
EXCLUDED = {'.git', '.local', 'bin', 'obj', '__pycache__', '.pytest_cache', '.vs', '.idea', '.DS_Store'}
FLAGS = ['-m:1', '/nodeReuse:false', '/p:UseSharedCompilation=false', '--disable-build-servers']
MARKERS = ('SYNTHETIC_SQL_PASSWORD_TYPE_SECRET', 'SYNTHETIC_CONTEXT_SQL_SCHEMA_ENDPOINT_SECRET',
           'SYNTHETIC_CALLBACK_SQL_PASSWORD_SECRET', 'SYNTHETIC_ASYNCLOCAL_SECRET',
           'SYNTHETIC_USER_SECRET', 'SYNTHETIC_PASSWORD_SECRET')
REQUIRED_MATRIX = ('t16_offline', 't16_tls', 't16_shared', 't17_offline', 't17_tls', 't17_shared',
                   'r2_tls_original', 'r2_shared_original', 't18_tls_resource', 'shared_exact_cleanup', 'ef_final')
EF_COMMIT = '113014cc74dd1f751ef97226a78d2ec855b32c8c'
EF_ARCHIVE_SHA256 = '9dead07c1de4220bd13bdadab0ed2713f43008af5e7c0cf7a629549a6739096d'


class Reject(Exception):
    def __init__(self, reason, code=1): self.reason, self.code = reason, code


def require(condition, reason, code=1):
    if not condition: raise Reject(reason, code)


def sha_bytes(data): return hashlib.sha256(data).hexdigest()
def sha(path): return sha_bytes(Path(path).read_bytes())
def encoded(value): return (json.dumps(value, sort_keys=True, indent=2) + '\n').encode()
def write(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(encoded(value))
def emit(value): print(json.dumps({'schema_version': 1, 'task': 'T19', **value}, separators=(',', ':')))


def included_files(root):
    files = []
    for name in ROOT_FILES:
        path = root / name
        require(path.is_file() and not path.is_symlink(), 'required_source_material_missing')
        files.append(path)
    for name in ROOTS:
        base = root / name
        require(base.is_dir() and not base.is_symlink(), 'source_root_missing_or_link')
        for directory, directories, names in os.walk(base, followlinks=False):
            current = Path(directory)
            for item in list(directories):
                path = current / item
                if item in EXCLUDED: directories.remove(item); continue
                require(not path.is_symlink(), 'source_symlink_rejected')
                require(item.lower() not in ('secrets', '.private'), 'secret_source_directory_rejected')
            for item in sorted(names):
                if item in EXCLUDED: continue
                path = current / item
                require(not path.is_symlink() and path.is_file(), 'source_file_type_rejected')
                require(path.suffix.lower() not in ('.env', '.pfx', '.p12', '.key'), 'secret_source_file_rejected')
                raw = path.read_bytes()
                # Source code/fixtures legitimately contain explicitly synthetic
                # controls. Reject actual PEM private-key material, not quoted
                # test constants or references to ignored secret paths.
                require(not re.search(rb'(?m)^-----BEGIN (?:RSA |EC |ENCRYPTED )?PRIVATE KEY-----\r?$', raw), 'private_key_source_rejected')
                files.append(path)
    return sorted(files, key=lambda path: path.relative_to(root).as_posix())


def source_rows(root):
    return [{'path': path.relative_to(root).as_posix(), 'size': path.stat().st_size, 'sha256': sha(path),
             'executable': bool(path.stat().st_mode & stat.S_IXUSR)} for path in included_files(root)]


def tree_hash(rows): return sha_bytes(encoded(rows))


def git_state(rows):
    # Called only by the acceptance runner. Patch bytes never enter logs/artifacts.
    head = subprocess.run(['git', 'rev-parse', 'HEAD'], cwd=ROOT, capture_output=True, check=True).stdout.decode().strip()
    require(re.fullmatch(r'[0-9a-f]{40,64}', head), 'git_head_invalid')
    diff = subprocess.run(['git', 'diff', 'HEAD', '--binary', '--no-ext-diff'], cwd=ROOT, capture_output=True, check=True).stdout
    raw = subprocess.run(['git', 'ls-files', '--others', '--exclude-standard', '-z'], cwd=ROOT, capture_output=True, check=True).stdout
    included = {row['path']: row for row in rows}
    untracked = sorted(name.decode() for name in raw.split(b'\0') if name and name.decode() in included)
    hash_input = hashlib.sha256(); hash_input.update(diff)
    for name in untracked:
        hash_input.update(b'\0untracked\0' + name.encode() + b'\0' + bytes.fromhex(included[name]['sha256']))
    return {'head': head, 'working_tree_diff_sha256': hash_input.hexdigest(), 'includes_untracked': True,
            'untracked_source_paths': untracked, 'tracked_diff_bytes': len(diff)}


def make_snapshot(run):
    rows = source_rows(ROOT)
    manifest = {'schema_version': 1, 'task': 'T19', 'files': rows, 'tree_sha256': tree_hash(rows),
                'source_link': False, 'validation_source_scope': 'all_current_validation_source_including_untracked',
                'excluded_source_ancestor_roots': ['decompiled', 'upstream', 'packages'],
                'excluded_ancestor_reason': 'not_compiled_validation_inputs; provenance_is_separately_recorded', **git_state(rows)}
    write(run / 'public/source-manifest.json', manifest)
    archive = run / 'public/candidate-source.tar.gz'
    with tarfile.open(archive, 'w:gz') as output:
        for row in rows:
            path = ROOT / row['path']; info = tarfile.TarInfo(row['path']); info.size = row['size']
            info.mode = 0o755 if row['executable'] else 0o644; info.mtime = 0
            with path.open('rb') as stream: output.addfile(info, stream)
    frozen = run / 'private/source'; frozen.mkdir(parents=True)
    with tarfile.open(archive) as input_archive:
        expected = {row['path'] for row in rows}; names = set()
        for item in input_archive.getmembers():
            path = PurePosixPath(item.name)
            require(item.isfile() and not path.is_absolute() and '..' not in path.parts and item.name in expected and item.name not in names,
                    'unsafe_source_archive')
            names.add(item.name)
            destination = frozen / item.name; destination.parent.mkdir(parents=True, exist_ok=True)
            with input_archive.extractfile(item) as stream: destination.write_bytes(stream.read())
            destination.chmod(item.mode)
        require(names == expected, 'source_archive_incomplete')
    require(source_rows(frozen) == rows, 'source_archive_reconstruction_mismatch')
    write(run / 'public/archive-verification.json', {'status': 'byte_reconstruction_verified', 'files': len(rows),
          'source_archive_sha256': sha(archive), 'source_manifest_sha256': sha(run / 'public/source-manifest.json'), 'tree_sha256': tree_hash(rows)})
    return frozen, manifest


def run_command(argv, run, stage, env, cwd, timeout=1200):
    log = run / 'private' / (stage + '.log'); began = time.monotonic()
    with log.open('wb') as output:
        child = subprocess.Popen([str(arg) for arg in argv], cwd=cwd, env=env, stdout=output, stderr=subprocess.STDOUT, start_new_session=True)
        try: code = child.wait(timeout)
        except subprocess.TimeoutExpired:
            try: os.killpg(child.pid, signal.SIGKILL)
            except ProcessLookupError: pass
            child.wait(); code = 124
    write(run / 'public' / (stage + '-command.json'), {'argv': [str(arg) for arg in argv], 'exit_code': code,
          'elapsed_seconds': time.monotonic() - began, 'stage': stage})
    require(code == 0, stage + '_failed', code if 0 < code < 256 else 1)
    return log


def candidate_version():
    version = os.environ.get('R3_CANDIDATE_VERSION') or '0.1.0-r3.' + datetime.datetime.now(datetime.timezone.utc).strftime('%Y%m%d%H%M%S')
    require(re.fullmatch(r'0\.1\.0-r3\.\d{14}', version), 'candidate_version_must_be_unique_UTC_timestamp', 64)
    datetime.datetime.strptime(version.rsplit('.', 1)[1], '%Y%m%d%H%M%S')
    reservations = ROOT / '.local/verification/r3/t19/versions'; reservations.mkdir(parents=True, exist_ok=True)
    try: (reservations / version).mkdir()
    except FileExistsError: raise Reject('candidate_version_already_reserved_no_overwrite')
    return version


def environment(run, version):
    env = os.environ.copy()
    for key in list(env):
        if key.startswith('DAMENG'): env.pop(key)
    for name in ('cli-home', 'cache'): (run / 'private' / name).mkdir()
    env.update(DOTNET_CLI_HOME=str(run / 'private/cli-home'), DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1', DOTNET_CLI_TELEMETRY_OPTOUT='1',
               NUGET_PACKAGES=str(run / 'private/cache'), R3_CANDIDATE_VERSION=version, R3_RESULTS_DIR=str(run / 'private/audit'))
    return env


def inspect_package(package, version, run, frozen):
    require(package.name == f'W.DmProvider.{version}.nupkg', 'candidate_package_name_mismatch')
    with zipfile.ZipFile(package) as archive:
        names = archive.namelist(); require(len(names) == len(set(names)), 'duplicate_package_entry')
        require(all(not PurePosixPath(name).is_absolute() and '..' not in PurePosixPath(name).parts for name in names), 'unsafe_package_entry')
        nuspecs = [name for name in names if name.endswith('.nuspec')]; require(len(nuspecs) == 1, 'nuspec_count')
        root = ET.fromstring(archive.read(nuspecs[0])); namespace = root.tag.partition('}')[0] + '}' if '}' in root.tag else ''
        metadata = root.find(namespace + 'metadata')
        require(metadata.findtext(namespace + 'id') == 'W.DmProvider' and metadata.findtext(namespace + 'version') == version, 'nuspec_identity')
        require(metadata.find(namespace + 'license') is not None and metadata.find(namespace + 'license').text == 'Apache-2.0', 'package_license_expression')
        expected = {'lib/net10.0/W.DmProvider.dll'} | {f'lib/net10.0/{culture}/W.DmProvider.resources.dll' for culture in ('en', 'zh-CN', 'zh-HK', 'zh-TW')}
        binaries = {name for name in names if name.lower().endswith(('.dll', '.so', '.dylib', '.pdb', '.a'))}
        require(binaries == expected, 'package_binary_assets_mismatch')
        require(not any('sourcelink' in name.lower() for name in names), 'unexpected_sourcelink_claim')
        for name in ('LICENSE', 'THIRD-PARTY-NOTICES.md'):
            require(name in names and archive.read(name) == (frozen / name).read_bytes(), 'packaged_legal_material_mismatch')
        dependencies = []
        for dependency in metadata.findall('.//' + namespace + 'dependency'):
            require(dependency.attrib.get('id', '').lower() != 'dm.dmprovider', 'official_provider_runtime_dependency_rejected')
            dependencies.append({'id': dependency.attrib['id'], 'version_range': dependency.attrib.get('version')})
        assets = [{'path': name, 'sha256': sha_bytes(archive.read(name)), 'size': len(archive.read(name))} for name in sorted(names)]
        write(run / 'public/package-assets.json', {'version': version, 'package_sha256': sha(package), 'assets': assets,
              'runtime_nuspec_dependencies': dependencies, 'source_link': False})
        return dependencies, {row['path']: row['sha256'] for row in assets}


def dependency_audit(run, frozen, version, runtime_nuspec):
    runtime_assets = json.loads((frozen / 'src/W.DmProvider/obj/project.assets.json').read_text())
    product_libraries = [{'identity': key, 'type': value.get('type'), 'sha512': value.get('sha512')}
                         for key, value in sorted(runtime_assets['libraries'].items())]
    validation = []
    for project in sorted((frozen / 'tests').glob('W.DmProvider.*Tests/obj/project.assets.json')):
        value = json.loads(project.read_text())
        validation.append({'project': project.parent.parent.relative_to(frozen).as_posix(), 'libraries':
            [{'identity': key, 'type': item.get('type'), 'sha512': item.get('sha512')} for key, item in sorted(value['libraries'].items())]})
    consumer = json.loads((run / 'private/identity-obj/project.assets.json').read_text())
    tool_libraries = [{'identity': key, 'type': value.get('type'), 'sha512': value.get('sha512')}
                      for key, value in sorted(consumer['libraries'].items())]
    write(run / 'public/dependency-scopes.json', {'runtime': {'nuget_libraries': product_libraries, 'nuspec': runtime_nuspec,
          'framework': 'Microsoft.NETCore.App/net10.0'}, 'test_only': validation, 'candidate_identity_tool_only': tool_libraries,
          'source_ancestor_is_not_runtime_dependency': True})
    provenance = {'source_map_path': 'docs/compatibility/t03-source-map.json', 'source_map_sha256': sha(frozen / 'docs/compatibility/t03-source-map.json'),
                  'LICENSE_sha256': sha(frozen / 'LICENSE'), 'THIRD_PARTY_NOTICES_sha256': sha(frozen / 'THIRD-PARTY-NOTICES.md'),
                  'ancestor': 'DM.DmProvider/8.3.1.47463', 'source_ancestor_not_runtime_dependency': True}
    write(run / 'public/source-provenance.json', provenance)
    root_ref = f'pkg:nuget/W.DmProvider@{version}'
    components = {}; runtime_refs = []
    for row in product_libraries:
        if row['type'] != 'package': continue
        name, dependency_version = row['identity'].rsplit('/', 1); reference = f'pkg:nuget/{name}@{dependency_version}'
        runtime_refs.append(reference); components[reference] = {'type': 'library', 'name': name, 'version': dependency_version,
             'bom-ref': reference, 'purl': reference, 'scope': 'required', 'properties': [{'name': 'wdm.role', 'value': 'runtime'}]}
    for lane in validation:
        for row in lane['libraries']:
            if row['type'] != 'package': continue
            name, dependency_version = row['identity'].rsplit('/', 1); reference = f'pkg:nuget/{name}@{dependency_version}'
            if reference not in components: components[reference] = {'type': 'library', 'name': name, 'version': dependency_version,
                'bom-ref': reference, 'purl': reference, 'scope': 'excluded', 'properties': [{'name': 'wdm.role', 'value': 'validation_only'}]}
    write(run / 'public/cyclonedx.json', {'bomFormat': 'CycloneDX', 'specVersion': '1.6', 'version': 1,
        'serialNumber': 'urn:uuid:' + str(uuid.uuid4()), 'metadata': {'component': {'type': 'library', 'name': 'W.DmProvider', 'version': version,
        'bom-ref': root_ref, 'purl': root_ref, 'licenses': [{'license': {'id': 'Apache-2.0'}}],
        'properties': [{'name': 'wdm.source_provenance_sha256', 'value': sha(run / 'public/source-provenance.json')}]}},
        'components': list(components.values()), 'dependencies': [{'ref': root_ref, 'dependsOn': sorted(runtime_refs)}]})


def scan_public(run):
    rows = []
    for path in sorted((run / 'public').rglob('*')):
        if not path.is_file(): continue
        if path.name == 'candidate-source.tar.gz':
            rows.append({'path': path.name, 'sha256': sha(path), 'scan_scope': 'source_path_and_private_key_scan; synthetic_source_controls_retained'})
            continue
        raw = path.read_bytes()
        require(not any(marker.encode() in raw for marker in MARKERS), 'public_evidence_synthetic_secret_leak')
        require(not re.search(rb'(?m)^-----BEGIN (?:RSA |EC |ENCRYPTED )?PRIVATE KEY-----', raw), 'public_evidence_private_key_leak')
        if path.name != 'artifact-hashes.json': rows.append({'path': path.relative_to(run / 'public').as_posix(), 'sha256': sha(path), 'scan_scope': 'safe_evidence'})
    write(run / 'public/artifact-hashes.json', rows)


def offline():
    version = candidate_version()
    base = Path(os.environ.get('R19_RESULTS_DIR', ROOT / '.local/verification/r3/t19/runs')).resolve()
    run = base / version; require(not run.exists(), 'candidate_run_already_exists_no_overwrite')
    (run / 'private').mkdir(parents=True, mode=0o700); (run / 'public').mkdir(mode=0o700)
    try:
        frozen, manifest = make_snapshot(run); env = environment(run, version)
        dotnet = shutil.which(os.environ.get('DOTNET_COMMAND', 'dotnet')); require(dotnet, 'dotnet_missing')
        require(json.loads((frozen / 'global.json').read_text()) == {'sdk': {'version': '10.0.203', 'rollForward': 'disable'}}, 'SDK_pin_changed')
        run_command([sys.executable, frozen / 'tools/R3ReleaseAudit/audit.py', 'offline'], run, 'full_twelve_suites_no_build_pack_seven_children', env, frozen, 3600)
        summaries = list((run / 'private/audit').glob('*/public/summary.json')); require(len(summaries) == 1, 'offline_audit_summary_count')
        audit = json.loads(summaries[0].read_text())
        require(audit['status'] == 'offline_verified' and audit['candidate_version'] == version and len(audit['suites']) == 12, 'candidate_audit_not_verified')
        audit_run = summaries[0].parent.parent
        for source in summaries[0].parent.iterdir():
            if source.is_file(): shutil.copy2(source, run / 'public' / ('offline-' + source.name))
        feed = run / 'private/feed'; feed.mkdir()
        package = feed / f'W.DmProvider.{version}.nupkg'
        with package.open('xb') as output: output.write((audit_run / '.private/feed' / package.name).read_bytes())
        runtime_nuspec, assets = inspect_package(package, version, run, frozen)
        app = run / 'private/identity-app'; obj = run / 'private/identity-obj'; app.mkdir(); obj.mkdir()
        run_command([dotnet, 'build', frozen / 'tools/R3CandidateGate/IdentityConsumer.csproj', '-c', 'Release', '--output', app,
            '/p:R3PackageVersion=' + version, '/p:R3PackageSource=' + str(feed), '/p:RestoreSources=' + str(feed),
            '/p:BaseIntermediateOutputPath=' + str(obj) + '/', '/p:MSBuildProjectExtensionsPath=' + str(obj) + '/', *FLAGS],
            run, 'candidate_identity_consumer_build', env, frozen)
        run_command([dotnet, app / 'IdentityConsumer.dll', version, run / 'public/identity-api-runtime.json'], run, 'identity_api_runtime', env, frozen)
        identity = json.loads((run / 'public/identity-api-runtime.json').read_text())
        require(identity['driver_dll_sha256'] == assets['lib/net10.0/W.DmProvider.dll'], 'consumer_driver_asset_mismatch')
        deps = json.loads((app / 'IdentityConsumer.deps.json').read_text())['libraries']
        require(deps.get('W.DmProvider/' + version, {}).get('type') == 'package', 'identity_consumer_not_exact_package')
        dependency_audit(run, frozen, version, runtime_nuspec)
        require(source_rows(frozen) == manifest['files'] and source_rows(ROOT) == manifest['files'], 'candidate_source_changed_during_validation')
        candidate = {'version': version, 'package_sha256': sha(package), 'driver_dll_sha256': identity['driver_dll_sha256'],
            'driver_mvid': identity['driver_mvid'], 'source_manifest_sha256': sha(run / 'public/source-manifest.json')}
        write(run / 'public/candidate.json', candidate)
        write(run / 'public/final-matrix.json', {'schema_version': 1, 'task': 'T19', 'candidate': candidate,
            'gates': {name: 'missing' for name in REQUIRED_MATRIX}, 'shared_resource': 'not_verified_not_required',
            'upstream_pending': ['shared_stream_and_original_R2', 'shared_exact_cleanup', 'final_EF', 'final_matrix_root_readback'],
            'production_release_accepted': False, 'status': 'matrix_pending'})
        scan_public(run)
        summary = {'status': 'offline_candidate_verified', 'candidate': candidate, 'run_dir': str(run),
            'source_link': False, 'sdk': '10.0.203', 'runtime': identity['runtime'], 'os': identity['os'], 'rid': identity['rid'],
            'total_passed': audit['total_passed'], 'integration': 'final_matrix_pending', 'production_release_accepted': False}
        write(run / 'public/summary.json', summary); scan_public(run)
        package.chmod(0o400); emit(summary)
    except Exception as error:
        write(run / 'public/summary.json', {'status': 'rejected', 'reason': error.reason if isinstance(error, Reject) else 'candidate_gate_failed',
              'candidate_version': version, 'production_release_accepted': False})
        raise


def verify_existing(run):
    candidate = json.loads((run / 'public/candidate.json').read_text()); version = candidate['version']
    package = run / 'private/feed' / f'W.DmProvider.{version}.nupkg'
    require(sha(package) == candidate['package_sha256'] and sha(run / 'public/source-manifest.json') == candidate['source_manifest_sha256'], 'immutable_candidate_identity_changed')
    manifest = json.loads((run / 'public/source-manifest.json').read_text())
    require(source_rows(ROOT) == manifest['files'], 'current_tool_source_differs_from_candidate')
    return candidate, package.parent


def real_matrix_lane(run, profile):
    require(profile in ('tls', 'shared'), 'matrix_profile_invalid', 64)
    # Presence only; credentials are loaded by the exact existing TEST wrappers,
    # never copied into the frozen source or CI.
    secret = ROOT / '.local/secrets' / ('dameng-tls-test.env' if profile == 'tls' else 'dameng-test.env')
    require(secret.is_file(), 'required_database_environment_missing', 66)
    candidate, feed = verify_existing(run)
    evidence = run / 'matrix-runs' / (profile + '-' + datetime.datetime.now(datetime.timezone.utc).strftime('%Y%m%dT%H%M%SZ') + '-' + uuid.uuid4().hex[:8])
    (evidence / 'private').mkdir(parents=True); (evidence / 'public').mkdir()
    env = os.environ.copy(); env['R3_CANDIDATE_VERSION'] = candidate['version']
    for gate, budget in (('t16', 1200), ('t17', 2400), ('t13', 1200), ('t14', 1800)):
        # Outer budgets include consumer build time; each original runtime and
        # operation deadline is preserved inside its immutable existing runner.
        run_command([ROOT / 'eng' / (gate + '.sh'), profile, candidate['version'], feed], evidence, gate + '_' + profile, env, ROOT, budget)
    run_command([ROOT / 'eng/t13.sh', 'security-' + profile, candidate['version'], feed], evidence,
                't13_security_' + profile, env, ROOT, 1200)
    if profile == 'tls':
        run_command([ROOT / 'eng/t18.sh', 'tls', candidate['version'], feed], evidence, 't18_tls_resource', env, ROOT, 1500)
    verify_existing(run)
    emit({'status': 'profile_commands_completed_requires_validator_readback', 'profile': profile, 'candidate': candidate,
          'run_dir': str(evidence), 'production_release_accepted': False})


def external_preflight(run, index_path):
    candidate, _ = verify_existing(run)
    index = json.loads(index_path.read_text()); require(index.get('schema_version') == 1 and isinstance(index.get('reports'), dict), 'external_evidence_index_invalid')
    rows = {}
    for gate, path in index['reports'].items():
        require(gate in REQUIRED_MATRIX, 'unknown_external_gate')
        report_path = Path(path).resolve(); require(report_path.is_file() and report_path.stat().st_size < 32 * 1024 * 1024, 'external_report_missing_or_unbounded')
        report = json.loads(report_path.read_text())
        identity = report.get('candidate', {})
        require(all(identity.get(key) == value for key, value in candidate.items()), 'external_candidate_identity_mismatch')
        require(report.get('schema_version') == 1 and report.get('production_release_accepted') is False, 'external_report_envelope_invalid')
        if gate == 'ef_final':
            require(report.get('task') == 'T19' and report.get('sdk') == '10.0.401' and report.get('efcore_version') == '10.0.12' and
                    report.get('ef_commit') == EF_COMMIT and report.get('ef_archive_sha256', report.get('archive_sha256')) == EF_ARCHIVE_SHA256,
                    'EF_source_toolchain_envelope_invalid')
            lanes = report.get('lanes'); require(isinstance(lanes, list) and lanes, 'EF_lanes_missing')
            for lane in lanes:
                require(all(key in lane for key in ('lane', 'profile', 'exit_code', 'strict_trx_counts', 'case_ids', 'before_audit', 'after_audit',
                        'loaded_dll_identity', 'testhost_proofs', 'nested_cli_proofs', 'binary_hashes_before', 'binary_hashes_after', 'locks', 'status')),
                        'EF_lane_proof_incomplete')
        else:
            require(report.get('task') in ('T13', 'T14', 'T16', 'T17', 'T18', 'T19') and
                    isinstance(report.get('validator'), dict) and all(key in report['validator'] for key in ('path', 'sha256', 'exit_code')),
                    'known_validator_proof_required')
            validator = report['validator']; proof = Path(validator['path']).resolve()
            require(proof.is_file() and sha(proof) == validator['sha256'] and validator['exit_code'] == 0, 'validator_readback_mismatch')
        rows[gate] = {'status': 'envelope_preflight_only_root_readback_required', 'path': str(report_path), 'sha256': sha(report_path)}
    pending = [gate for gate in REQUIRED_MATRIX if gate not in rows]
    write(run / 'public/external-evidence-preflight.json', {'schema_version': 1, 'task': 'T19', 'candidate': candidate,
        'reports': rows, 'missing_gates': pending, 'production_release_accepted': False, 'status': 'matrix_pending',
        'note': 'Presence, identities and envelopes do not replace strict validator contents or root readback.'})
    emit({'status': 'matrix_pending', 'missing_gates': pending, 'production_release_accepted': False})


def main():
    require(len(sys.argv) >= 2, 'usage', 64)
    mode = sys.argv[1]
    if mode == 'offline': require(len(sys.argv) == 2, 'usage', 64); offline()
    elif mode == 'release-environment':
        require(all(os.environ.get(key, '').strip() for key in ('DAMENG_TEST_CONNECTION_STRING', 'DAMENG_TLS_TEST_CONNECTION_STRING')),
                'required_database_environment_missing', 66)
        emit({'status': 'presence_preflight_only', 'production_release_accepted': False})
    elif mode == 'matrix':
        require(len(sys.argv) == 4, 'usage', 64); real_matrix_lane(Path(sys.argv[2]).resolve(), sys.argv[3])
    elif mode == 'external-preflight':
        require(len(sys.argv) == 4, 'usage', 64); external_preflight(Path(sys.argv[2]).resolve(), Path(sys.argv[3]).resolve())
    else: raise Reject('usage', 64)


if __name__ == '__main__':
    try: main()
    except Exception as error:
        emit({'status': 'rejected', 'reason': error.reason if isinstance(error, Reject) else 'candidate_gate_failed', 'production_release_accepted': False})
        sys.exit(error.code if isinstance(error, Reject) else 1)
