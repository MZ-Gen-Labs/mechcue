"""Integration test using MCP JSON-RPC, two fresh offline MechCue windows, and stdio.
No user document is changed. Only test processes launched by this script are terminated.
"""
import argparse,json,subprocess,threading,queue,time,pathlib,os,tempfile
p=argparse.ArgumentParser();p.add_argument('--mechcue',required=True);p.add_argument('--mcp',required=True);p.add_argument('--report',required=True);p.add_argument('--native-read',action='store_true');a=p.parse_args()
owned=[];logs=[];checks=[]
settings_temp=tempfile.TemporaryDirectory(prefix='mechcue-mcp-test-')
settings_path=pathlib.Path(settings_temp.name)/'access.json'
child_env=os.environ.copy();child_env['MECHCUE_MCP_SETTINGS_PATH']=str(settings_path);child_env['MECHCUE_MCP_NO_TRAY']='1'

def launch(command,stdio=False):
    startup=subprocess.STARTUPINFO();startup.dwFlags|=subprocess.STARTF_USESHOWWINDOW;startup.wShowWindow=0
    child=subprocess.Popen(command,stdin=subprocess.PIPE if stdio else subprocess.DEVNULL,stdout=subprocess.PIPE if stdio else subprocess.DEVNULL,stderr=subprocess.PIPE if stdio else subprocess.DEVNULL,text=True,encoding='utf-8',startupinfo=startup,env=child_env)
    owned.append(child);return child
try:
    host=launch([str(pathlib.Path(a.mechcue).resolve()),'--enable-ai'])
    server=launch([str(pathlib.Path(a.mcp).resolve())]+(['--allow-solidedge'] if a.native_read else []),True)
    replies=queue.Queue()
    def output():
        for line in server.stdout:
            try:replies.put(json.loads(line))
            except json.JSONDecodeError:replies.put({'invalid_stdout':line})
    def errors():
        for line in server.stderr:logs.append(line)
    threading.Thread(target=output,daemon=True).start();threading.Thread(target=errors,daemon=True).start()
    seq=0
    def request(method,params):
        global seq
        seq+=1;server.stdin.write(json.dumps({'jsonrpc':'2.0','id':seq,'method':method,'params':params})+'\n');server.stdin.flush()
        end=time.monotonic()+25
        while time.monotonic()<end:
            msg=replies.get(timeout=max(.01,end-time.monotonic()))
            if 'invalid_stdout' in msg:raise AssertionError('Non-protocol stdout: '+str(msg))
            if msg.get('id')==seq:
                if 'error' in msg:raise AssertionError(msg)
                return msg['result']
        raise TimeoutError(method)
    def tool(name,args=None,error=False):
        result=request('tools/call',{'name':name,'arguments':args or {}})
        assert bool(result.get('isError',False))==error,(name,result)
        if error:return result
        text=''.join(c.get('text','') for c in result.get('content',[]) if c.get('type')=='text')
        return json.loads(text)
    result=request('initialize',{'protocolVersion':'2025-11-25','capabilities':{},'clientInfo':{'name':'MechCue integration test','version':'1'}})
    assert 'tools' in result['capabilities'];checks.append('MCP initialization')
    server.stdin.write(json.dumps({'jsonrpc':'2.0','method':'notifications/initialized'})+'\n');server.stdin.flush()
    tools=request('tools/list',{})['tools'];assert len(tools)==57,len(tools)
    assert next(t for t in tools if t['name']=='mechcue_get_state')['annotations']['readOnlyHint']
    for name in ['solidedge_list_planes','solidedge_list_features','solidedge_list_concept_templates','solidedge_get_concept_machine','solidedge_plan_drawing','solidedge_list_drawing_views','solidedge_list_pmi','solidedge_get_automation_settings','solidedge_list_simulation_faces','solidedge_list_simulation_studies','solidedge_list_simulation_materials','solidedge_get_simulation_results']:
        assert next(t for t in tools if t['name']==name)['annotations']['readOnlyHint']
    checks.append('57 tools and read-only annotations')
    catalog=tool('solidedge_list_concept_templates')
    assert {t['type'] for t in catalog['templates']}=={'mill3','mill4','mill5','gantry'}
    tool('solidedge_create_concept_machine',{'type':'mill3','outputDirectory':'unused'},True)
    tool('solidedge_set_concept_pose',{'expectedDocument':'test.asm','manifestPath':'test.json','valuesJson':'{}'},True)
    checks.append('Concept catalog without CAD and creation/pose permission gates')
    tool('solidedge_new_document',{'kind':'part'},True)
    tool('solidedge_extrude_profile',{'expectedDocument':'test.par','shape':'rectangle','depthMm':10,'widthMm':20,'heightMm':30},True)
    tool('solidedge_save_document',{'expectedDocument':'test.par','outputPath':'test.par'},True)
    tool('solidedge_auto_pmi',{'expectedDocument':'unused'},True)
    tool('solidedge_show_pmi_view',{'expectedDocument':'unused','viewName':'MechCue PMI XY'},True)
    for name,arguments in [
        ('solidedge_create_simulation_study',{'expectedDocument':'unused'}),
        ('solidedge_add_simulation_fixed',{'expectedDocument':'unused','studyNumber':1}),
        ('solidedge_add_simulation_load',{'expectedDocument':'unused','studyNumber':1,'kind':'force','value':100}),
        ('solidedge_run_simulation',{'expectedDocument':'unused','studyNumber':1}),
        ('solidedge_apply_simulation_material',{'expectedDocument':'unused','materialName':'Steel'}),
        ('solidedge_show_simulation_results',{'expectedDocument':'unused','studyNumber':1})]:tool(name,arguments,True)
    tool('solidedge_set_display_alerts',{'displayAlerts':False},True)
    checks.append('CAD mutations and application alert changes disabled without explicit write flag')
    def mode(value):
        temp=settings_path.with_suffix('.tmp');temp.write_text(json.dumps({'schema':1,'mode':value}),encoding='utf-8');temp.replace(settings_path)
    for value in ['read','write','chart','write','read']:
        mode(value)
        result=tool('solidedge_new_document',{'kind':'invalid'},True)
        message=str(result)
        if value=='write': assert 'kind must be part' in message,message
        else:
            assert 'kind must be part' not in message,message
            tool('solidedge_set_display_alerts',{'displayAlerts':False},True)
    mode('write')
    result=tool('solidedge_create_concept_machine',{'type':'invalid','outputDirectory':'unused'},True)
    assert 'type must be' in str(result),result
    result=tool('solidedge_create_concept_machine',{'type':'mill5','outputDirectory':'unused','xTravelMm':0},True)
    assert 'travel must be' in str(result),result
    checks.append('Invalid concept inputs rejected before CAD connection')
    bad=tool('solidedge_plan_drawing',{'expectedDocument':'unused','frontOrientation':'bad'},True);assert 'frontOrientation' in str(bad),bad
    bad=tool('solidedge_dimension_drawing_view',{'expectedDocument':'unused','viewNumber':1,'maxDimensions':0},True);assert 'maxDimensions' in str(bad),bad
    bad=tool('solidedge_complete_drawing',{'expectedDocument':'unused','dimensionMode':'bad'},True);assert 'dimensionMode' in str(bad),bad
    checks.append('Invalid drawing direction and dimension options rejected before CAD connection')
    bad=tool('solidedge_auto_pmi',{'expectedDocument':'unused','dimensionMode':'bad'},True);assert 'dimensionMode' in str(bad),bad
    bad=tool('solidedge_auto_pmi',{'expectedDocument':'unused','maxDimensions':0},True);assert 'maxDimensions' in str(bad),bad
    checks.append('PMI write permissions and invalid options rejected before CAD access')
    simulation_bad=[
        ('solidedge_create_simulation_study',{'meshSizeMm':-1},'meshSizeMm'),
        ('solidedge_create_simulation_study',{'meshLevel':0},'meshLevel'),
        ('solidedge_create_simulation_study',{'meshLevel':11},'meshLevel'),
        ('solidedge_run_simulation',{'studyNumber':1,'meshLevel':-1},'meshLevel'),
        ('solidedge_run_simulation',{'studyNumber':1,'meshLevel':11},'meshLevel'),
        ('solidedge_run_simulation',{'studyNumber':1,'meshLevel':8,'meshSizeMm':3},'not both'),
        ('solidedge_run_simulation',{'studyNumber':1,'meshSizeMm':-1},'meshSizeMm'),
        ('solidedge_add_simulation_fixed',{'studyNumber':1,'faceIdsJson':'[1]'},'faceIdsJson'),
        ('solidedge_add_simulation_load',{'studyNumber':1,'kind':'bad','value':1},'kind'),
        ('solidedge_add_simulation_load',{'studyNumber':1,'kind':'force','value':-1},'positive'),
        ('solidedge_add_simulation_load',{'studyNumber':1,'kind':'force','value':1,'directionX':0,'directionY':0,'directionZ':0},'nonzero'),
        ('solidedge_add_simulation_load',{'studyNumber':1,'kind':'force','value':1,'faceIdsJson':'["bad"]'},'face ID'),
        ('solidedge_get_simulation_results',{'studyNumber':1,'resultKind':'bad'},'resultKind'),
        ('solidedge_show_simulation_results',{'studyNumber':1,'resultKind':'bad'},'resultKind')]
    for name,arguments,expected in simulation_bad:
        bad=tool(name,{'expectedDocument':'unused',**arguments},True);assert expected in str(bad),bad
    checks.append('Simulation permissions and invalid inputs rejected before CAD connection')
    settings_path.write_text('invalid',encoding='utf-8')
    assert 'kind must be part' not in str(tool('solidedge_new_document',{'kind':'invalid'},True))
    settings_path.unlink()
    checks.append('Running MCP follows live tray settings without restart; malformed settings deny editing')

    def own_session(pid):
        end=time.monotonic()+15
        while time.monotonic()<end:
            sessions=tool('mechcue_list_sessions')
            found=[s for s in sessions if s.get('processId',s.get('ProcessId'))==pid]
            if found:return found[0].get('id',found[0].get('Id'))
            time.sleep(.1)
        raise AssertionError('Test session missing')
    sid=own_session(host.pid);context={'sessionId':sid}
    tool('mechcue_import_concept_axes',context|{'manifestPath':'C:/__missing_concept__/mechcue-concept.json'},True)
    checks.append('Concept import into disconnected session rejected without touching CAD')
    original=tool('mechcue_get_state',context);assert len(original['tracks'])==3 and not original['connected']
    state=tool('mechcue_set_keyframe',context|{'trackNumber':1,'time':2,'value':100});assert state['tracks'][0]['points'][1]['value']==100
    state=tool('mechcue_set_keyframe',context|{'trackId':state['tracks'][0]['id'],'time':1,'value':75});assert any(p['time']==1 and p['value']==75 for p in state['tracks'][0]['points'])
    before=state
    tool('mechcue_set_keyframe',context|{'trackNumber':1,'time':-1,'value':999},True)
    assert tool('mechcue_get_state',context)['tracks']==before['tracks'];checks.append('Set/insert by track number and stable ID; invalid mutation leaves graph unchanged')
    state=tool('mechcue_resample',context|{'endTime':10,'step':1});assert all([p['time'] for p in t['points']]==list(range(11)) for t in state['tracks'])
    assert not state['playing'] and not state['applyToCad']
    state=tool('mechcue_undo',context);assert state['tracks']==before['tracks'];checks.append('All-track 1-second resampling through 10 seconds; atomic batch undo')
    state=tool('mechcue_reset_values',context|{'trackNumber':2,'value':100});assert all(p['value']==100 for p in state['tracks'][1]['points']);assert state['tracks'][1]['id']==before['tracks'][1]['id'];checks.append('Reset track values and retain IDs/times')
    state=tool('mechcue_seek',context|{'time':4});assert state['time']==4
    state=tool('mechcue_play',context);assert state['playing'] and state['time']<4
    state=tool('mechcue_stop',context);assert not state['playing'];checks.append('Seek, playback from end and stop')
    tool('mechcue_resample',context|{'endTime':10,'step':0},True)
    listing=tool('mechcue_list_patterns',context);first=listing['activePatternId'];assert len(listing['patterns'])==1
    before=tool('mechcue_get_state',context)
    state=tool('mechcue_create_pattern',context|{'name':'Large demo','duplicate':True});second=state['activePatternId'];assert state['tracks']==before['tracks'] and not state['applyToCad'] and state['time']==0
    state=tool('mechcue_reset_values',context|{'trackNumber':1,'value':222})
    state=tool('mechcue_switch_pattern',context|{'patternId':first});assert state['tracks']==before['tracks']
    state=tool('mechcue_switch_pattern',context|{'patternName':'Large demo'});assert all(p['value']==222 for p in state['tracks'][0]['points'])
    state=tool('mechcue_rename_pattern',context|{'name':'Collision demo','description':'Intentional collision'});assert state['patternName']=='Collision demo'
    tool('mechcue_create_pattern',context|{'name':'Collision demo'},True)
    state=tool('mechcue_delete_pattern',context|{'patternId':second});assert state['activePatternId']==first
    tool('mechcue_delete_pattern',context,True)
    assert len(tool('mechcue_list_patterns',context)['patterns'])==1
    checks.append('Named patterns: independent edits, ID/name switching, rename/description, delete, shared track IDs, last-pattern and duplicate-name protection')
    host2=launch([str(pathlib.Path(a.mechcue).resolve()),'--enable-ai']);sid2=own_session(host2.pid)
    tool('mechcue_get_state',error=True)
    other=tool('mechcue_get_state',{'sessionId':sid2});assert other['tracks'][1]['points'][0]['value']==0;checks.append('Multiple windows require explicit session selection; edits remain isolated')
    if a.native_read:
        doc=tool('solidedge_get_document');assert doc['name'];parts=tool('solidedge_list_parts',{'expectedDocument':doc['fullName']});assert parts
        variables=tool('solidedge_list_variables',{'expectedDocument':doc['fullName']});assert 'variables' in variables
        assert any(v['name'] and v['value'] is not None and v['unitsType'] is not None for v in variables['variables']),[(v['unitsType'],v['error']) for v in variables['variables']]
        tool('solidedge_list_parts',{'expectedDocument':'__not_the_active_document__'},True)
        checks.append('Native Solid Edge document/parts/variables reads and document mismatch rejection; no save or geometry changes')
    else:
        tool('solidedge_get_document',error=True);checks.append('Direct Solid Edge access disabled without explicit server flag')
finally:
    for child in reversed(owned):
        if child.poll() is None:child.terminate()
        try:child.wait(timeout=5)
        except subprocess.TimeoutExpired:child.kill();child.wait()
    settings_temp.cleanup()
    target=pathlib.Path(a.report);target.parent.mkdir(parents=True,exist_ok=True)
    target.write_text('\n'.join('PASS: '+c for c in checks)+'\n\nServer stderr:\n'+''.join(logs),encoding='utf-8')
print('\n'.join('PASS: '+c for c in checks))
