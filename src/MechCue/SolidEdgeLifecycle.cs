using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;
namespace MechCue;

public sealed partial class Bridge
{
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window,out uint processId);
    static int CadProcessId(object application)
    {
        GetWindowThreadProcessId(new IntPtr(Convert.ToInt64(Get(application,"hWnd"))),out var id);
        if(id==0)throw new InvalidOperationException("Cannot identify Solid Edge process; no lifecycle mutation performed");
        return checked((int)id);
    }
    static object[] CadOpenDocuments(object application)
    {
        var docs=Get(application,"Documents");int count=Convert.ToInt32(Get(docs,"Count"));
        return Enumerable.Range(1,count).Select(i=>GetItem(docs,i)).ToArray();
    }
    public static object CadApplicationState()
    {
        var processes=Process.GetProcessesByName("Edge");
        var instances=processes.Select(p=>new {processId=p.Id,title=p.MainWindowTitle}).ToArray();
        foreach(var p in processes)p.Dispose();
        object app;
        try {app=CadApplication();}
        catch(Exception error){return new {state=instances.Length==0?"not_running":"starting_or_unavailable",instances,error=(error.InnerException??error).Message};}
        try {
            var documents=CadOpenDocuments(app);int processId=CadProcessId(app);
            return new {state=documents.Length==0?"ready_no_document":"ready",processId,instances,documents=documents.Select(CadInfo).ToArray()};
        } catch(Exception error){return new {state="starting_or_unavailable",instances,error=(error.InnerException??error).Message};}
    }
    public static object CadStartApplication()
    {
        try {var app=CadApplication();_=CadOpenDocuments(app);return new {state="reused",processId=CadProcessId(app)};}
        catch(Exception) {}
        var processes=Process.GetProcessesByName("Edge");
        if(processes.Length>0){var ids=processes.Select(p=>p.Id).ToArray();foreach(var p in processes)p.Dispose();return new {state="waiting",processIds=ids,message="Existing processes are not automation-ready; inspect startup/license dialogs. No duplicate was launched."};}
        using var prog=Registry.ClassesRoot.OpenSubKey(@"SolidEdge.Application\CLSID");
        string id=Convert.ToString(prog?.GetValue(null))??throw new InvalidOperationException("Solid Edge is not registered");
        using var server=Registry.ClassesRoot.OpenSubKey(@"CLSID\"+id+@"\LocalServer32");
        string command=Convert.ToString(server?.GetValue(null))??throw new InvalidOperationException("Solid Edge executable registration missing");
        int exeEnd=command.IndexOf(".exe",StringComparison.OrdinalIgnoreCase);
        string executable=command.StartsWith('"')?command.Split('"')[1]:exeEnd>=0?command[..(exeEnd+4)]:command;
        if(!File.Exists(executable))throw new FileNotFoundException("Registered executable not found; registration repair required",executable);
        using var process=Process.Start(new ProcessStartInfo(executable){UseShellExecute=true})??throw new InvalidOperationException("Solid Edge launch failed");
        return new {state="launched",processId=process.Id,message="Poll application state; licensing/startup may require user interaction."};
    }
    public static object CadExitApplication(int expectedProcessId)
    {
        if(expectedProcessId<=0)throw new ArgumentOutOfRangeException(nameof(expectedProcessId));
        var app=CadApplication();int actual=CadProcessId(app);
        if(actual!=expectedProcessId)throw new InvalidOperationException("Accessible Solid Edge instance differs from expectedProcessId");
        var documents=CadOpenDocuments(app);
        var dirty=documents.Where(d=>Convert.ToBoolean(Get(d,"Dirty"))).Select(CadName).ToArray();
        if(dirty.Length>0)throw new InvalidOperationException("Exit refused: unsaved documents: "+string.Join(", ",dirty));
        Call(app,"Quit");return new {state="quit_requested",processId=actual};
    }
    public static object CadOpenMechCue(string expectedDocument)
    {
        var app=CadApplication();_=CadDocument(app,expectedDocument,".asm",false);
        var addins=Get(app,"AddIns");object? automation=null;
        for(int i=1;i<=Convert.ToInt32(Get(addins,"Count"));i++)
        {
            var addin=GetItem(addins,i);
            if(!string.Equals(Convert.ToString(Get(addin,"GUID")),"{79B86022-7C7D-4768-A3A7-CF8EBD1F7826}",StringComparison.OrdinalIgnoreCase))continue;
            if(!Convert.ToBoolean(Get(addin,"Connect")))throw new InvalidOperationException("Enable the MechCue add-in in Solid Edge first");
            automation=Get(addin,"Object");break;
        }
        if(automation==null)throw new InvalidOperationException("Updated MechCue add-in automation is unavailable. Install/load the updated add-in.");
        string json=Convert.ToString(Call(automation,"OpenChart",expectedDocument))!;
        return System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);
    }
}
