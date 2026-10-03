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
