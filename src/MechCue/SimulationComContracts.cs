using System.Runtime.InteropServices;
namespace MechCue;
// Minimal IDispatch contracts from Solid Edge 2026. Simulation geometry parameters
// require SAFEARRAY(VT_DISPATCH), not late-bound SAFEARRAY(VT_VARIANT).
[ComImport,Guid("01ED826E-60CF-40F9-AE87-FDBF326C83D6"),InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
internal interface ISimulationStudyGeometry
{
    [DispId(1610678273)] void SetGeometries([In,MarshalAs(UnmanagedType.SafeArray,SafeArraySubType=VarEnum.VT_DISPATCH)] ref Array geometries);
}
[ComImport,Guid("A5EBDCF4-C5E4-4020-B9F7-69836AA9A143"),InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
internal interface ISimulationLoadOwner
{
    [DispId(1610678273)] void AddLoad([In,MarshalAs(UnmanagedType.SafeArray,SafeArraySubType=VarEnum.VT_DISPATCH)] ref Array geometries,
        int type,double value,double angularAcceleration,int direction,double x,double y,double z,double xx,double yy,double zz,
        uint color,uint angularColor,double spacingFactor,double sizeFactor,double steeringLength,uint properties,double spacing,double size,
        [Out,MarshalAs(UnmanagedType.IDispatch)] out object load);
}
[ComImport,Guid("222FB896-6114-4EA0-BD9B-FB6706A635E6"),InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
internal interface ISimulationConstraintOwner
{
    [DispId(1610678273)] void AddConstraint([In,MarshalAs(UnmanagedType.SafeArray,SafeArraySubType=VarEnum.VT_DISPATCH)] ref Array geometries,
        int type,int direction,double x,double y,double z,double xx,double yy,double zz,uint color,double spacingFactor,double sizeFactor,
        double steeringLength,uint properties,double spacing,double size,[Out,MarshalAs(UnmanagedType.IDispatch)] out object constraint,
        uint degreesOfFreedom,[In,MarshalAs(UnmanagedType.IDispatch)] object? coordinateSystem);
}
[ComImport,Guid("4A890CF3-06CB-427F-A986-A8F17356B5F0"),InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
internal interface ISimulationLoadGeometry
{
    [DispId(1610678280)] void GetGeometries([In,Out,MarshalAs(UnmanagedType.SafeArray,SafeArraySubType=VarEnum.VT_DISPATCH)] ref Array geometries);
}
[ComImport,Guid("28C4BD5A-AAE3-44CD-A92A-BD6983923260"),InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
internal interface ISimulationConstraintGeometry
{
    [DispId(1610678276)] void GetGeometries([In,Out,MarshalAs(UnmanagedType.SafeArray,SafeArraySubType=VarEnum.VT_DISPATCH)] ref Array geometries);
}
