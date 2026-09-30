#!/usr/bin/env python3
"""Strict T11 candidate ADO and concurrency contracts for the captured server profile."""
import json,pathlib,sys
if len(sys.argv) not in (4,5):raise SystemExit('usage')
mode,path,sha=sys.argv[1:4];entry=sys.argv[4] if len(sys.argv)==5 else 'internal_profile'
r=json.loads(pathlib.Path(path).read_text())
if (mode not in ('matrix','litmus') or entry not in ('internal_profile','public') or r.get('entry')!=entry or r.get('implementation')!='W-T11-candidate' or r.get('assembly_sha256')!=sha or
    not r.get('assembly_mvid') or r.get('server_version')!='8.1.5.60' or r.get('status')!='matrix_observed' or
    r.get('server_account_verified') is not True or r.get('cleanup_verified') is not True or
    r.get('final_database_state')!='unique_objects_absent' or len(r.get('levels',[]))!=3):
 raise SystemExit('candidate envelope rejected')
issues=[]
def reject(case,reason):issues.append({'case':case,'reason':reason})
if mode=='matrix':
 shapes={'single_update','update_rowcount','insert_key','savepoint_keyword','savepoint_ef','repeated_prepare'}
 for level,name in zip(r['levels'],('ReadCommitted','ReadUncommitted','Serializable')):
  if (level.get('isolation')!=name or level.get('begin_succeeded') is not True or
      level.get('control_statement_retained') is not False or level.get('public_reset_read_committed') is not True or
      len(level.get('cases',[]))!=12):reject(name,'begin_or_reset_or_case_count')
  if name!='ReadCommitted':
   trace=level.get('control_trace',[])
   set_index=next((i for i,t in enumerate(trace) if t.get('request_opcode')==5 and t.get('sql_code')==0),None)
   close_index=next((i for i,t in enumerate(trace) if t.get('request_opcode')==4 and t.get('sql_code')==0 and t.get('response_opcode')==0),None)
   if set_index is None or close_index is None or close_index<=set_index:
    reject(name,'verified_SET_then_STMT_CLOSE_not_observed')
  combos={(c.get('shape'),c.get('parameterized')) for c in level.get('cases',[])}
  if combos!={(shape,parameters) for shape in shapes for parameters in (False,True)}:reject(name,'matrix_shapes_missing')
  for c in level.get('cases',[]):
   case=f"{name}:{c.get('shape')}:{c.get('parameterized')}"
   if (c.get('functional_contract') is not True or c.get('entry')!=entry or c.get('causal_intervention')!='none' or
       c.get('control_statement_retained') is not False or c.get('connection_closed') is not True or
       c.get('failure') is not None or c.get('end_failure') is not None or c.get('final_failure') is not None or
       c.get('final_value')!=(2 if c.get('shape')=='repeated_prepare' else 0) or c.get('final_insert_rows')!=0 or
       c.get('transaction_outcome')!=('Committed' if c.get('shape')=='repeated_prepare' else 'RolledBack')):
    reject(case,'value_or_transaction_or_end_state')
   commands=c.get('commands',[])
   for command in commands:
    s=command.get('snapshot',{})
    if s.get('statement_present') is True and (s.get('owner_is_caller') is not True or s.get('inherited_transaction_statement') is not False):
     reject(case,'foreign_statement_owner')
   live=[x['snapshot'] for x in commands if x.get('phase') in ('after_execute_reader','before_next_result','after_prepare') and x.get('snapshot',{}).get('statement_present')]
   if c.get('shape') in ('update_rowcount','insert_key','savepoint_keyword','savepoint_ef','repeated_prepare') and not live:
    reject(case,'live_statement_snapshot_missing')
   reader_snapshots=[x.get('snapshot',{}) for x in commands if x.get('phase') in ('after_execute_reader','before_next_result')]
   if c.get('shape') in ('update_rowcount','insert_key','savepoint_keyword','savepoint_ef'):
    if not reader_snapshots or any(s.get('statement_present') is not True or s.get('holder')!='reader' or
        s.get('reader_open') is not True or s.get('dual_statement_reference') is not False or
        s.get('owner_is_caller') is not True or s.get('inherited_transaction_statement') is not False or
        s.get('captured_session_available') is not True or s.get('captured_session_matches_statement') is not True or
        any(type((s.get('captured_operation') or {}).get(k)) is not int or
            (s.get('captured_operation') or {}).get(k,0)<=0 for k in ('session_id','lease_generation','execution_id'))
        for s in reader_snapshots):reject(case,'reader_holder_or_captured_session_invalid')
   values=[v for item in c.get('results',[]) for rowset in item.get('rowsets',[]) for v in rowset.get('values',[])]
   if c.get('shape') in ('update_rowcount','savepoint_keyword','savepoint_ef') and values!=[1,1]:reject(case,'ROWCOUNT_values')
   if c.get('shape')=='insert_key' and (len(values)!=2 or any(x<=0 for x in values) or values[0]==values[1]):reject(case,'identity_key_values')
else:
 for level in r['levels']:
  name=level.get('isolation')
  if level.get('functional_contract') is not True or level.get('entry')!=entry or level.get('failure') is not None or level.get('worker_joined') is not True or level.get('fresh_final_value')!=1:
   reject(name,'litmus_or_worker_or_final_state')
  if name in ('ReadCommitted','ReadUncommitted'):
   read=level.get('first_read') or {}
   if (level.get('observer_execute_sent') is not True or read.get('Failure') is not None or read.get('Value') not in (0,1) or
       level.get('committed_reread')!=1 or level.get('writer_commit_confirmed') is not True or level.get('observer_end_confirmed') is not True):
    reject(name,'visibility_chain')
   if name=='ReadCommitted' and level.get('first_completed_before_writer_commit') is True and read.get('Value')!=0:reject(name,'dirty_read')
  elif name=='Serializable':
   writer=level.get('writer') or {}
   if (level.get('writer_execute_sent') is not True or level.get('first_read')!=0 or level.get('repeat_read')!=0 or
       writer.get('Committed') is not True or writer.get('Affected')!=1 or writer.get('Failure') is not None or
       level.get('reader_end_confirmed') is not True or level.get('strategy')=='ambiguous_completion_requires_review'):
    reject(name,'serializable_repeat_or_writer_end')
  else:reject(str(name),'unexpected_isolation')
 if {x.get('isolation') for x in r['levels']}!={'ReadCommitted','ReadUncommitted','Serializable'}:reject('all','levels_missing')
if issues:
 print(json.dumps({'task':'T11','mode':mode,'status':'rejected','issues':issues},separators=(',',':')));raise SystemExit(1)
print(json.dumps({'task':'T11','mode':mode,'status':'candidate_'+mode+'_verified','entry':entry,'assembly_sha256':sha,
 'scope':'observed_SERVER_8_1_5_60_ADO_and_statement_schedule_only;EF_and_public_capability_gate_pending',
 'final_database_state':'unique_objects_absent'},separators=(',',':')))
