using System.Runtime.InteropServices;
namespace MechCue;
// Used by the standalone native verification driver, where Solid Edge can briefly
// reject automation while focus returns from a modal MechCue results window.
[ComVisible(true),Guid("00000016-0000-0000-C000-000000000046"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface ICadMessageFilter
{
    [PreserveSig] int HandleInComingCall(int type,IntPtr caller,int ticks,IntPtr info);
    [PreserveSig] int RetryRejectedCall(IntPtr callee,int ticks,int rejection);
    [PreserveSig] int MessagePending(IntPtr callee,int ticks,int pending);
}
[ComVisible(true)]
public sealed class CadBusyRetry:ICadMessageFilter,IDisposable
{
    [DllImport("ole32.dll")] static extern int CoRegisterMessageFilter(ICadMessageFilter? value,out ICadMessageFilter? previous);
    [DllImport("ole32.dll")] static extern int CoInitializeEx(IntPtr reserved,uint flags);
    [DllImport("ole32.dll")] static extern void CoUninitialize();
    readonly ICadMessageFilter? previous;
    bool disposed;
    public CadBusyRetry(){Marshal.ThrowExceptionForHR(CoInitializeEx(IntPtr.Zero,2));try{Marshal.ThrowExceptionForHR(CoRegisterMessageFilter(this,out previous));}catch{CoUninitialize();throw;}}
    public int HandleInComingCall(int type,IntPtr caller,int ticks,IntPtr info)=>0;
    public int RetryRejectedCall(IntPtr callee,int ticks,int rejection)=>ticks<5000&&rejection is 1 or 2?100:-1;
    public int MessagePending(IntPtr callee,int ticks,int pending)=>2;
    public void Dispose(){if(disposed)return;disposed=true;CoRegisterMessageFilter(previous,out _);CoUninitialize();}
}
