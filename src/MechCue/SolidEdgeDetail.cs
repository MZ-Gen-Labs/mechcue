using System.Text.Json;
using System.Runtime.InteropServices;
namespace MechCue;

public sealed record CadDetailGroup(string Id, string Parent, string[] BodyIds);
public sealed partial class Bridge
{
    public static object CadCloseDocument(string expectedDocument, string returnDocument = "")
    {
        var app = CadApplication(); var document = CadDocument(app, expectedDocument, write: false);
        if (!Path.IsPathFullyQualified(expectedDocument) || !File.Exists(expectedDocument)) throw new InvalidOperationException("Close requires a saved file; save first. No changes discarded.");
        var documents = CadOpenDocuments(app);
        var back = returnDocument == "" ? null : documents.SingleOrDefault(d => string.Equals(CadName(d), returnDocument, StringComparison.OrdinalIgnoreCase))
            ?? (returnDocument == "" ? null : throw new InvalidOperationException("returnDocument must already be open"));
        if (back != null && string.Equals(returnDocument, expectedDocument, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Cannot return to the document being closed");
        EnsureCloseClean(document, new HashSet<string>(StringComparer.OrdinalIgnoreCase), 0);
        Call(document, "Close", false);
        if (back != null) CadActivateDocument(back);
        var remaining = CadOpenDocuments(app).Where(d => string.Equals(CadName(d), expectedDocument, StringComparison.OrdinalIgnoreCase)).ToArray();
        int windows = remaining.Sum(d => Convert.ToInt32(Get(Get(d, "Windows"), "Count")));
        if (windows != 0) throw new InvalidOperationException("Native close left visible windows open; do not assume closure succeeded");
        return new { document = expectedDocument, closed = true, stillLoadedAsReference = remaining.Length > 0, returnDocument,
            note = "Saved CAD document closed. Referenced children may remain loaded by Solid Edge. Save pending MechCue chart edits separately before closing." };
    }
    static void EnsureCloseClean(object document, HashSet<string> seen, int depth)
    {
        if (depth > 32) throw new InvalidOperationException("Cannot verify dirty child documents beyond depth 32; close refused");
        if (!seen.Add(CadName(document))) return;
        if (Convert.ToBoolean(Get(document, "Dirty"))) throw new InvalidOperationException("Save first; close refused for dirty document: " + CadName(document));
        if (CadExtension(document) != ".asm") return;
        var occurrences = Get(document, "Occurrences");
        for (int i = 1; i <= Convert.ToInt32(Get(occurrences, "Count")); i++) EnsureCloseClean(Get(GetItem(occurrences, i), "OccurrenceDocument"), seen, depth + 1);
    }
    internal static double[] InvertRigidMatrix(double[] matrix)
    {
        double[] inverse = [matrix[0],matrix[4],matrix[8],0,matrix[1],matrix[5],matrix[9],0,matrix[2],matrix[6],matrix[10],0,0,0,0,1];
        for (int row = 0; row < 3; row++) inverse[12+row] = -Enumerable.Range(0,3).Sum(k => inverse[k*4+row] * matrix[12+k]);
        return inverse;
    }
    internal static CadDetailGroup[] DetailGroups(ConceptMachine source, string groupsJson)
    {
        source.Validate();
        var groups = groupsJson == "" ? source.Bodies.GroupBy(b => b.Parent).Select(g => new CadDetailGroup(g.Key == "" ? "FixedStructure" : g.Key + "Module", g.Key, g.Select(b => b.Id).ToArray())).ToArray()
            : JsonSerializer.Deserialize<CadDetailGroup[]>(groupsJson, ConceptMachine.JsonOptions) ?? throw new ArgumentException("Invalid groupsJson");
        if (groups.Length is < 1 or > 128 || groups.Select(g => g.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != groups.Length) throw new ArgumentException("Group IDs must be unique, 1..128 groups");
        var bodyIds = groups.SelectMany(g => g.BodyIds ?? throw new ArgumentException("bodyIds required")).ToArray();
        if (bodyIds.Length != source.Bodies.Count || bodyIds.Distinct().Count() != bodyIds.Length || bodyIds.Any(id => !source.Bodies.Any(b => b.Id == id))) throw new ArgumentException("Every source body must belong to exactly one group");
        var probe = JsonSerializer.Deserialize<ConceptMachine>(JsonSerializer.Serialize(source, ConceptMachine.JsonOptions), ConceptMachine.JsonOptions)!;
        probe.Bodies = groups.Select(g => {
            if (g.BodyIds.Length == 0 || g.BodyIds.Any(id => source.Bodies.Single(b => b.Id == id).Parent != g.Parent)) throw new ArgumentException("Group bodies must share exactly the same driving parent axis");
            if (g.Id.Equals("detail", StringComparison.OrdinalIgnoreCase) || source.Bodies.Any(b => string.Equals(Path.GetFileNameWithoutExtension(b.File), g.Id, StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("Group file name conflicts with a part or root assembly");
            return new ConceptBody { Id = g.Id, Parent = g.Parent };
        }).ToList(); probe.Validate();
        var reserved = new[] { "CON", "PRN", "AUX", "NUL" }.Concat(Enumerable.Range(1, 9).SelectMany(n => new[] { "COM" + n, "LPT" + n }));
        foreach (var group in groups) if (reserved.Contains(group.Id, StringComparer.OrdinalIgnoreCase)) throw new ArgumentException("Reserved Windows group file name");
        return groups;
    }
    public static object CadCreateDetailAssembly(string expectedDocument, string manifestPath, string outputDirectory, string groupsJson = "")
    {
        var (source, original, parts) = ConceptLoad(expectedDocument, manifestPath, false);
        var groups = DetailGroups(source, groupsJson);
        if (Convert.ToBoolean(Get(original, "Dirty"))) throw new InvalidOperationException("Save source CAD/MechCue settings before detail generation");
        if (source.Bodies.Any(b => Path.GetExtension(b.File).ToLowerInvariant() != ".par")) throw new InvalidOperationException("First-stage detail grouping accepts flat concept .par bodies only; nested file cloning is not supported");
        var sourcePoses = source.Poses(source.Values); var joints = source.AxisPoses(source.Values);
        foreach (var body in source.Bodies) {
            if (!ConceptSame(Matrix(parts[body.Id]), sourcePoses[body.Id])) throw new InvalidOperationException("Source must be at manifest reference pose: " + body.Id);
            if (Convert.ToBoolean(Get(Get(parts[body.Id], "OccurrenceDocument"), "Dirty"))) throw new InvalidOperationException("Save source component first: " + body.File);
        }
        if (!Path.IsPathFullyQualified(outputDirectory)) throw new ArgumentException("Absolute new outputDirectory required");
        outputDirectory = Path.GetFullPath(outputDirectory);
        if (Directory.Exists(outputDirectory) || File.Exists(outputDirectory) || !Directory.Exists(Path.GetDirectoryName(outputDirectory))) throw new IOException("Use a new output directory with an existing parent");
        var created = new List<string>(); object? working = null;
        var model = JsonSerializer.Deserialize<ConceptMachine>(JsonSerializer.Serialize(source, ConceptMachine.JsonOptions), ConceptMachine.JsonOptions)!;
        model.AssemblyFile = "detail.asm"; model.Bodies.Clear();
        Directory.CreateDirectory(outputDirectory);
        try {
            foreach (var file in source.Bodies.Select(b => b.File).Distinct(StringComparer.OrdinalIgnoreCase)) {
                var path = Path.Combine(outputDirectory, file); File.Copy(Path.Combine(Path.GetDirectoryName(manifestPath)!, file), path, false); created.Add(path);
            }
            foreach (var group in groups) {
                CadNew("assembly"); working = Get(CadApplication(), "ActiveDocument"); var name = CadName(working);
                foreach (var id in group.BodyIds) {
                    var body = source.Bodies.Single(b => b.Id == id); CadPlacePart(name, Path.Combine(outputDirectory, body.File));
                    var occurrence = CadOccurrence(working, Convert.ToInt32(Get(Get(working, "Occurrences"), "Count")));
                    Set(occurrence, "Name", body.Id); var local = ConceptMachine.Multiply(InvertRigidMatrix(joints[group.Parent]), sourcePoses[id]);
                    CadCallRef(occurrence, "PutMatrix", [0], local, true);
                    if (!ConceptSame(Matrix(occurrence), local)) throw new InvalidOperationException("Grouped local pose rejected: " + id);
                }
                string path = Path.Combine(outputDirectory, group.Id + ".asm"); CadSave(name, path); created.Add(path);
                CadCloseDocument(path, expectedDocument); working = null;
                model.Bodies.Add(new ConceptBody { Id = group.Id, Parent = group.Parent, File = group.Id + ".asm", Occurrence = group.Id });
            }
            CadNew("assembly"); working = Get(CadApplication(), "ActiveDocument"); string rootName = CadName(working);
            foreach (var body in model.Bodies) {
                CadPlacePart(rootName, Path.Combine(outputDirectory, body.File)); var occurrence = CadOccurrence(working, Convert.ToInt32(Get(Get(working, "Occurrences"), "Count")));
                Set(occurrence, "Name", body.Id); CadCallRef(occurrence, "PutMatrix", [0], joints[body.Parent], true);
                if (!ConceptSame(Matrix(occurrence), joints[body.Parent])) throw new InvalidOperationException("Grouped world pose rejected: " + body.Id);
            }
            model.Validate(); string rootPath = Path.Combine(outputDirectory, model.AssemblyFile); CadSave(rootName, rootPath); created.Add(rootPath);
            string manifest = Path.Combine(outputDirectory, "mechcue-concept.json"); File.WriteAllText(manifest, JsonSerializer.Serialize(model, ConceptMachine.JsonOptions)); created.Add(manifest);
            var tree = JsonSerializer.SerializeToElement(CadAssemblyTree(rootPath)); var nodes = tree.GetProperty("nodes").Deserialize<CadAssemblyNode[]>()!;
            if (tree.GetProperty("truncated").GetBoolean()) throw new InvalidOperationException("Detail tree verification incomplete");
            foreach (var group in groups) foreach (var id in group.BodyIds) {
                var node = nodes.Single(n => n.Depth == 2 && n.Name == id && Path.GetFileName(n.FileName) == source.Bodies.Single(b => b.Id == id).File);
                if (!ConceptSame(node.WorldMatrix, sourcePoses[id])) throw new InvalidOperationException("World pose not preserved: " + id);
            }
            return new { fullName = rootPath, manifestPath = manifest, groupCount = groups.Length, leafCount = source.Bodies.Count, createdFiles = created, worldPosesPreserved = true, saved = true,
                note = "New independent part copies and rigid subassemblies. Source unchanged. Native occurrence keys are new; use the new tree. MechCue charts are not migrated automatically: migrate saved source settings explicitly." };
        } catch (Exception error) {
            string recovery = "";
            try { Call(original, "Activate"); } catch (Exception activate) { recovery = "; source reactivation failed: " + (activate.InnerException ?? activate).Message; }
            throw new InvalidOperationException("Detail generation failed; partial NEW documents/files retained, no automatic retry. Output: " + outputDirectory + "; completed files: " + string.Join(", ", created) + "; " + (error.InnerException ?? error).Message + recovery, error);
        }
    }
    static object DetailPart(string expectedDocument, bool write = true)
    {
        var doc = CadDocument(CadApplication(), expectedDocument, ".par", write);
        if (Convert.ToInt32(Get(doc, "ModelingMode")) != 2 || Convert.ToInt32(Get(Get(doc, "Models"), "Count")) != 1) throw new InvalidOperationException("Single-model Ordered .par required");
        return doc;
    }
    static void DetailHealthy(object doc)
    {
        var models = Get(doc, "Models");
        for (int m=1;m<=Convert.ToInt32(Get(models,"Count"));m++) {
            var features=Get(GetItem(models,m),"Features");
            for(int i=1;i<=Convert.ToInt32(Get(features,"Count"));i++) if(CadFeatureStatus(GetItem(features,i))!=1216476310) throw new InvalidOperationException("Non-OK feature: "+Convert.ToString(Get(GetItem(features,i),"Name")));
        }
    }
    static double[] DetailLinePoint(object line, string method)
    {
        object[] args=[0d,0d]; CadCallRef(line,method,[0,1],args); return args.Select(Convert.ToDouble).ToArray();
    }
    public static object CadEditExtrusionProfile(string expectedDocument, string featureName, double widthMm = 0, double heightMm = 0, double radiusMm = 0)
    {
        foreach(var value in new[]{widthMm,heightMm,radiusMm}) {CadFinite(value,"dimension");if(value<0)throw new ArgumentException("Use positive dimensions; zero keeps the existing value");}
        if(widthMm==0&&heightMm==0&&radiusMm==0)throw new ArgumentException("At least one dimension must change");
        var doc=DetailPart(expectedDocument);DetailHealthy(doc);var model=GetItem(Get(doc,"Models"),1);var features=Get(model,"Features");
        var feature=Enumerable.Range(1,Convert.ToInt32(Get(features,"Count"))).Select(i=>GetItem(features,i)).Single(f=>string.Equals(Convert.ToString(Get(f,"Name")),featureName,StringComparison.Ordinal));
        var profile=Get(feature,"Profile");var lines=Get(profile,"Lines2d");var circles=Get(profile,"Circles2d");
        if(Convert.ToInt32(Get(Get(profile,"Dimensions"),"Count"))!=0)throw new InvalidOperationException("Profile has driving dimensions; use set_variables instead");
        int lineCount=Convert.ToInt32(Get(lines,"Count")),circleCount=Convert.ToInt32(Get(circles,"Count"));
        var before=Enumerable.Range(1,lineCount).Select(i=>(Line:GetItem(lines,i),Start:DetailLinePoint(GetItem(lines,i),"GetStartPoint"),End:DetailLinePoint(GetItem(lines,i),"GetEndPoint"))).ToArray();
        object? circle=null;double oldRadius=0;
        if(lineCount==4&&circleCount==0) {
            if(radiusMm>0)throw new ArgumentException("Rectangle requires width/height only");
            if(before.Any(p=>Math.Abs(p.Start[0]-p.End[0])>1e-9&&Math.Abs(p.Start[1]-p.End[1])>1e-9))throw new InvalidOperationException("Only axis-aligned rectangular profiles supported");
        } else if(lineCount==0&&circleCount==1) {
            if(radiusMm<=0||widthMm>0||heightMm>0)throw new ArgumentException("Circle requires radiusMm only");
            circle=GetItem(circles,1);oldRadius=Convert.ToDouble(Get(circle,"Radius"));
        } else throw new InvalidOperationException("Only one-loop rectangles/circles supported; arbitrary profiles unchanged");
        var coordinates=before.SelectMany(p=>new[]{p.Start,p.End}).ToArray();
        double cx=lineCount==0?0:(coordinates.Min(p=>p[0])+coordinates.Max(p=>p[0]))/2,cy=lineCount==0?0:(coordinates.Min(p=>p[1])+coordinates.Max(p=>p[1]))/2;
        double oldWidth=lineCount==0?0:coordinates.Max(p=>p[0])-coordinates.Min(p=>p[0]),oldHeight=lineCount==0?0:coordinates.Max(p=>p[1])-coordinates.Min(p=>p[1]);
        if(lineCount>0&&(oldWidth<=1e-9||oldHeight<=1e-9))throw new InvalidOperationException("Degenerate rectangle");
        double sx=widthMm==0?1:widthMm/1000/oldWidth,sy=heightMm==0?1:heightMm/1000/oldHeight;
        try {
            foreach(var p in before){Call(p.Line,"SetStartPoint",cx+(p.Start[0]-cx)*sx,cy+(p.Start[1]-cy)*sy);Call(p.Line,"SetEndPoint",cx+(p.End[0]-cx)*sx,cy+(p.End[1]-cy)*sy);}
            if(circle!=null)Set(circle,"Radius",radiusMm/1000);
            if(Convert.ToInt32(Call(profile,"End",1))!=0)throw new InvalidOperationException("Edited profile not closed");
            Call(doc,"Recompute");DetailHealthy(doc);
            foreach(var p in before) if(DetailLinePoint(p.Line,"GetStartPoint").Zip(new[]{cx+(p.Start[0]-cx)*sx,cy+(p.Start[1]-cy)*sy}).Any(v=>Math.Abs(v.First-v.Second)>1e-8)||DetailLinePoint(p.Line,"GetEndPoint").Zip(new[]{cx+(p.End[0]-cx)*sx,cy+(p.End[1]-cy)*sy}).Any(v=>Math.Abs(v.First-v.Second)>1e-8))throw new InvalidOperationException("Profile constraints rejected requested dimensions");
            if(circle!=null&&Math.Abs(Convert.ToDouble(Get(circle,"Radius"))-radiusMm/1000)>1e-8)throw new InvalidOperationException("Radius not retained");
        } catch(Exception error) {
            try {foreach(var p in before){Call(p.Line,"SetStartPoint",p.Start[0],p.Start[1]);Call(p.Line,"SetEndPoint",p.End[0],p.End[1]);}if(circle!=null)Set(circle,"Radius",oldRadius);Call(profile,"End",1);Call(doc,"Recompute");DetailHealthy(doc);
                foreach(var p in before)if(!DetailLinePoint(p.Line,"GetStartPoint").SequenceEqual(p.Start)||!DetailLinePoint(p.Line,"GetEndPoint").SequenceEqual(p.End))throw new InvalidOperationException("Original profile coordinates not retained");
                if(circle!=null&&Math.Abs(Convert.ToDouble(Get(circle,"Radius"))-oldRadius)>1e-8)throw new InvalidOperationException("Original radius not retained");
            }catch(Exception rollback){throw new InvalidOperationException("Profile edit failed: "+(error.InnerException??error).Message+"; ROLLBACK FAILED: "+(rollback.InnerException??rollback).Message);}
            throw new InvalidOperationException("Profile edit failed; original geometry restored (document may be dirty): "+(error.InnerException??error).Message);
        }
        return new { document=expectedDocument,featureName,widthMm=oldWidth*1000*sx,heightMm=oldHeight*1000*sy,radiusMm=circle==null?0:radiusMm,allFeaturesOk=true,saved=false };
    }
    public static object CadListEdges(string expectedDocument)
    {
        var doc=DetailPart(expectedDocument,false);var edges=DrawingIndexed(Get(GetItem(Get(doc,"Models"),1),"Body"),"Edges",1);
        return new {document=expectedDocument,edges=Enumerable.Range(1,Convert.ToInt32(Get(edges,"Count"))).Select(i=>{var edge=GetItem(edges,i);return new {number=i,edgeId=Convert.ToBase64String(ReferenceKey(edge)),boundsMm=SimulationRange(edge)};}).ToArray(),note="Re-read after geometry edits; native edge IDs may become invalid after topology changes."};
    }
    public static object CadRoundEdges(string expectedDocument,string edgeIdsJson,double radiusMm,string featureName="")
    {
        CadFinite(radiusMm,nameof(radiusMm),true);var ids=JsonSerializer.Deserialize<string[]>(edgeIdsJson)??throw new ArgumentException("edgeIdsJson required");
        if(ids.Length is <1 or >256||ids.Distinct().Count()!=ids.Length)throw new ArgumentException("Select 1..256 distinct edges from list_edges");
        var doc=DetailPart(expectedDocument);DetailHealthy(doc);var model=GetItem(Get(doc,"Models"),1);CadValidateFeatureName(Get(doc,"Models"),featureName);
        var edges=DrawingIndexed(Get(model,"Body"),"Edges",1);var current=Enumerable.Range(1,Convert.ToInt32(Get(edges,"Count"))).Select(i=>GetItem(edges,i)).ToDictionary(e=>Convert.ToBase64String(ReferenceKey(e)));
        if(ids.Any(id=>!current.ContainsKey(id)))throw new InvalidOperationException("Edge identity no longer resolves; re-read list_edges before retrying");
        var set=Array.CreateInstance(typeof(DispatchWrapper),[ids.Length],[1]);var radii=Array.CreateInstance(typeof(double),[ids.Length],[1]);
        for(int i=0;i<ids.Length;i++){set.SetValue(new DispatchWrapper(current[ids[i]]),i+1);radii.SetValue(radiusMm/1000,i+1);}
        var rounds=Get(model,"Rounds");int count=Convert.ToInt32(Get(rounds,"Count"));
        try {var round=Call(rounds,"Add",ids.Length,set,radii,Type.Missing,Type.Missing,Type.Missing,Type.Missing);if(featureName!="")Set(round,"Name",featureName);DetailHealthy(doc);
            return new {document=expectedDocument,featureName=Convert.ToString(Get(round,"Name")),radiusMm,edgeCount=ids.Length,allFeaturesOk=true,saved=false};
        } catch(Exception error) {
            try {while(Convert.ToInt32(Get(rounds,"Count"))>count)Call(GetItem(rounds,Convert.ToInt32(Get(rounds,"Count"))),"Delete");Call(doc,"Recompute");DetailHealthy(doc);}
            catch(Exception rollback){throw new InvalidOperationException("Round failed: "+(error.InnerException??error).Message+"; ROLLBACK FAILED: "+(rollback.InnerException??rollback).Message);}
            throw new InvalidOperationException("Round failed; new rounds removed, document may be dirty: "+(error.InnerException??error).Message);
        }
    }
}
