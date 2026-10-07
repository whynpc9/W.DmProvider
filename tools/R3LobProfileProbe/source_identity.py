#!/usr/bin/env python3
"""Record consumer source hashes; product source mapping remains the root frozen candidate manifest."""
import hashlib
import json
from pathlib import Path
import re
import subprocess
import sys


def main():
    repo = Path(sys.argv[1]).resolve()
    files = sorted([repo / 'tools/R3LobProfileProbe' / name for name in
                    ('Program.cs', 'R3LobProfileProbe.csproj', 'README.md', 'run_probe.py', 'validate.py', 'source_identity.py')] +
                   [repo / 'eng/t17-profile.sh', repo / 'eng/T17-profile.md', repo / 'global.json',
                    repo / 'tests/fixtures/r3-lob/vectors.json', repo / 'tests/fixtures/r3-lob/generate_vectors.py',
                    repo / 'tests/fixtures/r3-lob/README.md',
                    repo / 'docs/implementation/evidence/T16/accepted-v3/accepted-source-manifest.json'])
    hashes = {str(path.relative_to(repo)): hashlib.sha256(path.read_bytes()).hexdigest() for path in files if path.is_file()}
    head = subprocess.run(['git', 'rev-parse', 'HEAD'], cwd=repo, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, check=True).stdout.decode().strip()
    if not re.fullmatch(r'[a-f0-9]{40}', head):
        raise ValueError('head_identity')
    result = {'schema_version': 1, 'task': 'T17-profile', 'identity_scope': 'consumer_source_only', 'observed_git_head': head,
              'consumer_source_sha256': hashlib.sha256(json.dumps(hashes, sort_keys=True).encode()).hexdigest(),
              'files': hashes, 'product_source_mapping': 'root_frozen_candidate_manifest_required', 'sourcelink_claimed': False}
    Path(sys.argv[2]).write_text(json.dumps(result, sort_keys=True, indent=2) + '\n')
    print(json.dumps({'task': 'T17-profile', 'status': 'consumer_source_recorded', 'consumer_source_sha256': result['consumer_source_sha256']}))


if __name__ == '__main__':
    try:
        main()
    except Exception:
        print('{"task":"T17-profile","status":"rejected","classification":"consumer_source_identity_failed"}')
        sys.exit(1)
