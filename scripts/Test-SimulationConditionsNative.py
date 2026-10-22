"""Edit/delete simulation conditions on a NEW part only; no native solver is run."""
import argparse,json,os,pathlib,queue,subprocess,threading
p=argparse.ArgumentParser();p.add_argument('--mcp',required=True);p.add_argument('--output',required=True);a=p.parse_args()
root=pathlib.Path(a.output).resolve();root.mkdir(parents=True,exist_ok=False)
access=root/'access.json';access.write_text('{"schema":1,"mode":"write"}',encoding='utf-8')
env=os.environ.copy();env.update(MECHCUE_MCP_NO_TRAY='1',MECHCUE_MCP_SETTINGS_PATH=str(access))
startup=subprocess.STARTUPINFO();startup.dwFlags|=subprocess.STARTF_USESHOWWINDOW;startup.wShowWindow=0
server=subprocess.Popen([str(pathlib.Path(a.mcp).resolve())],stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=subprocess.PIPE,text=True,encoding='utf-8',env=env,startupinfo=startup)
replies=queue.Queue();history=[];seq=0;passed=False
threading.Thread(target=lambda:[replies.put(json.loads(line)) for line in server.stdout],daemon=True).start()
threading.Thread(target=lambda:[None for line in server.stderr],daemon=True).start()
def request(method,params):
    global seq
    seq+=1;server.stdin.write(json.dumps({'jsonrpc':'2.0','id':seq,'method':method,'params':params})+'\n');server.stdin.flush()
    while True:
        result=replies.get(timeout=60)
        if result.get('id')==seq:
            if 'error' in result:raise RuntimeError(result['error'])
            return result['result']
def tool(name,args={},error=False):
    result=request('tools/call',{'name':name,'arguments':args});history.append({'tool':name,'arguments':args,'result':result})
    assert bool(result.get('isError'))==error,(name,result)
    return result if error else json.loads(''.join(c['text'] for c in result.get('content',[]) if c['type']=='text'))
try:
    request('initialize',{'protocolVersion':'2025-11-25','capabilities':{},'clientInfo':{'name':'simulation condition verification','version':'1'}})
    server.stdin.write('{"jsonrpc":"2.0","method":"notifications/initialized"}\n');server.stdin.flush()
    state=tool('solidedge_get_application_state');assert not any(d['dirty'] for d in state.get('documents',[])),state
    original=tool('solidedge_get_document')['fullName'] if state.get('documents') else ''
    doc=tool('solidedge_new_document',{'kind':'part'})['fullName']
    tool('solidedge_extrude_profile',{'expectedDocument':doc,'shape':'rectangle','widthMm':100,'heightMm':10,'depthMm':10})
    path=str(root/'conditions.par');tool('solidedge_save_document',{'expectedDocument':doc,'outputPath':path});guard={'expectedDocument':path}
    study=tool('solidedge_create_simulation_study',guard);target=guard|{'studyNumber':study['study']['number']}
    face=tool('solidedge_list_simulation_faces',guard)['faces'][0]['faceId']
    tool('solidedge_add_simulation_fixed',target|{'faceIdsJson':json.dumps([face]),'name':'fixed'})
    tool('solidedge_add_simulation_load',target|{'kind':'force','value':100,'faceIdsJson':json.dumps([face]),'name':'force'})
    load=target|{'category':'load','conditionNumber':1,'expectedName':'force'}
    tool('solidedge_edit_simulation_condition',load|{'name':'edited force','value':200,'directionJson':'[0,2,0]','suppressed':True})
    info=tool('solidedge_list_simulation_studies',guard)['studies'][0]['loads'][0]
    assert info['name']=='edited force' and info['valueNative']==200 and info['suppressed'] and info['direction']==[0,1,0],info
    tool('solidedge_delete_simulation_condition',load,True)
    load['expectedName']='edited force'
    tool('solidedge_edit_simulation_condition',load|{'suppressed':False})
    constraint=target|{'category':'constraint','conditionNumber':1,'expectedName':'fixed'}
    tool('solidedge_edit_simulation_condition',constraint|{'name':'edited fixed','suppressed':True})
    info=tool('solidedge_list_simulation_studies',guard)['studies'][0]['constraints'][0]
    assert info['name']=='edited fixed' and info['suppressed'],info
    tool('solidedge_delete_simulation_condition',load)
    constraint['expectedName']='edited fixed';tool('solidedge_delete_simulation_condition',constraint)
    info=tool('solidedge_list_simulation_studies',guard)['studies'][0];assert not info['loads'] and not info['constraints'],info
    tool('solidedge_save_document',guard);tool('solidedge_close_document',guard|{'returnDocument':original})
    passed=True;print('PASS: native force value/direction/name/suppression, fixed condition editing, guarded deletion, saved cleanup')
finally:
    (root/'report.json').write_text(json.dumps({'passed':passed,'history':history},ensure_ascii=False,indent=2),encoding='utf-8')
    server.stdin.close()
    try:server.wait(timeout=5)
    except subprocess.TimeoutExpired:server.terminate()
