namespace MechCue;
public sealed partial class Bridge
{
    sealed class MotionGeometryGuard
    {
        readonly List<(object Document,string File,long Length,long Stamp)> documents=[];
        public bool CacheSafe {get;private set;}=true;
        public List<string> UnsafeReasons {get;}=[];
        public MotionGeometryGuard(List<MotionInspectionPart> parts)
        {
            foreach(var definition in parts.SelectMany(p=>p.Chain).Distinct()){
                try{var document=Get(definition,"OccurrenceDocument");if(documents.Any(d=>Equals(d.Document,document)))continue;string file=CadName(document);var info=new FileInfo(file);
                    // Unsaved component geometry cannot safely be reused.
                    if(!info.Exists||Convert.ToBoolean(Get(document,"Dirty"))){CacheSafe=false;UnsafeReasons.Add((info.Exists?"Unsaved component: ":"Missing component file: ")+file);continue;}
                    documents.Add((document,file,info.Length,info.LastWriteTimeUtc.Ticks));
                }catch(Exception error){CacheSafe=false;UnsafeReasons.Add("Component revision unavailable: "+(error.InnerException??error).Message);}
            }
        }
        public void Validate()
        {
            foreach(var d in documents){var info=new FileInfo(d.File);if(!info.Exists||info.Length!=d.Length||info.LastWriteTimeUtc.Ticks!=d.Stamp||Convert.ToBoolean(Get(d.Document,"Dirty")))throw new InvalidOperationException("Component geometry changed during inspection; restart after saving components. Cached results invalidated.");}
        }
    }
    internal static double[] MoveInspectionPoint(double[] pointMm,double[] from,double[] to)
    {
        var change=ConceptMachine.Multiply(to,InvertRigidMatrix(from));
        return Enumerable.Range(0,3).Select(k=>change[k]*pointMm[0]+change[k+4]*pointMm[1]+change[k+8]*pointMm[2]+change[k+12]*1000).ToArray();
    }
    sealed record FixedMotionPair(MotionPairDistance Result,MotionClearancePair? Distance,double[] FirstPose,double[] SecondPose);
    sealed class FixedMotionPairCache(Func<string,string,bool> stationary)
    {
        internal readonly Dictionary<string,FixedMotionPair> Values=[];
        public int Hits {get;private set;}
        public bool Eligible(string a,string b)=>stationary(a,b);
        public bool TryGet(string a,string b,out FixedMotionPair? pair){if(Values.TryGetValue(MotionInspectionPolicy.PairKey(a,b),out pair)){Hits++;return true;}return false;}
    }
}
