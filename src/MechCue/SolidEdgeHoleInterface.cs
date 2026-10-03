namespace MechCue;
public sealed partial class Bridge
{
    public static object CadCheckHoleInterface(string expectedDocument,string firstKeyPath,string firstHoleName,string secondKeyPath,string secondHoleName,
        double boltDiameterMm,double stackThicknessMm,double boltUnderHeadLengthMm,double requiredEngagementMm,double headDiameterMm=0,double headHeightMm=0,double alignmentToleranceMm=.05)
    {
        object ReadProperty(object item,string name){try{return Get(item,name);}catch(Exception error){throw new InvalidOperationException("Hole interface property "+name+": "+(error.InnerException??error).Message,error);}}
        foreach(var v in new[]{boltDiameterMm,stackThicknessMm,boltUnderHeadLengthMm,requiredEngagementMm,headDiameterMm,headHeightMm,alignmentToleranceMm})CadFinite(v,"interface dimension");
        if(boltDiameterMm<=0||stackThicknessMm<=0||boltUnderHeadLengthMm<=0||requiredEngagementMm<=0||headDiameterMm<0||headHeightMm<0||alignmentToleranceMm<=0||firstKeyPath==secondKeyPath)throw new ArgumentException("Positive bolt/stack/length/engagement/tolerance and distinct leaf keys required");
        var application=CadApplication();var document=CadDocument(application,expectedDocument,".asm",false);var leaves=new Bridge(application,document).MotionParts(true);
        (object Hole,double[] Entry,double[] Axis) Hole(MotionInspectionPart leaf,string name){
            object Read(Func<object> read,string stage){try{return read();}catch(Exception error){throw new InvalidOperationException("Hole interface "+leaf.Path+" "+name+" "+stage+": "+(error.InnerException??error).Message,error);}}
            var part=Read(()=>ReadProperty(leaf.Chain[^1],"OccurrenceDocument"),"referenced document");if(CadExtension(part)!=".par"||Convert.ToInt32(Read(()=>ReadProperty(part,"ModelingMode"),"modeling mode"))!=2||Convert.ToInt32(Read(()=>ReadProperty(ReadProperty(part,"Models"),"Count"),"models"))!=1)throw new InvalidOperationException("Ordered single-model leaf parts required");
            var collection=Read(()=>ReadProperty(GetItem(ReadProperty(part,"Models"),1),"Holes"),"holes");var hole=Enumerable.Range(1,Convert.ToInt32(ReadProperty(collection,"Count"))).Select(i=>GetItem(collection,i)).Single(h=>Convert.ToString(ReadProperty(h,"Name"))==name);
            var profile=Read(()=>ReadProperty(hole,"Profile"),"profile");var centers=Read(()=>ReadProperty(profile,"Holes2d"),"profile holes");if(Convert.ToInt32(Read(()=>ReadProperty(centers,"Count"),"profile center count"))!=1)throw new InvalidOperationException("Single seed center required; pattern instances must be checked separately");
            var center=(double[])Read(()=>FeaturePoint(GetItem(centers,1),"GetCenterPoint"),"center");var plane=Read(()=>ReadProperty(profile,"Plane"),"plane");var origin=(double[])Read(()=>ConceptPlaneVector(plane,"GetRootPoint"),"plane origin");var n=(double[])Read(()=>ConceptPlaneVector(plane,"GetNormal"),"plane normal");var u=(double[])Read(()=>ConceptPlaneVector(plane,"GetReferenceDirection"),"plane direction");double[] v=[n[1]*u[2]-n[2]*u[1],n[2]*u[0]-n[0]*u[2],n[0]*u[1]-n[1]*u[0]];
            var local=Enumerable.Range(0,3).Select(k=>origin[k]+u[k]*center[0]/1000+v[k]*center[1]/1000).ToArray();var m=leaf.WorldMatrix;
            double[] World(double[] point,bool direction)=>Enumerable.Range(0,3).Select(k=>m[k]*point[0]+m[4+k]*point[1]+m[8+k]*point[2]+(direction?0:m[12+k])).ToArray();
            var axis=World(n,true);int side=Convert.ToInt32(Read(()=>ReadProperty(hole,"ExtentSide"),"extent side"));if(side is not (1 or 2))throw new InvalidOperationException("One-direction holes required");return(hole,World(local,false).Select(v=>v*1000).ToArray(),axis.Select(v=>side==1?-v:v).ToArray());
        }
        var a=leaves.Single(p=>p.KeyPath==firstKeyPath);var b=leaves.Single(p=>p.KeyPath==secondKeyPath);var first=Hole(a,firstHoleName);var second=Hole(b,secondHoleName);
        double dot=first.Axis.Zip(second.Axis,(x,y)=>x*y).Sum();var delta=first.Entry.Zip(second.Entry,(x,y)=>y-x).ToArray();double axial=delta.Zip(first.Axis,(x,y)=>x*y).Sum();double radial=Math.Sqrt(delta.Select((v,k)=>Math.Pow(v-axial*first.Axis[k],2)).Sum());
        var clearance=ReadProperty(first.Hole,"HoleData");var tapped=ReadProperty(second.Hole,"HoleData");bool threaded=Convert.ToInt32(ReadProperty(tapped,"TreatmentType"))==37;var thread=threaded?MachiningThreads().Entries.SingleOrDefault(t=>t.Description==Convert.ToString(ReadProperty(tapped,"ThreadDescription"))):null;
        double availableLength=boltUnderHeadLengthMm-stackThicknessMm;int extent=Convert.ToInt32(ReadProperty(second.Hole,"ExtentType"));double? holeDepth=extent==13?Convert.ToDouble(ReadProperty(second.Hole,"Depth"))*1000:null;
        double? threadDepth=threaded&&Convert.ToInt32(ReadProperty(tapped,"ThreadDepthMethod"))==13?Convert.ToDouble(ReadProperty(tapped,"ThreadDepth"))*1000:holeDepth;
        int type=Convert.ToInt32(ReadProperty(clearance,"HoleType"));bool headSpecified=headDiameterMm>0&&headHeightMm>0;
        bool? headFits=headSpecified&&type==34?headDiameterMm<=Convert.ToDouble(ReadProperty(clearance,"CounterboreDiameter"))*1000&&headHeightMm<=Convert.ToDouble(ReadProperty(clearance,"CounterboreDepth"))*1000:null;
        var checks=new Dictionary<string,bool?>{
            ["coaxial"]=Math.Abs(Math.Abs(dot)-1)<1e-6&&radial<=alignmentToleranceMm,
            ["entryAlongBoltAxis"]=axial>=-alignmentToleranceMm,
            ["clearanceDiameter"]=Convert.ToDouble(ReadProperty(clearance,"HoleDiameter"))*1000>boltDiameterMm&&Convert.ToInt32(ReadProperty(clearance,"TreatmentType"))!=37,
            ["threadNominalDiameter"]=thread!=null&&Math.Abs(thread.NominalDiameterMm-boltDiameterMm)<1e-5,
            ["requiredEngagement"]=threadDepth.HasValue?Math.Min(availableLength,threadDepth.Value)>=requiredEngagementMm:null,
            ["doesNotBottomOut"]=holeDepth.HasValue?availableLength<holeDepth.Value:null,
            ["headFitsCounterbore"]=headFits};
        return new{document=expectedDocument,first=new{a.Path,a.KeyPath,a.Name,hole=firstHoleName,entryMm=first.Entry,axis=first.Axis},second=new{b.Path,b.KeyPath,b.Name,hole=secondHoleName,entryMm=second.Entry,axis=second.Axis},
            radialOffsetMm=radial,axialSeparationMm=axial,axisDot=dot,boltDiameterMm,stackThicknessMm,boltUnderHeadLengthMm,availableEngagementMm=availableLength,requiredEngagementMm,checks,
            allSpecifiedChecksPass=checks.Values.All(v=>v==true),assessmentComplete=checks.Values.All(v=>v.HasValue),
            scope="Read-only seed-hole geometric/dimensional screening; caller supplies effective stack length and required engagement. Full-thread through-hole length, unspecified/non-counterbore head seats remain unknown. Does not create constraints, bolts or certify strength/tolerance/tool access."};
    }
}
