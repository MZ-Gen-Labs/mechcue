using System.Text.Json;
namespace MechCue;
public sealed partial class Bridge
{
    internal static void VerifyCompactMotionInSolidEdge(string chartsPath,string policyPath,string outputDirectory)
    {
        if(Directory.Exists(outputDirectory))throw new ArgumentException("Use a new verification output directory");Directory.CreateDirectory(outputDirectory);
        using var charts=JsonDocument.Parse(File.ReadAllText(chartsPath));using var policyJson=JsonDocument.Parse(File.ReadAllText(policyPath));
        var bridge=new Bridge();bridge.Connect();string expected=charts.RootElement.GetProperty("document").GetProperty("fullName").GetString()!;
        if(!string.Equals(Path.GetFullPath(CadName(bridge.Document)),Path.GetFullPath(expected),StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Active assembly differs from the chart backup");
        var settings=DocumentSettings.Parse(bridge.ReadSettings()??throw new InvalidOperationException("Assembly has no saved concept settings"));
        if(settings.Concept==null)throw new InvalidOperationException("Expected saved concept axes");
        var tracks=settings.Tracks.Select(t=>t.Track).ToList();bridge.RestoreConcept(settings.Concept,tracks);
        bridge.InspectionPolicy=policyJson.RootElement.GetProperty("policy").Deserialize<MotionInspectionPolicy>()!;
        var occurrences=Get(bridge.Document,"Occurrences");var before=Enumerable.Range(1,Convert.ToInt32(Get(occurrences,"Count"))).Select(i=>Matrix(GetItem(occurrences,i))).ToArray();
        var summary=new List<object>();
        foreach(var pattern in charts.RootElement.GetProperty("planned").EnumerateArray()){
            foreach(var edit in pattern.GetProperty("edits").EnumerateArray()){var track=tracks.Single(t=>t.Id==edit.GetProperty("trackId").GetGuid());track.Points=edit.GetProperty("points").Deserialize<List<KeyPoint>>(new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;bridge.ValidateConceptPoints(track,track.Points);}
            double end=tracks.Max(t=>t.Points[^1].Time);var plan=bridge.PlanCoarseMotionSamples(0,end);
            var result=bridge.CheckContinuousMotion(plan,.5);string name=pattern.GetProperty("pattern").GetProperty("name").GetString()!;
            if(!result.GetProperty("samplingComplete").GetBoolean()||!result.GetProperty("analysisComplete").GetBoolean()||!result.GetProperty("poseRestored").GetBoolean())throw new Exception("Incomplete real-model inspection: "+name);
            if(result.GetProperty("allSamplesClear").GetBoolean()||result.GetProperty("continuousPathCertified").GetBoolean())throw new Exception("Known unsafe real model marked clear: "+name);
            string path=Path.Combine(outputDirectory,$"pattern-{summary.Count+1:00}.json");using(var stream=File.Create(path))JsonSerializer.Serialize(stream,result);
            var failures=result.GetProperty("samples").EnumerateArray().SelectMany(s=>s.GetProperty("analysis").GetProperty("pairs").EnumerateArray()).Select(p=>p.GetProperty("Part1").GetString()+" ↔ "+p.GetProperty("Part2").GetString()).Distinct().ToArray();
            summary.Add(new{name,checkedSamples=result.GetProperty("checkedSampleCount").GetInt32(),bytes=new FileInfo(path).Length,failures});
            if(before.Where((matrix,i)=>!ConceptSame(matrix,Matrix(GetItem(occurrences,i+1)))).Any())throw new Exception("Real-model native pose was not restored");
            File.WriteAllText(Path.Combine(outputDirectory,"summary.json"),JsonSerializer.Serialize(summary,new JsonSerializerOptions{WriteIndented=true}));
        }
        // No document save, event attachment, binding cleanup or pre-existing window closure.
    }
}
