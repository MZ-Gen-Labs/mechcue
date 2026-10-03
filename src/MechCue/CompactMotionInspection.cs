using System.Text.Json;
namespace MechCue;

// Full pair distances are working data for interval proofs, not persistent JSON history.
sealed record MotionPoseInspection(double Time,object[] Values,List<MotionPairDistance> Pairs,MotionPairDistance[] Failures,MotionClearanceResult? Clearance,bool Complete,bool Passed,string? Error,int PairCount)
{
    public MotionPoseInspection ReleasePairs()=>this with{Pairs=[]};
    public JsonElement Compact(IReadOnlyDictionary<string,int> ids)=>JsonSerializer.SerializeToElement(new{
        time=Time,values=Values,analysisComplete=Complete,passed=Passed,
        analysis=new{clear=Passed,analysisComplete=Complete,state=Error!=null?"error":Passed?"clear":"interference-or-clearance-shortfall",comparison="instance-leaf-pairs",count=Failures.Length,checkedPairCount=PairCount,error=Error,
            pairs=Failures.Select(p=>new{firstId=ids[p.FirstKeyPath],secondId=ids[p.SecondKeyPath],p.Part1,p.Part2,p.ExactDistanceMm,p.Clear,p.AnalysisComplete,p.NativeStatus,p.ClearanceShortfall,p.Error}).ToArray()},
        clearance=Clearance==null?null:new{Clearance.AnalysisComplete,Clearance.Clear,Clearance.RequiredMm,Clearance.MinimumMm,Clearance.PairCount,Clearance.ClosestPair,Clearance.Error},
        pairResults="failures-only; full identities are in the report partTable"
    });
}
public sealed partial class Bridge
{
    MotionPoseInspection InspectTypedMotionPose(double position,double required,MotionInspectionPolicy policy,Action? validate,FixedMotionPairCache? reuse=null)
    {
        object[] Values()=>DrivenMotionTracks().Select(t=>(object)new{trackId=t.Id,t.Name,t.Kind,t.Axis,value=t.At(position)}).ToArray();
        try {
            Apply(position);validate?.Invoke();var inspection=InspectMotionPairs(required,policy,reuse);validate?.Invoke();
            return new(position,Values(),inspection.Pairs,inspection.Pairs.Where(p=>!p.Clear).ToArray(),inspection.Clearance,inspection.AnalysisComplete,inspection.Clear,null,inspection.Pairs.Count);
        }
        catch(Exception error){return new(position,Values(),[],[],null,false,false,(error.InnerException??error).Message,0);}
    }
}
