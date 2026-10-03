using System.Text.Json;
namespace MechCue;
public sealed partial class Bridge
{
    public static void VerifyAdvancedMotionInSolidEdge(string outputDirectory)
    {
        using var retry=new CadBusyRetry();outputDirectory=Path.GetFullPath(outputDirectory);if(Directory.Exists(outputDirectory))throw new IOException("Use a new directory");Directory.CreateDirectory(outputDirectory);
        object? app=null;try{app=CadApplication();}catch{CadStartApplication();}
        for(int attempt=0;app==null&&attempt<40;attempt++){Thread.Sleep(500);try{app=CadApplication();}catch{}}
        if(app==null)throw new InvalidOperationException("Solid Edge is not automation-ready");
        object? original=null;try{original=Get(app,"ActiveDocument");}catch{}bool dirty=original!=null&&Convert.ToBoolean(Get(original,"Dirty"));var checks=new List<string>();
        string BackName()=>original==null?"":CadName(original);
        string Name()=>CadName(Get(app,"ActiveDocument"));
        void Verify(bool ok,string message){if(!ok)throw new Exception(message);checks.Add(message);File.AppendAllText(Path.Combine(outputDirectory,"checks.txt"),message+Environment.NewLine);}
        try {
            CadNew("part",@"C:\Siemens\Solid Edge 2026\Template\ISO Metric\iso metric part.par");CadExtrude(Name(),"rectangle",10,10,0,10);
            string part=Path.Combine(outputDirectory,"block.par");CadSave(Name(),part);CadCloseDocument(part,BackName());
            CadNew("assembly",@"C:\Siemens\Solid Edge 2026\Template\ISO Metric\iso metric assembly.asm");CadPlacePart(Name(),part);CadPlacePart(Name(),part,xMm:20);
            string module=Path.Combine(outputDirectory,"module.asm");CadSave(Name(),module);CadCloseDocument(module,BackName());
            CadNew("assembly",@"C:\Siemens\Solid Edge 2026\Template\ISO Metric\iso metric assembly.asm");CadPlacePart(Name(),module);CadPlacePart(Name(),module,xMm:100,rzDeg:90);
            string assembly=Path.Combine(outputDirectory,"root.asm");CadSave(Name(),assembly);var document=Get(app,"ActiveDocument");var bridge=new Bridge(app,document);
            var leaves=bridge.MotionParts(true);Verify(leaves.Count==4&&leaves.Select(p=>p.KeyPath).Distinct().Count()==4,"Repeated nested assembly instances have distinct leaf identities");
            var internalGap=bridge.InspectMotionPairs(11,new());Verify(internalGap.AnalysisComplete&&internalGap.Clearance.Violations.Count==2,"Nested internal 10 mm gaps detected in both transformed instances");
            Verify(internalGap.Pairs.Count(p=>p.ExactDistanceMm is >=9.999 and <=10.001)==2,"Native reference distance resolves transformed leaf instances");
            var allowFirst=new MotionInspectionPolicy{AllowedContacts=[new(leaves[0].KeyPath,leaves[1].KeyPath,"Intentional fixture gap")]};
            var partial=bridge.InspectMotionPairs(11,allowFirst);Verify(!partial.Clear&&partial.Clearance.Violations.Count==1,"Contact exclusion applies to one exact instance only");
            var allowBoth=allowFirst with{AllowedContacts=[..allowFirst.AllowedContacts,new(leaves[2].KeyPath,leaves[3].KeyPath,"Intentional fixture gap")]};
            var clear=bridge.InspectMotionPairs(11,allowBoth);Verify(clear.AnalysisComplete&&clear.Clear,"Excluded intentional pairs do not block unrelated clear pairs");
            var moving=CadOccurrence(document,1);var track=new Track{Kind="部品座標",Axis="Y",Points=[new(0,0),new(1,20)]};bridge.Bind(track,new Target("Moving module",moving,"Matrix"));
            var continuous=bridge.CheckContinuousMotion([0,1],1);
            Verify(continuous.GetProperty("continuousPathCertified").GetBoolean(),"Nested rigid motion certified using relative travel; common parent motion cancels");
            Verify(Math.Abs(Matrix(moving)[13])<1e-8,"Continuous inspection restores original pose");
            var pointBefore=continuous.GetProperty("samples")[0].GetProperty("clearance").GetProperty("ClosestPair").GetProperty("Point1Mm")[1].GetDouble();
            var pointAfter=continuous.GetProperty("samples")[1].GetProperty("clearance").GetProperty("ClosestPair").GetProperty("Point1Mm")[1].GetDouble();
            Verify(Math.Abs(pointAfter-pointBefore-20)<1e-6,"Reused closest-point coordinates follow the common moving parent");
            track.Axis="X";track.Points=[new(0,0),new(1,160)];
            var crossing=bridge.CheckContinuousMotion([0,1],0,maxSamples:501);
            Verify(!crossing.GetProperty("allSamplesClear").GetBoolean()&&!crossing.GetProperty("continuousPathCertified").GetBoolean(),"Interval refinement detects collision missed by clear endpoints");
            var limited=bridge.CheckContinuousMotion([0,1],0,new MotionInspectionPolicy{MaxRefinementDepth=0},2);
            Verify(limited.GetProperty("allSamplesClear").GetBoolean()&&!limited.GetProperty("continuousPathCertified").GetBoolean(),"Unresolved interval cannot become a certificate at a refinement limit");
            bridge.Unbind(track);
            CadPlacePart(assembly,part,xMm:5);var collision=bridge.InspectMotionPairs(0,allowBoth);Verify(collision.AnalysisComplete&&!collision.Clear,"Unexcluded nested-to-root collision remains detectable");
            track.Axis="Y";track.Points=[new(0,0),new(1,20)];bridge.Bind(track,new Target("Moving module",moving,"Matrix"));
            var independent=bridge.CheckContinuousMotion([0,1],0,allowBoth,maxSamples:501);
            var individual=independent.GetProperty("continuousVerification").GetProperty("PairResults");
            Verify(!independent.GetProperty("continuousPathCertified").GetBoolean()&&individual.EnumerateArray().Any(p=>p.GetProperty("state").GetString()=="verified-clear")&&individual.EnumerateArray().Any(p=>p.GetProperty("state").GetString()=="sample-violation"),"Normal pairs remain continuously verified despite a different colliding pair");
            Verify(independent.GetProperty("fixedPairCacheHits").GetInt32()>0,"Fixed/common-motion pair measurements are reused within the guarded run");bridge.Unbind(track);
            var unsupportedTrack=new Track{Kind="距離拘束",Points=[new(0,0),new(1,1)]};
            bridge.Bind(unsupportedTrack,new Target("Unsupported constraint fixture",new SelfTest.FakeRelation(),"Offset"));
            var unsupported=bridge.CheckContinuousMotion([0,1],0,allowBoth,501);bridge.Unbind(unsupportedTrack);
            File.WriteAllText(Path.Combine(outputDirectory,"unsupported-model.json"),unsupported.GetRawText());
            Verify(!unsupported.GetProperty("continuousPathCertified").GetBoolean()&&unsupported.GetProperty("continuousVerification").GetProperty("PairResults").EnumerateArray().Any(p=>p.GetProperty("state").GetString()=="sample-violation"),"Unsupported continuous models still label the pairs with native pose violations");
            CadSave(assembly);CadCloseDocument(assembly,BackName());Verify(original==null||Convert.ToBoolean(Get(original,"Dirty"))==dirty,"Original document dirty state unchanged");
            CadNew("assembly",@"C:\Siemens\Solid Edge 2026\Template\ISO Metric\iso metric assembly.asm");CadPlacePart(Name(),part,xMm:-30);CadPlacePart(Name(),part);
            string playbackAssembly=Path.Combine(outputDirectory,"playback.asm");CadSave(Name(),playbackAssembly);var playbackDocument=Get(app,"ActiveDocument");var playbackBridge=new Bridge(app,playbackDocument);var playbackPart=CadOccurrence(playbackDocument,1);
            var playbackTrack=new Track{Kind="部品座標",Axis="X",Points=[new(0,-30),new(1,-40)]};playbackBridge.Bind(playbackTrack,new Target("Playback moving",playbackPart,"Matrix"));
            var playbackReports=new List<JsonElement>();playbackBridge.InspectionObserver=playbackReports.Add;playbackBridge.ApplyCheckedPath(0,1);
            Verify(Math.Abs(Matrix(playbackPart)[12]+.040)<1e-8&&playbackReports[^1].GetProperty("continuousPathCertified").GetBoolean(),"Checked playback reaches the final pose only after certifying the whole interval");
            var divided=playbackBridge.CheckContinuousMotion(Enumerable.Range(0,17).Select(i=>i/16d).ToArray(),1,maxSamples:2);
            Verify(divided.GetProperty("samplingComplete").GetBoolean()&&divided.GetProperty("continuousPathCertified").GetBoolean()&&divided.GetProperty("chunkCount").GetInt32()>1&&divided.GetProperty("checkedSampleCount").GetInt32()==17,"Automatic batches exceed the per-batch point limit without losing coverage or the global certificate");
            var capped=playbackBridge.CheckContinuousMotion([0,.5,1],1,new MotionInspectionPolicy{AutoSplit=true,MaxTotalSamples=3},2);
            Verify(capped.GetProperty("samplingComplete").GetBoolean()&&capped.GetProperty("chunkCount").GetInt32()>1,"Total sample cap remains independent of the working batch size");
            var bounded=playbackBridge.CheckContinuousMotion([0,1],1000,new MotionInspectionPolicy{AutoSplit=false},2);
            Verify(!bounded.GetProperty("continuousPathCertified").GetBoolean(),"Disabling automatic splitting preserves a bounded non-certificate on violations");
            var originalStamp=File.GetLastWriteTimeUtc(part);
            try{
                bool changed=false;
                var invalidated=playbackBridge.CheckContinuousMotionAsync([0,.5,1],1,new(),2,CancellationToken.None,(count,_)=>{if(!changed&&count==1){changed=true;File.SetLastWriteTimeUtc(part,originalStamp.AddSeconds(2));}}).GetAwaiter().GetResult();
                Verify(!invalidated.GetProperty("analysisComplete").GetBoolean()&&!invalidated.GetProperty("continuousPathCertified").GetBoolean()&&invalidated.GetProperty("error").GetString()!.Contains("geometry changed"),"Geometry file revision invalidates cached measurements and continuous certificates");
            }finally{File.SetLastWriteTimeUtc(part,originalStamp);}
            playbackBridge.ApplyCheckedPath(1,0);playbackTrack.Points=[new(0,-30),new(1,30)];bool stopped=false;try{playbackBridge.ApplyCheckedPath(0,1);}catch(InvalidOperationException){stopped=true;}
            Verify(stopped&&Math.Abs(Matrix(playbackPart)[12]+.030)<1e-8&&!playbackReports[^1].GetProperty("allSamplesClear").GetBoolean(),"Checked playback stops on interior collision and restores interval-start pose");
            var cancelled=playbackBridge.CheckContinuousMotion([0,1],0,new(),501,cancellation:new CancellationToken(true));
            Verify(!cancelled.GetProperty("samplingComplete").GetBoolean()&&!cancelled.GetProperty("continuousPathCertified").GetBoolean(),"Cancelled inspection cannot certify or hide uninspected intervals");
            var plainLeaves=playbackBridge.MotionParts(true);var excludeAll=new MotionInspectionPolicy{AllowedContacts=[new(plainLeaves[0].KeyPath,plainLeaves[1].KeyPath,"Fixture test only")]};
            var excluded=playbackBridge.CheckContinuousMotion([0,1],0,excludeAll,501);Verify(excluded.GetProperty("allSamplesClear").GetBoolean()&&!excluded.GetProperty("continuousPathCertified").GetBoolean(),"Excluding every comparison pair never produces a continuous certificate");
            var stale=excludeAll with{AllowedContacts=[new("/00",plainLeaves[1].KeyPath,"Stale test")]};bool refused=false;try{playbackBridge.CheckContinuousMotion([0,1],0,stale);}catch(InvalidOperationException){refused=true;}
            Verify(refused&&Math.Abs(Matrix(playbackPart)[12]+.030)<1e-8,"Stale contact keys are rejected before CAD movement");
            playbackBridge.Unbind(playbackTrack);
            using(var form=new MainForm())form.VerifyNativeMotionInspectionUi(app,playbackDocument,outputDirectory);
            Verify(File.Exists(Path.Combine(outputDirectory,"ui-reports.json")),"Native UI batch records collision and safe continuous results and restores the chart");
            var conceptModel=new ConceptMachine{Type="mill5",AssemblyFile="playback.asm"};string parent="";
            foreach(string id in new[]{"X","Y","Z","A","C"}){bool rotary=id is "A" or "C";conceptModel.Axes.Add(new(){Id=id,Parent=parent,Kind=rotary?"rotary":"linear",Direction=id=="A"?"X":id=="C"?"Z":id,Minimum=rotary?-360:-100,Maximum=rotary?360:100,OriginMm=id=="C"?[0,20,0]:[0,0,0]});conceptModel.Values[id]=0;parent=id;}
            var fixedPart=CadOccurrence(playbackDocument,2);
            conceptModel.Bodies=[new(){Id="moving",Parent="C",CenterMm=[30,0,0],SizeMm=[10,10,10],File="block.par",Occurrence=Convert.ToString(Get(playbackPart,"Name"))!},new(){Id="fixed",CenterMm=[-150,0,0],SizeMm=[10,10,10],File="block.par",Occurrence=Convert.ToString(Get(fixedPart,"Name"))!}];
            var home=conceptModel.Poses(conceptModel.Values);CadCallRef(playbackPart,"PutMatrix",[0],home["moving"],true);CadCallRef(fixedPart,"PutMatrix",[0],home["fixed"],true);
            var conceptBridge=new Bridge(app,playbackDocument);var conceptTracks=conceptModel.Axes.Select(a=>new Track{Name=a.Id,Kind=a.Kind=="rotary"?"部品回転":"部品移動",Axis=a.Direction,Points=[new(0,0),new(1,a.Id=="C"?360:a.Id=="A"?90:10)]}).ToArray();
            conceptBridge.RestoreConcept(new(){Model=conceptModel,Baseline=new(conceptModel.Values),Tracks=conceptTracks.ToDictionary(t=>t.Name,t=>t.Id)},conceptTracks);
            var fiveAxis=conceptBridge.CheckContinuousMotion([0,1],1,maxSamples:501);Verify(fiveAxis.GetProperty("continuousPathCertified").GetBoolean(),"Simultaneous XYZ/A/C concept motion including full C revolution has a continuous clearance certificate");
            Verify(ConceptSame(Matrix(playbackPart),home["moving"]),"Five-axis inspection restores actual concept body pose");
            CadSave(playbackAssembly);CadCloseDocument(playbackAssembly,BackName());
            File.WriteAllText(Path.Combine(outputDirectory,"results.json"),JsonSerializer.Serialize(new{checks,internalGap,partial,clear,continuous,crossing,limited,collision,independent,divided,capped,bounded,fiveAxis},new JsonSerializerOptions{WriteIndented=true}));
        }
        catch(Exception error){File.WriteAllText(Path.Combine(outputDirectory,"error.txt"),error.ToString());throw;}
        finally{if(original!=null)CadActivateDocument(original);}
    }
}
