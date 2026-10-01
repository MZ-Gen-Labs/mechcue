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
replies=queue.Queue();history=[];checks=[];errors=[];seq=0

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
    request('initialize',{'protocolVersion':'2025-11-25','capabilities':{},'clientInfo':{'name':'Native Simulation test','version':'1'}})
    doc=tool('solidedge_new_document',{'kind':'part','templatePath':r'C:\Siemens\Solid Edge 2026\Template\ISO Metric\iso metric part.par'})['fullName']
    tool('solidedge_extrude_profile',{'expectedDocument':doc,'shape':'rectangle','widthMm':100,'heightMm':10,'depthMm':10})
    path=str(root/'cantilever.par');tool('solidedge_save_document',{'expectedDocument':doc,'outputPath':path});doc=path
    base={'expectedDocument':doc};before=tool('solidedge_list_features',base)
    faces=tool('solidedge_list_simulation_faces',base)['faces'];check(len(faces)==6,'Six actual solid faces with persistent IDs')
    low=next(f['faceId'] for f in faces if abs(f['minMm'][0])<1e-5 and abs(f['maxMm'][0])<1e-5)
    high=next(f['faceId'] for f in faces if abs(f['minMm'][0]-100)<1e-5 and abs(f['maxMm'][0]-100)<1e-5)
    mats=tool('solidedge_list_simulation_materials',base);check(a.material in mats['materials'],'Exact installed library material found')
    check(not tool('solidedge_get_document')['dirty'],'Read-only face/material inspection preserves saved status')
    study=tool('solidedge_create_simulation_study',{**base,'meshSizeMm':3});n=study['study']['number'];target={**base,'studyNumber':n}
    check(study['study']['meshSizeMm']==3 and study['study']['studyType']==1,'Linear static tetrahedral study and 3 mm mesh')
    check('elastic modulus' in str(tool('solidedge_run_simulation',target,True)),'Missing material rejected before native solve')
    tool('solidedge_apply_simulation_material',{**base,'materialName':a.material,'libraryName':a.library})
    tool('solidedge_select_simulation_faces',{**base,'faceIdsJson':json.dumps([low])})
    fixed=tool('solidedge_add_simulation_fixed',{**target,'name':'Fixed root'});check(fixed['faceIds']==[low],'Currently selected face becomes fixed condition')
    tool('solidedge_add_simulation_load',{**target,'kind':'force','value':100,'directionY':-2,'directionZ':0,'faceIdsJson':json.dumps([high]),'name':'100 N tip force'})
    state=tool('solidedge_list_simulation_studies',base)['studies'][0]
    check(state['loads'][0]['valueNative']==100 and state['loads'][0]['direction']==[0,-1,0],'100 N force and normalized global direction')
    check(state['constraints'][0]['faceIds']==[low] and state['loads'][0]['faceIds']==[high],'Native fixed/load assignments reference intended faces')
    check(not state['solved'],'New conditions are unsolved')
    tool('solidedge_get_simulation_results',target,True)
    wrong=tool('solidedge_add_simulation_fixed',{'expectedDocument':'wrong.par','studyNumber':n,'faceIdsJson':json.dumps([low])},True);check('differs' in str(wrong),'Active-document mismatch rejected')
    stale=tool('solidedge_add_simulation_fixed',{**target,'faceIdsJson':'["AQID"]'},True);check('no longer present' in str(stale),'Unknown face ID rejected without adding constraint')
    check(len(tool('solidedge_list_simulation_studies',base)['studies'][0]['constraints'])==1,'Rejected face leaves fixed conditions unchanged')
    second=tool('solidedge_create_simulation_study',{**base,'meshSizeMm':4})['study']['number'];other={**base,'studyNumber':second}
    tool('solidedge_add_simulation_fixed',{**other,'faceIdsJson':json.dumps([low])})
    pressure_faces=[f['faceId'] for f in faces if f['faceId'] not in [low,high]][:2]
    tool('solidedge_add_simulation_load',{**other,'kind':'pressure','value':1.5,'faceIdsJson':json.dumps(pressure_faces)})
    state=tool('solidedge_list_simulation_studies',base)
    check(len(state['studies'])==2 and len(state['studies'][0]['loads'])==1,'New study preserves existing first study and conditions')
    load=state['studies'][1]['loads'][0];check(load['valueNative']==1500000 and set(load['faceIds'])==set(pressure_faces),'1.5 MPa pressure converts to Pa with two face assignments')
    mesh=tool('solidedge_run_simulation',{**target,'meshOnly':True});check(mesh['completed'] and mesh['study']['meshed'] and not mesh['study']['solved'],'Native mesh-only completion without solved results')
    check(not mesh['warnings'] and mesh['study']['loads'][0]['faceIds']==[high],'Condition faces remain readable after meshing')
    solved=tool('solidedge_run_simulation',target);check(solved['completed'] and solved['study']['solved'] and not solved['study']['resultsError'],'Native Nastran static solve completed')
    stress=tool('solidedge_get_simulation_results',{**target,'resultKind':'stress'});displacement=tool('solidedge_get_simulation_results',{**target,'resultKind':'displacement'})
    check(stress['plotType']==60031 and stress['unit']=='MPa' and 40<stress['maximum']<90,'Von Mises stress in MPa matches cantilever order of magnitude')
    # Beam bending predicts F*L^3/(3*E*I). This is a broad independent physical check, not an accuracy certificate.
    E=tool('solidedge_list_simulation_studies',base)['material']['elasticModulusPa'];prediction=100*.1**3/(3*E*(.01*.01**3/12))*1000
    check(displacement['plotType']==1 and displacement['unit']=='mm' and abs(displacement['maximum']/prediction-1)<.25,'Total translation in mm agrees with beam bending within 25%')
    tool('solidedge_add_simulation_load',{**target,'kind':'force','value':100,'directionY':-1,'directionZ':0,'faceIdsJson':json.dumps([high]),'name':'Additional 100 N'})
    tool('solidedge_get_simulation_results',target,True);check(not tool('solidedge_list_simulation_studies',base)['studies'][0]['resultsCurrent'],'Load change invalidates verified results despite native solved flag')
    double=tool('solidedge_run_simulation',target);check(double['completed'],'Changed load solves again')
    double_disp=tool('solidedge_get_simulation_results',{**target,'resultKind':'displacement'})
    check(abs(double_disp['maximum']/displacement['maximum']-2)<.03,'Doubling the force doubles linear displacement')
    tool('solidedge_show_simulation_results',{**target,'resultKind':'displacement'})
    active=tool('solidedge_get_simulation_results',{**target,'resultKind':'active'});check(active['plotType']==1,'Requested displacement plot activated')
    tool('solidedge_get_simulation_results',{**target,'resultKind':'stress'})
    check(tool('solidedge_get_simulation_results',{**target,'resultKind':'active'})['plotType']==1,'Reading stress does not change the visible displacement plot')
    tool('solidedge_show_simulation_results',{**target,'resultKind':'stress'})
    check(tool('solidedge_list_features',base)==before,'Simulation preserves Ordered geometry feature tree')
    tool('solidedge_save_document',base);check(not tool('solidedge_get_document')['dirty'],'Explicit CAD save stores study and results')
    check('Close the Solid Edge' in str(tool('solidedge_create_simulation_study',base,True)), 'New study rejected safely inside Results environment')
    check('Close the Solid Edge' in str(tool('solidedge_run_simulation',other,True)), 'Switching study rejected safely inside Results environment')
    check(len(tool('solidedge_list_simulation_studies',base)['studies'])==2,'Rejected study creation preserves study count')
    (root/'result.json').write_text(json.dumps({'checks':checks,'document':doc,'stress100N':stress,'displacement100N':displacement,'displacement200N':double_disp,'beamPredictionMm':prediction},ensure_ascii=False,indent=2),encoding='utf-8')
finally:
    (root/'test-result.txt').write_text('\n'.join('PASS: '+c for c in checks)+'\n',encoding='utf-8')
    (root/'stderr.txt').write_text(''.join(errors),encoding='utf-8');server.stdin.close()
    try:server.wait(timeout=5)
    except subprocess.TimeoutExpired:server.terminate();server.wait(timeout=5)
