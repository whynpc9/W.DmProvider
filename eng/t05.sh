#!/usr/bin/env bash
set -euo pipefail
set +x

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo"
if (( $# != 1 )) || [[ "$1" != offline && "$1" != real && "$1" != blob ]]; then
  printf '{"schema_version":1,"task":"T05","status":"rejected","reason":"usage","exit_code":64}\n'
  exit 64
fi
mode="$1"
base="$repo/.local/t05"
run="$base/runs/$(date -u +%Y%m%dT%H%M%SZ)-$$-$RANDOM"
mkdir -p "$run" "$base/dotnet-cli-home"
export DOTNET_CLI_HOME="$base/dotnet-cli-home"
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export DOTNET_CLI_TELEMETRY_OPTOUT=1
flags=(-m:1 /nodeReuse:false /p:UseSharedCompilation=false --disable-build-servers)

failed() {
  local stage="$1" code="$2"
  printf '{"schema_version":1,"task":"T05","mode":"%s","status":"rejected","stage":"%s","exit_code":%s,"run_dir":"%s"}\n' "$mode" "$stage" "$code" "$run"
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
    run_logged restore dotnet restore tests/W.DmProvider.SessionTests/W.DmProvider.SessionTests.csproj "${flags[@]}"
    run_logged test dotnet test tests/W.DmProvider.SessionTests/W.DmProvider.SessionTests.csproj --no-restore \
      --logger "trx;LogFileName=t05.trx" --results-directory "$run" "${flags[@]}"
    run_logged probe_build dotnet build tools/SessionProbe/SessionProbe.csproj "${flags[@]}"
    [[ -s "$run/t05.trx" ]] || failed test_result_missing 1
    python3 - "$run/t05.trx" "$run" <<'PY' || failed test_result_validation 1
import json,sys,xml.etree.ElementTree as ET
root=ET.parse(sys.argv[1]).getroot()
ns={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
c=root.find('t:ResultSummary/t:Counters',ns)
if c is None: raise SystemExit('TRX counters missing')
required=('total','executed','passed','failed','error','timeout','aborted','inconclusive',
          'passedButRunAborted','notRunnable','notExecuted','disconnected','warning',
          'completed','inProgress','pending')
if any(c.get(k) is None for k in required): raise SystemExit('TRX counter missing')
counts={k:int(c.get(k)) for k in required}
summary=root.find('t:ResultSummary',ns)
if (summary is None or summary.get('outcome') not in ('Completed','Passed') or counts['total'] <= 0 or
    counts['passed'] != counts['total'] or counts['executed'] != counts['total'] or
    any(v != 0 for k,v in counts.items() if k not in ('total','executed','passed'))):
    raise SystemExit('TRX counters invalid')
print(json.dumps({'schema_version':1,'task':'T05','mode':'offline','status':'offline_verified',
                  'integration':'integration_pending','tests':counts,'run_dir':sys.argv[2]},separators=(',',':')))
PY
    ;;
  real)
    run_logged probe_build dotnet build tools/SessionProbe/SessionProbe.csproj "${flags[@]}"
    code=0
    scripts/with-dameng-test.sh dotnet tools/SessionProbe/bin/Debug/net10.0/SessionProbe.dll real \
      >"$run/real-result.json" 2>/dev/null || code=$?
    [[ -s "$run/real-result.json" ]] || failed real_result_missing 1
    (( code == 0 )) || failed real_probe "$code"
    python3 - "$run/real-result.json" <<'PY' || failed real_result_validation 1
import json,sys
r=json.load(open(sys.argv[1]))
required=('server_account_verified','reader_ownership_verified','transaction_conflict_verified',
          'close_idempotent_verified','independent_connection_verified','cleanup_verified',
          'same_reader_failfast_verified','same_reader_zero_send_verified','simultaneous_wire_verified',
          'after_send_failclosed_verified','before_decode_failclosed_verified',
          'stale_close_safe_verified','lob_getbytes_verified','lob_stream_verified',
          'direct_lob_stream_ownership_verified')
required+=('old_transaction_safe_verified','dispose_no_implicit_commit_verified','prepare_dispose_safe_verified',
           'schema_table_verified','cancel_does_not_reconnect_verified','close_during_handshake_verified',
           'competing_command_zero_send_verified','connection_schema_verified',
           'primary_key_dictionary_view_verified','state_callback_outside_lock_verified')
if (r.get('status')!='real_verified' or r.get('implementation')!='W' or r.get('task')!='T05' or
    r.get('integration')!='real_test_schema' or r.get('final_database_state')!='random_object_absent' or
    r.get('explicit_transport')!='PlaintextAllowed' or not all(r.get(k) is True for k in required)):
    raise SystemExit('real probe success envelope invalid')
PY
    cat "$run/real-result.json"
    ;;
  blob)
    for implementation in W Official Restored; do
      run_logged "blob_${implementation}_build" dotnet build "tools/SessionProbe/Blob${implementation}.csproj" "${flags[@]}"
    done
    for entry in 'W W' 'O Official' 'R Restored'; do
      read -r label project <<< "$entry"
      code=0
      scripts/with-dameng-test.sh dotnet "tools/SessionProbe/bin/Blob${project}/net10.0/Blob${project}.dll" "$label" \
        >"$run/blob-${label}.json" 2>/dev/null || code=$?
      [[ -s "$run/blob-${label}.json" ]] || failed "blob_${label}_result_missing" 1
      (( code == 0 )) || failed "blob_${label}_probe" "$code"
    done
    python3 - "$run" <<'PY' || failed blob_comparison 1
import json,pathlib,sys
folder=pathlib.Path(sys.argv[1])
rows={key:json.loads((folder/f'blob-{key}.json').read_text()) for key in ('W','O','R')}
for key,row in rows.items():
    if row.get('implementation')!=key or row.get('probe')!='three_parameter_blob_insert' or row.get('status')!='observed' or not row.get('server_account_verified') or not row.get('cleanup_verified') or row.get('final_database_state')!='random_object_absent' or not row.get('assembly_sha256') or not row.get('assembly_mvid'):
        raise SystemExit(f'{key} blob probe envelope invalid')
passed=all(row.get('insert_succeeded') is True and row.get('independent_readback_verified') is True for row in rows.values())
out={'schema_version':1,'task':'T05','mode':'blob','status':'blob_verified' if passed else 'rejected',
     'insert_succeeded':{key:row.get('insert_succeeded') for key,row in rows.items()},
     'independent_readback_verified':{key:row.get('independent_readback_verified') for key,row in rows.items()},
     'reader_schema_succeeded':{key:row.get('reader_schema_succeeded') for key,row in rows.items()},
     'reader_schema_is_key':{key:row.get('reader_schema_is_key') for key,row in rows.items()},
     'reader_schema_error_number':{key:row.get('reader_schema_error_number') for key,row in rows.items()},
     'assembly_sha256':{key:row.get('assembly_sha256') for key,row in rows.items()},
     'assembly_mvid':{key:row.get('assembly_mvid') for key,row in rows.items()},
     'work_error_kind':{key:row.get('work_error_kind') for key,row in rows.items()},
     'comparative_regression': rows['O'].get('insert_succeeded') is True and rows['R'].get('insert_succeeded') is True and rows['W'].get('insert_succeeded') is not True,
     'all_cleanup_verified':True,'run_dir':str(folder)}
print(json.dumps(out,separators=(',',':')))
if not passed:
    raise SystemExit('blob insert/readback gate failed')
PY
    ;;
esac
