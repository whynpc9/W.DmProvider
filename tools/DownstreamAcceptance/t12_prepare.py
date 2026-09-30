#!/usr/bin/env python3
"""Pinned EF export + scoped patches + W bridge, without dotnet or secrets."""
import argparse
import difflib
from datetime import datetime
import hashlib
import io
import json
import os
from pathlib import Path
import re
import subprocess
import tarfile
import zipfile
import xml.etree.ElementTree as ET

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent.parent
EF_COMMIT = '113014cc74dd1f751ef97226a78d2ec855b32c8c'
ARCHIVE_SHA = '9dead07c1de4220bd13bdadab0ed2713f43008af5e7c0cf7a629549a6739096d'


def sha(data):
    return hashlib.sha256(data).hexdigest()


def bounded(path):
    p = Path(path).resolve()
    if not p.is_relative_to((ROOT / '.local/t12/ef').resolve()):
        raise ValueError('outside_t12_ef')
    return p


def sources(folder):
    return {str(p.relative_to(folder)): sha(p.read_bytes()) for p in sorted(folder.rglob('*')) if p.is_file()
            and not any(x in ('obj', 'bin', 'artifacts', '.git') for x in p.relative_to(folder).parts)}


def replace(text, old, new, tag):
    if old not in text:
        raise ValueError('bridge_anchor_' + tag)
    return text.replace(old, new)


def bridge(folder):
    before = {str(p.relative_to(folder)): p.read_bytes() for p in folder.rglob('*') if p.is_file()}
    for p in folder.rglob('*.cs'):
        p.write_text(p.read_text().replace('using Dm;', 'using W.Dm;').replace('cref="Dm.', 'cref="W.Dm.'))
    for p in folder.rglob('*.csproj'):
        p.write_text(p.read_text().replace('PackageReference Include="DM.DmProvider"', 'PackageReference Include="W.DmProvider"'))
    guard = folder / 'test/Shared/T12CandidateGuard.cs'
    guard.write_bytes((HERE / 't12_test_guard.cs').read_bytes())
    for name in ('Tests', 'FunctionalTests', 'Specification.Tests'):
        p = folder / ('test/W.EntityFrameworkCore.Dameng.' + name + '/W.EntityFrameworkCore.Dameng.' + name + '.csproj')
        p.write_text(p.read_text().replace('</Project>', '<ItemGroup><Compile Include="../Shared/T12CandidateGuard.cs" Link="T12CandidateGuard.cs" /></ItemGroup>\n</Project>'))
    p = folder / 'src/W.EntityFrameworkCore.Dameng/Storage/Internal/DamengTransientExceptionDetector.cs'
    p.write_text('''using W.Dm;

namespace W.EntityFrameworkCore.Dameng.Storage.Internal;

internal static class DamengTransientExceptionDetector
{
    // R1 has no public NotSent/ReplaySafe classifier. Numbers never prove safe replay.
    public static bool ShouldRetryOn(Exception exception)
        => exception is DmException { IsTransient: true }
            && exception is not DmCommitOutcomeUnknownException;
}
''')
    p = folder / 'src/W.EntityFrameworkCore.Dameng/Storage/Internal/DamengRetryingExecutionStrategy.cs'
    text = p.read_text()
    text = replace(text, '=> _additionalErrorNumbers = errorNumbersToAdd?.ToFrozenSet();',
                   '=> _additionalErrorNumbers = ValidateAdditionalNumbers(errorNumbersToAdd);', 'retry_ctor')
    text = replace(text, '''        => (exception is DmException dmException
                && _additionalErrorNumbers?.Contains(dmException.Number) == true)
            || DamengTransientExceptionDetector.ShouldRetryOn(exception);''',
                   '''        => DamengTransientExceptionDetector.ShouldRetryOn(exception);

    private static FrozenSet<int>? ValidateAdditionalNumbers(IEnumerable<int>? values)
    {
        var numbers = values?.ToFrozenSet();
        if (numbers is { Count: > 0 })
        {
            throw new NotSupportedException("W R1 does not support error-number retry overrides without a safe replay classifier.");
        }
        return numbers;
    }''', 'retry_predicate')
    text = text.replace('Additional <see cref="DmException.Number" /> values to treat as transient.',
                        'Error-number overrides. Nonempty values are unsupported in W R1.')
    p.write_text(text)
    p = folder / 'src/W.EntityFrameworkCore.Dameng/Infrastructure/DamengDbContextOptionsBuilder.cs'
    text = p.read_text()
    text = replace(text, 'var errorNumbers = errorNumbersToAdd.ToArray();',
                   'var errorNumbers = errorNumbersToAdd.ToArray();\n        RejectNonemptyOverrides(errorNumbers);', 'retry_builder_collection')
    text = replace(text, 'var errorNumbers = errorNumbersToAdd?.ToArray();',
                   'var errorNumbers = errorNumbersToAdd?.ToArray();\n        RejectNonemptyOverrides(errorNumbers);', 'retry_builder_enumerable')
    text = text.replace('Additional <see cref="W.Dm.DmException.Number" /> values to treat as transient.',
                        'Error-number overrides. Nonempty values are unsupported in W R1.')
    text = text.rstrip()[:-1] + '''    private static void RejectNonemptyOverrides(int[]? numbers)
    {
        if (numbers is { Length: > 0 })
        {
            throw new NotSupportedException("W R1 does not support error-number retry overrides without a safe replay classifier.");
        }
    }
}
'''
    p.write_text(text)
    (folder / 'test/W.EntityFrameworkCore.Dameng.Tests/DamengExecutionStrategyTests.cs').write_bytes((HERE / 't12_retry_tests.cs').read_bytes())
    for name in ('FunctionalTests', 'Specification.Tests'):
        p = folder / ('test/W.EntityFrameworkCore.Dameng.' + name + '/DamengTestEnvironment.cs')
        text = replace(p.read_text(), '=> Environment.GetEnvironmentVariable(ConnectionStringVariable);',
                       '''=> ConfigureTestConnection(Environment.GetEnvironmentVariable(ConnectionStringVariable));

    private static string? ConfigureTestConnection(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var builder = new global::W.Dm.DmConnectionStringBuilder(raw)
        {
            TransportSecurity = global::W.Dm.DmTransportSecurity.PlaintextAllowed,
            PersistSecurityInfo = false,
            Schema = "WDM_PROVIDER_TEST",
            ConnectTimeout = TimeSpan.FromSeconds(20),
            CommandTimeout = 20
        };
        if (!string.Equals(builder.User, "WDM_PROVIDER_TEST", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The dedicated TEST user is required before authentication.");
        return builder.ConnectionString;
    }''', 'test_environment_' + name)
        p.write_text(text)
    p = folder / 'test/W.EntityFrameworkCore.Dameng.FunctionalTests/DamengIsolationLevelFunctionalTests.cs'
    text = p.read_text().replace('SaveChangesFailsAfterBeginningReadUncommittedOrSerializable', 'SaveChangesSucceedsAfterBeginningReadUncommittedOrSerializable')
    text = replace(text, '''                    var updateException = await Assert.ThrowsAsync<DbUpdateException>(
                        () => context.SaveChangesAsync());
                    AssertCommandTextHasNoValue(updateException);
                    context.ChangeTracker.Clear();''',
                   '''                    Assert.Equal(1, await context.SaveChangesAsync());
                    await transaction.RollbackAsync();''', 'isolation_update')
    text = replace(text, '''                    var insertException = await Assert.ThrowsAsync<DbUpdateException>(
                        () => context.SaveChangesAsync());
                    AssertCommandTextHasNoValue(insertException);
                }
            });''',
                   '''                    Assert.Equal(1, await context.SaveChangesAsync());
                    Assert.True(context.Entities.Local.Single().Id > 0);
                    await transaction.RollbackAsync();
                }
                await using (var verify = CreateContext(store))
                {
                    var remaining = Assert.Single(await verify.Entities.AsNoTracking().ToListAsync());
                    Assert.Equal(id, remaining.Id);
                    Assert.Equal("隔离级别-种子", remaining.Name);
                    Assert.Equal(1, remaining.Version);
                }
            });''', 'isolation_insert_final')
    text = replace(text, '''    private static void AssertCommandTextHasNoValue(DbUpdateException exception)
    {
        var inner = Assert.IsType<InvalidOperationException>(exception.InnerException);
        Assert.Contains("CommandText has no value", inner.Message, StringComparison.Ordinal);
    }

''', '', 'isolation_old_assert')
    text = text.replace('    [DamengTheory]\n    [InlineData(IsolationLevel.ReadUncommitted)]',
                        '    [DamengTheory]\n    [Trait("Category", "Improvement")]\n    [InlineData(IsolationLevel.ReadUncommitted)]')
    p.write_text(text)
    p = folder / 'test/W.EntityFrameworkCore.Dameng.FunctionalTests/DamengExtendedTypeMappingFunctionalTests.cs'
    text = replace(p.read_text(), '    public Task DateTimeOffsetRawAdoReadbackPreservesServerEvidence()',
                   '    [Trait("Category", "OfficialCharacterization")]\n    public Task DateTimeOffsetRawAdoReadbackPreservesServerEvidence()', 'raw_dto_category')
    p.write_text(text)
    p = folder / 'test/W.EntityFrameworkCore.Dameng.FunctionalTests/DamengMigrationsFunctionalTests.cs'
    text = replace(p.read_text(), '''                    $"EnsureSchema execution step {step} failed. Text: "
                    + text.Replace("\\r", "\\\\r", StringComparison.Ordinal)
                        .Replace("\\n", "\\\\n", StringComparison.Ordinal),
                    exception);''',
                   '''                    $"EnsureSchema execution step {step} failed ({exception.GetType().Name}).");''', 'migration_safe_exception')
    p.write_text(text)
    changes = []
    for p in sorted(folder.rglob('*')):
        if not p.is_file():
            continue
        rel = str(p.relative_to(folder))
        old = before.get(rel, b'')
        new = p.read_bytes()
        if old != new:
            changes.extend(difflib.unified_diff(old.decode().splitlines(True), new.decode().splitlines(True),
                           fromfile='a/' + rel, tofile='b/' + rel))
    return ''.join(changes)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--output', required=True)
    parser.add_argument('--candidate', required=True)
    parser.add_argument('--candidate-sha256', required=True)
    parser.add_argument('--version', required=True)
    parser.add_argument('--driver-dll-sha256', required=True)
    parser.add_argument('--patch', action='append', default=[])
    parser.add_argument('--ef-repo', default=os.environ.get('DAMENG_T12_EF_REPO', '/Users/wanghongyi/Projects/dameng-entityframework-core'))
    args = parser.parse_args()
    output = bounded(args.output)
    if output.exists():
        raise ValueError('output_exists')
    if not re.fullmatch(r'0\.1\.0-(?:t11|t12|r1)\.[0-9]{14}', args.version):
        raise ValueError('candidate_unique_version_required')
    datetime.strptime(args.version.rsplit('.', 1)[1], '%Y%m%d%H%M%S')
    package = Path(args.candidate).read_bytes()
    if sha(package) != args.candidate_sha256:
        raise ValueError('candidate_package_hash_mismatch')
    with zipfile.ZipFile(io.BytesIO(package)) as z:
        spec = ET.fromstring(z.read(next(n for n in z.namelist() if n.endswith('.nuspec'))))
        fields = {n.tag.split('}')[-1]: n.text for n in spec.iter() if n.tag.split('}')[-1] in ('id', 'version')}
        if fields != {'id': 'W.DmProvider', 'version': args.version}:
            raise ValueError('candidate_identity_mismatch')
        if sha(z.read('lib/net10.0/W.DmProvider.dll')) != args.driver_dll_sha256:
            raise ValueError('candidate_dll_hash_mismatch')
    ef_repo = str(Path(args.ef_repo).resolve())
    archive = subprocess.check_output(['git', '-C', ef_repo, 'archive', '--format=tar', EF_COMMIT])
    if sha(archive) != ARCHIVE_SHA:
        raise ValueError('pinned_archive_hash_mismatch')
    folder = output / 'W'
    folder.mkdir(parents=True)
    with tarfile.open(fileobj=io.BytesIO(archive)) as tar:
        for item in tar.getmembers():
            name = Path(item.name)
            if name.is_absolute() or '..' in name.parts or item.issym() or item.islnk() or '.local' in name.parts or name.name == '.local-test.secrets.json':
                raise ValueError('unsafe_archive_member')
            target = folder / name
            if item.isdir():
                target.mkdir(parents=True, exist_ok=True)
            elif item.isfile():
                target.parent.mkdir(parents=True, exist_ok=True)
                target.write_bytes(tar.extractfile(item).read())
                target.chmod(item.mode)
    (output / 'ef-clean.tar').write_bytes(archive)
    patches = []
    for value in args.patch:
        path = Path(value).resolve()
        patch = path.read_bytes()
        for header in re.findall(r'^\+\+\+ (?:b/)?([^\t\n]+)', patch.decode(), re.MULTILINE):
            target_path = Path(header)
            if target_path.is_absolute() or '..' in target_path.parts or target_path.suffix != '.cs' or target_path.parts[:2] != ('test', 'W.EntityFrameworkCore.Dameng.FunctionalTests'):
                raise ValueError('owner_patch_outside_functional_scope')
        subprocess.run(['patch', '-p1', '--dry-run', '-i', str(path)], cwd=folder, check=True, capture_output=True)
        subprocess.run(['patch', '-p1', '-i', str(path)], cwd=folder, check=True, capture_output=True)
        target = output / ('owner-' + str(len(patches)) + '.patch')
        target.write_bytes(patch)
        patches.append({'file': str(target), 'sha256': sha(patch)})
    patch = bridge(folder)
    (output / 'bridge.patch').write_text(patch)
    props = folder / 'Directory.Packages.props'
    old_props = props.read_text()
    props.write_text(old_props.replace('[10.0.12,11.0.0)', '[10.0.12]').replace(
        '<PackageVersion Include="DM.DmProvider" Version="[8.3.1.47463,8.4.0)" />',
        '<PackageVersion Include="W.DmProvider" Version="[' + args.version + ']" />'))
    with (output / 'bridge.patch').open('a') as stream:
        stream.writelines(difflib.unified_diff(old_props.splitlines(True), props.read_text().splitlines(True),
                          fromfile='a/Directory.Packages.props', tofile='b/Directory.Packages.props'))
    feed = output / 'candidate-feed'
    feed.mkdir()
    candidate_path = feed / ('W.DmProvider.' + args.version + '.nupkg')
    candidate_path.write_bytes(package)
    config = ET.Element('configuration'); ps = ET.SubElement(config, 'packageSources'); ET.SubElement(ps, 'clear')
    ET.SubElement(ps, 'add', key='candidate', value=str(feed)); ET.SubElement(ps, 'add', key='nuget.org', value='https://api.nuget.org/v3/index.json')
    ET.ElementTree(config).write(folder / 'NuGet.Config', encoding='utf-8')
    audit = folder / 'T12Audit'; audit.mkdir()
    (audit / 'Program.cs').write_bytes((HERE / 't12_audit.cs').read_bytes())
    (audit / 'T12Audit.csproj').write_text('''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><EnableNETAnalyzers>false</EnableNETAnalyzers><NuGetAudit>false</NuGetAudit></PropertyGroup><ItemGroup><ProjectReference Include="../src/W.EntityFrameworkCore.Dameng/W.EntityFrameworkCore.Dameng.csproj" /></ItemGroup></Project>\n''')
    manifest = {'schema': 1, 'task': 'T12', 'ef_commit': EF_COMMIT, 'ef_archive_sha256': ARCHIVE_SHA, 'ef_source_repo': ef_repo,
                'candidate': {'id': 'W.DmProvider', 'version': args.version, 'package_sha256': args.candidate_sha256,
                              'driver_dll_sha256': args.driver_dll_sha256, 'package_path': str(candidate_path)},
                'bridge_patch_sha256': sha((output / 'bridge.patch').read_bytes()), 'owner_patches': patches,
                'source_hashes': sources(folder), 'ef_version': '10.0.12',
                'tool_hashes': {p.name: sha(p.read_bytes()) for p in HERE.glob('t12*') if p.is_file()},
                'excluded': ['admin MigrationScript fixture creates users/tablespaces', 'SchemaCreationGuard creates/drops schema',
                             'DateTimeOffsetRawAdoReadbackPreservesServerEvidence: official CLR representation characterization'],
                'retry_policy': 'driver IsTransient true and non-CommitUnknown only; nonempty number override rejected',
                'lanes_ready': ['audit', 'unit', 'functional', 'specification', 'reverse', 'migrations', 'queries']}
    if any('DamengDotNetEfCliFunctionalTests.cs' in Path(p['file']).read_text() for p in patches): manifest['lanes_ready'].append('cli')
    if (folder / 'test/W.EntityFrameworkCore.Dameng.FunctionalTests/DamengCurrentSchemaScriptFunctionalTests.cs').exists(): manifest['lanes_ready'].append('scripts')
    (output / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n')
    print('t12_snapshot_ready ' + str(output))


if __name__ == '__main__':
    try:
        main()
    except Exception as error:
        reason = str(error) if isinstance(error, ValueError) and re.fullmatch('[a-zA-Z0-9_]+', str(error)) else type(error).__name__
        print('t12_prepare_failed ' + reason)
        raise SystemExit(1)
