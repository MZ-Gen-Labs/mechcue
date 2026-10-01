"""Native drawing integration through MechCue MCP. Requires Solid Edge 2026.
Creates only new test files. Leaves test documents open for inspection.
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
    request('initialize',{'protocolVersion':'2025-11-25','capabilities':{},'clientInfo':{'name':'Native drawing test','version':'1'}})
    server.stdin.write('{"jsonrpc":"2.0","method":"notifications/initialized"}\n');server.stdin.flush()
    templates=pathlib.Path(r'C:\Siemens\Solid Edge 2026\Template\ISO Metric')
    plate=save_part('plate','rectangle',widthMm=120,heightMm=80,depthMm=12)
    for x,y in [(30,25),(90,55)]:tool('solidedge_extrude_profile',{'expectedDocument':plate,'shape':'circle','radiusMm':5,'depthMm':20,'xMm':x,'yMm':y,'operation':'cut'})
    tool('solidedge_save_document',{'expectedDocument':plate})
    plan=tool('solidedge_plan_drawing',{'expectedDocument':plate});check(plan['plan']['FrontOrientation']=='top','Plate front follows cylindrical features')
    def drawing(model,name,**opts):
        tool('solidedge_open_document',{'filePath':model});path=str(root/(name+'.dft'))
        result=tool('solidedge_auto_drawing',{'expectedDocument':model,'outputPath':path,'templatePath':str(templates/'iso metric draft.dft'),**opts})
        return path,result
    draft,result=drawing(plate,'plate-drawing')
    check(sorted(round(d['valueMm'],3) for d in result['dimensions'])==[10,12,25,30,55,80,90,120],'Plate overall, thickness, diameter and hole positions')
    def check_lanes(document, label):
        before=tool('solidedge_list_drawing_views',{'expectedDocument':document})
        arranged=tool('solidedge_arrange_drawing_dimensions',{'expectedDocument':document})
        after=tool('solidedge_list_drawing_views',{'expectedDocument':document})
        check(not arranged['warnings'],label+' arrangement has no warnings')
        check(before['dimensionDetails']==after['dimensionDetails'],label+' arrangement is idempotent and preserves native values')
        groups={}
        native={d['mechCueId']:d for d in after['dimensionDetails']}
        for row in arranged['placements']:
            groups.setdefault((row['viewNumber'],row['horizontal']),[]).append(row)
            check(abs(native[row['mechCueId']]['sheetLineMm']-row['sheetLineMm'])<.001,label+' native dimension line matches placement')
        for rows in groups.values():
            rows.sort(key=lambda r:r['valueMm'])
            check(all(a['sheetLineMm']>b['sheetLineMm']+.001 for a,b in zip(rows,rows[1:])),label+' larger dimensions are farther outside')
        return arranged
    check_lanes(draft,'Plate')
    repeat=tool('solidedge_dimension_drawing_view',{'expectedDocument':draft,'viewNumber':1});check(not repeat['added'] and not repeat['warnings'],'Dimension repeat avoids duplicates')
    repeat=tool('solidedge_complete_drawing',{'expectedDocument':draft});check(not repeat['addedViews'] and not repeat['dimensions'],'Existing complete draft avoids duplicate views and dimensions')
    proposal=tool('solidedge_plan_drawing',{'expectedDocument':draft});check('Existing' in proposal['plan']['Reason'],'Existing front retained')
    check(not tool('solidedge_get_document')['dirty'],'Planning does not change drawing')
    for name,orientation in [('opposite','back'),('side','right')]:
        path,result=drawing(plate,name,frontOrientation=orientation,includeIsometric=False)
        check(result['plan']['FrontOrientation']==orientation and len(result['views']['views'])==3,'Explicit '+orientation+' front')
    cylinder=save_part('cylinder','circle',radiusMm=20,depthMm=50)
    path,result=drawing(cylinder,'cylinder-drawing',includeIsometric=False)
    check(len(result['views']['views'])==2 and sorted(round(d['valueMm'],3) for d in result['dimensions'])==[40,50],'Cylinder uses two views and diameter/length')
    step=save_part('step','polygon',depthMm=15,pointsJson=json.dumps([{'x':0,'y':0},{'x':100,'y':0},{'x':100,'y':35},{'x':60,'y':35},{'x':60,'y':70},{'x':0,'y':70}]))
    path,result=drawing(step,'step-drawing',includeIsometric=False)
    check_lanes(path,'Step')
    check(any(d['kind'].startswith('step-') for d in result['dimensions']),'Step dimensions added')
    ids=[(d['kind'],round(d['valueMm'],3)) for d in result['dimensions'] if d['kind'].startswith('step-')];check(len(ids)==len(set(ids)),'Step dimensions deduplicated across views')
    doc=tool('solidedge_new_document',{'kind':'draft','templatePath':str(templates/'iso metric draft.dft')})['fullName']
    tool('solidedge_add_drawing_view',{'expectedDocument':doc,'modelPath':plate,'orientation':'front','scale':1,'xMm':140,'yMm':120})
    result=tool('solidedge_complete_drawing',{'expectedDocument':doc});check(len(result['addedViews'])==3,'Missing projected and isometric views added')
    repeat=tool('solidedge_complete_drawing',{'expectedDocument':doc});check(not repeat['addedViews'] and not repeat['dimensions'],'Complete repeat avoids duplicates')
    tool('solidedge_save_document',{'expectedDocument':doc,'outputPath':str(root/'completed.dft')})
    asm=tool('solidedge_new_document',{'kind':'assembly','templatePath':str(templates/'iso metric assembly.asm')})['fullName']
    tool('solidedge_place_part',{'expectedDocument':asm,'filePath':plate})
    tool('solidedge_place_part',{'expectedDocument':asm,'filePath':cylinder,'xMm':200})
    assembly=str(root/'assembly.asm');tool('solidedge_save_document',{'expectedDocument':asm,'outputPath':assembly})
    path,result=drawing(assembly,'assembly-drawing',frontOrientation='front',dimensionMode='overall')
    check(any(abs(d['valueMm']-220)<.01 for d in result['dimensions']),'Assembly width is 220 mm')
    tool('solidedge_open_document',{'filePath':assembly});tool('solidedge_position_part',{'expectedDocument':assembly,'partNumber':2,'xMm':260,'yMm':0,'zMm':0});tool('solidedge_save_document',{'expectedDocument':assembly})
    tool('solidedge_open_document',{'filePath':path});tool('solidedge_update_drawing',{'expectedDocument':path});state=tool('solidedge_list_drawing_views',{'expectedDocument':path})
    check(any(abs(d['valueNative']-.28)<1e-6 for d in state['dimensionDetails']),'Associated assembly width follows component movement (280 mm)')
    tool('solidedge_save_document',{'expectedDocument':path})
    root.joinpath('result.json').write_text(json.dumps({'success':True,'checks':checks,'finalDrawing':path,'state':state},ensure_ascii=False,indent=2),encoding='utf-8')
    print('PASS: '+str(len(checks))+' native drawing checks. '+str(root/'result.json'),flush=True)
except Exception as error:
    root.joinpath('result.json').write_text(json.dumps({'success':False,'checks':checks,'error':str(error)},ensure_ascii=False,indent=2),encoding='utf-8');raise
finally:
    server.stdin.close()
    try:server.wait(timeout=5)
    except subprocess.TimeoutExpired:server.kill();server.wait()
