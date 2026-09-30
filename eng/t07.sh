#!/usr/bin/env bash
set -euo pipefail
set +x

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo"
if (( $# != 1 )) || [[ "$1" != offline && "$1" != real && "$1" != matrix ]]; then
  printf '{"schema_version":1,"task":"T07","status":"rejected","reason":"usage","exit_code":64}\n'
  exit 64
fi
mode="$1"
base="$repo/.local/t07"
run="$base/runs/$(date -u +%Y%m%dT%H%M%SZ)-$$-$RANDOM"
mkdir -p "$run" "$base/dotnet-cli-home"
chmod 700 "$run"
export DOTNET_CLI_HOME="$base/dotnet-cli-home"
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export DOTNET_CLI_TELEMETRY_OPTOUT=1
flags=(-m:1 /nodeReuse:false /p:UseSharedCompilation=false --disable-build-servers)

failed() {
  local stage="$1" code="$2"
  printf '{"schema_version":1,"task":"T07","mode":"%s","status":"rejected","stage":"%s","exit_code":%s,"run_dir":"%s"}\n' "$mode" "$stage" "$code" "$run"
  exit "$code"
}
run_logged() {
  local stage="$1"; shift
  local code=0
  "$@" >"$run/$stage.log" 2>&1 || code=$?
  if (( code != 0 )); then failed "$stage" "$code"; fi
}

case "$mode" in
  offline)
    run_logged restore dotnet restore tests/W.DmProvider.SecurityTests/W.DmProvider.SecurityTests.csproj "${flags[@]}"
    run_logged test dotnet test tests/W.DmProvider.SecurityTests/W.DmProvider.SecurityTests.csproj --no-restore \
      --logger "trx;LogFileName=t07.trx" --results-directory "$run" "${flags[@]}"
    run_logged t04_regression dotnet test tests/W.DmProvider.ConfigurationTests/W.DmProvider.ConfigurationTests.csproj \
      --logger "trx;LogFileName=t04-regression.trx" --results-directory "$run" "${flags[@]}"
    run_logged probe_build dotnet build tools/SecurityProbe/SecurityProbe.csproj "${flags[@]}"
    run_logged local_env_recovery python3 tools/LocalTlsEnvironment/test_provision.py
    run_logged t06_regression eng/t06.sh offline
    python3 - "$run" <<'PY' || failed test_result_validation 1
import json,pathlib,sys,xml.etree.ElementTree as ET
folder=pathlib.Path(sys.argv[1]); ns={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
required=('total','executed','passed','failed','error','timeout','aborted','inconclusive',
          'passedButRunAborted','notRunnable','notExecuted','disconnected','warning',
          'completed','inProgress','pending')
results={}
for label,file in (('T07','t07.trx'),('T04-regression','t04-regression.trx')):
    path=folder/file
    if not path.is_file() or not path.stat().st_size: raise SystemExit(f'{label} TRX missing')
    root=ET.parse(path).getroot(); c=root.find('t:ResultSummary/t:Counters',ns)
    if c is None or any(c.get(k) is None for k in required): raise SystemExit(f'{label} counters missing')
    counts={k:int(c.get(k)) for k in required}; summary=root.find('t:ResultSummary',ns)
    if (summary is None or summary.get('outcome') not in ('Completed','Passed') or counts['total']<=0 or
        counts['passed']!=counts['total'] or counts['executed']!=counts['total'] or
        any(v!=0 for k,v in counts.items() if k not in ('total','executed','passed'))):
        raise SystemExit(f'{label} counters invalid')
    results[label]=counts
print(json.dumps({'schema_version':1,'task':'T07','mode':'offline','status':'offline_verified',
                  'integration':'integration_pending','tests':results,
                  't06_ownership_regression':'verified','local_environment_recovery':'verified',
                  'run_dir':str(folder)},separators=(',',':')))
PY
    ;;
  real)
    run_logged probe_build dotnet build tools/SecurityProbe/SecurityProbe.csproj "${flags[@]}"
    for probe in smoke real unknown-ca unknown-ca-plaintext-allowed plaintext-reject; do
      code=0
      if [[ "$probe" == plaintext-reject ]]; then
        scripts/with-dameng-test.sh dotnet tools/SecurityProbe/bin/Debug/net10.0/SecurityProbe.dll "$probe" \
          >"$run/$probe.json" 2>/dev/null || code=$?
      else
        scripts/with-dameng-tls-test.sh dotnet tools/SecurityProbe/bin/Debug/net10.0/SecurityProbe.dll "$probe" \
          >"$run/$probe.json" 2>/dev/null || code=$?
      fi
      [[ -s "$run/$probe.json" ]] || failed "${probe}_result_missing" 1
      (( code == 0 )) || failed "${probe}_probe" "$code"
    done
    run_logged t06_plaintext_regression eng/t06.sh real
    python3 - "$run" <<'PY' || failed real_result_validation 1
import json,pathlib,sys
folder=pathlib.Path(sys.argv[1]); rows={name:json.loads((folder/f'{name}.json').read_text())
    for name in ('smoke','real','unknown-ca','unknown-ca-plaintext-allowed','plaintext-reject')}
for name,row in rows.items():
    if (row.get('task')!='T07' or row.get('implementation')!='W' or row.get('probe')!=name or
        row.get('first_open_successful_tcp_connections')!=1 or row.get('created_tcp_sockets',0)<1 or
        row.get('created_tcp_sockets')!=row.get('disposed_tcp_sockets') or
        row.get('connection_closed_verified') is not True):
        raise SystemExit(f'{name} envelope invalid')
positive=rows['smoke']
if (positive.get('status')!='tls_smoke_verified' or positive.get('integration')!='isolated_local_tls_test_schema' or
    positive.get('explicit_transport')!='RequireTls' or positive.get('final_database_state')!='no_object_created' or
    positive.get('negotiated_encrypt_mode')!=1 or positive.get('negotiated_tls_protocol') not in ('Tls12','Tls13') or
    not positive.get('server_version') or positive.get('revocation_policy')!='NoCheck' or
    positive.get('server_account_verified') is not True or positive.get('query_verified') is not True or
    positive.get('successful_tls_upgrades')!=1 or positive.get('failed_tls_upgrades')!=0):
    raise SystemExit('TLS success envelope invalid')
business=rows['real']
if (business.get('status')!='tls_real_verified' or business.get('integration')!='isolated_local_tls_test_schema' or
    business.get('explicit_transport')!='RequireTls' or business.get('revocation_policy')!='NoCheck' or
    business.get('negotiated_encrypt_mode')!=1 or business.get('negotiated_tls_protocol') not in ('Tls12','Tls13') or
    business.get('first_open_successful_tls_upgrades')!=1 or business.get('failed_tls_upgrades')!=0 or
    business.get('final_database_state')!='random_object_absent' or
    any(business.get(k) is not True for k in ('server_account_verified','query_verified','crud_verified',
        'transaction_verified','blob_96k_readback_verified','cleanup_verified'))):
    raise SystemExit('TLS business envelope invalid')
for name,mode,failed_upgrades,policy in (('unknown-ca',1,1,'RequireTls'),
        ('unknown-ca-plaintext-allowed',1,1,'PlaintextAllowed'),('plaintext-reject',0,0,'RequireTls')):
    row=rows[name]
    if (row.get('status')!='tls_rejection_verified' or row.get('negotiated_encrypt_mode')!=mode or
        row.get('explicit_transport')!=policy or row.get('final_database_state')!='no_object_created' or
        row.get('failure_closed_before_cleanup') is not True or row.get('login_not_encoded') is not True or
        row.get('opening_opcodes')!=[200] or row.get('successful_tls_upgrades')!=0 or
        row.get('failed_tls_upgrades')!=failed_upgrades):
        raise SystemExit(f'{name} rejection envelope invalid')
if any(rows[name].get('integration')!='isolated_local_tls_test_schema' or rows[name].get('revocation_policy')!='NoCheck'
       for name in ('unknown-ca','unknown-ca-plaintext-allowed')) or rows['plaintext-reject'].get('integration')!='existing_plaintext_test_schema':
    raise SystemExit('isolation envelope invalid')
print(json.dumps({'schema_version':1,'task':'T07','mode':'real','status':'real_verified',
                  'isolated_tls_mode':1,'negotiated_protocol':positive['negotiated_tls_protocol'],
                  'server_version':positive['server_version'],'unknown_ca_rejected_before_login':True,
                  'plaintext_allowed_still_validates_ca':True,'plaintext_rejected_before_login':True,
                  'tls_business_roundtrip':'verified','final_database_state':'random_object_absent',
                  'run_dir':str(folder)},separators=(',',':')))
PY
    ;;
  matrix)
    run_logged probe_build dotnet build tools/SecurityProbe/SecurityProbe.csproj "${flags[@]}"
    run_logged mode_matrix python3 tools/LocalTlsEnvironment/mode_matrix.py "$run"
    [[ -s "$run/matrix-result.json" ]] || failed matrix_result_missing 1
    python3 - "$run/matrix-result.json" <<'PY' || failed matrix_result_validation 1
import json,sys
r=json.load(open(sys.argv[1]))
required=('mode2_auth_only_rejected','mode4_tls_verified','mode4_unknown_ca_rejected',
          'server_mode5_via_wire4_verified','mode1_restored_and_logged_in')
if r.get('task')!='T07' or r.get('mode')!='matrix' or r.get('status')!='real_verified' or \
   r.get('server_configured_mode')!=5 or r.get('mode5_negotiated_encrypt_mode')!=4 or \
   not all(r.get(key) is True for key in required):
    raise SystemExit('mode matrix envelope invalid')
PY
    cat "$run/matrix-result.json"
    ;;
esac
