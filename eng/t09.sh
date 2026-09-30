#!/usr/bin/env bash
set -euo pipefail
set +x

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo"
if (( $# != 1 )) || [[ "$1" != baseline && "$1" != offline && "$1" != real ]]; then
  printf '{"schema_version":1,"task":"T09","status":"rejected","reason":"usage","exit_code":64}\n'
  exit 64
fi
mode="$1"
base="$repo/.local/t09"
run="$base/runs/$(date -u +%Y%m%dT%H%M%SZ)-$$-$RANDOM"
mkdir -p "$run" "$base/dotnet-cli-home"
chmod 700 "$run"
export DOTNET_CLI_HOME="$base/dotnet-cli-home"
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export DOTNET_CLI_TELEMETRY_OPTOUT=1
flags=(-m:1 /nodeReuse:false /p:UseSharedCompilation=false --disable-build-servers)

check_run_source() {
  [[ -n "${before:-}" ]] || return 0
  local final_source
  final_source="$(python3 tools/ProductPackageProbe/inspect.py source src/W.DmProvider)"
  printf '%s\n' "$final_source" >"$run/source-after-run-sha.txt"
  [[ "$final_source" == "$before" ]]
}

failed() {
  local stage="$1" code="$2"
  if ! check_run_source; then stage=product_source_changed; code=1; fi
  printf '{"schema_version":1,"task":"T09","mode":"%s","status":"rejected","stage":"%s","exit_code":%s,"run_dir":"%s"}\n' \
    "$mode" "$stage" "$code" "$run"
  exit "$code"
}
run_logged() {
  local stage="$1"; shift
  local code=0
  "$@" >"$run/$stage.log" 2>&1 || code=$?
  (( code == 0 )) || failed "$stage" "$code"
}
probe() {
  local stage="$1" assembly="$2" argument="$3"
  local code=0
  if [[ "$argument" == profile ]]; then
    scripts/with-dameng-test.sh dotnet "$assembly" profile >"$run/$stage.json" 2>/dev/null || code=$?
  else
    dotnet "$assembly" codec >"$run/$stage.json" 2>/dev/null || code=$?
  fi
  [[ -s "$run/$stage.json" ]] || failed "${stage}_result_missing" 1
  (( code == 0 )) || failed "$stage" "$code"
}
frozen=".local/t08/accepted-binaries/net10.0/W.DmProvider.dll"
frozen_sha="51e340397f64538e41d999fcf01e154bfabbf94f08704e749bf03ac7128440f0"
if [[ "$mode" == baseline ]]; then
  [[ -f "$frozen" ]] || failed frozen_missing 1
  [[ "$(shasum -a 256 "$frozen" | cut -d' ' -f1)" == "$frozen_sha" ]] || failed frozen_hash_invalid 1
  run_logged official_build dotnet build tools/TypeProbe/Official.csproj "${flags[@]}"
  run_logged frozen_build dotnet build tools/TypeProbe/Frozen.csproj "${flags[@]}"
  for culture in en zh-CN zh-HK zh-TW; do
    cp -a ".local/t08/accepted-binaries/net10.0/$culture" "tools/TypeProbe/bin/Frozen/net10.0/"
  done
  probe official_codec tools/TypeProbe/bin/Official/net10.0/TypeProbe.Official.dll codec
  probe frozen_codec tools/TypeProbe/bin/Frozen/net10.0/TypeProbe.Frozen.dll codec
  run_logged official_codec_validate python3 tools/TypeProbe/validate.py codec "$run/official_codec.json" O
  run_logged frozen_codec_validate python3 tools/TypeProbe/validate.py codec "$run/frozen_codec.json" W-T08-frozen
  probe official_profile tools/TypeProbe/bin/Official/net10.0/TypeProbe.Official.dll profile
  probe frozen_profile tools/TypeProbe/bin/Frozen/net10.0/TypeProbe.Frozen.dll profile
  run_logged baseline_validate python3 tools/TypeProbe/validate.py baseline \
    "$run/official_profile.json" "$run/frozen_profile.json"
  [[ "$(shasum -a 256 "$frozen" | cut -d' ' -f1)" == "$frozen_sha" ]] || failed frozen_changed 1
  cat "$run/baseline_validate.log"
elif [[ "$mode" == offline ]]; then
  before="$(python3 tools/ProductPackageProbe/inspect.py source src/W.DmProvider)"
  printf '%s\n' "$before" >"$run/source-before-sha.txt"
  run_logged type_tests dotnet test tests/W.DmProvider.TypeTests/W.DmProvider.TypeTests.csproj \
    --logger "trx;LogFileName=t09.trx" --results-directory "$run" "${flags[@]}"
  run_logged candidate_build dotnet build tools/TypeProbe/Candidate.csproj "${flags[@]}"
  after="$(python3 tools/ProductPackageProbe/inspect.py source src/W.DmProvider)"
  printf '%s\n' "$after" >"$run/source-after-sha.txt"
  [[ "$before" == "$after" ]] || failed product_source_changed 1
  printf '%s\n' "$before" >"$run/source-sha.txt"
  check_run_source || failed product_source_changed 1
  python3 - "$run/t09.trx" "$run" <<'PY' || failed trx_validation 1
import json,sys,xml.etree.ElementTree as ET
root=ET.parse(sys.argv[1]).getroot(); ns={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
summary=root.find('t:ResultSummary',ns); counters=root.find('t:ResultSummary/t:Counters',ns)
required=('total','executed','passed','failed','error','timeout','aborted','inconclusive',
          'passedButRunAborted','notRunnable','notExecuted','disconnected','warning',
          'completed','inProgress','pending')
if summary is None or counters is None or any(counters.get(k) is None for k in required):
    raise SystemExit('T09 TRX incomplete')
counts={k:int(counters.get(k)) for k in required}
if summary.get('outcome') not in ('Completed','Passed') or counts['total']<=0 or \
   counts['passed']!=counts['total'] or counts['executed']!=counts['total'] or \
   any(v for k,v in counts.items() if k not in ('total','executed','passed')):
    raise SystemExit('T09 TRX failure or skip')
print(json.dumps({'schema_version':1,'task':'T09','mode':'offline','status':'offline_verified',
                  'integration':'integration_pending','tests':counts,'run_dir':sys.argv[2]},separators=(',',':')))
PY
else
  before="$(python3 tools/ProductPackageProbe/inspect.py source src/W.DmProvider)"
  printf '%s\n' "$before" >"$run/source-before-sha.txt"
  run_logged candidate_build dotnet build tools/TypeProbe/Candidate.csproj "${flags[@]}"
  after="$(python3 tools/ProductPackageProbe/inspect.py source src/W.DmProvider)"
  printf '%s\n' "$after" >"$run/source-after-sha.txt"
  [[ "$before" == "$after" ]] || failed product_source_changed 1
  printf '%s\n' "$before" >"$run/source-sha.txt"
  candidate="tools/TypeProbe/bin/Candidate/net10.0/W.DmProvider.dll"
  candidate_sha="$(shasum -a 256 "$candidate" | cut -d' ' -f1)"
  probe candidate_profile tools/TypeProbe/bin/Candidate/net10.0/TypeProbe.Candidate.dll profile
  code=0
  python3 tools/TypeProbe/validate.py candidate "$run/candidate_profile.json" "$candidate_sha" \
    >"$run/candidate_validate.log" 2>&1 || code=$?
  if (( code != 0 )); then
    python3 tools/TypeProbe/diagnose.py "$run/candidate_profile.json" \
      >"$run/candidate_diagnostic.json" 2>/dev/null || true
    failed candidate_validate "$code"
  fi
  check_run_source || failed product_source_changed 1
  cat "$run/candidate_validate.log"
fi
