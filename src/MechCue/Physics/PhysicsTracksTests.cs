using MechCue.Physics;

namespace MechCue;

public static partial class SelfTest
{
    static void TestPhysicsTracks()
    {
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        Check(PhysicsTracks.FreeBodyAxes(ConceptMachine.Create("arm5", 500, 300, 300)) == null, "arm5 has no physics tracks");
        var model = ConceptMachine.Create("arm5p", 500, 300, 300);
        Check(string.Join(",", PhysicsTracks.FreeBodyAxes(model)!) == "WX,WY,WZ,WRZ,WRY,WRX", "arm5p physics axes");
        var tracks = model.Axes.Select(a => new Track { Name = a.Id, Kind = a.Kind == "rotary" ? "部品回転" : "部品移動", Axis = a.Direction, Points = [new(0, 0), new(2, 0)] }).ToList();
        var concept = new SavedConcept { Model = model, Baseline = new(model.Values), Tracks = model.Axes.Select((a, i) => (a.Id, tracks[i].Id)).ToDictionary(p => p.Item1, p => p.Item2) };
        PhysicsTracks.MarkDerived(concept, tracks);
        Check(tracks.Where(t => t.Derived).Select(t => t.Name).OrderBy(n => n).SequenceEqual(["WRX", "WRY", "WRZ", "WX", "WY", "WZ"]) && concept.Physics != null && concept.Physics.Auto, "physics tracks are flagged derived and auto is on");
        var byId = tracks.ToDictionary(t => t.Id); var pattern = Guid.NewGuid();
        string hash = PhysicsTracks.Hash(concept, byId, pattern);
        var (setup, joints) = PhysicsTracks.Inputs(concept, byId);
        Check(!joints.ContainsKey("WX") && joints.ContainsKey("J1") && joints.ContainsKey("F1") && setup.EndTime == 2, "only commanded tracks are inputs");
        var result = PhysicsTracks.Run(concept, setup, joints);
        PhysicsTracks.Apply(concept, result, byId, hash);
        Check(concept.Physics!.ComputedHash == hash && tracks.Single(t => t.Name == "WZ").Points.All(p => Math.Abs(p.Value) < 0.5), "a resting workpiece stays at zero");
        Check(PhysicsTracks.Hash(concept, byId, pattern) == hash, "writing physics tracks does not change the fingerprint");
        // Editing a commanded keyframe, the settings or the motion pattern makes the stored result out of date.
        tracks.Single(t => t.Name == "J1").Points = [new(0, 0), new(2, 10)];
        string edited = PhysicsTracks.Hash(concept, byId, pattern); Check(edited != hash, "editing a commanded track changes the fingerprint");
        concept.Physics.Setup.WorkpieceFriction = 0.9; Check(PhysicsTracks.Hash(concept, byId, pattern) != edited, "settings change the fingerprint");
        Check(PhysicsTracks.Hash(concept, byId, Guid.NewGuid()) != PhysicsTracks.Hash(concept, byId, pattern), "the motion pattern changes the fingerprint");
        // The flag and settings survive a save and load.
        var settings = new DocumentSettings { Concept = concept, Tracks = tracks.Select(t => new SavedTrack { Track = t }).ToList(), Version = 2 };
        var back = System.Text.Json.JsonSerializer.Deserialize<DocumentSettings>(settings.Json())!;
        Check(back.Tracks.Count(t => t.Track.Derived) == 6 && back.Concept!.Physics!.Setup.WorkpieceFriction == 0.9 && back.Concept.Physics.ComputedHash == hash, "derived flag and physics settings round-trip");
        if (PhysicsSelfTest.Run() != 0) throw new Exception("Physics engine self-test failed");
    }
}
