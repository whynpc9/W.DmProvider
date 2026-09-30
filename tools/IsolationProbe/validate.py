#!/usr/bin/env python3
"""Validate pinned T11 observation envelopes; baseline failures remain observations."""
import json,pathlib,sys
pins={
 'O':'8f6e59680d0a076df53bea50d5a2bdbd288535cd85b2d7ca5064c02adc9c6e6b',
 'R':'1bbedef16e8720227fd416421e744b7894df79082f3ddd7d5a9537c1d424fba3',
 'W-T10-frozen':'51043edb57710cb5c4aa7e13a514e1672e4783150babb9af3ddd0ed1b329323b'}
if len(sys.argv)!=3:raise SystemExit('usage')
r=json.loads(pathlib.Path(sys.argv[2]).read_text());label=sys.argv[1]
if (r.get('task')!='T11' or r.get('implementation')!=label or r.get('assembly_sha256')!=pins[label] or
    not r.get('assembly_mvid') or r.get('server_account_verified') is not True or
    r.get('cleanup_verified') is not True or r.get('final_database_state')!='unique_objects_absent' or
    r.get('status')!='matrix_observed' or len(r.get('levels',[]))!=3):raise SystemExit('baseline envelope rejected')
if label=='R' and r['assembly_mvid']!='07f0bc2a-e2dc-4e7f-bac9-349f6d75effd':raise SystemExit('R mvid rejected')
expected=['ReadCommitted','ReadUncommitted','Serializable'];summary=[]
for level,name in zip(r['levels'],expected):
 if level.get('isolation')!=name:raise SystemExit('level order rejected')
 cases=level.get('cases',[])
 if level.get('begin_succeeded') is True:
  expected_count=(4 if label in ('O','R') else 2) if r.get('matrix_scope')=='minimal_causal' else (24 if label in ('O','R') else 12)
  if len(cases)!=expected_count:raise SystemExit('matrix case count rejected')
  if any(c.get('connection_closed') is not True or c.get('final_failure') is not None for c in cases):
   raise SystemExit('case final state missing')
 elif label!='W-T10-frozen' or name=='ReadCommitted' or cases:
  raise SystemExit('unexpected Begin profile rejection')
 summary.append({'isolation':name,'begin_succeeded':level['begin_succeeded'],'case_count':len(cases),
  'functional_passes':sum(c.get('functional_contract') is True for c in cases),
  'natural_failures':sum(c.get('causal_intervention')=='none' and c.get('functional_contract') is False for c in cases),
  'causal_failures':sum(c.get('causal_intervention')!='none' and c.get('functional_contract') is False for c in cases)})
print(json.dumps({'task':'T11','status':'baseline_profiled','implementation':label,'levels':summary,
 'candidate_acceptance':False,'scope':'ADO_matrix_only;server_litmus_and_EF_pending'},separators=(',',':')))
