using System.ComponentModel;
using ModelContextProtocol.Server;
using MechCue;
[McpServerToolType]
public static class SolidEdgeConceptTools
{
    [McpServerTool(ReadOnly=true),Description("List concept-machine templates without connecting to CAD: mill3 (XYZ), mill4 (XYZ+C), mill5 (XYZ+A+C table-table), gantry (XYZ). Includes scope and units.")]
    public static object solidedge_list_concept_templates() => Bridge.CadConceptTemplates();
    [McpServerTool,Description("Generate a NEW simplified Ordered-parts assembly and mechcue-concept.json in a new absolute outputDirectory whose parent exists. type=mill3/mill4/mill5/gantry, travels 10..5000 mm. Parts are fixed; forward kinematics are handled by concept tools. Saves only the newly generated files, leaves the new assembly active. Templates optional absolute .par/.asm paths. Failures may leave partial NEW files; no overwrite. Not a vendor replica or production-ready design. Does not automatically create or bind MechCue chart tracks. Requires tray Creation/edit permission.")]
    public static Task<string> solidedge_create_concept_machine(string type,string outputDirectory,double xTravelMm=500,double yTravelMm=300,double zTravelMm=300,string partTemplate="",string assemblyTemplate="") => SolidEdgeTools.ExecuteCad("create-concept",()=>Bridge.CadCreateConcept(type,outputDirectory,xTravelMm,yTravelMm,zTravelMm,partTemplate,assemblyTemplate));
    [McpServerTool(ReadOnly=true),Description("Read a concept manifest and validate its mapping to the active expectedDocument assembly; returns axis definitions/travel limits and actual part matrices. Home values are reference values, not measured joint values. Requires tray Read/select or Creation/edit permission.")]
    public static Task<string> solidedge_get_concept_machine(string expectedDocument,string manifestPath) => SolidEdgeTools.ExecuteCad("get-concept",()=>Bridge.CadReadConcept(expectedDocument,manifestPath),false);
    [McpServerTool,Description("Move ALL dependent concept parts using absolute forward kinematics. Supply valuesJson with every axis ID, e.g. {\"X\":100,\"Y\":0,\"Z\":50,\"A\":30,\"C\":90}; linear mm, rotary degrees. Rotation follows the parent-local axis, around its defined origin. Validate all limits/mappings first; on solver failure attempts pose rollback and reports incomplete restoration. No implicit save, collision test, TCP or inverse kinematics. Requires tray Creation/edit permission.")]
    public static Task<string> solidedge_set_concept_pose(string expectedDocument,string manifestPath,string valuesJson) => SolidEdgeTools.ExecuteCad("pose-concept",()=>Bridge.CadPoseConcept(expectedDocument,manifestPath,valuesJson));
}
