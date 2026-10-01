using System.Text.Json;
using System.Runtime.InteropServices;
namespace MechCue;

public sealed partial class Bridge
{
    static object SimulationDocument(string expected, bool write = true)
    {
        var doc = CadDocument(CadApplication(), expected, ".par", write);
        var models = Get(doc, "Models");
        if (Convert.ToInt32(Get(models, "Count")) != 1 || !Convert.ToBoolean(Get(Get(GetItem(models, 1), "Body"), "IsSolid")))
            throw new InvalidOperationException("Simulation requires a single solid body in a .par part.");
        return doc;
    }
    static object SimulationOwner(object item, string method)
    {
        object[] args = [new DispatchWrapper(null)]; CadCallRef(item, method, [0], args);
        return args[0] ?? throw new InvalidOperationException("Solid Edge did not return " + method + ". Check Simulation installation and license.");
    }
    static object SimulationStudy(object doc, int number)
    {
        var owner = Get(doc, "StudyOwner");
        if (number < 1 || number > Convert.ToInt32(Get(owner, "Count"))) throw new ArgumentOutOfRangeException(nameof(number), "Read solidedge_list_simulation_studies first.");
        var study = GetItem(owner, number);
        if (SimulationInt(study, "GetStudyType") != 1 || SimulationInt(study, "GetMeshType") != 1)
            throw new InvalidOperationException("Only linear static, tetrahedral solid studies are supported in this phase.");
        return study;
    }
    static int SimulationInt(object item, string method) { object[] a = [0]; CadCallRef(item, method, [0], a); return Convert.ToInt32(a[0]); }
    static double SimulationDouble(object item, string method) { object[] a = [0d]; CadCallRef(item, method, [0], a); return Convert.ToDouble(a[0]); }
    static string SimulationName(object study) { object[] a = [""]; CadCallRef(study, "GetStudyName", [0], a); return Convert.ToString(a[0]) ?? ""; }
    static bool SimulationStatus(object study, int status) { object[] a = [status, false]; CadCallRef(study, "GetStudyStatus", [1], a); return Convert.ToBoolean(a[1]); }
    static string SimulationError(Exception error) => (error.InnerException ?? error).Message;
    static object SimulationFaces(object doc) => DrawingIndexed(Get(GetItem(Get(doc, "Models"), 1), "Body"), "Faces", 1);
    static string SimulationFaceId(object face) => Convert.ToBase64String(ReferenceKey(face));
    static double[][] SimulationRange(object face)
    {
        object[] a = [new double[3], new double[3]]; CadCallRef(face, "GetRange", [0,1], a);
        return a.Select(v => ((Array)v).Cast<object>().Select(x => Convert.ToDouble(x)*1000).ToArray()).ToArray();
    }
    static string[] SimulationParseFaces(string faceIdsJson)
    {
        string[] ids;
        try { ids = JsonSerializer.Deserialize<string[]>(faceIdsJson) ?? throw new ArgumentException("faceIdsJson must be a JSON array of face IDs."); }
        catch (JsonException) { throw new ArgumentException("faceIdsJson must be a JSON array of face IDs."); }
        if (ids.Length > 100 || ids.Any(s => string.IsNullOrWhiteSpace(s)) || ids.Distinct().Count() != ids.Length)
            throw new ArgumentException("faceIdsJson must contain at most 100 distinct nonempty face IDs.");
        foreach (string id in ids) { try { if(Convert.FromBase64String(id).Length == 0) throw new FormatException(); } catch(FormatException) { throw new ArgumentException("Invalid face ID; read solidedge_list_simulation_faces."); } }
        return ids;
    }
    static object[] SimulationResolveFaces(object doc, string[] ids, bool allowSelection = true)
    {
        var faces = SimulationFaces(doc); var current = Enumerable.Range(1, Convert.ToInt32(Get(faces, "Count"))).Select(i => GetItem(faces, i)).ToDictionary(SimulationFaceId);
        if (ids.Length == 0 && allowSelection) {
            var selection = Get(doc, "SelectSet"); var selected = new List<string>();
            for (int i=1;i<=Convert.ToInt32(Get(selection,"Count"));i++) {
                string id;
                try { id=SimulationFaceId(GetItem(selection,i)); } catch { throw new ArgumentException("Select only faces belonging to this part's solid body."); }
                if (!current.ContainsKey(id)) throw new ArgumentException("Selected object is not a current face of this part.");
                selected.Add(id);
            }
            ids=selected.Distinct().ToArray();
        }
        if(ids.Length==0 || ids.Length>100) throw new ArgumentException("Specify faceIdsJson from the face list, or select 1..100 faces in Solid Edge.");
        if(ids.Any(id=>!current.ContainsKey(id))) throw new ArgumentException("A face ID is no longer present. Read the face list again after geometry changes.");
        return ids.Select(id=>current[id]).ToArray();
    }
    public static object CadListSimulationFaces(string expectedDocument)
    {
        var doc=SimulationDocument(expectedDocument,false); var faces=SimulationFaces(doc); var result=new List<object>();
        for(int i=1;i<=Convert.ToInt32(Get(faces,"Count"));i++) {
            var face=GetItem(faces,i); var range=SimulationRange(face); double[]? normal=null;
            try { normal=PmiVector(Get(face,"Geometry"),"GetNormalVector"); } catch { }
            result.Add(new {number=i,faceId=SimulationFaceId(face),areaMm2=Convert.ToDouble(Get(face,"Area"))*1e6,minMm=range[0],maxMm=range[1],planeNormal=normal});
        }
        return new {fullName=CadName(doc),faces=result,note="Use faceId, not list position. Re-read after geometry edits. Plane normals describe the analytic surface, not necessarily outward orientation."};
    }
    public static object CadSelectSimulationFaces(string expectedDocument,string faceIdsJson)
    {
        var ids=SimulationParseFaces(faceIdsJson); var doc=SimulationDocument(expectedDocument,false); var faces=SimulationResolveFaces(doc,ids,false);
        var selection=Get(doc,"SelectSet"); Call(selection,"RemoveAll"); foreach(var face in faces) Call(selection,"Add",face);
        return new {fullName=CadName(doc),selectedFaceIds=faces.Select(SimulationFaceId).ToArray()};
    }
    static object SimulationMaterial(object doc)
    {
        var table=Call(CadApplication(),"GetMaterialTable"); object[] name=[doc,""]; CadCallRef(table,"GetCurrentMaterialName",[1],name);
        double? Property(int index) { try {object[] a=[doc,index,null!];CadCallRef(table,"GetMaterialPropValueFromDoc",[2],a);return Convert.ToDouble(a[2]);}catch{return null;} }
        return new {name=Convert.ToString(name[1]),elasticModulusPa=Property(27),poissonRatio=Property(28),densityKgM3=Property(23),yieldStressPa=Property(29)};
    }
    public static object CadListSimulationMaterials(string expectedDocument,string libraryName="")
    {
        var doc=SimulationDocument(expectedDocument,false); var table=Call(CadApplication(),"GetMaterialTable");
        object[] libraries=[null!,0]; CadCallRef(table,"GetMaterialLibraryList",[0,1],libraries);
        if(libraryName=="")libraryName=((Array)libraries[0]).Cast<object>().Select(x=>Convert.ToString(x)??"").FirstOrDefault()??throw new InvalidOperationException("No material libraries are available.");
        object[] materials=[libraryName,0,null!];
        CadCallRef(table,"GetMaterialListFromLibrary",[1,2],materials);
        static string[] Strings(object? value)=>value is Array array?array.Cast<object>().Select(x=>Convert.ToString(x)??"").ToArray():[];
        return new {fullName=CadName(doc),current=SimulationMaterial(doc),libraries=Strings(libraries[0]),libraryName,materials=Strings(materials[^1])};
    }
    public static object CadApplySimulationMaterial(string expectedDocument,string materialName,string libraryName="")
    {
        if(string.IsNullOrWhiteSpace(materialName))throw new ArgumentException("materialName is required; use the exact name from the material list.");
        var doc=SimulationDocument(expectedDocument); var table=Call(CadApplication(),"GetMaterialTable");
        if(libraryName=="") {object[] libraries=[null!,0];CadCallRef(table,"GetMaterialLibraryList",[0,1],libraries);libraryName=((Array)libraries[0]).Cast<object>().Select(x=>Convert.ToString(x)??"").FirstOrDefault()??throw new InvalidOperationException("No material libraries are available.");}
        Call(table,"ApplyMaterialToDoc",doc,materialName,libraryName);
        return new {fullName=CadName(doc),material=SimulationMaterial(doc),saved=false};
    }
    static object SimulationStudyInfo(object doc,object study,int number,List<string> warnings)
    {
        object? Read(string name,Func<object> getter) {try{return getter();}catch(Exception ex){warnings.Add(name+": "+SimulationError(ex));return null;}}
        bool nativeSolved=SimulationStatus(study,10);
        bool? current=nativeSolved?(bool?)Read("result verification",()=>SimulationResultsCurrent(doc,study)):false;
        return new {number,name=SimulationName(study),nativeSolved,resultsCurrent=current,studyType=SimulationInt(study,"GetStudyType"),meshType=SimulationInt(study,"GetMeshType"),
            meshLevel=Read("mesh level",()=>SimulationDouble(SimulationOwner(study,"GetMeshOwner"),"GetMeshSize")),
            meshSizeMm=Read("mesh size",()=>SimulationDouble(SimulationOwner(study,"GetMeshOwner"),"GetMeshSizeValue")*1000),
            loads=Read("loads",()=>SimulationConditions(study,false)),constraints=Read("constraints",()=>SimulationConditions(study,true)),
            readyForMesh=Read("readyForMesh",()=>SimulationStatus(study,4)),readyForSolve=Read("readyForSolve",()=>SimulationStatus(study,5)),
            meshed=Read("meshed",()=>SimulationStatus(study,9)),solved=nativeSolved&&current==true,
            geometryError=Read("geometryError",()=>SimulationStatus(study,1)),meshError=Read("meshError",()=>SimulationStatus(study,2)),resultsError=Read("resultsError",()=>SimulationStatus(study,3))};
    }
    static object[] SimulationConditions(object study,bool fixedConditions)
    {
        var owner=SimulationOwner(study,fixedConditions?"GetConstraintOwner":"GetLoadOwner");var result=new List<object>();
        for(int i=1;i<=Convert.ToInt32(Get(owner,"Count"));i++) {
            var condition=GetItem(owner,i);string[] faceIds=[];string? geometryWarning=null;
            try {
                Array geometry=new object[1];
                if(fixedConditions)((ISimulationConstraintGeometry)condition).GetGeometries(ref geometry);else ((ISimulationLoadGeometry)condition).GetGeometries(ref geometry);
                faceIds=(geometry??Array.Empty<object>()).Cast<object?>().Where(x=>x!=null).Select(x=>SimulationFaceId(x!)).ToArray();
            } catch(Exception ex) {geometryWarning="Solid Edge could not expose condition face IDs: "+SimulationError(ex);}

            if(fixedConditions)result.Add(new {number=i,name=Get(condition,"Name"),type=SimulationInt(condition,"GetConstraintType"),suppressed=Get(condition,"Suppress"),geometryWarning,faceIds});
            else {object[] direction=[0d,0d,0d],flip=[false];CadCallRef(condition,"GetLoadDirection",[0,1,2],direction);CadCallRef(condition,"GetFlip",[0],flip); result.Add(new {flipped=Convert.ToBoolean(flip[0]),number=i,name=Get(condition,"Name"),type=SimulationInt(condition,"GetLoadType"),suppressed=Get(condition,"Suppress"),valueNative=SimulationDouble(condition,"GetLoadValue"),geometryWarning,direction=direction.Select(Convert.ToDouble).ToArray(),faceIds});}
        }
        return result.ToArray();
    }
    public static object CadListSimulationStudies(string expectedDocument)
    {
        var doc=SimulationDocument(expectedDocument,false);var warnings=new List<string>();var owner=Get(doc,"StudyOwner");var studies=new List<object>();
        for(int i=1;i<=Convert.ToInt32(Get(owner,"Count"));i++)studies.Add(SimulationStudyInfo(doc,GetItem(owner,i),i,warnings));
        return new {fullName=CadName(doc),material=SimulationMaterial(doc),studies,warnings};
    }
    static void SimulationMeshSize(double mm) { if(!double.IsFinite(mm)||mm<.1||mm>1000)throw new ArgumentOutOfRangeException(nameof(mm),"meshSizeMm must be 0.1..1000 mm."); }
    static void SimulationMeshLevel(int level) {if(level<1||level>10)throw new ArgumentOutOfRangeException(nameof(level),"meshLevel must be 1..10 (1 coarse, 10 fine, default 5).");}
    static void SimulationMeshOptions(double mm,int level,bool create)
    {
        if(mm!=0)SimulationMeshSize(mm);
        if(create||level!=0)SimulationMeshLevel(level);
        if(!create&&mm>0&&level>0)throw new ArgumentException("Specify meshLevel or meshSizeMm, not both.");
    }
    public static object CadCreateSimulationStudy(string expectedDocument,double meshSizeMm=0,int meshLevel=5)
    {
        SimulationMeshOptions(meshSizeMm,meshLevel,true);var doc=SimulationDocument(expectedDocument);SimulationRequireModelEnvironment();var owner=Get(doc,"StudyOwner");
        object[] a=[1,1,0d,0u,0u,0d,0d,"","",1052673u,new DispatchWrapper(null)];CadCallRef(owner,"AddStudy",[10],a);var study=a[10];
        var body=Get(GetItem(Get(doc,"Models"),1),"Body");Array geometries=new object[]{body};((ISimulationStudyGeometry)study).SetGeometries(ref geometries);
        var mesh=SimulationOwner(study,"GetMeshOwner");
        if(meshSizeMm>0)Call(mesh,"SetMeshSizeValue",meshSizeMm/1000);else Call(mesh,"SetMeshSize",(double)meshLevel);
        var warnings=new List<string>();return new {fullName=CadName(doc),study=SimulationStudyInfo(doc,study,Convert.ToInt32(Get(owner,"Count")),warnings),warnings,saved=false};
    }
    public static object CadAddSimulationFixed(string expectedDocument,int studyNumber,string faceIdsJson="[]",string name="")
    {
        var ids=SimulationParseFaces(faceIdsJson);var doc=SimulationDocument(expectedDocument);var faces=SimulationResolveFaces(doc,ids);var study=SimulationStudy(doc,studyNumber);
        var range=SimulationRange(faces[0]);var center=range[0].Zip(range[1]).Select(p=>(p.First+p.Second)/2000).ToArray();
        Array geometry=faces;
        ((ISimulationConstraintOwner)SimulationOwner(study,"GetConstraintOwner")).AddConstraint(ref geometry,1,6,1,0,0,center[0],center[1],center[2],0xCC6600u,.1,.2,0,0,.01,.02,out var constraint,0,null);
        if(name!="")Set(constraint,"Name",name);
        return new {fullName=CadName(doc),studyNumber,name=Get(constraint,"Name"),faceIds=faces.Select(SimulationFaceId).ToArray(),saved=false};
    }
    public static object CadAddSimulationLoad(string expectedDocument,int studyNumber,string kind,double value,double directionX=0,double directionY=0,double directionZ=-1,string faceIdsJson="[]",string name="")
    {
        if(kind is not ("force" or "pressure"))throw new ArgumentException("kind must be force or pressure.");
        if(!double.IsFinite(value)||value<=0||value>1e9)throw new ArgumentOutOfRangeException(nameof(value),"Use a positive finite load: force in N, pressure in MPa.");
        var direction=new[]{directionX,directionY,directionZ};if(direction.Any(x=>!double.IsFinite(x)||Math.Abs(x)>1e6))throw new ArgumentException("Use a finite direction vector.");
        double length=Math.Sqrt(direction.Sum(x=>x*x));if(kind=="force"&&length<1e-12)throw new ArgumentException("Force direction must be nonzero.");
        var ids=SimulationParseFaces(faceIdsJson);var doc=SimulationDocument(expectedDocument);var faces=SimulationResolveFaces(doc,ids);var study=SimulationStudy(doc,studyNumber);
        if(kind=="force")direction=direction.Select(x=>x/length).ToArray();var range=SimulationRange(faces[0]);var center=range[0].Zip(range[1]).Select(p=>(p.First+p.Second)/2000).ToArray();
        Array geometry=faces;
        ((ISimulationLoadOwner)SimulationOwner(study,"GetLoadOwner")).AddLoad(ref geometry,kind=="force"?1:2,kind=="force"?value:value*1e6,0,kind=="force"?1:2,direction[0],direction[1],direction[2],center[0],center[1],center[2],0x0066CCu,0,.1,.2,0,0,.01,.02,out var load);
        if(name!="")Set(load,"Name",name);
        return new {fullName=CadName(doc),studyNumber,name=Get(load,"Name"),kind,value,unit=kind=="force"?"N":"MPa",faceIds=faces.Select(SimulationFaceId).ToArray(),saved=false};
    }
    public static object CadRunSimulation(string expectedDocument,int studyNumber,bool meshOnly=false,double meshSizeMm=0,bool suppressAlerts=false,bool regenerateMesh=false,int meshLevel=0)
    {
        SimulationMeshOptions(meshSizeMm,meshLevel,false);
        return CadWithSuppressedAlerts(suppressAlerts,()=>SimulationRunCore(expectedDocument,studyNumber,meshOnly,meshSizeMm,regenerateMesh,meshLevel));
    }
    static object SimulationRunCore(string expectedDocument,int studyNumber,bool meshOnly,double meshSizeMm,bool regenerateMesh,int meshLevel)
    {
        if(meshSizeMm!=0)SimulationMeshSize(meshSizeMm);var doc=SimulationDocument(expectedDocument);var study=SimulationStudy(doc,studyNumber);
        if(!meshOnly) {
            var material=JsonSerializer.SerializeToElement(SimulationMaterial(doc));
            if(material.GetProperty("elasticModulusPa").ValueKind!=JsonValueKind.Number||material.GetProperty("elasticModulusPa").GetDouble()<=0)throw new InvalidOperationException("Assign a material with a positive elastic modulus before solving.");
            var constraints=SimulationOwner(study,"GetConstraintOwner");var loads=SimulationOwner(study,"GetLoadOwner");
            bool Enabled(object owner)=>Enumerable.Range(1,Convert.ToInt32(Get(owner,"Count"))).Any(i=>!Convert.ToBoolean(Get(GetItem(owner,i),"Suppress")));
            if(Enumerable.Range(1,Convert.ToInt32(Get(constraints,"Count"))).Any(i=>SimulationInt(GetItem(constraints,i),"GetConstraintType")!=1) || Enumerable.Range(1,Convert.ToInt32(Get(loads,"Count"))).Any(i=>SimulationInt(GetItem(loads,i),"GetLoadType") is not (1 or 2)))
                throw new InvalidOperationException("This phase supports fully fixed face conditions and force/pressure loads only.");
            if(!Enabled(constraints)||!Enabled(loads))throw new InvalidOperationException("Solving requires at least one active fixed condition and one active load. Review solidedge_list_simulation_studies.");
        }
        SimulationCheckStudySwitch(doc,study);
        SimulationRemember(doc,study,"pending");
        if(regenerateMesh) {
            Call(SimulationOwner(study,"GetMeshOwner"),"DeleteAllMesh");
            // Native mesh deletion can activate a different open document.
            CadActivateDocument(doc);
            CadDocument(CadApplication(),expectedDocument,".par");
        }
        if(meshLevel!=0)Call(SimulationOwner(study,"GetMeshOwner"),"SetMeshSize",(double)meshLevel);
        else if(meshSizeMm!=0)Call(SimulationOwner(study,"GetMeshOwner"),"SetMeshSizeValue",meshSizeMm/1000);
        Call(Get(doc,"StudyOwner"),"SetStudyActive",SimulationName(study));
        if(!meshOnly) {object[] options=[0u];CadCallRef(study,"GetResultOptions",[0],options);Call(study,"SetResultOptions",Convert.ToUInt32(options[0])|1052673u);}
        Call(study,"Solve",doc,meshOnly?1:0);
        CadDocument(CadApplication(),expectedDocument,".par");
        bool completed=SimulationStatus(study,meshOnly?9:10)&&!SimulationStatus(study,1)&&!SimulationStatus(study,2)&&(meshOnly||!SimulationStatus(study,3));
        if(!meshOnly&&completed)SimulationRemember(doc,study,SimulationFingerprint(doc,study));
        var warnings=new List<string>();var state=SimulationStudyInfo(doc,study,studyNumber,warnings);
        object[] error=[""];if(!meshOnly&&!completed)CadCallRef(study,"GetNastranErrorMessage",[0],error);
        return new {fullName=CadName(doc),operation=meshOnly?"mesh":"solve",completed,study=state,nastranError=Convert.ToString(error[0]),warnings,note=completed?"Review results; the part has not been saved.":"Solid Edge has not reported completion. Inspect its solver/license dialogs and read the study status again; no automatic retry was performed."};
    }
    static object? SimulationTryItem(object collection,int index)
    {
        try { return GetItem(collection,index); }
        catch(Exception ex) when((ex.InnerException??ex).HResult==unchecked((int)0x8002000B)) { return null; }
    }
    static (object owner,object plot) SimulationPlot(object study,string resultKind)
    {
        if(resultKind=="active") {var owner=SimulationOwner(study,"GetPlotsOwner");return(owner,SimulationOwner(owner,"GetActivePlot"));}
        var mode=SimulationOwner(study,"GetMode");int requiredOwner=resultKind=="stress"?7:1;int requiredPlot=resultKind=="stress"?60031:1;
        for(int i=1;i<=16;i++) {
            var owner=SimulationTryItem(mode,i);if(owner==null)break;if(SimulationInt(owner,"GetPlotOwnerType")!=requiredOwner)continue;
            for(int p=1;p<=64;p++) {var plot=SimulationTryItem(owner,p);if(plot==null)break;if(SimulationInt(plot,"GetPlotType")==requiredPlot)return(owner,plot);}
        }
        throw new InvalidOperationException("Requested "+resultKind+" plot is not available. Solve with stress and displacement outputs enabled.");
    }
    static void SimulationResultKind(string resultKind) {if(resultKind is not ("active" or "stress" or "displacement"))throw new ArgumentException("resultKind must be active, stress or displacement.");}
    public static object CadGetSimulationResults(string expectedDocument,int studyNumber,string resultKind="stress")
    {
        SimulationResultKind(resultKind);var doc=SimulationDocument(expectedDocument,false);var study=SimulationStudy(doc,studyNumber);
        if(!SimulationStatus(study,10)||!SimulationResultsCurrent(doc,study))throw new InvalidOperationException("This study has no verified current results. Conditions may have changed, or it was solved outside MechCue. Run solidedge_run_simulation again.");
        var (owner,plot)=SimulationPlot(study,resultKind);object[] range=[0d,0d];CadCallRef(plot,"GetMinMaxValues",[0,1],range);
        object[] max=[0d,0d,0d],min=[0d,0d,0d];CadCallRef(plot,"GetXYZLocationsOfMaxValue",[0,1,2],max);CadCallRef(plot,"GetXYZLocationsOfMinValue",[0,1,2],min);
        int ownerType=SimulationInt(owner,"GetPlotOwnerType"),plotType=SimulationInt(plot,"GetPlotType");
        bool translation=ownerType==1&&plotType is >=1 and <=4;
        string nativeUnit=ownerType==7?"Pa":translation?"m":"native";string unit=ownerType==7?"MPa":translation?"mm":"native";double scale=ownerType==7?1e-6:translation?1000:1;
        return new {fullName=CadName(doc),studyNumber,resultKind,plotOwnerType=ownerType,plotType,
            quantity=plotType==60031?"solid von Mises stress":ownerType==1&&plotType==1?"total translation":"native plot type "+plotType,
            maximum=Convert.ToDouble(range[0])*scale,minimum=Convert.ToDouble(range[1])*scale,unit,
            maximumNative=Convert.ToDouble(range[0]),minimumNative=Convert.ToDouble(range[1]),nativeUnit,
            maximumPositionMm=max.Select(x=>Convert.ToDouble(x)*1000).ToArray(),minimumPositionMm=min.Select(x=>Convert.ToDouble(x)*1000).ToArray(),
            note="Native plot extrema and locations. Location reporting follows Solid Edge; review the native plot before interpreting local peaks. This tool does not certify a design."};
    }
    public static object CadShowSimulationResults(string expectedDocument,int studyNumber,string resultKind="stress")
    {
        SimulationResultKind(resultKind);var doc=SimulationDocument(expectedDocument);var study=SimulationStudy(doc,studyNumber);if(!SimulationStatus(study,10)||!SimulationResultsCurrent(doc,study))throw new InvalidOperationException("This study has no verified current results. Run solidedge_run_simulation again.");
        Call(Get(doc,"StudyOwner"),"SetStudyActive",SimulationName(study));var(owner,plot)=SimulationPlot(study,resultKind);
        Call(SimulationOwner(study,"GetMode"),"SetActivePlotOwner",owner);Call(owner,"SetActivePlot",plot);Call(study,"ViewResults");return CadGetSimulationResults(expectedDocument,studyNumber,resultKind);
    }
}
