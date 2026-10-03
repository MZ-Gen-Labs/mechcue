"""Test only copied payloads and processes owned by this test; never install or touch CAD."""
import argparse,json,os,pathlib,shutil,subprocess,tempfile,time
p=argparse.ArgumentParser();p.add_argument('--mcp',required=True);p.add_argument('--report',required=True);p.add_argument('--legacy');a=p.parse_args()
owned=[];checks=[]
startup=subprocess.STARTUPINFO();startup.dwFlags|=subprocess.STARTF_USESHOWWINDOW;startup.wShowWindow=0
with tempfile.TemporaryDirectory(prefix='mechcue-update-test-') as work:
    root=pathlib.Path(work);install=root/'Installed MechCue';scope=install/'MCP';outside=root/'Outside';helper=root/'Helper'
    shutil.copytree(pathlib.Path(a.mcp).resolve().parent,scope);shutil.copytree(scope,outside);shutil.copytree(scope/'control',helper)
    env=os.environ.copy();env['MECHCUE_MCP_NO_TRAY']='1';env['MECHCUE_MCP_SETTINGS_PATH']=str(root/'settings.json')
    (root/'settings.json').write_text('{"schema":1,"mode":"chart"}',encoding='utf-8');settings=(root/'settings.json').read_bytes()
    def launch(exe,args=[]):
        child=subprocess.Popen([str(exe),*args],stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=subprocess.PIPE,startupinfo=startup,env=env);owned.append(child);return child
    def ready(child):
        child.stdin.write((json.dumps({'jsonrpc':'2.0','id':1,'method':'initialize','params':{'protocolVersion':'2025-11-25','capabilities':{},'clientInfo':{'name':'update test','version':'1'}}})+'\n').encode());child.stdin.flush()
        assert child.stdout.readline(),child.stderr.read().decode()
    def shutdown():
        result=subprocess.run([str(helper/'MechCue.Mcp.Control.exe'),'--shutdown-for-update',str(install)],startupinfo=startup,env=env,timeout=90);assert result.returncode==0,result.returncode
    try:
        sentinel=launch(outside/'MechCue.Mcp.exe');ready(sentinel)
        server=launch(scope/'MechCue.Mcp.exe');ready(server)
        tray=launch(scope/'control'/'MechCue.Mcp.Control.exe');time.sleep(1);assert tray.poll() is None
        shutdown();assert server.wait(5)==0 and tray.wait(5)==0
        assert sentinel.poll() is None,'Stopped another installation'
        assert (root/'settings.json').read_bytes()==settings,'Access settings changed'
        checks.append('MCP and tray gracefully exited; outside installation and access settings unchanged')
        blocked=launch(scope/'MechCue.Mcp.exe');assert blocked.wait(5)==0
        blocked_tray=launch(scope/'control'/'MechCue.Mcp.Control.exe');assert blocked_tray.wait(5)==0
        checks.append('AI auto-restart is blocked while update marker is active')
        marker=scope/'.update-in-progress';marker.unlink();resumed=launch(scope/'MechCue.Mcp.exe');ready(resumed);shutdown();assert resumed.wait(5)==0
        checks.append('Fresh MCP connection initializes after update marker is cleared')
        if a.legacy:
            marker.unlink();shutil.copytree(pathlib.Path(a.legacy).resolve().parent,scope,dirs_exist_ok=True)
            old=launch(scope/'MechCue.Mcp.exe');ready(old)
            old_tray=launch(scope/'control'/'MechCue.Mcp.Control.exe');time.sleep(1);assert old_tray.poll() is None
            shutdown();old.wait(5);old_tray.wait(5);assert sentinel.poll() is None
            checks.append('Legacy MCP and tray without shutdown endpoint stopped within installation scope')
    finally:
        for child in reversed(owned):
            if child.poll() is None:child.kill()
            child.wait(timeout=5)
        if (scope/'.update-in-progress').exists():(scope/'.update-in-progress').unlink()
    result='\n'.join('PASS: '+c for c in checks);print(result);pathlib.Path(a.report).write_text(result,encoding='utf-8')
