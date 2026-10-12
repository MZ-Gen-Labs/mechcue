"""Fresh installed-MCP verification, using the installed add-in (no MechCue UI EXE)."""
import datetime,json,os,pathlib,queue,subprocess,sys,threading,copy,shutil
sys.stdout.reconfigure(encoding='utf-8')
folder=pathlib.Path(sys.argv[1]).resolve();phase=sys.argv[2]; folder.mkdir(parents=True,exist_ok=True)
env=os.environ.copy();env['MECHCUE_MCP_NO_TRAY']='1'
startup=subprocess.STARTUPINFO();startup.dwFlags|=subprocess.STARTF_USESHOWWINDOW;startup.wShowWindow=0
server=subprocess.Popen([r'C:\Program Files\MechCue\MCP\MechCue.Mcp.exe'],stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=subprocess.PIPE,text=True,encoding='utf-8',env=env,startupinfo=startup)
messages=queue.Queue();history=[];seq=0
threading.Thread(target=lambda:[messages.put(json.loads(line)) for line in server.stdout],daemon=True).start()
threading.Thread(target=lambda:[None for line in server.stderr],daemon=True).start()
def request(method,params):
    global seq
    seq+=1;server.stdin.write(json.dumps({'jsonrpc':'2.0','id':seq,'method':method,'params':params})+'\n');server.stdin.flush()
    while True:
        response=messages.get(timeout=300)
        if response.get('id')==seq:
            if 'error' in response:raise RuntimeError(response['error'])
            return response['result']
def tool(name,args={},error=False):
    result=request('tools/call',{'name':name,'arguments':args});history.append({'tool':name,'args':args,'result':result})
    assert bool(result.get('isError'))==error,(name,result)
    if error:return result
    return json.loads(''.join(c['text'] for c in result['content'] if c['type']=='text'))
try:
    request('initialize',{'protocolVersion':'2025-11-25','capabilities':{},'clientInfo':{'name':'installed workflow verification','version':'1'}})
    server.stdin.write('{"jsonrpc":"2.0","method":"notifications/initialized"}\n');server.stdin.flush()
    catalog=request('tools/list',{})['tools'];assert len(catalog)==66,len(catalog)
    concept=str(folder/'concept'/'concept.asm');manifest=str(folder/'concept'/'mechcue-concept.json')
    if phase=='concept':
        result=tool('solidedge_create_concept_machine',{'type':'mill4','outputDirectory':str(folder/'concept'),'xTravelMm':500,'yTravelMm':300,'zTravelMm':300})
        tree=tool('solidedge_get_assembly_tree',{'expectedDocument':concept});assert tree['count']==9 and not tree['truncated']
        tool('solidedge_set_view',{'expectedDocument':concept,'orientation':'isometric'})
        tool('solidedge_export_view_image',{'expectedDocument':concept,'outputPath':str(folder/'concept.jpg')})
        print(json.dumps({'concept':result,'treeCount':tree['count']}),flush=True)
    elif phase=='call':
        print(json.dumps(tool(sys.argv[3],json.loads(sys.argv[4])),ensure_ascii=False),flush=True)
    elif phase=='chart':
        sid=sys.argv[3];context={'sessionId':sid}
        tool('mechcue_import_concept_axes',{**context,'manifestPath':manifest})
        state=tool('mechcue_get_state',context);assert state['connected'] and sum(t['name'] in ['X','Y','Z','C'] and t['target'] is not None for t in state['tracks'])==4,state
        ids={t['name']:t['id'] for t in state['tracks'] if t['name'] in ['X','Y','Z','C']}
        tool('mechcue_rename_pattern',{**context,'name':'確認 XYZC連動・退避'})
        tool('mechcue_resample',{**context,'endTime':16,'step':4})
        rows=[[0,0,150,0],[100,50,100,90],[0,100,50,180],[-100,50,100,270],[0,0,150,360]]
        for index,row in enumerate(rows):
            for axis,value in zip(['X','Y','Z','C'],row):tool('mechcue_set_keyframe',{**context,'trackId':ids[axis],'time':index*4,'value':value})
        before=tool('mechcue_get_state',context)
        tool('mechcue_import_concept_axes',{**context,'manifestPath':manifest})
        after=tool('mechcue_get_state',context);assert before['tracks']==after['tracks'],'Repeated import changed charts'
        (folder/'concept-chart.json').write_text(json.dumps(after,ensure_ascii=False,indent=2),encoding='utf-8')
        print(json.dumps(after,ensure_ascii=False),flush=True)
    elif phase in ['detail','detail-resume']:
        detail=folder/'detail';detail.mkdir(exist_ok=phase=='detail-resume')
        model=json.loads((folder/'concept'/'mechcue-concept.json').read_text(encoding='utf-8'))
        bodies={b['id']:b for b in model['bodies']}
        identity=[1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1]
        assert all(b['geometryMatrix']==identity for b in bodies.values()),'Native part basis requires explicit transform support'
        for body in bodies.values():
            if not (detail/body['file']).exists():shutil.copy2(folder/'concept'/body['file'],detail/body['file'])
        for name,shape,x,y,z in [('y-rail','rectangle',20,640,10),('x-rail','rectangle',850,20,10),('z-rail','rectangle',20,10,620),('servo','rectangle',80,100,100),('head-arm','rectangle',180,170,100),('z-block','rectangle',40,10,60),('c-housing','circle',230,230,20)]:
            if (detail/(name+'.par')).exists():continue
            doc=tool('solidedge_new_document',{'kind':'part'})['fullName']
            tool('solidedge_extrude_profile',{'expectedDocument':doc,'shape':shape,'depthMm':z,'widthMm':x,'heightMm':y,'radiusMm':x/2,'xMm':-x/2 if shape=='rectangle' else 0,'yMm':-y/2 if shape=='rectangle' else 0,'direction':'symmetric','featureName':name})
            tool('solidedge_save_document',{'expectedDocument':doc,'outputPath':str(detail/(name+'.par'))})
            print('PART '+name,flush=True)
        # Extend the support under the rearward column without changing the concept copy.
        base=str(detail/'base.par');tool('solidedge_open_document',{'filePath':base})
        if len(tool('solidedge_list_features',{'expectedDocument':base})['features'])==1:tool('solidedge_extrude_profile',{'expectedDocument':base,'shape':'rectangle','widthMm':900,'heightMm':300,'depthMm':100,'xMm':-450,'yMm':200,'direction':'symmetric','featureName':'Rear-column-support'})
        tool('solidedge_save_document',{'expectedDocument':base})
        for name,width,height,xs,ys,depth in [('saddle',30,260,[-125,95],[-130,-130],40),('table',700,30,[-350,-350],[-90,60],30)]:
            path=str(detail/(name+'.par'));tool('solidedge_open_document',{'filePath':path})
            features=tool('solidedge_list_features',{'expectedDocument':path})['features'];assert len(features)<=3 and all(f['status']==features[0]['status'] for f in features)
            for index,(x,y) in enumerate(zip(xs,ys)):
                if index+1<len(features):continue
                tool('solidedge_extrude_profile',{'expectedDocument':path,'shape':'rectangle','widthMm':width,'heightMm':height,'depthMm':depth,'xMm':x,'yMm':y,'operation':'cut','direction':'negative','featureName':'Rail-clearance-'+str(index+1)})
            tool('solidedge_save_document',{'expectedDocument':path})
        groups=[('fixed-structure','',['base','column']),('y-saddle','Y',['saddle']),('x-table','X',['table']),('c-fixture','C',['rotary-table','workpiece']),('z-head','Z',['head','spindle','tool'])]
        extra={'fixed-structure':[('y-rail',-110,0,105),('y-rail',110,0,105),('z-rail',-65,275,690),('z-rail',65,275,690),('servo',0,-390,120),('servo',0,370,1060)],'y-saddle':[('x-rail',0,-75,185),('x-rail',0,75,185),('servo',680,0,210)],'x-table':[('c-housing',0,0,250)],'c-fixture':[],'z-head':[('head-arm',0,175,850),('z-block',-65,265,800),('z-block',65,265,800)]}
        def place(doc,name,xyz):return tool('solidedge_place_part',{'expectedDocument':doc,'filePath':str(detail/(name+'.par')),'xMm':xyz[0],'yMm':xyz[1],'zMm':xyz[2]})
        for name,axis,parts in groups:
            doc=tool('solidedge_new_document',{'kind':'assembly'})['fullName']
            for part in parts:
                xyz=bodies[part]['centerMm'].copy()
                if part=='column':xyz[1]=370
                place(doc,part,xyz)
            for part,x,y,z in extra[name]:place(doc,part,[x,y,z])
            tool('solidedge_save_document',{'expectedDocument':doc,'outputPath':str(detail/(name+'.asm'))})
            print('SUBASSEMBLY '+name,flush=True)
        doc=tool('solidedge_new_document',{'kind':'assembly'})['fullName'];model['assemblyFile']='detailed.asm';model['bodies']=[]
        for name,axis,parts in groups:
            tool('solidedge_place_part',{'expectedDocument':doc,'filePath':str(detail/(name+'.asm')),'zMm':290 if axis=='C' else 0})
            # Native occurrence names are read after placement, never guessed.
            actual=tool('solidedge_get_assembly_tree',{'expectedDocument':doc,'maxDepth':1})['nodes'][-1]
            model['bodies'].append({'id':name,'parent':axis,'shape':'box','sizeMm':[900,1000,1150],'centerMm':[0,0,0],'file':name+'.asm','occurrence':actual['Name'],'geometryMatrix':identity})
        target=str(detail/'detailed.asm');tool('solidedge_save_document',{'expectedDocument':doc,'outputPath':target})
        (detail/'mechcue-concept.json').write_text(json.dumps(model,ensure_ascii=False,indent=2),encoding='utf-8')
        tool('solidedge_get_concept_machine',{'expectedDocument':target,'manifestPath':str(detail/'mechcue-concept.json')})
        tree=tool('solidedge_get_assembly_tree',{'expectedDocument':target});assert tree['count']==27 and not tree['truncated'],tree
        tool('solidedge_set_view',{'expectedDocument':target,'orientation':'isometric'})
        tool('solidedge_export_view_image',{'expectedDocument':target,'outputPath':str(folder/'detail.jpg')})
        print('PASS detailed structure '+str(tree['count']),flush=True)
    elif phase=='fix-support':
        fixed=str(folder/'detail'/'fixed-structure.asm');tool('solidedge_open_document',{'filePath':fixed})
        tree=tool('solidedge_get_assembly_tree',{'expectedDocument':fixed})
        motor=next(n for n in tree['nodes'] if n['Path']=='/7');assert motor['Name']=='servo.par:1' and abs(motor['LocalMatrix'][13]+.39)<1e-8
        tool('solidedge_position_part',{'expectedDocument':fixed,'partNumber':7,'xMm':0,'yMm':-390,'zMm':160})
        tool('solidedge_save_document',{'expectedDocument':fixed})
        target=str(folder/'detail'/'detailed.asm');tool('solidedge_open_document',{'filePath':target});tool('solidedge_save_document',{'expectedDocument':target})
        print('PASS Y motor moved up 40 mm; rigid subassembly reference retained',flush=True)
    elif phase=='inspection':
        target=str(folder/'detail'/'detailed.asm');definition=str(folder/'detail'/'mechcue-concept.json');guard={'expectedDocument':target,'manifestPath':definition}
        summary=[];suffix='-'+sys.argv[3] if len(sys.argv)>3 else ''
        try:
            for label,values in [('home',{'X':0,'Y':0,'Z':0,'C':0}),('t4',{'X':100,'Y':50,'Z':100,'C':90}),('t8',{'X':0,'Y':100,'Z':50,'C':180}),('t12',{'X':-100,'Y':50,'Z':100,'C':270}),('limits',{'X':250,'Y':150,'Z':150,'C':360})]:
                tool('solidedge_set_concept_pose',{**guard,'valuesJson':json.dumps(values)})
                full=tool('solidedge_check_interference',{'expectedDocument':target})
                if full['count']>0:tool('solidedge_export_interference_report',{'expectedDocument':target,'reportPath':str(folder/('interference-'+label+suffix+'.txt'))})
                pairs=[]
                for i in range(1,6):
                    for j in range(i+1,6):
                        result=tool('solidedge_check_interference',{'expectedDocument':target,'set1Json':json.dumps([i]),'set2Json':json.dumps([j])})
                        assert result['analysisComplete'];pairs.append({'set1':i,'set2':j,'count':result['count'],'state':result['state']})
                summary.append({'label':label,'values':values,'fullCount':full['count'],'state':full['state'],'crossComparisons':pairs})
                print(json.dumps(summary[-1]),flush=True)
            (folder/('interference-summary'+suffix+'.json')).write_text(json.dumps(summary,ensure_ascii=False,indent=2),encoding='utf-8')
        finally:
            tool('solidedge_set_concept_pose',{**guard,'valuesJson':'{"X":0,"Y":0,"Z":0,"C":0}'})
            tool('solidedge_save_document',{'expectedDocument':target})
    elif phase=='sample-path':
        target=str(folder/'detail'/'detailed.asm');definition=str(folder/'detail'/'mechcue-concept.json');guard={'expectedDocument':target,'manifestPath':definition}
        state=json.loads((folder/'concept-chart.json').read_text(encoding='utf-8'));tracks={t['name']:t['points'] for t in state['tracks'] if t['name'] in ['X','Y','Z','C']}
        def at(points,t):
            if t>=points[-1]['time']:return points[-1]['value']
            for a,b in zip(points,points[1:]):
                if a['time']<=t<=b['time']:return a['value']+(b['value']-a['value'])*(t-a['time'])/(b['time']-a['time'])
            raise ValueError(t)
        checked=[]
        try:
            for time in range(17):
                values={name:at(points,time) for name,points in tracks.items()};tool('solidedge_set_concept_pose',{**guard,'valuesJson':json.dumps(values)})
                result=tool('solidedge_check_interference',{'expectedDocument':target});assert result['clear'],(time,result)
                checked.append({'time':time,'values':values,'count':result['count']})
            (folder/'sampled-path.json').write_text(json.dumps(checked,indent=2),encoding='utf-8');print('PASS 17 static samples, 1-second spacing; not a continuous swept-path proof',flush=True)
        finally:
            tool('solidedge_set_concept_pose',{**guard,'valuesJson':'{"X":0,"Y":0,"Z":0,"C":0}'});tool('solidedge_save_document',{'expectedDocument':target})
    elif phase=='final':
        context={'sessionId':sys.argv[3]};state=tool('mechcue_get_state',context);original=json.loads((folder/'concept-chart.json').read_text(encoding='utf-8'))
        assert state['connected'] and not state['applyToCad'] and not state['playing']
        assert state['tracks']==original['tracks'] and state['activePatternId']==original['activePatternId']
        target=str(folder/'detail'/'detailed.asm');doc=tool('solidedge_get_document');assert doc['fullName']==target and not doc['dirty']
        tree=tool('solidedge_get_assembly_tree',{'expectedDocument':target});assert tree['count']==27 and all(n['KeyPath'] for n in tree['nodes'])
        tool('solidedge_export_view_image',{'expectedDocument':target,'outputPath':str(folder/'detail-final.jpg')})
        (folder/'final-state.json').write_text(json.dumps({'state':state,'document':doc,'tree':tree},ensure_ascii=False,indent=2),encoding='utf-8')
        print('PASS original track/pattern IDs and keyframes retained in installed add-in; saved home, 27 keyed occurrences',flush=True)
    elif phase=='motion':
        sid=sys.argv[3];context={'sessionId':sid}
        target=str(folder/sys.argv[4]/sys.argv[5]);definition=str(folder/sys.argv[4]/'mechcue-concept.json'); guard={'expectedDocument':target,'manifestPath':definition}
        assert tool('mechcue_get_state',context)['applyToCad']
        for time,values in [(4,{'X':100,'Y':50,'Z':100,'C':90}),(8,{'X':0,'Y':100,'Z':50,'C':180})]:
            tool('mechcue_seek',{**context,'time':time})
            chart=tool('solidedge_get_concept_machine',guard)
            tool('solidedge_set_concept_pose',{**guard,'valuesJson':json.dumps(values)})
            native=tool('solidedge_get_concept_machine',guard)
            assert chart['bodies']==native['bodies'],'Chart pose differs from absolute forward kinematics'
        before=tool('mechcue_get_state',context)
        tool('mechcue_set_keyframe',{**context,'trackId':next(t['id'] for t in before['tracks'] if t['name']=='X'),'time':4,'value':251},True)
        assert tool('mechcue_get_state',context)['tracks']==before['tracks']
        tool('solidedge_save_document',{'expectedDocument':target})
        print('PASS chart-driven t=4/t=8 poses, range rejection and CAD save',flush=True)
    else:raise ValueError(phase)
finally:
    (folder/(phase+'-'+datetime.datetime.now().strftime('%H%M%S')+'.json')).write_text(json.dumps(history,ensure_ascii=False,indent=2),encoding='utf-8')
    server.stdin.close()
    try:server.wait(timeout=5)
    except subprocess.TimeoutExpired:server.terminate();server.wait(timeout=5)
