namespace MechCue;

// Every track is piecewise linear; include all key times before subdividing.
public static class MotionSampling
{
    public static double[] CoarsePlan(IEnumerable<Track> source,double start,double end,double step=1,int maxSamples=5001)
    {
        if(!double.IsFinite(start)||!double.IsFinite(end)||!double.IsFinite(step)||start<0||end<start||step<=0||maxSamples is <2 or >100001)throw new ArgumentException("Invalid coarse inspection interval/budget");
        double count=Math.Ceiling((end-start)/step);if(count>=maxSamples)throw new InvalidOperationException("Coarse sampling budget exceeded; split the inspection interval");
        var times=Enumerable.Range(0,(int)count).Select(i=>start+i*step).Append(end).ToHashSet();
        foreach(var track in source){track.Validate();for(int i=1;i<track.Points.Count-1;i++){
            var p=track.Points[i];if(p.Time<=start||p.Time>=end)continue;
            int before=Math.Sign(p.Value-track.Points[i-1].Value),after=Math.Sign(track.Points[i+1].Value-p.Value);
            if(before!=after)times.Add(p.Time);
        }}
        if(times.Count>maxSamples)throw new InvalidOperationException("Coarse sampling budget exceeded by reversals/stops; split the interval");
        return times.Order().ToArray();
    }
    public static double[] Plan(IEnumerable<Track> source,double start,double end,double maxTimeStep=1,double maxLinearStepMm=5,double maxAngularStepDeg=1,int maxSamples=5001,double surfaceRadiusMm=0,double maxSurfaceStepMm=5)
    {
        foreach(var v in new[]{start,end,maxTimeStep,maxLinearStepMm,maxAngularStepDeg,surfaceRadiusMm,maxSurfaceStepMm})if(!double.IsFinite(v))throw new ArgumentException("Finite sampling values required");
        if(surfaceRadiusMm<0||maxSurfaceStepMm<=0)throw new ArgumentException("Invalid surface sampling limits");
        if(start<0||end<start||maxTimeStep<=0||maxLinearStepMm<=0||maxAngularStepDeg<=0||maxSamples is <2 or >100001)throw new ArgumentException("Positive sampling limits, nonnegative ordered interval and 2..100001 total samples required");
        var tracks=source.Distinct().ToArray();foreach(var track in tracks){track.Validate();if(track.Kind is not ("距離拘束" or "角度拘束" or "部品移動" or "部品回転" or "部品座標"))throw new ArgumentException("Unsupported driven track units: "+track.Kind);}
        var knots=tracks.SelectMany(t=>t.Points.Select(p=>p.Time)).Where(t=>t>start&&t<end).Append(start).Append(end).Distinct().Order().ToArray();
        if(knots.Length>maxSamples)throw new InvalidOperationException("Sampling budget exceeded by key times; shorten the inspection interval");
        var plan=new List<double>{start};
        for(int i=1;i<knots.Length;i++) {
            double a=knots[i-1],b=knots[i],linear=0,angular=0;
            foreach(var track in tracks){double delta=Math.Abs(track.At(b)-track.At(a));if(track.Kind is "角度拘束" or "部品回転")angular+=delta;else linear+=delta;}
            // Sum simultaneous axis changes, rather than allowing every axis the full budget.
            double surface=surfaceRadiusMm>0?(linear+surfaceRadiusMm*angular*Math.PI/180)/maxSurfaceStepMm:0;
            double segments=Math.Max(1,Math.Ceiling(Math.Max(surface,Math.Max((b-a)/maxTimeStep,Math.Max(linear/maxLinearStepMm,angular/maxAngularStepDeg)))));
            if(!double.IsFinite(segments)||segments>maxSamples-plan.Count)throw new InvalidOperationException("Sampling budget exceeded; shorten the inspection interval or explicitly adjust limits. No CAD motion performed.");
            for(int n=1;n<=(int)segments;n++){double time=n==(int)segments?b:a+(b-a)*n/segments;if(time<=plan[^1])throw new InvalidOperationException("Sampling time precision insufficient; shorten interval");plan.Add(time);}
        }
        return plan.ToArray();
    }
}

public sealed partial class Bridge
{
    internal double[] PlanCoarseMotionSamples(double start,double end,double step=1,int maxSamples=5001)
    {Check();if(BindingCount==0)throw new InvalidOperationException("No CAD motion tracks bound");return MotionSampling.CoarsePlan(DrivenMotionTracks(),start,end,step,maxSamples);}
    internal Track[] DrivenMotionTracks()=>bindings.Keys.Concat(conceptTracks.Values).Distinct().ToArray();
    internal double[] PlanMotionSamples(double start,double end,double step,double linearStep,double angularStep,int maxSamples,bool surfaceBased=false,double surfaceStep=5)
    {
        Check();if(BindingCount==0)throw new InvalidOperationException("No CAD motion tracks bound");
        return MotionSampling.Plan(DrivenMotionTracks(),start,end,step,linearStep,angularStep,maxSamples,surfaceBased?MotionSurfaceRadiusMm(start,end):0,surfaceStep);
    }
    internal double PlaybackClearanceMm;
    internal Action<System.Text.Json.JsonElement>? InspectionObserver;
    public int ApplyCheckedPath(double fromTime,double toTime,bool surfaceBased=true)
    {
        var plan=PlanMotionSamples(Math.Min(fromTime,toTime),Math.Max(fromTime,toTime),1,5,1,5001,surfaceBased);
        if(toTime<fromTime)Array.Reverse(plan);
        if(InspectionPolicy.IncludeNested||InspectionPolicy.VerifyContinuous||InspectionPolicy.AllowedContacts.Length>0)return ApplyVerifiedPath(plan);
        var restore=CaptureVideoPose();double at=fromTime;
        var results=new List<System.Text.Json.JsonElement>();
        try {
            EnsureNoInterference();
            foreach(var point in plan){at=point;var sample=InspectMotionPose(point,PlaybackClearanceMm);results.Add(sample);if(!sample.GetProperty("passed").GetBoolean())throw new InvalidOperationException("干渉・すきま不足または解析未完了。 "+sample.GetRawText());}
            InspectionObserver?.Invoke(MotionInspectionSummary(plan,results,true,1,5,1,surfaceBased,5,PlaybackClearanceMm,false));return plan.Length;
        }
        catch(Exception error) {
            try{restore();}catch(Exception rollback){throw new InvalidOperationException($"Path check at {at:0.######}s failed; ROLLBACK FAILED: {rollback.Message}",error);}
            InspectionObserver?.Invoke(MotionInspectionSummary(plan,results,true,1,5,1,surfaceBased,5,PlaybackClearanceMm,true));
            throw new InvalidOperationException($"経路検査 {at:0.######} 秒で停止し、区間開始姿勢へ戻しました。 "+(error.InnerException??error).Message,error);
        }
    }
}
