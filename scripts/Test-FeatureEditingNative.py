"""Native machining feature checks in NEW files; preserves pre-existing user windows."""
import argparse,json,pathlib,subprocess,queue,threading,os,sys
from pathlib import Path
p=argparse.ArgumentParser();p.add_argument('--mcp',required=True);p.add_argument('--output',required=True);a=p.parse_args()
root=pathlib.Path(a.output).resolve();root.mkdir(parents=True,exist_ok=False);sys.stdout.reconfigure(encoding='utf-8')
env=os.environ.copy();env['MECHCUE_MCP_SETTINGS_PATH']=str(root/'access.json');env['MECHCUE_MCP_NO_TRAY']='1';(root/'access.json').write_text('{"schema":1,"mode":"write"}')
startup=subprocess.STARTUPINFO();startup.dwFlags|=subprocess.STARTF_USESHOWWINDOW;startup.wShowWindow=0
server=subprocess.Popen([str(pathlib.Path(a.mcp).resolve())],stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=subprocess.PIPE,text=True,encoding='utf-8',env=env,startupinfo=startup)
q=queue.Queue();history=[];seq=0;original=None;checks=[];owned_control=False
threading.Thread(target=lambda:[q.put(json.loads(s)) for s in server.stdout],daemon=True).start()
threading.Thread(target=lambda:[None for s in server.stderr],daemon=True).start()
def req(method,params):
 global seq
 seq+=1;server.stdin.write(json.dumps(dict(jsonrpc='2.0',id=seq,method=method,params=params))+'\n');server.stdin.flush()
 while True:
  r=q.get(timeout=150)
  if r.get('id')==seq:
   if 'error' in r:raise RuntimeError(r['error'])
   return r['result']
def tool(tool_name,error=False,**args):
 r=req('tools/call',dict(name=tool_name,arguments=args));history.append(dict(tool=tool_name,args=args,result=r));(root/'results.json').write_text(json.dumps(history,ensure_ascii=False,indent=2),encoding='utf-8')
 assert bool(r.get('isError'))==error,(tool_name,r)
 if error:return r
 return json.loads(''.join(c.get('text','') for c in r['content']))
def passed(message):checks.append(message);print('PASS '+message,flush=True)
def block(name,driving=False):
 doc=tool('solidedge_new_document',kind='part')['fullName']
 tool('solidedge_extrude_profile',expectedDocument=doc,shape='rectangle',widthMm=100,heightMm=100,depthMm=20,xMm=-50,yMm=-50,direction='positive',featureName='base',drivingDimensions=driving)
 path=root/(name+'.par');tool('solidedge_save_document',expectedDocument=doc,outputPath=str(path));return str(path)
def finish(doc):
 tool('solidedge_save_document',expectedDocument=doc);tool('solidedge_close_document',expectedDocument=doc,returnDocument=original['fullName'])
def span(doc,axis):
 points=[p for e in tool('solidedge_list_edges',expectedDocument=doc)['edges'] for p in e['boundsMm']];return max(p[axis] for p in points)-min(p[axis] for p in points)
try:
 req('initialize',dict(protocolVersion='2025-11-25',capabilities={},clientInfo=dict(name='feature edit regression',version='1')));server.stdin.write('{"jsonrpc":"2.0","method":"notifications/initialized"}\n');server.stdin.flush()
 baseline=tool('solidedge_get_application_state')
 if len(baseline['documents'])==0:
  control=tool('solidedge_new_document',kind='part')['fullName'];tool('solidedge_save_document',expectedDocument=control,outputPath=str(root/'control.par'));owned_control=True
 original=tool('solidedge_get_document');before=tool('solidedge_get_application_state');assert not original['dirty']
 doc=block('edits');common=dict(expectedDocument=doc,planeOffsetMm=20,direction='negative')
 tool('solidedge_create_hole',**common,centersJson='[{"x":-25,"y":-25}]',diameterMm=6,holeType='counterbore',counterboreDiameterMm=12,counterboreDepthMm=4,featureName='seed')
 tool('solidedge_pattern_hole',expectedDocument=doc,sourceFeatureName='seed',xCount=2,yCount=2,xSpacingMm=30,ySpacingMm=30,featureName='pattern')
 details=tool('solidedge_get_feature_details',expectedDocument=doc,featureName='seed');assert details['dependenciesComplete'] and any(d['name']=='pattern' for d in details['dependents'])
 assert details['profile']['centersMm']==[[-25,-25]] and abs(details['profile']['originMm'][2]-20)<1e-5
 tool('solidedge_delete_machining_feature',expectedDocument=doc,featureName='seed',backupPath=str(root/'refused.par'),error=True);assert not (root/'refused.par').exists()
 passed('native dependencies, hole entry plane/center and dependent deletion refusal')
 tool('solidedge_edit_hole',expectedDocument=doc,featureName='seed',changesJson=json.dumps(dict(diameterMm=7,counterboreDiameterMm=14,counterboreDepthMm=5,centerXmm=-20,centerYmm=-20)))
 d=tool('solidedge_get_feature_details',expectedDocument=doc,featureName='seed');assert d['hole']['diameterMm']==7 and d['profile']['centersMm']==[[-20,-20]]
 tool('solidedge_edit_hole_pattern',expectedDocument=doc,featureName='pattern',changesJson=json.dumps(dict(xCount=3,yCount=2,xSpacingMm=20,ySpacingMm=30)))
 d=tool('solidedge_get_feature_details',expectedDocument=doc,featureName='pattern');assert d['pattern']['instanceCount']==6 and d['pattern']['inputs'][0]['name']=='seed'
 passed('native hole size/location edits preserve dependent pattern; rectangular count/spacing edit')
 tool('solidedge_edit_hole',expectedDocument=doc,featureName='seed',changesJson='{"centerXmm":500}',error=True)
 d=tool('solidedge_get_feature_details',expectedDocument=doc,featureName='seed');assert d['profile']['centersMm']==[[-20,-20]] and d['status']==1216476310
 tool('solidedge_edit_hole_pattern',expectedDocument=doc,featureName='pattern',changesJson='{"xSpacingMm":500}',error=True)
 d=tool('solidedge_get_feature_details',expectedDocument=doc,featureName='pattern');assert d['pattern']['layout']['xSpacingMm']==20 and d['status']==1216476310
 passed('out-of-solid hole/pattern edits fail and restore healthy original values')
 tool('solidedge_delete_machining_feature',expectedDocument=doc,featureName='pattern',backupPath=str(root/'before-pattern-delete.par'))
 tool('solidedge_delete_machining_feature',expectedDocument=doc,featureName='seed',backupPath=str(root/'before-hole-delete.par'))
 assert len(tool('solidedge_list_features',expectedDocument=doc)['features'])==1
 passed('explicit dependent then seed deletion; recovery copies created')
 tool('solidedge_extrude_profile',expectedDocument=doc,shape='rectangle',operation='cut',planeNumber=1,widthMm=10,heightMm=12,xMm=-5,yMm=-6,depthMm=5,featureName='pocket')
 d=tool('solidedge_get_feature_details',expectedDocument=doc,featureName='pocket');assert d['kind']=='extruded-cutout' and abs(d['depthMm']-5)<1e-5 and len(d['profile']['lines'])==4
 passed('native cutout plane, side, depth and complete rectangle lines')
 finish(doc)
 doc=block('tapped');tool('solidedge_create_hole',expectedDocument=doc,planeOffsetMm=20,direction='negative',centersJson='[{"x":0,"y":0}]',diameterMm=6.647,extent='finite',depthMm=15,threadDescription='M8',threadDepthMm=10,featureName='tap')
 tool('solidedge_edit_hole',expectedDocument=doc,featureName='tap',changesJson='{"diameterMm":6.8,"depthMm":16,"threadDepthMm":12}')
 d=tool('solidedge_list_holes',expectedDocument=doc)['holes'][0];assert abs(d['effectiveBoreDiameterMm']-6.8)<1e-5 and d['depthMm']==16 and d['threadDepthMm']==12 and d['threadDescription']=='M8'
 tool('solidedge_edit_hole',expectedDocument=doc,featureName='tap',changesJson='{"threadDepthMm":19}',error=True)
 passed('finite native tapped diameter/depth edits and thread depth guard');finish(doc)
 tool('solidedge_open_document',filePath=doc)
 tool('solidedge_set_hole_thread',expectedDocument=doc,featureName='tap',threadDescription='M10',tapDrillDiameterMm=8.5,threadDepthMm=10)
 h=tool('solidedge_list_holes',expectedDocument=doc)['holes'][0];assert h['threadDescription']=='M10' and abs(h['effectiveBoreDiameterMm']-8.5)<1e-5 and h['threadNominalDiameterMm']==10
 tool('solidedge_set_hole_thread',expectedDocument=doc,featureName='tap',threadDescription='',tapDrillDiameterMm=8.5)
 assert tool('solidedge_list_holes',expectedDocument=doc)['holes'][0]['treatmentType']==44
 tool('solidedge_set_hole_thread',expectedDocument=doc,featureName='tap',threadDescription='M8',tapDrillDiameterMm=6.8,threadDepthMm=12)
 passed('native thread designation replacement/removal/reapplication preserves feature');finish(doc)
 tool('solidedge_open_document',filePath=doc)
 h=tool('solidedge_list_holes',expectedDocument=doc)['holes'][0];assert h['threadDescription']=='M8' and h['depthMm']==16 and h['threadDepthMm']==12 and abs(h['effectiveBoreDiameterMm']-6.8)<1e-5
 passed('native thread, finite hole depth and effective bore persist after save/reopen');finish(doc)
 doc=tool('solidedge_new_document',kind='part')['fullName'];tool('solidedge_extrude_profile',expectedDocument=doc,shape='rectangle',widthMm=10,heightMm=10,xMm=-5,yMm=-5,depthMm=20,featureName='small-base');path=str(root/'thread-rollback.par');tool('solidedge_save_document',expectedDocument=doc,outputPath=path);doc=path
 tool('solidedge_create_hole',expectedDocument=doc,planeOffsetMm=20,direction='negative',centersJson='[{"x":0,"y":0}]',diameterMm=3.3,threadDescription='M4',featureName='tap')
 old=tool('solidedge_list_holes',expectedDocument=doc)['holes'][0]
 m20=next(t for t in tool('solidedge_list_metric_threads',nominalDiameterMm=20)['threads'] if t['Description']=='M20')
 tool('solidedge_set_hole_thread',expectedDocument=doc,featureName='tap',threadDescription='M20',tapDrillDiameterMm=m20['InternalMinorDiameterMm'],error=True)
 restored=tool('solidedge_list_holes',expectedDocument=doc)['holes'][0];assert old==restored,(old,restored)
 assert all(f['status']==1216476310 for f in tool('solidedge_list_features',expectedDocument=doc)['features'])
 passed('invalid solid-removing thread edit restores complete original native hole data and health');finish(doc)
 doc=block('circular');tool('solidedge_create_hole',expectedDocument=doc,planeOffsetMm=20,direction='negative',centersJson='[{"x":25,"y":0}]',diameterMm=6,featureName='seed')
 tool('solidedge_pattern_hole',expectedDocument=doc,sourceFeatureName='seed',patternType='circular',radialCount=4,angleSpacingDeg=90,featureName='circle-pattern')
 tool('solidedge_edit_hole_pattern',expectedDocument=doc,featureName='circle-pattern',changesJson='{"angleSpacingDeg":80,"radialCount":3}')
 passed('circular native angular spacing edit');finish(doc)
 tool('solidedge_open_document',filePath=doc);assert abs(tool('solidedge_get_feature_details',expectedDocument=doc,featureName='circle-pattern')['pattern']['layout']['angleSpacingDeg']-80)<1e-5;finish(doc)
 doc=tool('solidedge_new_document',kind='assembly')['fullName'];asm=str(root/'pairs.asm');tool('solidedge_save_document',expectedDocument=doc,outputPath=asm);doc=asm
 tool('solidedge_place_part',expectedDocument=doc,filePath=str(root/'edits.par'))
 tool('solidedge_place_part',expectedDocument=doc,filePath=str(root/'edits.par'),xMm=40)
 tool('solidedge_place_part',expectedDocument=doc,filePath=str(root/'edits.par'),xMm=200)
 result=tool('solidedge_check_interference_pairs',expectedDocument=doc,includeVolume=True);assert result['analysisComplete'] and not result['clear'] and result['totalPairs']==3 and len(result['pairs'])==1
 pair=result['pairs'][0];assert pair['first']['keyPath']!=pair['second']['keyPath'] and pair['first']['fileName']==pair['second']['fileName']
 assert pair['volumeAvailable'] and pair['volumeMm3']>0,pair
 scoped=tool('solidedge_check_interference_pairs',expectedDocument=doc,set1Json='[1]',set2Json='[3]');assert scoped['clear'] and scoped['totalPairs']==1
 tool('solidedge_check_interference_pairs',expectedDocument=doc,maxPairs=1,error=True)
 passed('instance-specific structured collision pair, scoped clear and pre-analysis budget guard');finish(doc)
 doc=block('clearance');tool('solidedge_create_hole',expectedDocument=doc,planeOffsetMm=20,direction='negative',centersJson='[{"x":0,"y":0}]',diameterMm=9,holeType='counterbore',counterboreDiameterMm=16,counterboreDepthMm=5,featureName='clearance');finish(doc)
 doc=tool('solidedge_new_document',kind='assembly')['fullName'];asm=str(root/'interface.asm');tool('solidedge_save_document',expectedDocument=doc,outputPath=asm);doc=asm
 tool('solidedge_place_part',expectedDocument=doc,filePath=str(root/'clearance.par'),zMm=20)
 tool('solidedge_place_part',expectedDocument=doc,filePath=str(root/'tapped.par'))
 nodes=tool('solidedge_get_assembly_tree',expectedDocument=doc)['nodes']
 inputs=dict(expectedDocument=doc,firstKeyPath=nodes[0]['KeyPath'],firstHoleName='clearance',secondKeyPath=nodes[1]['KeyPath'],secondHoleName='tap',boltDiameterMm=8,stackThicknessMm=20,boltUnderHeadLengthMm=30,requiredEngagementMm=8,headDiameterMm=13,headHeightMm=5)
 result=tool('solidedge_check_hole_interface',**inputs);assert result['assessmentComplete'] and result['allSpecifiedChecksPass'],result
 bad=tool('solidedge_check_hole_interface',**{**inputs,'boltUnderHeadLengthMm':40});assert not bad['allSpecifiedChecksPass'] and bad['checks']['doesNotBottomOut']==False
 tool('solidedge_position_part',expectedDocument=doc,partNumber=2,xMm=1,yMm=0,zMm=0)
 bad=tool('solidedge_check_hole_interface',**inputs);assert bad['checks']['coaxial']==False
 passed('referenced native hole interface aligns; shortfall/bottoming and offset fail without opening children');finish(doc)
 current=tool('solidedge_get_document');after=tool('solidedge_get_application_state');assert current['fullName']==original['fullName'] and current['dirty']==original['dirty'];assert sum(d['windowCount'] for d in before['documents'])==sum(d['windowCount'] for d in after['documents'])
 passed('original CAD document/windows preserved; test parts saved and closed')
 if owned_control:
  tool('solidedge_close_document',expectedDocument=original['fullName']);assert len(tool('solidedge_get_application_state')['documents'])==0
  passed('owned control part closed; Solid Edge returned to no-document state')
 (root/'summary.json').write_text(json.dumps(dict(passed=True,checks=checks),indent=2))
finally:
 if owned_control and original:
  try:
   state=tool('solidedge_get_application_state')
   if state['documents']:
    current=tool('solidedge_get_document')['fullName']
    if Path(current).resolve().is_relative_to(root):
     if Path(current).resolve()!=Path(original['fullName']).resolve():
      tool('solidedge_save_document',expectedDocument=current);tool('solidedge_close_document',expectedDocument=current,returnDocument=original['fullName'])
     tool('solidedge_close_document',expectedDocument=original['fullName'])
  except Exception as cleanup:print('OWNED TEST CLEANUP NEEDS ATTENTION '+str(cleanup),flush=True)
 server.terminate();server.wait(timeout=10)
