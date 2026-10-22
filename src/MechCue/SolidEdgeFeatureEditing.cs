using System.Text.Json;
namespace MechCue;

public sealed partial class Bridge
{
    static double[] FeaturePoint(object item,string method)
    {
        object[] args=[0d,0d];CadCallRef(item,method,[0,1],args);
        var point=args.Select(v=>Convert.ToDouble(v)*1000).ToArray();
        if(point.Any(v=>!double.IsFinite(v)))throw new InvalidOperationException("Invalid profile point");return point;
    }
    static object FeatureProfileInfo(object profile)
    {
        var plane=Get(profile,"Plane");var n=ConceptPlaneVector(plane,"GetNormal");var x=ConceptPlaneVector(plane,"GetReferenceDirection");
        object[] Items(string collection,Func<object,object> describe){
            var c=Get(profile,collection);
            return Enumerable.Range(1,Convert.ToInt32(Get(c,"Count"))).Select(i=>describe(GetItem(c,i))).ToArray();
        }
        return new{originMm=ConceptPlaneVector(plane,"GetRootPoint").Select(v=>v*1000).ToArray(),normal=ConceptPlaneVector(plane,"GetNormal"),
            xDirection=x,yDirection=new[]{n[1]*x[2]-n[2]*x[1],n[2]*x[0]-n[0]*x[2],n[0]*x[1]-n[1]*x[0]},
            lines=Items("Lines2d",l=>new{startMm=FeaturePoint(l,"GetStartPoint"),endMm=FeaturePoint(l,"GetEndPoint")}),
            circles=Items("Circles2d",c=>new{centerMm=FeaturePoint(c,"GetCenterPoint"),radiusMm=Convert.ToDouble(Get(c,"Radius"))*1000}),
            centersMm=Items("Holes2d",h=>FeaturePoint(h,"GetCenterPoint")),
            arcsCount=Convert.ToInt32(Get(Get(profile,"Arcs2d"),"Count")),
            note="Plane-local sketch coordinates; entry direction is feature extentSide. Lines/circles/hole centers only; other curves are not reconstructed."};
    }
    static (object[] Parents,object[] Dependents) FeatureDependencies(object doc,object feature)
    {
        object[] counts=[feature,0,0];CadCallRef(doc,"GetNumberOfParentsAndDependents",[1,2],counts);
        object[] args=[feature,Array.CreateInstance(typeof(System.Runtime.InteropServices.DispatchWrapper),Convert.ToInt32(counts[1])),
            Array.CreateInstance(typeof(System.Runtime.InteropServices.DispatchWrapper),Convert.ToInt32(counts[2]))];
        CadCallRef(doc,"GetParentsAndDependents",[1,2],args);
        object[] Objects(object v)=>v is Array a?a.Cast<object>().ToArray():[];
        var parents=Objects(args[1]);var dependents=Objects(args[2]);
        if(parents.Length!=Convert.ToInt32(counts[1])||dependents.Length!=Convert.ToInt32(counts[2]))throw new InvalidOperationException("Incomplete native dependencies; mutation refused");
        return(parents,dependents);
    }
    static object FeatureIdentity(object f)=>new{name=Convert.ToString(Get(f,"Name")),nativeType=Convert.ToInt32(Get(f,"Type"))};
    public static object CadGetFeatureDetails(string expectedDocument,string featureName)
    {
        var doc=DetailPart(expectedDocument,false);var f=MachiningFeature(doc,featureName);var dependencies=FeatureDependencies(doc,f);
        var result=new Dictionary<string,object?>{{"document",expectedDocument},{"featureName",featureName},{"status",CadFeatureStatus(f)},
            {"nativeType",Convert.ToInt32(Get(f,"Type"))},{"parents",dependencies.Parents.Select(FeatureIdentity).ToArray()},
            {"dependents",dependencies.Dependents.Select(FeatureIdentity).ToArray()},{"dependenciesComplete",true}};
        var model=GetItem(Get(doc,"Models"),1);
        bool Contains(string c)=>Enumerable.Range(1,Convert.ToInt32(Get(Get(model,c),"Count"))).Any(i=>Convert.ToString(Get(GetItem(Get(model,c),i),"Name"))==featureName);
        if(Contains("Holes")){result["kind"]="hole";result["hole"]=MachiningHoleInfo(f);result["extentSide"]=Get(f,"ExtentSide");result["profile"]=FeatureProfileInfo(Get(f,"Profile"));}
        else if(Contains("Patterns")){result["kind"]="pattern";result["pattern"]=FeaturePatternInfo(f);}
        else if(Contains("ExtrudedCutouts")||Contains("ExtrudedProtrusions")){
            result["kind"]=Contains("ExtrudedCutouts")?"extruded-cutout":"extruded-protrusion";
            int extent=Convert.ToInt32(Get(f,"ExtentType"));result["extentType"]=extent;result["extentSide"]=Get(f,"ExtentSide");
            result["depthMm"]=extent==13?Convert.ToDouble(Get(f,"Depth"))*1000:null;result["profile"]=FeatureProfileInfo(Get(f,"Profile"));
        }else result["kind"]="other";
        return result;
    }
    static object FeaturePatternInfo(object pattern)
    {
        object[] seeds=[Array.CreateInstance(typeof(System.Runtime.InteropServices.DispatchWrapper),Convert.ToInt32(Get(pattern,"NumberOfInputFeatures")))];
        CadCallRef(pattern,"GetInputFeatures",[0],seeds);
        var inputs=seeds[0] is Array array?array.Cast<object>().Select(FeatureIdentity).ToArray():[];
        if(inputs.Length!=Convert.ToInt32(Get(pattern,"NumberOfInputFeatures")))throw new InvalidOperationException("Incomplete pattern inputs");
        var profile=Get(pattern,"Profile");var rect=Get(profile,"RectangularPatterns2d");var circ=Get(profile,"CircularPatterns2d");
        bool rectangular=Convert.ToInt32(Get(rect,"Count"))==1,circular=Convert.ToInt32(Get(circ,"Count"))==1;
        object? layout=rectangular?new{xCount=Get(GetItem(rect,1),"XCount"),yCount=Get(GetItem(rect,1),"YCount"),xSpacingMm=Convert.ToDouble(Get(GetItem(rect,1),"XSpace"))*1000,
            ySpacingMm=Convert.ToDouble(Get(GetItem(rect,1),"YSpace"))*1000}:
            circular?new{radialCount=Convert.ToInt32(Get(pattern,"NumberOfOccurrences")),angleSpacingDeg=Convert.ToDouble(Get(GetItem(circ,1),"AngularSpacing"))*180/Math.PI}:null;
        return new{patternType=rectangular?"rectangular":circular?"circular":"unsupported",instanceCount=Convert.ToInt32(Get(pattern,"NumberOfOccurrences")),inputs,layout};
    }
    static JsonElement FeaturePatch(string text,string[] allowed)
    {
        var patch=JsonSerializer.Deserialize<JsonElement>(text);
        if(patch.ValueKind!=JsonValueKind.Object)throw new ArgumentException("changesJson must be an object");
        var names=new HashSet<string>(StringComparer.Ordinal);
        foreach(var p in patch.EnumerateObject())if(!allowed.Contains(p.Name)||!names.Add(p.Name))throw new ArgumentException("Unknown or duplicate edit field: "+p.Name);
        if(names.Count==0)throw new ArgumentException("At least one edit field required");return patch;
    }
    static void FeatureEdit(object doc,Action apply,Action verify,Action restore,string operation)
    {
        try{apply();Call(doc,"Recompute");DetailHealthy(doc);verify();}
        catch(Exception error){
            try{
                restore();
                Call(doc,"Recompute");
                DetailHealthy(doc);
            }catch(Exception rollback){
                throw new InvalidOperationException(operation+" failed; ROLLBACK FAILED: "+(rollback.InnerException??rollback).Message,error);
            }
            throw new InvalidOperationException(operation+" failed; original values restored, document may be dirty: "+(error.InnerException??error).Message,error);}
    }
    public static object CadEditHole(string expectedDocument,string featureName,string changesJson)
    {
        var patch=FeaturePatch(changesJson,["diameterMm","depthMm","counterboreDiameterMm","counterboreDepthMm","countersinkDiameterMm","countersinkAngleDeg","threadDepthMm","centerXmm","centerYmm"]);
        var doc=DetailPart(expectedDocument);DetailHealthy(doc);var holes=Get(GetItem(Get(doc,"Models"),1),"Holes");
        var hole=Enumerable.Range(1,Convert.ToInt32(Get(holes,"Count"))).Select(i=>GetItem(holes,i)).Single(h=>Convert.ToString(Get(h,"Name"))==featureName);
        var data=Get(hole,"HoleData");int type=Convert.ToInt32(Get(data,"HoleType")),extent=Convert.ToInt32(Get(hole,"ExtentType")),treatment=Convert.ToInt32(Get(data,"TreatmentType"));
        if(Enumerable.Range(1,Convert.ToInt32(Get(holes,"Count"))).Select(i=>GetItem(holes,i)).Any(h=>Convert.ToString(Get(h,"Name"))!=featureName
            &&ReferenceEquals(Get(h,"HoleData"),data)))throw new InvalidOperationException("Shared HoleData; edit would affect another feature");
        var updates=new List<(object Target,string Property,object Old,double Value)>();
        foreach(var p in patch.EnumerateObject()){
            double mm=p.Value.GetDouble();CadFinite(mm,p.Name);if(p.Name.StartsWith("center"))continue;
            if(mm<=0)throw new ArgumentException("Positive machining dimensions required");
            if(p.Name=="depthMm"&&extent!=13||p.Name.StartsWith("counterbore")&&type!=34||p.Name.StartsWith("countersink")&&type!=35||p.Name=="threadDepthMm"
                &&(treatment!=37||Convert.ToInt32(Get(data,"ThreadDepthMethod"))!=13))throw new ArgumentException("Field incompatible with existing hole type/extent/treatment");
            if(p.Name=="diameterMm"&&treatment==37
                &&Convert.ToInt32(Get(data,
                    "ThreadDiameterOption"))!=0)throw new InvalidOperationException("Tapped diameter editing requires native tap-drill option; use set_hole_thread to change mode");
            string property=p.Name switch{"diameterMm"=>treatment==37?"ThreadTapDrillDiameter":"HoleDiameter","depthMm"=>"Depth","counterboreDiameterMm"=>"CounterboreDiameter",
                "counterboreDepthMm"=>"CounterboreDepth","countersinkDiameterMm"=>"CountersinkDiameter","countersinkAngleDeg"=>"CountersinkAngle","threadDepthMm"=>"ThreadDepth",
                _=>throw new ArgumentException("Unsupported edit")};
            var target=p.Name=="depthMm"?hole:data;updates.Add((target,property,Get(target,property),p.Name=="countersinkAngleDeg"?mm:mm/1000));
        }
        double Proposed(object target,string property)=>updates.FirstOrDefault(u=>ReferenceEquals(u.Target,target)&&u.Property==property) is var u
            &&u.Target!=null?u.Value:Convert.ToDouble(Get(target,property));
        double diameter=Proposed(data,treatment==37?"ThreadTapDrillDiameter":"HoleDiameter"),depth=extent==13?Proposed(hole,"Depth"):double.PositiveInfinity;
        if(type==34&&(Proposed(data,"CounterboreDiameter")<=diameter||Proposed(data,"CounterboreDepth")>=depth)||type==35
            &&(Proposed(data,"CountersinkDiameter")<=diameter||Proposed(data,"CountersinkAngle")>=180))throw new ArgumentException("Invalid counterbore/countersink dimensions");
        if(treatment==37){
            var thread=MachiningThreads().Entries.Single(e=>e.Description==Convert.ToString(Get(data,"ThreadDescription")));
            if(diameter*1000<thread.InternalMinorDiameterMm||diameter*1000>=thread.NominalDiameterMm||Convert.ToInt32(Get(data,"ThreadDepthMethod"))==13
                &&Proposed(data,"ThreadDepth")>depth)throw new ArgumentException("Tap drill/thread depth outside native bounds");
        }
        var centers=Get(Get(hole,"Profile"),"Holes2d");if(Convert.ToInt32(Get(centers,"Count"))!=1)throw new InvalidOperationException("Edit supports one native center");
        var center=GetItem(centers,1);
        var oldPoint=FeaturePoint(center,"GetCenterPoint");
        double x=patch.TryGetProperty("centerXmm",out var px)?px.GetDouble():oldPoint[0],y=patch.TryGetProperty("centerYmm",out var py)?py.GetDouble():oldPoint[1];
        bool move=patch.TryGetProperty("centerXmm",out _)||patch.TryGetProperty("centerYmm",out _);
        FeatureEdit(doc,()=>{foreach(var u in updates)Set(u.Target,u.Property,u.Value);if(move)Call(center,"SetCenterPoint",x/1000,y/1000);},
            ()=>{
                foreach(var u in updates)if(Math.Abs(Convert.ToDouble(Get(u.Target,u.Property))-u.Value)>1e-8)throw new InvalidOperationException("Edited value not retained: "+u.Property);
                var p=FeaturePoint(center,"GetCenterPoint");
                if(Math.Abs(p[0]-x)>1e-5||Math.Abs(p[1]-y)>1e-5)throw new InvalidOperationException("Center not retained");
            },
            ()=>{foreach(var u in updates.AsEnumerable().Reverse())Set(u.Target,u.Property,u.Old);if(move)Call(center,"SetCenterPoint",oldPoint[0]/1000,oldPoint[1]/1000);
                foreach(var u in updates)if(Math.Abs(Convert.ToDouble(Get(u.Target,u.Property))-Convert.ToDouble(u.Old))>1e-8)throw new InvalidOperationException("Original dimension not restored");
                var p=FeaturePoint(center,"GetCenterPoint");
                if(p.Where((v,k)=>Math.Abs(v-oldPoint[k])>1e-5).Any())throw new InvalidOperationException("Original center not restored");
                },"Hole edit");
        return new{document=expectedDocument,hole=MachiningHoleInfo(hole),centersMm=new[]{FeaturePoint(center,"GetCenterPoint")},allFeaturesOk=true,saved=false};
    }
    public static object CadEditHolePattern(string expectedDocument,string featureName,string changesJson)
    {
        var patch=FeaturePatch(changesJson,["xCount","yCount","xSpacingMm","ySpacingMm","angleSpacingDeg","radialCount"]);
        var doc=DetailPart(expectedDocument);DetailHealthy(doc);var patterns=Get(GetItem(Get(doc,"Models"),1),"Patterns");
        var pattern=Enumerable.Range(1,Convert.ToInt32(Get(patterns,"Count"))).Select(i=>GetItem(patterns,i)).Single(p=>Convert.ToString(Get(p,"Name"))==featureName);
        var info=JsonSerializer.SerializeToElement(FeaturePatternInfo(pattern));if(info.GetProperty("inputs").GetArrayLength()!=1)throw new InvalidOperationException("Single seed pattern required");
        string kind=info.GetProperty("patternType").GetString()!;if(kind=="unsupported")throw new InvalidOperationException("Unsupported pattern layout");
        var array=GetItem(Get(Get(pattern,"Profile"),kind=="rectangular"?"RectangularPatterns2d":"CircularPatterns2d"),1);
        var updates=new List<(string Property,object Old,double Value)>();
        foreach(var p in patch.EnumerateObject()){
            if(kind=="rectangular"&&(p.Name=="angleSpacingDeg"||p.Name=="radialCount")||kind=="circular"
                &&p.Name is not ("angleSpacingDeg" or "radialCount"))throw new ArgumentException("Field incompatible with pattern type");
            double value=p.Value.GetDouble();
            CadFinite(value,p.Name,true);
            if(p.Name.EndsWith("Count")&&(value!=Math.Truncate(value)||value>256))throw new ArgumentException("Integer count 1..256 required");
            string prop=p.Name switch{"xCount"=>"XCount","yCount"=>"YCount","radialCount"=>"Count","xSpacingMm"=>"XSpace","ySpacingMm"=>"YSpace",_=>"AngularSpacing"};
            updates.Add((prop,Get(array,prop),p.Name.EndsWith("Count")?value:p.Name=="angleSpacingDeg"?value*Math.PI/180:value/1000));
        }
        double Proposed(string property)=>updates.FirstOrDefault(u=>u.Property==property) is var u&&u.Property!=null?u.Value:Convert.ToDouble(Get(array,property));
        int expected=Convert.ToInt32(Get(pattern,"NumberOfOccurrences"));
        if(kind=="rectangular"){
            expected=(int)(Proposed("XCount")*Proposed("YCount"));
            if(expected is <2 or >256)throw new ArgumentException("2..256 total instances required");
        }
        else {
            expected=(int)Proposed("Count");double angle=Proposed("AngularSpacing");
            if(expected is <2 or >256||(expected-1)*angle>=2*Math.PI-1e-8)throw new ArgumentException("Circular 2..256 instances, no duplicate endpoint required");
        }
        void Put((string Property,object Old,double Value) u,bool restore)=>Set(array,u.Property,restore?u.Old:u.Property.EndsWith("Count")?(object)(int)u.Value:u.Value);
        FeatureEdit(doc,()=>{foreach(var u in updates)Put(u,false);},
            ()=>{
                if(Convert.ToInt32(Get(pattern,"NumberOfOccurrences"))!=expected||updates.Any(u=>Math.Abs(Convert.ToDouble(Get(array,u.Property))-u.Value)>1e-8))
                    throw new InvalidOperationException("Pattern values not retained");
            },
            ()=>{
                foreach(var u in updates.AsEnumerable().Reverse())Put(u,true);
                if(updates.Any(u=>Math.Abs(Convert.ToDouble(Get(array,u.Property))-Convert.ToDouble(u.Old))>1e-8))throw new InvalidOperationException("Original pattern data not restored");
            },
            "Pattern edit");
        return new{document=expectedDocument,pattern=FeaturePatternInfo(pattern),allFeaturesOk=true,saved=false};
    }
    public static object CadSetHoleThread(string expectedDocument,string featureName,string threadDescription,double tapDrillDiameterMm,double threadDepthMm=0)
    {
        CadFinite(tapDrillDiameterMm,nameof(tapDrillDiameterMm),true);
        CadFinite(threadDepthMm,nameof(threadDepthMm));
        if(threadDepthMm<0||threadDescription.Length>255)throw new ArgumentException("Nonnegative depth and exact catalog thread required");
        var thread=threadDescription==""?null:MachiningThreads().Entries.SingleOrDefault(t=>t.Description==threadDescription)??throw new ArgumentException("Unknown native thread description");
        if(thread==null&&threadDepthMm!=0||thread!=null
            &&(tapDrillDiameterMm<thread.InternalMinorDiameterMm||tapDrillDiameterMm>=thread.NominalDiameterMm))throw new ArgumentException("Invalid tap drill or unthreaded depth");
        var doc=DetailPart(expectedDocument);DetailHealthy(doc);var holes=Get(GetItem(Get(doc,"Models"),1),"Holes");
        var hole=Enumerable.Range(1,Convert.ToInt32(Get(holes,"Count"))).Select(i=>GetItem(holes,i)).Single(h=>Convert.ToString(Get(h,"Name"))==featureName);
        int extent=Convert.ToInt32(Get(hole,"ExtentType"));if(extent==13&&threadDepthMm>Convert.ToDouble(Get(hole,"Depth"))*1000)throw new ArgumentException("Thread depth exceeds finite hole depth");
        var old=Get(hole,"HoleData");
        string oldInfo=JsonSerializer.Serialize(MachiningHoleInfo(hole));
        int type=Convert.ToInt32(Get(old,"HoleType"));
        if(type is not (33 or 34 or 35)||Convert.ToBoolean(Get(hole,"CreatePhysicalThread")))throw new InvalidOperationException("Regular/counterbore/countersink cosmetic holes supported");
        if(Enumerable.Range(1,Convert.ToInt32(Get(holes,"Count"))).Select(i=>GetItem(holes,i)).Any(h=>Convert.ToString(Get(h,"Name"))!=featureName
            &&ReferenceEquals(Get(h,"HoleData"),old)))throw new InvalidOperationException("Shared HoleData; thread edit refused");
        string[] properties=["TreatmentType","ThreadSetting","ThreadNominalDiameter","ThreadMinorDiameter","ThreadDescription","ThreadDepthMethod","ThreadDepth","ThreadDiameterOption",
            "ThreadTapDrillDiameter","HoleDiameter"];
        var original=properties.ToDictionary(p=>p,p=>Get(old,p));
        double? originalDepth=extent==13?Convert.ToDouble(Get(hole,"Depth")):null;
        int undoStart=Convert.ToInt32(Call(doc,"UndoTransactionCount"));
        if(Convert.ToInt32(Get(doc,"UndoSteps"))<1)throw new InvalidOperationException("Native undo must be enabled for thread editing");
        if(type==34&&Convert.ToDouble(Get(old,"CounterboreDiameter"))*1000<=tapDrillDiameterMm||type==35
            &&Convert.ToDouble(Get(old,"CountersinkDiameter"))*1000<=tapDrillDiameterMm)throw new ArgumentException("Tap drill must be smaller than seat");
        try{
            var assigned=Get(hole,"HoleData");Set(assigned,"TreatmentType",thread==null?44:37);
            if(thread!=null)Set(assigned,"ThreadSetting",164); // igRegularThread: supported straight metric catalog
            if(thread==null)Set(assigned,"HoleDiameter",tapDrillDiameterMm/1000);
            if(thread!=null){
                Set(assigned,"ThreadDataByDescription",threadDescription);
                Set(assigned,"ThreadDiameterOption",0);
                Set(assigned,"ThreadTapDrillDiameter",tapDrillDiameterMm/1000);
                Set(assigned,"ThreadDepthMethod",threadDepthMm==0?16:13);
                Set(assigned,"ThreadDepth",threadDepthMm/1000);
            }
            Set(hole,"HoleData",assigned);if(originalDepth.HasValue)Set(hole,"Depth",originalDepth.Value);
            Call(doc,"Recompute");DetailHealthy(doc);
            var retained=Get(hole,"HoleData");
            if(Convert.ToInt32(Get(retained,"TreatmentType"))!=(thread==null?44:37)
                ||thread==null&&Math.Abs(Convert.ToDouble(Get(retained,"HoleDiameter"))*1000-tapDrillDiameterMm)>1e-5
                ||thread!=null&&(Convert.ToString(Get(retained,"ThreadDescription"))!=threadDescription
                    ||Math.Abs(Convert.ToDouble(Get(retained,"ThreadTapDrillDiameter"))*1000-tapDrillDiameterMm)>1e-5
                    ||Math.Abs(Convert.ToDouble(Get(retained,"ThreadNominalDiameter"))*1000-thread.NominalDiameterMm)>1e-5
                    ||Convert.ToInt32(Get(retained,"ThreadDepthMethod"))!=(threadDepthMm==0?16:13)
                    ||threadDepthMm>0&&Math.Abs(Convert.ToDouble(Get(retained,"ThreadDepth"))*1000-threadDepthMm)>1e-5))
                throw new InvalidOperationException("Thread replacement not retained: "+JsonSerializer.Serialize(MachiningHoleInfo(hole)));
            var faces=Get(hole,"SideFaces");
            var diameters=new List<double>();
            for(int i=1;i<=Convert.ToInt32(Get(faces,"Count"));i++)try{
                diameters.Add(Convert.ToDouble(Get(Get(GetItem(faces,i),"Geometry"),"Radius"))*2000);
            }catch{}
            if(!diameters.Any(d=>Math.Abs(d-tapDrillDiameterMm)<1e-5))throw new InvalidOperationException("Tap drill solid diameter not retained: "+JsonSerializer.Serialize(diameters));
        }catch(Exception error){
            try{
                int delta=Convert.ToInt32(Call(doc,"UndoTransactionCount"))-undoStart;
                if(delta>0)Call(doc,"Undo",delta);
                else {foreach(var p in properties)Set(old,p,original[p]);Set(hole,"HoleData",old);if(originalDepth.HasValue)Set(hole,"Depth",originalDepth.Value);}
                Call(doc,"Recompute");
                DetailHealthy(doc);
                if(JsonSerializer.Serialize(MachiningHoleInfo(MachiningFeature(doc,featureName)))!=oldInfo)throw new InvalidOperationException("Original hole information not restored");
            }catch(Exception rollback){
                throw new InvalidOperationException("Thread edit failed; ROLLBACK FAILED: "+rollback.Message,error);
            }
            throw new InvalidOperationException("Thread edit failed; original HoleData restored: "+(error.InnerException??error).Message,error);
        }
        return new{document=expectedDocument,hole=MachiningHoleInfo(hole),allFeaturesOk=true,saved=false,
            note="Native cosmetic internal thread; original native properties snapshotted for recovery. No helical solid."};
    }
    public static object CadDeleteMachiningFeature(string expectedDocument,string featureName,string backupPath)
    {
        backupPath=CadPath(backupPath,".par");
        if(File.Exists(backupPath)||!Directory.Exists(Path.GetDirectoryName(backupPath)))throw new ArgumentException("New backup .par in existing directory required");
        var doc=DetailPart(expectedDocument);DetailHealthy(doc);var feature=MachiningFeature(doc,featureName);var model=GetItem(Get(doc,"Models"),1);
        bool Supported(string c)=>Enumerable.Range(1,Convert.ToInt32(Get(Get(model,c),"Count"))).Any(i=>Convert.ToString(Get(GetItem(Get(model,c),i),"Name"))==featureName);
        if(!Supported("Holes")&&!Supported("Patterns"))throw new InvalidOperationException("Only native Hole/Pattern deletion supported");
        var deps=FeatureDependencies(doc,feature);
        if(deps.Dependents.Length>0)throw new InvalidOperationException("feature_has_dependents: "+JsonSerializer.Serialize(deps.Dependents.Select(FeatureIdentity)));
        var before=JsonSerializer.SerializeToElement(CadListFeatures(expectedDocument)).GetProperty("features").EnumerateArray().Select(f=>f.GetProperty("name").GetString()).ToArray();
        int undo=Convert.ToInt32(Call(doc,"UndoTransactionCount"));if(Convert.ToInt32(Get(doc,"UndoSteps"))<1)throw new InvalidOperationException("Native undo must be enabled before deletion");
        Call(doc,"SaveCopyAs",backupPath);if(!File.Exists(backupPath)||new FileInfo(backupPath).Length==0)throw new IOException("Backup not created; deletion refused");
        try{
            Call(feature,"Delete");
            Call(doc,"Recompute");
            DetailHealthy(doc);
            var after=JsonSerializer.SerializeToElement(CadListFeatures(expectedDocument)).GetProperty("features").EnumerateArray().Select(f=>f.GetProperty("name").GetString()).ToArray();
            if(!after.SequenceEqual(before.Where(n=>n!=featureName)))throw new InvalidOperationException("Unexpected feature removal");
        }
        catch(Exception error){
            try{
                int delta=Convert.ToInt32(Call(doc,"UndoTransactionCount"))-undo;
                if(delta<=0)throw new InvalidOperationException("No undo transaction available");
                Call(doc,"Undo",delta);
                DetailHealthy(doc);
                var restored=JsonSerializer.SerializeToElement(CadListFeatures(expectedDocument)).GetProperty("features").EnumerateArray().Select(f=>f.GetProperty("name").GetString());
                if(!restored.SequenceEqual(before))throw new InvalidOperationException("Original feature list not restored");
            }catch(Exception rollback){
                throw new InvalidOperationException("Delete failed; ROLLBACK FAILED. Recover from backup "+backupPath+": "+rollback.Message,error);
            }throw new InvalidOperationException("Delete failed; original features restored. Backup: "+backupPath,error);
        }
        return new{document=expectedDocument,deletedFeature=featureName,backupPath,allFeaturesOk=true,saved=false,
            note="Backup is a recovery copy. No source overwrite or dependent cascade; reread topology keys."};
    }
}
