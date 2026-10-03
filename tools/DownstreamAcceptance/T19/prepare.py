#!/usr/bin/env python3
"""Prepare fresh per-lane EF archives using read-only accepted patches and an exact final driver package."""
import argparse
import difflib
import io
import json
from pathlib import Path
import re
import subprocess
import sys
import tarfile
import tempfile
import zipfile
import xml.etree.ElementTree as ET
from common import *

SETTINGS = '''using W.Dm;

internal static class T19ConnectionSettings
{
    internal static string Profile => Environment.GetEnvironmentVariable("DAMENG_T19_PROFILE") is "shared" or "tls" ?
        Environment.GetEnvironmentVariable("DAMENG_T19_PROFILE")! : throw new InvalidOperationException("T19 profile is required.");
    internal static string? Configure(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var b = new DmConnectionStringBuilder(raw)
        {
            Schema = "WDM_PROVIDER_TEST", PersistSecurityInfo = false, ConnectTimeout = TimeSpan.FromSeconds(20),
            CommandTimeout = 20, Pooling = Profile == "shared", StmtPooling = false, PreparePooling = false,
            LogLevel = global::W.Dm.Config.LogLevel.OFF,
            TransportSecurity = Profile == "tls" ? DmTransportSecurity.RequireTls : DmTransportSecurity.PlaintextAllowed
        };
        if (!b.User.Equals("WDM_PROVIDER_TEST", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("T19 requires the dedicated TEST identity before authentication.");
        if (Profile == "tls")
        {
            if (b.Server != "127.0.0.1" || b.Port != 15236) throw new InvalidOperationException("T19 isolated TLS target required.");
            string repo = Environment.GetEnvironmentVariable("DAMENG_T19_REPO_ROOT") ?? throw new InvalidOperationException("T19 source root required.");
            string certs = Path.Combine(repo, ".local", "t07", "certs", "client_ssl", "WDM_PROVIDER_TEST");
            b.TlsCaCertificatePath = Path.Combine(certs, "ca-cert.pem"); b.TlsClientCertificatePath = Path.Combine(certs, "client-cert.pem");
            b.TlsClientPrivateKeyPath = Path.Combine(certs, "client-key.pem"); b.TlsRevocationMode = DmTlsRevocationMode.NoCheck;
        }
        return b.ConnectionString;
    }
}
'''


def replace_one(text, old, new, code):
    if text.count(old) != 1: raise ValueError('anchor_' + code)
    return text.replace(old, new)


def extract(raw, folder):
    with tarfile.open(fileobj=io.BytesIO(raw)) as archive:
        for member in archive.getmembers():
            name = Path(member.name)
            if name.is_absolute() or '..' in name.parts or member.issym() or member.islnk() or '.local' in name.parts or name.name == '.local-test.secrets.json':
                raise ValueError('unsafe_archive_member')
            target = folder / name
            if member.isdir(): target.mkdir(parents=True, exist_ok=True)
            elif member.isfile():
                target.parent.mkdir(parents=True, exist_ok=True); target.write_bytes(archive.extractfile(member).read()); target.chmod(member.mode)


def adapt(folder, version):
    before = {str(path.relative_to(folder)): path.read_bytes() for path in folder.rglob('*') if path.is_file()}
    central = folder / 'Directory.Packages.props'; document = ET.parse(central)
    candidates = [node for node in document.getroot().iter('PackageVersion') if node.get('Include') == 'W.DmProvider']
    if len(candidates) != 1: raise ValueError('central_driver_missing')
    candidates[0].set('Version', '[' + version + ']')
    for node in document.getroot().iter('PackageVersion'):
        if node.get('Include', '').startswith('Microsoft.EntityFrameworkCore'): node.set('Version', '[10.0.12]')
    document.write(central, encoding='utf-8')
    # A new restore graph belongs to this lane, never to an accepted T12 directory.
    for path in folder.rglob('packages.lock.json'): path.unlink()
    shared = folder / 'test/Shared'; shared.mkdir(exist_ok=True)
    (shared / 'T19CandidateGuard.cs').write_bytes((HERE / 'candidate_guard.cs').read_bytes())
    (shared / 'T19ConnectionSettings.cs').write_text(SETTINGS)
    old_guard = shared / 'T12CandidateGuard.cs'
    if old_guard.exists(): old_guard.unlink()
    for name in ('Tests', 'FunctionalTests', 'Specification.Tests'):
        project = folder / f'test/W.EntityFrameworkCore.Dameng.{name}/W.EntityFrameworkCore.Dameng.{name}.csproj'
        text = project.read_text().replace('T12CandidateGuard.cs', 'T19CandidateGuard.cs')
        text = replace_one(text, '</Project>', '<ItemGroup><Compile Include="../Shared/T19ConnectionSettings.cs" Link="T19ConnectionSettings.cs" /></ItemGroup>\n</Project>', 'settings_link_' + name)
        project.write_text(text)
    (folder / 'test/W.EntityFrameworkCore.Dameng.FunctionalTests/R3DriverContractTests.cs').write_bytes((HERE / 'r3_contract_tests.cs').read_bytes())
    (folder / 'test/W.EntityFrameworkCore.Dameng.Tests/R3ExecutionStrategyTests.cs').write_bytes((HERE / 'r3_retry_unit_tests.cs').read_bytes())
    for name in ('FunctionalTests', 'Specification.Tests'):
        path = folder / f'test/W.EntityFrameworkCore.Dameng.{name}/DamengTestEnvironment.cs'
        text = path.read_text()
        start = text.index('    private static string? ConfigureTestConnection(string? raw)')
        opening = text.index('{', start); level = 1; cursor = opening + 1
        while level:
            if text[cursor] == '{': level += 1
            elif text[cursor] == '}': level -= 1
            cursor += 1
        path.write_text(text[:start] + '    private static string? ConfigureTestConnection(string? raw)\n        => global::T19ConnectionSettings.Configure(raw);' + text[cursor:])
    cli = folder / 'test/W.EntityFrameworkCore.Dameng.FunctionalTests/DamengDotNetEfCliFunctionalTests.cs'
    text = cli.read_text()
    anchor = '        File.WriteAllText(Path.Combine(projectDirectory, "global.json"),'
    text = replace_one(text, anchor,
        '        File.Copy(Path.Combine(FindRepositoryRoot(), "test", "Shared", "T19CandidateGuard.cs"), Path.Combine(projectDirectory, "T19CandidateGuard.cs"), true);\n' + anchor, 'cli_guard_copy')
    anchor = '        Process? process = null;'
    text = replace_one(text, anchor,
        '        startInfo.Environment["DAMENG_T19_DRIVER_ASSET_ROOT"] = RequiredAbsolutePath("DAMENG_T19_DRIVER_ASSET_ROOT", directory: true);\n' +
        '        startInfo.Environment["DAMENG_T19_DRIVER_LOAD_RECORD"] = Path.Combine(RequiredAbsolutePath("DAMENG_T19_DRIVER_LOAD_ROOT", directory: true), "cli-" + Guid.NewGuid().ToString("N") + ".json");\n' + anchor,
        'cli_unique_proof')
    cli.write_text(text)
    # Audit SQL/inventory/limited permission checks stay the same; configuration and exact candidate proof are new.
    audit = folder / 'T19Audit'; audit.mkdir()
    text = (HERE.parent / 't12_audit.cs').read_text().replace('"T12"', '"T19"')
    start = text.index('        var builder = new DmConnectionStringBuilder(raw)')
    end = text.index('        if (!string.Equals(builder.User', start)
    text = text[:start] + '        var builder = new DmConnectionStringBuilder(global::T19ConnectionSettings.Configure(raw)!);\n' + text[end:]
    (audit / 'Program.cs').write_text(text)
    (audit / 'T19Audit.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><EnableNETAnalyzers>false</EnableNETAnalyzers><NuGetAudit>false</NuGetAudit></PropertyGroup><ItemGroup><ProjectReference Include="../src/W.EntityFrameworkCore.Dameng/W.EntityFrameworkCore.Dameng.csproj" /><Compile Include="../test/Shared/T19CandidateGuard.cs" Link="T19CandidateGuard.cs" /><Compile Include="../test/Shared/T19ConnectionSettings.cs" Link="T19ConnectionSettings.cs" /></ItemGroup></Project>\n')
    for path in folder.rglob('*.csproj'):
        text = path.read_text()
        if 'DM.DmProvider' in text or re.search(r'<ProjectReference[^>]+W\.DmProvider', text): raise ValueError('official_or_driver_project_reference')
    return before


def overlay(before, folder):
    after = {str(path.relative_to(folder)): path.read_bytes() for path in folder.rglob('*') if path.is_file()}
    changes = []
    for relative in sorted(before.keys() | after.keys()):
        old, new = before.get(relative, b''), after.get(relative, b'')
        if old == new: continue
        for line in difflib.unified_diff(old.decode().splitlines(True), new.decode().splitlines(True),
                fromfile='a/' + relative if relative in before else '/dev/null',
                tofile='b/' + relative if relative in after else '/dev/null'):
            if line.endswith('\n'):
                changes.append(line)
            else:
                # Preserve the source bytes: terminate the diff record, then mark
                # the original data/context line as lacking its own terminal LF.
                changes.append(line + '\n')
                changes.append('\\ No newline at end of file\n')
    return ''.join(changes)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--output', required=True); parser.add_argument('--ef-archive', required=True)
    parser.add_argument('--candidate', required=True); parser.add_argument('--candidate-sha256', required=True)
    parser.add_argument('--version', required=True); parser.add_argument('--driver-dll-sha256', required=True); parser.add_argument('--driver-mvid', required=True)
    parser.add_argument('--source-manifest', required=True); parser.add_argument('--source-manifest-sha256', required=True)
    parser.add_argument('--profiles', nargs='+', choices=('shared', 'tls'), default=['shared', 'tls'])
    parser.add_argument('--execution-overlay'); parser.add_argument('--producer-source'); parser.add_argument('--execution-source'); parser.add_argument('--base-output')
    args = parser.parse_args(); output = bounded(args.output)
    if output.exists(): raise ValueError('output_exists')
    if not re.fullmatch(r'0\.1\.0-r3\.[0-9]{14}', args.version): raise ValueError('final_unique_version_required')
    package = Path(args.candidate).read_bytes(); source_manifest = Path(args.source_manifest).read_bytes()
    if sha(package) != args.candidate_sha256 or sha(source_manifest) != args.source_manifest_sha256: raise ValueError('candidate_source_hash_mismatch')
    with zipfile.ZipFile(io.BytesIO(package)) as archive:
        nuspec = ET.fromstring(archive.read(next(name for name in archive.namelist() if name.endswith('.nuspec'))))
        fields = {node.tag.split('}')[-1]: node.text for node in nuspec.iter() if node.tag.split('}')[-1] in ('id', 'version')}
        raw_dll = archive.read('lib/net10.0/W.DmProvider.dll')
        if fields != {'id': 'W.DmProvider', 'version': args.version} or sha(raw_dll) != args.driver_dll_sha256 or mvid(raw_dll) != args.driver_mvid:
            raise ValueError('exact_package_DLL_identity_mismatch')
    overlay_descriptor = None
    if any((args.execution_overlay, args.producer_source, args.execution_source, args.base_output)):
        if not all((args.execution_overlay, args.producer_source, args.execution_source, args.base_output)): raise ValueError('execution_overlay_inputs_required')
        from execution_overlay import validate_manifest
        verified = validate_manifest(manifest_path=Path(args.execution_overlay), producer_manifest_path=Path(args.source_manifest),
            producer_source_dir=Path(args.producer_source), execution_source_dir=Path(args.execution_source),
            expected_candidate_bindings={'version': args.version, 'package_sha256': args.candidate_sha256, 'driver_dll_sha256': args.driver_dll_sha256,
                'driver_mvid': args.driver_mvid, 'source_manifest_sha256': args.source_manifest_sha256})
        for name, digest in ((path.name, sha(path)) for path in HERE.iterdir() if path.is_file()):
            if sha(Path(verified['execution_tool_root']) / name) != digest: raise ValueError('execution_overlay_preparing_tool_mismatch')
        overlay_descriptor = {'manifest_sha256': sha(Path(args.execution_overlay)), 'producer_source_dir': verified['producer_source_dir'],
                              'execution_source_dir': verified['execution_source_dir'], 'verification': verified}
        base_output = bounded(args.base_output)
        base_manifest = json.loads((base_output / 'manifest.json').read_text())
        expected_candidate = {'version': args.version, 'package_sha256': args.candidate_sha256, 'driver_dll_sha256': args.driver_dll_sha256,
            'driver_mvid': args.driver_mvid, 'source_manifest_sha256': args.source_manifest_sha256}
        if base_manifest['candidate'] != expected_candidate or base_manifest.get('execution_overlay'): raise ValueError('base_producer_identity_mismatch')
        overlay_descriptor['base_evidence'] = {'output': str(base_output), 'manifest_sha256': sha(base_output / 'manifest.json'),
            'summary_sha256': sha(base_output / 'summary.json'), 'legacy_case_baseline_sha256': sha(base_output / 'legacy-case-baseline.json')}
    raw_archive = Path(args.ef_archive).read_bytes()
    if sha(raw_archive) != ARCHIVE_SHA: raise ValueError('EF_archive_hash_mismatch')
    for name, expected in PATCHES.items():
        if sha(PATCH_ROOT / name) != expected: raise ValueError('accepted_T12_patch_drift')
    output.mkdir(parents=True); (output / 'ef-clean.tar').write_bytes(raw_archive); (output / 'source-manifest.json').write_bytes(source_manifest)
    if overlay_descriptor:
        for name in ('execution-overlay.json', 'execution-source-manifest.json', 'execution-source.tar.gz'):
            (output / name).write_bytes((Path(args.execution_overlay).parent / name).read_bytes())
    baseline = (HERE / 'legacy-case-baseline.json').read_bytes(); (output / 'legacy-case-baseline.json').write_bytes(baseline)
    candidate = {'version': args.version, 'package_sha256': args.candidate_sha256, 'driver_dll_sha256': args.driver_dll_sha256,
                 'driver_mvid': args.driver_mvid, 'source_manifest_sha256': args.source_manifest_sha256}
    manifest = {'schema_version': 1, 'task': 'T19', 'candidate': candidate, 'ef_commit': EF_COMMIT, 'ef_archive_sha256': ARCHIVE_SHA,
                'efcore_version': EFCORE, 'sdk': SDK, 'accepted_T12_patches': PATCHES, 'lanes': {},
                'upstream_pending': ['T17_shared_large_value', 'shared_cleanup_recovery', 'R2_shared_crosspage', 'complete_R3_matrix'],
                'production_release_accepted': False, 'budget_policy': budget_policy()}
    manifest['legacy_case_baseline_sha256'] = sha(baseline)
    if overlay_descriptor: manifest['execution_overlay'] = overlay_descriptor
    for profile in dict.fromkeys(args.profiles):
        for lane in SHARED_LANES if profile == 'shared' else TLS_LANES:
            root = output / 'lanes' / (profile + '-' + lane); folder = root / 'W'; folder.mkdir(parents=True)
            extract(raw_archive, folder)
            for name in ('combined.patch',):
                subprocess.run(['patch', '-p1', '--dry-run', '-i', str(PATCH_ROOT / name)], cwd=folder, capture_output=True, check=True)
                subprocess.run(['patch', '-p1', '-i', str(PATCH_ROOT / name)], cwd=folder, capture_output=True, check=True)
            before_overlay = adapt(folder, args.version)
            feed = root / 'feed'; feed.mkdir(); package_path = feed / ('W.DmProvider.' + args.version + '.nupkg'); package_path.write_bytes(package)
            config = ET.Element('configuration'); sources_xml = ET.SubElement(config, 'packageSources'); ET.SubElement(sources_xml, 'clear')
            ET.SubElement(sources_xml, 'add', key='candidate', value=str(feed)); ET.SubElement(sources_xml, 'add', key='nuget.org', value='https://api.nuget.org/v3/index.json')
            ET.ElementTree(config).write(folder / 'NuGet.Config', encoding='utf-8')
            adaptation = overlay(before_overlay, folder); (root / 'T19-overlay.patch').write_text(adaptation)
            expected_sources = sources(folder)
            with tempfile.TemporaryDirectory(prefix='reconstruction-', dir=root) as temporary:
                rebuilt = Path(temporary) / 'W'; rebuilt.mkdir(); extract(raw_archive, rebuilt)
                for patch in (PATCH_ROOT / 'combined.patch', root / 'T19-overlay.patch'):
                    remove_empty = ['-E'] if patch.name == 'T19-overlay.patch' else []
                    subprocess.run(['patch', '-p1', *remove_empty, '--dry-run', '-i', str(patch)], cwd=rebuilt, capture_output=True, check=True)
                    subprocess.run(['patch', '-p1', *remove_empty, '-i', str(patch)], cwd=rebuilt, capture_output=True, check=True)
                if sources(rebuilt) != expected_sources: raise ValueError('overlay_reconstruction_source_mismatch')
            pin = json.loads((folder / 'global.json').read_text())['sdk']
            if pin.get('version') != SDK: raise ValueError('downstream_sdk_pin_mismatch')
            project_kind = lane if lane in PROJECTS else 'functional'
            manifest['lanes'][profile + '-' + lane] = {'profile': profile, 'lane': lane, 'root': str(root), 'project': PROJECTS[project_kind],
                'filter': FILTERS.get(lane), 'legacy_filter': LEGACY_FILTERS.get(lane), 'sources': expected_sources, 'overlay_reconstruction_verified': True,
                'overlay_sha256': sha(root / 'T19-overlay.patch'), 'candidate_path': str(package_path), 'status': 'prepared_not_run',
                'policy_id': BUDGET_POLICY_ID, 'host_timeout_seconds': host_timeout_seconds(profile, lane)}
    manifest['tool_hashes'] = {path.name: sha(path) for path in HERE.iterdir() if path.is_file()}
    write(output / 'manifest.json', manifest); emit({'status': 'prepared_not_run', 'run_dir': str(output), 'lanes': list(manifest['lanes'])})


if __name__ == '__main__':
    try: main()
    except Exception as error:
        emit({'status': 'rejected', 'reason': str(error) if isinstance(error, ValueError) and re.fullmatch(r'[A-Za-z0-9_]+', str(error)) else 'prepare_failed'})
        sys.exit(1)
