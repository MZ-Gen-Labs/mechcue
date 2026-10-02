"""Native workflow extension checks. Only creates new test files, never quits user CAD."""
import argparse,json,subprocess,queue,threading,pathlib,os
p=argparse.ArgumentParser();p.add_argument('--mcp',required=True);p.add_argument('--output',required=True);a=p.parse_args()
root=pathlib.Path(a.output).resolve();root.mkdir(exist_ok=False,parents=True)
env=os.environ.copy();env['MECHCUE_MCP_SETTINGS_PATH']=str(root/'access.json');env['MECHCUE_MCP_NO_TRAY']='1'
(root/'access.json').write_text('{"schema":1,"mode":"write"}')
startup=subprocess.STARTUPINFO();startup.dwFlags|=subprocess.STARTF_USESHOWWINDOW;startup.wShowWindow=0
server=subprocess.Popen([str(pathlib.Path(a.mcp).resolve())],stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=subprocess.PIPE,text=True,encoding='utf-8',env=env,startupinfo=startup)
replies=queue.Queue();history=[];seq=0
threading.Thread(target=lambda:[replies.put(json.loads(s)) for s in server.stdout],daemon=True).start()
threading.Thread(target=lambda:[None for s in server.stderr],daemon=True).start()
def request(method,params):
 global seq
 seq+=1;server.stdin.write(json.dumps(dict(jsonrpc='2.0',id=seq,method=method,params=params))+'\n');server.stdin.flush()
 while True:
  reply=replies.get(timeout=150)
  if reply.get('id')==seq:return reply['result']
def tool(toolname,error=False,**args):
 r=request('tools/call',dict(name=toolname,arguments=args));history.append(dict(tool=toolname,args=args,result=r))
 (root/'results.json').write_text(json.dumps(history,ensure_ascii=False,indent=2),encoding='utf-8')
 assert bool(r.get('isError'))==error,(toolname,r)
 if error:return r
 return json.loads(''.join(c.get('text','') for c in r['content']))
try:
 request('initialize',dict(protocolVersion='2025-11-25',capabilities={},clientInfo=dict(name='workflow native test',version='1')))
 server.stdin.write('{"jsonrpc":"2.0","method":"notifications/initialized"}\n');server.stdin.flush()
 state=tool('solidedge_get_application_state');assert state['state'] in ['ready','ready_no_document'],state
 reused=tool('solidedge_start_application');assert reused['state']=='reused' and reused['processId']==state['processId']
 tool('solidedge_exit_application',error=True,expectedProcessId=state['processId']+1)
 original=tool('solidedge_get_document')['fullName']
 part=tool('solidedge_new_document',kind='part')['fullName']
 tool('solidedge_extrude_profile',expectedDocument=part,shape='rectangle',widthMm=20,heightMm=20,depthMm=10)
 path=str(root/'test-part.par');tool('solidedge_save_document',expectedDocument=part,outputPath=path)
 variables=tool('solidedge_list_variables',expectedDocument=path)['variables']
 v=next(v for v in variables if v['unitsType']==1 and abs(v['value']-.01)<1e-8)
 tool('solidedge_set_variables',expectedDocument=path,variablesJson=json.dumps([dict(name=v['name'],value=12,unit='mm')]))
 reread=tool('solidedge_list_variables',expectedDocument=path)['variables'];assert abs(next(x for x in reread if x['name']==v['name'])['value']-.012)<1e-8
 dirty=tool('solidedge_exit_application',error=True,expectedProcessId=state['processId']);assert 'unsaved' in str(dirty)
 tool('solidedge_set_custom_property',expectedDocument=path,name='McpWorkflowTest',value='verified')
 assert any(p['name']=='McpWorkflowTest' and p['value']=='verified' for p in tool('solidedge_get_custom_properties',expectedDocument=path)['properties'])
 tool('solidedge_save_document',expectedDocument=path)
 child=tool('solidedge_new_document',kind='assembly')['fullName']
 for x in [-50,50]:tool('solidedge_place_part',expectedDocument=child,filePath=path,xMm=x)
 childpath=str(root/'child.asm');tool('solidedge_save_document',expectedDocument=child,outputPath=childpath)
 assembly=tool('solidedge_new_document',kind='assembly')['fullName'];tool('solidedge_place_part',expectedDocument=assembly,filePath=childpath)
 assemblypath=str(root/'test-root.asm');tool('solidedge_save_document',expectedDocument=assembly,outputPath=assemblypath)
 tree=tool('solidedge_get_assembly_tree',expectedDocument=assemblypath);key=next(n['KeyPath'] for n in tree['nodes'] if n['Depth']==2)
 tool('solidedge_position_nested_part',error=True,expectedDocument=assemblypath,keyPath=key,xMm=-60,yMm=0,zMm=0)
 tool('solidedge_position_nested_part',expectedDocument=assemblypath,keyPath=key,xMm=-60,yMm=0,zMm=0,modifySharedSubassembly=True)
 assert abs(next(n['LocalMatrix'][12] for n in tool('solidedge_get_assembly_tree',expectedDocument=assemblypath)['nodes'] if n['KeyPath']==key)+.06)<1e-8
 bom=tool('solidedge_get_bom',expectedDocument=assemblypath);assert bom['leafOccurrenceCount']==2 and bom['rows'][0]['quantity']==2
 tool('solidedge_open_document',filePath=childpath);tool('solidedge_save_document',expectedDocument=childpath)
 tool('solidedge_open_document',filePath=assemblypath);tool('solidedge_save_document',expectedDocument=assemblypath)
 if pathlib.Path(original).is_file():tool('solidedge_open_document',filePath=original)
 print('PASS: native lifecycle reuse/identity/dirty guards, dimensions, metadata, stable nested position and BOM')
finally:
 server.terminate();server.wait(timeout=10)
