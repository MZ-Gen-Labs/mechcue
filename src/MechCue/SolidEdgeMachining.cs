using System.Runtime.InteropServices;
using System.Text.Json;
using System.Globalization;
namespace MechCue;

public sealed partial class Bridge
{
    public sealed record CadMetricThread(string Description,double NominalDiameterMm,double ExternalMinorDiameterMm,double InternalMinorDiameterMm,string Family);
    static (string Path,CadMetricThread[] Entries) MachiningThreads()
    {
        object[] args=[61,null!];CadCallRef(CadApplication(),"GetGlobalParameter",[1],args);
        string path=Convert.ToString(args[1])??throw new InvalidOperationException("Native hole size file unavailable");
        if(!File.Exists(path)||!path.EndsWith(".txt",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Configured legacy hole size .txt file required for this thread catalog: "+path);
        bool metric=false;var entries=new List<CadMetricThread>();
        foreach(var line in File.ReadLines(path)) {
            var text=line.Trim();if(text=="BeginMetric"){metric=true;continue;}if(text=="EndMetric"){metric=false;continue;}if(!metric||text.StartsWith(@"\\"))continue;
            var fields=text.Split(';').Select(s=>s.Trim()).ToArray();
            if(fields.Length<5||!double.TryParse(fields[0],NumberStyles.Float,CultureInfo.InvariantCulture,out var nominal)||!double.TryParse(fields[1],NumberStyles.Float,CultureInfo.InvariantCulture,out var external)||!double.TryParse(fields[2],NumberStyles.Float,CultureInfo.InvariantCulture,out var minor))continue;
            if(nominal>0&&minor>0&&external>0&&minor<nominal&&fields[3]!="")entries.Add(new(fields[3],nominal,external,minor,fields[4]));
        }
        if(entries.Count==0)throw new InvalidOperationException("Configured hole size file has no metric thread entries");return(path,entries.ToArray());
    }
    public static object CadListMetricThreads() {var catalog=MachiningThreads();return new {sourceFile=catalog.Path,threads=catalog.Entries,note="Configured native legacy HoleSizeFile, metric straight thread entries; not the newer workbook hole-standard catalog. Exact description required. Tap drill diameter is caller-selected between internal minor and nominal diameter."};}
    static object MachiningFeature(object doc, string name)
    {
        var features=Get(GetItem(Get(doc,"Models"),1),"Features");
        return Enumerable.Range(1,Convert.ToInt32(Get(features,"Count"))).Select(i=>GetItem(features,i)).Single(f=>string.Equals(Convert.ToString(Get(f,"Name")),name,StringComparison.Ordinal));
    }
    static void MachiningRollback(object doc,object collection,int before,Action? cleanup,Exception error,string operation)
    {
        try {
            while(Convert.ToInt32(Get(collection,"Count"))>before)Call(GetItem(collection,Convert.ToInt32(Get(collection,"Count"))),"Delete");
            cleanup?.Invoke();Call(doc,"Recompute");DetailHealthy(doc);
        }catch(Exception rollback){throw new InvalidOperationException(operation+" failed: "+(error.InnerException??error).Message+"; ROLLBACK FAILED: "+(rollback.InnerException??rollback).Message,error);}
        throw new InvalidOperationException(operation+" failed; newly created objects removed, document may be dirty: "+(error.InnerException??error).Message,error);
    }
    static object MachiningHoleInfo(object hole)
    {
        var data=Get(hole,"HoleData");var profile=Get(hole,"Profile");
        int type=Convert.ToInt32(Get(data,"HoleType")),treatment=Convert.ToInt32(Get(data,"TreatmentType")),extent=Convert.ToInt32(Get(hole,"ExtentType"));
        int threadDepthMethod=treatment==37?Convert.ToInt32(Get(data,"ThreadDepthMethod")):0;
        return new {featureName=Convert.ToString(Get(hole,"Name")),status=CadFeatureStatus(hole),holeType=type,diameterMm=Convert.ToDouble(Get(data,"HoleDiameter"))*1000,
            counterboreDiameterMm=type==34?Convert.ToDouble(Get(data,"CounterboreDiameter"))*1000:0,counterboreDepthMm=type==34?Convert.ToDouble(Get(data,"CounterboreDepth"))*1000:0,
            countersinkDiameterMm=type==35?Convert.ToDouble(Get(data,"CountersinkDiameter"))*1000:0,countersinkAngleDeg=type==35?Convert.ToDouble(Get(data,"CountersinkAngle")):0,
            treatmentType=treatment,threadDescription=treatment==37?Convert.ToString(Get(data,"ThreadDescription")):"",threadDepthMm=threadDepthMethod==13?Convert.ToDouble(Get(data,"ThreadDepth"))*1000:(double?)null,
            threadDepthMethod,extentType=extent,depthMm=extent==13?Convert.ToDouble(Get(hole,"Depth"))*1000:(double?)null,
            threadDiameterOption=treatment==37?Convert.ToInt32(Get(data,"ThreadDiameterOption")):0,threadTapDrillDiameterMm=treatment==37?Convert.ToDouble(Get(data,"ThreadTapDrillDiameter"))*1000:0,
            holeCount=Convert.ToInt32(Get(Get(profile,"Holes2d"),"Count")),physicalThread=Convert.ToBoolean(Get(hole,"CreatePhysicalThread"))};
    }
    public static object CadListHoles(string expectedDocument)
    {
        var doc=DetailPart(expectedDocument,false);var holes=Get(GetItem(Get(doc,"Models"),1),"Holes");
        return new {document=expectedDocument,holes=Enumerable.Range(1,Convert.ToInt32(Get(holes,"Count"))).Select(i=>MachiningHoleInfo(GetItem(holes,i))).ToArray()};
    }
    public static object CadCreateHole(string expectedDocument,string centersJson,double diameterMm,string holeType="regular",string extent="through",double depthMm=0,
        int planeNumber=1,double planeOffsetMm=0,string direction="positive",double counterboreDiameterMm=0,double counterboreDepthMm=0,double countersinkDiameterMm=0,double countersinkAngleDeg=90,
        string threadDescription="",double threadDepthMm=0,string featureName="")
    {
        CadFinite(diameterMm,nameof(diameterMm),true);CadFinite(planeOffsetMm,nameof(planeOffsetMm));
        int type=holeType switch{"regular"=>33,"counterbore"=>34,"countersink"=>35,_=>throw new ArgumentException("holeType: regular/counterbore/countersink")};
        int side=direction switch{"positive"=>2,"negative"=>1,_=>throw new ArgumentException("direction: positive/negative")};
        if(extent is not ("through" or "finite"))throw new ArgumentException("extent: through/finite");
        foreach(var v in new[]{depthMm,counterboreDiameterMm,counterboreDepthMm,countersinkDiameterMm,threadDepthMm}){CadFinite(v,"hole dimensions");if(v<0)throw new ArgumentException("Negative hole dimension");}
        CadFinite(countersinkAngleDeg,nameof(countersinkAngleDeg),true);
        if(countersinkAngleDeg>=180)throw new ArgumentException("Countersink angle must be <180 degrees");
        if(extent=="finite"&&depthMm<=0)throw new ArgumentException("Finite depth required");
        if(type==34&&(counterboreDiameterMm<=diameterMm||counterboreDepthMm<=0||(extent=="finite"&&counterboreDepthMm>=depthMm)))throw new ArgumentException("Counterbore must be wider and shallower than hole");
        if(type==35&&countersinkDiameterMm<=diameterMm)throw new ArgumentException("Countersink must be wider than hole");
        if(type!=34&&(counterboreDiameterMm!=0||counterboreDepthMm!=0)||type!=35&&countersinkDiameterMm!=0)throw new ArgumentException("Dimensions do not match selected hole type");
        if(threadDescription==""&&threadDepthMm!=0||threadDescription!=""&&(string.IsNullOrWhiteSpace(threadDescription)||threadDescription.Length>255||threadDescription.Any(char.IsControl)))throw new ArgumentException("Valid native thread description required");
        if(extent=="finite"&&threadDepthMm>depthMm)throw new ArgumentException("Thread depth exceeds hole depth");
        var centers=JsonSerializer.Deserialize<CadPoint[]>(centersJson,new JsonSerializerOptions{PropertyNameCaseInsensitive=true})??throw new ArgumentException("centersJson required");
        if(centers.Length!=1)throw new ArgumentException("One native hole center required; use pattern_hole for repeated holes. Native profile creation can silently drop additional centers.");
        foreach(var p in centers){CadFinite(p.X,"center X");CadFinite(p.Y,"center Y");}
        var doc=DetailPart(expectedDocument);DetailHealthy(doc);var model=GetItem(Get(doc,"Models"),1);CadValidateFeatureName(Get(doc,"Models"),featureName);
        var thread=threadDescription==""?null:MachiningThreads().Entries.SingleOrDefault(e=>e.Description==threadDescription)??(threadDescription==""?null:throw new ArgumentException("Unknown native metric thread description; use list_metric_threads"));
        if(thread!=null&&(diameterMm<thread.InternalMinorDiameterMm||diameterMm>=thread.NominalDiameterMm))throw new ArgumentException("Tap drill diameter must be at least native internal minor diameter and below nominal thread diameter");
        var plane=CadPlane(doc,planeNumber);var holes=Get(model,"Holes");int count=Convert.ToInt32(Get(holes,"Count"));object? data=null,profileSet=null,offsetPlane=null;
        var profileSets=Get(doc,"ProfileSets");var holeDatas=Get(doc,"HoleDataCollection");var refPlanes=Get(doc,"RefPlanes");
        int profileCount=Convert.ToInt32(Get(profileSets,"Count")),dataCount=Convert.ToInt32(Get(holeDatas,"Count")),planeCount=Convert.ToInt32(Get(refPlanes,"Count"));
        try {
            object[] args=Enumerable.Repeat<object>(Type.Missing,21).ToArray();args[0]=type;args[1]=diameterMm/1000;args[6]=0d;args[7]=44;args[20]=true;
            if(type==34){args[2]=counterboreDiameterMm/1000;args[3]=counterboreDepthMm/1000;args[15]=149;}
            if(type==35){args[4]=countersinkDiameterMm/1000;args[5]=countersinkAngleDeg;}
            if(thread!=null) {args[7]=37;args[10]=thread.InternalMinorDiameterMm/1000;args[11]=threadDepthMm==0?16:13;args[12]=threadDepthMm/1000;args[18]=thread.NominalDiameterMm/1000;args[19]=thread.Description;}
            object[] exArgs=Enumerable.Repeat<object>(Type.Missing,37).ToArray();exArgs[0]=args[0];for(int i=1;i<21;i++)exArgs[i+4]=args[i];
            if(thread!=null){exArgs[25]=0;exArgs[26]=diameterMm/1000;}
            data=Call(Get(doc,"HoleDataCollection"),"AddEx",exArgs);
            if(planeOffsetMm!=0){offsetPlane=Call(Get(doc,"RefPlanes"),"AddParallelByDistance",plane,Math.Abs(planeOffsetMm)/1000,planeOffsetMm>0?2:1,Type.Missing,Type.Missing,Type.Missing,Type.Missing);plane=offsetPlane;}
            profileSet=Call(Get(doc,"ProfileSets"),"Add");var profile=Call(Get(profileSet,"Profiles"),"Add",plane);
            foreach(var center in centers)Call(Get(profile,"Holes2d"),"Add",center.X/1000,center.Y/1000);
            if(Convert.ToInt32(Call(profile,"End",1))!=0)throw new InvalidOperationException("Invalid hole profile");
            var hole=extent=="through"?Call(holes,"AddThroughAll",profile,side,data):Call(holes,"AddFinite",profile,side,depthMm/1000,data);
            if(featureName!="")Set(hole,"Name",featureName);Set(profile,"Visible",false);Call(doc,"Recompute");DetailHealthy(doc);
            var retained=Get(hole,"HoleData");
            if(Convert.ToInt32(Get(retained,"HoleType"))!=type||Math.Abs(Convert.ToDouble(Get(retained,"HoleDiameter"))*1000-diameterMm)>1e-5||Convert.ToInt32(Get(Get(profile,"Holes2d"),"Count"))!=centers.Length)throw new InvalidOperationException("Requested hole dimensions or centers not retained: type="+Get(retained,"HoleType")+"; diameter mm="+Convert.ToDouble(Get(retained,"HoleDiameter"))*1000+"; centers="+Get(Get(profile,"Holes2d"),"Count"));
            if(type==34&&(Math.Abs(Convert.ToDouble(Get(retained,"CounterboreDiameter"))*1000-counterboreDiameterMm)>1e-5||Math.Abs(Convert.ToDouble(Get(retained,"CounterboreDepth"))*1000-counterboreDepthMm)>1e-5))throw new InvalidOperationException("Counterbore dimensions not retained");
            if(type==35&&(Math.Abs(Convert.ToDouble(Get(retained,"CountersinkDiameter"))*1000-countersinkDiameterMm)>1e-5||Math.Abs(Convert.ToDouble(Get(retained,"CountersinkAngle"))-countersinkAngleDeg)>1e-5))throw new InvalidOperationException("Countersink dimensions not retained");
            if(extent=="finite"&&Math.Abs(Convert.ToDouble(Get(hole,"Depth"))*1000-depthMm)>1e-5)throw new InvalidOperationException("Hole depth not retained");
            if(thread!=null&&(!string.Equals(Convert.ToString(Get(Get(hole,"HoleData"),"ThreadDescription")),thread.Description,StringComparison.Ordinal)||Convert.ToInt32(Get(Get(hole,"HoleData"),"TreatmentType"))!=37))throw new InvalidOperationException("Native tapped-hole data not retained");
            if(thread!=null&&(Convert.ToInt32(Get(retained,"ThreadDepthMethod"))!=(threadDepthMm==0?16:13)||threadDepthMm>0&&Math.Abs(Convert.ToDouble(Get(retained,"ThreadDepth"))*1000-threadDepthMm)>1e-5))throw new InvalidOperationException("Thread depth not retained");
            return new {document=expectedDocument,hole=MachiningHoleInfo(hole),saved=false};
        }catch(Exception error){MachiningRollback(doc,holes,count,()=>{MachiningRemoveAdded(profileSets,profileCount);MachiningRemoveAdded(holeDatas,dataCount);MachiningRemoveAdded(refPlanes,planeCount);},error,"Hole creation");throw;}
    }
    static void MachiningRemoveAdded(object collection,int count) {while(Convert.ToInt32(Get(collection,"Count"))>count)Call(GetItem(collection,Convert.ToInt32(Get(collection,"Count"))),"Delete");}
    public static object CadPatternHole(string expectedDocument,string sourceFeatureName,string patternType="rectangular",int xCount=2,int yCount=2,double xSpacingMm=20,double ySpacingMm=20,
        int radialCount=4,double angleSpacingDeg=90,int planeNumber=1,double centerXmm=0,double centerYmm=0,double centerZmm=0,double rectangleAngleDeg=0,string featureName="")
    {
        foreach(var v in new[]{xSpacingMm,ySpacingMm,angleSpacingDeg,centerXmm,centerYmm,centerZmm,rectangleAngleDeg})CadFinite(v,"pattern value");
        if(patternType is not ("rectangular" or "circular"))throw new ArgumentException("patternType: rectangular/circular");
        if(patternType=="rectangular"&&(xCount<1||yCount<1||(long)xCount*yCount is <2 or >256||xCount>1&&xSpacingMm<=0||yCount>1&&ySpacingMm<=0))throw new ArgumentException("2..256 instances with positive active-axis spacing required");
        if(patternType=="circular"&&(radialCount is <2 or >256||angleSpacingDeg<=0||(radialCount-1)*angleSpacingDeg>=360-1e-8))throw new ArgumentException("2..256 circular instances; positive angle, no duplicate 360-degree endpoint");
        var doc=DetailPart(expectedDocument);DetailHealthy(doc);var model=GetItem(Get(doc,"Models"),1);var holes=Get(model,"Holes");
        var source=Enumerable.Range(1,Convert.ToInt32(Get(holes,"Count"))).Select(i=>GetItem(holes,i)).Single(h=>Convert.ToString(Get(h,"Name"))==sourceFeatureName);
        if(Convert.ToInt32(Get(Get(Get(source,"Profile"),"Holes2d"),"Count"))!=1)throw new InvalidOperationException("Pattern seed must be a single native hole");
        CadValidateFeatureName(Get(doc,"Models"),featureName);var plane=CadPlane(doc,planeNumber);var patterns=Get(model,"Patterns");int count=Convert.ToInt32(Get(patterns,"Count"));
        var features=Array.CreateInstance(typeof(DispatchWrapper),[1],[1]);features.SetValue(new DispatchWrapper(source),1);
        try {
            var pattern=patternType=="rectangular"?Call(patterns,"AddByRectangular",1,features,plane,xCount,yCount,xSpacingMm/1000,ySpacingMm/1000,rectangleAngleDeg*Math.PI/180,2,1)
                :Call(patterns,"AddByCircular",1,features,plane,radialCount,angleSpacingDeg*Math.PI/180,new[]{centerXmm/1000,centerYmm/1000,centerZmm/1000},2,2,true);
            if(featureName!="")Set(pattern,"Name",featureName);Call(doc,"Recompute");DetailHealthy(doc);
            int instances=Convert.ToInt32(Get(pattern,"NumberOfOccurrences"));
            if(instances!=(patternType=="rectangular"?xCount*yCount:radialCount))throw new InvalidOperationException("Native instance count differs");
            var array=GetItem(Get(Get(pattern,"Profile"),patternType=="rectangular"?"RectangularPatterns2d":"CircularPatterns2d"),1);
            if(patternType=="rectangular") {if(Convert.ToInt32(Get(array,"XCount"))!=xCount||Convert.ToInt32(Get(array,"YCount"))!=yCount||xCount>1&&Math.Abs(Convert.ToDouble(Get(array,"XSpace"))*1000-xSpacingMm)>1e-5||yCount>1&&Math.Abs(Convert.ToDouble(Get(array,"YSpace"))*1000-ySpacingMm)>1e-5)throw new InvalidOperationException("Native pattern counts or spacing not retained");}
            else {if(Convert.ToInt32(Get(array,"Count"))!=radialCount||Math.Abs(Convert.ToDouble(Get(array,"AngularSpacing"))*180/Math.PI-angleSpacingDeg)>1e-5)throw new InvalidOperationException("Native circular count or spacing not retained");}
            return new {document=expectedDocument,featureName=Convert.ToString(Get(pattern,"Name")),sourceFeatureName,patternType,instanceCount=instances,allFeaturesOk=true,saved=false};
        }catch(Exception error){MachiningRollback(doc,patterns,count,null,error,"Hole pattern");throw;}
    }
    public static object CadChamferEdges(string expectedDocument,string edgeIdsJson,double setbackMm,string featureName="")
    {
        CadFinite(setbackMm,nameof(setbackMm),true);var ids=JsonSerializer.Deserialize<string[]>(edgeIdsJson)??throw new ArgumentException("edgeIdsJson required");
        if(ids.Length is <1 or >256||ids.Distinct().Count()!=ids.Length)throw new ArgumentException("Select 1..256 distinct current edges");
        var doc=DetailPart(expectedDocument);DetailHealthy(doc);var model=GetItem(Get(doc,"Models"),1);CadValidateFeatureName(Get(doc,"Models"),featureName);
        var edges=DrawingIndexed(Get(model,"Body"),"Edges",1);var current=Enumerable.Range(1,Convert.ToInt32(Get(edges,"Count"))).Select(i=>GetItem(edges,i)).ToDictionary(e=>Convert.ToBase64String(ReferenceKey(e)));
        if(ids.Any(id=>!current.ContainsKey(id)))throw new InvalidOperationException("Edge identity no longer resolves; re-read list_edges");
        var set=Array.CreateInstance(typeof(DispatchWrapper),[ids.Length],[1]);for(int i=0;i<ids.Length;i++)set.SetValue(new DispatchWrapper(current[ids[i]]),i+1);
        var chamfers=Get(model,"Chamfers");int count=Convert.ToInt32(Get(chamfers,"Count"));
        try{var chamfer=Call(chamfers,"AddEqualSetback",ids.Length,set,setbackMm/1000);if(featureName!="")Set(chamfer,"Name",featureName);DetailHealthy(doc);return new {document=expectedDocument,featureName=Convert.ToString(Get(chamfer,"Name")),setbackMm,edgeCount=ids.Length,allFeaturesOk=true,saved=false};}
        catch(Exception error){MachiningRollback(doc,chamfers,count,null,error,"Chamfer");throw;}
    }
    public static object CadListProfileDimensions(string expectedDocument,string featureName)
    {
        var doc=DetailPart(expectedDocument,false);var dims=Get(Get(MachiningFeature(doc,featureName),"Profile"),"Dimensions");
        return new {document=expectedDocument,featureName,dimensions=Enumerable.Range(1,Convert.ToInt32(Get(dims,"Count"))).Select(i=>{var d=GetItem(dims,i);int type=Convert.ToInt32(Get(d,"DimensionType"));double value=Convert.ToDouble(Get(d,"Value"));return new{number=i,variableName=Convert.ToString(Get(d,"VariableTableName")),valueMm=type is 1 or 5?value*1000:(double?)null,nativeValue=value,supported=type is 1 or 5,driving=Convert.ToBoolean(Get(d,"Constraint")),dimensionType=type};}).ToArray()};
    }
    public static object CadSetProfileDimension(string expectedDocument,string featureName,string variableName,double valueMm)
    {
        CadFinite(valueMm,nameof(valueMm),true);var doc=DetailPart(expectedDocument);DetailHealthy(doc);var profile=Get(MachiningFeature(doc,featureName),"Profile");var dims=Get(profile,"Dimensions");
        var dim=Enumerable.Range(1,Convert.ToInt32(Get(dims,"Count"))).Select(i=>GetItem(dims,i)).Single(d=>Convert.ToString(Get(d,"VariableTableName"))==variableName);
        if(Convert.ToInt32(Get(dim,"DimensionType")) is not (1 or 5))throw new InvalidOperationException("Only linear length and circular diameter dimensions supported; angular/chamfer/other values unchanged");
        if(!Convert.ToBoolean(Get(dim,"Constraint")))throw new InvalidOperationException("Dimension is not driving");double old=Convert.ToDouble(Get(dim,"Value"));
        try{Set(dim,"Value",valueMm/1000);Call(doc,"Recompute");DetailHealthy(doc);if(Math.Abs(Convert.ToDouble(Get(dim,"Value"))-valueMm/1000)>1e-8)throw new InvalidOperationException("Requested dimension not retained");}
        catch(Exception error){try{Set(dim,"Value",old);Call(doc,"Recompute");DetailHealthy(doc);if(Math.Abs(Convert.ToDouble(Get(dim,"Value"))-old)>1e-8)throw new InvalidOperationException("Original dimension not restored");}catch(Exception rollback){throw new InvalidOperationException("Dimension edit failed; ROLLBACK FAILED: "+rollback.Message,error);}throw new InvalidOperationException("Dimension edit failed; original value restored, document may be dirty: "+(error.InnerException??error).Message,error);}
        return CadListProfileDimensions(expectedDocument,featureName);
    }
}
