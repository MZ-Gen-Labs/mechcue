using System.Text.Json;
namespace MechCue;
public partial class MainForm
{
    internal void VerifyMotionInspectionUi()
    {
        void Assert(bool ok,string message){if(!ok)throw new Exception(message);}
        var sample=JsonSerializer.SerializeToElement(new{time=0,passed=true,analysisComplete=true});
        var native=new Bridge();
        var full=native.MotionInspectionSummary([0,4],[sample,sample],true,1,5,1,true,5,1,true);
        RecordMotionInspection(full);
        Assert(InspectionState(motionReports[^1])=="全区間の検査点で問題なし","Complete report not distinguished");
        var partial=native.MotionInspectionSummary([0,4],[sample],true,1,5,1,true,5,1,true,true);
        RecordMotionInspection(partial);
        Assert(InspectionState(motionReports[^1]).Contains("未確認"),"Cancellation hides uninspected interval");
        var overview=JsonSerializer.SerializeToElement(InspectionReports(JsonSerializer.SerializeToElement(new{limit=1})));
        Assert(overview.GetProperty("records").GetArrayLength()==1&&overview.GetProperty("nextOffset").GetInt32()==1,"Report overview pagination lost records");
        Assert(!overview.GetProperty("records")[0].GetProperty("Result").TryGetProperty("samples",out _),"Overview contains bulk sample data");
        var page=JsonSerializer.SerializeToElement(InspectionReports(JsonSerializer.SerializeToElement(new{reportId=motionReports[0].Id,includeSamples=true,sampleOffset=1,sampleLimit=1})));
        Assert(page.GetProperty("records")[0].GetProperty("Result").GetProperty("samples").GetArrayLength()==1,"Detailed sample paging lost its offset");
        var failing=JsonSerializer.SerializeToElement(new{time=0,passed=false,analysisComplete=true,analysis=new{pairs=new[]{new{Part1="Motor",Part2="Table",NativeStatus=2,ClearanceShortfall=false}}}});
        var failureReport=native.MotionInspectionSummary([0],[failing],false,1,5,1,false,5,0,true);
        Assert(InspectionDetails(new(Guid.NewGuid(),Guid.NewGuid(),"Failure","fixture.asm","",DateTimeOffset.Now,0,failureReport)).Contains("Motor ↔ Table [専用干渉]"),"Native leaf pair names/classification missing from details");
        InspectionDialogTestHook=(dialog,run,all)=>dialog.Shown+=(_,_)=>{
            try {var table=dialog.Controls.OfType<DataGridView>().Single();Assert(table.Rows.Count==2,"Results dialog lost history");using var image=new Bitmap(dialog.Width,dialog.Height);dialog.DrawToBitmap(image,new Rectangle(Point.Empty,dialog.Size));image.Save(Path.Combine(AppContext.BaseDirectory,"motion-inspection-preview.png"));}
            finally{dialog.Close();}
        };
        try{ShowMotionInspection();}finally{InspectionDialogTestHook=null;motionReports.Clear();}
        JsonElement PlaybackReport(double start,double end,bool verified){
            double[] times=start==end?[start]:[start,end];var values=times.Select(t=>JsonSerializer.SerializeToElement(new{time=t,passed=true,analysisComplete=true})).ToList();
            var fields=native.MotionInspectionSummary(times,values,true,1,5,1,true,5,1,false).EnumerateObject().ToDictionary(p=>p.Name,p=>p.Value.Clone());
            fields["continuousPathCertified"]=JsonSerializer.SerializeToElement(verified);fields["inspectionPolicy"]=JsonSerializer.SerializeToElement(new MotionInspectionPolicy());fields["baseSampleTimes"]=JsonSerializer.SerializeToElement(times);fields["baseSampleCount"]=JsonSerializer.SerializeToElement(times.Length);
            fields["continuousVerification"]=JsonSerializer.SerializeToElement(new{Requested=true,NumericalMarginMm=.01,UnverifiedCount=verified?0:1,Segments=start==end?Array.Empty<ContinuousMotionSegment>():[new(start,end,verified?"verified-clear":"unverified",null,"test")]});return JsonSerializer.SerializeToElement(fields);
        }
        RecordMotionInspection(PlaybackReport(0,0,false));RecordMotionInspection(PlaybackReport(0,4,true),merge:true);
        Assert(motionReports.Count==1&&motionReports[0].Result.GetProperty("continuousPathCertified").GetBoolean(),"Initial checked pose incorrectly blocks a verified playback interval");motionReports.Clear();
        RecordMotionInspection(PlaybackReport(0,2,false));RecordMotionInspection(PlaybackReport(2,4,true),merge:true);
        Assert(motionReports.Count==1&&!motionReports[0].Result.GetProperty("continuousPathCertified").GetBoolean(),"Verified later interval erased an earlier unverified playback interval");
        Assert(motionReports[0].Result.GetProperty("baseSampleTimes").GetArrayLength()==3,"Merged report lost its earlier base sampling times");motionReports.Clear();
        VerifyArchivedReportUi();
    }
    void VerifyArchivedReportUi()
    {
        var samples=new MotionInspectionSeries("samples",200,1000000);var segments=new MotionInspectionSeries("segments",200,1000000);
        for(int i=0;i<137;i++)samples.Add(new{time=i,passed=i!=41,analysisComplete=true,payload=new string('x',100)});
        for(int i=0;i<136;i++)segments.Add(new ContinuousMotionSegment(i,i+1,"unverified",null,"fixture",1));
        var result=JsonSerializer.SerializeToElement(new{samples=samples.Preview(),samplesTruncated=true,segmentsTruncated=true,checkedSampleCount=137,plannedSampleCount=137,startTime=0,endTime=136,analysisComplete=true,samplingComplete=true,allSamplesClear=false,continuousPathCertified=false,requiredClearanceMm=0,detailArchive=new{samples=samples.Manifest(),segments=segments.Manifest()},continuousVerification=new{Requested=true,NumericalMarginMm=.01,Segments=segments.Preview()}});
        RecordMotionInspection(result);
        var page=JsonSerializer.SerializeToElement(InspectionReports(JsonSerializer.SerializeToElement(new{reportId=motionReports[0].Id,includeSamples=true,sampleOffset=130,sampleLimit=10})));
        var record=page.GetProperty("records")[0].GetProperty("Result");
        if(record.GetProperty("totalSampleCount").GetInt32()!=137||record.GetProperty("samples").GetArrayLength()!=7||record.GetProperty("samples")[0].GetProperty("time").GetInt32()!=130||record.GetProperty("continuousVerification").GetProperty("Segments").GetArrayLength()!=6)throw new Exception("MCP paging lost archived samples or intervals beyond the preview");
        string path=Path.Combine(AppContext.BaseDirectory,"archived-ui-report.json");ExportMotionInspection(path);
        using(var exported=JsonDocument.Parse(File.ReadAllText(path))){var full=exported.RootElement.GetProperty("records")[0].GetProperty("Result");if(full.GetProperty("samples").GetArrayLength()!=137||full.GetProperty("continuousVerification").GetProperty("Segments").GetArrayLength()!=136||full.TryGetProperty("detailArchive",out _))throw new Exception("Portable JSON export lost archived details or retained machine-local dependencies");}
        foreach(var retained in motionReports)MotionInspectionSeries.DeleteOwnedArchives(retained.Result);motionReports.Clear();
    }
    internal void VerifyNativeMotionInspectionUi(object application,object document,string outputDirectory)
    {
        using var form=new MainForm(hostedApplication:application);form.Show();Application.DoEvents();if(!form.bridge.Connected)form.bridge.Connect();form.documentReady=false;
        form.tracks.Clear();var track=new Track{Kind="部品座標",Axis="X",Points=[new(0,-30),new(1,30)]};form.tracks.Add(track);form.RefreshTracks(0);form.ResetPatterns();
        var occurrence=document.GetType().InvokeMember("Occurrences",System.Reflection.BindingFlags.GetProperty,null,document,null)!;
        var moving=occurrence.GetType().InvokeMember("Item",System.Reflection.BindingFlags.GetProperty|System.Reflection.BindingFlags.InvokeMethod,null,occurrence,[1])!;
        form.bridge.Bind(track,new Target("Moving",moving,"Matrix"));
        JsonElement Command(string method,object args)=>JsonSerializer.SerializeToElement(form.HandleAi(JsonSerializer.SerializeToElement(new{method,args})));
        if(Command("list_inspection_parts",new{}).GetProperty("parts").GetArrayLength()!=2)throw new Exception("MCP inspection part enumeration failed");
        Command("set_inspection_policy",new{policy=new MotionInspectionPolicy()});
        var initial=form.activePattern;var safe=form.CreatePattern("Safe path",true);track.Points=[new(0,-30),new(1,-40)];form.RefreshTracks(0);form.StoreActivePattern();form.SwitchPattern(initial);
        Exception? failure=null;
        form.InspectionDialogTestHook=(dialog,run,all)=>dialog.Shown+=async(_,_)=>{
            try {
                all.PerformClick();var started=DateTime.UtcNow;
                while(form.motionInspectionBusy){if(DateTime.UtcNow-started>TimeSpan.FromSeconds(180))throw new Exception("Native UI inspection timeout");await Task.Delay(20);}
                if(form.motionReports.Count!=2||form.motionReports[0].Result.GetProperty("allSamplesClear").GetBoolean()||!form.motionReports[1].Result.GetProperty("allSamplesClear").GetBoolean()||form.motionReports.Any(r=>!r.Result.GetProperty("samplingComplete").GetBoolean()))throw new Exception("UI batch failed to distinguish collision and safe paths");
                if(form.activePattern!=initial||track.Points[^1].Value!=30)throw new Exception("UI inspection changed active chart");
                if(!form.motionReports[1].Result.GetProperty("continuousPathCertified").GetBoolean())throw new Exception("Safe path not certified in the native UI");
                if(Command("get_inspection_reports",new{}).GetProperty("records").GetArrayLength()!=2)throw new Exception("MCP reports lost UI inspections");
                form.ExportMotionInspection(Path.Combine(outputDirectory,"ui-reports.json"));
                using var image=new Bitmap(dialog.Width,dialog.Height);dialog.DrawToBitmap(image,new Rectangle(Point.Empty,dialog.Size));image.Save(Path.Combine(outputDirectory,"ui-results.png"));
            }
            catch(Exception error){failure=error;}
            finally{dialog.Close();while(form.motionInspectionBusy)await Task.Delay(20);dialog.Close();}
        };
        form.ShowMotionInspection();form.bridge.Unbind(track);form.bridge.Disconnect();form.Close();if(failure!=null)throw failure;
    }
}
