namespace MechCue;
public sealed partial class Bridge
{
    internal static double? StaticReportVolume(string text,int expectedCount)
    {
        var matches=System.Text.RegularExpressions.Regex.Matches(text.Replace("\r",""),@"(?im)^\s*(?:干渉体積|Interference\s+Volume)\s*:?[ \t]*([0-9]+(?:\.[0-9]+)?)[ \t]+mm\^3[ \t]*$");
        if(expectedCount<1||matches.Count!=expectedCount)return null;
        double sum=0;foreach(System.Text.RegularExpressions.Match m in matches){if(!double.TryParse(m.Groups[1].Value,System.Globalization.NumberStyles.AllowDecimalPoint,System.Globalization.CultureInfo.InvariantCulture,out var value)||!double.IsFinite(value)||value<0)return null;sum+=value;}return double.IsFinite(sum)?sum:null;
    }
    public static object CadCheckInterferencePairs(string expectedDocument,string set1Json="[]",string set2Json="[]",int maxPairs=10000,bool ignoreThreadInterferences=false,bool includeVolume=false)
    {
        if(maxPairs is <1 or >10000)throw new ArgumentException("maxPairs: 1..10000");
        int[] Parse(string text){var ids=System.Text.Json.JsonSerializer.Deserialize<int[]>(text)??throw new ArgumentException("JSON array required");if(ids.Any(i=>i<1)||ids.Distinct().Count()!=ids.Length)throw new ArgumentException("Distinct positive occurrence numbers required");return ids;}
        var first=Parse(set1Json);var second=Parse(set2Json);if(first.Length==0&&second.Length>0||first.Intersect(second).Any())throw new ArgumentException("Invalid/overlapping inspection sets");
        var application=CadApplication();var document=CadDocument(application,expectedDocument,".asm",false);int count=Convert.ToInt32(Get(Get(document,"Occurrences"),"Count"));
        if(first.Concat(second).Any(i=>i>count))throw new ArgumentException("Occurrence number out of range");if(first.Length==0)first=Enumerable.Range(1,count).ToArray();
        var leaves=new Bridge(application,document).MotionParts(true);
        bool In(MotionInspectionPart p,int[] set)=>set.Contains(int.Parse(p.Path.Split('/')[1]));
        var pairs=new List<(MotionInspectionPart A,MotionInspectionPart B)>();
        for(int i=0;i<leaves.Count;i++)for(int j=i+1;j<leaves.Count;j++){
            var a=leaves[i];var b=leaves[j];if(second.Length==0?In(a,first)&&In(b,first):In(a,first)&&In(b,second)||In(b,first)&&In(a,second))pairs.Add((a,b));
        }
        if(pairs.Count>maxPairs)throw new InvalidOperationException("Pair budget exceeded before inspection; increase maxPairs or select explicit subsets");
        object Identity(MotionInspectionPart p)=>new{path=p.Path,keyPath=p.KeyPath,name=p.Name,modulePath="/"+p.Path.Split('/')[1],fileName=Convert.ToString(Get(p.Chain[^1],"OccurrenceFileName"))};
        var results=new List<object>();int checkedPairs=0;bool complete=true,clear=true;
        foreach(var (a,b) in pairs){
            try{
                if(a.Chain.Concat(b.Chain).Any(p=>!Convert.ToBoolean(Get(p,"IncludeInInterference"))))throw new InvalidOperationException("IncludeInInterference disabled; pair unavailable");
                object[] args=[1,InterferenceSet([a.Native]),0,1,1,InterferenceSet([b.Native]),false,Type.Missing,Type.Missing,0,Type.Missing,Type.Missing,Type.Missing,Type.Missing,ignoreThreadInterferences];
                CadCallRef(document,"CheckInterference",[1,2,9,10,11,12,13],args);checkedPairs++;int status=Convert.ToInt32(args[2]),n=Convert.ToInt32(args[9]);bool done=status is >=1 and <=4,ok=status==1&&n==0;complete&=done;clear&=ok;
                double? volume=null;string? volumeError=null;
                if(includeVolume&&done&&!ok&&n>0){string path=Path.Combine(Path.GetTempPath(),"mechcue-pair-"+Guid.NewGuid().ToString("N")+".txt");
                    try{object[] report=[1,InterferenceSet([a.Native]),0,1,1,InterferenceSet([b.Native]),false,path,15,0,Type.Missing,Type.Missing,Type.Missing,Type.Missing,ignoreThreadInterferences];CadCallRef(document,"CheckInterference",[1,2,9,10,11,12,13],report);
                        if(Convert.ToInt32(report[2])!=status||Convert.ToInt32(report[9])!=n)throw new InvalidOperationException("Native pair analysis changed during volume collection");
                        if(File.Exists(path))volume=StaticReportVolume(File.ReadAllText(path),n);if(volume==null)volumeError="Native volume report unavailable or unsupported label/number/unit format; no value inferred";
                    }catch(Exception error){volumeError=(error.InnerException??error).Message;}
                    finally{if(File.Exists(path))try{File.Delete(path);}catch(IOException){} }
                }
                if(!ok)results.Add(new{first=Identity(a),second=Identity(b),status,count=n,analysisComplete=done,clear=ok,volumeMm3=volume,volumeAvailable=volume.HasValue,volumeError,
                    volumeSource=volume.HasValue?"native per-pair report, rounded mm^3":null,error=done?null:"Incomplete native pair analysis"});
            }catch(Exception error){complete=clear=false;results.Add(new{first=Identity(a),second=Identity(b),analysisComplete=false,clear=false,error=(error.InnerException??error).Message});}
        }
        return new{document=expectedDocument,set1=first,set2=second,comparison=second.Length==0?"set1-vs-itself":"set1-vs-set2",totalPairs=pairs.Count,checkedPairs,analysisComplete=complete,clear=complete&&clear,pairs=results,
            pairDetailsComplete=complete,includeVolume,scope="Static pose, instance-specific nested leaf pairs. Only collisions/unknown pairs returned. Optional volume uses recognized Japanese/English native report lines in mm^3; other formats remain unknown. No path certification."};
    }
}
