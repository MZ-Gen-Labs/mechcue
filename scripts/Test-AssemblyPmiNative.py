"""Native assembly PMI/drawing regression. Only new fixture files are modified.
Preserves the original documents; saves/closes completed fixture windows.
"""
import argparse, hashlib, json, os, pathlib, queue, subprocess, threading, time
parser=argparse.ArgumentParser()
parser.add_argument('--mcp',required=True)
parser.add_argument('--output',required=True)
args=parser.parse_args()
root=pathlib.Path(args.output).resolve()
if root.exists(): raise RuntimeError('Use a new fixture output directory')
root.mkdir(parents=True)
env=os.environ.copy();env['MECHCUE_MCP_NO_TRAY']='1'
startup=subprocess.STARTUPINFO();startup.dwFlags|=subprocess.STARTF_USESHOWWINDOW;startup.wShowWindow=0
server=subprocess.Popen([str(pathlib.Path(args.mcp).resolve())],stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=subprocess.DEVNULL,text=True,encoding='utf-8',env=env,startupinfo=startup)
responses=queue.Queue()
def read():
    for line in server.stdout: responses.put(json.loads(line))
threading.Thread(target=read,daemon=True).start()
sequence=0;report={};checks=[]
def request(method,params):
    global sequence
    sequence+=1
    server.stdin.write(json.dumps(dict(jsonrpc='2.0',id=sequence,method=method,params=params))+'\n');server.stdin.flush()
    deadline=time.monotonic()+90
    while True:
        reply=responses.get(timeout=max(.1,deadline-time.monotonic()))
        if reply.get('id')!=sequence: continue
        if 'error' in reply: raise RuntimeError(reply['error'])
        return reply['result']
def tool(name,arguments=None):
    report['pending']=name
    (root/'report.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
    result=request('tools/call',dict(name=name,arguments=arguments or {}))
    if result.get('isError'): raise RuntimeError(result)
    data=json.loads(next(c['text'] for c in result['content'] if c['type']=='text'))
    report.setdefault('calls',[]).append(dict(tool=name,result=data));report.pop('pending',None)
    (root/'report.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
    return data
def verify(ok,label):
    if not ok: raise AssertionError(label)
    checks.append(label);print('PASS: '+label,flush=True)
def save_as(kind,name):
    document=tool('solidedge_new_document',dict(kind=kind))['fullName']
    path=str(root/name)
    tool('solidedge_save_document',dict(expectedDocument=document,outputPath=path))
    return path
def hash_file(path): return hashlib.sha256(pathlib.Path(path).read_bytes()).hexdigest()
def pmi_value(document,ident):
    return next(d['valueNative'] for d in tool('solidedge_list_pmi',dict(expectedDocument=document))['dimensions'] if d['mechCueId']==ident)
def refused(name,arguments,text):
    try: tool(name,arguments)
    except RuntimeError as error: return text in str(error)
    return False

def close_assembly_fixture(document,original):
    try:
        tool('solidedge_close_document',dict(expectedDocument=document,returnDocument=original))
    except RuntimeError as error:
        if 'Save first; close refused for dirty document' not in str(error): raise
        # Strict MCP close correctly refuses even transient read-only child state.
        # Native cleanup is restricted to our own saved fixtures and read-only children.
        subprocess.run(['powershell.exe','-NoProfile','-File',str(pathlib.Path(__file__).with_name('Close-AssemblyPmiFixture.ps1')),
                        '-FixtureRoot',str(root),'-ReturnDocument',original],check=True,timeout=30)
        report['fixtureCleanup']='Native cleanup of transient read-only reference states; no constituent file saved'
try:
    request('initialize',dict(protocolVersion='2025-11-25',capabilities={},clientInfo=dict(name='Assembly PMI regression',version='1')))
    server.stdin.write(json.dumps(dict(jsonrpc='2.0',method='notifications/initialized'))+'\n');server.stdin.flush()
    report['before']=tool('solidedge_get_application_state')
    original=tool('solidedge_get_document')['fullName']
    part=tool('solidedge_new_document',dict(kind='part'))['fullName']
    tool('solidedge_extrude_profile',dict(expectedDocument=part,shape='rectangle',widthMm=120,heightMm=80,depthMm=20))
    part_path=str(root/'block.par')
    tool('solidedge_save_document',dict(expectedDocument=part,outputPath=part_path))
    tool('solidedge_close_document',dict(expectedDocument=part_path,returnDocument=original))
    part_hash=hash_file(part_path)
    child=save_as('assembly','child.asm')
    tool('solidedge_place_part',dict(expectedDocument=child,filePath=part_path))
    tool('solidedge_place_part',dict(expectedDocument=child,filePath=part_path,xMm=250,yMm=30,zMm=40,rzDeg=90))
    tool('solidedge_save_document',dict(expectedDocument=child))
    top_plan=tool('solidedge_plan_drawing',dict(expectedDocument=child))
    top=tool('solidedge_auto_pmi',dict(expectedDocument=child,dimensionMode='overall'))
    report['topLevelPmi']=top
    verify(not top['warnings'] and len(top['added'])==3,'Translated/rotated occurrences get three associated overall PMI dimensions')
    verify(all(not d['constraint'] for d in top['state']['dimensions']),'Assembly PMI dimensions are reference dimensions')
    verify(all(d.get('motionRebuilt') is True for d in top['added']),'New assembly PMI is rebuilt once after creation')
    # Moving the second parent froze API-created PMI before the creation-time rebuild.
    # Listing reads native values and never calls auto_pmi/UpdateStatus.
    tool('solidedge_position_part',dict(expectedDocument=child,partNumber=2,xMm=350,yMm=30,zMm=40,rzDeg=90))
    immediate=tool('solidedge_list_pmi',dict(expectedDocument=child))
    immediate_x=next(d['valueNative'] for d in immediate['dimensions'] if d['mechCueId']=='overall-X')
    report['immediateAssemblyPmiFollow']=dict(expectedX=.35,actualX=immediate_x,follows=abs(immediate_x-.35)<1e-7)
    verify(abs(immediate_x-.35)<1e-7,'New assembly PMI follows motion immediately without save/reopen or UpdateStatus')
    tool('solidedge_position_part',dict(expectedDocument=child,partNumber=2,xMm=250,yMm=30,zMm=40,rzDeg=90))
    distance=tool('solidedge_add_pmi_distance',dict(expectedDocument=child,id='test/part2-x',fromPart='1',fromPoint='vertex:min,min,min',
                                                  toPart='2',toPoint='vertex:min,min,min',axis='X'))
    report['distancePmi']=distance
    expected_x=abs(distance['to']['positionMm'][0]-distance['from']['positionMm'][0])
    verify(distance['motionRebuilt'] is True and not distance['warnings'] and abs(distance['valueMm']-expected_x)<1e-6,'Distance PMI between occurrences is created, verified and rebuilt')
    height=tool('solidedge_add_pmi_distance',dict(expectedDocument=child,id='test/part2-z',fromPart='1',fromPoint='vertex:min,min,min',
                                                toPart='2',toPoint='vertex:max,max,max',axis='Z',plane='YZ'))
    verify(height['plane']=='YZ' and height['modelView']=='MechCue PMI YZ' and height['motionRebuilt'] is True,'Distance PMI on an explicit plane joins its model view')
    tool('solidedge_position_part',dict(expectedDocument=child,partNumber=2,xMm=350,yMm=30,zMm=70,rzDeg=90))
    verify(abs(pmi_value(child,'test/part2-x')*1000-(expected_x+100))<1e-6 and abs(pmi_value(child,'test/part2-z')*1000-(height['valueMm']+30))<1e-6,
           'Distance PMI follows the moved second occurrence immediately')
    tool('solidedge_position_part',dict(expectedDocument=child,partNumber=2,xMm=250,yMm=30,zMm=40,rzDeg=90))
    verify(abs(pmi_value(child,'test/part2-x')*1000-expected_x)<1e-6,'Distance PMI follows return motion')
    verify(refused('solidedge_add_pmi_distance',dict(expectedDocument=child,id='test/part2-x',fromPart='1',fromPoint='vertex:min,min,min',toPart='2',toPoint='vertex:min,min,min',axis='X'),'already exists'),
           'Duplicate distance PMI id is refused')
    again=tool('solidedge_add_pmi_distance',dict(expectedDocument=child,id='test/part2-x',fromPart='1',fromPoint='vertex:min,min,min',toPart='2',toPoint='vertex:min,min,min',axis='X',replace=True))
    verify(again['replaced'] and sum(1 for d in tool('solidedge_list_pmi',dict(expectedDocument=child))['dimensions'] if d['mechCueId']=='test/part2-x')==1,'replace=true recreates one distance PMI')
    recreated=tool('solidedge_auto_pmi',dict(expectedDocument=child,dimensionMode='overall',recreateGenerated=True))
    report['recreatedPmi']=recreated
    ids={d['mechCueId'] for d in recreated['state']['dimensions']}
    verify(sorted(recreated['removedGenerated'])==['overall-X','overall-Y','overall-Z'] and len(recreated['added'])==3 and all(d['motionRebuilt'] for d in recreated['added'])
           and {'test/part2-x','test/part2-z'}<=ids,'recreateGenerated replaces generated PMI and keeps distance PMI')
    tool('solidedge_position_part',dict(expectedDocument=child,partNumber=2,xMm=250,yMm=30,zMm=40,rzDeg=90))
    tool('solidedge_save_document',dict(expectedDocument=child))
    tool('solidedge_reopen_document',dict(expectedDocument=child))
    tool('solidedge_position_part',dict(expectedDocument=child,partNumber=2,xMm=350,yMm=30,zMm=40,rzDeg=90))
    live=tool('solidedge_list_pmi',dict(expectedDocument=child))
    verify(abs(next(d['valueNative'] for d in live['dimensions'] if d['mechCueId']=='overall-X')-.35)<1e-7,
           'After save/reopen, assembly PMI follows motion without UpdateStatus or auto_pmi')
    tool('solidedge_position_part',dict(expectedDocument=child,partNumber=2,xMm=250,yMm=30,zMm=40,rzDeg=90))
    live=tool('solidedge_list_pmi',dict(expectedDocument=child))
    verify(abs(next(d['valueNative'] for d in live['dimensions'] if d['mechCueId']=='overall-X')-.25)<1e-7,
           'Assembly PMI follows return motion without explicit recalculation')
    repeat=tool('solidedge_auto_pmi',dict(expectedDocument=child,dimensionMode='overall'))
    verify(not repeat['added'] and not repeat['warnings'],'Repeated assembly PMI does not duplicate dimensions/views')
    tool('solidedge_position_part',dict(expectedDocument=child,partNumber=2,xMm=350,yMm=30,zMm=40,rzDeg=90))
    refresh=tool('solidedge_auto_pmi',dict(expectedDocument=child,dimensionMode='overall'))
    verify(not refresh['added'] and not refresh['warnings'] and len(refresh['updated'])==3,'Repeated PMI refreshes associated reference dimensions without duplicates')
    tool('solidedge_save_document',dict(expectedDocument=child))
    updated=tool('solidedge_list_pmi',dict(expectedDocument=child))
    verify(abs(next(d['valueNative'] for d in updated['dimensions'] if d['mechCueId']=='overall-X')-.35)<1e-7,'Associated assembly PMI updates when the referenced occurrence moves')
    tool('solidedge_position_part',dict(expectedDocument=child,partNumber=2,xMm=250,yMm=30,zMm=40,rzDeg=90))
    tool('solidedge_auto_pmi',dict(expectedDocument=child,dimensionMode='overall'))
    tool('solidedge_save_document',dict(expectedDocument=child))
    tool('solidedge_close_document',dict(expectedDocument=child,returnDocument=original))
    child_hash=hash_file(child)
    # Use a separate, annotation-free child for the drawing fixture. Native drawing/model
    # views may change transient display flags on read-only children that already have PMI.
    drawing_child=save_as('assembly','drawing-child.asm')
    tool('solidedge_place_part',dict(expectedDocument=drawing_child,filePath=part_path))
    tool('solidedge_place_part',dict(expectedDocument=drawing_child,filePath=part_path,xMm=250,yMm=30,zMm=40,rzDeg=90))
    tool('solidedge_save_document',dict(expectedDocument=drawing_child))
    tool('solidedge_close_document',dict(expectedDocument=drawing_child,returnDocument=original))
    drawing_child_hash=hash_file(drawing_child)
    nested=save_as('assembly','nested.asm')
    tool('solidedge_place_part',dict(expectedDocument=nested,filePath=drawing_child))
    tool('solidedge_place_part',dict(expectedDocument=nested,filePath=drawing_child,xMm=1000,yMm=500,zMm=100,rzDeg=90))
    tool('solidedge_save_document',dict(expectedDocument=nested))
    nested_result=tool('solidedge_auto_pmi',dict(expectedDocument=nested,dimensionMode='features',maxDimensions=30))
    report['nestedPmi']=nested_result
    verify(not nested_result['warnings'] and len([d for d in nested_result['added'] if d['kind']=='overall'])==3,'Repeated nested subassemblies get root-level associated PMI')
    verify(all(d['statusId']==6 for d in nested_result['state']['dimensions']),'Nested dimensions have native associated-reference status')
    nested_x=pmi_value(nested,'overall-X')
    tool('solidedge_position_part',dict(expectedDocument=nested,partNumber=2,xMm=1100,yMm=500,zMm=100,rzDeg=90))
    verify(abs(pmi_value(nested,'overall-X')-(nested_x+.1))<1e-7,'Root PMI follows a moved nested subassembly immediately')
    tool('solidedge_position_part',dict(expectedDocument=nested,partNumber=2,xMm=1000,yMm=500,zMm=100,rzDeg=90))
    verify(abs(pmi_value(nested,'overall-X')-nested_x)<1e-7,'Root PMI follows nested subassembly return motion')
    tool('solidedge_show_pmi_view',dict(expectedDocument=nested,viewName='MechCue PMI XZ'))
    shown=tool('solidedge_list_pmi',dict(expectedDocument=nested))
    verify(all(d['plane']=='XZ' for d in shown['dimensions'] if not d['hidden']),'Assembly PMI model view switches visible dimensions')
    tool('solidedge_save_document',dict(expectedDocument=nested))
    verify(hash_file(part_path)==part_hash and hash_file(child)==child_hash and hash_file(drawing_child)==drawing_child_hash,'PMI on the root assembly leaves constituent files unchanged')
    draft=str(root/'nested.dft')
    drawing=tool('solidedge_auto_drawing',dict(expectedDocument=nested,outputPath=draft,dimensionMode='overall'))
    report['drawing']=drawing
    verify(len(drawing['views']['views'])==4 and len(drawing['dimensions'])>=3,'Existing auto-drawing supports assemblies with four views and associated dimensions')
    tool('solidedge_close_document',dict(expectedDocument=draft,returnDocument=nested))
    close_assembly_fixture(nested,original)
    # Check the legacy part route independently so annotated parts are not drawing references.
    part_pmi=tool('solidedge_new_document',dict(kind='part'))['fullName']
    tool('solidedge_extrude_profile',dict(expectedDocument=part_pmi,shape='rectangle',widthMm=120,heightMm=80,depthMm=20))
    part_result=tool('solidedge_auto_pmi',dict(expectedDocument=part_pmi,dimensionMode='overall'))
    verify(not part_result['warnings'] and sorted(round(d['value'],3) for d in part_result['added'])==[20,80,120],'Existing part PMI remains compatible')
    part_pmi_path=str(root/'part-pmi.par')
    tool('solidedge_save_document',dict(expectedDocument=part_pmi,outputPath=part_pmi_path))
    tool('solidedge_close_document',dict(expectedDocument=part_pmi_path,returnDocument=original))
    circle=tool('solidedge_new_document',dict(kind='part'))['fullName']
    tool('solidedge_extrude_profile',dict(expectedDocument=circle,shape='circle',radiusMm=20,depthMm=40))
    circle_path=str(root/'circle.par')
    tool('solidedge_save_document',dict(expectedDocument=circle,outputPath=circle_path))
    tool('solidedge_close_document',dict(expectedDocument=circle_path,returnDocument=original))
    circular=save_as('assembly','circular.asm')
    tool('solidedge_place_part',dict(expectedDocument=circular,filePath=circle_path,xMm=100,yMm=60,zMm=20,rxDeg=90))
    result=tool('solidedge_auto_pmi',dict(expectedDocument=circular,dimensionMode='features'))
    report['circularPmi']=result
    verify(any(d['kind']=='diameter' and abs(d['value']-40)<1e-5 for d in result['added']),'Rotated circular occurrence gets associated diameter PMI')
    verify(all('unsupported extremum' in warning for warning in result['warnings']),'Unsupported curved overall extrema are reported instead of approximated')
    tool('solidedge_save_document',dict(expectedDocument=circular))
    close_assembly_fixture(circular,original)
    holes=save_as('assembly','holes.asm')
    tool('solidedge_place_part',dict(expectedDocument=holes,filePath=part_path))
    tool('solidedge_place_part',dict(expectedDocument=holes,filePath=circle_path,xMm=300,yMm=200,zMm=0))
    hole_result=tool('solidedge_auto_pmi',dict(expectedDocument=holes,dimensionMode='features'))
    report['holePmi']=hole_result
    positions=[d for d in hole_result['added'] if d['kind']=='hole-position']
    verify(len(positions)>=2 and not any('hole-' in warning for warning in hole_result['warnings']),'Diagonal hole-position PMI is created with verified orientation')
    hole_x=next(d for d in positions if d['id'].endswith('-x'))
    tool('solidedge_position_part',dict(expectedDocument=holes,partNumber=2,xMm=350,yMm=200,zMm=0))
    verify(abs(pmi_value(holes,hole_x['id'])*1000-(hole_x['value']+50))<1e-6,'Hole-position PMI follows the moved circular occurrence immediately')
    tool('solidedge_position_part',dict(expectedDocument=holes,partNumber=2,xMm=300,yMm=200,zMm=0))
    tool('solidedge_save_document',dict(expectedDocument=holes))
    close_assembly_fixture(holes,original)
    report['after']=tool('solidedge_get_application_state')
    report['checks']=checks;report['result']='PASS'
finally:
    (root/'report.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
    server.stdin.close()
    try: server.wait(timeout=5)
    except subprocess.TimeoutExpired: server.terminate();server.wait(timeout=5)
