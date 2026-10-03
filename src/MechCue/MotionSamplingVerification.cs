using System.Text.Json;
namespace MechCue;
public sealed partial class Bridge
{
    public static void VerifyMotionSamplingInSolidEdge(string outputDirectory)
    {
        using var busyRetry=new CadBusyRetry();
        outputDirectory=Path.GetFullPath(outputDirectory);
        if(Directory.Exists(outputDirectory))throw new IOException("Use a new output directory");
        Directory.CreateDirectory(outputDirectory);
        var application=CadApplication();var original=Get(application,"ActiveDocument");bool dirty=Convert.ToBoolean(Get(original,"Dirty"));
        var checks=new List<string>();
        void RetryBusy(Action action){for(int attempt=0;;attempt++){try{action();return;}catch(Exception error)when((error.InnerException??error).HResult==unchecked((int)0x80010001)&&attempt<30){Application.DoEvents();Thread.Sleep(100);}}}
        void Verify(bool condition,string message){if(!condition)throw new Exception(message);checks.Add(message);}
        string Name()=>CadName(Get(application,"ActiveDocument"));
        try {
            CadNew("part",@"C:\Siemens\Solid Edge 2026\Template\ISO Metric\iso metric part.par");
            // Offset the body from its part origin so rotation has substantial surface travel.
            CadExtrude(Name(),"rectangle",10,10,0,10,xMm:1000,yMm:-5);
            string part=Path.Combine(outputDirectory,"block.par");CadSave(Name(),part);CadCloseDocument(part,CadName(original));
            CadNew("assembly",@"C:\Siemens\Solid Edge 2026\Template\ISO Metric\iso metric assembly.asm");
            CadPlacePart(Name(),part,xMm:-30);CadPlacePart(Name(),part);
            string assembly=Path.Combine(outputDirectory,"sampling.asm");CadSave(Name(),assembly);
            var document=Get(application,"ActiveDocument");var moving=CadOccurrence(document,1);
            var bridge=new Bridge(application,document);
            var track=new Track{Kind="部品座標",Axis="X",Points=[new(0,-30),new(1,30)]};
            bridge.Bind(track,new Target("Moving",moving,"Matrix"));
            var clearance=bridge.CheckMotionClearance(21);
            Verify(clearance.AnalysisComplete&&!clearance.Clear&&Math.Abs(clearance.MinimumMm!.Value-20)<1e-5,"Native minimum distance detects 20 mm gap against 21 mm requirement");
            var gapReport=JsonSerializer.SerializeToElement(bridge.CheckMotionSamples([0],clearanceMm:21));
            Verify(!gapReport.GetProperty("allSamplesClear").GetBoolean()&&gapReport.GetProperty("samplingComplete").GetBoolean(),"Motion report records clearance shortfall with completed native analysis");
            Verify(bridge.MotionSurfaceRadiusMm(0,1)>30,"Surface sampling includes actual occurrence extent");
            var fixedResult=JsonSerializer.SerializeToElement(bridge.CheckMotionSamples([0,1]));
            Verify(fixedResult.GetProperty("allSamplesClear").GetBoolean(),"One-second endpoint inspection misses interior collision");
            var samples=bridge.PlanMotionSamples(0,1,1,5,1,5001);
            var adaptive=JsonSerializer.SerializeToElement(bridge.CheckMotionSamples(samples,true,true));
            Verify(!adaptive.GetProperty("allSamplesClear").GetBoolean(),"Adaptive inspection detects native interior collision");
            Verify(adaptive.GetProperty("analysisComplete").GetBoolean(),"Native interference analysis completed");
            bool stopped=false;try{bridge.ApplyCheckedPath(0,1);}catch(InvalidOperationException){stopped=true;}
            Verify(stopped,"Checked playback stops on native interior collision");
            double[] matrix=Matrix(moving);
            Verify(Math.Abs(matrix[12]+.03)<1e-8,"Checked playback restores interval start pose");
            bridge.Unbind(track);
            var rotation=new Track{Kind="部品回転",Axis="Z",Points=[new(0,0),new(1,1)]};bridge.Bind(rotation,new Target("Offset rotation",moving,"Matrix"));
            int angularOnly=bridge.PlanMotionSamples(0,1,1,5,1,5001,false).Length;
            int surfaceSamples=bridge.PlanMotionSamples(0,1,1,5,1,5001,true).Length;
            Verify(surfaceSamples>angularOnly,"Native 1 m offset body increases sample density for one-degree rotation");bridge.Unbind(rotation);
            using(var ui=new MainForm())ui.VerifyNativeMotionInspectionUi(application,document,outputDirectory);
            Verify(true,"UI inspects all motion patterns, records full paths and exports results; active chart preserved");
            RetryBusy(()=>CadSave(assembly));RetryBusy(()=>CadCloseDocument(assembly,CadName(original)));
            Verify(Convert.ToBoolean(Get(original,"Dirty"))==dirty,"Original document dirty state unchanged");
            File.WriteAllText(Path.Combine(outputDirectory,"results.json"),JsonSerializer.Serialize(new{checks,fixedResult,adaptive},new JsonSerializerOptions{WriteIndented=true}));
        }
        catch(Exception error){File.WriteAllText(Path.Combine(outputDirectory,"error.txt"),error.ToString());throw;}
        finally{RetryBusy(()=>CadActivateDocument(original));}
    }
}
