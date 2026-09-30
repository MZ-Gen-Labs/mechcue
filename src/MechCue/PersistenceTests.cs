using System.Runtime.InteropServices;

namespace MechCue;
internal static class StorageTestFactory
{
    [DllImport("ole32.dll", CharSet = CharSet.Unicode, PreserveSig = false)] internal static extern void StgCreateDocfile(string name, uint mode, uint reserved, out IStorage storage);
    [DllImport("ole32.dll", CharSet = CharSet.Unicode, PreserveSig = false)] internal static extern void StgOpenStorage(string name, IStorage? priority, uint mode, IntPtr exclude, uint reserved, out IStorage storage);
    public static object Open(string path, int mode)
    {
        IStorage storage;
        if (File.Exists(path)) StgOpenStorage(path, null, mode == 0 ? 0x12u : (uint)mode, IntPtr.Zero, 0, out storage);
        else if (mode == 0) StgCreateDocfile(path, 0x1012, 0, out storage);
        else throw new COMException("Not found", unchecked((int)0x80030002));
        return storage;
    }
}
public static partial class SelfTest
{
    public class FakeAttributes
    {
        readonly Dictionary<string, object> items = new();
        public object Item(object key) => items[(string)key];
        public FakeAttributes Add(string key) { var result = new FakeAttributes(); items.Add(key, result); return result; }
        public FakeAttribute Add(string key, int type) { var result = new FakeAttribute(); items.Add(key, result); return result; }
    }
    public class FakeAttribute { public object Value { get; set; } = ""; }
    static void TestDocumentPersistence()
    {
        void Assert(bool ok, string message) { if (!ok) throw new Exception(message); }
        var doc = new FakeDocument(); var app = new FakeApplication { ActiveDocument = doc }; app.OpenDocuments.Items.Add(doc);
        var part = new FakePart(); var ground = new FakeGround(); part.Relations3d.Items.Add(ground); doc.Occurrences.Items.Add(part);
        var relation = new FakeRelation { Offset = .012 }; doc.Relations3d.Items.Add(relation);
        var coordinate = new Track { Name = "Coordinate", Kind = "部品座標", Points = [new(0,500),new(1,550)] };
        var offset = new Track { Name = "Offset", Points = [new(0,12),new(1,25)] };
        var bridge = new Bridge(app, doc);
        Assert(bridge.ReadSettings() == null && !doc.Dirty && !File.Exists(doc.StoragePath), "Reading clean model must not create storage");
        bridge.Bind(coordinate, bridge.Targets(coordinate.Kind).Single()); bridge.Bind(offset, bridge.Targets(offset.Kind).Single());
        var settings = new DocumentSettings { Tracks = [new() { Track = coordinate, Target = bridge.CaptureTarget(coordinate) }, new() { Track = offset, Target = bridge.CaptureTarget(offset), Hidden = true }], Speed = 2, Loop = true, Collision = true, DragStep = .5m };
        bridge.WriteSettings(settings.Json());
        var loaded = DocumentSettings.Parse(bridge.ReadSettings()!);
        Assert(loaded.Tracks[1].Hidden && loaded.Speed == 2 && loaded.Loop && loaded.Collision && loaded.DragStep == .5m, "Document settings roundtrip");
        bridge.Disconnect();
        part.Name = "Renamed"; relation.Offset = .02; part.Pose[12] = .8;
        // Reorder unrelated relations: never use the previous relation index/name as an identity.
        doc.Relations3d.Items.Insert(0, new FakeRelation { Offset = .999 });
        bridge = new Bridge(app, doc);
        foreach (var entry in loaded.Tracks) Assert(bridge.RestoreTarget(entry.Track, entry.Target!) == null, "Restore persistent identity");
        Assert(!ground.Suppress && part.Pose[12] == .8 && relation.Offset == .02, "Opening chart must not drive or suppress grounds");
        bridge.Disconnect();
        Assert(!ground.Suppress && part.Pose[12] == .8 && relation.Offset == .02, "Closing untouched restored chart must not change CAD");
        bridge = new Bridge(app, doc);
        foreach (var entry in loaded.Tracks) Assert(bridge.RestoreTarget(entry.Track, entry.Target!) == null, "Second restoration");
        bridge.Apply(1); Assert(ground.Suppress && Math.Abs(part.Pose[12] - .55) < 1e-10 && relation.Offset == .025, "Restored drivers apply values");
        bridge.Disconnect(); Assert(!ground.Suppress && part.Pose[12] == .5 && relation.Offset == .012, "Saved baseline and ground state restore");
        doc.Relations3d.Items.Remove(relation); bridge = new Bridge(app, doc);
        Assert(bridge.RestoreTarget(loaded.Tracks[1].Track, loaded.Tracks[1].Target!) != null && bridge.BindingCount == 0, "Missing target must remain unbound");
        Assert(bridge.CaptureTarget(loaded.Tracks[1].Track)!.AttributeId == loaded.Tracks[1].Target!.AttributeId, "Unresolved identity survives resave");
        bridge.Unbind(loaded.Tracks[1].Track); Assert(bridge.CaptureTarget(loaded.Tracks[1].Track) == null, "Explicit unbind clears unresolved identity");
        doc.Relations3d.Items.Add(relation); var duplicate = new FakeRelation { AttributeSets = relation.AttributeSets }; doc.Relations3d.Items.Add(duplicate);
        Assert(bridge.RestoreTarget(loaded.Tracks[1].Track, loaded.Tracks[1].Target!) != null && bridge.BindingCount == 0, "Duplicate ID must remain unbound");
        doc.ReadOnly = true;
        try { bridge.WriteSettings(settings.Json()); throw new Exception("Read-only storage accepted"); } catch (InvalidOperationException) { }
        Assert(DocumentSettings.Parse(bridge.ReadSettings()!).Tracks.Count == 2, "Rejected write preserves previous data");
        foreach (var invalid in new[] { "{\"Version\":2}", "{\"Version\":1,\"Tracks\":[]}", "{\"Version\":1,\"Tracks\":[null]}" })
            try { DocumentSettings.Parse(invalid); throw new Exception("Invalid settings accepted"); } catch (System.IO.InvalidDataException) { }
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "document-persistence-test-result.txt"), "PASS: native IStorage UTF-8 roundtrip, read without creation, settings, renamed/reordered targets, passive restore, saved baseline, ground state, missing/duplicate IDs, unresolved resave, read-only rejection, schema validation");
    }
}
