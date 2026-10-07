"""Explicit, immutable T19 tooling overlay; no builds, network or environment reads."""
import gzip
import hashlib
import io
import json
import os
from pathlib import Path, PurePosixPath
import stat
import tarfile

REVISION = 't19-cli-retention-v1'
CANDIDATE = {
    'version': '0.1.0-r3.20261003023000',
    'package_sha256': '8fb616decd1053cff6a81a7f3533b22891fab7074bd76bd632d535c55b4c4594',
    'driver_dll_sha256': 'c0dfcfbc0ab39ba8029c741b3f4a264e3945b354febcab12a4a09fa8cc42b2c7',
    'driver_mvid': '854dc1b7-7a1a-4ed9-9b26-9efeca7acedf',
    'source_manifest_sha256': 'ba66f5485c5473fc51aa74354e6f00b4e85cbaa70e294fc25f6e3190385b919f',
}
PREFIX = 'tools/DownstreamAcceptance/T19/'
ALLOWLIST = frozenset(PREFIX + name for name in (
    'candidate_guard.cs', 'prepare.py', 'run.py', 'validate.py', 'README.md',
    'test_loaded_asset_contract.py', 'execution_overlay.py', 'test_execution_overlay.py'))
ROOTS = ('src', 'tests', 'tools', 'eng', 'scripts', 'docs', '.github')
ROOT_FILES = ('global.json', 'AGENTS.md', 'README.md', '.gitignore', 'LICENSE', 'THIRD-PARTY-NOTICES.md')
EXCLUDED = frozenset(('.git', '.local', 'bin', 'obj', '__pycache__', '.pytest_cache', '.vs', '.idea', '.DS_Store'))
OVERLAY_NAME = 'execution-overlay.json'
MANIFEST_NAME = 'execution-source-manifest.json'
ARCHIVE_NAME = 'execution-source.tar.gz'


class OverlayError(ValueError):
    """Only fixed safe codes, never source contents/paths/exception messages."""


def need(value, code):
    if not value:
        raise OverlayError(code)


def canonical(value):
    # Match the producer's exact tree-hash serialization, including ASCII escaping.
    return (json.dumps(value, sort_keys=True, indent=2) + '\n').encode()


def digest(value):
    return hashlib.sha256(value).hexdigest()


def file_sha(path):
    with Path(path).open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def relative(value):
    need(type(value) is str and value and '\\' not in value and '\x00' not in value, 'invalid_relative_path')
    path = PurePosixPath(value)
    need(not path.is_absolute() and all(part not in ('', '.', '..') for part in path.parts) and path.as_posix() == value,
         'path_escape_or_noncanonical')
    return value


def safe_path(value, directory=False):
    path = Path(value).absolute()
    need(path.exists(), 'required_path_missing')
    for part in (path, *path.parents):
        need(not part.is_symlink(), 'symlink_rejected')
    need(path.is_dir() if directory else path.is_file(), 'path_type_mismatch')
    return path.resolve()


def regular(path):
    st = path.lstat()
    need(stat.S_ISREG(st.st_mode), 'nonregular_source_rejected')
    return {'size': st.st_size, 'sha256': file_sha(path), 'mode': stat.S_IMODE(st.st_mode)}


def inventory(root):
    """Same validation-source scope as the producer, with full permission modes."""
    root = safe_path(root, True)
    files = []
    for name in ROOT_FILES:
        path = root / name
        if path.exists() or path.is_symlink():
            need(not path.is_symlink(), 'symlink_rejected')
            files.append(path)
    for name in ROOTS:
        top = root / name
        if not top.exists() and not top.is_symlink():
            continue
        need(top.is_dir() and not top.is_symlink(), 'source_root_type_or_symlink')
        for base, directories, names in os.walk(top, followlinks=False):
            kept = []
            for entry in directories:
                if entry in EXCLUDED:
                    continue
                need(not (Path(base) / entry).is_symlink(), 'symlink_rejected')
                kept.append(entry)
            directories[:] = kept
            for entry in names:
                if entry not in EXCLUDED:
                    path = Path(base) / entry
                    need(not path.is_symlink(), 'symlink_rejected')
                    files.append(path)
    rows = []
    for path in sorted(files, key=lambda p: p.relative_to(root).as_posix()):
        name = relative(path.relative_to(root).as_posix())
        rows.append({'path': name, **regular(path)})
    need(len({row['path'] for row in rows}) == len(rows), 'duplicate_inventory_path')
    return rows


def pins(bindings):
    need(type(bindings) is dict and all(bindings.get(key) == value for key, value in CANDIDATE.items()), 'candidate_tuple_mismatch')
    return dict(CANDIDATE)


def producer_rows(manifest_path, source_dir):
    path = safe_path(manifest_path)
    need(file_sha(path) == CANDIDATE['source_manifest_sha256'], 'producer_manifest_hash_mismatch')
    need(path.stat().st_size <= 8 * 1024 * 1024, 'producer_manifest_bound_exceeded')
    manifest = json.loads(path.read_bytes())
    need(manifest.get('schema_version') == 1 and manifest.get('task') == 'T19' and type(manifest.get('files')) is list,
         'producer_manifest_schema_mismatch')
    rows = []
    for row in manifest['files']:
        need(type(row) is dict and set(row) == {'path', 'sha256', 'size', 'executable'} and type(row['executable']) is bool and
             type(row['size']) is int and row['size'] >= 0 and type(row['sha256']) is str and len(row['sha256']) == 64,
             'producer_file_schema_mismatch')
        rows.append({'path': relative(row['path']), 'sha256': row['sha256'], 'size': row['size'],
                     'mode': 0o755 if row['executable'] else 0o644})
    need(rows == sorted(rows, key=lambda row: row['path']) and len({row['path'] for row in rows}) == len(rows), 'producer_path_order_or_duplicate')
    need(digest(canonical(manifest['files'])) == manifest.get('tree_sha256'), 'producer_tree_hash_mismatch')
    need(inventory(source_dir) == rows, 'producer_source_bytes_or_modes_mismatch')
    return rows, manifest['tree_sha256']


def declared_map(declarations):
    need(type(declarations) is list and declarations, 'explicit_declaration_required')
    result = {}
    for declaration in declarations:
        need(type(declaration) is dict and set(declaration) == {'path', 'action'}, 'declaration_schema_mismatch')
        path = relative(declaration['path'])
        need(path in ALLOWLIST, 'path_not_in_frozen_allowlist')
        need(path not in result and declaration['action'] in ('add', 'modify'), 'duplicate_or_invalid_declaration')
        result[path] = declaration['action']
    return result


def differences(old_rows, new_rows, declarations):
    declared = declared_map(declarations)
    old = {row['path']: row for row in old_rows}
    new = {row['path']: row for row in new_rows}
    need(set(old) <= set(new), 'source_deletion_rejected')
    changed = {path for path in new if path not in old or new[path] != old[path]}
    need(changed == set(declared), 'undeclared_difference_or_unused_declaration')
    changes = []
    for path in sorted(changed):
        action = 'modify' if path in old else 'add'
        need(declared[path] == action, 'declared_action_mismatch')
        changes.append({'path': path, 'action': action, 'old': old.get(path), 'new': new[path]})
    return changes


def write_new(path, data):
    with os.fdopen(os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600), 'wb') as stream:
        stream.write(data)
        stream.flush()
        os.fsync(stream.fileno())


def make_archive(path, root, rows):
    with os.fdopen(os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600), 'wb') as raw:
        with gzip.GzipFile(filename='', mode='wb', fileobj=raw, mtime=0) as compressed:
            with tarfile.open(mode='w|', fileobj=compressed, format=tarfile.PAX_FORMAT) as archive:
                for row in rows:
                    data = (root / row['path']).read_bytes()
                    need(len(data) == row['size'] and digest(data) == row['sha256'] and stat.S_IMODE((root / row['path']).stat().st_mode) == row['mode'],
                         'source_changed_during_archive')
                    item = tarfile.TarInfo(row['path'])
                    item.size, item.mode, item.mtime = len(data), row['mode'], 0
                    item.uid = item.gid = 0
                    item.uname = item.gname = ''
                    archive.addfile(item, io.BytesIO(data))
        raw.flush()
        os.fsync(raw.fileno())


def verify_archive(path, rows):
    wanted = {row['path']: row for row in rows}
    seen = set()
    with tarfile.open(safe_path(path), mode='r:gz') as archive:
        for member in archive:
            name = relative(member.name)
            need(member.isfile() and name in wanted and name not in seen, 'archive_unsafe_or_extra_member')
            row = wanted[name]
            need(member.mode == row['mode'] and member.size == row['size'], 'archive_mode_or_size_mismatch')
            source = archive.extractfile(member)
            need(source is not None and hashlib.file_digest(source, 'sha256').hexdigest() == row['sha256'], 'archive_file_hash_mismatch')
            seen.add(name)
    need(seen == set(wanted), 'archive_missing_member')


def create_manifest(*, producer_manifest_path, producer_source_dir, execution_source_dir,
                    candidate_bindings, declared_changes, output_dir):
    pins(candidate_bindings)
    producer = safe_path(producer_source_dir, True)
    execution = safe_path(execution_source_dir, True)
    output = safe_path(output_dir, True)
    need(not any(output.iterdir()) and producer != execution, 'new_output_and_separate_trees_required')
    need(not output.is_relative_to(producer), 'artifact_output_inside_producer_rejected')
    if output.is_relative_to(execution):
        need(any(part in EXCLUDED for part in output.relative_to(execution).parts), 'artifact_output_inside_execution_inputs_rejected')
    old, producer_tree = producer_rows(producer_manifest_path, producer)
    new = inventory(execution)
    changes = differences(old, new, declared_changes)
    execution_manifest = {'schema_version': 1, 'task': 'T19_EXECUTION_SOURCE', 'revision': REVISION,
                          'candidate': dict(CANDIDATE), 'files': new, 'tree_sha256': digest(canonical(new))}
    write_new(output / MANIFEST_NAME, canonical(execution_manifest))
    make_archive(output / ARCHIVE_NAME, execution, new)
    verify_archive(output / ARCHIVE_NAME, new)
    need(inventory(producer) == old and inventory(execution) == new, 'source_changed_during_freeze')
    manifest = {'schema_version': 1, 'task': 'T19_EXECUTION_OVERLAY', 'revision': REVISION,
                'candidate': dict(CANDIDATE), 'producer_source_manifest_sha256': CANDIDATE['source_manifest_sha256'],
                'producer_tree_sha256': producer_tree, 'allowlist': sorted(ALLOWLIST), 'changes': changes,
                'execution_source_manifest': MANIFEST_NAME, 'execution_source_manifest_sha256': file_sha(output / MANIFEST_NAME),
                'execution_tree_sha256': execution_manifest['tree_sha256'], 'execution_source_archive': ARCHIVE_NAME,
                'execution_source_archive_sha256': file_sha(output / ARCHIVE_NAME),
                'scope': 'explicit_tooling_only;product_tests_build_inputs_unchanged', 'production_release_accepted': False}
    write_new(output / OVERLAY_NAME, canonical(manifest))
    return validate_manifest(manifest_path=output / OVERLAY_NAME, producer_manifest_path=producer_manifest_path,
                             producer_source_dir=producer, execution_source_dir=execution,
                             expected_candidate_bindings=candidate_bindings)


def validate_manifest(*, manifest_path, producer_manifest_path, producer_source_dir, execution_source_dir,
                      expected_candidate_bindings):
    pins(expected_candidate_bindings)
    path = safe_path(manifest_path)
    need(path.stat().st_size <= 1024 * 1024, 'overlay_manifest_bound_exceeded')
    overlay = json.loads(path.read_bytes())
    required = {'schema_version', 'task', 'revision', 'candidate', 'producer_source_manifest_sha256', 'producer_tree_sha256',
                'allowlist', 'changes', 'execution_source_manifest', 'execution_source_manifest_sha256', 'execution_tree_sha256',
                'execution_source_archive', 'execution_source_archive_sha256', 'scope', 'production_release_accepted'}
    need(set(overlay) == required and overlay['schema_version'] == 1 and overlay['task'] == 'T19_EXECUTION_OVERLAY' and
         overlay['revision'] == REVISION and overlay['production_release_accepted'] is False and
         overlay['scope'] == 'explicit_tooling_only;product_tests_build_inputs_unchanged', 'overlay_schema_or_revision_mismatch')
    need(overlay['candidate'] == CANDIDATE and overlay['producer_source_manifest_sha256'] == CANDIDATE['source_manifest_sha256'] and
         overlay['allowlist'] == sorted(ALLOWLIST), 'overlay_pins_or_allowlist_mismatch')
    producer = safe_path(producer_source_dir, True)
    execution = safe_path(execution_source_dir, True)
    old, producer_tree = producer_rows(producer_manifest_path, producer)
    need(overlay['producer_tree_sha256'] == producer_tree, 'producer_tree_binding_mismatch')
    need(overlay['execution_source_manifest'] == MANIFEST_NAME and overlay['execution_source_archive'] == ARCHIVE_NAME, 'artifact_path_escape')
    em_path, archive_path = path.parent / MANIFEST_NAME, path.parent / ARCHIVE_NAME
    safe_path(em_path); safe_path(archive_path)
    need(file_sha(em_path) == overlay['execution_source_manifest_sha256'] and file_sha(archive_path) == overlay['execution_source_archive_sha256'], 'execution_artifact_hash_mismatch')
    need(em_path.stat().st_size <= 8 * 1024 * 1024, 'execution_manifest_bound_exceeded')
    em = json.loads(em_path.read_bytes())
    need(set(em) == {'schema_version', 'task', 'revision', 'candidate', 'files', 'tree_sha256'} and em['schema_version'] == 1 and
         em['task'] == 'T19_EXECUTION_SOURCE' and em['revision'] == REVISION and em['candidate'] == CANDIDATE,
         'execution_manifest_schema_mismatch')
    new = inventory(execution)
    need(em['files'] == new and em['tree_sha256'] == digest(canonical(new)) == overlay['execution_tree_sha256'], 'execution_source_bytes_or_modes_mismatch')
    need(type(overlay['changes']) is list and all(type(row) is dict and set(row) == {'path', 'action', 'old', 'new'} for row in overlay['changes']), 'change_schema_mismatch')
    declarations = [{'path': row['path'], 'action': row['action']} for row in overlay['changes']]
    need(differences(old, new, declarations) == overlay['changes'], 'declared_old_new_hash_or_mode_mismatch')
    verify_archive(archive_path, new)
    return {'revision': REVISION, 'execution_overlay_manifest_path': str(path), 'execution_overlay_sha256': file_sha(path),
            'producer_source_manifest_sha256': CANDIDATE['source_manifest_sha256'],
            'execution_source_manifest_sha256': file_sha(em_path), 'execution_source_archive_sha256': file_sha(archive_path),
            'execution_tree_sha256': em['tree_sha256'], 'producer_source_dir': str(producer), 'execution_source_dir': str(execution),
            'producer_tool_root': str(producer / PREFIX), 'execution_tool_root': str(execution / PREFIX)}
