using System.Runtime.InteropServices;
namespace MechCue.AddIn;
[ComVisible(true),Guid("AAC3C076-8875-4B36-A78C-B67AD34F62B5"),InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
public interface IMechCueAutomation
{
    [DispId(1)] string OpenChart(string expectedDocument);
}
