"""Launch through MCP with licensing variables omitted, leaving Solid Edge open.

Requires no existing Edge process. Never quits Solid Edge or changes documents.
Only the test MCP process is stopped. Uses the user's existing access settings.
"""
import argparse
import json
import os
import pathlib
import queue
import subprocess
import threading
import time

parser = argparse.ArgumentParser()
parser.add_argument('--mcp', required=True)
parser.add_argument('--report', required=True)
args = parser.parse_args()
environment = os.environ.copy()
for name in ('SE_LICENSE_SERVER', 'SALT_LICENSE_SERVER', 'SPLM_LICENSE_SERVER',
             'UGS_LICENSE_SERVER', 'LM_LICENSE_FILE'):
    environment.pop(name, None)
environment['MECHCUE_MCP_NO_TRAY'] = '1'
startup = subprocess.STARTUPINFO()
startup.dwFlags |= subprocess.STARTF_USESHOWWINDOW
startup.wShowWindow = 0
server = subprocess.Popen([str(pathlib.Path(args.mcp).resolve())], env=environment,
                          stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                          stderr=subprocess.DEVNULL, text=True, startupinfo=startup)
responses = queue.Queue()
def read():
    for line in server.stdout:
        responses.put(json.loads(line))
threading.Thread(target=read, daemon=True).start()
sequence = 0
def request(method, params):
    global sequence
    sequence += 1
    server.stdin.write(json.dumps(dict(jsonrpc='2.0', id=sequence,
                                      method=method, params=params)) + '\n')
    server.stdin.flush()
    deadline = time.monotonic() + 30
    while time.monotonic() < deadline:
        message = responses.get(timeout=max(.1, deadline - time.monotonic()))
        if message.get('id') != sequence:
            continue
        if 'error' in message:
            raise RuntimeError(message['error'])
        return message['result']
    raise TimeoutError(method)
def tool(name):
    result = request('tools/call', dict(name=name, arguments={}))
    if result.get('isError'):
        raise RuntimeError(result)
    return json.loads(next(c['text'] for c in result['content'] if c['type'] == 'text'))
report = {}
try:
    request('initialize', dict(protocolVersion='2025-11-25', capabilities={},
                              clientInfo=dict(name='licensing launch regression', version='1')))
    server.stdin.write(json.dumps(dict(jsonrpc='2.0', method='notifications/initialized')) + '\n')
    server.stdin.flush()
    report['before'] = tool('solidedge_get_application_state')
    if report['before']['state'] != 'not_running':
        raise RuntimeError('Existing Solid Edge instance; no launch attempted')
    report['launch'] = tool('solidedge_start_application')
    print(json.dumps(report['launch']), flush=True)
    if not report['launch'].get('licenseEnvironmentSources'):
        raise RuntimeError('No Windows licensing settings were recovered')
    deadline = time.monotonic() + 90
    while time.monotonic() < deadline:
        report['after'] = tool('solidedge_get_application_state')
        if report['after']['state'] in ('ready', 'ready_no_document'):
            assert report['after']['processId'] == report['launch']['processId']
            report['result'] = 'PASS'
            break
        time.sleep(2)
    else:
        raise RuntimeError('Solid Edge did not become ready; inspect startup dialog')
    print(json.dumps(report['after']), flush=True)
finally:
    pathlib.Path(args.report).write_text(json.dumps(report, indent=2), encoding='utf-8')
    server.stdin.close()
    try:
        server.wait(timeout=5)
    except subprocess.TimeoutExpired:
        server.terminate()
        server.wait(timeout=5)
