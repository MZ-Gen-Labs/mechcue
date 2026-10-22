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
        // The engine runs in MechCue.Physics.exe (tested there); here a resting result stands in for it.
        var result = new RigidBodyResult { Engine = "test" };
        foreach (var axis in PhysicsTracks.FreeBodyAxes(model)!) result.Axes[axis] = [new() { Time = 0, Value = 0 }, new() { Time = 2, Value = 0.1 }];
        result.FingerAxes["F1"] = [new() { Time = 0, Value = 0 }, new() { Time = 1, Value = -5 }, new() { Time = 2, Value = 0 }];
        result.Events.Add(new() { Time = 1, Type = "finger-stopped", Detail = "F1 stopped at the workpiece" });
        result.Warnings.Add("test warning");
        PhysicsTracks.Apply(concept, result, byId, hash);
        var finger = tracks.Single(t => t.Name == "F1");
        Check(finger.At(1) == -5 && finger.Points.All(p => p.Value == 0) && !finger.Derived, "actual finger travel drives playback while commands stay editable");
        Check(PhysicsTracks.Inputs(concept, byId).Joints["F1"].All(p => p.Value == 0), "recomputation keeps the original commands");
        Check(MotionSampling.KeyPlan([finger], 0, 2).Contains(1) && MotionBoundMath.Variation(finger, 0, 2) == 10 && MotionBoundMath.MaximumAbs(finger, 0, 2) == 5, "inspection includes actual finger reversals and travel");
        finger.Points[1] = new(2, -10);
        Check(finger.At(1) == -5 && ReferenceEquals(finger.PlaybackPoints, finger.Points), "editing a finger invalidates its previous actual curve");
        finger.Points[1] = new(2, 0);
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
        Check(back.Tracks.Single(t => t.Track.Name == "F1").Track.At(1) == -5 && back.Concept!.Physics!.Report!.Events.Single().Type == "finger-stopped" && back.Concept.Physics.Report.Warnings.Single() == "test warning", "actual travel, events and warnings survive saving");
        Check(back.Tracks.Count(t => t.Track.Derived) == 6 && back.Concept!.Physics!.Setup.WorkpieceFriction == 0.9 && back.Concept.Physics.ComputedHash == hash, "derived flag and physics settings round-trip");
        // Moving the concept settings to the detailed assembly keeps the physics settings and the derived flag.
        foreach (var track in tracks) track.Points = [new(0, model.Values[track.Name]), new(2, model.Values[track.Name])];
        concept.Physics.Auto = false;
        var migrated = Bridge.BuildConceptMigration(settings.Json(), ConceptMachine.Create("arm5p", 500, 300, 300), false);
        Check(migrated.Concept!.Physics is { Auto: false, ComputedHash: not null } p && p.Setup.WorkpieceFriction == 0.9 && p.ComputedHash == concept.Physics.ComputedHash
            && migrated.Tracks.Count(t => t.Track.Derived) == 6, "migration keeps physics settings and physics tracks");
    }
}
