#!/usr/bin/env python3
"""Low-owned build/TEST execution. No EF launcher, admin lane, or nested CLI."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
from datetime import datetime, timezone

from prepare import bounded, digest, hashes, ROOT

DOTNET = Path('/Users/wanghongyi/.dotnet/dotnet')
FLAGS = ['-m:1', '/nodeReuse:false', '/p:UseSharedCompilation=false', '--disable-build-servers']


def checked_sources(run, manifest, lane):
    actual = hashes(run / lane)
    expected = manifest['source_hashes'][lane]
    # Only generated restore locks may evolve in this isolated lane. Every other input is frozen.
    differences = {k for k in actual.keys() | expected.keys() if actual.get(k) != expected.get(k)}
    if any(not key.endswith('packages.lock.json') for key in differences):
        raise ValueError('frozen_source_drift')


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('action', choices=('build', 'run'))
    parser.add_argument('--output', required=True)
    parser.add_argument('--lane', choices=('O', 'W'), required=True)
    parser.add_argument('--entry', choices=('public', 'profile'), default='public')
    args = parser.parse_args()
    run = bounded(args.output)
    manifest = json.loads((run / 'manifest.json').read_text())
    if manifest['ef_commit'] != '113014cc74dd1f751ef97226a78d2ec855b32c8c':
        raise ValueError('unexpected_ef_pin')
    if args.lane == 'W' and manifest['candidate'] is None:
        raise ValueError('candidate_unbound')
    if args.lane == 'O' and args.entry != 'public':
        raise ValueError('official_profile_forbidden')
    checked_sources(run, manifest, args.lane)
    lane = run / args.lane
    candidate = manifest['candidate']
    if args.lane == 'W':
        package = Path(candidate['package_path'])
        if digest(package.read_bytes()) != candidate['package_sha256']:
            raise ValueError('bound_candidate_package_drift')
    state_path = run / ('build-' + args.lane + '.json')
    env = os.environ.copy()
    # Build never receives credentials; run exclusively invokes W's TEST wrapper.
    env.pop('DAMENG_TEST_CONNECTION_STRING', None)
    env['DOTNET_CLI_HOME'] = str(run / '.cli' / args.lane)
    env['DOTNET_SKIP_FIRST_TIME_EXPERIENCE'] = '1'
    env['DOTNET_CLI_TELEMETRY_OPTOUT'] = '1'
    env['NUGET_PACKAGES'] = str(run / '.packages' / args.lane)
    env['NUGET_HTTP_CACHE_PATH'] = str(run / '.http-cache' / args.lane)
    env['DOTNET_HOST_PATH'] = str(DOTNET)
    env['DOTNET_ROOT'] = str(DOTNET.parent)
    Path(env['DOTNET_CLI_HOME']).mkdir(parents=True, exist_ok=True)
    if args.action == 'build':
        if state_path.exists():
            raise ValueError('build_record_exists_use_new_snapshot')
        evidence = {'utc': datetime.now(timezone.utc).isoformat(), 'lane': args.lane,
                    'dotnet_host': str(DOTNET), 'candidate': candidate if args.lane == 'W' else None,
                    'commands': [], 'accepted': False}
        code = 0
        version = subprocess.run([str(DOTNET), '--version'], cwd=lane, env=env, capture_output=True, text=True)
        evidence['sdk'] = version.stdout.strip()
        if version.returncode != 0 or evidence['sdk'] != '10.0.401':
            raise ValueError('sdk_pin_not_satisfied')
        project = 'T11Consumer/T11Consumer.csproj'
        # First bounded dependency substitution may rewrite locks. The second restore must be locked.
        commands = [
            [str(DOTNET), 'restore', project, '--force-evaluate', '--disable-parallel',
             '--configfile', str(lane / 'NuGet.Config'), *FLAGS],
            [str(DOTNET), 'restore', project, '--locked-mode', '--disable-parallel',
             '--configfile', str(lane / 'NuGet.Config'), *FLAGS],
            [str(DOTNET), 'build', project, '--no-restore', *FLAGS]
        ]
        for index, command in enumerate(commands):
            log = run / ('build-' + args.lane + '-' + str(index) + '.log')
            with log.open('wb') as stream:
                result = subprocess.run(command, cwd=lane, env=env, stdout=stream, stderr=subprocess.STDOUT)
            evidence['commands'].append({'argv': command, 'exit_code': result.returncode,
                                         'log': str(log), 'log_sha256': digest(log.read_bytes())})
            print(args.lane + ' build_step=' + str(index) + ' exit=' + str(result.returncode))
            if result.returncode:
                code = result.returncode
                break
        if not code:
            locks = {str(p.relative_to(lane)): digest(p.read_bytes()) for p in lane.rglob('packages.lock.json')}
            evidence['locks_sha256'] = locks
            product_lock = json.loads((lane / 'src/W.EntityFrameworkCore.Dameng/packages.lock.json').read_text())
            deps = product_lock['dependencies']['net10.0']
            driver_id = 'W.DmProvider' if args.lane == 'W' else 'DM.DmProvider'
            driver_version = candidate['version'] if args.lane == 'W' else '8.3.1.47463'
            if deps[driver_id]['resolved'] != driver_version or deps['Microsoft.EntityFrameworkCore.Relational']['resolved'] != '10.0.12':
                raise ValueError('resolved_dependency_mismatch')
            dll_name = 'W.DmProvider.dll' if args.lane == 'W' else 'DM.DmProvider.dll'
            driver = lane / 'T11Consumer/bin/Debug/net10.0' / dll_name
            evidence['driver_dll_sha256'] = digest(driver.read_bytes())
            if args.lane == 'W' and evidence['driver_dll_sha256'] != candidate['driver_dll_sha256']:
                raise ValueError('consumer_driver_hash_mismatch')
            other_id = 'DM.DmProvider' if args.lane == 'W' else 'W.DmProvider'
            if other_id in deps:
                raise ValueError('mixed_driver_dependencies')
            evidence['consumer_dll_sha256'] = digest((lane / 'T11Consumer/bin/Debug/net10.0' / ('T11Consumer.' + args.lane + '.dll')).read_bytes())
            evidence['source_hashes_after_restore'] = hashes(lane)
            checked_sources(run, manifest, args.lane)
            evidence['accepted'] = True
        state_path.write_text(json.dumps(evidence, indent=2) + '\n')
        return code
    state = json.loads(state_path.read_text())
    if not state['accepted'] or hashes(lane) != state['source_hashes_after_restore']:
        raise ValueError('consumer_build_unaccepted_or_source_drift')
    dll = lane / 'T11Consumer/bin/Debug/net10.0' / ('T11Consumer.' + args.lane + '.dll')
    if digest(dll.read_bytes()) != state['consumer_dll_sha256']:
        raise ValueError('consumer_binary_drift')
    stamp = datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%S%fZ')
    evidence_file = run / ('ef-' + args.lane + '-' + args.entry + '-' + stamp + '.json')
    command = [str(ROOT / 'scripts/with-dameng-test.sh'), str(DOTNET), str(dll), str(evidence_file),
               args.entry, state['driver_dll_sha256']]
    result = subprocess.run(command, cwd=lane, env=env)
    record = {'argv': command, 'exit_code': result.returncode, 'entry': args.entry,
              'candidate': candidate if args.lane == 'W' else None,
              'result_sha256': digest(evidence_file.read_bytes()) if evidence_file.exists() else None,
              'build_record_sha256': digest(state_path.read_bytes())}
    (run / ('run-' + args.lane + '-' + args.entry + '-' + stamp + '.json')).write_text(json.dumps(record, indent=2) + '\n')
    return result.returncode


if __name__ == '__main__':
    try:
        sys.exit(main())
    except Exception as error:
        print('downstream_runner_failed ' + type(error).__name__)
        sys.exit(1)
