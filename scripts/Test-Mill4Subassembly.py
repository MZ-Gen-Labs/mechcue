"""Verify the built MCP against the new mill4 detail study; no MechCue EXE UI.
Only the supplied active study assembly is posed, restored to home and saved.
"""
import json, os, pathlib, queue, subprocess, sys, threading
root = pathlib.Path(sys.argv[1]).resolve()
server_path = pathlib.Path(sys.argv[2]).resolve()
env = os.environ.copy()
env['MECHCUE_MCP_NO_TRAY'] = '1'
startup = subprocess.STARTUPINFO()
startup.dwFlags |= subprocess.STARTF_USESHOWWINDOW
startup.wShowWindow = 0
server = subprocess.Popen([str(server_path)], stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                          stderr=subprocess.PIPE, text=True, encoding='utf-8', env=env, startupinfo=startup)
replies = queue.Queue()
threading.Thread(target=lambda: [replies.put(json.loads(line)) for line in server.stdout], daemon=True).start()
threading.Thread(target=lambda: [None for line in server.stderr], daemon=True).start()
ident = 0
history = []
def request(method, params):
    global ident
    ident += 1
    server.stdin.write(json.dumps({'jsonrpc':'2.0','id':ident,'method':method,'params':params})+'\n')
    server.stdin.flush()
    while True:
        r = replies.get(timeout=30)
        if r.get('id') == ident:
            if 'error' in r: raise RuntimeError(r['error'])
            return r['result']
def tool(name, args, error=False):
    r = request('tools/call', {'name':name,'arguments':args})
    history.append({'tool':name,'result':r})
    assert bool(r.get('isError')) == error, r
    if error: return r
    return json.loads(''.join(c['text'] for c in r['content'] if c['type']=='text'))
expected = str(root/'mill4-detail.asm')
args = {'expectedDocument':expected,'manifestPath':str(root/'mechcue-concept.json')}
home = {'X':0,'Y':0,'Z':0,'C':0}
verified = False
try:
    request('initialize', {'protocolVersion':'2025-11-25','capabilities':{},'clientInfo':{'name':'mill4 subassembly verification','version':'1'}})
    server.stdin.write('{"jsonrpc":"2.0","method":"notifications/initialized"}\n'); server.stdin.flush()
    doc = tool('solidedge_get_document', {})
    assert doc['fullName'].lower() == expected.lower()
    before = tool('solidedge_get_concept_machine', args)
    assert len(before['bodies']) == 5
    tool('solidedge_set_concept_pose', {**args,'valuesJson':json.dumps({'X':100,'Y':50,'Z':100,'C':90})})
    current = {b['id']:b['actualMatrix'] for b in tool('solidedge_get_concept_machine', args)['bodies']}
    assert abs(current['y-saddle'][13]-.05) < 1e-8
    assert abs(current['x-table'][12]-.1) < 1e-8
    assert abs(current['c-fixture'][1]-1) < 1e-8
    assert abs(current['z-head'][14]-.1) < 1e-8
    tool('solidedge_set_concept_pose', {**args,'valuesJson':json.dumps({'X':251,'Y':0,'Z':0,'C':0})}, error=True)
    unchanged = {b['id']:b['actualMatrix'] for b in tool('solidedge_get_concept_machine', args)['bodies']}
    assert current == unchanged
    verified = True
finally:
    try:
        tool('solidedge_set_concept_pose', {**args,'valuesJson':json.dumps(home)})
        tool('solidedge_save_document', {'expectedDocument':expected})
        (root/'mcp-subassembly-verification.json').write_text(json.dumps({'passed':verified,'history':history},ensure_ascii=False,indent=2),encoding='utf-8')
    finally:
        server.stdin.close()
        try: server.wait(timeout=5)
        except subprocess.TimeoutExpired: server.terminate(); server.wait(timeout=5)
print('PASS: built MCP read/pose of five subassemblies, C rotation, range rejection without mutation, home restored and saved.')
