namespace MechCue;
public sealed record MotionClearancePair(string Part1,string Part2,double DistanceMm,double[] Point1Mm,double[] Point2Mm);
public sealed record MotionClearanceResult(bool AnalysisComplete,bool Clear,double RequiredMm,double? MinimumMm,int PairCount,List<MotionClearancePair> Violations,MotionClearancePair? ClosestPair,string? Error=null);
public sealed partial class Bridge
{
    internal MotionClearanceResult CheckMotionClearance(double requiredMm)
    {
        Check();if(!double.IsFinite(requiredMm)||requiredMm<0||requiredMm>10000)throw new ArgumentException("Clearance must be 0..10000 mm");
        var parts=Enumerable.Range(1,Convert.ToInt32(Get(Get(doc!,"Occurrences"),"Count"))).Select(i=>CadOccurrence(doc!,i)).ToArray();
        int pairs=parts.Length*(parts.Length-1)/2;
        if(pairs<1||pairs>10000)throw new InvalidOperationException("Clearance requires 2 or more top-level occurrences and at most 10000 pairs");
        var violations=new List<MotionClearancePair>();MotionClearancePair? closest=null;int checkedPairs=0;
        try {
            for(int i=0;i<parts.Length;i++)for(int j=i+1;j<parts.Length;j++) {
                object[] args=[parts[i],parts[j],0d,new double[3],new double[3]];
                CadCallRef(doc!,"MinimumDistance",[2,3,4],args);
                double distance=Convert.ToDouble(args[2])*1000;
                double[] Point(object value)=>((Array)value).Cast<object>().Select(v=>Convert.ToDouble(v)*1000).ToArray();
                var pair=new MotionClearancePair(Convert.ToString(Get(parts[i],"Name"))!,Convert.ToString(Get(parts[j],"Name"))!,distance,Point(args[3]),Point(args[4]));
                if(!double.IsFinite(distance)||distance<0||pair.Point1Mm.Length!=3||pair.Point2Mm.Length!=3||pair.Point1Mm.Concat(pair.Point2Mm).Any(v=>!double.IsFinite(v)))throw new InvalidOperationException("Invalid native minimum-distance result");
                checkedPairs++;if(closest==null||distance<closest.DistanceMm)closest=pair;
                if(distance+1e-6<requiredMm)violations.Add(pair);
            }
            return new(true,violations.Count==0,requiredMm,closest!.DistanceMm,checkedPairs,violations,closest);
        }
        catch(Exception error){return new(false,false,requiredMm,closest?.DistanceMm,checkedPairs,violations,closest,(error.InnerException??error).Message);}
    }
    internal double MotionSurfaceRadiusMm(double start,double end)
    {
        Check();double extent=0;
        var occurrences=Get(doc!,"Occurrences");
        for(int i=1;i<=Convert.ToInt32(Get(occurrences,"Count"));i++) {
            object[] args=[0d,0d,0d,0d,0d,0d];CadCallRef(GetItem(occurrences,i),"Range",[0,1,2,3,4,5],args);
            double[] bounds=args.Select(Convert.ToDouble).Select(v=>v*1000).ToArray();
            if(bounds.Any(v=>!double.IsFinite(v))||Enumerable.Range(0,3).Any(k=>bounds[k]>bounds[k+3]))throw new InvalidOperationException("Invalid occurrence range; surface sampling unavailable");
            extent=Math.Max(extent,Math.Sqrt(Enumerable.Range(0,3).Sum(k=>Math.Pow(Math.Max(Math.Abs(bounds[k]),Math.Abs(bounds[k+3])),2))));
        }
        // Include the local offset chain of concept joints and the bound native reference poses.
        double pivots=concept?.Model.Axes.Sum(a=>Math.Sqrt(a.OriginMm.Sum(v=>v*v)))??0;
        foreach(var b in bindings.Values.Where(b=>b.Target.Property=="Matrix"))pivots=Math.Max(pivots,Math.Sqrt(((double[])b.Original).Skip(12).Take(3).Sum(v=>v*v))*1000);
        double travel=DrivenMotionTracks().Where(t=>t.Kind is not ("角度拘束" or "部品回転")).Sum(t=>t.Points.Where(p=>p.Time>=start&&p.Time<=end).Select(p=>p.Value).Append(t.At(start)).Append(t.At(end)).Select(Math.Abs).Max());
        double radius=2*(extent+pivots+travel);
        if(!double.IsFinite(radius)||radius<=0)throw new InvalidOperationException("Cannot determine surface sampling radius");
        return radius;
    }
}
