namespace MechCue;
public static class MotionBoundMath
{
    public static double Variation(Track track,double a,double b)
    {
        if(b<a)(a,b)=(b,a);var times=track.Points.Select(p=>p.Time).Where(t=>t>a&&t<b).Append(a).Append(b).Distinct().Order().ToArray();
        return times.Zip(times.Skip(1)).Sum(pair=>Math.Abs(track.At(pair.Second)-track.At(pair.First)));
    }
    public static double MaximumAbs(Track track,double a,double b)=>track.Points.Where(p=>p.Time>=a&&p.Time<=b).Select(p=>Math.Abs(p.Value)).Append(Math.Abs(track.At(a))).Append(Math.Abs(track.At(b))).Max();
    public static bool Rigid(double[] m)=>m.Length==16&&m.All(double.IsFinite)&&Math.Abs(m[3])+Math.Abs(m[7])+Math.Abs(m[11])+Math.Abs(m[15]-1)<1e-9&&Enumerable.Range(0,3).All(a=>Enumerable.Range(0,3).All(b=>Math.Abs(Enumerable.Range(0,3).Sum(k=>m[4*a+k]*m[4*b+k])-(a==b?1:0))<1e-9));
    internal static double LocalRadius(double[] bounds,double[] world)
    {
        if(!Rigid(world))throw new InvalidOperationException("Scaled or non-rigid occurrence transform");
        var inverse=Bridge.InvertRigidMatrix(world);double radius=0;
        for(int mask=0;mask<8;mask++){var point=Enumerable.Range(0,3).Select(k=>bounds[k+(((mask>>k)&1)*3)]/1000).ToArray();var local=Enumerable.Range(0,3).Select(k=>inverse[k]*point[0]+inverse[k+4]*point[1]+inverse[k+8]*point[2]+inverse[k+12]).ToArray();radius=Math.Max(radius,Math.Sqrt(local.Sum(v=>v*v))*1000);}
        return radius;
    }
}
public sealed partial class Bridge
{
    sealed record MotionInfluence(string InstanceId,Track Track,double RadiusMm);
    sealed record MotionPoseNode(object Definition,double[] FixedPose,Track? Driver,string? ConceptBodyId);
    sealed class MotionKinematicsModel
    {
        readonly Bridge bridge;
        readonly Dictionary<string,List<MotionInfluence>> influences=[];
        readonly List<MotionPoseNode> nodes=[];
        public string? UnsupportedReason {get;private set;}
        public bool Supported=>UnsupportedReason==null;
        internal MotionKinematicsModel(Bridge owner,List<MotionInspectionPart> parts,double start,double end,bool includeNested)
        {
            bridge=owner;
            try {
                if(!includeNested)throw new InvalidOperationException("Continuous verification requires all nested leaves");
                if(owner.bindings.Any(p=>p.Value.Target.Property!="Matrix"||p.Key.Kind is not ("部品移動" or "部品座標" or "部品回転")))throw new InvalidOperationException("Constraint-driven motion has no verified rigid kinematic model; sampled checks remain available");
                foreach(var part in parts) {
                    var entries=new List<MotionInfluence>();double localRadius=MotionBoundMath.LocalRadius(part.BoundsMm,part.WorldMatrix);
                    var drivers=part.Chain.Select(d=>owner.bindings.FirstOrDefault(b=>Equals(b.Value.Target.Com,d))).ToArray();
                    var matrices=part.Chain.Select((d,i)=>drivers[i].Key!=null?(double[])drivers[i].Value.Original:Matrix(d)).ToArray();
                    foreach(var matrix in matrices)if(!MotionBoundMath.Rigid(matrix))throw new InvalidOperationException("Scaled/sheared reference pose cannot be certified");
                    double[] envelopes=matrices.Select((m,i)=>Math.Sqrt(m.Skip(12).Take(3).Sum(v=>v*v))*1000+(drivers[i].Key is Track t&&t.Kind!="部品回転"?MotionBoundMath.MaximumAbs(t,start,end):0)).ToArray();
                    var keys=part.KeyPath.Split('/',StringSplitOptions.RemoveEmptyEntries);
                    for(int i=0;i<part.Chain.Length;i++) {
                        var definition=part.Chain[i];var relations=Get(definition,"Relations3d");
                        if(Convert.ToBoolean(Get(definition,"Adjustable"))||Convert.ToBoolean(Get(definition,"IsAdjustablePart")))throw new InvalidOperationException("Adjustable geometry or assembly poses cannot be certified as fixed rigid bodies");
                        if(Convert.ToBoolean(Get(definition,"UseSimplified"))||Convert.ToBoolean(Get(definition,"HasBodyOverride")))throw new InvalidOperationException("Simplified or assembly-overridden geometry requires explicit geometry verification; continuous certificate unavailable");
                        for(int r=1;r<=Convert.ToInt32(Get(relations,"Count"));r++){var relation=GetItem(relations,r);if(Convert.ToInt32(Get(relation,"Type"))!=1959028688&&!Convert.ToBoolean(Get(relation,"Suppress")))throw new InvalidOperationException("Active assembly constraints can move additional parts; continuous certificate unavailable");}
                        string? conceptId=owner.conceptParts.FirstOrDefault(p=>Equals(p.Value,definition)).Key;
                        if(string.IsNullOrEmpty(conceptId))conceptId=null;
                        if(conceptId!=null&&drivers[i].Key!=null)throw new InvalidOperationException("Competing native and concept drivers on the same body cannot be certified");
                        nodes.Add(new(definition,matrices[i],drivers[i].Key,conceptId));
                        if(drivers[i].Key is Track driver)entries.Add(new("native/"+string.Join('/',keys.Take(i+1)),driver,localRadius+envelopes.Skip(i+1).Sum()));
                        if(conceptId!=null) {
                            var model=owner.concept!.Model;var body=model.Bodies.Single(b=>b.Id==conceptId);var axes=new List<ConceptAxis>();string parent=body.Parent;
                            while(parent!=""){var axis=model.Axes.Single(a=>a.Id==parent);axes.Insert(0,axis);parent=axis.Parent;}
                            double bodyReach=localRadius+envelopes.Skip(i+1).Sum()+Math.Sqrt(body.CenterMm.Sum(v=>v*v))+Math.Sqrt(body.GeometryMatrix.Skip(12).Take(3).Sum(v=>v*v))*1000;
                            for(int a=0;a<axes.Count;a++){var axis=axes[a];var track=owner.conceptTracks[axis.Id];double radius=bodyReach+axes.Skip(a+1).Sum(next=>Math.Sqrt(next.OriginMm.Sum(v=>v*v))+(next.Kind=="linear"?MotionBoundMath.MaximumAbs(owner.conceptTracks[next.Id],start,end):0));entries.Add(new("concept/"+axis.Id,track,radius));}
                        }
                    }
                    influences.Add(part.KeyPath,entries);
                }
            }
            catch(Exception error){UnsupportedReason=(error.InnerException??error).Message;}
        }
        public double PairTravel(string first,string second,double start,double end)
        {
            if(!Supported)throw new InvalidOperationException(UnsupportedReason);
            var a=influences[first];var b=influences[second];var common=a.Select(x=>x.InstanceId).Intersect(b.Select(x=>x.InstanceId)).ToHashSet();
            // Shared ancestor transforms are rigid common motion and cancel from relative distance.
            return a.Concat(b).Where(x=>!common.Contains(x.InstanceId)).Sum(x=>MotionBoundMath.Variation(x.Track,start,end)*(x.Track.Kind=="部品回転"?x.RadiusMm*Math.PI/180:1));
        }
        public void ValidatePose(double time)
        {
            if(!Supported)return;
            Dictionary<string,double[]>? concepts=bridge.concept==null?null:bridge.concept.Model.Poses(bridge.conceptTracks.ToDictionary(p=>p.Key,p=>p.Value.At(time)));
            foreach(var node in nodes){var expected=node.ConceptBodyId!=null?concepts![node.ConceptBodyId]:node.Driver==null?node.FixedPose:Transform.Apply(node.FixedPose,node.Driver.Kind,node.Driver.Axis,node.Driver.At(time));if(!ConceptSame(Matrix(node.Definition),expected))throw new InvalidOperationException("Observed pose differs from the verified rigid motion model; continuous verification stopped");}
        }
    }
}
