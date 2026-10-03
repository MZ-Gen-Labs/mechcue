"""Native document recovery checks in NEW files; preserves pre-existing user windows."""
import argparse,json,pathlib,subprocess,queue,threading,os,sys
p=argparse.ArgumentParser();p.add_argument('--mcp',required=True);p.add_argument('--output',required=True);a=p.parse_args()
root=pathlib.Path(a.output).resolve();root.mkdir(parents=True,exist_ok=False);sys.stdout.reconfigure(encoding='utf-8')
env=os.environ.copy();env['MECHCUE_MCP_SETTINGS_PATH']=str(root/'access.json');env['MECHCUE_MCP_NO_TRAY']='1';(root/'access.json').write_text('{"schema":1,"mode":"write"}')
startup=subprocess.STARTUPINFO();startup.dwFlags|=subprocess.STARTF_USESHOWWINDOW;startup.wShowWindow=0
server=subprocess.Popen([str(pathlib.Path(a.mcp).resolve())],stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=subprocess.PIPE,text=True,encoding='utf-8',env=env,startupinfo=startup)
q=queue.Queue();history=[];seq=0;original=None;checks=[]
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
def passed(message):checks.append(message);print('PASS '+message,flush=True)
try:
 req('initialize',dict(protocolVersion='2025-11-25',capabilities={},clientInfo=dict(name='document recovery',version='1')))
 server.stdin.write('{"jsonrpc":"2.0","method":"notifications/initialized"}\n');server.stdin.flush()
 original=tool('solidedge_get_document');before=tool('solidedge_get_application_state')
 assert not original['dirty'],'Save your active document before running the native regression'
 caps=tool('mechcue_get_capabilities');assert caps['releaseVersion'].startswith('0.3.1') and caps['reconnectAfterUpdate']['automatic']==False
 doc=tool('solidedge_new_document',kind='part')['fullName']
 tool('solidedge_extrude_profile',expectedDocument=doc,shape='rectangle',widthMm=20,heightMm=20,depthMm=10,featureName='block')
 part=str(root/'block.par');tool('solidedge_save_document',expectedDocument=doc,outputPath=part)
 tool('solidedge_close_document',expectedDocument=part,returnDocument=original['fullName'])
 doc=tool('solidedge_new_document',kind='assembly')['fullName']
 tool('solidedge_place_part',expectedDocument=doc,filePath=part)
 child=str(root/'child.asm');tool('solidedge_save_document',expectedDocument=doc,outputPath=child)
 tool('solidedge_close_document',expectedDocument=child,returnDocument=original['fullName'])
 doc=tool('solidedge_new_document',kind='assembly')['fullName']
 tool('solidedge_place_part',expectedDocument=doc,filePath=child)
 assembly=str(root/'root.asm');tool('solidedge_save_document',expectedDocument=doc,outputPath=assembly)
 tool('solidedge_set_custom_property',expectedDocument=assembly,name='RecoveryTest',value='unsaved')
 refused=tool('solidedge_open_document',filePath=original['fullName'],error=True)
 assert 'document_switch_requires_save' in str(refused) and 'mutationStarted' in str(refused)
 assert tool('solidedge_get_document')['fullName']==assembly
 passed('dirty active assembly rejected before document activation')
 tool('solidedge_save_document',expectedDocument=assembly)
 tool('solidedge_open_document',filePath=original['fullName']);tool('solidedge_open_document',filePath=assembly)
 passed('explicit save permits document switching')
 nodes=tool('solidedge_get_assembly_tree',expectedDocument=assembly)['nodes']
 node=next(n for n in nodes if n['FileName'].lower()==part.lower())
 tool('solidedge_position_nested_part',expectedDocument=assembly,keyPath=node['KeyPath'],xMm=5,yMm=0,zMm=0,modifySharedSubassembly=True)
 refused=tool('solidedge_open_document',filePath=original['fullName'],error=True)
 assert 'document_switch_requires_save' in str(refused)
 tool('solidedge_save_referenced_document',expectedDocument=assembly,documentPath=original['fullName'],error=True)
 result=tool('solidedge_save_referenced_document',expectedDocument=assembly,documentPath=child)
 assert result['saved'] and result['dirty']==False and result['activeDocument']==assembly
 passed('hidden referenced assembly saved explicitly; unrelated document refused')
 tool('solidedge_save_document',expectedDocument=assembly)
 tool('solidedge_close_document',expectedDocument=assembly,returnDocument=original['fullName'])
 after=tool('solidedge_get_application_state');current=tool('solidedge_get_document')
 assert current['fullName']==original['fullName'] and current['dirty']==original['dirty']
 assert sum(d['windowCount'] for d in before['documents'])==sum(d['windowCount'] for d in after['documents'])
 passed('original document and window count preserved; temporary windows closed')
 (root/'summary.json').write_text(json.dumps(dict(passed=True,checks=checks),indent=2))
finally:
 server.terminate();server.wait(timeout=10)
