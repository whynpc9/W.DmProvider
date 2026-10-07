#!/usr/bin/env python3
"""Bind consumer inputs to explicit frozen provenance or an actually observed Git HEAD."""
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import stat
import subprocess
import sys


PRODUCER_PATH_ENV = 'R3_PRODUCER_SOURCE_MANIFEST'
PRODUCER_SHA_ENV = 'R3_PRODUCER_SOURCE_MANIFEST_SHA256'
REQUIRED_FILES = tuple(sorted(
    ['tools/R3ResourceProbe/' + name for name in ('Program.cs', 'ResourceSoak.cs', 'DiagnosticListenerCapture.cs',
     'R3ResourceProbe.csproj', 'README.md', 'run_probe.py', 'validate.py', 'source_identity.py')] +
    ['eng/t18.sh', 'eng/T18.md', 'global.json', 'tests/fixtures/r3-lob/vectors.json',
     'tests/fixtures/r3-lob/generate_vectors.py', 'tests/W.DmProvider.DiagnosticsTests/ScriptedTransactionFixture.cs']))


class SourceIdentityError(ValueError):
    pass  # Fixed codes only; never source contents, credentials or raw Git stderr.


def need(condition, reason):
    if not condition: raise SourceIdentityError(reason)


def encoded(value):
    return (json.dumps(value, sort_keys=True, indent=2) + '\n').encode()


def file_sha(path):
    with path.open('rb') as stream: return hashlib.file_digest(stream, 'sha256').hexdigest()


def relative(value):
    need(type(value) is str and value and '\\' not in value and '\x00' not in value, 'manifest_relative_path_invalid')
    path = PurePosixPath(value)
    need(not path.is_absolute() and '..' not in path.parts and '.' not in path.parts and path.as_posix() == value,
         'manifest_path_escape_or_noncanonical')
    return value


def source_file(repo, name):
    path = repo / relative(name)
    need(path.is_file(), 'required_consumer_source_missing')
    current = path
    while current != repo:
        need(not current.is_symlink(), 'consumer_source_symlink_rejected')
        current = current.parent
    need(stat.S_ISREG(path.stat().st_mode), 'consumer_source_type_invalid')
    return path


def record_sources(repo, *, producer_manifest=None, producer_manifest_sha256=None):
    original = Path(repo).absolute()
    need(original.is_dir() and not original.is_symlink(), 'source_root_invalid')
    repo = original.resolve()
    metadata = {}
    for name in REQUIRED_FILES:
        path = source_file(repo, name)
        metadata[name] = {'sha256': file_sha(path), 'size': path.stat().st_size,
                          'executable': bool(path.stat().st_mode & stat.S_IXUSR)}
    head, producer_head, manifest_sha = None, None, None
    explicit = producer_manifest is not None or producer_manifest_sha256 is not None
    if explicit:
        need(producer_manifest is not None and type(producer_manifest_sha256) is str and
             re.fullmatch(r'[0-9a-f]{64}', producer_manifest_sha256), 'producer_manifest_path_and_hash_required')
        path = Path(producer_manifest).absolute()
        need(path.is_file() and not path.is_symlink(), 'producer_manifest_missing_or_symlink')
        need(path.stat().st_size <= 8 * 1024 * 1024, 'producer_manifest_size_invalid')
        manifest_sha = file_sha(path)
        need(manifest_sha == producer_manifest_sha256, 'producer_manifest_hash_mismatch')
        try: manifest = json.loads(path.read_bytes())
        except (ValueError, UnicodeError): raise SourceIdentityError('producer_manifest_json_invalid') from None
        need(type(manifest) is dict and type(manifest.get('schema_version')) is int and manifest.get('schema_version') == 1 and manifest.get('task') == 'T19' and
             manifest.get('source_link') is False and manifest.get('validation_source_scope') == 'all_current_validation_source_including_untracked' and
             type(manifest.get('files')) is list, 'producer_manifest_schema_invalid')
        producer_head = manifest.get('head')
        need(type(producer_head) is str and re.fullmatch(r'(?:[0-9a-f]{40}|[0-9a-f]{64})', producer_head), 'producer_git_head_invalid')
        rows = manifest['files']; by_path = {}; ordered = []
        for row in rows:
            need(type(row) is dict and set(row) == {'path', 'size', 'sha256', 'executable'} and
                 type(row['size']) is int and row['size'] >= 0 and type(row['executable']) is bool and
                 type(row['sha256']) is str and re.fullmatch(r'[0-9a-f]{64}', row['sha256']), 'producer_manifest_file_schema_invalid')
            name = relative(row['path']); need(name not in by_path, 'producer_manifest_duplicate_path')
            by_path[name] = row; ordered.append(name)
        need(ordered == sorted(ordered) and rows and hashlib.sha256(encoded(rows)).hexdigest() == manifest.get('tree_sha256'),
             'producer_manifest_tree_invalid')
        for name, actual in metadata.items():
            need(name in by_path and all(by_path[name][key] == actual[key] for key in ('sha256', 'size', 'executable')),
                 'consumer_source_not_bound_to_producer')
        origin = 'verified_producer_source_manifest'
    else:
        try:
            head = subprocess.run(['git', 'rev-parse', 'HEAD'], cwd=repo, stdout=subprocess.PIPE,
                                  stderr=subprocess.DEVNULL, check=True).stdout.decode().strip()
        except (OSError, subprocess.CalledProcessError, UnicodeError):
            raise SourceIdentityError('live_git_head_required') from None
        need(re.fullmatch(r'[a-f0-9]{40}', head), 'head_identity')
        origin = 'live_git'
    hashes = {name: row['sha256'] for name, row in metadata.items()}
    return {'schema_version': 1, 'task': 'T18', 'identity_scope': 'consumer_source_only', 'source_origin': origin,
            'observed_git_head': head, 'producer_git_head': producer_head, 'producer_source_manifest_sha256': manifest_sha,
            'consumer_source_sha256': hashlib.sha256(json.dumps(hashes, sort_keys=True).encode()).hexdigest(),
            'files': hashes, 'file_metadata': metadata, 'product_source_mapping': 'root_frozen_candidate_manifest_required',
            'sourcelink_claimed': False}


def main():
    need(len(sys.argv) == 3, 'usage')
    result = record_sources(sys.argv[1], producer_manifest=os.environ.get(PRODUCER_PATH_ENV),
                            producer_manifest_sha256=os.environ.get(PRODUCER_SHA_ENV))
    Path(sys.argv[2]).write_bytes(encoded(result))
    print(json.dumps({'task': 'T18', 'status': 'consumer_source_recorded', 'source_origin': result['source_origin'],
                      'consumer_source_sha256': result['consumer_source_sha256']}))


if __name__ == '__main__':
    try:
        main()
    except Exception as error:
        print(json.dumps({'task': 'T18', 'status': 'rejected', 'classification': 'consumer_source_identity_failed',
                          'reason': str(error) if isinstance(error, SourceIdentityError) else 'consumer_source_identity_failed'}))
        sys.exit(1)
