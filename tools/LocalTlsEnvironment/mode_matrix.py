#!/usr/bin/env python3
"""Run only the allowlisted modes on the owned T07 container, restoring mode 1.

The script captures structured, redacted child results. A finally block always
attempts mode-1 restoration and verifies an actual TEST TLS login afterward.
"""

from __future__ import annotations

import json
import os
from pathlib import Path
import signal
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[2]
PROVISION = ROOT / "tools" / "LocalTlsEnvironment" / "provision.py"
WRAPPER = ROOT / "scripts" / "with-dameng-tls-test.sh"
PROBE = ROOT / "tools" / "SecurityProbe" / "bin" / "Debug" / "net10.0" / "SecurityProbe.dll"


class MatrixError(Exception):
    pass


def command(folder: Path, stage: str, arguments: list[str]) -> dict:
    try:
        completed = subprocess.run(arguments, cwd=ROOT, text=True, capture_output=True,
                                   check=False, timeout=180)
    except subprocess.TimeoutExpired:
        raise MatrixError(stage + "_timeout") from None
    # These entrypoints emit only schema-checked JSON and never raw exceptions.
    # stderr is deliberately not written to a file or the terminal.
    try:
        row = json.loads(completed.stdout)
    except json.JSONDecodeError:
        raise MatrixError(stage + "_result_missing") from None
    path = folder / (stage + ".json")
    path.write_text(json.dumps(row, separators=(",", ":")) + "\n", encoding="utf-8")
    path.chmod(0o600)
    if completed.returncode != 0:
        raise MatrixError(stage + "_exit_" + str(completed.returncode))
    return row


def switch(folder: Path, target: int) -> None:
    row = command(folder, "switch_mode_" + str(target),
                  [sys.executable, str(PROVISION), "switch-mode", str(target)])
    if row.get("enable_encrypt") != target or row.get("status") not in ("selected", "already_selected"):
        raise MatrixError("switch_mode_" + str(target) + "_invalid")


def probe(folder: Path, stage: str, label: str, mode: int, *, rejection: bool = False) -> None:
    row = command(folder, stage, [str(WRAPPER), "dotnet", str(PROBE), label])
    expected_wire_mode = 4 if label == "mode5-smoke" else mode
    if row.get("task") != "T07" or row.get("implementation") != "W" or row.get("probe") != label or \
       row.get("server_configured_encrypt_mode") != mode or row.get("negotiated_encrypt_mode") != expected_wire_mode or \
       row.get("connection_closed_verified") is not True or \
       row.get("first_open_successful_tcp_connections") != 1 or row.get("created_tcp_sockets") != 1 or \
       row.get("disposed_tcp_sockets") != 1 or row.get("final_database_state") != "no_object_created":
        raise MatrixError(stage + "_envelope_invalid")
    if rejection:
        if row.get("status") != "tls_rejection_verified" or row.get("opening_opcodes") != [200] or \
           row.get("login_not_encoded") is not True or row.get("failure_closed_before_cleanup") is not True or \
           row.get("successful_tls_upgrades") != 0 or row.get("failed_tls_upgrades") != (0 if mode == 2 else 1):
            raise MatrixError(stage + "_rejection_invalid")
    else:
        if row.get("status") != "tls_smoke_verified" or row.get("server_account_verified") is not True or \
           row.get("query_verified") is not True or row.get("first_open_successful_tls_upgrades") != 1 or \
           row.get("negotiated_tls_protocol") not in ("Tls12", "Tls13"):
            raise MatrixError(stage + "_success_invalid")


def main() -> int:
    os.umask(0o077)
    def interrupted(_signal: int, _frame: object) -> None:
        raise MatrixError("matrix_interrupted")
    signal.signal(signal.SIGINT, interrupted)
    signal.signal(signal.SIGTERM, interrupted)
    if len(sys.argv) != 2:
        print(json.dumps({"task": "T07", "mode": "matrix", "status": "rejected", "reason": "usage"}))
        return 64
    folder = Path(sys.argv[1]).resolve()
    folder.mkdir(parents=True, exist_ok=True)
    folder.chmod(0o700)
    failure: str | None = None
    restored = False
    checks = {"mode2_auth_only_rejected": False, "mode4_tls_verified": False,
              "mode4_unknown_ca_rejected": False, "server_mode5_via_wire4_verified": False}
    try:
        baseline = command(folder, "baseline_verify_mode1", [sys.executable, str(PROVISION), "verify-tls"])
        if baseline.get("enable_encrypt") != 1 or baseline.get("status") != "official_tls_test_verified":
            raise MatrixError("baseline_mode1_invalid")
        switch(folder, 2)
        probe(folder, "mode2_auth_only_reject", "auth-only-reject", 2, rejection=True)
        checks["mode2_auth_only_rejected"] = True
        switch(folder, 4)
        probe(folder, "mode4_tls_smoke", "mode4-smoke", 4)
        checks["mode4_tls_verified"] = True
        probe(folder, "mode4_unknown_ca_reject", "unknown-ca-mode4", 4, rejection=True)
        checks["mode4_unknown_ca_rejected"] = True
        switch(folder, 5)
        probe(folder, "mode5_tls_smoke", "mode5-smoke", 5)
        checks["server_mode5_via_wire4_verified"] = True
    except MatrixError as error:
        failure = str(error)
    finally:
        try:
            switch(folder, 1)
            official = command(folder, "restored_official_mode1", [sys.executable, str(PROVISION), "verify-tls"])
            if official.get("enable_encrypt") != 1 or official.get("status") != "official_tls_test_verified":
                raise MatrixError("restore_official_login_invalid")
            probe(folder, "restored_w_mode1", "smoke", 1)
            restored = True
        except MatrixError as error:
            failure = (failure + ";" if failure else "") + str(error)
    result = {"schema_version": 1, "task": "T07", "mode": "matrix",
              "status": "real_verified" if failure is None and restored else "rejected",
              **checks,
              "server_configured_mode": 5 if checks["server_mode5_via_wire4_verified"] else None,
              "mode5_negotiated_encrypt_mode": 4 if checks["server_mode5_via_wire4_verified"] else None,
              "mode1_restored_and_logged_in": restored,
              "run_dir": str(folder)}
    if failure: result["reason"] = failure
    path = folder / "matrix-result.json"
    path.write_text(json.dumps(result, separators=(",", ":")) + "\n", encoding="utf-8")
    path.chmod(0o600)
    print(json.dumps(result, separators=(",", ":")))
    return 0 if result["status"] == "real_verified" else 1


if __name__ == "__main__":
    raise SystemExit(main())
