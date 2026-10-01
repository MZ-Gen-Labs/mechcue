using System.Runtime.InteropServices;

namespace MechCue;
[ComVisible(true), Guid("90223887-09CD-11D1-BA07-080036230602"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IMechCueApplicationEvents
{
    void AfterActiveDocumentChange([MarshalAs(UnmanagedType.IDispatch)] object document);
    void AfterCommandRun(int command);
    void AfterDocumentOpen([MarshalAs(UnmanagedType.IDispatch)] object document);
    void AfterDocumentPrint([MarshalAs(UnmanagedType.IDispatch)] object document, int hdc, ref double modelToDc, ref int rect);
    void AfterDocumentSave([MarshalAs(UnmanagedType.IDispatch)] object document);
    void AfterEnvironmentActivate([MarshalAs(UnmanagedType.IDispatch)] object environment);
    void AfterNewDocumentOpen([MarshalAs(UnmanagedType.IDispatch)] object document);
    void AfterNewWindow([MarshalAs(UnmanagedType.IDispatch)] object window);
    void AfterWindowActivate([MarshalAs(UnmanagedType.IDispatch)] object window);
    void BeforeCommandRun(int command);
    void BeforeDocumentClose([MarshalAs(UnmanagedType.IDispatch)] object document);
    void BeforeDocumentPrint([MarshalAs(UnmanagedType.IDispatch)] object document, int hdc, ref double modelToDc, ref int rect);
    void BeforeEnvironmentDeactivate([MarshalAs(UnmanagedType.IDispatch)] object environment);
    void BeforeWindowDeactivate([MarshalAs(UnmanagedType.IDispatch)] object window);
    void BeforeQuit();
    void BeforeDocumentSave([MarshalAs(UnmanagedType.IDispatch)] object document);
}
[ComVisible(true), ClassInterface(ClassInterfaceType.None)]
public sealed class ApplicationEventSink(object target, Action beforeSave, Action beforeClose, Action? afterDeactivate = null) : IMechCueApplicationEvents
{
    public void AfterActiveDocumentChange(object document) { if (!SameDocument(target, document)) afterDeactivate?.Invoke(); }
    public void AfterCommandRun(int command) { }
    public void AfterDocumentOpen(object document) { }
    public void AfterDocumentPrint(object document, int hdc, ref double modelToDc, ref int rect) { }
    public void AfterDocumentSave(object document) { }
    public void AfterEnvironmentActivate(object environment) { }
    public void AfterNewDocumentOpen(object document) { }
    public void AfterNewWindow(object window) { }
    public void AfterWindowActivate(object window) { }
    public void BeforeCommandRun(int command) { }
    public void BeforeDocumentClose(object document) { if (SameDocument(target, document)) beforeClose(); }
    public void BeforeDocumentPrint(object document, int hdc, ref double modelToDc, ref int rect) { }
    public void BeforeEnvironmentDeactivate(object environment) { }
    public void BeforeWindowDeactivate(object window) { }
    public void BeforeQuit() { beforeClose(); }
    public void BeforeDocumentSave(object document) { if (SameDocument(target, document)) beforeSave(); }
    internal static bool SameDocument(object expected, object actual)
    {
        if (expected == null || actual == null) return false;
        if (Equals(expected, actual)) return true;
        if (!Marshal.IsComObject(expected) || !Marshal.IsComObject(actual)) return false;
        IntPtr first = IntPtr.Zero, second = IntPtr.Zero;
        try
        {
            // Events expose SolidEdgeDocument; the UI uses AssemblyDocument. Normalize
            // both to the same assembly interface, rather than compare RCW instances.
            first = Marshal.GetComInterfaceForObject(expected, typeof(IAssemblyKeyResolver));
            second = Marshal.GetComInterfaceForObject(actual, typeof(IAssemblyKeyResolver));
            return first == second;
        }
        catch (InvalidCastException) { return false; }
        finally { if (first != IntPtr.Zero) Marshal.Release(first); if (second != IntPtr.Zero) Marshal.Release(second); }
    }
}
