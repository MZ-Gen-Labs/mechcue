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
                foreach(var action in HostCommands.RibbonActions)
                {
                    int bitmap=101+10*((int)action-1);string label=HostCommands.Caption(action);
                    Array names=new[]{typeof(MechCue.AddIn.TimeChartAddIn).GUID.ToString("B")+"_"+(int)action+"\n"+label+"\n"+label+"\n"+label};Array ids=new[]{(int)action};
                    addin.SetAddInInfoEx(resource,"{26618395-09D6-11D1-BA07-080036230602}","MechCue\n検証：" + HostCommands.Group(action),bitmap,bitmap+1,bitmap+2,bitmap+3,1,ref names,ref ids);
                    // Opt-in live integration check; use a separate group until the add-in is updated.
                    {
                        var button=addin.AddCommandBarButton("{26618395-09D6-11D1-BA07-080036230602}","MechCue\n検証：" + HostCommands.Group(action),(int)action);
                        var style=(ICommandButtonStyle)button;int wantedStyle=HostCommands.IsToggle(action)?7:action==HostAction.Play?3:5;style.Style=wantedStyle;
                        if(style.Style!=wantedStyle)throw new Exception("Large button style rejected");
                        Marshal.ReleaseComObject(button);
                    }
                    Console.WriteLine($"PASS native ribbon: {action}, local {(int)action}, runtime {ids.GetValue(0)}");
                }
            } catch(Exception ex) {error=ex;}
        });thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join();if(error!=null)throw error;
    }
}