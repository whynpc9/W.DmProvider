#!/usr/bin/env python3
"""Map the fixed R API list to an independently inspected W assembly API list."""

import argparse
import hashlib
import json
import re
from pathlib import Path


CORE_TYPES = {
    "Dm.DmConnection", "Dm.DmCommand", "Dm.DmDataReader", "Dm.DmParameter",
    "Dm.DmParameterCollection", "Dm.DmTransaction", "Dm.DmConnectionStringBuilder",
    "Dm.DmClientFactory", "Dm.DmException", "Dm.DmDbType",
}


def translated(signature: str) -> str:
    result = re.sub(r"(?<![\w.])Dm\.", "W.Dm.", signature)
    result = re.sub(r"(?<![\w.])A\.", "W.Dm.Internal.Legacy.A.", result)
    result = re.sub(r"(?<![\w.])NetTaste\.", "W.Dm.Internal.Legacy.NetTaste.", result)
    return re.sub(r"(?<![\w.])DmSqlRow(Updated|Updating)EventArgs\b",
                  r"W.Dm.DmSqlRow\1EventArgs", result)


def declaring_type(signature: str) -> str:
    kind, value = signature.split("|", 1)
    if kind == "T":
        return value
    if "::" not in value:
        raise ValueError(f"member without declaring type: {signature}")
    return value.split("::", 1)[0].split()[-1]


def digest(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("old_api", type=Path)
    parser.add_argument("new_api", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    old = args.old_api.read_text().splitlines()
    new = args.new_api.read_text().splitlines()
    if len(old) != 3651 or len(set(old)) != 3651:
        raise SystemExit("expected 3651 unique R API entries")
    if len(new) != len(set(new)):
        raise SystemExit("duplicate W API entries")
    expected = [translated(signature) for signature in old]
    if len(set(expected)) != 3651 or set(expected) != set(new):
        missing, extra = set(expected) - set(new), set(new) - set(expected)
        raise SystemExit(f"W API mismatch: {len(missing)} missing, {len(extra)} extra")
    entries = []
    for old_signature, new_signature in zip(old, expected):
        owner = declaring_type(old_signature)
        core = owner in CORE_TYPES
        classification = "preserved" if core else "obsolete"
        entries.append({
            "old_signature": old_signature,
            "new_api": new_signature,
            "classification": classification,
            "verification_state": "untested",
            "static_surface_present": True,
            "deprecation_mechanism": None if core else "documentation_only",
            "runtime_guard": "pending_T04" if (
                owner in {"Dm.DmConnection", "Dm.DmConnectionStringBuilder"} and
                any(term in old_signature.lower() for term in
                    ("reconnect", "readwritesplit", "rwsplit", "enlist", "pool", "xa", "tls", "ssl"))
            ) else None,
            "reason": (
                "Primary ADO.NET surface retained under W.Dm; behavior is not accepted by this static mapping."
                if core else
                "Callable legacy compatibility surface retained under W identity; documented as deprecated, with no Obsolete attribute or behavior acceptance in T03."
            ),
        })
    result = {
        "schema_version": 1,
        "source_api_sha256": digest(args.old_api),
        "product_api_sha256": digest(args.new_api),
        "classification_policy": "T03 static surface policy; obsolete is documentation-only compatibility, not an attribute or disabled runtime path.",
        "verification_policy": "All entries are untested for behavior; exact W binary API membership was checked separately from runtime acceptance.",
        "entries": entries,
        "new_api": [],
    }
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n")
    print(json.dumps({"mapped": len(entries), "preserved": sum(e["classification"] == "preserved" for e in entries),
                      "obsolete": sum(e["classification"] == "obsolete" for e in entries),
                      "internalized": 0, "unsupported": 0, "new_api": 0}))


if __name__ == "__main__":
    main()
