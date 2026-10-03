using System.ComponentModel;
using ModelContextProtocol.Server;
using MechCue;

[McpServerToolType]
public static class WorkflowTools
{
    [McpServerTool(ReadOnly=true),Description("Read Solid Edge readiness, exact process ID and all open documents/dirty flags. starting_or_unavailable includes possible license waits; process existence alone is not readiness. Requires Read/select mode.")]
    public static Task<string> solidedge_get_application_state()=>SolidEdgeTools.ExecuteCad("application-state",Bridge.CadApplicationState,false);
    [McpServerTool,Description("Start registered Solid Edge when no Edge process exists; reuse an accessible instance. Never launch duplicates while startup/license wait is in progress. Returns immediately; poll get_application_state until ready or ready_no_document. Does not open files.")]
    public static Task<string> solidedge_start_application()=>SolidEdgeTools.ExecuteCad("start-application",Bridge.CadStartApplication);
    [McpServerTool(Destructive=true),Description("Gracefully quit the accessible instance identified by expectedProcessId. Refuses if ANY open document is dirty or inspection fails. Never kills, discards edits, or automatically saves. Returns quit_requested; poll state for termination.")]
    public static Task<string> solidedge_exit_application(int expectedProcessId)=>SolidEdgeTools.ExecuteCad("exit-application",()=>Bridge.CadExitApplication(expectedProcessId));
    [McpServerTool,Description("Open/connect MechCue through the installed add-in to expectedDocument (assembly), reusing its chart/settings. Returns sessionId. Requires updated installed add-in; no UI click or standalone fallback.")]
    public static Task<string> solidedge_open_mechcue(string expectedDocument)=>SolidEdgeTools.ExecuteCad("open-mechcue",()=>Bridge.CadOpenMechCue(expectedDocument));
    [McpServerTool,Description("Explicitly save one existing writable referenced part/subassembly without opening a window. expectedDocument guards the active assembly; documentPath must occur in its recursive references. No unrelated files, no SaveAs, no chart save. Subassemblies with dirty descendants are refused: explicitly save each named child first. Use to recover document_switch_requires_save, then save the active assembly. Requires Creation/edit mode.")]
    public static Task<string> solidedge_save_referenced_document(string expectedDocument,string documentPath)=>SolidEdgeTools.ExecuteCad("save-referenced-document",()=>Bridge.CadSaveReferenced(expectedDocument,documentPath));
    [McpServerTool,Description("Set rigid nested occurrence local pose by stable keyPath from get_assembly_tree. XYZ mm; Rx/Ry/Rz deg in parent frame. Nested changes edit a child document and affect EVERY reference to that file: require modifySharedSubassembly=true. Flexible overrides unsupported. Verifies pose/rolls back on failure; no save.")]
    public static Task<string> solidedge_position_nested_part(string expectedDocument,string keyPath,double xMm,double yMm,double zMm,double rxDeg=0,double ryDeg=0,double rzDeg=0,bool modifySharedSubassembly=false)=>SolidEdgeTools.ExecuteCad("position-nested-part",()=>Bridge.CadPositionNested(expectedDocument,keyPath,xMm,yMm,zMm,rxDeg,ryDeg,rzDeg,modifySharedSubassembly));
    [McpServerTool,Description("Batch set exact-name Variables/Dimensions. variablesJson=[{name,value,unit}], unit=mm/deg/native. Validates unitsType before edits; rolls back values on failure and reports rollback errors. Read list_variables first. Guarded active document; no save.")]
    public static Task<string> solidedge_set_variables(string expectedDocument,string variablesJson)=>SolidEdgeTools.ExecuteCad("set-variables",()=>Bridge.CadSetVariables(expectedDocument,variablesJson));
    [McpServerTool(ReadOnly=true),Description("Read named custom properties from guarded active document; no edit/save.")]
    public static Task<string> solidedge_get_custom_properties(string expectedDocument)=>SolidEdgeTools.ExecuteCad("get-custom-properties",()=>Bridge.CadCustomProperties(expectedDocument),false);
    [McpServerTool,Description("Add or replace one string-valued Custom property (design assumptions/status etc.) in the guarded document. name <=128 chars, value <=4000; no automatic save. Native existing numeric property type may reject a string.")]
    public static Task<string> solidedge_set_custom_property(string expectedDocument,string name,string value)=>SolidEdgeTools.ExecuteCad("set-custom-property",()=>Bridge.CadSetCustomProperty(expectedDocument,name,value));
    [McpServerTool(ReadOnly=true),Description("Aggregate rigid assembly leaf occurrences by file path with quantities/stable keyPaths. Reports truncated/unknown branches; ignores no hidden manufacturing assumptions. No material/mass or suppressed/alternate configuration evaluation; no edit/save.")]
    public static Task<string> solidedge_get_bom(string expectedDocument)=>SolidEdgeTools.ExecuteCad("bom",()=>Bridge.CadBom(expectedDocument),false);
}
