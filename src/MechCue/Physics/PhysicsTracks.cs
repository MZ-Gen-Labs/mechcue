using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MechCue.Physics;

/// <summary>Physics settings of a concept chart: saved with the assembly next to the concept axes.</summary>
public sealed class ConceptPhysicsSettings
{
    /// <summary>Recompute the physics tracks after the commanded tracks change.</summary>
    public bool Auto { get; set; } = true;
    public RigidBodySetup Setup { get; set; } = new();
    /// <summary>Fingerprint of the commanded tracks and settings the stored physics tracks were computed from.</summary>
    public string? ComputedHash { get; set; }
    public PhysicsReport? Report { get; set; }
}

public sealed class PhysicsReport
{
    public string Engine { get; set; } = "";
    public string FinalState { get; set; } = "";
    public double EndTime { get; set; }
    public int Samples { get; set; }
    public double MaxTiltDegrees { get; set; }
    public List<PhysicsEvent> Events { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}

/// <summary>
/// Commanded tracks (arm, fingers: keyframes edited by hand) drive the physics tracks (the workpiece's free-body axes):
/// the latter are computed by the rigid-body engine, are read-only and are recomputed when the former change.
/// This class holds the part that does not depend on the UI.
/// </summary>
public static class PhysicsTracks
{
    /// <summary>Axis ids of the workpiece's free-body chain (linear X/Y/Z then yaw/pitch/roll), or null when the model has none (everything but arm5p).</summary>
    public static IReadOnlyList<string>? FreeBodyAxes(ConceptMachine model, string bodyId = "workpiece")
    {
        var body = model.Bodies.FirstOrDefault(b => b.Id == bodyId); if (body == null) return null;
        var chain = new List<ConceptAxis>();
        for (var id = body.Parent; id != "";) { var a = model.Axes.Single(x => x.Id == id); chain.Insert(0, a); id = a.Parent; }
        int linear = chain.TakeWhile(x => x.Kind == "linear").Count(), rotary = chain.Count - linear;
        if (linear == 0 || rotary == 0 || chain.Skip(linear).Any(x => x.Kind != "rotary") || chain.Take(linear).Select(x => x.Direction).Distinct().Count() != linear) return null;
        if (!chain.Skip(linear).Select(x => x.Direction).SequenceEqual("ZYX".Take(rotary).Select(c => c.ToString()))) return null;
        if (model.Bodies.Any(b => b != body && chain.Any(x => x.Id == b.Parent))) return null;
        return chain.Select(x => x.Id).ToList();
    }

    public static void MarkDerived(SavedConcept concept, IEnumerable<Track> tracks)
    {
        var axes = FreeBodyAxes(concept.Model); if (axes == null) return;
        var byId = tracks.ToDictionary(t => t.Id);
        foreach (var axis in axes) if (concept.Tracks.TryGetValue(axis, out var id) && byId.TryGetValue(id, out var track)) track.Derived = true;
        concept.Physics ??= new() { Setup = concept.Model.PhysicsDefaults == null ? new() : JsonSerializer.Deserialize<RigidBodySetup>(JsonSerializer.Serialize(concept.Model.PhysicsDefaults))! };
    }

    static IEnumerable<(string Axis, Track Track)> Commanded(SavedConcept concept, IReadOnlyDictionary<Guid, Track> byId, IReadOnlyList<string> derived)
        => concept.Model.Axes.Where(a => !derived.Contains(a.Id)).Select(a => (a.Id, byId[concept.Tracks[a.Id]]));

    /// <summary>Fingerprint of everything the physics tracks depend on (commanded keyframes, settings and the active motion pattern).</summary>
    public static string Hash(SavedConcept concept, IReadOnlyDictionary<Guid, Track> byId, Guid pattern)
    {
        var derived = FreeBodyAxes(concept.Model) ?? throw new InvalidOperationException("The concept has no physics tracks.");
        var text = new StringBuilder("actual-fingers-v1|").Append(pattern).Append('|').Append(JsonSerializer.Serialize(concept.Model, ConceptMachine.JsonOptions)).Append('|').Append(JsonSerializer.Serialize(concept.Physics?.Setup ?? new RigidBodySetup()));
        if (concept.Model.Axes.All(a => derived.Contains(a.Id))) text.Append('|').Append(concept.Tracks.Values.Max(id => byId[id].Points[^1].Time).ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        foreach (var (axis, track) in Commanded(concept, byId, derived)) {
            text.Append('|').Append(axis);
            foreach (var p in track.Points) text.Append(';').Append(p.Time.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(',').Append(p.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    /// <summary>The simulation inputs, copied so a background run is not affected by later edits.</summary>
    public static (RigidBodySetup Setup, Dictionary<string, List<JointPoint>> Joints) Inputs(SavedConcept concept, IReadOnlyDictionary<Guid, Track> byId)
    {
        var derived = FreeBodyAxes(concept.Model) ?? throw new InvalidOperationException("The concept has no physics tracks.");
        var setup = JsonSerializer.Deserialize<RigidBodySetup>(JsonSerializer.Serialize(concept.Physics?.Setup ?? new RigidBodySetup()))!;
        var joints = new Dictionary<string, List<JointPoint>>();
        foreach (var (axis, track) in Commanded(concept, byId, derived)) joints[axis] = track.Points.Select(p => new JointPoint { Time = p.Time, Value = p.Value }).ToList();
        setup.EndTime = Math.Max(0.1, joints.Values.Select(p => p[^1].Time).DefaultIfEmpty(concept.Tracks.Values.Max(id => byId[id].Points[^1].Time)).Max());
        return (setup, joints);
    }

    /// <summary>Writes a result into the physics tracks. Values outside an axis range are clamped and reported.</summary>
    public static List<string> Apply(SavedConcept concept, RigidBodyResult result, IReadOnlyDictionary<Guid, Track> byId, string hash)
    {
        var warnings = new List<string>(result.Warnings);
        foreach (var id in concept.Tracks.Values) { byId[id].ActualPoints = null; byId[id].ActualSourcePoints = null; byId[id].ActualMotionCurrent = true; }
        foreach (var (axisId, points) in result.Axes) {
            var axis = concept.Model.Axes.Single(a => a.Id == axisId); var track = byId[concept.Tracks[axisId]];
            var list = points.Select(p => new KeyPoint(p.Time, Math.Clamp(p.Value, axis.Minimum, axis.Maximum))).ToList();
            if (list.Count < 2) list.Add(list[0] with { Time = list[0].Time + 1 });
            track.Points = list; track.Derived = true;
        }
        foreach (var (axisId, points) in result.FingerAxes) {
            var axis = concept.Model.Axes.Single(a => a.Id == axisId); var track = byId[concept.Tracks[axisId]];
            var actual = points.Select(p => new KeyPoint(p.Time, Math.Clamp(p.Value, axis.Minimum, axis.Maximum))).ToList();
            new Track { Points = actual }.Validate();
            track.ActualSourcePoints = track.Points.ToList(); track.ActualPoints = actual;
        }
        concept.Physics ??= new(); concept.Physics.ComputedHash = hash;
        concept.Physics.Report = new() { Engine = result.Engine, FinalState = result.FinalState, EndTime = result.EndTime, Samples = result.Samples,
            MaxTiltDegrees = result.MaxTiltDegrees, Events = result.Events.ToList(), Warnings = warnings.ToList() };
        return warnings;
    }
}
