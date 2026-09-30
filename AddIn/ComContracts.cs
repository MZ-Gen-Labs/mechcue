using System.Runtime.InteropServices;

namespace MechCue.AddIn.Interop;

// IUnknown interfaces require exact method order; verified against Solid Edge 2026.
public enum SeConnectMode { seConnectAtStartup = 1, seConnectByUser = 2, seConnectExternally = 3 }
public enum SeDisconnectMode { seDisconnectAtShutdown = 1, seDisconnectByUser = 2, seDisconnectExternally = 3 }

[ComImport, Guid("A50D497D-288A-11D2-B586-080036E8B802"), InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
public interface AddIn { }

[ComImport, ComVisible(true), Guid("D3F30AE5-2582-11D2-BAF9-080036230602"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface ISolidEdgeAddIn
{
    void OnConnection([In, MarshalAs(UnmanagedType.IDispatch)] object application, [In] SeConnectMode mode, [In, MarshalAs(UnmanagedType.Interface)] AddIn addIn);
    void OnConnectToEnvironment([In, MarshalAs(UnmanagedType.BStr)] string category, [In, MarshalAs(UnmanagedType.IDispatch)] object environment, [In] bool firstTime);
    void OnDisconnection([In] SeDisconnectMode mode);
}

[ComImport, ComVisible(true), Guid("0F539244-4816-11D2-B5AC-080036E8B802"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface ISEAddInEvents
{
    void OnCommand([In] int commandId);
    void OnCommandHelp([In] int frame, [In] int helpCommandId, [In] int commandId);
    void OnCommandUpdateUI([In] int commandId, [In, Out] ref int flags, [Out, MarshalAs(UnmanagedType.BStr)] out string text, [In, Out] ref int bitmapId);
}

[ComImport, Guid("DC601E2F-5BB3-4BF2-A9C7-03E60975E897"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface ISEAddInEx
{
    object Application { [return: MarshalAs(UnmanagedType.IDispatch)] get; }
    object AddInEvents { [return: MarshalAs(UnmanagedType.IUnknown)] get; }
    bool Connect { get; [param: In] set; }
    string Description { [return: MarshalAs(UnmanagedType.BStr)] get; [param: In, MarshalAs(UnmanagedType.BStr)] set; }
    string GUID { [return: MarshalAs(UnmanagedType.BStr)] get; }
    int GuiVersion { get; [param: In] set; }
    object Object { [return: MarshalAs(UnmanagedType.IDispatch)] get; [param: In, MarshalAs(UnmanagedType.IDispatch)] set; }
    string ProgID { [return: MarshalAs(UnmanagedType.BStr)] get; }
    bool Visible { get; [param: In] set; }
    void SetAddInInfo([In] int instance, [In, MarshalAs(UnmanagedType.BStr)] string environment, [In, MarshalAs(UnmanagedType.BStr)] string category,
        [In] int colorMedium, [In] int colorLarge, [In] int monoMedium, [In] int monoLarge, [In] int count,
        [In, MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_BSTR)] ref Array names,
        [In, Out, MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_I4)] ref Array ids);
    int AddCommand([In, MarshalAs(UnmanagedType.BStr)] string environment, [In, MarshalAs(UnmanagedType.BStr)] string name, [In] int id);
    [return: MarshalAs(UnmanagedType.Interface)]
    object AddCommandBarButton([In, MarshalAs(UnmanagedType.BStr)] string environment, [In, MarshalAs(UnmanagedType.BStr)] string bar, [In] int id);
    void SetAddInInfoEx([In, MarshalAs(UnmanagedType.BStr)] string resource, [In, MarshalAs(UnmanagedType.BStr)] string environment, [In, MarshalAs(UnmanagedType.BStr)] string category,
        [In] int colorMedium, [In] int colorLarge, [In] int monoMedium, [In] int monoLarge, [In] int count,
        [In, MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_BSTR)] ref Array names,
        [In, Out, MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_I4)] ref Array ids);
    object AddInEdgeBarEvents { [return: MarshalAs(UnmanagedType.IUnknown)] get; }
}
