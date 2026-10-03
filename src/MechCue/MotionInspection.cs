using System.Text.Json;
namespace MechCue;
public sealed partial class Bridge
{
    internal JsonElement InspectMotionPose(double position,double clearanceMm=0)
    {
        object analysis;MotionClearanceResult? clearance=null;
        try {
            Apply(position);
            var parts=Enumerable.Range(1,Convert.ToInt32(Get(Get(doc!,"Occurrences"),"Count"))).Select(i=>CadOccurrence(doc!,i)).ToArray();
            if(parts.Length==0)throw new InvalidOperationException("No interference targets");
            object[] args=[parts.Length,InterferenceSet(parts),0,4,Type.Missing,Type.Missing,false,Type.Missing,Type.Missing,0,Type.Missing,Type.Missing,Type.Missing,Type.Missing,false];
            CadCallRef(doc!,"CheckInterference",[1,2,9,10,11,12,13],args);
            analysis=CadInterferenceResult(CadName(doc!),Enumerable.Range(1,parts.Length).ToArray(),[],args);
            if(clearanceMm>0)clearance=CheckMotionClearance(clearanceMm);
        }
        catch(Exception error){analysis=new{clear=false,analysisComplete=false,state="error",count=(int?)null,error=(error.InnerException??error).Message};}
        var native=JsonSerializer.SerializeToElement(analysis);
        bool complete=native.GetProperty("analysisComplete").GetBoolean()&&(clearanceMm==0||clearance?.AnalysisComplete==true);
        bool passed=complete&&native.GetProperty("clear").GetBoolean()&&(clearanceMm==0||clearance?.Clear==true);
        return JsonSerializer.SerializeToElement(new{time=position,values=DrivenMotionTracks().Select(t=>new{trackId=t.Id,t.Name,t.Kind,t.Axis,value=t.At(position)}).ToArray(),analysis,clearance,analysisComplete=complete,passed});
    }
    internal JsonElement MotionInspectionSummary(double[] samples,List<JsonElement> results,bool adaptive,double step,double linearStep,double angularStep,bool surfaceBased,double surfaceStep,double clearanceMm,bool restored,bool cancelled=false)
    {
        bool complete=results.Count>0&&results.All(r=>r.GetProperty("analysisComplete").GetBoolean());
        bool finished=results.Count==samples.Length&&complete&&!cancelled;
        return JsonSerializer.SerializeToElement(new{samples=results,allSamplesClear=results.Count>0&&results.All(r=>r.GetProperty("passed").GetBoolean()),samplingComplete=finished,analysisComplete=complete,
            plannedSampleCount=samples.Length,checkedSampleCount=results.Count,startTime=samples[0],endTime=samples[^1],lastCheckedTime=results.Count==0?(double?)null:results[^1].GetProperty("time").GetDouble(),
            adaptive,maxTimeStep=step,maxLinearStepMm=adaptive?(double?)linearStep:null,maxAngularStepDeg=adaptive?(double?)angularStep:null,surfaceBased,maxSurfaceStepMm=surfaceBased?(double?)surfaceStep:null,
            requiredClearanceMm=clearanceMm,clearanceChecked=clearanceMm>0,cancelled,continuousPathCertified=false,poseRestored=restored,
            scope="Discrete static poses; extent-based surface movement estimate is not a continuous certificate. Clearance between top-level occurrence pairs; no contact exclusions or checks inside a single subassembly."});
    }
}
