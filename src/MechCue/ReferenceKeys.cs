using System.Runtime.InteropServices;

namespace MechCue;
// IDispatch member copied from the Solid Edge 2026 contract; a typed out-dispatch
// argument avoids DISP_E_TYPEMISMATCH from late-bound ref VARIANT output.
[ComImport, Guid("00C6BF00-483B-11CE-951A-08003601BE52"), InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
internal interface IAssemblyKeyResolver
{
    [DispId(24582), PreserveSig]
    void BindKeyToObject([In, Out, MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_UI1)] ref Array referenceKey, [Out, MarshalAs(UnmanagedType.IDispatch)] out object target);
}
