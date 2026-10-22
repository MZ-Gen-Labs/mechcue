"""Native planar-convex capture and contact test. Creates and saves NEW fixtures only.
Requires a running Solid Edge. Keeps pre-existing documents; never discards edits.
"""
import argparse, json, os, pathlib, queue, subprocess, threading

p=argparse.ArgumentParser();p.add_argument('--mcp',required=True);p.add_argument('--output',required=True);p.add_argument('--mechcue');a=p.parse_args()
root=pathlib.Path(a.output).resolve();root.mkdir(parents=True,exist_ok=False)
settings=root/'access.json';settings.write_text('{"schema":1,"mode":"write"}',encoding='utf-8')
env=os.environ.copy();env['MECHCUE_MCP_NO_TRAY']='1';env['MECHCUE_MCP_SETTINGS_PATH']=str(settings)
startup=subprocess.STARTUPINFO();startup.dwFlags|=subprocess.STARTF_USESHOWWINDOW;startup.wShowWindow=0
server=subprocess.Popen([str(pathlib.Path(a.mcp).resolve())],stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=subprocess.PIPE,text=True,encoding='utf-8',env=env,startupinfo=startup)
replies=queue.Queue();history=[];seq=0;fixture=None;original='';passed=False
threading.Thread(target=lambda:[replies.put(json.loads(line)) for line in server.stdout],daemon=True).start()
threading.Thread(target=lambda:[None for line in server.stderr],daemon=True).start()
def request(method,params):
    global seq
    seq+=1;server.stdin.write(json.dumps({'jsonrpc':'2.0','id':seq,'method':method,'params':params})+'\n');server.stdin.flush()
    while True:
        result=replies.get(timeout=180)
        if result.get('id')==seq:
            if 'error' in result:raise RuntimeError(result['error'])
            return result['result']
def tool(name,args,expect_error=False):
    result=request('tools/call',{'name':name,'arguments':args});history.append({'tool':name,'arguments':args,'result':result})
    assert bool(result.get('isError'))==expect_error,(name,result)
    return result if expect_error else json.loads(''.join(c['text'] for c in result.get('content',[]) if c['type']=='text'))
try:
    request('initialize',{'protocolVersion':'2025-11-25','capabilities':{},'clientInfo':{'name':'convex physics native verification','version':'1'}})
    server.stdin.write('{"jsonrpc":"2.0","method":"notifications/initialized"}\n');server.stdin.flush()
    state=tool('solidedge_get_application_state',{})
    if state.get('documents'):
        active=tool('solidedge_get_document',{});original=active['fullName']
        assert not any(d.get('dirty') for d in state['documents']),'Save pre-existing edits before native verification'
    fixture=tool('solidedge_create_concept_machine',{'type':'gantry','outputDirectory':str(root/'model')})
    guard={'expectedDocument':fixture['fullName']}
    work=next(i+1 for i,b in enumerate(fixture['bodies']) if b['id']=='workpiece')
    part_path=str(root/'model'/'workpiece.par')
    tool('solidedge_open_document',{'filePath':part_path})
    materials=tool('solidedge_list_simulation_materials',{'expectedDocument':part_path})
    material=next((name for name in materials['materials'] if name in ('鋼鉄','Steel')),materials['materials'][0])
    applied=tool('solidedge_apply_simulation_material',{'expectedDocument':part_path,'materialName':material,'libraryName':materials['libraryName']})
    tool('solidedge_save_document',{'expectedDocument':part_path})
    tool('solidedge_close_document',{'expectedDocument':part_path,'returnDocument':fixture['fullName']})
    tool('solidedge_save_document',guard)
    captured=tool('solidedge_capture_convex_physics',guard|{'workpieceNumber':work,'outputPath':str(root/'convex.json')})
    model=json.loads((root/'convex.json').read_text(encoding='utf-8'))
    assert all(b['shape']=='convex' and len(b['verticesMm'])==8 for b in model['bodies'])
    assert 0<model['physicsDefaults']['workpieceMassKg']<1000
    assert abs(model['physicsDefaults']['workpieceMassKg']-.001*applied['material']['densityKgM3'])<1e-6,'CAD mass must use the original material density'
    tool('solidedge_capture_convex_physics',guard|{'workpieceNumber':work,'outputPath':str(root/'convex.json'),'workpieceMassKg':.5},True)
    result=tool('solidedge_simulate_concept_rigid_body',{'manifestPath':captured['manifestPath']})
    assert result['finalState']=='resting',result
    values={axis:points[-1]['value'] for axis,points in result['axisKeyframes'].items()}
    assert .01<values['WZ']<.05,values
    tool('solidedge_set_concept_pose',guard|{'manifestPath':captured['manifestPath'],'valuesJson':json.dumps(values)})
    interference=tool('solidedge_check_interference',guard|{'set1Json':'[1]','set2Json':json.dumps([work])})
    assert interference.get('clear') is True,interference
    # Return the newly created assembly to its original pose, save it, then close only its window.
    tool('solidedge_set_concept_pose',guard|{'manifestPath':captured['manifestPath'],'valuesJson':json.dumps(model['values'])})
    tool('solidedge_save_document',guard)
    if a.mechcue:
        checked=subprocess.run([str(pathlib.Path(a.mechcue).resolve()),'--convex-physics-integration-test',captured['manifestPath'],str(root/'chart-path-report.json')],capture_output=True,text=True,encoding='utf-8',env=env,startupinfo=startup,timeout=120)
        assert checked.returncode==0,(checked.stdout,checked.stderr)
    tool('solidedge_close_document',guard|{'returnDocument':original})
    passed=True
    print(json.dumps({'passed':True,'bodies':len(model['bodies']),'restingGapMm':values['WZ'],'nativeInterference':interference},ensure_ascii=False))
finally:
    (root/'report.json').write_text(json.dumps({'passed':passed,'history':history},ensure_ascii=False,indent=2),encoding='utf-8')
    server.stdin.close()
    try:server.wait(timeout=10)
    except subprocess.TimeoutExpired:server.terminate()
