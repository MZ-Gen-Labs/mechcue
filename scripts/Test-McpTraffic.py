"""Validate traffic from real MCP transports without opening CAD or altering user settings."""
import argparse,json,os,pathlib,queue,subprocess,tempfile,threading,time
p=argparse.ArgumentParser();p.add_argument('--mcp',required=True);p.add_argument('--report',required=True);a=p.parse_args()
checks=[];owned=[]
with tempfile.TemporaryDirectory(prefix='mechcue-traffic-') as work:
    root=pathlib.Path(work);settings=root/'access.json';settings.write_text('{"schema":1,"mode":"chart"}')
    env=os.environ.copy();env['MECHCUE_MCP_SETTINGS_PATH']=str(settings);env['MECHCUE_MCP_NO_TRAY']='1'
    startup=subprocess.STARTUPINFO();startup.dwFlags|=subprocess.STARTF_USESHOWWINDOW;startup.wShowWindow=0
    def launch(environment):
        child=subprocess.Popen([str(pathlib.Path(a.mcp).resolve())],stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=subprocess.PIPE,env=environment,startupinfo=startup)
        owned.append(child);responses=queue.Queue()
        def read():
            for line in child.stdout:
                try:responses.put(json.loads(line))
                except Exception as e:responses.put(e)
        def errors():
            for line in child.stderr:pass
        threading.Thread(target=read,daemon=True).start();threading.Thread(target=errors,daemon=True).start()
        def request(i,method,params={}):
            frame={'jsonrpc':'2.0','id':i,'method':method,'params':params};wire=(json.dumps(frame,ensure_ascii=False)+'\n').encode()
            # Fragment input across UTF-8 bytes to exercise stream buffering.
            for pos in range(0,len(wire),7):child.stdin.write(wire[pos:pos+7]);child.stdin.flush()
            while True:
                response=responses.get(timeout=15)
                if isinstance(response,Exception):raise response
                if response.get('id')==i:return frame,response
        request(1,'initialize',{'protocolVersion':'2025-11-25','capabilities':{},'clientInfo':{'name':'通信テスト','version':'1'}})
        child.stdin.write(b'{"jsonrpc":"2.0","method":"notifications/initialized"}\n');child.stdin.flush()
        return child,request
    def records():
        result=[]
        for file in pathlib.Path(str(settings)+'.traffic').glob('*.jsonl'):
            for line in file.read_text(encoding='utf-8').splitlines():
                try:result.append(json.loads(line))
                except json.JSONDecodeError:pass
        return result
    try:
        child,call=launch(env);child2,call2=launch(env)
        frame,response=call(31,'tools/call',{'name':'solidedge_create_simulation_study','arguments':{'expectedDocument':'検証用.par'}})
        frame2,response2=call2(31,'ping')
        assert response['result']['isError'] and response2.get('result')=={}
        end=time.monotonic()+5
        while time.monotonic()<end:
            entries=records();selected=[e for e in entries if e['Id']=='31']
            if len(selected)==4:break
            time.sleep(.05)
        assert len(selected)==4,selected
        assert {e['ProcessId'] for e in selected}=={child.pid,child2.pid}
        for proc,expected_in,expected_out in [(child,frame,response),(child2,frame2,response2)]:
            pair={e['Direction']:e for e in selected if e['ProcessId']==proc.pid}
            assert json.loads(pair['in']['Json'])==expected_in
            assert json.loads(pair['out']['Json'])==expected_out
            assert pair['out']['DurationMs'] is not None and pair['out']['Method']==pair['in']['Method']
        assert next(e for e in selected if e['ProcessId']==child.pid and e['Direction']=='out')['Status']=='error'
        checks.append('Real JSON-RPC request/result equality, UTF-8 fragments, tool failures, timing and separate process IDs')
        options=pathlib.Path(str(settings)+'.monitor.json');options.write_text('{"Capture":false,"TopMost":true}')
        _,off=call(32,'ping');assert off.get('result')=={}
        time.sleep(.15);assert not any(e['Id']=='32' for e in records())
        options.write_text('{"Capture":true,"TopMost":true}')
        call(33,'ping');end=time.monotonic()+5
        while time.monotonic()<end and not any(e['Id']=='33' for e in records()):time.sleep(.05)
        assert any(e['Id']=='33' for e in records())
        checks.append('Recording switches live; protocol stays healthy when recording is off')
        broken=root/'broken.json';pathlib.Path(str(broken)+'.traffic').write_text('not a directory')
        broken_env=env.copy();broken_env['MECHCUE_MCP_SETTINGS_PATH']=str(broken)
        _,broken_call=launch(broken_env);_,healthy=broken_call(34,'ping');assert healthy.get('result')=={}
        checks.append('Journal I/O failure does not corrupt stdout or interrupt MCP')
    finally:
        for child in owned:
            if child.poll() is None:child.terminate()
            try:child.wait(5)
            except subprocess.TimeoutExpired:child.kill();child.wait()
target=pathlib.Path(a.report);target.parent.mkdir(parents=True,exist_ok=True);target.write_text('\n'.join('PASS: '+c for c in checks),encoding='utf-8')
print(target.read_text(encoding='utf-8'))
