"""Single-part native FEA integration through MechCue MCP. Requires Solid Edge 2026 + Simulation.
Creates NEW test documents only; uses isolated MCP access settings. Native solving can show license/error dialogs.
Does not change shared material libraries or close existing user documents.
"""
import argparse,json,subprocess,threading,queue,pathlib,os,time,math
p=argparse.ArgumentParser();p.add_argument('--mcp',required=True);p.add_argument('--output',required=True);p.add_argument('--material',default='鋼鉄');p.add_argument('--library',default='Materials');a=p.parse_args()
root=pathlib.Path(a.output).resolve()
if root.exists():raise SystemExit('Use a fresh output folder.')
root.mkdir(parents=True);settings=root/'access.json';settings.write_text('{"schema":1,"mode":"write"}',encoding='utf-8')
env=os.environ.copy();env['MECHCUE_MCP_NO_TRAY']='1';env['MECHCUE_MCP_SETTINGS_PATH']=str(settings)
startup=subprocess.STARTUPINFO();startup.dwFlags|=subprocess.STARTF_USESHOWWINDOW;startup.wShowWindow=0
server=subprocess.Popen([str(pathlib.Path(a.mcp).resolve())],stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=subprocess.PIPE,text=True,encoding='utf-8',env=env,startupinfo=startup)
replies=queue.Queue();history=[];checks=[];errors=[];seq=0;original_alerts=None

def output():
    for line in server.stdout:
        try:replies.put(json.loads(line))
        except json.JSONDecodeError:replies.put({'invalid':line})
def stderr():
    for line in server.stderr:errors.append(line)
threading.Thread(target=output,daemon=True).start();threading.Thread(target=stderr,daemon=True).start()
def request(method,params):
    global seq
    seq+=1;ident=seq;server.stdin.write(json.dumps({'jsonrpc':'2.0','id':ident,'method':method,'params':params})+'\n');server.stdin.flush();deadline=time.monotonic()+240
    while time.monotonic()<deadline:
        r=replies.get(timeout=max(.1,deadline-time.monotonic()))
        if r.get('id')!=ident:continue
        if 'error' in r:raise RuntimeError(r['error'])
        return r['result']
    raise TimeoutError(method)
def tool(name,args={},expect_error=False):
    result=request('tools/call',{'name':name,'arguments':args});history.append({'name':name,'arguments':args,'result':result})
    (root/'history.json').write_text(json.dumps(history,ensure_ascii=False,indent=2),encoding='utf-8')
    if bool(result.get('isError'))!=expect_error:raise AssertionError((name,result))
    if expect_error:return result
    return json.loads(''.join(c.get('text','') for c in result['content'] if c['type']=='text'))
def check(value,name):
    if not value:raise AssertionError(name)
    checks.append(name);print('PASS:',name,flush=True)
try:
    request('initialize',{'protocolVersion':'2025-11-25','capabilities':{},'clientInfo':{'name':'Native Mesh Slider Verification','version':'1'}})
    doc=tool('solidedge_new_document',{'kind':'part','templatePath':r'C:\Siemens\Solid Edge 2026\Template\ISO Metric\iso metric part.par'})['fullName']
    tool('solidedge_extrude_profile',{'expectedDocument':doc,'shape':'rectangle','widthMm':200,'heightMm':30,'depthMm':10})
    docpath=str(root/'slider-test.par');tool('solidedge_save_document',{'expectedDocument':doc,'outputPath':docpath});doc=docpath;base={'expectedDocument':doc}
    original_alerts=tool('solidedge_get_automation_settings')['displayAlerts']
    defaults=tool('solidedge_create_simulation_study',base)['study'];check(defaults['meshLevel']==5,'New study defaults to native slider level 5')
    coarse=tool('solidedge_create_simulation_study',{**base,'meshLevel':1})['study'];fine=tool('solidedge_create_simulation_study',{**base,'meshLevel':10})['study']
    check(coarse['meshLevel']==1 and fine['meshLevel']==10,'Native slider endpoints read back 1 and 10')
    print('INITIAL SIZES',json.dumps({'default':defaults['meshSizeMm'],'coarse':coarse['meshSizeMm'],'fine':fine['meshSizeMm']}),flush=True)
    target={**base,'studyNumber':defaults['number']}
    one=tool('solidedge_run_simulation',{**target,'meshOnly':True,'meshLevel':1,'suppressAlerts':True})['study']
    ten=tool('solidedge_run_simulation',{**target,'meshOnly':True,'meshLevel':10,'regenerateMesh':True,'suppressAlerts':True})['study']
    check(one['meshLevel']==1 and ten['meshLevel']==10,'Meshing applies requested native slider levels')
    check(one['meshSizeMm']>ten['meshSizeMm']>0,'Geometry-derived global mesh size decreases as slider increases')
    faces=tool('solidedge_list_simulation_faces',base)['faces'];low=next(f['faceId'] for f in faces if abs(f['minMm'][0])<1e-5 and abs(f['maxMm'][0])<1e-5);high=next(f['faceId'] for f in faces if abs(f['minMm'][0]-200)<1e-5 and abs(f['maxMm'][0]-200)<1e-5)
    tool('solidedge_apply_simulation_material',{**base,'materialName':a.material,'libraryName':a.library})
    tool('solidedge_add_simulation_fixed',{**target,'faceIdsJson':json.dumps([low])})
    tool('solidedge_add_simulation_load',{**target,'kind':'force','value':200,'directionZ':-1,'faceIdsJson':json.dumps([high])})
    solved=tool('solidedge_run_simulation',{**target,'suppressAlerts':True});check(solved['completed'] and solved['study']['solved'],'Native slider fine mesh solves successfully')
    stress=tool('solidedge_show_simulation_results',{**target,'resultKind':'stress'});disp=tool('solidedge_get_simulation_results',{**target,'resultKind':'displacement'})
    tool('solidedge_save_document',base)
    result={'document':doc,'coarse1':one,'fine10':ten,'defaults':defaults,'stress':stress,'displacement':disp,'checks':checks}
    (root/'result.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
    check(tool('solidedge_get_automation_settings')['displayAlerts']==original_alerts,'Native slider operations restore original DisplayAlerts')
    print('RESULT',json.dumps({'level1SizeMm':one['meshSizeMm'],'level10SizeMm':ten['meshSizeMm'],'stressMPa':stress['maximum'],'displacementMm':disp['maximum']}),flush=True)
finally:
    (root/'test-result.txt').write_text('\n'.join('PASS: '+c for c in checks)+'\n',encoding='utf-8')
    (root/'stderr.txt').write_text(''.join(errors),encoding='utf-8');server.stdin.close()
    try:server.wait(timeout=5)
    except subprocess.TimeoutExpired:server.terminate();server.wait(timeout=5)
