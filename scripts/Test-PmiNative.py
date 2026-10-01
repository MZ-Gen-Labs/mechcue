"""Native PMI integration through MechCue MCP. Requires Solid Edge 2026.
Creates only new test files. Leaves test documents open for inspection. Set tray access to Creation/edit first.
"""
import argparse,subprocess,json,queue,threading,time,pathlib,os
parser=argparse.ArgumentParser();parser.add_argument('--mcp',required=True);parser.add_argument('--output',required=True)
args=parser.parse_args();root=pathlib.Path(args.output).resolve()
if root.exists():raise SystemExit('Use a fresh output folder.')
root.mkdir(parents=True)
startup=subprocess.STARTUPINFO();startup.dwFlags|=subprocess.STARTF_USESHOWWINDOW;startup.wShowWindow=0
env=os.environ.copy();env['MECHCUE_MCP_NO_TRAY']='1'
server=subprocess.Popen([str(pathlib.Path(args.mcp).resolve())],stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=subprocess.PIPE,text=True,encoding='utf-8',env=env,startupinfo=startup)
replies=queue.Queue();errors=[]
def output():
    for line in server.stdout:
        try:replies.put(json.loads(line))
        except json.JSONDecodeError:replies.put({'invalid':line})
def stderr():
    for line in server.stderr:errors.append(line)
threading.Thread(target=output,daemon=True).start();threading.Thread(target=stderr,daemon=True).start()
seq=0
history={}
def request(method,args):
    global seq
    seq+=1;ident=seq;server.stdin.write(json.dumps({'jsonrpc':'2.0','id':ident,'method':method,'params':args})+'\n');server.stdin.flush()
    deadline=time.monotonic()+240
    while time.monotonic()<deadline:
        reply=replies.get(timeout=max(.1,deadline-time.monotonic()))
        if reply.get('id')!=ident:continue
        if 'error' in reply:raise RuntimeError(reply['error'])
        return reply['result']
    raise TimeoutError(method)
def tool(name,args={}):
    result=request('tools/call',{'name':name,'arguments':args})
    if result.get('isError'):raise RuntimeError(result)
    return json.loads(''.join(c.get('text','') for c in result['content'] if c['type']=='text'))

checks=[]
def check(value,name):
    if not value:raise AssertionError(name)
    checks.append(name)
def save_part(name,shape,**values):
    doc=tool('solidedge_new_document',{'kind':'part','templatePath':str(templates/'iso metric part.par')})['fullName']
    tool('solidedge_extrude_profile',{'expectedDocument':doc,'shape':shape,**values})
    path=str(root/(name+'.par'));tool('solidedge_save_document',{'expectedDocument':doc,'outputPath':path});return path
try:
    request('initialize',{'protocolVersion':'2025-11-25','capabilities':{},'clientInfo':{'name':'Native PMI test','version':'1'}})
    templates=pathlib.Path(r'C:\Siemens\Solid Edge 2026\Template\ISO Metric')
    plate=save_part('plate','rectangle',widthMm=120,heightMm=80,depthMm=12)
    for x,y in [(30,25),(90,55)]:tool('solidedge_extrude_profile',{'expectedDocument':plate,'shape':'circle','radiusMm':5,'depthMm':20,'xMm':x,'yMm':y,'operation':'cut'})
    tool('solidedge_save_document',{'expectedDocument':plate})
    before=tool('solidedge_list_features',{'expectedDocument':plate})
    state=tool('solidedge_list_pmi',{'expectedDocument':plate})
    check(not state['dimensions'],'New part has no PMI dimensions')
    check(before==tool('solidedge_list_features',{'expectedDocument':plate}),'Initial native PMI container access preserves geometry features')
    result=tool('solidedge_auto_pmi',{'expectedDocument':plate})
    check(not result['warnings'],'Plate generation has no warnings')
    check(sorted(round(d['value'],3) for d in result['added'])==[10,12,25,30,55,80,90,120],'Plate overall, thickness, diameter and hole positions')
    check(all(not d['constraint'] and d['statusId']==6 for d in result['state']['dimensions']),'Native associated reference PMI, no driving constraints')
    check(before==tool('solidedge_list_features',{'expectedDocument':plate}),'PMI preserves modeling feature tree')
    repeat=tool('solidedge_auto_pmi',{'expectedDocument':plate})
    check(not repeat['added'] and not repeat['warnings'] and len(repeat['state']['modelViews'])==2,'Repeated generation avoids duplicate dimensions and views')
    tool('solidedge_show_pmi_view',{'expectedDocument':plate,'viewName':'MechCue PMI XZ'})
    state=tool('solidedge_list_pmi',{'expectedDocument':plate})
    check([d['mechCueId'] for d in state['dimensions'] if not d['hidden']]==['overall-Z'],'Model view shows thickness PMI only')
    tool('solidedge_show_pmi_view',{'expectedDocument':plate,'viewName':'MechCue PMI XY'})
    tool('solidedge_save_document',{'expectedDocument':plate})
    state=tool('solidedge_list_pmi',{'expectedDocument':plate})
    check(state['saved'] and len(state['dimensions'])==8,'PMI and model views save inside .par')
    tool('solidedge_open_document',{'filePath':plate})
    check(tool('solidedge_list_pmi',{'expectedDocument':plate})==state,'Reactivation preserves PMI data')
    cylinder=save_part('cylinder','circle',radiusMm=20,depthMm=50)
    result=tool('solidedge_auto_pmi',{'expectedDocument':cylinder})
    check(not result['warnings'] and sorted(round(d['value'],3) for d in result['added'])==[40,50],'Cylinder associated diameter and axial length')
    tool('solidedge_save_document',{'expectedDocument':cylinder})
    step=save_part('step','polygon',depthMm=15,pointsJson=json.dumps([{'x':0,'y':0},{'x':100,'y':0},{'x':100,'y':35},{'x':60,'y':35},{'x':60,'y':70},{'x':0,'y':70}]))
    result=tool('solidedge_auto_pmi',{'expectedDocument':step})
    check(not result['warnings'] and any(d['kind']=='step' for d in result['added']),'Stepped profile adds associated step dimensions')
    tool('solidedge_save_document',{'expectedDocument':step})
    slope=save_part('incline','polygon',depthMm=10,pointsJson=json.dumps([{'x':0,'y':0},{'x':100,'y':0},{'x':100,'y':20},{'x':50,'y':70},{'x':0,'y':70}]))
    result=tool('solidedge_auto_pmi',{'expectedDocument':slope})
    check(not result['warnings'] and any(d['kind']=='angle' and abs(d['value']-45)<.001 for d in result['added']),'Inclined profile creates native 45 degree angular PMI')
    tool('solidedge_save_document',{'expectedDocument':slope})
    overall=save_part('overall','rectangle',widthMm=90,heightMm=60,depthMm=20)
    result=tool('solidedge_auto_pmi',{'expectedDocument':overall,'dimensionMode':'overall','createModelViews':False})
    check(not result['warnings'] and len(result['added'])==3 and not result['modelViews'],'Overall-only mode and optional model views')
    tool('solidedge_save_document',{'expectedDocument':overall})
    root.joinpath('result.json').write_text(json.dumps({'success':True,'checks':checks,'plate':plate,'state':state},ensure_ascii=False,indent=2),encoding='utf-8')
    print('PASS: '+str(len(checks))+' native PMI checks. '+str(root/'result.json'),flush=True)
except Exception as error:
    root.joinpath('result.json').write_text(json.dumps({'success':False,'checks':checks,'error':str(error)},ensure_ascii=False,indent=2),encoding='utf-8');raise
finally:
    server.stdin.close()
    try:server.wait(timeout=5)
    except subprocess.TimeoutExpired:server.kill();server.wait()
