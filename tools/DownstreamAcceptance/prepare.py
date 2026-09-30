#!/usr/bin/env python3
"""Export pinned EF sources and add the bounded T11 consumer. Never runs dotnet."""
import argparse
import difflib
import hashlib
import io
import json
from pathlib import Path
import re
import subprocess
import tarfile
import zipfile
import xml.etree.ElementTree as ET

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent.parent
EF_REPO = Path('/Users/wanghongyi/Projects/dameng-entityframework-core')
EF_COMMIT = '113014cc74dd1f751ef97226a78d2ec855b32c8c'


def digest(data):
    return hashlib.sha256(data).hexdigest()


def bounded(path):
    path = Path(path).resolve()
    if not path.is_relative_to((ROOT / '.local/t11/ef').resolve()):
        raise ValueError('output_outside_t11_ef')
    return path


def hashes(path):
    return {str(p.relative_to(path)): digest(p.read_bytes()) for p in sorted(path.rglob('*'))
            if p.is_file() and not any(x in ('bin', 'obj', 'artifacts') for x in p.relative_to(path).parts)}


def snapshot(output):
    output = bounded(output)
    if output.exists():
        raise ValueError('output_exists_use_new_run_directory')
    archive = subprocess.check_output(['git', '-C', str(EF_REPO), 'archive', '--format=tar', EF_COMMIT])
    tree = subprocess.check_output(['git', '-C', str(EF_REPO), 'rev-parse', EF_COMMIT + '^{tree}']).decode().strip()
    with tarfile.open(fileobj=io.BytesIO(archive)) as tar:
        members = tar.getmembers()
        for item in members:
            name = Path(item.name)
            if name.is_absolute() or '..' in name.parts or item.issym() or item.islnk():
                raise ValueError('unsafe_archive_member')
            if name.name == '.local-test.secrets.json' or '.local' in name.parts:
                raise ValueError('forbidden_archive_secret_path')
        output.mkdir(parents=True)
        (output / 'ef-clean.tar').write_bytes(archive)
        for lane in ('O', 'W'):
            folder = output / lane
            folder.mkdir()
            for item in members:
                target = folder / item.name
                if item.isdir():
                    target.mkdir(parents=True, exist_ok=True)
                elif item.isfile():
                    target.parent.mkdir(parents=True, exist_ok=True)
                    target.write_bytes(tar.extractfile(item).read())
                    target.chmod(item.mode)
                else:
                    raise ValueError('unexpected_archive_member_type')
            consumer = folder / 'T11Consumer'
            consumer.mkdir()
            (consumer / 'T11Consumer.csproj').write_text((HERE / 'T11Consumer.csproj.template').read_text().replace('@LANE@', lane))
            (consumer / 'Program.cs').write_bytes((HERE / 'Program.cs').read_bytes())
            # Strict EF pin also applies to transitive dependencies. No driver source reference.
            props = folder / 'Directory.Packages.props'
            props.write_text(props.read_text().replace('[10.0.12,11.0.0)', '[10.0.12]'))
            (folder / 'NuGet.Config').write_text('<configuration><packageSources><clear/><add key="nuget.org" value="https://api.nuget.org/v3/index.json"/></packageSources></configuration>\n')
    manifest = {'schema': 1, 'task': 'T11', 'ef_commit': EF_COMMIT, 'ef_tree': tree,
                'ef_archive_sha256': digest(archive), 'archive_copies': {'O': digest(archive), 'W': digest(archive)},
                'official_package': {'id': 'DM.DmProvider', 'version': '8.3.1.47463'},
                'ef_version': '10.0.12', 'candidate': None,
                'source_hashes': {lane: hashes(output / lane) for lane in ('O', 'W')},
                'tool_hashes': {p.name: digest(p.read_bytes()) for p in HERE.iterdir() if p.is_file()},
                'safety': {'secrets_copied': False, 'original_ef_modified': False, 'admin_lane_allowed': False}}
    (output / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n')
    print('snapshot_ready ' + str(output))


def bind(output, package, package_hash, version, dll_hash):
    output = bounded(output)
    if not re.fullmatch(r'0\.1\.0-t11\.[0-9A-Za-z.-]+', version):
        raise ValueError('candidate_requires_unique_t11_version')
    if not all(re.fullmatch('[0-9a-fA-F]{64}', x) for x in (package_hash, dll_hash)):
        raise ValueError('candidate_requires_sha256')
    manifest = json.loads((output / 'manifest.json').read_text())
    if manifest['candidate'] is not None:
        raise ValueError('candidate_already_bound_use_new_snapshot')
    package = Path(package).resolve()
    data = package.read_bytes()
    if digest(data) != package_hash.lower():
        raise ValueError('candidate_package_hash_mismatch')
    with zipfile.ZipFile(io.BytesIO(data)) as z:
        nuspecs = [n for n in z.namelist() if n.endswith('.nuspec')]
        if len(nuspecs) != 1:
            raise ValueError('candidate_nuspec_count')
        spec = ET.fromstring(z.read(nuspecs[0]))
        values = {node.tag.split('}')[-1]: node.text for node in spec.iter()
                  if node.tag.split('}')[-1] in ('id', 'version')}
        if values != {'id': 'W.DmProvider', 'version': version}:
            raise ValueError('candidate_nuspec_identity_mismatch')
        candidate_dll = z.read('lib/net10.0/W.DmProvider.dll')
        if digest(candidate_dll) != dll_hash.lower():
            raise ValueError('candidate_dll_hash_mismatch')
    folder = output / 'W'
    if hashes(folder) != manifest['source_hashes']['W']:
        raise ValueError('unbound_source_drift')
    changes = []
    for p in sorted((folder / 'src').rglob('*')):
        if not p.is_file() or p.suffix not in ('.cs', '.csproj'):
            continue
        old = p.read_text()
        new = old.replace('using Dm;', 'using W.Dm;').replace('cref="Dm.', 'cref="W.Dm.')
        new = new.replace('PackageReference Include="DM.DmProvider"', 'PackageReference Include="W.DmProvider"')
        if old != new:
            p.write_text(new)
            changes.extend(difflib.unified_diff(old.splitlines(True), new.splitlines(True),
                           fromfile='a/' + str(p.relative_to(folder)), tofile='b/' + str(p.relative_to(folder))))
    props = folder / 'Directory.Packages.props'
    old = props.read_text()
    new = old.replace('<PackageVersion Include="DM.DmProvider" Version="[8.3.1.47463,8.4.0)" />',
                      '<PackageVersion Include="W.DmProvider" Version="[' + version + ']" />')
    if old == new:
        raise ValueError('central_driver_reference_not_found')
    props.write_text(new)
    changes.extend(difflib.unified_diff(old.splitlines(True), new.splitlines(True),
                   fromfile='a/Directory.Packages.props', tofile='b/Directory.Packages.props'))
    feed = output / 'candidate-feed'
    feed.mkdir()
    target = feed / ('W.DmProvider.' + version + '.nupkg')
    target.write_bytes(data)
    (folder / 'NuGet.Config').write_text('<configuration><packageSources><clear/><add key="candidate" value="' +
        str(feed) + '"/><add key="nuget.org" value="https://api.nuget.org/v3/index.json"/></packageSources></configuration>\n')
    (output / 'W-driver.patch').write_text(''.join(changes))
    manifest['candidate'] = {'id': 'W.DmProvider', 'version': version, 'package_sha256': package_hash.lower(),
                             'driver_dll_sha256': dll_hash.lower(), 'package_path': str(target),
                             'tfm': 'net10.0', 'patch_sha256': digest((output / 'W-driver.patch').read_bytes())}
    manifest['source_hashes']['W'] = hashes(folder)
    (output / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n')
    print('candidate_bound ' + version)


def main():
    parser = argparse.ArgumentParser()
    sub = parser.add_subparsers(dest='action', required=True)
    p = sub.add_parser('snapshot')
    p.add_argument('--output', required=True)
    p = sub.add_parser('bind')
    p.add_argument('--output', required=True)
    p.add_argument('--candidate', required=True)
    p.add_argument('--candidate-sha256', required=True)
    p.add_argument('--version', required=True)
    p.add_argument('--driver-dll-sha256', required=True)
    args = parser.parse_args()
    if args.action == 'snapshot':
        snapshot(args.output)
    else:
        bind(args.output, args.candidate, args.candidate_sha256, args.version, args.driver_dll_sha256)


if __name__ == '__main__':
    try:
        main()
    except Exception as error:
        # Never surface subprocess stderr or arbitrary source/package text.
        print('preparation_failed ' + type(error).__name__)
        raise SystemExit(1)
