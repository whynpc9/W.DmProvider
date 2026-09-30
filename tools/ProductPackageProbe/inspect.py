#!/usr/bin/env python3
"""Package and API completeness checks; output is one JSON document."""
import hashlib
import json
import pathlib
import sys
import zipfile
import xml.etree.ElementTree as ET


def digest(data):
    return hashlib.sha256(data).hexdigest()


def fail(kind):
    print(json.dumps({"schema_version": 1, "status": "rejected", "reason": kind}, separators=(",", ":")))
    raise SystemExit(1)


def source_hash(root):
    root = pathlib.Path(root)
    files = [p for p in root.rglob("*") if p.is_file() and
             not any(part in {"bin", "obj"} for part in p.relative_to(root).parts)]
    files.append(root.parent.parent / "THIRD-PARTY-NOTICES.md")
    h = hashlib.sha256()
    for p in sorted(files, key=lambda p: str(p)):
        h.update(str(p.relative_to(root.parent.parent)).encode())
        h.update(b"\0")
        h.update(bytes.fromhex(digest(p.read_bytes())))
    return h.hexdigest(), len(files)


def package(path, out, version, source_root):
    path = pathlib.Path(path)
    if not path.is_file():
        fail("package_missing")
    with zipfile.ZipFile(path) as archive:
        names = archive.namelist()
        if len(names) != len(set(names)):
            fail("duplicate_zip_entry")
        bad = [n for n in names if n.startswith("/") or ".." in pathlib.PurePosixPath(n).parts]
        if bad:
            fail("unsafe_zip_entry")
        binaries = [n for n in names if n.lower().endswith((".dll", ".so", ".dylib", ".a", ".pdb"))]
        expected = {"lib/net10.0/W.DmProvider.dll"} | {
            f"lib/net10.0/{culture}/W.DmProvider.resources.dll"
            for culture in ("en", "zh-CN", "zh-HK", "zh-TW")
        }
        if set(binaries) != expected:
            fail("package_binary_set_invalid")
        allowed = expected | {"README.md", "THIRD-PARTY-NOTICES.md", "W.DmProvider.nuspec",
                              "[Content_Types].xml", "_rels/.rels"}
        unexpected = [n for n in names if n not in allowed and
                      not (n.startswith("package/services/metadata/core-properties/") and n.endswith(".psmdcp"))]
        if unexpected:
            fail("unexpected_package_asset")
        if any("DM.DmProvider" in n or "/runtimes/" in f"/{n}" for n in names):
            fail("official_or_runtime_asset_present")
        assets = {n: digest(archive.read(n)) for n in sorted(binaries)}
        nuspecs = [n for n in names if n.endswith(".nuspec")]
        if len(nuspecs) != 1:
            fail("nuspec_count_invalid")
        root = ET.fromstring(archive.read(nuspecs[0]))
        ns = {"n": "http://schemas.microsoft.com/packaging/2012/06/nuspec.xsd"}
        metadata = root.find("n:metadata", ns)
        if metadata is None:
            fail("nuspec_metadata_missing")
        field = lambda name: metadata.findtext(f"n:{name}", namespaces=ns)
        if (field("id") != "W.DmProvider" or field("version") != version or
                path.name != f"W.DmProvider.{version}.nupkg"):
            fail("nuspec_identity_or_version_invalid")
        license_element = metadata.find("n:license", ns)
        if license_element is None or license_element.get("type") != "expression" or license_element.text != "Apache-2.0":
            fail("nuspec_identity_or_license_invalid")
        repository = metadata.find("n:repository", ns)
        if repository is not None and repository.get("commit"):
            fail("unverified_repository_commit")
        dependencies = metadata.find("n:dependencies", ns)
        if dependencies is None:
            fail("nuspec_dependencies_missing")
        groups = dependencies.findall("n:group", ns)
        if (len(groups) != 1 or groups[0].get("targetFramework") != "net10.0" or
                list(groups[0]) or len(list(dependencies)) != 1):
            fail("nuspec_dependency_graph_invalid")
        if "THIRD-PARTY-NOTICES.md" not in names:
            fail("third_party_notice_missing")
    source_sha, source_count = source_hash(source_root)
    result = {"schema_version": 1, "status": "package_verified", "package_name": path.name,
              "version": version, "package_sha256": digest(path.read_bytes()), "assets": assets,
              "source_sha256": source_sha, "source_file_count": source_count, "entries": sorted(names)}
    out = pathlib.Path(out)
    out.parent.mkdir(parents=True, exist_ok=True)
    if out.exists():
        fail("manifest_already_exists")
    out.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n")
    print(json.dumps(result, ensure_ascii=False, separators=(",", ":")))


def verify(path, manifest_path):
    path = pathlib.Path(path)
    saved = json.loads(pathlib.Path(manifest_path).read_text())
    if (digest(path.read_bytes()) != saved.get("package_sha256") or
            path.name != f"W.DmProvider.{saved.get('version')}.nupkg"):
        fail("package_changed")
    with zipfile.ZipFile(path) as archive:
        for name, expected in saved.get("assets", {}).items():
            if digest(archive.read(name)) != expected:
                fail("asset_changed")
    print(json.dumps({"schema_version": 1, "status": "candidate_unchanged"}))


def consumer(run_dir, version):
    run_dir = pathlib.Path(run_dir).resolve()
    assets = json.loads((run_dir / "obj/project.assets.json").read_text())
    deps = json.loads((run_dir / "app/ProductPackageProbe.deps.json").read_text())
    key = f"W.DmProvider/{version}"
    if (set(assets.get("libraries", {})) != {key} or
            set(assets.get("packageFolders", {})) != {str(run_dir / "nuget-cache")} or
            set(assets.get("project", {}).get("restore", {}).get("sources", {})) != {str(run_dir)}):
        fail("restore_graph_invalid")
    if (set(deps.get("libraries", {})) != {key, "ProductPackageProbe/1.0.0"} or
            deps["libraries"][key].get("type") != "package"):
        fail("runtime_graph_invalid")
    targets = list(deps.get("targets", {}).values())
    if (len(targets) != 1 or set(targets[0]) != {key, "ProductPackageProbe/1.0.0"} or
            targets[0]["ProductPackageProbe/1.0.0"].get("dependencies") != {"W.DmProvider": version} or
            targets[0][key].get("dependencies")):
        fail("runtime_dependency_invalid")
    print(json.dumps({"schema_version": 1, "status": "consumer_graph_verified", "version": version}))


def api(mapping_path, original_path, exported_path):
    mapping = json.loads(pathlib.Path(mapping_path).read_text())
    originals = pathlib.Path(original_path).read_text().splitlines()
    actual = pathlib.Path(exported_path).read_text().splitlines()
    if mapping.get("schema_version") != 1 or len(originals) != 3651 or len(set(originals)) != 3651:
        fail("original_or_map_schema_invalid")
    entries = mapping.get("entries")
    if not isinstance(entries, list) or len(entries) != 3651:
        fail("api_entry_count_invalid")
    old = [x.get("old_signature") for x in entries]
    if len(set(old)) != len(old) or set(old) != set(originals):
        fail("api_old_coverage_invalid")
    if any(x.get("classification") not in {"preserved", "obsolete", "internalized", "unsupported"}
           or not x.get("verification_state") or not x.get("reason") for x in entries):
        fail("api_record_incomplete")
    if len(actual) != len(set(actual)):
        fail("exported_api_invalid")
    mapped = [x.get("new_signature", x.get("new_api")) for x in entries
              if x.get("new_signature", x.get("new_api"))]
    additions = mapping.get("new_api", mapping.get("added_api", []))
    if not isinstance(additions, list):
        fail("added_api_invalid")
    additions = [x.get("signature") if isinstance(x, dict) else x for x in additions]
    if len(mapped + additions) != len(set(mapped + additions)) or set(mapped + additions) != set(actual):
        fail("new_api_coverage_invalid")
    print(json.dumps({"schema_version": 1, "status": "api_coverage_verified", "old_count": len(old),
                      "new_count": len(actual), "added_count": len(additions)}, separators=(",", ":")))


if __name__ == "__main__":
    if len(sys.argv) == 6 and sys.argv[1] == "package":
        package(*sys.argv[2:])
    elif len(sys.argv) == 3 and sys.argv[1] == "source":
        print(source_hash(sys.argv[2])[0])
    elif len(sys.argv) == 4 and sys.argv[1] == "verify":
        verify(sys.argv[2], sys.argv[3])
    elif len(sys.argv) == 4 and sys.argv[1] == "consumer":
        consumer(sys.argv[2], sys.argv[3])
    elif len(sys.argv) == 5 and sys.argv[1] == "api":
        api(*sys.argv[2:])
    else:
        fail("usage")
