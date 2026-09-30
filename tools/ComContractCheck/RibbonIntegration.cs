using System.Reflection;
using System.Runtime.InteropServices;
using MechCue;
using MechCue.AddIn.Interop;
static class RibbonIntegration
{
    [DllImport("ole32.dll",CharSet=CharSet.Unicode)] static extern int CLSIDFromProgID(string progId,out Guid id);
    [DllImport("oleaut32.dll",PreserveSig=false)] static extern void GetActiveObject(ref Guid id,IntPtr reserved,[MarshalAs(UnmanagedType.IUnknown)] out object app);
    public static void Run(string resource)
    {
        Exception? error=null;
        var thread=new Thread(()=> {
            try {
                Marshal.ThrowExceptionForHR(CLSIDFromProgID("SolidEdge.Application",out var id));GetActiveObject(ref id,IntPtr.Zero,out var app);
                object Get(object o,string property)=>o.GetType().InvokeMember(property,BindingFlags.GetProperty,null,o,null)!;
                var addins=Get(app,"AddIns");
                var raw=addins.GetType().InvokeMember("Item",BindingFlags.InvokeMethod|BindingFlags.GetProperty,null,addins,["{79B86022-7C7D-4768-A3A7-CF8EBD1F7826}"])!;
                var addin=(ISEAddInEx)raw;
                foreach(var action in Enum.GetValues<HostAction>())
                {
                    int bitmap=101+10*((int)action-1);string label=action switch { HostAction.Open=>"タイムチャート",HostAction.Play=>"再生",HostAction.Stop=>"停止",HostAction.Maximize=>"最大化",HostAction.Minimize=>"最小化",HostAction.Compact=>"最小表示",_=>"CAD保存" };
                    Array names=new[]{"\n"+label+"\n"+label+"\n"+label};Array ids=new[]{(int)action};
                    addin.SetAddInInfoEx(resource,"{26618395-09D6-11D1-BA07-080036230602}","MechCue",bitmap,bitmap+1,bitmap+2,bitmap+3,1,ref names,ref ids);
                    // Open already exists in the reported broken ribbon. Repair only missing buttons.
                    if(action!=HostAction.Open)
                    {
                        Console.WriteLine("AddCommand runtime: "+addin.AddCommand("{26618395-09D6-11D1-BA07-080036230602}",label,(int)action));
                        var button=addin.AddCommandBarButton("{26618395-09D6-11D1-BA07-080036230602}","MechCue",(int)action);
                        var style=(ICommandButtonStyle)button;style.Style=5;
                        if(style.Style!=5)throw new Exception("Large button style rejected");
                        Marshal.ReleaseComObject(button);
                    }
                    Console.WriteLine($"PASS native ribbon: {action}, local {(int)action}, runtime {ids.GetValue(0)}");
                }
            } catch(Exception ex) {error=ex;}
        });thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join();if(error!=null)throw error;
    }
}