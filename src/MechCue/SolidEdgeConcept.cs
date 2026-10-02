using System.Text.Json;
namespace MechCue;
public sealed partial class Bridge
{
    public static object CadConceptTemplates() => new {
        templates = new[] {
            new { type="mill3", description="Vertical mill: table X on saddle Y; spindle Z", axes="XYZ", purpose="Layout, travel and motion concept" },
            new { type="mill4", description="Vertical mill with table rotation C about local Z", axes="XYZC", purpose="Indexing and workpiece orientation" },
            new { type="mill5", description="Vertical mill with trunnion A about local X and child table C about local Z", axes="XYZAC", purpose="Table-table five-axis motion concept; no TCP or inverse kinematics" },
            new { type="gantry", description="Cartesian handling: X, child Y, child Z and simplified gripper", axes="XYZ", purpose="Handling layout and travel; gripper has no open/close joint" }
        }, units = new { length="mm", angle="degree" }, scope="Concept geometry and forward kinematics; not vendor replicas, machining simulation or production-ready machine design."
    };
    static double[] ConceptPlaneVector(object plane, string method) {
        object[] args = [new double[3]]; CadCallRef(plane, method, [0], args);
        return ((Array)args[0]).Cast<object>().Select(Convert.ToDouble).ToArray();
    }
    static double[] ConceptGeometry(object document) {
        var plane = CadPlane(document, 1);
        double[] n = ConceptPlaneVector(plane, "GetNormal"), u = ConceptPlaneVector(plane, "GetReferenceDirection"), origin = ConceptPlaneVector(plane, "GetRootPoint");
        double[] v = [n[1]*u[2]-n[2]*u[1], n[2]*u[0]-n[0]*u[2], n[0]*u[1]-n[1]*u[0]];
        // Transpose maps the native plane basis to the concept's XYZ basis.
        return [u[0],v[0],n[0],0, u[1],v[1],n[1],0, u[2],v[2],n[2],0, -(u[0]*origin[0]+u[1]*origin[1]+u[2]*origin[2]), -(v[0]*origin[0]+v[1]*origin[1]+v[2]*origin[2]), -(n[0]*origin[0]+n[1]*origin[1]+n[2]*origin[2]),1];
    }
    public static object CadCreateConcept(string type, string outputDirectory, double xTravelMm=500, double yTravelMm=300, double zTravelMm=300, string partTemplate="", string assemblyTemplate="") {
        var model = ConceptMachine.Create(type, xTravelMm, yTravelMm, zTravelMm);
        if (!Path.IsPathFullyQualified(outputDirectory)) throw new ArgumentException("Use a new absolute output directory.");
        outputDirectory = Path.GetFullPath(outputDirectory);
        if (Directory.Exists(outputDirectory) || File.Exists(outputDirectory)) throw new IOException("Output directory already exists. Use a new directory; existing data is never overwritten.");
        if (!Directory.Exists(Path.GetDirectoryName(outputDirectory))) throw new DirectoryNotFoundException("The parent output directory must exist.");
        foreach (var (template, ext) in new[] { (partTemplate, ".par"), (assemblyTemplate, ".asm") }) if (template != "" && !File.Exists(CadPath(template, ext))) throw new FileNotFoundException("Template not found", template);
        var application = CadApplication(); object? original = null; try { original=Get(application,"ActiveDocument"); } catch { }
        Directory.CreateDirectory(outputDirectory);
        object? workingPart = null;
        try {
            foreach (var body in model.Bodies) {
                CadNew("part",partTemplate); workingPart=Get(application,"ActiveDocument"); string expected=CadName(workingPart);
                if(Convert.ToInt32(Get(Get(workingPart,"Models"),"Count"))!=0) throw new InvalidOperationException("Use an empty part template for concept generation.");
                body.GeometryMatrix=ConceptGeometry(workingPart);
                CadExtrude(expected,body.Shape=="box"?"rectangle":"circle",body.SizeMm[0],body.SizeMm[1],body.SizeMm[0]/2,body.SizeMm[2],xMm:body.Shape=="box"?-body.SizeMm[0]/2:0,yMm:body.Shape=="box"?-body.SizeMm[1]/2:0,direction:"symmetric",featureName:body.Id);
                body.File=body.Id+".par"; CadSave(expected,Path.Combine(outputDirectory,body.File));
                Call(workingPart,"Close",false); workingPart=null;
            }
            CadNew("assembly",assemblyTemplate); var document=Get(application,"ActiveDocument"); string name=CadName(document);
            if(Convert.ToInt32(Get(Get(document,"Occurrences"),"Count"))!=0) throw new InvalidOperationException("Use an empty assembly template for concept generation.");
            var poses=model.Poses(model.Values);
            foreach(var body in model.Bodies) {
                CadPlacePart(name,Path.Combine(outputDirectory,body.File));
                var occurrence=CadOccurrence(document,Convert.ToInt32(Get(Get(document,"Occurrences"),"Count")));
                Set(occurrence,"Name",body.Id); body.Occurrence=Convert.ToString(Get(occurrence,"Name"))!;
                CadCallRef(occurrence,"PutMatrix",[0],poses[body.Id],true);
                if(!ConceptSame(Matrix(occurrence),poses[body.Id])) throw new InvalidOperationException("Concept placement was rejected: "+body.Id);
            }
            CadSave(name,Path.Combine(outputDirectory,model.AssemblyFile));
            string manifest=Path.Combine(outputDirectory,"mechcue-concept.json");
            File.WriteAllText(manifest,JsonSerializer.Serialize(model,ConceptMachine.JsonOptions));
            Call(document,"Activate");
            return new { fullName=CadName(document), manifestPath=manifest, type, axes=JsonSerializer.SerializeToElement(model.Axes,ConceptMachine.JsonOptions), bodies=model.Bodies.Select(b=>new {id=b.Id,parent=b.Parent,occurrence=b.Occurrence,shape=b.Shape,sizeMm=b.SizeMm}), homeValues=model.Values, saved=true, note="Fixed simplified parts; concept-axis operations move all dependent parts together. No MechCue chart assignments are created automatically." };
        } catch(Exception error) {
            if(workingPart!=null) try { Call(workingPart,"Close",false); } catch { }
            if(original!=null) try { Call(original,"Activate"); } catch { }
            throw new InvalidOperationException("Concept creation failed. Partial NEW files/documents were retained in "+outputDirectory+"; existing user documents were not saved. "+error.Message,error);
        }
    }
    static bool ConceptSame(double[] a, double[] b) => a.Length==16 && b.Length==16 && a.Zip(b).All(p=>Math.Abs(p.First-p.Second)<1e-8);
    static (ConceptMachine Model, object Document, Dictionary<string,object> Parts) ConceptLoad(string expectedDocument,string manifestPath,bool write) {
        manifestPath=CadPath(manifestPath,".json");
        if(new FileInfo(manifestPath).Length>1024*1024) throw new ArgumentException("Concept manifest exceeds 1 MB.");
        var model=JsonSerializer.Deserialize<ConceptMachine>(File.ReadAllText(manifestPath),ConceptMachine.JsonOptions) ?? throw new ArgumentException("Invalid concept manifest."); model.Validate();
        string directory=Path.GetDirectoryName(manifestPath)!;
        string Local(string file,params string[] extensions) {
            if(Path.GetFileName(file)!=file || string.IsNullOrEmpty(file)) throw new ArgumentException("Manifest references must be local file names.");
            return CadPath(Path.Combine(directory,file),extensions);
        }
        string assembly=Local(model.AssemblyFile,".asm");
        if(!string.Equals(assembly,expectedDocument,StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Concept belongs to a different assembly.");
        var document=CadDocument(CadApplication(),expectedDocument,".asm",write);
        var occurrences=Get(document,"Occurrences"); var parts=new Dictionary<string,object>();
        foreach(var body in model.Bodies) {
            string file=Local(body.File,".par",".asm");
            var matches=Enumerable.Range(1,Convert.ToInt32(Get(occurrences,"Count"))).Select(i=>GetItem(occurrences,i)).Where(o=>Convert.ToString(Get(o,"Name"))==body.Occurrence && string.Equals(CadName(Get(o,"OccurrenceDocument")),file,StringComparison.OrdinalIgnoreCase)).ToList();
            if(matches.Count!=1) throw new InvalidOperationException("Concept part missing, renamed or ambiguous: "+body.Id);
            if(parts.Values.Any(p=>ReferenceEquals(p,matches[0]))) throw new ArgumentException("Duplicate concept part mapping.");
            parts[body.Id]=matches[0];
        }
        return (model,document,parts);
    }
    public static object CadReadConcept(string expectedDocument,string manifestPath) {
        var (model,document,parts)=ConceptLoad(expectedDocument,manifestPath,false);
        return new { fullName=CadName(document), type=model.Type, axes=JsonSerializer.SerializeToElement(model.Axes,ConceptMachine.JsonOptions), homeValues=model.Values, bodies=model.Bodies.Select(b=>new {id=b.Id,parent=b.Parent,sizeMm=b.SizeMm,shape=b.Shape,occurrence=b.Occurrence,actualMatrix=Matrix(parts[b.Id])}), note="homeValues are the reference pose, not a measurement of current joint values. Matrices use metres, column-major. Pose requests supply ALL axis values." };
    }
    public static object CadPoseConcept(string expectedDocument,string manifestPath,string valuesJson) {
        var (model,document,parts)=ConceptLoad(expectedDocument,manifestPath,true);
        var values=JsonSerializer.Deserialize<Dictionary<string,double>>(valuesJson) ?? throw new ArgumentException("valuesJson must be an object with all axis IDs.");
        var poses=model.Poses(values); // Validate all travel limits before the first CAD mutation.
        foreach(var part in parts.Values) {
            var relations=Get(part,"Relations3d");
            for(int i=1;i<=Convert.ToInt32(Get(relations,"Count"));i++) {
                var relation=GetItem(relations,i);
                if(Convert.ToInt32(Get(relation,"Type"))!=1959028688 && !Convert.ToBoolean(Get(relation,"Suppress"))) throw new InvalidOperationException("Concept pose requires fixed/free parts without active non-ground constraints. Suppress those constraints before using concept-axis control.");
            }
        }
        var before=parts.ToDictionary(p=>p.Key,p=>Matrix(p.Value));
        var applied=new List<string>();
        try {
            foreach(var body in model.Bodies) {
                applied.Add(body.Id); CadCallRef(parts[body.Id],"PutMatrix",[0],poses[body.Id],true);
            }
            foreach(var body in model.Bodies) if(!ConceptSame(Matrix(parts[body.Id]),poses[body.Id])) throw new InvalidOperationException("Solver rejected concept pose: "+body.Id);
        } catch(Exception error) {
            var failed=new List<string>();
            foreach(string id in applied.AsEnumerable().Reverse()) try { CadCallRef(parts[id],"PutMatrix",[0],before[id],true); } catch { failed.Add(id); }
            foreach(var pair in before) try { if(!ConceptSame(Matrix(parts[pair.Key]),pair.Value) && !failed.Contains(pair.Key)) failed.Add(pair.Key); } catch { if(!failed.Contains(pair.Key)) failed.Add(pair.Key); }
            throw new InvalidOperationException(error.Message+(failed.Count==0?" Previous poses restored; document may remain dirty.":" Rollback incomplete; inspect parts: "+string.Join(", ",failed)),error);
        }
        return new { fullName=CadName(document), values, updatedBodies=poses.Count, saved=false, note="Absolute forward-kinematic pose; no collision test, cutting simulation or automatic chart binding. Save the assembly explicitly if needed." };
    }
}
