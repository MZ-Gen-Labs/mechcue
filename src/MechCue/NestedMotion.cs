namespace MechCue;
public sealed partial class Bridge
{
    readonly Dictionary<Track,string> nestedKeyPaths=new();
    internal object CheckMotionSamples(double[] samples,bool stopOnInterference=false,bool adaptive=false,double step=1,double linearStep=5,double angularStep=1,bool surfaceBased=false,double surfaceStep=5,double clearanceMm=0)
    {
        if(samples.Length==0)throw new ArgumentException("No samples");
        if(!double.IsFinite(clearanceMm)||clearanceMm<0||clearanceMm>10000)throw new ArgumentException("Clearance must be 0..10000 mm");
        var restore=CaptureVideoPose();var results=new List<System.Text.Json.JsonElement>();
        try
        {
            foreach(double position in samples)
            {
                var sample=InspectMotionPose(position,clearanceMm);results.Add(sample);
                if(!sample.GetProperty("analysisComplete").GetBoolean()||(stopOnInterference&&!sample.GetProperty("passed").GetBoolean()))break;
            }
        }
        finally {restore();}
        var result=MotionInspectionSummary(samples,results,adaptive,step,linearStep,angularStep,surfaceBased,surfaceStep,clearanceMm,true);
        return result;
    }
    internal void BindNested(Track track,string keyPath,bool modifySharedSubassembly)
    {
        Check();
        if(!modifySharedSubassembly)throw new InvalidOperationException("Nested motion edits a shared subassembly document; explicitly enable modifySharedSubassembly");
        if(keyPath.Split('/',StringSplitOptions.RemoveEmptyEntries).Length<2)throw new ArgumentException("Use a nested path with at least two reference keys");
        if(IsConcept(track))throw new InvalidOperationException("Concept axis tracks cannot be reassigned");
        if(!track.Kind.StartsWith("部品"))throw new InvalidOperationException("Nested motion requires a part track");
        var (occurrence,parent)=ResolveOccurrenceKeyPath(doc!,keyPath);
        if(Convert.ToBoolean(Get(parent,"ReadOnly")))throw new InvalidOperationException("Parent subassembly is read-only");
        var relations=Get(occurrence,"Relations3d");
        for(int i=1;i<=Convert.ToInt32(Get(relations,"Count"));i++){var r=GetItem(relations,i);if(Convert.ToInt32(Get(r,"Type"))!=1959028688&&!Convert.ToBoolean(Get(r,"Suppress")))throw new InvalidOperationException("Nested drive requires a free/grounded part without other active constraints");}
        Bind(track,new Target("入れ子部品 / "+keyPath,occurrence,"Matrix",Convert.ToString(Get(occurrence,"Name"))!),false,true);
        nestedKeyPaths[track]=keyPath;
    }
}
