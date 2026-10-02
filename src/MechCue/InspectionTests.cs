using System.Text.Json;
namespace MechCue;

public static partial class SelfTest
{
    static void TestInspectionResults()
    {
        JsonElement Result(int status, int count, object? a = null, object? b = null, object? confirmed = null)
        {
            object[] args = [0, 0, status, 0, 0, 0, false, 0, 0, count, a!, b!, confirmed!, null!, false];
            return JsonSerializer.SerializeToElement(Bridge.CadInterferenceResult("test.asm", [1], [2], args));
        }
        void Assert(bool result, string message) { if (!result) throw new Exception(message); }
        foreach (int status in new[] { 2, 3, 4, 5, 0, 99 })
            Assert(!Result(status, 0).GetProperty("clear").GetBoolean(), "Non-clear native status must not become clear.");
        Assert(!Result(1, 1).GetProperty("clear").GetBoolean(), "Contradictory native count must not become clear.");
        Assert(Result(1, 0).GetProperty("clear").GetBoolean(), "Completed zero-intersection analysis is clear.");
        Assert(!Result(5, 0).GetProperty("analysisComplete").GetBoolean(), "Incomplete analysis is explicit.");
        Assert(!Result(2, 1).GetProperty("pairDetailsComplete").GetBoolean(), "Missing native pair arrays are explicit.");
        var pair = Result(3, 1, new[] { new InspectionPart("a") }, new[] { new InspectionPart("b") }, new[] { false });
        Assert(pair.GetProperty("pairDetailsComplete").GetBoolean(), "Available pair arrays retained.");
        Assert(!pair.GetProperty("pairs")[0].GetProperty("confirmed").GetBoolean(), "Probable contact remains unconfirmed.");
        Assert(pair.GetProperty("pairs")[0].GetProperty("first").GetProperty("name").GetString() == "a", "Pair name preserved.");
        var reportFolder = Path.Combine(Path.GetTempPath(), "mechcue-report-test-" + Guid.NewGuid());
        Directory.CreateDirectory(reportFolder);
        try
        {
            string clearPath = Path.Combine(reportFolder, "clear.txt");
            var report = JsonSerializer.SerializeToElement(Bridge.CadInterferenceReport(Result(1, 0), clearPath));
            Assert(File.Exists(clearPath) && report.GetProperty("reportSource").GetString() == "mechcue-clear-summary", "Clear without a native report produces a labelled summary.");
            foreach (int status in new[] { 2, 5, 99 })
            {
                string missing = Path.Combine(reportFolder, status + ".txt");
                var incomplete = JsonSerializer.SerializeToElement(Bridge.CadInterferenceReport(Result(status, 0), missing));
                Assert(!File.Exists(missing) && !incomplete.GetProperty("reportCreated").GetBoolean() && !incomplete.GetProperty("analysis").GetProperty("clear").GetBoolean(), "Missing native report never fabricates clear.");
            }
        }
        finally { Directory.Delete(reportFolder, true); }
        var features = new FakeCollection(); features.Items.Add(new InspectionPart("Rail-clearance"));
        var models = new FakeCollection(); models.Items.Add(new InspectionModel(features));
        bool duplicateRejected = false;
        try { Bridge.CadValidateFeatureName(models, "rail-clearance"); } catch (InvalidOperationException) { duplicateRejected = true; }
        Assert(duplicateRejected && features.Count == 1, "Duplicate names rejected without mutations.");
        Bridge.CadValidateFeatureName(models, "Rail-clearance-2");
        TestConceptMigration();
        var child = new InspectionOccurrence { Name = "leaf", OccurrenceFileName = "leaf.par", Pose = Bridge.CadTransform(100, 0, 0, 0, 0, 0) };
        var shared = new InspectionDocument("shared.asm"); shared.Occurrences.Items.Add(child);
        var root = new InspectionDocument("root.asm");
        root.Occurrences.Items.Add(new InspectionOccurrence { Name = "rotated", OccurrenceFileName = "shared.asm", Subassembly = true, OccurrenceDocument = shared, Pose = Bridge.CadTransform(500, 0, 0, 0, 0, 90) });
        root.Occurrences.Items.Add(new InspectionOccurrence { Name = "translated", OccurrenceFileName = "shared.asm", Subassembly = true, OccurrenceDocument = shared, Pose = Bridge.CadTransform(1000, 0, 0, 0, 0, 0) });
        var tree = JsonSerializer.SerializeToElement(Bridge.CadAssemblyTreeCore(root, 16, 100));
        Assert(tree.GetProperty("count").GetInt32() == 4 && !tree.GetProperty("truncated").GetBoolean(), "Repeated subassembly instances both traversed.");
        var nodes = tree.GetProperty("nodes");
        Assert(Math.Abs(nodes[1].GetProperty("WorldMatrix")[12].GetDouble() - .5) < 1e-9 && Math.Abs(nodes[1].GetProperty("WorldMatrix")[13].GetDouble() - .1) < 1e-9, "Rotated parent composes child translation.");
        Assert(Math.Abs(nodes[3].GetProperty("WorldMatrix")[12].GetDouble() - 1.1) < 1e-9, "Repeated child's second world transform.");
        Assert(nodes[1].GetProperty("KeyPath").GetString() != nodes[3].GetProperty("KeyPath").GetString(), "Repeated part reference keys scoped to their parents.");
        Assert(JsonSerializer.SerializeToElement(Bridge.CadAssemblyTreeCore(root, 16, 1)).GetProperty("truncated").GetBoolean(), "Occurrence limit reports incomplete traversal.");
        shared.Occurrences.Items.Add(new InspectionOccurrence { OccurrenceFileName = "root.asm", Subassembly = true, OccurrenceDocument = root });
        Assert(JsonSerializer.SerializeToElement(Bridge.CadAssemblyTreeCore(root, 16, 100)).GetProperty("truncated").GetBoolean(), "Cyclic references terminate explicitly.");
    }
    public sealed class InspectionPart(string name)
    {
        public string Name => name;
        public int Type => 1;
    }
    public sealed class InspectionModel(FakeCollection features) { public FakeCollection Features => features; }
    static void TestConceptMigration()
    {
        void Assert(bool value, string message) { if (!value) throw new Exception(message); }
        var model = ConceptMachine.Create("mill4", 500, 300, 300);
        var tracks = model.Axes.Select(a => new Track { Name = a.Id, Kind = a.Kind == "rotary" ? "部品回転" : "部品移動", Axis = a.Direction, Points = [new(0, 0), new(4, 50)] }).ToList();
        var sample = new Track(); tracks.Add(sample);
        var pattern = new MotionPattern { Name = "Original", Points = tracks.ToDictionary(t => t.Id, t => t.Points.ToList()) };
        var settings = new DocumentSettings { Version = 2, Tracks = tracks.Select(t => new SavedTrack { Track = t }).ToList(),
            Patterns = [pattern], ActivePatternId = pattern.Id,
            Concept = new SavedConcept { Model = model, Baseline = new(model.Values), Tracks = model.Axes.ToDictionary(a => a.Id, a => tracks.Single(t => t.Name == a.Id).Id) } };
        string source = settings.Json();
        var detail = JsonSerializer.Deserialize<ConceptMachine>(JsonSerializer.Serialize(model, ConceptMachine.JsonOptions), ConceptMachine.JsonOptions)!;
        detail.AssemblyFile = "detail.asm";
        var migrated = Bridge.BuildConceptMigration(source, detail, true);
        Assert(migrated.ActivePatternId == settings.ActivePatternId && migrated.Tracks.Select(t => t.Track.Id).SequenceEqual(tracks.Select(t => t.Id)), "Migration preserves pattern and track identity.");
        Assert(JsonSerializer.Serialize(migrated.Patterns) == JsonSerializer.Serialize(settings.Patterns), "Migration preserves all keyframes and patterns.");
        Assert(migrated.Tracks.Single(t => t.Track.Id == sample.Id).Hidden && !migrated.Tracks[0].Hidden, "Only unassigned samples hidden.");
        Assert(!Bridge.BuildConceptMigration(source, detail, false).Tracks.Last().Hidden, "Preserve option retains sample visibility.");
        detail.Axes[0].OriginMm[0] = 1;
        bool rejected = false; try { Bridge.BuildConceptMigration(source, detail, true); } catch (InvalidOperationException) { rejected = true; }
        Assert(rejected && settings.Json() == source, "Changed axes rejected without source mutation.");
        detail.Axes[0].OriginMm[0] = 0;
        settings.Patterns[0].Points[tracks[0].Id][1] = new(4, 999);
        rejected = false; try { Bridge.BuildConceptMigration(settings.Json(), detail, true); } catch (InvalidOperationException) { rejected = true; }
        Assert(rejected, "Inactive/active pattern points must respect axis limits.");
    }
    public sealed class InspectionDocument(string fileName)
    {
        public string FullName => fileName;
        public FakeCollection Occurrences { get; } = new();
    }
    public sealed class InspectionOccurrence : FakePart
    {
        public new void GetReferenceKey(ref byte[] key, [System.Runtime.InteropServices.Optional] ref object size)
        { key = PersistentId.ToByteArray(); size = key.Length; }
        public bool Subassembly { get; set; }
        public string OccurrenceFileName { get; set; } = "part.par";
        public InspectionDocument? OccurrenceDocument { get; set; }
    }
}
