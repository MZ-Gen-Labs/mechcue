using System.Reflection;
using System.Runtime.InteropServices;
using MechCue.AddIn.Interop;

var contracts = new[] { typeof(ISolidEdgeAddIn), typeof(ISEAddInEvents), typeof(ISEAddInEx) };
var expected = new[] {
    "OnConnection,OnConnectToEnvironment,OnDisconnection",
    "OnCommand,OnCommandHelp,OnCommandUpdateUI",
    "get_Application,get_AddInEvents,get_Connect,set_Connect,get_Description,set_Description,get_GUID,get_GuiVersion,set_GuiVersion,get_Object,set_Object,get_ProgID,get_Visible,set_Visible,SetAddInInfo,AddCommand,AddCommandBarButton,SetAddInInfoEx,get_AddInEdgeBarEvents"
};
var guids = new[] { "d3f30ae5-2582-11d2-baf9-080036230602", "0f539244-4816-11d2-b5ac-080036e8b802", "dc601e2f-5bb3-4bf2-a9c7-03e60975e897" };
for (int i = 0; i < contracts.Length; i++)
{
    var type = contracts[i];
    if (type.GUID != Guid.Parse(guids[i]) || !type.IsImport || type.GetCustomAttribute<InterfaceTypeAttribute>()?.Value != ComInterfaceType.InterfaceIsIUnknown)
        throw new Exception("COM identity mismatch: " + type.Name);
    if (string.Join(',', Methods(type).Select(m => m.Name)) != expected[i]) throw new Exception("COM vtable order changed: " + type.Name);
}
var assembly = typeof(MechCue.AddIn.TimeChartAddIn).Assembly;
if (assembly.GetReferencedAssemblies().Any(a => a.Name!.Contains("CADTeam") || a.Name.Contains("SolidEdge"))) throw new Exception("Vendor binary dependency remains");
// Optional local-only reference check. No Siemens binary is copied into artifacts.
if (args.Length == 1)
{
    var reference = Assembly.LoadFrom(Path.GetFullPath(args[0]));
    foreach (var type in contracts)
    {
        var original = reference.GetType("CADTeam.SolidEdge.Framework.Interop." + type.Name, true)!;
        if (original.GUID != type.GUID) throw new Exception("GUID differs: " + type.Name);
        var actual = Methods(type).Select(Signature).ToArray();
        var wanted = Methods(original).Select(Signature).ToArray();
        if (!actual.SequenceEqual(wanted)) throw new Exception($"Native signature mismatch: {type.Name}\nActual:\n{string.Join('\n', actual)}\nReference:\n{string.Join('\n', wanted)}");
    }
}
BitmapResources.Verify(assembly.Location);
Console.WriteLine("PASS: COM identities, method order, no vendor assembly dependency" + (args.Length == 1 ? ", native signatures match Solid Edge reference" : ""));

static MethodInfo[] Methods(Type type) => type.GetMethods().OrderBy(m => m.MetadataToken).ToArray();
static string Signature(MethodInfo method) => method.Name + " " + Native(method.ReturnParameter) + "(" + string.Join(',', method.GetParameters().Select(Native)) + ")";
static string Native(ParameterInfo parameter)
{
    var marshal = parameter.GetCustomAttribute<MarshalAsAttribute>();
    var type = parameter.ParameterType;
    string name = type.IsEnum ? Enum.GetUnderlyingType(type).Name : type.Name;
    if (marshal?.Value == UnmanagedType.Interface) name = "InterfacePointer";
    return $"{name}:{parameter.IsIn}:{parameter.IsOut}:{marshal?.Value}:{marshal?.SafeArraySubType}";
}

static class BitmapResources
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);
    [DllImport("kernel32.dll")] static extern bool FreeLibrary(IntPtr module);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindResource(IntPtr module, IntPtr name, IntPtr type);
    [DllImport("kernel32.dll")] static extern IntPtr LoadResource(IntPtr module, IntPtr resource);
    [DllImport("kernel32.dll")] static extern IntPtr LockResource(IntPtr resource);
    public static void Verify(string path)
    {
        var module = LoadLibraryEx(path, IntPtr.Zero, 0x22);
        if (module == IntPtr.Zero) throw new Exception("Cannot load add-in bitmap resources");
        try
        {
            foreach (var (id, size) in new[] { (101,16), (102,32), (103,16), (104,32) })
            {
                var resource = FindResource(module, new IntPtr(id), new IntPtr(2));
                if (resource == IntPtr.Zero) throw new Exception("Missing Win32 bitmap resource: " + id);
                var data = LockResource(LoadResource(module, resource));
                if (data == IntPtr.Zero || Marshal.ReadInt32(data) != 40 || Marshal.ReadInt32(data,4) != size || Marshal.ReadInt32(data,8) != size)
                    throw new Exception("Invalid command bitmap: " + id);
            }
        }
        finally { FreeLibrary(module); }
        Console.WriteLine("PASS: command bitmap resources 101-104, medium/large color and monochrome");
    }
}
