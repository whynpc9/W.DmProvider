"""Read-only pins and local T19 path/package guards. No dotnet, database or credential reads on import."""
import hashlib
import json
from pathlib import Path
import re
import struct
import uuid

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
EF_COMMIT = '113014cc74dd1f751ef97226a78d2ec855b32c8c'
ARCHIVE_SHA = '9dead07c1de4220bd13bdadab0ed2713f43008af5e7c0cf7a629549a6739096d'
EFCORE = '10.0.12'
SDK = '10.0.401'
BASE = ROOT / '.local/verification/r3/t19/ef'
PATCH_ROOT = HERE.parent / 'T12/handoff/R1'
PATCHES = {
    'combined.patch': '947e415a987f818cc22db17bc42580dc26d077e8d907e8d9a74dee12990a9fe2',
    'bridge.patch': '791e8cab59ce1348017214f169415f4eda1756460d77a684f4200dcb1df5f6d1',
    'cli-safety.patch': 'f2baa32522dff40317018149049b6865bb25e0c47fc0dc6b81d0276de76c5bf0',
    'current-schema-scripts.patch': '6603ae6481c9683e50b7cf5db648206da35107fff8ed8afae31269bd3aca9398'
}
FLAGS = ['-m:1', '/nodeReuse:false', '/p:UseSharedCompilation=false', '--disable-build-servers']
PROJECTS = {
    'audit': 'T19Audit/T19Audit.csproj',
    'unit': 'test/W.EntityFrameworkCore.Dameng.Tests/W.EntityFrameworkCore.Dameng.Tests.csproj',
    'specification': 'test/W.EntityFrameworkCore.Dameng.Specification.Tests/W.EntityFrameworkCore.Dameng.Specification.Tests.csproj',
    'functional': 'test/W.EntityFrameworkCore.Dameng.FunctionalTests/W.EntityFrameworkCore.Dameng.FunctionalTests.csproj'
}
LEGACY_FILTERS = {
    'functional': 'FullyQualifiedName!~DamengMigrationScriptFunctionalTests&Category!~CapabilityProbe&FullyQualifiedName!~DamengDotNetEfCliFunctionalTests&FullyQualifiedName!~DamengCurrentSchemaScriptFunctionalTests&Category!~OfficialCharacterization',
    'reverse': 'FullyQualifiedName~DamengReverseEngineeringFunctionalTests',
    'migrations': 'FullyQualifiedName~DamengMigrationsFunctionalTests',
    'cli': 'FullyQualifiedName~DamengDotNetEfCliFunctionalTests',
    'scripts': 'FullyQualifiedName~DamengCurrentSchemaScriptFunctionalTests',
    'queries': 'FullyQualifiedName!~DamengMigrationScriptFunctionalTests&Category!~CapabilityProbe&FullyQualifiedName!~DamengDotNetEfCliFunctionalTests&FullyQualifiedName!~DamengCurrentSchemaScriptFunctionalTests&FullyQualifiedName!~DamengMigrationsFunctionalTests&FullyQualifiedName!~DamengReverseEngineeringFunctionalTests&Category!~OfficialCharacterization'
}
FILTERS = {key: value + '&FullyQualifiedName!~R3DriverContractTests' for key, value in LEGACY_FILTERS.items()}
FILTERS.update(r3_shared='FullyQualifiedName~R3DriverContractTests&Category~R3Shared',
               r3_tls='FullyQualifiedName~R3DriverContractTests&Category~R3Tls')
SHARED_LANES = ('unit', 'audit', 'functional', 'cli', 'scripts', 'specification', 'r3_shared')
TLS_LANES = ('unit', 'r3_tls')
COUNTERS = ('total', 'executed', 'passed', 'failed', 'error', 'timeout', 'aborted', 'inconclusive', 'passedButRunAborted',
            'notRunnable', 'notExecuted', 'disconnected', 'warning', 'completed', 'inProgress', 'pending')
NS = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
BUDGET_POLICY_ID = 't19-shared-functional-host-v2'


def budget_policy():
    return {'policy_id': BUDGET_POLICY_ID, 'shared_functional_host_timeout_seconds': 1500,
            'unit_host_timeout_seconds': 600, 'other_test_host_timeout_seconds': 900,
            'audit_timeout_seconds': 90, 'build_timeout_seconds': 600, 'nested_cli_timeout_seconds': 300}


def is_frozen_budget_policy(value):
    expected = budget_policy()
    return isinstance(value, dict) and value.keys() == expected.keys() and all(type(value[key]) is type(want) and value[key] == want for key, want in expected.items())


def host_timeout_seconds(profile, lane):
    declared = SHARED_LANES if profile == 'shared' else TLS_LANES if profile == 'tls' else ()
    if lane not in declared: raise ValueError('unknown_budget_profile_or_lane')
    if lane == 'audit': return 90
    if lane == 'unit': return 600
    return 1500 if (profile, lane) == ('shared', 'functional') else 900


def command_timeout_seconds(profile, lane, execution_phase):
    host = host_timeout_seconds(profile, lane)
    if execution_phase == 'test': return host
    if execution_phase == 'audit': return 90
    if execution_phase == 'build': return 600
    raise ValueError('unknown_budget_execution_phase')


def verify_lane_budget(manifest, entry, key=None):
    expected = host_timeout_seconds(entry.get('profile'), entry.get('lane'))
    if not is_frozen_budget_policy(manifest.get('budget_policy')) or entry.get('policy_id') != BUDGET_POLICY_ID or \
        type(entry.get('host_timeout_seconds')) is not int or entry['host_timeout_seconds'] != expected or \
        key is not None and key != entry['profile'] + '-' + entry['lane']:
        raise ValueError('frozen_host_budget_policy_mismatch')
    return expected


def verify_execution_budget(profile, lane, execution_phase, policy_id, timeout):
    if policy_id != BUDGET_POLICY_ID or type(timeout) is not int or timeout != command_timeout_seconds(profile, lane, execution_phase):
        raise ValueError('execution_host_budget_mismatch')


def verify_lane_execution_record(manifest, entry, item):
    host = verify_lane_budget(manifest, entry)
    if item.get('policy_id') != BUDGET_POLICY_ID or item.get('profile') != entry['profile'] or item.get('lane') != entry['lane'] or \
        type(item.get('host_timeout_seconds')) is not int or item['host_timeout_seconds'] != host:
        raise ValueError('recorded_host_budget_policy_mismatch')
    commands = item.get('commands')
    if not isinstance(commands, list) or not commands: raise ValueError('host_execution_record_missing')
    for row in commands:
        if row.get('profile') != entry['profile'] or row.get('lane') != entry['lane']: raise ValueError('execution_profile_lane_mismatch')
        verify_execution_budget(row['profile'], row['lane'], row.get('execution_phase'), row.get('policy_id'), row.get('host_timeout_seconds'))
        elapsed = row.get('actual_elapsed_seconds')
        if type(row.get('timed_out')) is not bool or type(row.get('host_exit_code')) is not int or type(row.get('exit_code')) is not int or \
            type(elapsed) not in (int, float) or not 0 <= elapsed < float('inf') or \
            row.get('exit_code') != (124 if row['timed_out'] else row['host_exit_code']):
            raise ValueError('host_execution_outcome_mismatch')
        if row['timed_out'] or row['host_exit_code'] != 0: raise ValueError('command_process_outcome_not_successful')
    phase = 'audit' if entry['lane'] == 'audit' else 'test'
    hosts = [row for row in commands if row['execution_phase'] == phase]
    if len(hosts) != 1: raise ValueError('unique_lane_host_execution_required')
    row = hosts[0]
    if any(type(item.get(name)) is not type(row[name]) or item.get(name) != row[name] for name in ('host_exit_code', 'timed_out', 'actual_elapsed_seconds')) or \
        type(item.get('exit_code')) is not int or item['exit_code'] != row['exit_code'] or item['exit_code'] != 0 or row['timed_out'] or row['host_exit_code'] != 0:
        raise ValueError('lane_process_outcome_not_reconciled')


def sha(value): return hashlib.sha256(value if isinstance(value, bytes) else Path(value).read_bytes()).hexdigest()


def bounded(value):
    path = Path(value).resolve()
    if not path.is_relative_to(BASE.resolve()) or path == BASE.resolve(): raise ValueError('outside_new_t19_run')
    return path


def sources(folder):
    return {str(path.relative_to(folder)): sha(path) for path in sorted(folder.rglob('*')) if path.is_file() and
            not any(part in ('bin', 'obj', 'artifacts', '.git', '.packages', '.cli', '.http-cache') for part in path.relative_to(folder).parts)}


def write(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, sort_keys=True, indent=2) + '\n')


def emit(value): print(json.dumps({'schema_version': 1, 'task': 'T19', **value}, separators=(',', ':')))


def mvid(raw):
    """Read ECMA-335 Module.Mvid from an ordinary managed PE, without loading executable code."""
    def u16(offset): return struct.unpack_from('<H', raw, offset)[0]
    def u32(offset): return struct.unpack_from('<I', raw, offset)[0]
    def u64(offset): return struct.unpack_from('<Q', raw, offset)[0]
    pe = u32(0x3c)
    if raw[pe:pe + 4] != b'PE\0\0': raise ValueError('invalid_managed_pe')
    sections = u16(pe + 6); optional_size = u16(pe + 20); optional = pe + 24
    magic = u16(optional)
    directories = optional + (96 if magic == 0x10b else 112 if magic == 0x20b else 0)
    if directories == optional: raise ValueError('invalid_pe_optional_header')
    section_offset = optional + optional_size
    def file_offset(rva):
        for index in range(sections):
            offset = section_offset + index * 40
            address, virtual, size, pointer = u32(offset + 12), u32(offset + 8), u32(offset + 16), u32(offset + 20)
            if address <= rva < address + max(virtual, size):
                result = pointer + rva - address
                if result >= len(raw): raise ValueError('pe_rva_outside_file')
                return result
        raise ValueError('pe_rva_not_mapped')
    cli = file_offset(u32(directories + 14 * 8)); metadata = file_offset(u32(cli + 8))
    if raw[metadata:metadata + 4] != b'BSJB': raise ValueError('invalid_metadata_root')
    cursor = (metadata + 16 + u32(metadata + 12) + 3) & ~3
    streams = u16(cursor + 2); cursor += 4; heaps = {}
    for _ in range(streams):
        offset, length = u32(cursor), u32(cursor + 4); name_start = cursor + 8
        end = raw.index(0, name_start); name = raw[name_start:end].decode('ascii')
        heaps[name] = (metadata + offset, length); cursor = (end + 1 + 3) & ~3
    tables, _ = heaps.get('#~', heaps.get('#-', (0, 0))); guid_start, guid_size = heaps['#GUID']
    if not tables: raise ValueError('metadata_tables_missing')
    heap_flags = raw[tables + 6]; valid = u64(tables + 8); rows = tables + 24; module_rows = None
    for index in range(64):
        if valid & (1 << index):
            if index == 0: module_rows = u32(rows)
            rows += 4
    if module_rows != 1: raise ValueError('metadata_module_row_invalid')
    string_size = 4 if heap_flags & 1 else 2; guid_size_index = 4 if heap_flags & 2 else 2
    field = rows + 2 + string_size; ordinal = u32(field) if guid_size_index == 4 else u16(field)
    if ordinal < 1 or ordinal * 16 > guid_size: raise ValueError('metadata_mvid_index_invalid')
    return str(uuid.UUID(bytes_le=raw[guid_start + (ordinal - 1) * 16:guid_start + ordinal * 16]))
