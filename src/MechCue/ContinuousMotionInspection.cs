using System.Text.Json;
namespace MechCue;
public sealed record ContinuousMotionSegment(double StartTime,double EndTime,string State,double? ClearanceLowerBoundMm,string? Reason);
public sealed partial class Bridge
{
    sealed class MotionInspectionRunner
    {
        readonly Bridge bridge;readonly double[] plan;readonly double required;readonly MotionInspectionPolicy policy;readonly int maxSamples;readonly bool stopOnFailure;
        readonly MotionKinematicsModel? model;readonly Dictionary<double,JsonElement> cache=[];
        readonly Stack<(double A,double B,int Depth)> pending=[];readonly List<ContinuousMotionSegment> segments=[];
        int initialIndex;bool stopped,cancelled;string? unsupported;
        public bool Done=>stopped||(initialIndex==plan.Length&&pending.Count==0);
        public int CheckedCount=>cache.Count;
        public double LastTime {get;private set;}
        public void ApplyEndPose(double time){bridge.Apply(time);model?.ValidatePose(time);}
        public MotionInspectionRunner(Bridge owner,double[] samples,double clearance,MotionInspectionPolicy configuration,int budget,bool stopOnInterference=false)
        {
            bridge=owner;required=clearance;policy=configuration;maxSamples=budget;stopOnFailure=stopOnInterference;policy.Validate();
            if(!double.IsFinite(clearance)||clearance<0||clearance>10000||budget is <2 or >10001||samples.Length==0||samples.Any(t=>!double.IsFinite(t)||t<0))throw new ArgumentException("Invalid motion inspection limits");
            double start=samples.Min(),end=samples.Max();
            var knots=policy.VerifyContinuous?owner.DrivenMotionTracks().SelectMany(t=>t.Points.Select(p=>p.Time)).Where(t=>t>=start&&t<=end):[];
            plan=samples.Concat(knots).Distinct().Order().ToArray();if(samples[0]>samples[^1])Array.Reverse(plan);
            if(plan.Length>budget)throw new InvalidOperationException("Initial sampling budget exceeded before CAD motion");
            var parts=owner.MotionParts(policy.IncludeNested);var excluded=ValidateMotionContacts(policy,parts);
            if(policy.VerifyContinuous){model=new(owner,parts,start,end,policy.IncludeNested);unsupported=model.UnsupportedReason;if(parts.Count*(parts.Count-1)/2==excluded.Count)unsupported="No non-excluded leaf pairs to verify";}
            if(policy.VerifyContinuous&&plan.Length>1)for(int i=plan.Length-1;i>0;i--)pending.Push((plan[i-1],plan[i],0));
        }
        JsonElement Evaluate(double time)
        {
            if(cache.TryGetValue(time,out var sample))return sample;
            LastTime=time;sample=bridge.InspectMotionPose(time,required,policy,model==null?null:()=>model.ValidatePose(time));cache.Add(time,sample);
            if(!sample.GetProperty("analysisComplete").GetBoolean()||(stopOnFailure&&!sample.GetProperty("passed").GetBoolean()))stopped=true;
            return sample;
        }
        public void Step(CancellationToken cancellation=default)
        {
            if(Done)return;if(cancellation.IsCancellationRequested){cancelled=stopped=true;return;}
            if(initialIndex<plan.Length){Evaluate(plan[initialIndex++]);return;}
            if(cache.Values.Any(s=>!s.GetProperty("passed").GetBoolean())){
                while(pending.TryPop(out var interval)){bool violated=!cache[interval.A].GetProperty("passed").GetBoolean()||!cache[interval.B].GetProperty("passed").GetBoolean();segments.Add(new(interval.A,interval.B,violated?"sample-violation":"unverified",null,"A violation is already detected in this path; continuous proof stopped"));}stopped=true;return;
            }
            var (a,b,depth)=pending.Pop();var first=cache[a];var second=cache[b];
            if(!first.GetProperty("passed").GetBoolean()||!second.GetProperty("passed").GetBoolean()){segments.Add(new(a,b,"sample-violation",null,"Interference or clearance shortfall at a checked pose"));return;}
            if(unsupported!=null){segments.Add(new(a,b,"unverified",null,unsupported));return;}
            var d1=first.GetProperty("leafPairs").Deserialize<List<MotionPairDistance>>()!.ToDictionary(p=>MotionInspectionPolicy.PairKey(p.FirstKeyPath,p.SecondKeyPath));
            var d2=second.GetProperty("leafPairs").Deserialize<List<MotionPairDistance>>()!.ToDictionary(p=>MotionInspectionPolicy.PairKey(p.FirstKeyPath,p.SecondKeyPath));
            if(!d1.Keys.ToHashSet().SetEquals(d2.Keys))throw new InvalidOperationException("Inspected part identities changed during verification");
            double lower=d1.Count==0?double.NegativeInfinity:d1.Min(p=>Math.Max(p.Value.LowerBoundMm,d2[p.Key].LowerBoundMm)-model!.PairTravel(p.Value.FirstKeyPath,p.Value.SecondKeyPath,a,b)-policy.NumericalMarginMm);
            if(double.IsFinite(lower)&&lower>=required){segments.Add(new(a,b,"verified-clear",lower,null));return;}
            double mid=a+(b-a)/2;
            if(depth>=policy.MaxRefinementDepth||cache.Count>=maxSamples||mid==a||mid==b){segments.Add(new(a,b,"unverified",double.IsFinite(lower)?lower:null,cache.Count>=maxSamples?"Sample budget reached":"Refinement depth or time precision limit reached"));return;}
            Evaluate(mid);pending.Push((mid,b,depth+1));pending.Push((a,mid,depth+1));
        }
        public JsonElement Result(bool restored)
        {
            var unresolved=new List<ContinuousMotionSegment>(segments);
            unresolved.AddRange(pending.Select(s=>new ContinuousMotionSegment(s.A,s.B,"unverified",null,cancelled?"Cancelled":"Inspection stopped")));
            bool samplingComplete=plan.All(t=>cache.ContainsKey(t))&&cache.Values.All(s=>s.GetProperty("analysisComplete").GetBoolean())&&!cancelled;
            bool verified=policy.VerifyContinuous&&plan.Length>1&&samplingComplete&&unsupported==null&&unresolved.Count>0&&unresolved.All(s=>s.State=="verified-clear");
            var summary=bridge.MotionInspectionSummary(plan,cache.Values.OrderBy(s=>s.GetProperty("time").GetDouble()).ToList(),true,1,5,1,true,5,required,restored,cancelled).EnumerateObject().ToDictionary(p=>p.Name,p=>p.Value.Clone());
            summary["samplingComplete"]=JsonSerializer.SerializeToElement(samplingComplete);summary["plannedSampleCount"]=JsonSerializer.SerializeToElement(plan.Length+cache.Keys.Count(t=>!plan.Contains(t)));
            summary["baseSampleCount"]=JsonSerializer.SerializeToElement(plan.Length);summary["continuousPathCertified"]=JsonSerializer.SerializeToElement(verified);
            foreach(string name in new[]{"adaptive","maxTimeStep","maxLinearStepMm","maxAngularStepDeg","surfaceBased","maxSurfaceStepMm"})summary.Remove(name);
            summary["baseSampleTimes"]=JsonSerializer.SerializeToElement(plan);summary["intervalRefinementEnabled"]=JsonSerializer.SerializeToElement(policy.VerifyContinuous);
            summary["lastCheckedTime"]=JsonSerializer.SerializeToElement(cache.Count==0?(double?)null:LastTime);
            summary["inspectionPolicy"]=JsonSerializer.SerializeToElement(policy);
            summary["continuousVerification"]=JsonSerializer.SerializeToElement(new{Requested=policy.VerifyContinuous,KinematicsSupported=unsupported==null,UnsupportedReason=unsupported,NumericalMarginMm=policy.NumericalMarginMm,Segments=unresolved.OrderBy(s=>Math.Min(s.StartTime,s.EndTime)).ToArray(),UnverifiedCount=unresolved.Count(s=>s.State=="unverified"),ExcludedPairCount=policy.AllowedContacts.Length,Method="Rigid-body relative-travel bounds and native clearance with interval bisection; checked against observed poses. Conditional on fixed CAD geometry and native measurement accuracy within the configured margin."});
            summary["scope"]=JsonSerializer.SerializeToElement(policy.IncludeNested?"All non-excluded instance leaf pairs including internal subassembly pairs. Continuous clearance only for supported rigid kinematics; named exclusions and unresolved intervals remain outside the certificate.":"Top-level pairs only; continuous verification unavailable.");
            return JsonSerializer.SerializeToElement(summary);
        }
    }
    internal JsonElement CheckContinuousMotion(double[] samples,double clearance,MotionInspectionPolicy? policy=null,int maxSamples=5001,bool stopOnInterference=false,CancellationToken cancellation=default)
    {
        var runner=new MotionInspectionRunner(this,samples,clearance,policy??InspectionPolicy,maxSamples,stopOnInterference);var restore=CaptureVideoPose();
        try{while(!runner.Done)runner.Step(cancellation);}finally{RestoreInspectedPose(restore);}return runner.Result(true);
    }
    internal async Task<JsonElement> CheckContinuousMotionAsync(double[] samples,double clearance,MotionInspectionPolicy policy,int maxSamples,CancellationToken cancellation,Action<int,double>? progress=null)
    {
        var runner=new MotionInspectionRunner(this,samples,clearance,policy,maxSamples);var restore=CaptureVideoPose();
        try{while(!runner.Done){runner.Step(cancellation);progress?.Invoke(runner.CheckedCount,runner.LastTime);await Task.Delay(1);}}finally{RestoreInspectedPose(restore);}return runner.Result(true);
    }
    static void RestoreInspectedPose(Action restore){try{restore();}catch(Exception error){throw new InvalidOperationException("POSE RESTORATION FAILED: CADの姿勢復元に失敗しました。手動で姿勢を確認してください。",error);}}
    int ApplyVerifiedPath(double[] plan)
    {
        var runner=new MotionInspectionRunner(this,plan,PlaybackClearanceMm,InspectionPolicy,5001,true);var restore=CaptureVideoPose();
        try{
            var initial=InspectMotionPairs(PlaybackClearanceMm,InspectionPolicy);if(!initial.Clear)throw new InvalidOperationException("開始姿勢で干渉・すきま不足または解析未完了です。");
            while(!runner.Done)runner.Step();var result=runner.Result(false);
            if(!result.GetProperty("allSamplesClear").GetBoolean()||!result.GetProperty("samplingComplete").GetBoolean())throw new InvalidOperationException("経路上に干渉・すきま不足または解析未完了があります。");
            if(InspectionPolicy.VerifyContinuous&&plan[0]!=plan[^1]&&!result.GetProperty("continuousPathCertified").GetBoolean())throw new InvalidOperationException("連続区間を確認できません。経路検査画面の未確認理由を確認してください。");
            runner.ApplyEndPose(plan[^1]);InspectionObserver?.Invoke(result);return runner.CheckedCount;
        }
        catch(Exception error){try{restore();}catch(Exception rollback){throw new InvalidOperationException("経路検査の姿勢復元に失敗: "+rollback.Message,error);}InspectionObserver?.Invoke(runner.Result(true));throw new InvalidOperationException("経路検査で停止し、区間開始姿勢へ戻しました。 "+(error.InnerException??error).Message,error);}
    }
}
