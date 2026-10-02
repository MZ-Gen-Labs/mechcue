"""Native MCP verification on the existing mill4 study. No independent MechCue UI.
Only viewport changes and new JPEG/report output; assembly geometry is untouched.
"""
import json, os, pathlib, queue, subprocess, sys, threading, datetime
root = pathlib.Path(sys.argv[1]).resolve()
env = os.environ.copy(); env['MECHCUE_MCP_NO_TRAY'] = '1'
startup = subprocess.STARTUPINFO(); startup.dwFlags |= subprocess.STARTF_USESHOWWINDOW; startup.wShowWindow = 0
server = subprocess.Popen([str(pathlib.Path(sys.argv[2]).resolve())], stdin=subprocess.PIPE, stdout=subprocess.PIPE,
    stderr=subprocess.PIPE, text=True, encoding='utf-8', env=env, startupinfo=startup)
replies = queue.Queue()
threading.Thread(target=lambda: [replies.put(json.loads(line)) for line in server.stdout], daemon=True).start()
threading.Thread(target=lambda: [None for line in server.stderr], daemon=True).start()
ident = 0; history = []; passed = False
def request(method, params):
    global ident
    ident += 1
    server.stdin.write(json.dumps({'jsonrpc':'2.0','id':ident,'method':method,'params':params})+'\n'); server.stdin.flush()
    while True:
        value = replies.get(timeout=60)
        if value.get('id') == ident:
            if 'error' in value: raise RuntimeError(value['error'])
            return value['result']
def tool(name, args, error=False):
    value = request('tools/call', {'name':name,'arguments':args})
    history.append({'tool':name,'arguments':args,'result':value})
    assert bool(value.get('isError')) == error, value
    if error: return value
    return json.loads(''.join(c['text'] for c in value['content'] if c['type']=='text'))
expected = str(root/'mill4-detail.asm'); guard = {'expectedDocument':expected}
stamp = datetime.datetime.now().strftime('%Y%m%d-%H%M%S')
try:
    request('initialize', {'protocolVersion':'2025-11-25','capabilities':{},'clientInfo':{'name':'inspection verification','version':'1'}})
    server.stdin.write('{"jsonrpc":"2.0","method":"notifications/initialized"}\n'); server.stdin.flush()
    catalog = request('tools/list', {})['tools']; assert len(catalog) == 66
    for name in ['solidedge_get_assembly_tree','solidedge_get_view','solidedge_check_interference']:
        assert next(t for t in catalog if t['name']==name)['annotations']['readOnlyHint']
    document = tool('solidedge_get_document', {}); assert document['fullName'].lower() == expected.lower()
    before = tool('solidedge_list_parts', guard)
    tree = tool('solidedge_get_assembly_tree', guard)
    assert tree['count']==48 and not tree['truncated'], tree
    assert sum(n['IsAssembly'] for n in tree['nodes'])==5
    assert all(n['KeyPath'] for n in tree['nodes']), tree['warnings']
    limited = tool('solidedge_get_assembly_tree', {**guard,'maxOccurrences':3})
    assert limited['truncated'] and limited['count']==3
    depth = tool('solidedge_get_assembly_tree', {**guard,'maxDepth':1})
    assert depth['truncated'] and depth['count']==5
    camera = tool('solidedge_get_view', guard)
    tool('solidedge_set_view', {'expectedDocument':'wrong.asm'}, True)
    assert tool('solidedge_get_view', guard)==camera
    tool('solidedge_set_view', {**guard,'zoomFactor':0}, True)
    tool('solidedge_set_view', {**guard,'orientation':'bad'}, True)
    for orientation in ['front','top','right','isometric']:
        view = tool('solidedge_set_view', {**guard,'orientation':orientation})
        assert not view['perspective']
        eye = view['eyeMetres']; target = view['targetMetres']; delta = [a-b for a,b in zip(eye,target)]
        if orientation=='front': assert delta[1]<0 and abs(delta[0])+abs(delta[2])<1e-8
        if orientation=='top': assert delta[2]>0 and abs(delta[0])+abs(delta[1])<1e-8
        if orientation=='right': assert delta[0]>0 and abs(delta[1])+abs(delta[2])<1e-8
    tool('solidedge_set_view', {**guard,'fit':False,'zoomFactor':1.1})
    tool('solidedge_set_view', guard)
    image = str(root/('mcp-view-'+stamp+'.jpg'))
    tool('solidedge_export_view_image', {**guard,'outputPath':image,'width':800,'height':600})
    tool('solidedge_export_view_image', {**guard,'outputPath':image}, True)
    for ids in [('[]','[2]'),('[1]','[1]'),('[1,1]','[]'),('[99]','[]')]:
        tool('solidedge_check_interference', {**guard,'set1Json':ids[0],'set2Json':ids[1]}, True)
    full = tool('solidedge_check_interference', guard)
    assert full['analysisComplete'] and full['count']>0 and not full['clear'], full
    cross = []
    for i in range(1,6):
        for j in range(i+1,6):
            result = tool('solidedge_check_interference', {**guard,'set1Json':json.dumps([i]),'set2Json':json.dumps([j])})
            assert result['analysisComplete'], result
            cross.append(result)
    report = str(root/('mcp-interference-'+stamp+'.txt'))
    exported = tool('solidedge_export_interference_report', {**guard,'reportPath':report,'set1Json':'[1]','set2Json':'[5]'})
    assert exported['bytes']>0
    assert 'servo.par:2' in exported['reportText'] and 'head-arm.par:1' in exported['reportText']
    assert not exported['reportTextTruncated']
    tool('solidedge_export_interference_report', {**guard,'reportPath':report}, True)
    assert tool('solidedge_list_parts', guard)==before
    assert tool('solidedge_get_document', {})['dirty']==document['dirty']
    passed = True
    print(json.dumps({'passed':True,'nodes':tree['count'],'referenceKeys':48,'fullInterferenceCount':full['count'],'crossComparisons':[(r['set1'],r['set2'],r['count'],r['pairDetailsComplete']) for r in cross],'image':image}))
finally:
    (root/('mcp-inspection-'+stamp+'.json')).write_text(json.dumps({'passed':passed,'history':history},ensure_ascii=False,indent=2),encoding='utf-8')
    server.terminate(); server.wait(timeout=10)
