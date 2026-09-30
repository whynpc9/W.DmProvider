#!/usr/bin/env python3
"""Build-only by default; the execution mode is reserved for an approved maintenance window."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shlex
import stat
import subprocess
import sys
import tempfile

REPO = Path(__file__).absolute().parents[2]
TOOL = REPO / "tools/LocalTestPermissions"
OUTPUT = REPO / ".local/maintenance/LocalTestPermissions"
BIN = OUTPUT / "bin"
PRIVATE_VARIABLE = "WDM_PROVIDER_MAINTENANCE_CONNECTION_STRING"
SOURCE_VARIABLE = "DAMENG_ADMIN_CONNECTION_STRING"
TEST_SOURCE_VARIABLE = "DAMENG_TEST_CONNECTION_STRING"
TEST_PRIVATE_VARIABLE = "WDM_PROVIDER_TEST_INSPECTION_CONNECTION_STRING"
ROLE = "SOI"
SOURCES = ("Program.cs", "LocalTestPermissions.csproj", "run.py", "README.md")
ARTIFACTS = ("LocalTestPermissions.dll", "LocalTestPermissions.deps.json",
             "LocalTestPermissions.runtimeconfig.json", "W.DmProvider.dll")
PROOF_KEYS = {"AdminConfiguredSa", "AdminIdentitySa", "RoleReadBefore", "RolePresentBefore",
              "GrantAttempted", "GrantAcknowledged", "RoleReadAfter", "RolePresentAfter",
              "RoleWithoutAdminOption", "AdminClosed"}
CODES = {"arguments_rejected", "configuration_rejected", "connect_failed", "admin_identity_rejected",
         "role_read_before_failed", "existing_admin_option_rejected", "grant_failed",
         "role_read_after_failed", "role_postcondition_rejected", "completed", "admin_close_failed"}
INSPECTION_CODES = {"test_configuration_rejected", "test_connect_failed", "test_identity_rejected",
                    "test_schema_rejected", "role_inspection_failed", "role_row_limit_rejected",
                    "inspection_completed", "test_close_failed"}
INSPECTION_PROOF_KEYS = {"TestConfigured", "TestIdentity", "TestSchema", "RoleRowsRead", "TestClosed"}
FLAG_TEXT = {"NO", "YES", "N", "Y", "0", "1", "是", "否"}


class Rejected(Exception):
    def __init__(self, code):
        self.code = code


class SafeParser(argparse.ArgumentParser):
    def error(self, _message):
        raise Rejected("arguments_rejected")


def emit(code, success=False, **extra):
    print(json.dumps({"TargetRole": ROLE, "Code": code, "Success": success, **extra}, separators=(",", ":")))


def clean_environment():
    return {name: value for name, value in os.environ.items()
            if not name.upper().startswith("DAMENG_") and name not in (PRIVATE_VARIABLE, TEST_PRIVATE_VARIABLE)}


def digest(path):
    if path.is_symlink() or not path.is_file():
        raise Rejected("build_manifest_rejected")
    return hashlib.sha256(path.read_bytes()).hexdigest()


def build():
    # This branch never opens/stat-checks the admin secret or its directory.
    OUTPUT.mkdir(parents=True, exist_ok=True, mode=0o700)
    env = clean_environment()
    with tempfile.TemporaryDirectory(prefix="wdm-maintenance-dotnet-") as cli_home:
        env.update(DOTNET_CLI_HOME=cli_home, DOTNET_SKIP_FIRST_TIME_EXPERIENCE="1",
                   DOTNET_CLI_TELEMETRY_OPTOUT="1")
        completed = subprocess.run(
            ["dotnet", "build", str(TOOL / "LocalTestPermissions.csproj"), "-c", "Release",
             "-o", str(BIN), "-m:1", "/nodeReuse:false", "/p:UseSharedCompilation=false",
             "--disable-build-servers"], cwd=REPO, env=env,
            stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=300, check=False)
    if completed.returncode:
        emit("build_failed", ExitCode=completed.returncode, Proof={"SecretRead": False})
        return 1
    manifest = {"sources": {name: digest(TOOL / name) for name in SOURCES},
                "artifacts": {name: digest(BIN / name) for name in ARTIFACTS}}
    (OUTPUT / "build-manifest.json").write_text(json.dumps(manifest, separators=(",", ":")) + "\n")
    emit("build_completed", True, Proof={"SecretRead": False, "BuildManifestWritten": True})
    return 0


def verify_build():
    manifest_path = OUTPUT / "build-manifest.json"
    if manifest_path.is_symlink():
        raise Rejected("build_manifest_rejected")
    manifest = json.loads(manifest_path.read_text())
    expected = {"sources": {name: digest(TOOL / name) for name in SOURCES},
                "artifacts": {name: digest(BIN / name) for name in ARTIFACTS}}
    if manifest != expected:
        raise Rejected("build_manifest_rejected")


def parse_secret(data):
    lexer = shlex.shlex(data, posix=True)
    lexer.whitespace_split = True
    lexer.commenters = "#"
    tokens = list(lexer)
    if tokens and tokens[0] == "export":
        tokens = tokens[1:]
    if len(tokens) != 1:
        raise Rejected("secret_assignment_rejected")
    name, separator, value = tokens[0].partition("=")
    if name != SOURCE_VARIABLE or not separator or not value.strip():
        raise Rejected("secret_assignment_rejected")
    # shlex treats contents as data. There is no source/eval, expansion or shell execution.
    return value


def read_admin_secret():
    relative = Path(".local/secrets/dameng-admin.env")
    secret = REPO / relative
    for path in (REPO, REPO / ".local", secret.parent, secret):
        if stat.S_ISLNK(path.lstat().st_mode):
            raise Rejected("secret_symlink_rejected")
    ignored = subprocess.run(["git", "check-ignore", "--quiet", "--", str(relative)],
                             cwd=REPO, env=clean_environment(), stdout=subprocess.DEVNULL,
                             stderr=subprocess.DEVNULL, check=False, timeout=10)
    if ignored.returncode != 0:
        raise Rejected("secret_not_gitignored")
    directory_flags = os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW | os.O_CLOEXEC
    repo_fd = os.open(REPO, directory_flags)
    try:
        local_fd = os.open(".local", directory_flags, dir_fd=repo_fd)
        try:
            parent_fd = os.open("secrets", directory_flags, dir_fd=local_fd)
            try:
                parent = os.fstat(parent_fd)
                if not stat.S_ISDIR(parent.st_mode) or stat.S_IMODE(parent.st_mode) != 0o700 or parent.st_uid != os.getuid():
                    raise Rejected("secret_parent_permissions_rejected")
                flags = os.O_RDONLY | os.O_NOFOLLOW | os.O_CLOEXEC | os.O_NONBLOCK
                fd = os.open(secret.name, flags, dir_fd=parent_fd)
                try:
                    info = os.fstat(fd)
                    if not stat.S_ISREG(info.st_mode) or stat.S_IMODE(info.st_mode) != 0o600 or info.st_uid != os.getuid():
                        raise Rejected("secret_permissions_rejected")
                    if info.st_size > 65536:
                        raise Rejected("secret_size_rejected")
                    with os.fdopen(fd, "r", encoding="utf-8", closefd=False) as stream:
                        text = stream.read(65537)
                    if len(text) > 65536:
                        raise Rejected("secret_size_rejected")
                    return parse_secret(text)
                finally:
                    os.close(fd)
            finally:
                os.close(parent_fd)
        finally:
            os.close(local_fd)
    finally:
        os.close(repo_fd)


def validate_outcome(outcome, returncode):
    if not isinstance(outcome, dict) or set(outcome) != {"TargetRole", "Code", "ErrorType", "ProviderCode", "Success", "Proof"}:
        raise Rejected("child_output_rejected")
    if outcome["TargetRole"] != ROLE or outcome["Code"] not in CODES or type(outcome["Success"]) is not bool:
        raise Rejected("child_output_rejected")
    if outcome["ErrorType"] is not None and (not isinstance(outcome["ErrorType"], str) or
            re.fullmatch(r"[A-Za-z0-9_.+`]+", outcome["ErrorType"]) is None):
        raise Rejected("child_output_rejected")
    if outcome["ProviderCode"] is not None and type(outcome["ProviderCode"]) is not int:
        raise Rejected("child_output_rejected")
    proof = outcome["Proof"]
    if not isinstance(proof, dict) or set(proof) != PROOF_KEYS or any(type(value) is not bool for value in proof.values()):
        raise Rejected("child_output_rejected")
    if outcome["Success"] != (returncode == 0):
        raise Rejected("child_output_rejected")
    if outcome["Success"] and (outcome["Code"] != "completed" or not all(proof[name] for name in
            ("AdminConfiguredSa", "AdminIdentitySa", "RoleReadBefore", "RoleReadAfter", "RolePresentAfter",
             "RoleWithoutAdminOption", "AdminClosed"))):
        raise Rejected("child_output_rejected")
    return outcome


def execute(approval_granted):
    if not approval_granted:
        raise Rejected("explicit_approval_required")
    verify_build()  # Reject stale/changed compiled inputs before reading credentials.
    connection_string = read_admin_secret()
    env = clean_environment()
    env[PRIVATE_VARIABLE] = connection_string
    try:
        with tempfile.TemporaryDirectory(prefix="wdm-maintenance-execute-") as cli_home:
            env.update(DOTNET_CLI_HOME=cli_home, DOTNET_SKIP_FIRST_TIME_EXPERIENCE="1",
                       DOTNET_CLI_TELEMETRY_OPTOUT="1")
            completed = subprocess.run(["dotnet", str(BIN / "LocalTestPermissions.dll"), "grant-soi-to-test"],
                                       cwd=REPO, env=env, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL,
                                       text=True, check=False, timeout=120)
        outcome = validate_outcome(json.loads(completed.stdout), completed.returncode)
        print(json.dumps(outcome, separators=(",", ":")))
        return 0 if outcome["Success"] else 1
    finally:
        env.pop(PRIVATE_VARIABLE, None)


def validate_inspection(outcome, returncode):
    keys = {"TargetRole", "Mode", "Code", "ErrorType", "ProviderCode", "Success", "Proof",
            "FieldClrType", "RowsObserved", "RowCountComplete", "Flags"}
    if not isinstance(outcome, dict) or set(outcome) != keys:
        raise Rejected("child_output_rejected")
    if outcome["TargetRole"] != ROLE or outcome["Mode"] != "test_readonly_inspect" or outcome["Code"] not in INSPECTION_CODES:
        raise Rejected("child_output_rejected")
    for name in ("ErrorType", "FieldClrType"):
        if outcome[name] is not None and (not isinstance(outcome[name], str) or
                re.fullmatch(r"[A-Za-z0-9_.+`]+", outcome[name]) is None):
            raise Rejected("child_output_rejected")
    if outcome["ProviderCode"] is not None and type(outcome["ProviderCode"]) is not int:
        raise Rejected("child_output_rejected")
    if type(outcome["Success"]) is not bool or outcome["Success"] != (returncode == 0):
        raise Rejected("child_output_rejected")
    proof = outcome["Proof"]
    if not isinstance(proof, dict) or set(proof) != INSPECTION_PROOF_KEYS or any(type(value) is not bool for value in proof.values()):
        raise Rejected("child_output_rejected")
    if type(outcome["RowsObserved"]) is not int or not 0 <= outcome["RowsObserved"] <= 17 or type(outcome["RowCountComplete"]) is not bool:
        raise Rejected("child_output_rejected")
    flags = outcome["Flags"]
    if not isinstance(flags, list) or len(flags) > 16 or len(flags) > outcome["RowsObserved"]:
        raise Rejected("child_output_rejected")
    for flag in flags:
        if not isinstance(flag, dict) or set(flag) != {"ValueClrType", "Value", "Sha256"} or not isinstance(flag["ValueClrType"], str):
            raise Rejected("child_output_rejected")
        if re.fullmatch(r"[A-Za-z0-9_.+`]+", flag["ValueClrType"]) is None:
            raise Rejected("child_output_rejected")
        value, sha = flag["Value"], flag["Sha256"]
        if sha is not None:
            if value is not None or not isinstance(sha, str) or re.fullmatch(r"[a-f0-9]{64}", sha) is None:
                raise Rejected("child_output_rejected")
        elif not (type(value) is bool or type(value) is int and -(2**31) <= value < 2**31 or
                  isinstance(value, str) and len(value) <= 8 and value in FLAG_TEXT):
            raise Rejected("child_output_rejected")
    if outcome["Success"] and (outcome["Code"] != "inspection_completed" or not outcome["RowCountComplete"] or
            len(flags) != outcome["RowsObserved"] or not all(proof.values())):
        raise Rejected("child_output_rejected")
    return outcome


def inspect_as_test():
    verify_build()
    # Only the TEST wrapper's inherited variable. No admin file access or stat calls.
    raw = os.environ.get(TEST_SOURCE_VARIABLE, "")
    if not raw.strip():
        raise Rejected("test_wrapper_required")
    env = clean_environment()
    env[TEST_PRIVATE_VARIABLE] = raw
    try:
        with tempfile.TemporaryDirectory(prefix="wdm-test-inspect-") as cli_home:
            env.update(DOTNET_CLI_HOME=cli_home, DOTNET_SKIP_FIRST_TIME_EXPERIENCE="1",
                       DOTNET_CLI_TELEMETRY_OPTOUT="1")
            completed = subprocess.run(["dotnet", str(BIN / "LocalTestPermissions.dll"), "inspect-soi-as-test"],
                                       cwd=REPO, env=env, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL,
                                       text=True, check=False, timeout=120)
        outcome = validate_inspection(json.loads(completed.stdout), completed.returncode)
        print(json.dumps(outcome, separators=(",", ":")))
        return 0 if outcome["Success"] else 1
    finally:
        env.pop(TEST_PRIVATE_VARIABLE, None)


def main():
    try:
        parser = SafeParser(description="Fixed SOI grant maintenance; user approval is required for execute.")
        parser.add_argument("mode", nargs="?", default="build", choices=("build", "execute", "inspect"))
        parser.add_argument("--approval-granted", action="store_true")
        arguments = parser.parse_args()
        if arguments.mode == "build":
            if arguments.approval_granted:
                raise Rejected("arguments_rejected")
            return build()
        if arguments.mode == "inspect":
            if arguments.approval_granted:
                raise Rejected("arguments_rejected")
            return inspect_as_test()
        return execute(arguments.approval_granted)
    except Rejected as error:
        emit(error.code, ErrorType=type(error).__name__)
        return 1
    except Exception as error:
        emit("maintenance_failed", ErrorType=type(error).__name__)
        return 1


if __name__ == "__main__":
    sys.exit(main())
