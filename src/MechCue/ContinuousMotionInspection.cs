using System.Text.Json;
namespace MechCue;
public sealed record ContinuousMotionSegment(double StartTime,double EndTime,string State,double? ClearanceLowerBoundMm,string? Reason,int PairCount=0);
public sealed partial class Bridge
{
    sealed class MotionInspectionRunner
    {
        readonly Bridge bridge;readonly double[] plan;readonly double required;readonly MotionInspectionPolicy policy;readonly int maxSamples;readonly bool stopOnFailure;
        readonly MotionKinematicsModel? model;readonly MotionGeometryGuard geometry;readonly FixedMotionPairCache? fixedPairs;
        readonly Dictionary<double,MotionPoseInspection> cache=[];readonly HashSet<double> checkedTimes=[];
        readonly Dictionary<string,int> partIds=[];readonly object[] partTable;readonly string[] pairKeys;
        readonly Dictionary<string,(int Verified,int Violations,int Unverified)> pairStates=[];
        readonly MotionInspectionSeries samples=new("samples",8*1024*1024),segments=new("segments",8*1024*1024);
        readonly Stack<(double A,double B,int Depth,string[] Keys)> pending=[];
        const long PairBudget=1000000;
        long retainedPairs;int initialIndex,measurements,chunkMeasurements,chunkCount=1,unverifiedSegments;bool stopped,cancelled,allClear=true,complete=true,finalized;string? stopReason,unsupported;
        public bool Done=>stopped||(initialIndex==plan.Length&&pending.Count==0);
        public int CheckedCount=>checkedTimes.Count;
        public double LastTime {get;private set;}
        public void ApplyEndPose(double time){geometry.Validate();bridge.Apply(time);model?.ValidatePose(time);geometry.Validate();}
        public MotionInspectionRunner(Bridge owner,double[] points,double clearance,MotionInspectionPolicy configuration,int budget,bool stopOnInterference=false)
        {
            bridge=owner;required=clearance;policy=configuration;maxSamples=budget;stopOnFailure=stopOnInterference;policy.Validate();
            if(!double.IsFinite(clearance)||clearance<0||clearance>10000||budget is <2 or >10001||points.Length==0||points.Any(t=>!double.IsFinite(t)||t<0))throw new ArgumentException("Invalid motion inspection limits");
            double start=points.Min(),end=points.Max();plan=points.Distinct().Order().ToArray();if(points[0]>points[^1])Array.Reverse(plan);
            int totalLimit=policy.AutoSplit?policy.MaxTotalSamples:budget;
            if(plan.Length>totalLimit)throw new InvalidOperationException("Total sampling budget exceeded before CAD motion");
            var parts=owner.MotionParts(policy.IncludeNested);var excluded=ValidateMotionContacts(policy,parts);geometry=new(parts);
            partTable=parts.Select((p,i)=>{partIds.Add(p.KeyPath,i);return (object)new{id=i,p.KeyPath,p.Name};}).ToArray();
            pairKeys=parts.SelectMany((a,i)=>parts.Skip(i+1).Select(b=>MotionInspectionPolicy.PairKey(a.KeyPath,b.KeyPath))).Where(k=>!excluded.ContainsKey(k)).ToArray();
            foreach(var key in pairKeys)pairStates.Add(key,(0,0,0));
            if(policy.VerifyContinuous){model=new(owner,parts,start,end,policy.IncludeNested);unsupported=model.UnsupportedReason;if(pairKeys.Length==0)unsupported="No non-excluded leaf pairs to verify";if(!geometry.CacheSafe)unsupported??="Save component geometry before continuous verification";}
            if(model?.Supported==true&&geometry.CacheSafe)fixedPairs=new((a,b)=>model.PairTravel(a,b,start,end)==0);
            if(policy.VerifyContinuous&&plan.Length>1)for(int i=plan.Length-1;i>0;i--)pending.Push((plan[i-1],plan[i],0,pairKeys));
        }
        MotionPoseInspection? Evaluate(double time)
        {
            geometry.Validate();
            if(cache.TryGetValue(time,out var old))return old;
            int totalLimit=policy.AutoSplit?policy.MaxTotalSamples:maxSamples;
            if((!checkedTimes.Contains(time)&&checkedTimes.Count>=totalLimit)||measurements>=4L*totalLimit){stopReason="Total inspection budget reached; remaining intervals are unverified";stopped=true;return null;}
            if(chunkMeasurements>=maxSamples||retainedPairs+pairKeys.Length>PairBudget){
                if(!policy.AutoSplit){stopReason="Inspection working-data budget reached";stopped=true;return null;}
                cache.Clear();retainedPairs=0;chunkMeasurements=0;chunkCount++;
            }
            LastTime=time;var sample=bridge.InspectTypedMotionPose(time,required,policy,()=>{geometry.Validate();model?.ValidatePose(time);},fixedPairs);measurements++;chunkMeasurements++;
            allClear&=sample.Passed;complete&=sample.Complete;
            if(checkedTimes.Add(time))samples.Add(sample.Compact(partIds));
            if(!policy.VerifyContinuous||unsupported!=null)sample=sample.ReleasePairs();
            cache.Add(time,sample);retainedPairs+=sample.Pairs.Count;
            if(!sample.Complete||(stopOnFailure&&!sample.Passed))stopped=true;
            return sample;
        }
        void Terminal(double a,double b,string state,double? lower,string? reason,string[] keys)
        {
            if(keys.Length==0)return;
            foreach(string key in keys){var s=pairStates[key];pairStates[key]=state=="verified-clear"?(s.Verified+1,s.Violations,s.Unverified):state=="sample-violation"?(s.Verified,s.Violations+1,s.Unverified):(s.Verified,s.Violations,s.Unverified+1);}
            if(state=="unverified")unverifiedSegments++;
            segments.Add(new ContinuousMotionSegment(a,b,state,lower,reason,keys.Length));
        }
        public void Step(CancellationToken cancellation=default)
        {
            if(Done)return;if(cancellation.IsCancellationRequested){cancelled=stopped=true;return;}
            try{
                if(initialIndex<plan.Length){Evaluate(plan[initialIndex++]);return;}
                var interval=pending.Pop();double a=interval.A,b=interval.B;int depth=interval.Depth;var keys=interval.Keys;
                // Put it back until both endpoint inspections succeed, so a budget/error never loses coverage.
                pending.Push(interval);var first=Evaluate(a);if(first==null||stopped)return;var second=Evaluate(b);if(second==null||stopped)return;pending.Pop();
                if(unsupported!=null){
                    var violations=first.Failures.Concat(second.Failures).Where(p=>p.AnalysisComplete).Select(p=>MotionInspectionPolicy.PairKey(p.FirstKeyPath,p.SecondKeyPath)).ToHashSet();
                    Terminal(a,b,"sample-violation",null,"Violation at a checked pose; continuous model unavailable",keys.Where(violations.Contains).ToArray());
                    Terminal(a,b,"unverified",null,unsupported,keys.Where(k=>!violations.Contains(k)).ToArray());return;
                }
                var d1=first.Pairs.ToDictionary(p=>MotionInspectionPolicy.PairKey(p.FirstKeyPath,p.SecondKeyPath));
                var d2=second.Pairs.ToDictionary(p=>MotionInspectionPolicy.PairKey(p.FirstKeyPath,p.SecondKeyPath));
                if(!d1.Keys.ToHashSet().SetEquals(pairKeys)||!d2.Keys.ToHashSet().SetEquals(pairKeys))throw new InvalidOperationException("Inspected part identities changed during verification");
                var failed=new List<string>();var verified=new List<string>();var refine=new List<string>();double minimum=double.PositiveInfinity;
                foreach(string key in keys){
                    var p=d1[key];var q=d2[key];
                    if(!p.AnalysisComplete||!q.AnalysisComplete){Terminal(a,b,"unverified",null,"Native pair analysis incomplete",[key]);continue;}
                    if(!p.Clear||!q.Clear){failed.Add(key);continue;}
                    double lower=Math.Max(p.LowerBoundMm,q.LowerBoundMm)-model!.PairTravel(p.FirstKeyPath,p.SecondKeyPath,a,b)-policy.NumericalMarginMm;
                    if(double.IsFinite(lower)&&lower>=required){verified.Add(key);minimum=Math.Min(minimum,lower);}else refine.Add(key);
                }
                Terminal(a,b,"sample-violation",null,"Interference or clearance shortfall at a checked pose; other pairs continue",failed.ToArray());
                Terminal(a,b,"verified-clear",double.IsFinite(minimum)?minimum:null,null,verified.ToArray());
                if(refine.Count==0)return;double mid=a+(b-a)/2;
                if(depth>=policy.MaxRefinementDepth||mid==a||mid==b){Terminal(a,b,"unverified",null,"Refinement depth or time precision limit reached",refine.ToArray());return;}
                pending.Push((mid,b,depth+1,refine.ToArray()));pending.Push((a,mid,depth+1,refine.ToArray()));
            }catch(Exception error){complete=false;stopped=true;stopReason=(error.InnerException??error).Message;fixedPairs?.Values.Clear();}
        }
        public JsonElement Result(bool restored)
        {
            if(!finalized){
                foreach(var s in pending)try{Terminal(s.A,s.B,"unverified",null,cancelled?"Cancelled":stopReason??"Inspection stopped",s.Keys);}catch(Exception error){stopReason??=error.Message;complete=false;}
                finalized=true;
            }
            bool finished=initialIndex==plan.Length&&plan.All(checkedTimes.Contains)&&complete&&!cancelled&&stopReason==null;
            bool coverageComplete=finished&&pending.Count==0&&!stopped;
            var pairResults=pairStates.Select(p=>new{firstId=partIds[p.Key.Split('|')[0]],secondId=partIds[p.Key.Split('|')[1]],state=p.Value.Violations>0?"sample-violation":!complete||p.Value.Unverified>0||p.Value.Verified==0?"unverified":"verified-clear",verifiedIntervals=p.Value.Verified,violationIntervals=p.Value.Violations,unverifiedIntervals=p.Value.Unverified}).ToArray();
            bool certified=policy.VerifyContinuous&&plan.Length>1&&unsupported==null&&coverageComplete&&allClear&&pairResults.Length>0&&pairResults.All(p=>p.state=="verified-clear");
            var preview=samples.Preview();var segmentPreview=segments.Preview();
            return JsonSerializer.SerializeToElement(new{
                schemaVersion=3,samples=preview,partTable,allSamplesClear=checkedTimes.Count>0&&allClear,samplingComplete=finished,analysisComplete=checkedTimes.Count>0&&complete,
                plannedSampleCount=plan.Length+checkedTimes.Except(plan).Count(),checkedSampleCount=checkedTimes.Count,baseSampleCount=plan.Length,baseSampleTimes=plan,
                startTime=plan[0],endTime=plan[^1],lastCheckedTime=checkedTimes.Count==0?(double?)null:LastTime,requiredClearanceMm=required,clearanceChecked=required>0,
                cancelled,continuousPathCertified=certified,poseRestored=restored,error=stopReason,inspectionPolicy=policy,intervalRefinementEnabled=policy.VerifyContinuous,
                automaticSplitting=policy.AutoSplit,chunkCount,nativePoseEvaluationCount=measurements,workingDataPairBudget=PairBudget,resultByteBudget=16*1024*1024,
                fixedPairCacheHits=fixedPairs?.Hits??0,fixedPairCacheCount=fixedPairs?.Values.Count??0,
                geometryCacheUnavailableReasons=geometry.UnsafeReasons,
                samplesTruncated=samples.Archived,segmentsTruncated=segments.Archived,detailArchive=new{samples=samples.Manifest(),segments=segments.Manifest()},
                continuousVerification=new{Requested=policy.VerifyContinuous,KinematicsSupported=unsupported==null,UnsupportedReason=unsupported,policy.NumericalMarginMm,Segments=segmentPreview,TotalSegmentCount=segments.Count,UnverifiedCount=unverifiedSegments,PairResults=pairResults,ExcludedPairCount=policy.AllowedContacts.Length,Method="Independent leaf-pair relative-travel bounds with interval bisection; bounded working batches and streamed detail archive. Fixed-pair reuse is scoped to this run and guarded by component geometry and observed poses."},
                scope="All non-excluded instance leaf pairs. Pair results cover the full requested path only when complete; named contacts, unsupported motion and unresolved intervals remain outside the certificate."
            });
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
