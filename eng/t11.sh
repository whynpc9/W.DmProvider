#!/usr/bin/env bash
set -euo pipefail
set +x
repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo"
if (( $# != 1 )) || [[ "$1" != baseline && "$1" != offline && "$1" != real && "$1" != real-public ]]; then
  printf '{"task":"T11","status":"rejected","reason":"usage","exit_code":64}\n'; exit 64
fi
mode="$1"
run="$repo/.local/t11/runs/$(date -u +%Y%m%dT%H%M%SZ)-$$-$RANDOM"
mkdir -p "$run" "$repo/.local/t11/dotnet-cli-home"
chmod 700 "$run"
export DOTNET_CLI_HOME="$repo/.local/t11/dotnet-cli-home"
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 DOTNET_CLI_TELEMETRY_OPTOUT=1
flags=(-m:1 /nodeReuse:false /p:UseSharedCompilation=false --disable-build-servers)
input_hash() {
  python3 - "$mode" <<'PY'
import hashlib,pathlib,sys
roots=[pathlib.Path('tools/IsolationProbe')]
if sys.argv[1]!='baseline':roots += [pathlib.Path('src/W.DmProvider'),pathlib.Path('tests/W.DmProvider.IsolationTests'),pathlib.Path('global.json'),pathlib.Path('eng/t11.sh'),pathlib.Path('eng/T11.md')]
h=hashlib.sha256()
for root in roots:
 for p in sorted(root.rglob('*') if root.is_dir() else [root]):
  if not p.is_file() or any(x in ('bin','obj','__pycache__') for x in p.parts):continue
  h.update(str(p).encode());h.update(b'\0');h.update(p.read_bytes());h.update(b'\0')
print(h.hexdigest())
PY
}
check_source_end() { local after; after="$(input_hash)"; printf '%s\n' "$after" >"$run/source-after.txt"; [[ "$before" == "$after" ]]; }
failed() { local stage="$1" code="$2"; if ! check_source_end; then stage=input_source_changed; code=1; fi; printf '{"task":"T11","mode":"%s","status":"rejected","stage":"%s","exit_code":%s,"run_dir":"%s"}\n' "$mode" "$stage" "$code" "$run"; exit "$code"; }
logged() { local stage="$1"; shift; local code=0; "$@" >"$run/$stage.log" 2>&1 || code=$?; (( code == 0 )) || failed "$stage" "$code"; }
probe() { local stage="$1" assembly="$2" argument="$3" code=0; scripts/with-dameng-test.sh dotnet "$assembly" "$argument" >"$run/$stage.json" 2>/dev/null || code=$?; (( code == 0 )) || failed "$stage" "$code"; }
before="$(input_hash)"; printf '%s\n' "$before" >"$run/source-before.txt"
if [[ "$mode" == baseline ]]; then
  r=.local/t02/restored/bin/Debug/net9.0/DM.DmProvider.dll
  w=.local/t10/accepted-binaries/net10.0/W.DmProvider.dll
  [[ "$(shasum -a 256 "$r" | cut -d' ' -f1)" == 1bbedef16e8720227fd416421e744b7894df79082f3ddd7d5a9537c1d424fba3 ]] || failed restored_pin 1
  [[ "$(shasum -a 256 "$w" | cut -d' ' -f1)" == 51043edb57710cb5c4aa7e13a514e1672e4783150babb9af3ddd0ed1b329323b ]] || failed frozen_pin 1
  for name in Official Restored Frozen; do
    logged "${name}_build" dotnet build "tools/IsolationProbe/$name.csproj" "${flags[@]}"
    if [[ "$name" != Official ]]; then
      base=.local/t10/accepted-binaries/net10.0
      [[ "$name" != Restored ]] || base=.local/t02/restored/bin/Debug/net9.0
      for culture in en zh-CN zh-TW zh-HK; do [[ ! -d "$base/$culture" ]] || cp -a "$base/$culture" "tools/IsolationProbe/bin/$name/net10.0/"; done
    fi
    probe "$name" "tools/IsolationProbe/bin/$name/net10.0/IsolationProbe.$name.dll" causal
    label=O; [[ "$name" != Restored ]] || label=R; [[ "$name" != Frozen ]] || label=W-T10-frozen
    logged "${name}_validate" python3 tools/IsolationProbe/validate.py "$label" "$run/$name.json"
  done
  check_source_end || failed input_source_changed 1
  printf '{"task":"T11","mode":"baseline","status":"baseline_profiled","candidate_acceptance":false,"run_dir":"%s"}\n' "$run"
elif [[ "$mode" == offline ]]; then
  logged isolation_tests dotnet test tests/W.DmProvider.IsolationTests/W.DmProvider.IsolationTests.csproj \
    --logger "trx;LogFileName=t11.trx" --results-directory "$run" "${flags[@]}"
  logged candidate_build dotnet build tools/IsolationProbe/Candidate.csproj "${flags[@]}"
  check_source_end || failed input_source_changed 1
  python3 - "$run/t11.trx" "$run" <<'PY' || failed strict_trx_validation 1
import json,sys,xml.etree.ElementTree as ET
r=ET.parse(sys.argv[1]).getroot();n={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
s=r.find('t:ResultSummary',n);c=r.find('t:ResultSummary/t:Counters',n)
keys=('total','executed','passed','failed','error','timeout','aborted','inconclusive','passedButRunAborted','notRunnable','notExecuted','disconnected','warning','completed','inProgress','pending')
if s is None or c is None or any(c.get(k) is None for k in keys):raise SystemExit('TRX incomplete')
a={k:int(c.get(k)) for k in keys}
if s.get('outcome') not in ('Completed','Passed') or a['total']<4 or a['passed']!=a['total'] or a['executed']!=a['total'] or any(v for k,v in a.items() if k not in ('total','passed','executed')):raise SystemExit('TRX rejected')
print(json.dumps({'task':'T11','mode':'offline','status':'offline_verified','integration':'integration_pending','tests':a,'run_dir':sys.argv[2]},separators=(',',':')))
PY
else
  logged candidate_build dotnet build tools/IsolationProbe/Candidate.csproj "${flags[@]}"
  dll=tools/IsolationProbe/bin/Candidate/net10.0/W.DmProvider.dll
  sha="$(shasum -a 256 "$dll" | cut -d' ' -f1)"
  entry=internal_profile; matrix_mode=candidate-matrix; litmus_mode=candidate-litmus
  if [[ "$mode" == real-public ]]; then entry=public; matrix_mode=public-matrix; litmus_mode=public-litmus; fi
  probe candidate_matrix tools/IsolationProbe/bin/Candidate/net10.0/IsolationProbe.Candidate.dll "$matrix_mode"
  # Retain both complete observations before applying either strict gate.
  probe candidate_litmus tools/IsolationProbe/bin/Candidate/net10.0/IsolationProbe.Candidate.dll "$litmus_mode"
  matrix_code=0; litmus_code=0
  python3 tools/IsolationProbe/validate-candidate.py matrix "$run/candidate_matrix.json" "$sha" "$entry" >"$run/candidate_matrix_validate.json" 2>&1 || matrix_code=$?
  python3 tools/IsolationProbe/validate-candidate.py litmus "$run/candidate_litmus.json" "$sha" "$entry" >"$run/candidate_litmus_validate.json" 2>&1 || litmus_code=$?
  check_source_end || failed input_source_changed 1
  (( matrix_code == 0 )) || failed candidate_matrix_validate "$matrix_code"
  (( litmus_code == 0 )) || failed candidate_litmus_validate "$litmus_code"
  printf '{"task":"T11","mode":"%s","status":"candidate_ADO_and_litmus_verified","entry":"%s","assembly_sha256":"%s","EF_acceptance":"pending","run_dir":"%s"}\n' "$mode" "$entry" "$sha" "$run"
fi
