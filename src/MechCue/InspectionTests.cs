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
