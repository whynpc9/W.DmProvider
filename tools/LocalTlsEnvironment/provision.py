#!/usr/bin/env python3
"""Provision only the isolated local T07 Dameng Docker instance.

Commands never accept passwords on argv and never print raw database, Docker,
or OpenSSL output. Existing project containers and the shared Dameng instance
are outside this tool's scope.
"""

from __future__ import annotations

import hashlib
import json
import os
from pathlib import Path
import re
import secrets
import socket
import string
import subprocess
import sys
import time

ROOT = Path(__file__).resolve().parents[2]
LOCAL = ROOT / ".local" / "t07"
SECRETS = ROOT / ".local" / "secrets"
BOOTSTRAP = SECRETS / "dameng-tls-bootstrap.env"
TEST_SECRET = SECRETS / "dameng-tls-test.env"
MEDIA = LOCAL / "media" / "dm8_20241022_x86_rh6_64_single.tar"
DATA = LOCAL / "data"
CERTS = LOCAL / "certs"
SERVER_CERTS = CERTS / "server_ssl"
CLIENT_CERTS = CERTS / "client_ssl" / "WDM_PROVIDER_TEST"
LOGS = LOCAL / "logs"
IDENTITY = LOCAL / "container-identity.json"
IMAGE = "dm8_single:dm8_20241022_rev244896_x86_rh6_64"
CONTAINER = "wdm-provider-tls-t07"
SCOPE_LABEL = "org.wdmprovider.scope=t07-local-tls"
EXPECTED_MEDIA_SHA256 = "97cc976b618e0eb75a20c831fb4e258c74ccc574ffa3e59b187c0c9bb90f019c"
HOST_PORT = 15236
PRIVILEGES = (
    "CREATE SESSION", "CREATE TABLE", "CREATE INDEX", "CREATE VIEW",
    "CREATE SEQUENCE", "CREATE PROCEDURE", "CREATE TRIGGER",
)


class ProvisionError(Exception):
    pass


def safe_dir(path: Path) -> None:
    path.mkdir(parents=True, exist_ok=True)
    path.chmod(0o700)


def write_secret_once(path: Path, name: str) -> None:
    if path.exists():
        if path.stat().st_mode & 0o777 != 0o600:
            raise ProvisionError(f"secret_mode_invalid:{path.name}")
        return
    alphabet = string.ascii_letters + string.digits
    # Explicitly include each class required by dminit password strength rules.
    value = "T7a" + "".join(secrets.choice(alphabet) for _ in range(27))
    fd = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
    try:
        with os.fdopen(fd, "w", encoding="ascii") as output:
            output.write(f"{name}={value}\n")
    except BaseException:
        path.unlink(missing_ok=True)
        raise


def read_secret(path: Path, name: str) -> str:
    if not path.is_file() or path.stat().st_mode & 0o777 != 0o600:
        raise ProvisionError(f"secret_missing_or_mode_invalid:{path.name}")
    lines = path.read_text(encoding="ascii").splitlines()
    if len(lines) != 1 or not lines[0].startswith(name + "="):
        raise ProvisionError(f"secret_format_invalid:{path.name}")
    value = lines[0][len(name) + 1:]
    if not re.fullmatch(r"[A-Za-z0-9]{20,40}", value):
        raise ProvisionError(f"secret_value_invalid:{path.name}")
    return value


def redact(raw: str, *passwords: str) -> str:
    text = raw
    for password in passwords:
        if password:
            text = text.replace(password, "[REDACTED]")
    lines = []
    for line in text.splitlines():
        if re.search(r"(?i)(password|pwd=|identified by|(?:login|connect)\s+(sysdba|wdm_provider_test)/)", line):
            lines.append("[REDACTED credential line]")
        else:
            lines.append(line)
    return "\n".join(lines) + "\n"


def run(stage: str, command: list[str], *, input_text: str | None = None) -> str:
    try:
        completed = subprocess.run(command, input=input_text, text=True, capture_output=True,
                                   check=False, timeout=120)
    except subprocess.TimeoutExpired:
        raise ProvisionError(f"{stage}_timeout") from None
    passwords = []
    for path, name in ((BOOTSTRAP, "SYSDBA_PWD"), (TEST_SECRET, "DAMENG_TLS_TEST_PASSWORD")):
        if path.exists():
            passwords.append(read_secret(path, name))
    sanitized = redact(completed.stdout + "\n" + completed.stderr, *passwords)
    safe_dir(LOGS)
    log = LOGS / f"{stage}.log"
    log.write_text(sanitized, encoding="utf-8")
    log.chmod(0o600)
    if completed.returncode != 0:
        raise ProvisionError(f"{stage}_failed:exit_{completed.returncode}")
    return sanitized


def container_status() -> dict[str, str] | None:
    completed = subprocess.run(["docker", "inspect", CONTAINER, "--format",
        '{"id":"{{.Id}}","image":"{{.Image}}","status":"{{.State.Status}}",'
        '"labels":{{json .Config.Labels}},"ports":{{json .HostConfig.PortBindings}},'
        '"mounts":{{json .Mounts}}}'], text=True, capture_output=True, check=False, timeout=10)
    if completed.returncode != 0:
        return None
    found = json.loads(completed.stdout)
    image = subprocess.run(["docker", "image", "inspect", IMAGE, "--format", "{{.Id}}"],
                           text=True, capture_output=True, check=False, timeout=10)
    if image.returncode != 0 or found["image"] != image.stdout.strip():
        raise ProvisionError("container_image_identity_invalid")
    bindings = found["ports"]
    expected = [{"HostIp": "127.0.0.1", "HostPort": str(HOST_PORT)}]
    if bindings.get("5236/tcp") != expected:
        raise ProvisionError("container_port_scope_invalid")
    mounts = {item["Destination"]: item["Source"] for item in found["mounts"] if item.get("Type") == "bind"}
    expected_mounts = {"/opt/dmdbms/data": str(DATA.resolve()),
                       "/opt/dmdbms/bin/server_ssl": str(SERVER_CERTS.resolve()),
                       "/opt/dmdbms/bin/client_ssl": str(CLIENT_CERTS.parent.resolve())}
    if mounts != expected_mounts:
        raise ProvisionError("container_mount_scope_invalid")
    if (found["labels"] or {}).get("org.wdmprovider.scope") == "t07-local-tls":
        owner = "label"
    elif IDENTITY.is_file() and IDENTITY.stat().st_mode & 0o777 == 0o600:
        saved = json.loads(IDENTITY.read_text(encoding="ascii"))
        if saved.get("id") != found["id"] or saved.get("image") != found["image"]:
            raise ProvisionError("container_manifest_identity_invalid")
        owner = "manifest"
    else:
        raise ProvisionError("container_ownership_unverified")
    return {"status": found["status"], "image_id": found["image"], "id": found["id"], "owner": owner}


def adopt_created_container() -> dict[str, object]:
    if IDENTITY.exists():
        raise ProvisionError("container_manifest_already_exists")
    start_log = LOGS / "docker_start_plaintext.log"
    if not start_log.is_file():
        raise ProvisionError("container_creation_log_missing")
    lines = start_log.read_text(encoding="ascii").splitlines()
    returned = next((line.strip() for line in lines if re.fullmatch(r"[0-9a-f]{64}", line.strip())), None)
    if returned is None:
        raise ProvisionError("container_creation_id_missing")
    # The exact returned ID and known image/mount/port must all match. No generic
    # same-name container is adopted.
    details = subprocess.run(["docker", "inspect", CONTAINER, "--format", "{{.Id}}|{{.Image}}"],
                             text=True, capture_output=True, check=False, timeout=10)
    if details.returncode != 0:
        raise ProvisionError("isolated_container_missing")
    current_id, current_image = details.stdout.strip().split("|", 1)
    if current_id != returned:
        raise ProvisionError("container_creation_id_mismatch")
    IDENTITY.write_text(json.dumps({"id": returned, "image": current_image}, separators=(",", ":")) + "\n",
                        encoding="ascii")
    IDENTITY.chmod(0o600)
    try:
        checked = container_status()
    except Exception:
        IDENTITY.unlink(missing_ok=True)
        raise
    return {"container": CONTAINER, "identity": "creation_id_manifest", "status": checked["status"]}


def require_image() -> None:
    if not MEDIA.is_file():
        raise ProvisionError("official_media_missing")
    with MEDIA.open("rb") as source:
        digest = hashlib.file_digest(source, "sha256").hexdigest()
    if digest != EXPECTED_MEDIA_SHA256:
        raise ProvisionError("official_media_sha256_mismatch")
    completed = subprocess.run(
        ["docker", "image", "inspect", IMAGE, "--format", "{{.Architecture}}|{{.Os}}|{{json .Config.Entrypoint}}"],
        text=True, capture_output=True, check=False, timeout=10,
    )
    if completed.returncode != 0 or completed.stdout.strip() != 'amd64|linux|["/opt/startup.sh"]':
        raise ProvisionError("official_image_identity_invalid")


def prepare() -> dict[str, object]:
    require_image()
    safe_dir(SECRETS)
    for folder in (LOCAL, DATA, CERTS, SERVER_CERTS, CLIENT_CERTS, LOGS):
        safe_dir(folder)
    write_secret_once(BOOTSTRAP, "SYSDBA_PWD")
    write_secret_once(TEST_SECRET, "DAMENG_TLS_TEST_PASSWORD")
    return {"media_sha256": EXPECTED_MEDIA_SHA256, "image": IMAGE,
            "bootstrap_secret_mode": "0600", "test_secret_mode": "0600"}


def wait_port(seconds: int = 180) -> None:
    end = time.monotonic() + seconds
    while time.monotonic() < end:
        status = container_status()
        if status is None or status["status"] != "running":
            raise ProvisionError("isolated_container_not_running")
        try:
            with socket.create_connection(("127.0.0.1", HOST_PORT), timeout=1):
                return
        except OSError:
            time.sleep(2)
    raise ProvisionError("isolated_container_port_timeout")


def start_plaintext() -> dict[str, object]:
    prepare()
    if container_status() is not None:
        raise ProvisionError("isolated_container_already_exists")
    with socket.socket() as probe:
        if probe.connect_ex(("127.0.0.1", HOST_PORT)) == 0:
            raise ProvisionError("host_port_already_in_use")
    run("docker_start_plaintext", [
        "docker", "run", "-d", "--platform", "linux/amd64", "--name", CONTAINER,
        "--label", SCOPE_LABEL,
        "--publish", f"127.0.0.1:{HOST_PORT}:5236", "--env-file", str(BOOTSTRAP),
        "--env", "MODE=DMSINGLE", "--env", "PAGE_SIZE=32", "--env", "INSTANCE_NAME=T07DM",
        "--volume", f"{DATA}:/opt/dmdbms/data",
        "--volume", f"{SERVER_CERTS}:/opt/dmdbms/bin/server_ssl",
        "--volume", f"{CLIENT_CERTS.parent}:/opt/dmdbms/bin/client_ssl",
        IMAGE,
    ])
    if not IDENTITY.exists():
        adopt_created_container()
    wait_port()
    return {"container": CONTAINER, "status": "plaintext_port_ready", "host_binding": "127.0.0.1:15236"}


def disql(stage: str, user: str, password: str, statements: str, *, tls: bool = False) -> str:
    if not re.fullmatch(r"[A-Za-z0-9_]+", user):
        raise ProvisionError("disql_user_invalid")
    # disql /nolog reads credentials from stdin, never from argv or a stored SQL file.
    tls_option = "#{SSL_PATH=/opt/dmdbms/bin/client_ssl/WDM_PROVIDER_TEST,SSL_PWD=0}" if tls else ""
    input_text = f"CONNECT {user}/{password}@127.0.0.1:5236{tls_option}\n{statements}\nexit\n"
    output = run(stage, ["docker", "exec", "-i", "--env", "LD_LIBRARY_PATH=/opt/dmdbms/bin",
                         CONTAINER, "/opt/dmdbms/bin/disql", "/nolog"],
                 input_text=input_text)
    if re.search(r"\[-\d+\]|(?i:connection failure|error occurred|execution failed)", output):
        raise ProvisionError(f"{stage}_sql_error")
    return output


def create_user() -> dict[str, object]:
    if container_status() is None:
        raise ProvisionError("isolated_container_missing")
    wait_port()
    admin = read_secret(BOOTSTRAP, "SYSDBA_PWD")
    test = read_secret(TEST_SECRET, "DAMENG_TLS_TEST_PASSWORD")
    commands = (f'CREATE USER WDM_PROVIDER_TEST IDENTIFIED BY "{test}";\n'
                f'GRANT {", ".join(PRIVILEGES)} TO WDM_PROVIDER_TEST;\n'
                "COMMIT;")
    disql("create_tls_test_user", "SYSDBA", admin, commands)
    verification = disql("verify_tls_test_user_plaintext", "WDM_PROVIDER_TEST", test,
                         "SELECT USER FROM DUAL;")
    if "WDM_PROVIDER_TEST" not in verification:
        raise ProvisionError("tls_test_user_identity_unverified")
    return {"test_account": "WDM_PROVIDER_TEST", "schema": "WDM_PROVIDER_TEST",
            "direct_privileges_requested": list(PRIVILEGES), "plaintext_identity_verified": True}


def openssl(stage: str, *args: str) -> None:
    run(stage, ["openssl", *args])


def generate_certs() -> dict[str, object]:
    for folder in (CERTS, SERVER_CERTS, CLIENT_CERTS):
        safe_dir(folder)
    required = [SERVER_CERTS / "ca-cert.pem", SERVER_CERTS / "server-cert.pem",
                SERVER_CERTS / "server-key.pem", CLIENT_CERTS / "ca-cert.pem",
                CLIENT_CERTS / "client-cert.pem", CLIENT_CERTS / "client-key.pem"]
    if any(path.exists() for path in required):
        if not all(path.is_file() for path in required):
            raise ProvisionError("certificate_set_partial")
        return {"certificate_set": "already_present", "server_san": ["DNS:localhost", "IP:127.0.0.1"],
                "client_cn": "WDM_PROVIDER_TEST"}
    ca_key = CERTS / "ca-key.pem"
    ca_cert = CERTS / "ca-cert.pem"
    openssl("ca_key", "genpkey", "-algorithm", "RSA", "-pkeyopt", "rsa_keygen_bits:2048", "-out", str(ca_key))
    openssl("ca_cert", "req", "-new", "-x509", "-sha256", "-days", "365", "-key", str(ca_key),
            "-out", str(ca_cert), "-subj", "/CN=WDM-T07-CA",
            "-addext", "basicConstraints=critical,CA:TRUE",
            "-addext", "keyUsage=critical,keyCertSign,cRLSign")
    server_key = SERVER_CERTS / "server-key.pem"
    server_csr = CERTS / "server.csr"
    server_ext = CERTS / "server.ext"
    server_ext.write_text("subjectAltName=DNS:localhost,IP:127.0.0.1\n"
                          "basicConstraints=critical,CA:FALSE\n"
                          "keyUsage=critical,digitalSignature,keyEncipherment\n"
                          "extendedKeyUsage=serverAuth\n", encoding="ascii")
    server_ext.chmod(0o600)
    openssl("server_key", "genpkey", "-algorithm", "RSA", "-pkeyopt", "rsa_keygen_bits:2048", "-out", str(server_key))
    openssl("server_csr", "req", "-new", "-sha256", "-key", str(server_key), "-out", str(server_csr),
            "-subj", "/CN=localhost")
    openssl("server_cert", "x509", "-req", "-sha256", "-days", "365", "-in", str(server_csr),
            "-CA", str(ca_cert), "-CAkey", str(ca_key), "-CAcreateserial",
            "-out", str(SERVER_CERTS / "server-cert.pem"), "-extfile", str(server_ext))
    client_key = CLIENT_CERTS / "client-key.pem"
    client_csr = CERTS / "client.csr"
    client_ext = CERTS / "client.ext"
    client_ext.write_text("basicConstraints=critical,CA:FALSE\n"
                          "keyUsage=critical,digitalSignature,keyEncipherment\n"
                          "extendedKeyUsage=clientAuth\n", encoding="ascii")
    client_ext.chmod(0o600)
    openssl("client_key", "genpkey", "-algorithm", "RSA", "-pkeyopt", "rsa_keygen_bits:2048", "-out", str(client_key))
    openssl("client_csr", "req", "-new", "-sha256", "-key", str(client_key), "-out", str(client_csr),
            "-subj", "/CN=WDM_PROVIDER_TEST")
    openssl("client_cert", "x509", "-req", "-sha256", "-days", "365", "-in", str(client_csr),
            "-CA", str(ca_cert), "-CAkey", str(ca_key), "-CAcreateserial",
            "-out", str(CLIENT_CERTS / "client-cert.pem"), "-extfile", str(client_ext))
    for destination in (SERVER_CERTS / "ca-cert.pem", CLIENT_CERTS / "ca-cert.pem"):
        destination.write_bytes(ca_cert.read_bytes())
    for path in (ca_key, ca_cert, server_key, server_csr, server_ext, client_key, client_csr, client_ext, *required):
        path.chmod(0o600)
    openssl("verify_server_cert", "verify", "-CAfile", str(ca_cert), str(SERVER_CERTS / "server-cert.pem"))
    openssl("verify_client_cert", "verify", "-CAfile", str(ca_cert), str(CLIENT_CERTS / "client-cert.pem"))
    return {"certificate_set": "generated", "server_san": ["DNS:localhost", "IP:127.0.0.1"],
            "client_cn": "WDM_PROVIDER_TEST", "private_key_files_mode": "0600"}


def enable_tls() -> dict[str, object]:
    status = container_status()
    if status is None or status["status"] != "running":
        raise ProvisionError("isolated_container_not_running")
    if not (SERVER_CERTS / "server-cert.pem").is_file() or not (CLIENT_CERTS / "client-cert.pem").is_file():
        raise ProvisionError("certificate_set_missing")
    # The image runs dmserver as dmdba (uid 1000). Host-created mode-0600
    # server keys remain private but must be owned by that server identity.
    run("server_cert_ownership", ["docker", "exec", "--user", "root", CONTAINER,
                                  "chown", "-R", "dmdba:dinstall", "/opt/dmdbms/bin/server_ssl"])
    admin = read_secret(BOOTSTRAP, "SYSDBA_PWD")
    disql("enable_tls_ini", "SYSDBA", admin,
          "SP_SET_PARA_VALUE(2,'ENABLE_ENCRYPT',1);\n"
          "SP_SET_PARA_VALUE(2,'MIN_SSL_VERSION',771);\nCOMMIT;")
    run("restart_isolated_tls_container", ["docker", "restart", CONTAINER])
    wait_port()
    return verify_tls()


def verify_tls() -> dict[str, object]:
    current = container_status()
    if current is None or current["status"] != "running":
        raise ProvisionError("isolated_container_not_running")
    config = run("read_tls_ini", ["docker", "exec", CONTAINER, "/bin/sh", "-c",
                                  "awk '/^[[:space:]]*(ENABLE_ENCRYPT|MIN_SSL_VERSION)[[:space:]]*=/{print $1,$3}' /opt/dmdbms/data/DAMENG/dm.ini"])
    if not re.search(r"ENABLE_ENCRYPT\s+1\b", config) or not re.search(r"MIN_SSL_VERSION\s+771\b", config):
        raise ProvisionError("tls_ini_readback_invalid")
    wait_official_test_login("verify_tls_test_official")
    return {"container": CONTAINER, "host_binding": "127.0.0.1:15236",
            "enable_encrypt": 1, "min_ssl_version": "TLS1.2", "status": "official_tls_test_verified"}


def wait_official_test_login(stage: str, timeout_seconds: int = 90) -> None:
    test = read_secret(TEST_SECRET, "DAMENG_TLS_TEST_PASSWORD")
    end = time.monotonic() + timeout_seconds
    while time.monotonic() < end:
        try:
            account = disql(stage, "WDM_PROVIDER_TEST", test, "SELECT USER FROM DUAL;", tls=True)
            if "WDM_PROVIDER_TEST" in account and "not connected" not in account:
                return
        except ProvisionError:
            pass
        time.sleep(2)
    raise ProvisionError("official_tls_test_login_timeout")


def switch_mode(target: int) -> dict[str, object]:
    if target not in (1, 2, 4, 5):
        raise ProvisionError("tls_mode_not_allowlisted")
    current = container_status()
    if current is None or (current["status"] != "running" and target != 1):
        raise ProvisionError("isolated_container_not_running")
    ini = "/opt/dmdbms/data/DAMENG/dm.ini"
    ini_file = DATA / "DAMENG" / "dm.ini"
    previous = read_host_ini_mode(ini_file)
    if target != 1 and previous not in (1, 2, 4, 5):
        raise ProvisionError("current_tls_mode_unverified")
    if previous == target and current["status"] == "running":
        return {"container": CONTAINER, "previous_mode": previous, "enable_encrypt": target,
                "status": "already_selected"}
    if current["status"] == "running":
        # target is integer allowlisted above; the command edits exactly the
        # owned instance's ENABLE_ENCRYPT assignment.
        expression = f"s/^([[:space:]]*ENABLE_ENCRYPT[[:space:]]*=[[:space:]]*)[0-9]+/\\1{target}/"
        run("switch_tls_mode_file", ["docker", "exec", "--user", "root", CONTAINER,
                                     "sed", "-i", "-E", expression, ini])
        run("restart_isolated_tls_mode", ["docker", "restart", CONTAINER])
    else:
        # Recovery-only path: the exact container ID, image, port, and DATA bind
        # were checked above even though the container has exited.
        replace_host_ini_mode(ini_file, target)
        run("start_restored_mode1_container", ["docker", "start", CONTAINER])
    wait_port()
    if read_host_ini_mode(ini_file) != target:
        raise ProvisionError("switched_tls_mode_readback_invalid")
    wait_official_test_login("verify_switched_mode_" + str(target))
    return {"container": CONTAINER, "previous_mode": previous, "enable_encrypt": target,
            "status": "selected", "official_test_login_verified": True}


def read_host_ini_mode(path: Path) -> int:
    if path.is_symlink() or not path.is_file() or path.resolve().parent != (DATA / "DAMENG").resolve():
        raise ProvisionError("owned_ini_path_invalid")
    contents = path.read_text(encoding="utf-8")
    matches = re.findall(r"(?m)^[ \t]*ENABLE_ENCRYPT[ \t]*=[ \t]*([0-9]+)(?:[ \t]|$)", contents)
    if len(matches) != 1:
        raise ProvisionError("enable_encrypt_assignment_not_unique")
    return int(matches[0])


def replace_host_ini_mode(path: Path, target: int) -> None:
    read_host_ini_mode(path)
    original = path.read_text(encoding="utf-8")
    replacement, count = re.subn(
        r"(?m)^([ \t]*ENABLE_ENCRYPT[ \t]*=[ \t]*)[0-9]+",
        lambda match: match.group(1) + str(target), original)
    if count != 1:
        raise ProvisionError("enable_encrypt_assignment_not_unique")
    temporary = path.with_name("dm.ini.t07-restore.tmp")
    if temporary.exists() or temporary.is_symlink():
        raise ProvisionError("restore_temporary_path_exists")
    descriptor = os.open(temporary, os.O_WRONLY | os.O_CREAT | os.O_EXCL, path.stat().st_mode & 0o777)
    try:
        with os.fdopen(descriptor, "w", encoding="utf-8") as output:
            output.write(replacement)
            output.flush()
            os.fsync(output.fileno())
        os.replace(temporary, path)
    finally:
        temporary.unlink(missing_ok=True)


def status() -> dict[str, object]:
    current = container_status()
    return {"container": CONTAINER, "container_state": current["status"] if current else "absent",
            "host_binding": "127.0.0.1:15236" if current else None,
            "bootstrap_secret_present": BOOTSTRAP.is_file(), "test_secret_present": TEST_SECRET.is_file(),
            "server_certificate_present": (SERVER_CERTS / "server-cert.pem").is_file()}


def main() -> int:
    os.umask(0o077)
    allowed = {"inspect", "prepare", "start-plaintext", "adopt-created", "create-user",
               "generate-certs", "enable-tls", "verify-tls", "status", "switch-mode"}
    if len(sys.argv) < 2 or sys.argv[1] not in allowed or \
       (sys.argv[1] == "switch-mode" and len(sys.argv) != 3) or \
       (sys.argv[1] != "switch-mode" and len(sys.argv) != 2):
        print(json.dumps({"task": "T07", "status": "rejected", "reason": "usage"}))
        return 64
    mode = sys.argv[1]
    try:
        if mode == "switch-mode":
            if sys.argv[2] not in ("1", "2", "4", "5"):
                raise ProvisionError("tls_mode_not_allowlisted")
            value = switch_mode(int(sys.argv[2]))
        else:
            value = {
                "inspect": lambda: (require_image() or {"media_sha256": EXPECTED_MEDIA_SHA256, "image": IMAGE}),
                "prepare": prepare,
                "start-plaintext": start_plaintext,
                "adopt-created": adopt_created_container,
                "create-user": create_user,
                "generate-certs": generate_certs,
                "enable-tls": enable_tls,
                "verify-tls": verify_tls,
                "status": status,
            }[mode]()
        print(json.dumps({"task": "T07", "mode": mode, "status": "verified", **value}, separators=(",", ":")))
        return 0
    except ProvisionError as error:
        print(json.dumps({"task": "T07", "mode": mode, "status": "rejected", "reason": str(error)}, separators=(",", ":")))
        return 1
    except subprocess.TimeoutExpired:
        print(json.dumps({"task": "T07", "mode": mode, "status": "rejected", "reason": "docker_timeout"}, separators=(",", ":")))
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
