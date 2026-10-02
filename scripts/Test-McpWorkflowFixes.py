"""Native regression checks on disposable copies; never saves the source documents.
Usage: --source-detail <detail folder> --output <new folder> --mcp <built exe>.
Solid Edge must already be running and its active document must be clean.
"""
import argparse, json, os, pathlib, queue, shutil, subprocess, threading

p = argparse.ArgumentParser()
p.add_argument('--source-detail', required=True)
p.add_argument('--output', required=True)
p.add_argument('--mcp', required=True)
a = p.parse_args()
source = pathlib.Path(a.source_detail).resolve()
folder = pathlib.Path(a.output).resolve()
if folder.exists():
    raise RuntimeError('Use a new output folder')
env = os.environ.copy()
env['MECHCUE_MCP_NO_TRAY'] = '1'
startup = subprocess.STARTUPINFO()
startup.dwFlags |= subprocess.STARTF_USESHOWWINDOW
startup.wShowWindow = 0
server = subprocess.Popen([str(pathlib.Path(a.mcp).resolve()), '--allow-solidedge-write'],
    stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
    text=True, encoding='utf-8', env=env, startupinfo=startup)
messages = queue.Queue()
threading.Thread(target=lambda: [messages.put(json.loads(line)) for line in server.stdout], daemon=True).start()
threading.Thread(target=lambda: [None for line in server.stderr], daemon=True).start()
seq = 0
history = []
original = None

def request(method, params):
    global seq
    seq += 1
    server.stdin.write(json.dumps(dict(jsonrpc='2.0', id=seq, method=method, params=params)) + '\n')
    server.stdin.flush()
    while True:
        response = messages.get(timeout=60)
        if response.get('id') == seq:
            if 'error' in response:
                raise RuntimeError(response['error'])
            return response['result']

def tool(name, args=None, error=False):
    result = request('tools/call', dict(name=name, arguments=args or {}))
    history.append(dict(tool=name, args=args, result=result))
    assert bool(result.get('isError')) == error, result
    if error:
        return result
    return json.loads(''.join(c['text'] for c in result['content'] if c['type'] == 'text'))

try:
    request('initialize', dict(protocolVersion='2025-11-25', capabilities={}, clientInfo=dict(name='workflow fixes regression', version='1')))
    server.stdin.write('{"jsonrpc":"2.0","method":"notifications/initialized"}\n')
    server.stdin.flush()
    original = tool('solidedge_get_document')
    assert not original['dirty'], 'Save your active document before running this test'
    shutil.copytree(source, folder)
    assembly = str(folder / 'detailed.asm')
    guard = dict(expectedDocument=assembly)
    tool('solidedge_open_document', dict(path=assembly))
    before = tool('solidedge_list_parts', guard)
    settings = tool('solidedge_get_mechcue_settings', guard)
    assert settings['settingsJson'] is not None
    view = tool('solidedge_set_view', dict(**guard, orientation='isometric'))
    assert isinstance(view['dirtyBefore'], bool) and isinstance(view['dirtyAfter'], bool)
    image = tool('solidedge_export_view_image', dict(**guard, outputPath=str(folder / 'fixes-view.jpg')))
    assert 'dirtyBefore' in image and 'dirtyAfter' in image
    report = tool('solidedge_export_interference_report', dict(**guard, reportPath=str(folder / 'clear-report.txt')))
    assert report['analysis']['clear'] and report['reportCreated'] and report['bytes'] > 0, report
    assert tool('solidedge_list_parts', guard) == before
    tool('solidedge_reopen_document', dict(expectedDocument='wrong.asm'), error=True)
    if tool('solidedge_get_document')['dirty']:
        tool('solidedge_reopen_document', guard, error=True)
    tool('solidedge_save_document', guard)
    reopened = tool('solidedge_reopen_document', guard)
    assert reopened['embeddedSettingsPreserved']
    assert tool('solidedge_get_mechcue_settings', guard)['settingsJson'] == settings['settingsJson']
    part = str(folder / 'saddle.par')
    tool('solidedge_open_document', dict(path=part))
    features = tool('solidedge_list_features', dict(expectedDocument=part))
    feature_name = features['features'][0]['name']
    rejected = tool('solidedge_extrude_profile', dict(expectedDocument=part, shape='rectangle', widthMm=20,
        heightMm=20, depthMm=10, operation='cut', featureName=feature_name), error=True)
    assert 'Duplicate feature name' in str(rejected)
    assert tool('solidedge_list_features', dict(expectedDocument=part)) == features
    assert not tool('solidedge_get_document')['dirty']
    print('PASS: clear report, view dirty status, duplicate rejection without mutation, embedded settings and guarded reopen')
finally:
    try:
        if original:
            tool('solidedge_open_document', dict(path=original['fullName']))
    finally:
        if folder.exists():
            (folder / 'fixes-history.json').write_text(json.dumps(history, ensure_ascii=False, indent=2), encoding='utf-8')
        server.terminate()
        server.wait(timeout=10)
