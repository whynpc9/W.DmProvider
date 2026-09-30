#!/usr/bin/env bash
set -euo pipefail
set +x

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo"
if (( $# != 1 )) || [[ "$1" != baseline && "$1" != trace && "$1" != offline && "$1" != real && "$1" != package ]]; then
  printf '{"schema_version":1,"task":"T08","status":"rejected","reason":"usage","exit_code":64}\n'
  exit 64
fi
mode="$1"
base="$repo/.local/t08"
run="$base/runs/$(date -u +%Y%m%dT%H%M%SZ)-$$-$RANDOM"
mkdir -p "$run" "$base/dotnet-cli-home"
chmod 700 "$run"
export DOTNET_CLI_HOME="$base/dotnet-cli-home"
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export DOTNET_CLI_TELEMETRY_OPTOUT=1
flags=(-m:1 /nodeReuse:false /p:UseSharedCompilation=false --disable-build-servers)

failed() {
  local stage="$1" code="$2"
  printf '{"schema_version":1,"task":"T08","mode":"%s","status":"rejected","stage":"%s","exit_code":%s,"run_dir":"%s"}\n' "$mode" "$stage" "$code" "$run"
  exit "$code"
}
run_logged() {
  local stage="$1"; shift
  local code=0
  "$@" >"$run/$stage.log" 2>&1 || code=$?
  if (( code != 0 )); then failed "$stage" "$code"; fi
}
run_probe() {
  local stage="$1" assembly="$2" probe_mode="$3"
  local code=0
  scripts/with-dameng-test.sh dotnet "$assembly" "$probe_mode" >"$run/$stage.json" 2>/dev/null || code=$?
  [[ -s "$run/$stage.json" ]] || failed "${stage}_result_missing" 1
  (( code == 0 )) || failed "$stage" "$code"
}

case "$mode" in
  baseline)
    [[ -f .local/t08/baseline-w/net10.0/W.DmProvider.dll ]] || failed baseline_missing 1
    [[ "$(shasum -a 256 .local/t08/baseline-w/net10.0/W.DmProvider.dll | cut -d' ' -f1)" == \
       ae42d4aa9df4af8bf0874dd28056b6b2704f34677a28d34f44d0df200a66a4e5 ]] || failed baseline_hash_invalid 1
    run_logged oldw_build dotnet build tools/CommandProbe/OldW.csproj "${flags[@]}"
    for culture in en zh-CN zh-TW zh-HK; do
      cp -a ".local/t08/baseline-w/net10.0/$culture" "tools/CommandProbe/bin/OldW/net10.0/"
    done
    run_logged official_build dotnet build tools/CommandProbe/Official.csproj "${flags[@]}"
    run_probe official tools/CommandProbe/bin/Official/net10.0/CommandProbe.Official.dll real
    run_probe oldw tools/CommandProbe/bin/OldW/net10.0/CommandProbe.OldW.dll real
    python3 - "$run" <<'PY' || failed baseline_result_validation 1
import json,pathlib,sys
folder=pathlib.Path(sys.argv[1]); rows={k:json.loads((folder/f'{k}.json').read_text()) for k in ('official','oldw')}
identities={'official':('O','8f6e59680d0a076df53bea50d5a2bdbd288535cd85b2d7ca5064c02adc9c6e6b'),
            'oldw':('W-T07-baseline','ae42d4aa9df4af8bf0874dd28056b6b2704f34677a28d34f44d0df200a66a4e5')}
for key,row in rows.items():
    implementation,sha=identities[key]
    if (row.get('status')!='observed' or row.get('implementation')!=implementation or row.get('assembly_sha256')!=sha or
        not row.get('assembly_mvid') or row.get('server_account_verified') is not True or
        row.get('cleanup_verified') is not True or row.get('final_database_state')!='random_object_absent' or
        len(row.get('scenarios',{}))!=13):
        raise SystemExit(f'{key} baseline envelope invalid')
    scenarios=row['scenarios']
    if ([item['first_value'] for item in scenarios['two_selects']['sequence']]!=['1','2'] or
        [item['rows'] for item in scenarios['empty_then_select']['sequence']]!=[0,1] or
        scenarios['dml_zero_then_rowcount']['sequence'][0]['first_value']!='0' or
        scenarios['dml_then_rowcount']['sequence'][0]['first_value']!='1'):
        raise SystemExit(f'{key} baseline sequence invalid')
print(json.dumps({'schema_version':1,'task':'T08','mode':'baseline','status':'baseline_characterized',
                  'assembly_identities':{k:{'sha256':v['assembly_sha256'],'mvid':v['assembly_mvid']} for k,v in rows.items()},
                  'observed_baseline_defects':['non_dml_records_affected','compound_update0_final_records_affected'],
                  'run_dir':str(folder)},separators=(',',':')))
PY
    ;;
  trace)
    run_logged candidate_build dotnet build tools/CommandProbe/Candidate.csproj "${flags[@]}"
    run_probe candidate_targeted tools/CommandProbe/bin/Candidate/net10.0/CommandProbe.Candidate.dll targeted
    python3 - "$run/candidate_targeted.json" "$run" <<'PY' || failed trace_result_validation 1
import json,sys
r=json.load(open(sys.argv[1])); cases=r.get('scenarios',{})
if (r.get('status')!='observed' or r.get('implementation')!='W-T08-candidate' or
    r.get('opcode_observation')!='metadata_only_internal_hooks' or
    r.get('server_account_verified') is not True or r.get('cleanup_verified') is not True or
    r.get('final_database_state')!='random_object_absent' or len(cases)!=5 or
    not any(item.get('request_opcode')==44 for item in cases['select_update_select'].get('trace',[]))):
    raise SystemExit('targeted trace envelope invalid')
print(json.dumps({'schema_version':1,'task':'T08','mode':'trace','status':'candidate_trace_observed',
                  'assembly_sha256':r['assembly_sha256'],'assembly_mvid':r['assembly_mvid'],
                  'run_dir':sys.argv[2]},separators=(',',':')))
PY
    ;;
  offline)
    run_logged restore dotnet restore tests/W.DmProvider.CommandTests/W.DmProvider.CommandTests.csproj "${flags[@]}"
    run_logged test dotnet test tests/W.DmProvider.CommandTests/W.DmProvider.CommandTests.csproj --no-restore \
      --logger "trx;LogFileName=t08.trx" --results-directory "$run" "${flags[@]}"
    run_logged candidate_build dotnet build tools/CommandProbe/Candidate.csproj "${flags[@]}"
    run_logged t07_regression eng/t07.sh offline
    python3 - "$run/t08.trx" "$run" <<'PY' || failed test_result_validation 1
import json,sys,xml.etree.ElementTree as ET
root=ET.parse(sys.argv[1]).getroot(); ns={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
c=root.find('t:ResultSummary/t:Counters',ns)
required=('total','executed','passed','failed','error','timeout','aborted','inconclusive',
          'passedButRunAborted','notRunnable','notExecuted','disconnected','warning',
          'completed','inProgress','pending')
if c is None or any(c.get(k) is None for k in required): raise SystemExit('T08 TRX counters missing')
counts={k:int(c.get(k)) for k in required}; summary=root.find('t:ResultSummary',ns)
if (summary is None or summary.get('outcome') not in ('Completed','Passed') or counts['total']<=0 or
    counts['passed']!=counts['total'] or counts['executed']!=counts['total'] or
    any(v!=0 for k,v in counts.items() if k not in ('total','executed','passed'))):
    raise SystemExit('T08 TRX counters invalid')
print(json.dumps({'schema_version':1,'task':'T08','mode':'offline','status':'offline_verified',
                  'integration':'integration_pending','tests':counts,'t07_regression':'verified',
                  'run_dir':sys.argv[2]},separators=(',',':')))
PY
    ;;
  real)
    run_logged candidate_build dotnet build tools/CommandProbe/Candidate.csproj "${flags[@]}"
    run_probe candidate_verified tools/CommandProbe/bin/Candidate/net10.0/CommandProbe.Candidate.dll candidate_verified
    run_logged t06_real_regression eng/t06.sh real
    run_logged tls_smoke_build dotnet build tools/SecurityProbe/SecurityProbe.csproj "${flags[@]}"
    code=0
    scripts/with-dameng-tls-test.sh dotnet tools/SecurityProbe/bin/Debug/net10.0/SecurityProbe.dll smoke \
      >"$run/tls_smoke.json" 2>/dev/null || code=$?
    [[ -s "$run/tls_smoke.json" ]] || failed tls_smoke_result_missing 1
    (( code == 0 )) || failed tls_smoke "$code"
    python3 - "$run/candidate_verified.json" "$run" <<'PY' || failed real_result_validation 1
import hashlib,json,sys,pathlib
r=json.load(open(sys.argv[1])); folder=pathlib.Path(sys.argv[2]); cases=r.get('scenarios',{})
tls=json.load(open(folder/'tls_smoke.json'))
assembly=pathlib.Path('tools/CommandProbe/bin/Candidate/net10.0/W.DmProvider.dll')
actual=hashlib.sha256(assembly.read_bytes()).hexdigest()
if (r.get('task')!='T08' or r.get('implementation')!='W-T08-candidate' or
    r.get('status')!='candidate_verified' or r.get('opcode_observation')!='metadata_only_internal_hooks' or
    r.get('assembly_sha256')!=actual or not r.get('assembly_mvid') or
    r.get('server_account_verified') is not True or r.get('cleanup_verified') is not True or
    r.get('final_database_state')!='random_object_absent' or
    r.get('server_final_row_count_before_cleanup')!=0 or r.get('contract_failures')!=[] or len(cases)!=24):
    raise SystemExit('candidate CMD01-08 envelope invalid')
if (tls.get('status')!='tls_smoke_verified' or tls.get('implementation')!='W' or
    tls.get('negotiated_encrypt_mode')!=1 or tls.get('negotiated_tls_protocol') not in ('Tls12','Tls13') or
    tls.get('server_account_verified') is not True or tls.get('connection_closed_verified') is not True or
    tls.get('created_tcp_sockets')!=1 or tls.get('disposed_tcp_sockets')!=1):
    raise SystemExit('T07 TLS smoke regression invalid')
print(json.dumps({'schema_version':1,'task':'T08','mode':'real','status':'candidate_verified',
                  'assembly_sha256':actual,'assembly_mvid':r['assembly_mvid'],
                  'scenario_count':len(cases),'t06_real_regression':'verified','t07_tls_smoke':'verified',
                  'final_database_state':'random_object_absent','run_dir':str(folder)},separators=(',',':')))
PY
    ;;
  package)
    version=0.1.0-t08
    mkdir -p "$run/packages" "$run/nuget-cache"
    before_source="$(python3 tools/ProductPackageProbe/inspect.py source src/W.DmProvider)"
    run_logged pack dotnet pack src/W.DmProvider/W.DmProvider.csproj --configuration Release \
      --output "$run/packages" /p:Version="$version" "${flags[@]}"
    after_source="$(python3 tools/ProductPackageProbe/inspect.py source src/W.DmProvider)"
    [[ "$before_source" == "$after_source" ]] || failed source_changed_during_pack 1
    printf '%s\n' "$before_source" >"$run/source-sha.txt"
    package_file="$run/packages/W.DmProvider.$version.nupkg"
    [[ -s "$package_file" ]] || failed package_missing 1
    run_logged inspect python3 tools/ProductPackageProbe/inspect.py package "$package_file" \
      "$run/package-manifest.json" "$version" src/W.DmProvider
    export NUGET_PACKAGES="$run/nuget-cache"
    run_logged package_consumer_build dotnet build tools/CommandProbe/Package.csproj \
      /p:T08PackageVersion="$version" /p:T08PackageSource="$run/packages" "${flags[@]}"
    run_probe package_consumer tools/CommandProbe/bin/Package/net10.0/CommandProbe.Package.dll candidate_verified
    python3 - "$run/package-manifest.json" "$run/package_consumer.json" "$run" <<'PY' || failed package_result_validation 1
import hashlib,json,pathlib,sys
manifest=json.load(open(sys.argv[1])); result=json.load(open(sys.argv[2])); folder=pathlib.Path(sys.argv[3])
app=pathlib.Path('tools/CommandProbe/bin/Package/net10.0')
loaded=hashlib.sha256((app/'W.DmProvider.dll').read_bytes()).hexdigest()
deps=json.load(open(app/'CommandProbe.Package.deps.json'))
key='W.DmProvider/0.1.0-t08'
if (manifest.get('status')!='package_verified' or manifest.get('source_sha256')!=
        pathlib.Path(folder/'source-sha.txt').read_text().strip() or
    loaded!=manifest.get('assets',{}).get('lib/net10.0/W.DmProvider.dll') or
    deps.get('libraries',{}).get(key,{}).get('type')!='package' or
    result.get('status')!='package_verified' or result.get('implementation')!='W-T08-package' or
    result.get('assembly_sha256')!=loaded or not result.get('assembly_mvid') or
    result.get('server_account_verified') is not True or result.get('cleanup_verified') is not True or
    result.get('final_database_state')!='random_object_absent' or
    result.get('server_final_row_count_before_cleanup')!=0 or
    result.get('contract_failures')!=[] or len(result.get('scenarios',{}))!=24):
    raise SystemExit('package consumer CMD08 envelope invalid')
print(json.dumps({'schema_version':1,'task':'T08','mode':'package','status':'package_consumer_verified',
                  'package_sha256':manifest['package_sha256'],'loaded_assembly_sha256':loaded,
                  'loaded_assembly_mvid':result['assembly_mvid'],
                  'source_sha256':manifest['source_sha256'],'scenario_count':24,
                  'final_database_state':'random_object_absent','run_dir':str(folder)},separators=(',',':')))
PY
    ;;
esac
