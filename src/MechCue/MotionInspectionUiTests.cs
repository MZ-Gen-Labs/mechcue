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
    }
    internal void VerifyNativeMotionInspectionUi(object application,object document,string outputDirectory)
    {
        using var form=new MainForm(hostedApplication:application);form.Show();Application.DoEvents();if(!form.bridge.Connected)form.bridge.Connect();form.documentReady=false;
        form.tracks.Clear();var track=new Track{Kind="部品座標",Axis="X",Points=[new(0,-30),new(1,30)]};form.tracks.Add(track);form.RefreshTracks(0);form.ResetPatterns();
        var occurrence=document.GetType().InvokeMember("Occurrences",System.Reflection.BindingFlags.GetProperty,null,document,null)!;
        var moving=occurrence.GetType().InvokeMember("Item",System.Reflection.BindingFlags.GetProperty|System.Reflection.BindingFlags.InvokeMethod,null,occurrence,[1])!;
        form.bridge.Bind(track,new Target("Moving",moving,"Matrix"));
        var initial=form.activePattern;var safe=form.CreatePattern("Safe path",true);track.Points=[new(0,-30),new(1,-40)];form.RefreshTracks(0);form.StoreActivePattern();form.SwitchPattern(initial);
        Exception? failure=null;
        form.InspectionDialogTestHook=(dialog,run,all)=>dialog.Shown+=async(_,_)=>{
            try {
                all.PerformClick();var started=DateTime.UtcNow;
                while(form.motionInspectionBusy){if(DateTime.UtcNow-started>TimeSpan.FromSeconds(45))throw new Exception("Native UI inspection timeout");await Task.Delay(20);}
                if(form.motionReports.Count!=2||form.motionReports[0].Result.GetProperty("allSamplesClear").GetBoolean()||!form.motionReports[1].Result.GetProperty("allSamplesClear").GetBoolean()||form.motionReports.Any(r=>!r.Result.GetProperty("samplingComplete").GetBoolean()))throw new Exception("UI batch failed to distinguish collision and safe paths");
                if(form.activePattern!=initial||track.Points[^1].Value!=30)throw new Exception("UI inspection changed active chart");
                form.ExportMotionInspection(Path.Combine(outputDirectory,"ui-reports.json"));
                using var image=new Bitmap(dialog.Width,dialog.Height);dialog.DrawToBitmap(image,new Rectangle(Point.Empty,dialog.Size));image.Save(Path.Combine(outputDirectory,"ui-results.png"));
            }
            catch(Exception error){failure=error;}
            finally{dialog.Close();}
        };
        form.ShowMotionInspection();form.bridge.Unbind(track);form.bridge.Disconnect();form.Close();if(failure!=null)throw failure;
    }
}
