"""Native machining feature checks in NEW files; preserves pre-existing user windows."""
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
def block(name,driving=False):
 doc=tool('solidedge_new_document',kind='part')['fullName']
 tool('solidedge_extrude_profile',expectedDocument=doc,shape='rectangle',widthMm=100,heightMm=100,depthMm=20,xMm=-50,yMm=-50,direction='positive',featureName='base',drivingDimensions=driving)
 path=root/(name+'.par');tool('solidedge_save_document',expectedDocument=doc,outputPath=str(path));return str(path)
def finish(doc):
 tool('solidedge_save_document',expectedDocument=doc);tool('solidedge_close_document',expectedDocument=doc,returnDocument=original['fullName'])
def span(doc,axis):
 points=[p for e in tool('solidedge_list_edges',expectedDocument=doc)['edges'] for p in e['boundsMm']];return max(p[axis] for p in points)-min(p[axis] for p in points)
try:
 req('initialize',dict(protocolVersion='2025-11-25',capabilities={},clientInfo=dict(name='native machining',version='1')));server.stdin.write('{"jsonrpc":"2.0","method":"notifications/initialized"}\n');server.stdin.flush()
 original=tool('solidedge_get_document');before=tool('solidedge_get_application_state');print('USER DOCUMENT '+original['fullName'],flush=True)
 catalog=tool('solidedge_list_metric_threads');assert any(t['Description']=='M8' for t in catalog['threads']) and catalog['sourceFile'];passed('configured native metric thread catalog')
 doc=block('holes');common=dict(expectedDocument=doc,planeOffsetMm=20,direction='negative')
 tool('solidedge_create_hole',**common,centersJson='[{"x":-30,"y":-30}]',diameterMm=8,featureName='regular')
 tool('solidedge_create_hole',**common,centersJson='[{"x":0,"y":-30}]',diameterMm=8,holeType='counterbore',counterboreDiameterMm=16,counterboreDepthMm=5,featureName='counterbore')
 tool('solidedge_create_hole',**common,centersJson='[{"x":30,"y":-30}]',diameterMm=8,holeType='countersink',countersinkDiameterMm=16,countersinkAngleDeg=90,featureName='countersink')
 holes=tool('solidedge_list_holes',expectedDocument=doc)['holes'];assert len(holes)==3 and [h['holeType'] for h in holes]==[33,34,35] and holes[1]['counterboreDepthMm']==5
 passed('native regular/counterbore/countersink holes and retained machining data')
 count=len(tool('solidedge_list_features',expectedDocument=doc)['features']);tool('solidedge_create_hole',**common,centersJson='[{"x":0,"y":0}]',diameterMm=8,threadDescription='INVALID_THREAD',featureName='invalid',error=True)
 assert len(tool('solidedge_list_features',expectedDocument=doc)['features'])==count
 tool('solidedge_create_hole',**common,centersJson='[{"x":0,"y":0}]',diameterMm=6.647,extent='finite',depthMm=15,threadDescription='M8',threadDepthMm=10,featureName='M8')
 h=next(h for h in tool('solidedge_list_holes',expectedDocument=doc)['holes'] if h['featureName']=='M8');assert h['treatmentType']==37 and h['threadDepthMm']==10 and h['threadDescription']=='M8'
 tool('solidedge_create_hole',**common,centersJson='[{"x":30,"y":30}]',diameterMm=5,threadDescription='M6',featureName='M6-through')
 h=next(h for h in tool('solidedge_list_holes',expectedDocument=doc)['holes'] if h['featureName']=='M6-through');assert h['threadDepthMethod']==16 and h['threadDepthMm'] is None
 count=len(tool('solidedge_list_features',expectedDocument=doc)['features'])
 tool('solidedge_create_hole',**common,centersJson='[{"x":-30,"y":30},{"x":0,"y":30}]',diameterMm=6,featureName='two-centers',error=True)
 assert len(tool('solidedge_list_features',expectedDocument=doc)['features'])==count
 passed('native tapped hole, exact thread lookup, finite thread depth and invalid-thread rollback');finish(doc)
 doc=block('rectangular-pattern');common=dict(expectedDocument=doc,planeOffsetMm=20,direction='negative')
 tool('solidedge_create_hole',**common,centersJson='[{"x":-20,"y":-20}]',diameterMm=6,featureName='seed')
 tool('solidedge_pattern_hole',expectedDocument=doc,sourceFeatureName='seed',xCount=3,yCount=2,xSpacingMm=20,ySpacingMm=20,featureName='six-holes')
 features=tool('solidedge_list_features',expectedDocument=doc)['features'];assert len(features)==3 and features[-1]['name']=='six-holes';passed('native rectangular six-hole dependent pattern');finish(doc)
 doc=block('circular-pattern');common=dict(expectedDocument=doc,planeOffsetMm=20,direction='negative')
 tool('solidedge_create_hole',**common,centersJson='[{"x":25,"y":0}]',diameterMm=6,featureName='seed')
 tool('solidedge_pattern_hole',expectedDocument=doc,sourceFeatureName='seed',patternType='circular',radialCount=4,angleSpacingDeg=90,featureName='four-holes')
 passed('native circular four-hole dependent pattern');finish(doc)
 doc=block('chamfer');edges=tool('solidedge_list_edges',expectedDocument=doc)['edges'];assert len(edges)==12
 tool('solidedge_chamfer_edges',expectedDocument=doc,edgeIdsJson='["stale"]',setbackMm=1,error=True)
 tool('solidedge_chamfer_edges',expectedDocument=doc,edgeIdsJson=json.dumps([e['edgeId'] for e in edges]),setbackMm=1000,featureName='oversized',error=True)
 assert len(tool('solidedge_list_features',expectedDocument=doc)['features'])==1
 edges=tool('solidedge_list_edges',expectedDocument=doc)['edges']
 tool('solidedge_chamfer_edges',expectedDocument=doc,edgeIdsJson=json.dumps([e['edgeId'] for e in edges]),setbackMm=1,featureName='C1')
 passed('native equal-setback chamfer and stale edge rejection');finish(doc)
 doc=block('driving-rectangle',True);dims=tool('solidedge_list_profile_dimensions',expectedDocument=doc,featureName='base')['dimensions'];assert len(dims)==2 and all(d['driving'] for d in dims)
 tool('solidedge_set_profile_dimension',expectedDocument=doc,featureName='base',variableName=dims[0]['variableName'],valueMm=120);assert abs(span(doc,0)-120)<1e-5
 tool('solidedge_set_profile_dimension',expectedDocument=doc,featureName='base',variableName=dims[1]['variableName'],valueMm=80);assert abs(span(doc,1)-80)<1e-5
 passed('rectangle driving sketch dimensions change actual geometry');finish(doc)
 doc=tool('solidedge_new_document',kind='part')['fullName'];tool('solidedge_extrude_profile',expectedDocument=doc,shape='circle',radiusMm=20,depthMm=10,featureName='disk',drivingDimensions=True)
 path=root/'driving-circle.par';tool('solidedge_save_document',expectedDocument=doc,outputPath=str(path));doc=str(path)
 dims=tool('solidedge_list_profile_dimensions',expectedDocument=doc,featureName='disk')['dimensions'];assert len(dims)==1 and dims[0]['driving']
 tool('solidedge_set_profile_dimension',expectedDocument=doc,featureName='disk',variableName=dims[0]['variableName'],valueMm=50);assert abs(span(doc,0)-50)<1e-5
 finish(doc);tool('solidedge_open_document',filePath=doc);dims=tool('solidedge_list_profile_dimensions',expectedDocument=doc,featureName='disk')['dimensions'];assert abs(dims[0]['valueMm']-50)<1e-5;finish(doc)
 passed('circle diameter drives geometry and persists after save/reopen')
 after=tool('solidedge_get_application_state');assert sum(d['windowCount'] for d in before['documents'])==sum(d['windowCount'] for d in after['documents'])
 current=tool('solidedge_get_document');assert current['fullName']==original['fullName'] and current['dirty']==original['dirty'];passed('original user document and windows preserved; completed test files closed')
 (root/'summary.json').write_text(json.dumps(dict(passed=True,checks=checks),indent=2))
finally:
 if original:
  try:tool('solidedge_open_document',filePath=original['fullName'])
  except Exception:pass
 server.terminate();server.wait(timeout=10)
