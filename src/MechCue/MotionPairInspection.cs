using System.Text.Json;
namespace MechCue;
public sealed record MotionPairDistance(string FirstKeyPath,string SecondKeyPath,string Part1,string Part2,double LowerBoundMm,double? ExactDistanceMm,bool Clear,bool AnalysisComplete,string? Error);
sealed record MotionPairInspection(bool Clear,bool AnalysisComplete,List<MotionPairDistance> Pairs,AllowedMotionContact[] Excluded,MotionClearanceResult Clearance);
public sealed partial class Bridge
{
    internal static double BoxDistance(double[] a,double[] b)=>Math.Sqrt(Enumerable.Range(0,3).Sum(k=>Math.Pow(Math.Max(0,Math.Max(a[k]-b[k+3],b[k]-a[k+3])),2)));
    MotionPairInspection InspectMotionPairs(double requiredMm,MotionInspectionPolicy policy)
    {
        var parts=MotionParts(policy.IncludeNested);var excluded=ValidateMotionContacts(policy,parts);
        var results=new List<MotionPairDistance>();var violations=new List<MotionClearancePair>();MotionClearancePair? closest=null;
        for(int i=0;i<parts.Count;i++)for(int j=i+1;j<parts.Count;j++) {
            var a=parts[i];var b=parts[j];if(excluded.ContainsKey(MotionInspectionPolicy.PairKey(a.KeyPath,b.KeyPath)))continue;
            double lower=BoxDistance(a.BoundsMm,b.BoundsMm);double? exact=null;bool clear=true,complete=true;string? error=null;
            try {
                // Disjoint enclosing boxes cannot interfere. Never infer clearance from overlapping boxes.
                if(lower<=policy.NumericalMarginMm) {
                    if(a.Chain.Concat(b.Chain).Any(part=>!Convert.ToBoolean(Get(part,"IncludeInInterference"))))throw new InvalidOperationException("Native IncludeInInterference is disabled on an inspected occurrence; enable it or name this contact explicitly");
                    object[] args=[1,InterferenceSet([a.Native]),0,1,1,InterferenceSet([b.Native]),false,Type.Missing,Type.Missing,0,Type.Missing,Type.Missing,Type.Missing,Type.Missing,false];
                    CadCallRef(doc!,"CheckInterference",[1,2,9,10,11,12,13],args);
                    int status=Convert.ToInt32(args[2]),count=Convert.ToInt32(args[9]);complete=status is 1 or 2 or 3 or 4;clear=status==1&&count==0;
                    if(!complete)error="Native interference status "+status;
                }
                // Refine the closest distance exactly; preserve box lower bounds for all other pairs.
                if(closest==null||lower<=closest.DistanceMm||lower<=requiredMm+policy.NumericalMarginMm) {
                    object[] args=[a.Native,b.Native,0d,new double[3],new double[3]];CadCallRef(doc!,"MinimumDistance",[2,3,4],args);
                    exact=Convert.ToDouble(args[2])*1000;
                    double[] Point(object value)=>((Array)value).Cast<object>().Select(v=>Convert.ToDouble(v)*1000).ToArray();
                    var pair=new MotionClearancePair(a.Name,b.Name,exact.Value,Point(args[3]),Point(args[4]));
                    if(!double.IsFinite(pair.DistanceMm)||pair.DistanceMm<0||pair.Point1Mm.Length!=3||pair.Point2Mm.Length!=3||pair.Point1Mm.Concat(pair.Point2Mm).Any(v=>!double.IsFinite(v)))throw new InvalidOperationException("Invalid native minimum-distance result");
                    if(closest==null||pair.DistanceMm<closest.DistanceMm)closest=pair;if(pair.DistanceMm+1e-6<requiredMm){violations.Add(pair);clear=false;}
                    // An interfering contained solid can have a positive boundary distance; use zero in that case.
                    lower=clear?Math.Max(lower,exact.Value):0;
                }
            }
            catch(Exception failure){clear=complete=false;error=(failure.InnerException??failure).Message;}
            results.Add(new(a.KeyPath,b.KeyPath,a.Name,b.Name,lower,exact,clear,complete,error));
        }
        bool analysisComplete=results.All(p=>p.AnalysisComplete);bool allClear=analysisComplete&&results.All(p=>p.Clear);
        return new(allClear,analysisComplete,results,policy.AllowedContacts,new(analysisComplete,allClear,requiredMm,closest?.DistanceMm,results.Count,violations,closest,analysisComplete?null:"One or more leaf pairs could not be inspected"));
    }
}
