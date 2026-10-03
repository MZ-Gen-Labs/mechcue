namespace MechCue;
public static partial class SelfTest
{
    static void TestMotionSampling()
    {
        void Check(bool ok,string why){if(!ok)throw new Exception(why);}
        var peak=new Track{Kind="部品移動",Points=[new(0,0),new(.5,100),new(1,0)]};
        var plan=MotionSampling.Plan([peak],0,1);Check(plan.Length==41&&plan.Contains(.5)&&plan[^1]==1,"Interior peak lost between equal endpoints");
        foreach(var pair in plan.Zip(plan.Skip(1)))Check(Math.Abs(peak.At(pair.Second)-peak.At(pair.First))<=5+1e-8,"Linear sampling bound exceeded");
        var spin=new Track{Kind="部品回転",Points=[new(0,0),new(1,360)]};
        Check(MotionSampling.Plan([spin],0,1).Length==361,"Full revolution collapsed to equal endpoint poses");
        Check(MotionSampling.Plan([spin],0,1,surfaceRadiusMm:1000).Length>1200,"Offset rotating surface did not increase sampling density");
        var combined=MotionSampling.Plan([peak,new Track{Kind="部品移動",Points=peak.Points.ToList()}],0,1);Check(combined.Length==81,"Simultaneous axes not combined");
        bool rejected=false;try{MotionSampling.Plan([spin],0,1,maxSamples:100);}catch(InvalidOperationException){rejected=true;}Check(rejected,"Over-budget plan was truncated");
        rejected=false;try{MotionSampling.Plan([peak],0,1,maxAngularStepDeg:double.NaN);}catch(ArgumentException){rejected=true;}Check(rejected,"Nonfinite threshold accepted");
        var doc=new FakeCollisionDocument();var app=new FakeApplication{ActiveDocument=doc};app.OpenDocuments.Items.Add(doc);
        var moving=new FakePart{Name="Moving"};doc.Occurrences.Items.AddRange([moving,new FakePart{Name="Obstacle"}]);
        var track=new Track{Kind="部品座標",Points=[new(0,500),new(.5,600),new(1,500)]};
        var bridge=new Bridge(app,doc);bridge.Bind(track,new Target("Moving",moving,"Matrix"));
        var report=System.Text.Json.JsonSerializer.SerializeToElement(bridge.CheckMotionSamples(plan,false,true));
        Check(!report.GetProperty("allSamplesClear").GetBoolean()&&report.GetProperty("samplingComplete").GetBoolean(),"Full report hides interior collision or incomplete coverage");
        doc.ForcedStatus=5;report=System.Text.Json.JsonSerializer.SerializeToElement(bridge.CheckMotionSamples([0,1]));
        Check(!report.GetProperty("analysisComplete").GetBoolean()&&!report.GetProperty("samplingComplete").GetBoolean()&&report.GetProperty("checkedSampleCount").GetInt32()==1,"Native failure marked clear or inspection continued");doc.ForcedStatus=0;
        bridge.ApplyChecked(0);bridge.ApplyChecked(1); // Both endpoints pass the native fixture.
        rejected=false;try{bridge.ApplyCheckedPath(0,1,false);}catch(InvalidOperationException){rejected=true;}
        Check(rejected&&Math.Abs(moving.Pose[12]-.5)<1e-9,"Interior collision missed or interval rollback failed");
        doc.ForcedStatus=1;Check(bridge.ApplyCheckedPath(1,0,false)==41,"Reverse seek not sampled in chart order");
        bridge.Unbind(track);
    }
}
