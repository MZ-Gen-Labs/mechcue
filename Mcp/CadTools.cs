using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using MechCue;

[McpServerToolType]
public static class SolidEdgeCadTools
{
    static Task<string> Execute(string operation, Func<object> action) => SolidEdgeTools.ExecuteCad(operation, action);
    [McpServerTool, Description("Create and activate a new part, assembly or draft. Parts use Ordered mode. Optional absolute templatePath must match the document type. Does not save or close existing documents. Requires --allow-solidedge-write.")]
    public static Task<string> solidedge_new_document(string kind, string templatePath = "") => Execute("new-document", () => Bridge.CadNew(kind, templatePath));
    [McpServerTool, Description("Open and activate an existing .par/.asm/.dft/.psm using an absolute path. Does not close or save existing documents. Requires --allow-solidedge-write.")]
    public static Task<string> solidedge_open_document(string filePath) => Execute("open-document", () => Bridge.CadOpen(filePath));
    [McpServerTool, Description("Explicitly save the active document matched by expectedDocument. For new files supply an absolute outputPath in an existing folder; never overwrites another file. Empty outputPath saves the current existing file. Requires --allow-solidedge-write.")]
    public static Task<string> solidedge_save_document(string expectedDocument, string outputPath = "") => Execute("save-document", () => Bridge.CadSave(expectedDocument, outputPath));
    [McpServerTool(ReadOnly = true), Description("List reference planes with 1-based numbers and native names. For an assembly supply partNumber to read a component without activating its document. Plane-local XY coordinates depend on the selected plane; read before creating a profile. Requires --allow-solidedge.")]
    public static Task<string> solidedge_list_planes(string expectedDocument, int partNumber = 0) => SolidEdgeTools.ExecuteCad("list-planes", () => Bridge.CadListPlanes(expectedDocument, partNumber), false);
    [McpServerTool(ReadOnly = true), Description("List a part's feature tree and modeling mode (Ordered=2). Requires --allow-solidedge.")]
    public static Task<string> solidedge_list_features(string expectedDocument) => SolidEdgeTools.ExecuteCad("list-features", () => Bridge.CadListFeatures(expectedDocument), false);
    [McpServerTool, Description("Create an Ordered extrusion or finite cut. shape=rectangle/circle/polygon; operation=add/cut; direction=positive/negative/symmetric relative to plane normal. All lengths mm, profile origin is plane-local XY. Polygon pointsJson is [{\"x\":0,\"y\":0},...], 3-256 distinct vertices without repeated end; ignores origin. First additive feature creates the solid, later adds must touch it. Cut requires an existing solid. Single-body parts only. Geometry remains unsaved; no automatic undo. Failed COM operations may leave partial geometry; inspect the feature list. Requires --allow-solidedge-write.")]
    public static Task<string> solidedge_extrude_profile(string expectedDocument, string shape, double depthMm, double widthMm = 0, double heightMm = 0, double radiusMm = 0, int planeNumber = 1, double xMm = 0, double yMm = 0, string operation = "add", string direction = "positive", string pointsJson = "", string featureName = "")
        => Execute("extrude", () => Bridge.CadExtrude(expectedDocument, shape, widthMm, heightMm, radiusMm, depthMm, planeNumber, xMm, yMm, operation, direction, pointsJson, featureName));
    [McpServerTool, Description("Add a saved part/subassembly to the active assembly. Absolute XYZ in mm, rotations in degrees applied Rx then Ry then Rz. ground=true adds a fixed relationship; use false for parts to constrain. No automatic save. Requires --allow-solidedge-write.")]
    public static Task<string> solidedge_place_part(string expectedDocument, string filePath, double xMm = 0, double yMm = 0, double zMm = 0, double rxDeg = 0, double ryDeg = 0, double rzDeg = 0, bool ground = true)
        => Execute("place-part", () => Bridge.CadPlacePart(expectedDocument, filePath, xMm, yMm, zMm, rxDeg, ryDeg, rzDeg, ground));
    [McpServerTool, Description("Set a top-level occurrence's absolute XYZ mm and Rx/Ry/Rz degree pose. Existing assembly constraints may prevent the pose; the result is checked, but this is not an atomic rollback. No automatic save. Requires --allow-solidedge-write.")]
    public static Task<string> solidedge_position_part(string expectedDocument, int partNumber, double xMm, double yMm, double zMm, double rxDeg = 0, double ryDeg = 0, double rzDeg = 0)
        => Execute("position-part", () => Bridge.CadPositionPart(expectedDocument, partNumber, xMm, yMm, zMm, rxDeg, ryDeg, rzDeg));
    [McpServerTool, Description("Constrain two different top-level occurrences using their reference planes. Read each component's planes first. normalsAligned=false mates opposite normals; true aligns them. offsetMm is a native signed plane offset. Ground only the base part; overconstraints can fail. No automatic save or rollback. Requires --allow-solidedge-write.")]
    public static Task<string> solidedge_mate_planes(string expectedDocument, int partA, int planeA, int partB, int planeB, bool normalsAligned = false, double offsetMm = 0)
        => Execute("mate-planes", () => Bridge.CadMatePlanes(expectedDocument, partA, planeA, partB, planeB, normalsAligned, offsetMm));
    [McpServerTool, Description("Add a drawing view on the active draft sheet from an existing saved .par/.psm/.asm. orientation=front/top/right/left/back/bottom/isometric; scale=1 means 1:1. Sheet positions xMm/yMm use mm. Reuses model links. Does not add manufacturing dimensions, tolerances or save the draft. Requires --allow-solidedge-write.")]
    public static Task<string> solidedge_add_drawing_view(string expectedDocument, string modelPath, string orientation, double scale, double xMm, double yMm)
        => Execute("drawing-view", () => Bridge.CadAddDrawingView(expectedDocument, modelPath, orientation, scale, xMm, yMm));
    [McpServerTool, Description("Update all drawing views in the active draft from their linked models. Does not save. Requires --allow-solidedge-write.")]
    public static Task<string> solidedge_update_drawing(string expectedDocument) => Execute("update-drawing", () => Bridge.CadUpdateDrawing(expectedDocument));
    [McpServerTool, Description("Export the active draft as PDF to a new absolute outputPath; existing files are rejected. Output folder must exist. Requires --allow-solidedge-write.")]
    public static Task<string> solidedge_export_pdf(string expectedDocument, string outputPath) => Execute("export-pdf", () => Bridge.CadExportPdf(expectedDocument, outputPath));
}
