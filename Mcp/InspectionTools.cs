using System.ComponentModel;
using MechCue;
using ModelContextProtocol.Server;

[McpServerToolType]
public static class SolidEdgeInspectionTools
{
    [McpServerTool(ReadOnly = true), Description("Recursively read an assembly's occurrences, parent-relative and composed world matrices (metres), numeric paths and native reference-key paths. Bounded traversal reports truncation and unreadable nodes. Rigid subassemblies only; flexible overrides are not evaluated. Does not activate, save or move parts. Requires Read/select or Creation/edit mode.")]
    public static Task<string> solidedge_get_assembly_tree(string expectedDocument, int maxDepth = 16, int maxOccurrences = 10000)
        => SolidEdgeTools.ExecuteCad("assembly-tree", () => Bridge.CadAssemblyTree(expectedDocument, maxDepth, maxOccurrences), false);
    [McpServerTool(ReadOnly = true), Description("Read the active 3D window's camera: eye/target in metres, up vector, perspective and native scale/angle. Requires Read/select or Creation/edit mode.")]
    public static Task<string> solidedge_get_view(string expectedDocument)
        => SolidEdgeTools.ExecuteCad("get-view", () => Bridge.CadGetView(expectedDocument), false);
    [McpServerTool, Description("Orient, fit, zoom and refresh the active 3D window. orientation=current/front/back/top/bottom/right/left/isometric, fit=true, zoomFactor=0.1..10. Front looks from -Y, top from +Z, right from +X. Named orientations use orthographic projection. Geometry and motion are unchanged; document is not saved. Requires Creation/edit mode.")]
    public static Task<string> solidedge_set_view(string expectedDocument, string orientation = "current", bool fit = true, double zoomFactor = 1)
        => SolidEdgeTools.ExecuteCad("set-view", () => Bridge.CadSetView(expectedDocument, orientation, fit, zoomFactor));
    [McpServerTool, Description("Export the current 3D view as a new JPEG, 64..4096 pixels per dimension. Absolute outputPath in an existing folder; existing files rejected. Does not fit, change camera, save CAD or move parts. Requires Creation/edit mode.")]
    public static Task<string> solidedge_export_view_image(string expectedDocument, string outputPath, int width = 1280, int height = 960)
        => SolidEdgeTools.ExecuteCad("export-view-image", () => Bridge.CadExportView(expectedDocument, outputPath, width, height));
    [McpServerTool(ReadOnly = true), Description("Native static interference analysis. set1Json/set2Json are JSON arrays of distinct 1-based top-level occurrence numbers. Empty set1 checks all occurrences against themselves. Supply both disjoint sets to check ONLY between the two sets, excluding internal subassembly interference. Returns native status, count, available pairs and completeness; incomplete/unknown is not clear. No geometry, report file or pose changes. Requires Read/select or Creation/edit mode. Does not verify swept paths.")]
    public static Task<string> solidedge_check_interference(string expectedDocument, string set1Json = "[]", string set2Json = "[]", bool ignoreThreadInterferences = false)
        => SolidEdgeTools.ExecuteCad("check-interference", () => Bridge.CadCheckInterference(expectedDocument, set1Json, set2Json, ignoreThreadInterferences), false);
    [McpServerTool, Description("Run the same native static interference analysis and write a new native text report containing occurrence names, centres and interference volumes when available. Useful when nested Reference pair arrays are unavailable. reportPath must be an absolute new .txt file in an existing folder. Does not create interference geometry, move parts or save CAD. Sets and interpretation match solidedge_check_interference. Requires Creation/edit mode.")]
    public static Task<string> solidedge_export_interference_report(string expectedDocument, string reportPath, string set1Json = "[]", string set2Json = "[]", bool ignoreThreadInterferences = false)
        => SolidEdgeTools.ExecuteCad("export-interference-report", () => Bridge.CadCheckInterference(expectedDocument, set1Json, set2Json, ignoreThreadInterferences, reportPath));
}
