"""Native detail tools, guard and temporary-window checks; never closes pre-existing user documents."""
import argparse,json,pathlib,subprocess,queue,threading,os,sys,time
p=argparse.ArgumentParser();p.add_argument('--mcp',required=True);p.add_argument('--output',required=True);a=p.parse_args()
root=pathlib.Path(a.output).resolve();root.mkdir(parents=True,exist_ok=False);sys.stdout.reconfigure(encoding='utf-8')
env=os.environ.copy();env['MECHCUE_MCP_SETTINGS_PATH']=str(root/'access.json');env['MECHCUE_MCP_NO_TRAY']='1';(root/'access.json').write_text('{"schema":1,"mode":"write"}')
startup=subprocess.STARTUPINFO();startup.dwFlags|=subprocess.STARTF_USESHOWWINDOW;startup.wShowWindow=0
server=subprocess.Popen([str(pathlib.Path(a.mcp).resolve())],stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=subprocess.PIPE,text=True,encoding='utf-8',env=env,startupinfo=startup)
q=queue.Queue();history=[];seq=0;original=None
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
def tool(name,error=False,**args):
 r=req('tools/call',dict(name=name,arguments=args));history.append(dict(tool=name,args=args,result=r));(root/'results.json').write_text(json.dumps(history,ensure_ascii=False,indent=2),encoding='utf-8')
 assert bool(r.get('isError'))==error,(name,r)
 if error:return r
 return json.loads(''.join(c.get('text','') for c in r['content']))
def save(doc,path=None):return tool('solidedge_save_document',expectedDocument=doc,**({'outputPath':str(path)} if path else {}))
def openfile(path):return tool('solidedge_open_document',filePath=str(path))
def close(doc,back):return tool('solidedge_close_document',expectedDocument=doc,returnDocument=back)
def window_count(state):return sum(d['windowCount'] for d in state['documents'])
try:
 init=req('initialize',dict(protocolVersion='2025-11-25',capabilities={},clientInfo=dict(name='native detail tools',version='1')));assert 'solidedge_close_document' in init['instructions']
 server.stdin.write('{"jsonrpc":"2.0","method":"notifications/initialized"}\n');server.stdin.flush()
 original=tool('solidedge_get_document');orig=original['fullName'];before=tool('solidedge_get_application_state');print('USER DOCUMENT RETAINED '+orig,flush=True)
 caps=tool('mechcue_get_capabilities');assert caps['accessMode']=='write'
 doc=tool('solidedge_new_document',kind='part')['fullName'];tool('solidedge_close_document',expectedDocument=doc,error=True)
 tool('solidedge_extrude_profile',expectedDocument=doc,shape='rectangle',widthMm=20,heightMm=30,depthMm=10,xMm=-10,yMm=-15,direction='symmetric',featureName='block')
 path=root/'block.par';save(doc,path);doc=str(path)
 planes=tool('solidedge_list_planes',expectedDocument=doc)['planes'];assert len(planes)==3 and all(len(p['normal'])==3 and len(p['xDirection'])==3 and len(p['originMm'])==3 for p in planes)
 tool('solidedge_edit_extrusion_profile',expectedDocument=doc,featureName='block',widthMm=40,heightMm=50)
 edges=tool('solidedge_list_edges',expectedDocument=doc)['edges'];assert len(edges)==12
 points=[point for e in edges for point in e['boundsMm']];assert abs(max(p[0] for p in points)-min(p[0] for p in points)-40)<1e-6 and abs(max(p[1] for p in points)-min(p[1] for p in points)-50)<1e-6
 tool('solidedge_round_edges',expectedDocument=doc,edgeIdsJson='["stale"]',radiusMm=1,error=True)
 ids=json.dumps([e['edgeId'] for e in edges]);tool('solidedge_round_edges',expectedDocument=doc,edgeIdsJson=ids,radiusMm=1,featureName='R1')
 tool('solidedge_close_document',expectedDocument=doc,returnDocument=orig,error=True)
 save(doc);close(doc,orig);assert tool('solidedge_get_document')['fullName']==orig;print('PASS rectangle resize, plane frames, keyed rounds, dirty close refusal and return',flush=True)
 doc=tool('solidedge_new_document',kind='part')['fullName'];tool('solidedge_extrude_profile',expectedDocument=doc,shape='circle',radiusMm=10,depthMm=10,direction='symmetric',featureName='disk')
 path=root/'disk.par';save(doc,path);doc=str(path);tool('solidedge_edit_extrusion_profile',expectedDocument=doc,featureName='disk',radiusMm=12)
 save(doc);close(doc,orig);print('PASS circle resize and close',flush=True)
 source=tool('solidedge_create_concept_machine',type='mill5',outputDirectory=str(root/'concept'));src=source['fullName'];manifest=source['manifestPath']
 tree=tool('solidedge_get_assembly_tree',expectedDocument=src);initial_windows=window_count(tool('solidedge_get_application_state'))
 result=tool('solidedge_create_detail_assembly',expectedDocument=src,manifestPath=manifest,outputDirectory=str(root/'detail'));assert result['worldPosesPreserved'] and result['groupCount']==6 and result['leafCount']==10
 target=result['fullName'];after=tool('solidedge_get_application_state');assert window_count(after)<=initial_windows+1,'Generated subassembly windows leaked'
 detail=tool('solidedge_get_assembly_tree',expectedDocument=target);assert detail['count']==16 and not detail['truncated']
 poses=json.dumps(dict(X=100,Y=-50,Z=100,A=65,C=90));tool('solidedge_set_concept_pose',expectedDocument=target,manifestPath=result['manifestPath'],valuesJson=poses)
 moved=tool('solidedge_get_assembly_tree',expectedDocument=target);openfile(src);tool('solidedge_set_concept_pose',expectedDocument=src,manifestPath=manifest,valuesJson=poses)
 source_moved=tool('solidedge_get_assembly_tree',expectedDocument=src)
 for leaf in [n for n in moved['nodes'] if n['Depth']==2]:
  old=next(n for n in source_moved['nodes'] if n['Name']==leaf['Name']);assert all(abs(x-y)<1e-8 for x,y in zip(old['WorldMatrix'],leaf['WorldMatrix']))
 tool('solidedge_set_concept_pose',expectedDocument=src,manifestPath=manifest,valuesJson=json.dumps(dict(X=0,Y=0,Z=0,A=0,C=0)));save(src)
 openfile(target);tool('solidedge_set_concept_pose',expectedDocument=target,manifestPath=result['manifestPath'],valuesJson=json.dumps(dict(X=0,Y=0,Z=0,A=0,C=0)));save(target)
 tool('solidedge_close_document',expectedDocument='wrong.asm',returnDocument=orig,error=True);close(target,src);close(src,orig)
 current=tool('solidedge_get_document');assert current['fullName']==orig and current['dirty']==original['dirty'];assert window_count(tool('solidedge_get_application_state'))==window_count(before)
 print('PASS six modules, saved/closed children, 10 preserved leaf transforms, A/C compound pose and user windows unchanged',flush=True)
 (root/'summary.json').write_text(json.dumps(dict(passed=True,originalPreserved=True,sourceLeafCount=10,groupCount=6,windowsBefore=window_count(before),windowsAfter=window_count(tool('solidedge_get_application_state'))),indent=2))
finally:
 if original:
  try:openfile(original['fullName'])
  except Exception:pass
 server.terminate();server.wait(timeout=10)
