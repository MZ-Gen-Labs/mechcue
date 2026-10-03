using System.Text.Json;
namespace MechCue;
public sealed record AllowedMotionContact(string FirstKeyPath,string SecondKeyPath,string Reason);
public sealed record MotionInspectionPolicy
{
    public bool IncludeNested {get;init;}=true;
    public bool VerifyContinuous {get;init;}=true;
    public double NumericalMarginMm {get;init;}=.01;
    public int MaxRefinementDepth {get;init;}=20;
    public AllowedMotionContact[] AllowedContacts {get;init;}=[];
    public void Validate()
    {
        if(!double.IsFinite(NumericalMarginMm)||NumericalMarginMm<.001||NumericalMarginMm>10||MaxRefinementDepth is <0 or >30||AllowedContacts==null||AllowedContacts.Length>1000)throw new ArgumentException("Invalid inspection policy");
        var seen=new HashSet<string>();
        foreach(var contact in AllowedContacts){if(contact==null||string.IsNullOrWhiteSpace(contact.Reason)||contact.Reason.Length>500||contact.FirstKeyPath==contact.SecondKeyPath||!ValidKeyPath(contact.FirstKeyPath)||!ValidKeyPath(contact.SecondKeyPath)||!seen.Add(PairKey(contact.FirstKeyPath,contact.SecondKeyPath)))throw new ArgumentException("Contacts require distinct exact leaf key paths and a reason; duplicates refused");}
    }
    static bool ValidKeyPath(string? path)=>path!=null&&path.StartsWith('/')&&path.Length<=10000&&path.Split('/',StringSplitOptions.RemoveEmptyEntries).All(s=>s.Length>0&&s.Length%2==0&&s.All(Uri.IsHexDigit));
    public static string PairKey(string a,string b)=>string.CompareOrdinal(a,b)<0?a+"|"+b:b+"|"+a;
}
sealed record MotionInspectionPart(string Path,string KeyPath,string Name,object Native,object[] Chain,double[] WorldMatrix,double[] BoundsMm);
public sealed partial class Bridge
{
    internal MotionInspectionPolicy InspectionPolicy {get;set;}=new();
    static double[] MotionBounds(object native)
    {
        object[] args=[0d,0d,0d,0d,0d,0d];CadCallRef(native,"Range",[0,1,2,3,4,5],args);
        double[] bounds=args.Select(Convert.ToDouble).Select(v=>v*1000).ToArray();
        if(bounds.Any(v=>!double.IsFinite(v))||Enumerable.Range(0,3).Any(k=>bounds[k]>bounds[k+3]))throw new InvalidOperationException("Invalid native occurrence range");return bounds;
    }
    List<MotionInspectionPart> MotionParts(bool includeNested)
    {
        Check();var leaves=new List<MotionInspectionPart>();int count=0;
        void Visit(object native,string path,string keyPath,string parentName,object[] chain,bool nested,int depth)
        {
            if(depth>32||++count>1000)throw new InvalidOperationException("Assembly inspection traversal limit exceeded; no parts silently omitted");
            var definition=nested?Get(native,"ThisAsOccurrence"):native;
            string name=parentName+Convert.ToString(Get(native,"Name"));
            // Definition keys plus instance parent path distinguish repeated shared subassemblies.
            string key=keyPath+"/"+Convert.ToHexString(ReferenceKey(definition));
            var next=chain.Append(definition).ToArray();
            bool assembly=Convert.ToBoolean(Get(native,"Subassembly"));
            if(includeNested&&assembly){var children=Get(native,"SubOccurrences");int n=Convert.ToInt32(Get(children,"Count"));if(n==0)throw new InvalidOperationException("Empty or unloaded subassembly: "+name);for(int i=1;i<=n;i++)Visit(GetItem(children,i),path+"/"+i,key,name+" / ",next,true,depth+1);return;}
            var world=next.Aggregate(CadTransform(0,0,0,0,0,0),(matrix,item)=>ConceptMachine.Multiply(matrix,Matrix(item)));
            leaves.Add(new(path,key,name,nested?Get(native,"Reference"):native,next,world,MotionBounds(native)));
        }
        var roots=Get(doc!,"Occurrences");for(int i=1;i<=Convert.ToInt32(Get(roots,"Count"));i++)Visit(GetItem(roots,i),"/"+i,"","",[],false,1);
        if(leaves.Count==0)throw new InvalidOperationException("No interference targets; inspection unavailable");
        if(leaves.Count*(leaves.Count-1L)/2>10000)throw new InvalidOperationException("Inspection supports up to 10000 leaf pairs; split assembly inspection explicitly");
        if(leaves.Select(p=>p.KeyPath).Distinct().Count()!=leaves.Count)throw new InvalidOperationException("Ambiguous native instance keys; inspection refused");return leaves;
    }
    internal object ListMotionInspectionParts()=>new{document=InspectionDocumentName(),parts=MotionParts(true).Select(p=>new{p.Path,p.KeyPath,p.Name,depth=p.Chain.Length,p.BoundsMm}).ToArray(),policy=InspectionPolicy};
    string InspectionDocumentName()=>CadName(doc!);
    static Dictionary<string,AllowedMotionContact> ValidateMotionContacts(MotionInspectionPolicy policy,List<MotionInspectionPart> parts)
    {
        policy.Validate();var keys=parts.Select(p=>p.KeyPath).ToHashSet();
        foreach(var contact in policy.AllowedContacts)if(!keys.Contains(contact.FirstKeyPath)||!keys.Contains(contact.SecondKeyPath))throw new InvalidOperationException("Allowed contact no longer resolves to an inspected instance. Re-select it; no contact silently ignored.");
        return policy.AllowedContacts.ToDictionary(c=>MotionInspectionPolicy.PairKey(c.FirstKeyPath,c.SecondKeyPath));
    }
    internal void ValidateMotionInspectionPolicy(MotionInspectionPolicy policy){ValidateMotionContacts(policy,MotionParts(policy.IncludeNested));}
}
