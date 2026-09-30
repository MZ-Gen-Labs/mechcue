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
