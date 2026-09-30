#!/usr/bin/env bash
set -euo pipefail
set +x

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo"
if (( $# != 1 )) || [[ "$1" != offline && "$1" != real ]]; then
  printf '{"schema_version":1,"task":"T06","status":"rejected","reason":"usage","exit_code":64}\n'
  exit 64
fi
mode="$1"
base="$repo/.local/t06"
run="$base/runs/$(date -u +%Y%m%dT%H%M%SZ)-$$-$RANDOM"
mkdir -p "$run" "$base/dotnet-cli-home"
export DOTNET_CLI_HOME="$base/dotnet-cli-home"
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export DOTNET_CLI_TELEMETRY_OPTOUT=1
flags=(-m:1 /nodeReuse:false /p:UseSharedCompilation=false --disable-build-servers)

failed() {
  local stage="$1" code="$2"
  printf '{"schema_version":1,"task":"T06","mode":"%s","status":"rejected","stage":"%s","exit_code":%s,"run_dir":"%s"}\n' "$mode" "$stage" "$code" "$run"
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
    run_logged restore dotnet restore tests/W.DmProvider.TransportTests/W.DmProvider.TransportTests.csproj "${flags[@]}"
    run_logged test dotnet test tests/W.DmProvider.TransportTests/W.DmProvider.TransportTests.csproj --no-restore \
      --logger "trx;LogFileName=t06.trx" --results-directory "$run" "${flags[@]}"
    run_logged probe_build dotnet build tools/TransportProbe/TransportProbe.csproj "${flags[@]}"
    run_logged t05_ownership_regression eng/t05.sh offline
    [[ -s "$run/t06.trx" ]] || failed test_result_missing 1
    python3 - "$run/t06.trx" "$run" <<'PY' || failed test_result_validation 1
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
print(json.dumps({'schema_version':1,'task':'T06','mode':'offline','status':'offline_verified',
                  'integration':'integration_pending','t05_ownership_regression':'verified',
                  'tests':counts,'run_dir':sys.argv[2]},separators=(',',':')))
PY
    ;;
  real)
    run_logged probe_build dotnet build tools/TransportProbe/TransportProbe.csproj "${flags[@]}"
    code=0
    scripts/with-dameng-test.sh dotnet tools/TransportProbe/bin/Debug/net10.0/TransportProbe.dll real \
      >"$run/real-result.json" 2>/dev/null || code=$?
    [[ -s "$run/real-result.json" ]] || failed real_result_missing 1
    (( code == 0 )) || failed real_probe "$code"
    python3 - "$run/real-result.json" <<'PY' || failed real_result_validation 1
import json,sys
r=json.load(open(sys.argv[1]))
required=('server_account_verified','explicit_test_schema','single_tcp_open_verified',
          'independent_readback_verified','cleanup_verified')
for phase in ('startup','login','schema_initialization'):
    required+=(phase+'_fault_connection_closed_verified',phase+'_fault_socket_released_verified',
               phase+'_post_fault_reopen_verified')
if (r.get('status')!='real_verified' or r.get('implementation')!='W' or r.get('task')!='T06' or
    r.get('integration')!='real_test_schema' or r.get('final_database_state')!='random_object_absent' or
    r.get('explicit_transport')!='PlaintextAllowed' or not all(r.get(k) is True for k in required)):
    raise SystemExit('real probe success envelope invalid')
PY
    run_logged t05_ownership_regression eng/t05.sh real
    cat "$run/real-result.json"
    ;;
esac
