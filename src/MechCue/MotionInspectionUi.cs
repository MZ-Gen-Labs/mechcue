using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace MechCue;

public sealed record RecordedMotionInspection(Guid Id,Guid PatternId,string PatternName,string Document,string ChartFingerprint,DateTimeOffset Created,double ExpectedEnd,JsonElement Result);
public partial class MainForm
{
    readonly List<RecordedMotionInspection> motionReports=new();
    bool motionInspectionBusy;
    double requiredMotionClearanceMm;
    internal Action<Form,Button,Button>? InspectionDialogTestHook;
    internal void ExportMotionInspection(string path)=>File.WriteAllText(path,JsonSerializer.Serialize(new{created=DateTimeOffset.Now,continuousPathCertified=false,records=motionReports},new JsonSerializerOptions{WriteIndented=true}));
    string InspectionFingerprint()=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(tracks.Select(t=>new{t.Id,t.Name,t.Kind,t.Axis,t.Points})))));
    string InspectionDocument()=>bridge.Connected?Convert.ToString(bridge.Document.GetType().InvokeMember("FullName",System.Reflection.BindingFlags.GetProperty,null,bridge.Document,null))??"":"";
    void RecordMotionInspection(JsonElement result,Guid? patternId=null,string? patternName=null,bool merge=false)
    {
        string fingerprint=InspectionFingerprint(),document=InspectionDocument();
        var previous=motionReports.LastOrDefault();
        if(merge&&previous!=null&&previous.PatternId==activePattern&&previous.ChartFingerprint==fingerprint&&previous.Document==document&&previous.Result.GetProperty("samplingComplete").GetBoolean()&&previous.Result.GetProperty("allSamplesClear").GetBoolean()&&
            previous.Result.GetProperty("endTime").GetDouble()==result.GetProperty("startTime").GetDouble()&&result.GetProperty("endTime").GetDouble()>=result.GetProperty("startTime").GetDouble()&&previous.Result.GetProperty("requiredClearanceMm").GetDouble()==result.GetProperty("requiredClearanceMm").GetDouble())
        {
            var combined=previous.Result.GetProperty("samples").EnumerateArray().Concat(result.GetProperty("samples").EnumerateArray().Skip(1)).ToList();
            if(combined.Count>10001)throw new InvalidOperationException("再生検査の記録上限10001点です。経路検査画面で区間を確認してください。");
            var fields=JsonSerializer.Deserialize<Dictionary<string,JsonElement>>(result.GetRawText())!;
            fields["samples"]=JsonSerializer.SerializeToElement(combined);fields["startTime"]=previous.Result.GetProperty("startTime");
            fields["plannedSampleCount"]=JsonSerializer.SerializeToElement(previous.Result.GetProperty("plannedSampleCount").GetInt32()+result.GetProperty("plannedSampleCount").GetInt32()-1);
            fields["checkedSampleCount"]=JsonSerializer.SerializeToElement(combined.Count);
            fields["allSamplesClear"]=JsonSerializer.SerializeToElement(combined.All(s=>s.GetProperty("passed").GetBoolean()));
            result=JsonSerializer.SerializeToElement(fields);motionReports.RemoveAt(motionReports.Count-1);
        }
        if(motionReports.Count>=100)motionReports.RemoveAt(0);
        motionReports.Add(new(Guid.NewGuid(),patternId??activePattern,patternName??ActivePattern.Name,document,fingerprint,DateTimeOffset.Now,tracks.Max(t=>t.Points[^1].Time),result));
    }
    static string InspectionState(RecordedMotionInspection record)
    {
        var r=record.Result;
        if(!r.GetProperty("analysisComplete").GetBoolean())return "解析未完了";
        if(!r.GetProperty("allSamplesClear").GetBoolean())return "干渉／すきま不足";
        if(!r.GetProperty("samplingComplete").GetBoolean()||r.GetProperty("startTime").GetDouble()!=0||r.GetProperty("endTime").GetDouble()<record.ExpectedEnd)return "一部検査・未確認区間あり";
        return "全区間の検査点で問題なし";
    }
    static string InspectionDetails(RecordedMotionInspection record)
    {
        var text=new StringBuilder();text.AppendLine($"{record.PatternName} — {InspectionState(record)}");text.AppendLine(record.Document);
        if(record.Result.TryGetProperty("error",out var error))text.AppendLine("検査できませんでした: "+error.GetString());
        foreach(var sample in record.Result.GetProperty("samples").EnumerateArray()) {
            text.Append($"{sample.GetProperty("time").GetDouble():0.######} s: "+(sample.GetProperty("passed").GetBoolean()?"検査点で問題なし":"要確認"));
            if(sample.TryGetProperty("clearance",out var gap)&&gap.ValueKind==JsonValueKind.Object) {
                if(gap.GetProperty("MinimumMm").ValueKind==JsonValueKind.Number)text.Append($" / 最小すきま {gap.GetProperty("MinimumMm").GetDouble():0.###} mm");
                if(gap.TryGetProperty("ClosestPair",out var closest)&&closest.ValueKind==JsonValueKind.Object)text.Append(" / "+closest.GetProperty("Part1").GetString()+" ↔ "+closest.GetProperty("Part2").GetString());
                if(gap.GetProperty("Error").ValueKind==JsonValueKind.String)text.Append(" / "+gap.GetProperty("Error").GetString());
            }
            if(sample.TryGetProperty("analysis",out var analysis)) {
                if(analysis.TryGetProperty("pairs",out var pairs))foreach(var pair in pairs.EnumerateArray())foreach(string side in new[]{"first","second"})if(pair.TryGetProperty(side,out var part)&&part.ValueKind==JsonValueKind.Object&&part.TryGetProperty("name",out var name))text.Append(" / "+name.GetString());
                if(analysis.TryGetProperty("error",out var issue))text.Append(" / "+issue.GetString());
            }
            text.AppendLine();
        }
        return text.ToString();
    }
    static JsonElement UninspectedMotionResult(double end,double clearance,string error,bool cancelled=false)=>JsonSerializer.SerializeToElement(new{samples=Array.Empty<object>(),allSamplesClear=false,samplingComplete=false,analysisComplete=false,plannedSampleCount=0,checkedSampleCount=0,startTime=0,endTime=end,requiredClearanceMm=clearance,continuousPathCertified=false,poseRestored=true,error,cancelled});
    void ShowMotionInspection()
    {
        PausePlayback();Commit();StoreActivePattern();
        using var dialog=new Form{Text=PatternText("経路検査・結果（離散検査）","Path inspection results (sampled)"),Width=1050,Height=680,StartPosition=FormStartPosition.CenterParent,MinimumSize=new(800,520)};
        var options=new FlowLayoutPanel{Dock=DockStyle.Top,Height=85,WrapContents=true};
        var gap=new NumericUpDown{DecimalPlaces=3,Minimum=0,Maximum=10000,Value=(decimal)requiredMotionClearanceMm,Increment=.1m,Width=80};
        var surface=new NumericUpDown{DecimalPlaces=3,Minimum=.001m,Maximum=1000,Value=5,Increment=1,Width=75};
        var maximum=new NumericUpDown{Minimum=2,Maximum=10001,Value=5001,Width=75};
        var run=new Button{Text=PatternText("選択経路を検査","Inspect selected"),AutoSize=true};
        var all=new Button{Text=PatternText("全動作パターンを検査","Inspect all motions"),AutoSize=true};
        var cancel=new Button{Text=PatternText("中止","Cancel"),Enabled=false,AutoSize=true};
        var save=new Button{Text=PatternText("結果をJSON保存","Save results JSON"),AutoSize=true};
        options.Controls.AddRange([new Label{Text=PatternText("要求すきま [mm]（0＝無効）","Clearance mm (0 = off)"),AutoSize=true},gap,new Label{Text=PatternText("表面移動量の目安 [mm]","Surface step estimate mm"),AutoSize=true},surface,new Label{Text=PatternText("経路あたり最大点数","Samples per motion"),AutoSize=true},maximum,run,all,cancel,save]);
        var notice=new Label{Dock=DockStyle.Bottom,Height=52,Text=PatternText("記録は検査時点のスナップショットです。CAD・グラフ変更後は再検査してください。連続経路の安全保証ではありません。すきま判定はトップ階層部品間が対象です。直近100件を保持します。","Snapshots only; reinspect after CAD/chart edits. No continuous guarantee. Clearance between top-level occurrences. Last 100 records retained.")};
        var table=new DataGridView{Dock=DockStyle.Fill,ReadOnly=true,AllowUserToAddRows=false,SelectionMode=DataGridViewSelectionMode.FullRowSelect,MultiSelect=false,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill};
        foreach(string name in new[]{"動作","判定","検査区間 [s]","点数","すきま [mm]","記録時刻"})table.Columns.Add(name,name);
        var detail=new TextBox{Dock=DockStyle.Bottom,Height=180,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Both,WordWrap=false};
        var progress=new Label{Dock=DockStyle.Top,Height=25};
        dialog.Controls.Add(table);dialog.Controls.Add(detail);dialog.Controls.Add(progress);dialog.Controls.Add(options);dialog.Controls.Add(notice);
        void RefreshResults(){table.Rows.Clear();foreach(var record in motionReports){var r=record.Result;int row=table.Rows.Add(record.PatternName,InspectionState(record),$"{r.GetProperty("startTime").GetDouble():0.###} – {r.GetProperty("endTime").GetDouble():0.###} / {record.ExpectedEnd:0.###}",$"{r.GetProperty("checkedSampleCount").GetInt32()} / {r.GetProperty("plannedSampleCount").GetInt32()}",r.GetProperty("requiredClearanceMm").GetDouble(),record.Created.LocalDateTime.ToString("HH:mm:ss"));table.Rows[row].Tag=record;}if(table.Rows.Count>0){table.CurrentCell=table.Rows[^1].Cells[0];detail.Text=InspectionDetails(motionReports[^1]);}}
        table.SelectionChanged+=(_,_)=>{if(table.CurrentRow?.Tag is RecordedMotionInspection record)detail.Text=InspectionDetails(record);};
        CancellationTokenSource? cancellation=null;
        cancel.Click+=(_,_)=>cancellation?.Cancel();
        gap.ValueChanged+=(_,_)=>{requiredMotionClearanceMm=(double)gap.Value;bridge.PlaybackClearanceMm=requiredMotionClearanceMm;lastCheckedTime=null;};
        save.Click+=(_,_)=>{using var file=new SaveFileDialog{Filter="JSON|*.json",FileName="MechCue-motion-inspection.json"};if(file.ShowDialog(dialog)==DialogResult.OK)ExportMotionInspection(file.FileName);};
        async Task Inspect(bool every)
        {
            cancellation=new();motionInspectionBusy=true;run.Enabled=all.Enabled=gap.Enabled=surface.Enabled=maximum.Enabled=save.Enabled=false;cancel.Enabled=true;
            var original=tracks.ToDictionary(t=>t.Id,t=>t.Points.ToList());
            using var reflection=PauseCadReflection();
            var observer=bridge.InspectionObserver;bridge.InspectionObserver=null;
            try {
                foreach(var pattern in every?patterns.ToArray():new[]{ActivePattern}) {
                    foreach(var t in tracks)t.Points=pattern.Points[t.Id].ToList();
                    double end=tracks.Max(t=>t.Points[^1].Time);
                    if(cancellation.IsCancellationRequested){RecordMotionInspection(UninspectedMotionResult(end,(double)gap.Value,"中止により未実行",true),pattern.Id,pattern.Name);RefreshResults();continue;}
                    double[] plan;
                    try {plan=bridge.PlanMotionSamples(0,end,1,5,1,(int)maximum.Value,true,(double)surface.Value);}
                    catch(Exception error){RecordMotionInspection(UninspectedMotionResult(end,(double)gap.Value,(error.InnerException??error).Message),pattern.Id,pattern.Name);RefreshResults();continue;}
                    var restore=bridge.CaptureVideoPose();var samples=new List<JsonElement>();bool cancelled=false;
                    try {
                        foreach(double moment in plan){if(cancellation.IsCancellationRequested){cancelled=true;break;}var sample=bridge.InspectMotionPose(moment,(double)gap.Value);samples.Add(sample);progress.Text=$"{pattern.Name}: {samples.Count}/{plan.Length} ({moment:0.###} s)";if(!sample.GetProperty("analysisComplete").GetBoolean())break;await Task.Delay(1);}
                    }
                    finally{restore();}
                    var result=bridge.MotionInspectionSummary(plan,samples,true,1,5,1,true,(double)surface.Value,(double)gap.Value,true,cancelled);
                    RecordMotionInspection(result,pattern.Id,pattern.Name);RefreshResults();
                }
                progress.Text=cancellation.IsCancellationRequested?PatternText("中止・未検査区間あり","Cancelled; uninspected interval remains"):PatternText("検査終了。判定と点数を確認してください。","Inspection ended; review status and counts.");
            }
            catch(Exception error){progress.Text=PatternText("検査未完了: ","Inspection incomplete: ")+(error.InnerException??error).Message;}
            finally{foreach(var t in tracks)t.Points=original[t.Id];bridge.InspectionObserver=observer;motionInspectionBusy=false;run.Enabled=all.Enabled=gap.Enabled=surface.Enabled=maximum.Enabled=save.Enabled=true;cancel.Enabled=false;cancellation.Dispose();cancellation=null;}
        }
        run.Click+=async(_,_)=>await Inspect(false);all.Click+=async(_,_)=>await Inspect(true);
        dialog.FormClosing+=(_,e)=>{if(motionInspectionBusy){cancellation?.Cancel();e.Cancel=true;}};
        RefreshResults();InspectionDialogTestHook?.Invoke(dialog,run,all);dialog.ShowDialog(this);
    }
}
