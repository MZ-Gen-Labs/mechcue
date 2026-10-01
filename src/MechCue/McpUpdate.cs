using System.Diagnostics;
namespace MechCue;

public sealed class McpShutdownSignal : IDisposable
{
    readonly EventWaitHandle signal = new(false,EventResetMode.ManualReset,"Local\\MechCue-Mcp-Stop-" + Environment.ProcessId);
    readonly RegisteredWaitHandle registration;
    public McpShutdownSignal(Action shutdown) => registration = ThreadPool.RegisterWaitForSingleObject(signal,(_,_) => { try { shutdown(); } catch(Exception e) { DiagnosticLog.Error("mcp-update-stop",e); } },null,Timeout.Infinite,true);
    public void Dispose() { registration.Unregister(null); signal.Dispose(); }
}
public static class McpUpdate
{
    public static string Marker(string installRoot) => Path.Combine(Path.GetFullPath(installRoot),"MCP",".update-in-progress");
    public static bool Updating()
    {
        string folder=Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
        if(Path.GetFileName(folder).Equals("control",StringComparison.OrdinalIgnoreCase)) folder=Path.GetDirectoryName(folder)!;
        string path=Path.Combine(folder,".update-in-progress");
        return File.Exists(path) && DateTime.UtcNow-File.GetLastWriteTimeUtc(path)<TimeSpan.FromMinutes(15);
    }
    public static int StopForUpdate(string installRoot)
    {
        string root=Path.TrimEndingDirectorySeparator(Path.GetFullPath(installRoot));
        if(!Directory.Exists(root) || !File.Exists(Path.Combine(root,"MCP","MechCue.Mcp.exe"))) return 0;
        string scope=Path.Combine(root,"MCP")+Path.DirectorySeparatorChar;
        File.WriteAllText(Marker(root),DateTime.UtcNow.ToString("O"));
        for(int attempt=0;attempt<4;attempt++) {
            int remaining=0;
            foreach(var p in Process.GetProcesses()) using(p) {
                if(p.Id==Environment.ProcessId) continue;
                try {
                    if(p.ProcessName is not ("MechCue.Mcp" or "MechCue.Mcp.Control")) continue;
                    string? path=p.MainModule?.FileName;
                    if(path==null || !Path.GetFullPath(path).StartsWith(scope,StringComparison.OrdinalIgnoreCase)) continue;
                    DiagnosticLog.Write("mcp-update-target",new { pid=p.Id,path });
                    bool cooperative=false;
                    try { using var stop=EventWaitHandle.OpenExisting("Local\\MechCue-Mcp-Stop-"+p.Id); stop.Set(); cooperative=true; }
                    catch(WaitHandleCannotBeOpenedException) { }
                    if(cooperative) {
                        if(!p.WaitForExit(20000)) { remaining++; DiagnosticLog.Write("mcp-update-busy",new {pid=p.Id}); }
                    } else {
                        // Legacy servers have no shutdown endpoint. Only matching executables in this installation are stopped.
                        p.CloseMainWindow(); if(!p.WaitForExit(1000)) { p.Kill(); if(!p.WaitForExit(5000)) remaining++; }
                    }
                } catch(InvalidOperationException) { } // Process exited while being inspected.
                catch(Exception e) { remaining++; DiagnosticLog.Error("mcp-update-target",e); }
            }
            Thread.Sleep(300);
            if(remaining>0) return 1;
        }
        return 0;
    }
}
