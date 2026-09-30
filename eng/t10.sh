#!/usr/bin/env bash
set -euo pipefail
set +x

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo"
if (( $# != 1 )) || [[ "$1" != baseline && "$1" != offline && "$1" != real ]]; then
  printf '{"schema_version":1,"task":"T10","status":"rejected","reason":"usage","exit_code":64}\n'
  exit 64
fi
mode="$1"
base="$repo/.local/t10"
run="$base/runs/$(date -u +%Y%m%dT%H%M%SZ)-$$-$RANDOM"
mkdir -p "$run" "$base/dotnet-cli-home"
chmod 700 "$run"
export DOTNET_CLI_HOME="$base/dotnet-cli-home"
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export DOTNET_CLI_TELEMETRY_OPTOUT=1
flags=(-m:1 /nodeReuse:false /p:UseSharedCompilation=false --disable-build-servers)
source_hash() {
  python3 - <<'PY'
import hashlib,pathlib
root=pathlib.Path('src/W.DmProvider'); digest=hashlib.sha256()
for path in sorted(root.rglob('*')):
    if not path.is_file() or any(part in ('bin','obj') for part in path.parts): continue
    digest.update(str(path.relative_to(root)).encode()); digest.update(b'\0')
    digest.update(path.read_bytes()); digest.update(b'\0')
print(digest.hexdigest())
PY
}

check_run_source() {
  [[ -n "${source_start:-}" ]] || return 0
  local final_source
  final_source="$(source_hash)"
  printf '%s\n' "$final_source" >"$run/source-after-run-sha.txt"
  [[ "$final_source" == "$source_start" ]]
}

failed() {
  local stage="$1" code="$2"
  if ! check_run_source; then stage=product_source_changed; code=1; fi
  printf '{"schema_version":1,"task":"T10","mode":"%s","status":"rejected","stage":"%s","exit_code":%s,"run_dir":"%s"}\n' "$mode" "$stage" "$code" "$run"
  exit "$code"
}
run_logged() {
  local stage="$1"; shift
  local code=0
  "$@" >"$run/$stage.log" 2>&1 || code=$?
  if (( code != 0 )); then failed "$stage" "$code"; fi
}
run_probe() {
  local stage="$1" assembly="$2" probe_mode="${3:-profile}"
  local code=0
  scripts/with-dameng-test.sh dotnet "$assembly" "$probe_mode" >"$run/$stage.json" 2>/dev/null || code=$?
  [[ -s "$run/$stage.json" ]] || failed "${stage}_result_missing" 1
  (( code == 0 )) || failed "$stage" "$code"
}

case "$mode" in
  baseline)
    [[ -s .local/t08/accepted-binaries/net10.0/W.DmProvider.dll ]] || failed frozen_w_missing 1
    [[ "$(shasum -a 256 .local/t08/accepted-binaries/net10.0/W.DmProvider.dll | cut -d' ' -f1)" == \
       51e340397f64538e41d999fcf01e154bfabbf94f08704e749bf03ac7128440f0 ]] || failed frozen_w_hash_invalid 1
    run_logged official_build dotnet build tools/TransactionProbe/Official.csproj "${flags[@]}"
    run_logged oldw_build dotnet build tools/TransactionProbe/OldW.csproj "${flags[@]}"
    for culture in en zh-CN zh-TW zh-HK; do
      cp -a ".local/t08/accepted-binaries/net10.0/$culture" "tools/TransactionProbe/bin/OldW/net10.0/"
    done
    run_probe official tools/TransactionProbe/bin/Official/net10.0/TransactionProbe.Official.dll
    run_probe oldw tools/TransactionProbe/bin/OldW/net10.0/TransactionProbe.OldW.dll
    run_probe oldw_quoted tools/TransactionProbe/bin/OldW/net10.0/TransactionProbe.OldW.dll quoted-savepoint
    run_probe oldw_ddl tools/TransactionProbe/bin/OldW/net10.0/TransactionProbe.OldW.dll ddl-marker
    run_probe oldw_ddl_variants tools/TransactionProbe/bin/OldW/net10.0/TransactionProbe.OldW.dll ddl-variants
    python3 - "$run" <<'PY' || failed baseline_result_validation 1
import json,pathlib,sys
folder=pathlib.Path(sys.argv[1]); rows={key:json.loads((folder/f'{key}.json').read_text())
    for key in ('official','oldw')}
quoted=json.loads((folder/'oldw_quoted.json').read_text())
ddl=json.loads((folder/'oldw_ddl.json').read_text())
variants=json.loads((folder/'oldw_ddl_variants.json').read_text())
identity={'official':('O','8f6e59680d0a076df53bea50d5a2bdbd288535cd85b2d7ca5064c02adc9c6e6b'),
          'oldw':('W-T08-baseline','51e340397f64538e41d999fcf01e154bfabbf94f08704e749bf03ac7128440f0')}
for key,row in rows.items():
    label,sha=identity[key]; cases=row.get('scenarios',{})
    if (row.get('status')!='observed' or row.get('implementation')!=label or
        row.get('assembly_sha256')!=sha or not row.get('assembly_mvid') or
        row.get('server_version')!='8.1.5.60' or
        row.get('server_account_verified') is not True or row.get('cleanup_verified') is not True or
        row.get('final_database_state')!='random_objects_absent' or len(cases)!=7 or
        cases['commit_insert'].get('committed') is not True or
        cases['rollback_insert'].get('rolled_back') is not True or
        cases['ado_savepoint'].get('supports_savepoints') is not False or
        cases['sql_savepoint'].get('before_visible') is not True or
        cases['sql_savepoint'].get('after_absent') is not True or
        cases['ddl_implicit_commit'].get('row_before_ddl_visible') is not True or
        cases['ddl_implicit_commit'].get('ddl_table_exists') is not True):
        raise SystemExit(f'{key} T10 baseline envelope invalid')
w=rows['oldw']['scenarios']
empty=w['empty_begin_commit']; commit=w['commit_insert']; rollback=w['rollback_insert']
def ack(events,stage,opcode):
    return any(e.get('stage')==stage and e.get('request_opcode')==opcode and
               e.get('response_opcode')==0 and e.get('sql_code')==0 and
               e.get('wire_body_length_declared')==28 for e in events)
if (empty.get('begin_send_delta')!=0 or empty.get('commit_send_delta')!=1 or
    not ack(empty.get('trace',[]),'commit',8) or not ack(commit.get('trace',[]),'commit',8) or
    not ack(rollback.get('trace',[]),'rollback',9)):
    raise SystemExit('frozen W control ACK profile invalid')
for label,row,case in (('quoted',quoted,'quoted_sql_savepoint'),('ddl',ddl,'ddl_implicit_commit')):
    if (row.get('status')!='observed' or row.get('implementation')!='W-T08-baseline' or
        row.get('assembly_sha256')!=identity['oldw'][1] or row.get('server_version')!='8.1.5.60' or
        row.get('server_account_verified') is not True or row.get('cleanup_verified') is not True or
        row.get('final_database_state')!='random_objects_absent' or case not in row.get('scenarios',{})):
        raise SystemExit(f'{label} narrowed profile invalid')
q=quoted['scenarios']['quoted_sql_savepoint']
if (q.get('outcome')!='completed' or q.get('before_visible') is not True or
    q.get('after_absent') is not True):
    raise SystemExit('quoted savepoint profile invalid')
d=ddl['scenarios']['ddl_implicit_commit']; decoded=[e for e in d.get('trace',[])
    if e.get('event_kind')=='statement_decode']
insert=next((e for e in decoded if e.get('stage')=='none' and e.get('request_opcode')==5 and
    e.get('ret_stmt_type')==157),None)
terminal=next((e for e in decoded if e.get('stage')=='none' and e.get('request_opcode')==44 and
    e.get('is_terminal') is True),None)
business_ddl=next((e for e in decoded if e.get('stage')=='ddl' and e.get('request_opcode')==5 and
    e.get('ret_stmt_type')==129),None)
if (d.get('row_before_ddl_visible') is not True or d.get('ddl_table_exists') is not True or
    insert is None or terminal is None or business_ddl is None or
    insert.get('physical')!={'trx_status':1,'trans_finish':False} or
    terminal.get('physical')!={'trx_status':0,'trans_finish':True} or
    business_ddl.get('physical')!={'trx_status':0,'trans_finish':True}):
    raise SystemExit('response-time DDL profile invalid')
if (variants.get('status')!='observed' or variants.get('implementation')!='W-T08-baseline' or
    variants.get('assembly_sha256')!=identity['oldw'][1] or
    variants.get('server_version')!='8.1.5.60' or
    variants.get('server_account_verified') is not True or
    variants.get('cleanup_verified') is not True or
    variants.get('final_database_state')!='random_objects_absent'):
    raise SystemExit('DDL variant envelope invalid')
steps=variants.get('scenarios',{}).get('ddl_variants',{}).get('variants',[])
for item,(name,ret,exists) in zip(steps,
        [('alter',146,True),('truncate',194,True),('drop',139,False)]):
    decoded=[e for e in item.get('trace',[]) if e.get('event_kind')=='statement_decode']
    business=next((e for e in decoded if e.get('stage')==name and
        e.get('request_opcode')==5 and e.get('ret_stmt_type')==ret and
        e.get('is_terminal') is False),None)
    terminal=next((e for e in decoded if e.get('stage')==name and
        e.get('request_opcode')==44 and e.get('is_terminal') is True),None)
    if (item.get('name')!=name or item.get('outcome')!='observed' or
        item.get('preceding_insert_visible') is not True or
        item.get('target_exists') is not exists or business is None or terminal is None or
        business.get('physical')!={'trx_status':0,'trans_finish':True}):
        raise SystemExit(f'{name} DDL variant profile invalid')
if len(steps)!=3: raise SystemExit('DDL variant count invalid')
print(json.dumps({'schema_version':1,'task':'T10','mode':'baseline','status':'baseline_profiled',
                  'server_version':'8.1.5.60','begin_has_separate_ack':False,
                  'commit_ack':{'request_opcode':8,'response_opcode':0,'sql_code':0,'wire_body_length_declared':28},
                  'rollback_ack':{'request_opcode':9,'response_opcode':0,'sql_code':0,'wire_body_length_declared':28},
                  'ddl_rollback_does_not_undo_preceding_dml':True,
                  'quoted_generated_savepoint_verified':True,
                  'ddl_response_markers_verified':True,
                  'ddl_variant_ret_types':{'alter':146,'truncate':194,'drop':139},
                  'assembly_identities':{key:{'sha256':row['assembly_sha256'],'mvid':row['assembly_mvid']}
                      for key,row in rows.items()},'run_dir':str(folder)},separators=(',',':')))
PY
    ;;
  offline)
    source_start="$(source_hash)"
    run_logged restore dotnet restore tests/W.DmProvider.TransactionTests/W.DmProvider.TransactionTests.csproj "${flags[@]}"
    run_logged test dotnet test tests/W.DmProvider.TransactionTests/W.DmProvider.TransactionTests.csproj --no-restore \
      --logger "trx;LogFileName=t10.trx" --results-directory "$run" "${flags[@]}"
    run_logged candidate_build dotnet build tools/TransactionProbe/Candidate.csproj "${flags[@]}"
    run_logged t08_regression eng/t08.sh offline
    check_run_source || failed product_source_changed 1
    python3 - "$run/t10.trx" "$run" "$source_start" <<'PY' || failed test_result_validation 1
import json,sys,xml.etree.ElementTree as ET
root=ET.parse(sys.argv[1]).getroot(); ns={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
c=root.find('t:ResultSummary/t:Counters',ns)
required=('total','executed','passed','failed','error','timeout','aborted','inconclusive',
          'passedButRunAborted','notRunnable','notExecuted','disconnected','warning',
          'completed','inProgress','pending')
if c is None or any(c.get(k) is None for k in required): raise SystemExit('T10 TRX counters missing')
counts={k:int(c.get(k)) for k in required}; summary=root.find('t:ResultSummary',ns)
if (summary is None or summary.get('outcome') not in ('Completed','Passed') or counts['total']<=0 or
    counts['passed']!=counts['total'] or counts['executed']!=counts['total'] or
    any(v!=0 for k,v in counts.items() if k not in ('total','executed','passed'))):
    raise SystemExit('T10 TRX counters invalid')
print(json.dumps({'schema_version':1,'task':'T10','mode':'offline','status':'offline_verified',
                  'integration':'integration_pending','tests':counts,'t08_regression':'verified',
                  'product_source_sha256':sys.argv[3],
                  'run_dir':sys.argv[2]},separators=(',',':')))
PY
    ;;
  real)
    source_start="$(source_hash)"
    run_logged candidate_build dotnet build tools/TransactionProbe/Candidate.csproj "${flags[@]}"
    code=0
    scripts/with-dameng-test.sh dotnet tools/TransactionProbe/bin/Candidate/net10.0/TransactionProbe.Candidate.dll candidate_verified \
      >"$run/candidate_verified.json" 2>/dev/null || code=$?
    [[ -s "$run/candidate_verified.json" ]] || failed candidate_result_missing 1
    (( code == 0 )) || failed candidate_probe "$code"
    run_logged t08_real_regression eng/t08.sh real
    check_run_source || failed product_source_changed 1
    python3 - "$run/candidate_verified.json" "$run" "$source_start" <<'PY' || failed real_result_validation 1
import hashlib,json,pathlib,sys
r=json.load(open(sys.argv[1])); folder=pathlib.Path(sys.argv[2]); cases=r.get('scenarios',{})
assembly=pathlib.Path('tools/TransactionProbe/bin/Candidate/net10.0/W.DmProvider.dll')
actual=hashlib.sha256(assembly.read_bytes()).hexdigest()
if (r.get('task')!='T10' or r.get('implementation')!='W-T10-candidate' or
    r.get('status')!='candidate_verified' or r.get('server_version')!='8.1.5.60' or
    r.get('assembly_sha256')!=actual or not r.get('assembly_mvid') or
    r.get('server_account_verified') is not True or r.get('cleanup_verified') is not True or
    r.get('final_database_state')!='random_objects_absent' or r.get('contract_failures')!=[] or
    len(cases)!=10):
    raise SystemExit('candidate TX01-05/07-08 envelope invalid')
print(json.dumps({'schema_version':1,'task':'T10','mode':'real','status':'candidate_verified',
                  'server_version':'8.1.5.60','assembly_sha256':actual,'assembly_mvid':r['assembly_mvid'],
                  'scenario_count':len(cases),'t08_real_regression':'verified',
                  'product_source_sha256':sys.argv[3],
                  'final_database_state':'random_objects_absent','run_dir':str(folder)},separators=(',',':')))
PY
    ;;
esac
