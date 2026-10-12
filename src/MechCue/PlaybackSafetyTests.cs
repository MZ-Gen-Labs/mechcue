using System.Text.Json;

namespace MechCue;

public static partial class SelfTest
{
    static void TestPlaybackSafety()
    {
        if(MotionBoundMath.ClearanceLowerBound(0,0,0,.01,true)!=0 || MotionBoundMath.ClearanceLowerBound(0,0,.1,.01,true)>=0 || MotionBoundMath.ClearanceLowerBound(0,0,0,.01,false)>=0)
            throw new Exception("Only unchanged saved native-clear pairs may retain a zero clearance bound");
        var doc = new FakeCollisionDocument { ForcedStatus = 1 };
        var app = new FakeApplication { ActiveDocument = doc }; app.OpenDocuments.Items.Add(doc);
        var part = new FakePart(); doc.Occurrences.Items.Add(part);
        using var form = new MainForm(hostedApplication: app);
        form.VerifyPlaybackSafety(doc, part);
        var bridge = new Bridge(app, doc) { InspectionPolicy = new() { IncludeNested = false, VerifyContinuous = false }, PlaybackInspectionTimeLimit = TimeSpan.Zero };
        var track = new Track { Kind = "部品移動", Points = [new(0,0), new(1,10)] };
        bridge.Bind(track, new Target("Moving", part, "Matrix"));
        var before = part.Pose.ToArray();
        bool stopped = false;
        try { bridge.ApplyCheckedPath(0,1,false); }
        catch (InvalidOperationException error) { stopped = error.Message.Contains("未確認"); }
        if (!stopped || !part.Pose.SequenceEqual(before)) throw new Exception("Inspection deadline must stop as unverified and restore pose");
        bridge.Unbind(track);
        var inspected = new BudgetInspectionDocument { ForcedStatus = 1 };
        var inspectedApp = new FakeApplication { ActiveDocument = inspected }; inspectedApp.OpenDocuments.Items.Add(inspected);
        var first = new BudgetInspectionPart { Name="First" }; var second = new BudgetInspectionPart { Name="Second" };
        inspected.Occurrences.Items.AddRange([first,second]);
        var inspectedBridge = new Bridge(inspectedApp, inspected) { InspectionPolicy = new() { IncludeNested=false,VerifyContinuous=false } };
        inspectedBridge.Bind(track,new Target("First",first,"Matrix"));
        using var cancellation = new CancellationTokenSource();
        var original = first.Pose.ToArray();
        var task = inspectedBridge.CheckContinuousMotionAsync([0,.25,.5,.75,1],0,inspectedBridge.InspectionPolicy,5,cancellation.Token,(count,_)=>{if(count==1)cancellation.Cancel();});
        var wait = System.Diagnostics.Stopwatch.StartNew();
        while(!task.IsCompleted) { Application.DoEvents();Thread.Sleep(1);if(wait.Elapsed.TotalSeconds>5)throw new Exception("Cooperative inspection did not finish"); }
        var result = task.GetAwaiter().GetResult();
        if(!result.GetProperty("cancelled").GetBoolean() || result.GetProperty("samplingComplete").GetBoolean() || !result.GetProperty("poseRestored").GetBoolean() || result.GetProperty("checkedSampleCount").GetInt32()!=1 || !first.Pose.SequenceEqual(original))
            throw new Exception("Cancellation between inspection steps must retain incomplete coverage and restore actual pose");
        inspectedBridge.Unbind(track);
        var peak=new Track {Kind="部品移動",Points=[new(0,0),new(.5,20),new(1,0)]};
        var originalKeys=peak.Points.ToArray();
        if(!MotionSampling.KeyPlan([peak],0,1).SequenceEqual(new[]{0d,.5,1d}) || MotionSampling.Plan([peak],0,1).Length<=3)
            throw new Exception("Key-only inspection must retain reversals without inserting samples");
        inspectedBridge.InspectionPolicy=new(){IncludeNested=false,VerifyContinuous=true};
        inspectedBridge.Bind(peak,new Target("First",first,"Matrix"));
        JsonElement? keyReport=null;inspectedBridge.InspectionObserver=value=>keyReport=value;
        int keyCount=inspectedBridge.ApplyCheckedPath(0,1,subdivide:false);
        if(keyCount!=3 || keyReport==null || keyReport.Value.GetProperty("intervalRefinementEnabled").GetBoolean() || keyReport.Value.GetProperty("continuousPathCertified").GetBoolean() || !inspectedBridge.InspectionPolicy.VerifyContinuous || !peak.Points.SequenceEqual(originalKeys))
            throw new Exception("Subdivision OFF must inspect only original keys and never claim continuous coverage or change policy/keyframes");
        inspectedBridge.Unbind(peak);
    }

    public sealed class BudgetInspectionPart : FakePart
    {
        public bool Subassembly => false;
        public bool IncludeInInterference => true;
        public FakeDocument OccurrenceDocument { get; } = new();
        public void Range(ref double x1,ref double y1,ref double z1,ref double x2,ref double y2,ref double z2)
        { x1=Pose[12];y1=Pose[13];z1=Pose[14];x2=x1+.01;y2=y1+.01;z2=z1+.01; }
    }
    public sealed class BudgetInspectionDocument : FakeCollisionDocument
    {
        public void MinimumDistance(object a,object b,ref double distance,ref double[] first,ref double[] second)
        { distance=0;first=[.5,.6,.7];second=[.5,.6,.7]; }
    }
}

public partial class MainForm
{
    internal void VerifyPlaybackSafety(SelfTest.FakeCollisionDocument doc, SelfTest.FakePart part)
    {
        void Assert(bool value, string why) { if (!value) throw new Exception("Playback safety: "+why); }
        bridge.Connect(); tracks[0].Kind="部品移動"; RefreshTracks(0);
        bridge.Bind(tracks[0], new Target("Moving", part, "Matrix"));
        object Command(object args) => HandleAi(JsonSerializer.SerializeToElement(new { method="set_playback_options", args }));
        var pose=part.Pose.ToArray();
        Command(new {expectedDocument=doc.FullName,applyToCad=true,stopOnInterference=true});
        Assert(live.Checked&&collision.Checked&&!timer.Enabled&&part.Pose.SequenceEqual(pose),"ON changes selections without moving CAD");
        Command(new {expectedDocument=doc.FullName,stopOnInterference=false});
        Assert(live.Checked&&!collision.Checked,"unspecified Apply selection retained");
        bool refused=false;
        try { Command(new {expectedDocument="wrong.asm",applyToCad=false}); } catch(InvalidOperationException) { refused=true; }
        Assert(refused&&live.Checked,"wrong document refused before selection changes");
        Command(new {expectedDocument=doc.FullName,applyToCad=false});
        Assert(!live.Checked&&!collision.Checked&&part.Pose.SequenceEqual(pose),"OFF leaves CAD pose unchanged");
        Assert(subdivideInspection.Checked && top.Controls.GetChildIndex(subdivideInspection)==top.Controls.GetChildIndex(autoApply)+1,"default ON and directly after Auto");
        Command(new {expectedDocument=doc.FullName,subdivideInspection=false});
        Assert(!subdivideInspection.Checked && !live.Checked && !collision.Checked && part.Pose.SequenceEqual(pose),"subdivision-only option retained other settings without moving CAD");
        var saved=CaptureDocumentSettings();
        Assert(!DocumentSettings.Parse(saved.Json()).SubdivideInspection,"subdivision OFF persists in document settings");
        var legacy=System.Text.Json.Nodes.JsonNode.Parse(saved.Json())!.AsObject();legacy.Remove("SubdivideInspection");
        Assert(DocumentSettings.Parse(legacy.ToJsonString()).SubdivideInspection,"legacy settings default to subdivision ON");
        SetCompact(true);
        Assert(compactBar.Controls.GetChildIndex(subdivideInspection)==compactBar.Controls.GetChildIndex(autoApply)+1,"compact mode keeps subdivision after Auto");
        SetCompact(false);
        Assert(top.Controls.GetChildIndex(subdivideInspection)==top.Controls.GetChildIndex(autoApply)+1 && !subdivideInspection.Checked,"full mode restores location and selection");
        motionInspectionBusy=true;
        var state=JsonSerializer.SerializeToElement(HandleAi(JsonSerializer.SerializeToElement(new{method="get_state",args=new{}})));
        Assert(state.GetProperty("inspectionRunning").GetBoolean(),"busy inspection remains observable");
        using(var cancel=new CancellationTokenSource()) {
            mcpInspectionCancellation=cancel;
            HandleAi(JsonSerializer.SerializeToElement(new{method="stop",args=new{}}));
            Assert(cancel.IsCancellationRequested,"Stop cancels an inspection independently of mutation gate");
        }
        motionInspectionBusy=false;mcpInspectionCancellation=null;
        playbackTickActive=true;
        try { Error(new InvalidOperationException("fixture inspection failure")); }
        finally { playbackTickActive=false; }
        Assert(status.Text.Contains("fixture inspection failure"),"timer failure recorded without a modal wait");
        bridge.Unbind(tracks[0]); bridge.Disconnect();
    }
}
