using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using MechCue;

[McpServerToolType]
public static class DetailTools
{
    [McpServerTool(ReadOnly=true), Description("Read this MCP process's version, executable/base path, access mode and settings path without CAD access. Does not grant or change permissions. Use when separate connections behave differently.")]
    public static string mechcue_get_capabilities() => JsonSerializer.Serialize(new {
        version = typeof(DetailTools).Assembly.GetName().Version?.ToString(),
        releaseVersion = System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(typeof(DetailTools).Assembly)?.InformationalVersion,
        reconnectAfterUpdate = new { automatic=false, recovery="Restart the MCP connection in the AI app after setup; restart the AI app if no reconnect action is available. Read capabilities and session list before resuming. No interrupted edit is automatically retried." }, processId = Environment.ProcessId,
        executablePath = Environment.ProcessPath, baseDirectory = AppContext.BaseDirectory,
        accessMode = McpAccessSettings.Read(McpAccessSettings.LegacyMode(Environment.GetCommandLineArgs())), settingsPath = McpAccessSettings.SettingsPath,
        workflowInstructions = McpWorkflowRules.Instructions
    });
    [McpServerTool, Description("Close only the active expected saved CAD document after completing its work. Refuses dirty documents and dirty referenced descendants; never saves or discards changes. returnDocument optionally reactivates an already-open assembly/document. Save pending MechCue chart edits first. A component may remain loaded as an assembly reference with no window. Keep user-preexisting documents open unless explicitly asked to close them.")]
    public static Task<string> solidedge_close_document(string expectedDocument, string returnDocument = "") => SolidEdgeTools.ExecuteCad("close-document", () => Bridge.CadCloseDocument(expectedDocument, returnDocument));
    [McpServerTool, Description("Copy a saved flat concept assembly into NEW independent .par files and rigid subassemblies grouped by driving axis. Source must match its manifest reference pose and have no dirty source/parts. groupsJson optional [{id,parent,bodyIds:[...]}]; empty auto-groups by axis. Every body once, same axis within each group. Preserves world matrices, axes and baseline; creates detail.asm and mechcue-concept.json. Native reference keys are new. Closes newly generated module windows after saving; leaves detail active. Does not migrate charts: read saved source settings, open MechCue on detail, migrate explicitly. Failure retains partial NEW files; inspect before retry. No overwrites, no nested source cloning.")]
    public static Task<string> solidedge_create_detail_assembly(string expectedDocument, string manifestPath, string outputDirectory, string groupsJson = "") => SolidEdgeTools.ExecuteCad("create-detail", () => Bridge.CadCreateDetailAssembly(expectedDocument, manifestPath, outputDirectory, groupsJson));
    [McpServerTool, Description("Resize an existing single-loop axis-aligned rectangle or circle extrusion/cut profile by exact featureName in a single-model Ordered .par. Preserves profile centre, extrusion depth and plane. widthMm/heightMm for rectangles, radiusMm for circles; zero keeps unchanged. Rejects dimension-driven sketches (use set_variables), checks all downstream features and requested coordinates; attempts rollback on failure. Does not save. Re-read edge/face IDs after editing.")]
    public static Task<string> solidedge_edit_extrusion_profile(string expectedDocument, string featureName, double widthMm=0, double heightMm=0, double radiusMm=0) => SolidEdgeTools.ExecuteCad("edit-profile", () => Bridge.CadEditExtrusionProfile(expectedDocument,featureName,widthMm,heightMm,radiusMm));
    [McpServerTool(ReadOnly=true), Description("Read current single Ordered .par body's edge reference keys (edgeId), bounds in mm and current edge numbers. Does not edit or save. Re-read after topology changes; numbers are not persistent identity.")]
    public static Task<string> solidedge_list_edges(string expectedDocument) => SolidEdgeTools.ExecuteCad("list-edges", () => Bridge.CadListEdges(expectedDocument),false);
    [McpServerTool, Description("Add a native constant-radius round to explicitly selected current edgeIdsJson (JSON array of 1..256 edgeId strings from list_edges). Single Ordered .par; radiusMm positive. Validates all keys before mutation, checks downstream feature health and attempts removal of only newly added rounds on failure. Does not save. Re-read edge/face IDs after editing.")]
    public static Task<string> solidedge_round_edges(string expectedDocument,string edgeIdsJson,double radiusMm,string featureName="") => SolidEdgeTools.ExecuteCad("round-edges", () => Bridge.CadRoundEdges(expectedDocument,edgeIdsJson,radiusMm,featureName));
}

public static class McpWorkflowRules
{
    public const string Instructions = "For Solid Edge work, read the active document and keep expectedDocument guards. Record the user's original document and pre-existing open windows. After creating or opening a temporary part/subassembly for assembly work, finish editing, verify feature health, save it, then close its window with solidedge_close_document and returnDocument set to the working assembly. Keep only documents still needed for ongoing work; do not accumulate finished temporary windows. Do not close user-preexisting documents without authorization. Never discard unsaved edits, auto-save unrelated documents, or quit Solid Edge as cleanup. Save pending MechCue chart/settings edits using mechcue_save_document before closing their assembly. Closing a component window does not promise unloading referenced geometry or a performance improvement. Generated detail modules are automatically saved and closed by create_detail_assembly. On partial CAD failure inspect state before retrying; do not replay mutations blindly.";
}
