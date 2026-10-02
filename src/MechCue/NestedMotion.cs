namespace MechCue;
public sealed partial class Bridge
{
    readonly Dictionary<Track,string> nestedKeyPaths=new();
    internal object CheckMotionSamples(double[] samples)
    {
        var restore=CaptureVideoPose();var results=new List<object>();
        try
        {
            foreach(double position in samples)
            {
                Apply(position);
                // Preserve native status/count rather than interpreting an API error as clear.
                results.Add(new {time=position,analysis=CadCheckInterference(CadName(doc!))});
            }
        }
        finally {restore();}
        bool clear=results.All(r=>System.Text.Json.JsonSerializer.SerializeToElement(r).GetProperty("analysis").GetProperty("clear").GetBoolean());
        return new {samples=results,allSamplesClear=clear,poseRestored=true,scope="Discrete static poses only; no swept-path guarantee, pair exclusion, minimum clearance or continuous collision certificate."};
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
