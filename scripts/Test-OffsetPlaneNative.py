"""Native plane/thread checks in NEW files; preserves pre-existing user windows."""
import argparse,json,pathlib,subprocess,queue,threading,os,sys
p=argparse.ArgumentParser();p.add_argument('--mcp',required=True);p.add_argument('--output',required=True);p.add_argument('--reference');a=p.parse_args()
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
def tool(tool_name,error=False,**args):
 r=req('tools/call',dict(name=tool_name,arguments=args));history.append(dict(tool=tool_name,args=args,result=r));(root/'results.json').write_text(json.dumps(history,ensure_ascii=False,indent=2),encoding='utf-8')
 assert bool(r.get('isError'))==error,(tool_name,r)
 if error:return r
 return json.loads(''.join(c.get('text','') for c in r['content']))
def passed(message):checks.append(message);print('PASS '+message,flush=True)
try:
 req('initialize',dict(protocolVersion='2025-11-25',capabilities={},clientInfo=dict(name='plane/thread regression',version='1')))
 server.stdin.write('{"jsonrpc":"2.0","method":"notifications/initialized"}\n');server.stdin.flush()
 original=tool('solidedge_get_document');before=tool('solidedge_get_application_state');assert not original['dirty']
 catalog=tool('solidedge_list_metric_threads');m8=next(t for t in catalog['threads'] if t['Description']=='M8')
 filtered=tool('solidedge_list_metric_threads',descriptionContains='m8',family=m8['Family'],nominalDiameterMm=8,limit=1)
 assert filtered['matchedCount']>=1 and len(filtered['threads'])==1 and filtered['threads'][0]['NominalDiameterMm']==8
 page1=tool('solidedge_list_metric_threads',limit=1);page2=tool('solidedge_list_metric_threads',offset=page1['nextOffset'],limit=1)
 assert page1['threads'][0]==catalog['threads'][0] and page2['threads'][0]==catalog['threads'][1]
 empty=tool('solidedge_list_metric_threads',descriptionContains='__NO_SUCH_THREAD__');assert empty['matchedCount']==0 and empty['threads']==[] and empty['nextOffset'] is None
 passed('native thread AND filters, paging and empty results')
 doc=tool('solidedge_new_document',kind='part')['fullName']
 planes=tool('solidedge_list_planes',expectedDocument=doc)['planes']
 def offset(source,distance):
  result=tool('solidedge_create_offset_plane',expectedDocument=doc,planeNumber=source['number'],offsetMm=distance)
  assert all(abs(result['originMm'][i]-source['originMm'][i]-source['normal'][i]*distance)<1e-4 for i in range(3))
  current=next(p for p in tool('solidedge_list_planes',expectedDocument=doc)['planes'] if p['number']==result['planeNumber'])
  assert all(abs(current['originMm'][i]-result['originMm'][i])<1e-6 for i in range(3))
  return current
 first=offset(planes[0],15);top=offset(first,5)
 offset(planes[1],-8);offset(planes[2],12)
 passed('empty Ordered part supports positive/negative offsets, three orientations and chained planes')
 count=len(tool('solidedge_list_planes',expectedDocument=doc)['planes'])
 tool('solidedge_create_offset_plane',expectedDocument=doc,planeNumber=1,offsetMm=0,error=True)
 tool('solidedge_create_offset_plane',expectedDocument=doc,planeNumber=999,offsetMm=10,error=True)
 tool('solidedge_create_offset_plane',expectedDocument='wrong.par',planeNumber=1,offsetMm=10,error=True)
 assert len(tool('solidedge_list_planes',expectedDocument=doc)['planes'])==count
 passed('invalid offset/plane and active-document mismatch preserve plane count')
 tool('solidedge_extrude_profile',expectedDocument=doc,shape='rectangle',widthMm=30,heightMm=30,depthMm=20,featureName='base')
 tool('solidedge_extrude_profile',expectedDocument=doc,shape='rectangle',widthMm=10,heightMm=10,depthMm=5,xMm=5,yMm=5,planeNumber=top['number'],operation='cut',direction='negative',featureName='top-pocket')
 features=tool('solidedge_list_features',expectedDocument=doc)['features'];assert len(features)==2 and all(f['status']==1216476310 for f in features)
 passed('offset plane drives a native top-surface pocket with healthy features')
 offset(tool('solidedge_list_planes',expectedDocument=doc)['planes'][1],-12)
 passed('existing solid part accepts another persistent plane with correct returned number')
 retained=tool('solidedge_list_planes',expectedDocument=doc)['planes']
 path=str(root/'offset-part.par');tool('solidedge_save_document',expectedDocument=doc,outputPath=path)
 tool('solidedge_close_document',expectedDocument=path,returnDocument=original['fullName'])
 tool('solidedge_open_document',filePath=path)
 reopened=tool('solidedge_list_planes',expectedDocument=path)['planes'];assert len(reopened)==len(retained)
 for saved,actual in zip(retained,reopened):
  assert saved['number']==actual['number'] and saved['name']==actual['name']
  for field in ['originMm','normal','xDirection','yDirection']:assert all(abs(x-y)<1e-6 for x,y in zip(saved[field],actual[field]))
 assert len(tool('solidedge_list_features',expectedDocument=path)['features'])==2
 tool('solidedge_close_document',expectedDocument=path,returnDocument=original['fullName'])
 after=tool('solidedge_get_application_state');current=tool('solidedge_get_document')
 assert current['fullName']==original['fullName'] and current['dirty']==original['dirty']
 assert sum(d['windowCount'] for d in before['documents'])==sum(d['windowCount'] for d in after['documents'])
 passed('planes and dependent cut persist after reopen; original document/windows preserved')
 (root/'summary.json').write_text(json.dumps(dict(passed=True,checks=checks),indent=2))
finally:server.terminate();server.wait(timeout=10)
