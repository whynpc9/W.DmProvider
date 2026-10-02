#!/usr/bin/env python3
"""Inspect, never extract, the exact candidate package; exceptions stay private."""
import hashlib
import json
from pathlib import Path
import sys
import xml.etree.ElementTree as ET
import zipfile


def main() -> None:
    package, expected, output = Path(sys.argv[1]), sys.argv[2], Path(sys.argv[3])
    raw = package.read_bytes()
    with zipfile.ZipFile(package) as archive:
        nuspecs = [name for name in archive.namelist() if name.endswith('.nuspec')]
        if len(nuspecs) != 1:
            raise ValueError('nuspec_count')
        root = ET.fromstring(archive.read(nuspecs[0]))
        namespace = root.tag.split('}')[0] + '}' if '}' in root.tag else ''
        metadata = root.find(namespace + 'metadata')
        if metadata is None or metadata.findtext(namespace + 'id') != 'W.DmProvider' or metadata.findtext(namespace + 'version') != expected:
            raise ValueError('package_identity')
        asset = 'lib/net10.0/W.DmProvider.dll'
        if archive.namelist().count(asset) != 1:
            raise ValueError('asset_count')
        manifest = {'schema_version': 1, 'version': expected, 'package_sha256': hashlib.sha256(raw).hexdigest(),
                    'assets': {asset: hashlib.sha256(archive.read(asset)).hexdigest()}}
    output.write_text(json.dumps(manifest, sort_keys=True, indent=2) + '\n')
    print(json.dumps({'status': 'exact_package_inspected', 'package_sha256': manifest['package_sha256']}))


if __name__ == '__main__':
    try:
        main()
    except Exception as error:
        print(json.dumps({'status': 'rejected', 'classification': 'package_inspection_failed', 'error_type': type(error).__name__}))
        sys.exit(1)
