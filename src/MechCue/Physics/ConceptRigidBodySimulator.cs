using MechCue;

namespace MechCue.Physics;

public sealed class RigidBodySetup
{
    public string Workpiece { get; set; } = "workpiece";
    /// <summary>World direction of gravity (any non-zero vector; normalised).</summary>
    public double[] Gravity { get; set; } = [0, 0, -1];
    public double GravityMps2 { get; set; } = 9.81;
    public double WorkpieceMassKg { get; set; } = 0.5;
    public double WorkpieceFriction { get; set; } = 0.5;
    public double Restitution { get; set; }
    /// <summary>Bodies moved by the arm axes (hand, fingers, links) use this friction unless overridden in <see cref="Friction"/>.</summary>
    public double ArmFriction { get; set; } = 0.8;
    /// <summary>Fixed bodies (table, base) use this friction unless overridden in <see cref="Friction"/>.</summary>
    public double SupportFriction { get; set; } = 0.6;
    /// <summary>Friction per body id; overrides the defaults above.</summary>
    public Dictionary<string, double> Friction { get; set; } = [];
    /// <summary>Optional floor: a large static slab whose top is at this world height (mm). Null = no floor, things fall forever.</summary>
    public double? FloorZMm { get; set; }
    public double EndTime { get; set; }
    /// <summary>Spacing of the produced keyframes.</summary>
    public double OutputStepSeconds { get; set; } = 0.02;
    /// <summary>Physics steps per second (rounded up to a whole number of steps per output step).</summary>
    public double PhysicsHz { get; set; } = 240;
    /// <summary>The workpiece first rests for this long (with the arm held at t=0) so it settles before the chart starts.</summary>
    public double PreSettleSeconds { get; set; } = 0.3;
}

public sealed class RigidBodyResult
{
    public Dictionary<string, List<JointPoint>> Axes { get; } = [];
    public List<PhysicsEvent> Events { get; } = [];
    public List<string> Warnings { get; } = [];
    public string Engine { get; set; } = "";
    public string FinalState { get; set; } = "";
    public double EndTime { get; set; }
    public int Samples { get; set; }
    public double MaxTiltDegrees { get; set; }
}

/// <summary>
/// Runs a pick-and-place chart through a rigid-body engine. The fixed bodies of a concept model become static colliders,
/// the bodies moved by the arm/finger axes follow the chart as kinematic bodies, and the workpiece (a free-body axis chain,
/// as in arm5p) is a dynamic body. The workpiece's pose is turned back into keyframes of its WX/WY/WZ/WRZ/WRY/WRX axes.
/// </summary>
public static class ConceptRigidBodySimulator
{
    static double[] Translation(double xMm, double yMm, double zMm) => [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, xMm / 1000, yMm / 1000, zMm / 1000, 1];
    static PhysicsShape ShapeOf(ConceptBody b) => b.Shape == "cylinder"
        ? new(PhysicsShapeKind.CylinderZ, b.SizeMm[0] / 1000, b.SizeMm[1] / 1000, b.SizeMm[2] / 1000)
        : new(PhysicsShapeKind.Box, b.SizeMm[0] / 1000, b.SizeMm[1] / 1000, b.SizeMm[2] / 1000);

    public static RigidBodyResult Run(ConceptMachine model, RigidBodySetup setup, IReadOnlyDictionary<string, List<JointPoint>> joints, Func<IRigidBodyEngine>? createEngine = null)
    {
        model.Validate();
        var work = model.Bodies.FirstOrDefault(b => b.Id == setup.Workpiece) ?? throw new ArgumentException("Workpiece body not found: " + setup.Workpiece);
        var chain = new List<ConceptAxis>();
        for (var id = work.Parent; id != "";) { var a = model.Axes.Single(x => x.Id == id); chain.Insert(0, a); id = a.Parent; }
        if (chain.Count == 0 || chain.Any(a => model.Bodies.Any(b => b.Parent == a.Id && b != work)))
            throw new ArgumentException("The workpiece must hang from its own free-body axes (use the arm5p template).");
        var freeAxes = chain.Select(a => a.Id).ToHashSet();
        if (setup.Gravity.Length != 3 || setup.Gravity.Any(v => !double.IsFinite(v)) || setup.Gravity.All(v => v == 0)) throw new ArgumentException("Gravity must be a non-zero 3-vector.");
        if (!(setup.WorkpieceMassKg > 0 && setup.WorkpieceMassKg <= 1000)) throw new ArgumentException("WorkpieceMassKg must be in (0, 1000].");
        if (!(setup.GravityMps2 > 0 && setup.GravityMps2 <= 100)) throw new ArgumentException("GravityMps2 must be in (0, 100].");
        foreach (double f in new[] { setup.WorkpieceFriction, setup.ArmFriction, setup.SupportFriction }.Concat(setup.Friction.Values)) if (!(f >= 0 && f <= 20)) throw new ArgumentException("Friction coefficients must be 0..20.");
        foreach (var id in setup.Friction.Keys) if (model.Bodies.All(b => b.Id != id)) throw new ArgumentException("Friction for an unknown body: " + id);
        if (!(setup.OutputStepSeconds >= 0.005 && setup.OutputStepSeconds <= 1)) throw new ArgumentException("OutputStepSeconds must be 0.005..1.");
        if (!(setup.PhysicsHz >= 30 && setup.PhysicsHz <= 2000)) throw new ArgumentException("PhysicsHz must be 30..2000.");
        if (!(setup.PreSettleSeconds >= 0 && setup.PreSettleSeconds <= 10)) throw new ArgumentException("PreSettleSeconds must be 0..10.");
        foreach (var key in joints.Keys) if (!model.Axes.Any(a => a.Id == key) || freeAxes.Contains(key)) throw new ArgumentException("Trajectory for an unknown or workpiece axis: " + key);
        foreach (var points in joints.Values)
            if (points.Count == 0 || points.Any(p => !double.IsFinite(p.Time) || !double.IsFinite(p.Value) || p.Time < 0) || points.Zip(points.Skip(1)).Any(p => p.Second.Time <= p.First.Time)) throw new ArgumentException("Trajectories need strictly increasing nonnegative times.");
        double end = setup.EndTime > 0 ? setup.EndTime : joints.Values.Select(p => p[^1].Time).DefaultIfEmpty(0).Max();
        if (!(end > 0 && end <= 600)) throw new ArgumentException("EndTime must be 0..600 seconds (or implied by the trajectories).");
        double outStep = setup.OutputStepSeconds; int sub = (int)Math.Ceiling(outStep * setup.PhysicsHz - 1e-9), outputs = (int)Math.Ceiling(end / outStep - 1e-9);
        if (outputs > 50000 || (long)outputs * sub > 2_000_000) throw new ArgumentException("Too many steps; raise OutputStepSeconds or lower PhysicsHz.");
        double dt = outStep / sub;

        double Lerp(List<JointPoint> p, double t)
        {
            if (t <= p[0].Time) return p[0].Value; if (t >= p[^1].Time) return p[^1].Value;
            int i = p.FindIndex(x => x.Time >= t); double u = (t - p[i - 1].Time) / (p[i].Time - p[i - 1].Time); return p[i - 1].Value + u * (p[i].Value - p[i - 1].Value);
        }
        Dictionary<string, double[]> BodyFrames(double t)
        {
            var values = model.Axes.ToDictionary(a => a.Id, a => joints.TryGetValue(a.Id, out var p) ? Lerp(p, t) : model.Values[a.Id]);
            var axisPoses = model.AxisPoses(values);
            // Concept frame of each box: parent axis frame then the box centre. (The CAD geometry matrix only orients the native part.)
            return model.Bodies.ToDictionary(b => b.Id, b => ConceptMachine.Multiply(axisPoses[b.Parent], Translation(b.CenterMm[0], b.CenterMm[1], b.CenterMm[2])));
        }

        var result = new RigidBodyResult { EndTime = end };
        using var engine = (createEngine ?? (() => new BepuEngine()))();
        result.Engine = engine.Name;
        double gl = Math.Sqrt(setup.Gravity.Sum(v => v * v));
        engine.SetGravity(setup.Gravity[0] / gl * setup.GravityMps2, setup.Gravity[1] / gl * setup.GravityMps2, setup.Gravity[2] / gl * setup.GravityMps2);
        var start = BodyFrames(0); var kinematic = new List<string>();
        foreach (var b in model.Bodies) {
            double mu = setup.Friction.TryGetValue(b.Id, out var f) ? f : b.Id == work.Id ? setup.WorkpieceFriction : b.Parent == "" ? setup.SupportFriction : setup.ArmFriction;
            var material = new PhysicsMaterial(mu, b.Id == work.Id ? setup.Restitution : 0);
            var pose = RigidPose.FromMatrix(start[b.Id]);
            if (b.Id == work.Id) engine.AddDynamic(b.Id, ShapeOf(b), pose, material, setup.WorkpieceMassKg);
            else if (b.Parent == "") engine.AddStatic(b.Id, ShapeOf(b), pose, material);
            else { engine.AddKinematic(b.Id, ShapeOf(b), pose, material); kinematic.Add(b.Id); }
        }
        if (setup.FloorZMm is double floorZ) engine.AddStatic("floor-plane", new(PhysicsShapeKind.Box, 40, 40, 0.2), RigidPose.Identity with { Z = floorZ / 1000 - 0.1 }, new(setup.SupportFriction));
        void Step(double timeAfterStep)
        {
            var frames = BodyFrames(timeAfterStep);
            foreach (var id in kinematic) engine.SetKinematicTarget(id, RigidPose.FromMatrix(frames[id]));
            engine.Step(dt);
        }
        for (int i = 0, n = (int)Math.Round(setup.PreSettleSeconds / dt); i < n; i++) Step(0);

        var home = RigidPose.FromMatrix(start[work.Id]);
        double lastYaw = 0, lastPitch = 0, lastRoll = 0; bool first = true;
        var samples = new List<(double T, Dictionary<string, double> V)>();
        double restHeight = engine.GetPose(work.Id).Z, maxTilt = 0, calmSince = -1; bool lifted = false, settled = false; string state = "resting";
        Dictionary<string, double> Record(double t)
        {
            var p = engine.GetPose(work.Id);
            var values = Bridge.MeasureFreeBody(model, work.Id, ConceptMachine.Multiply(p.ToMatrix(), work.GeometryMatrix));
            string yawId = chain.First(a => a.Kind == "rotary" && a.Direction == "Z").Id, pitchId = chain.First(a => a.Direction == "Y" && a.Kind == "rotary").Id, rollId = chain.First(a => a.Direction == "X" && a.Kind == "rotary").Id;
            double Unwrap(double value, ref double last) { if (!first) while (value - last > 180) value -= 360; if (!first) while (value - last < -180) value += 360; last = value; return value; }
            double yaw = Unwrap(values[yawId], ref lastYaw), pitch = Unwrap(values[pitchId], ref lastPitch), roll = Unwrap(values[rollId], ref lastRoll);
            first = false; values[yawId] = yaw; values[pitchId] = pitch; values[rollId] = roll;
            var m = p.ToMatrix(); double tilt = Math.Acos(Math.Clamp(m[10], -1, 1)) * 180 / Math.PI; // angle between the workpiece's Z axis and world Z
            maxTilt = Math.Max(maxTilt, tilt);
            var v = engine.GetLinearVelocity(work.Id); double speed = Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
            if (!lifted && p.Z > restHeight + 0.005) { lifted = true; result.Events.Add(new() { Time = t, Type = "lift-off", Detail = $"the workpiece rose {1000 * (p.Z - restHeight):0.#} mm above its resting height" }); }
            if (speed < 0.02) { if (calmSince < 0) calmSince = t; } else { calmSince = -1; settled = false; }
            if (calmSince >= 0 && t - calmSince >= 0.25 && !settled && (lifted || t > setup.PreSettleSeconds)) {
                settled = true; if (lifted) result.Events.Add(new() { Time = t, Type = "settled", Detail = $"at rest, tilt {tilt:0.#}°, height {1000 * p.Z:0.#} mm" });
            }
            state = speed < 0.02 ? "resting" : "moving";
            return values;
        }
        samples.Add((0, Record(0)));
        for (int i = 1; i <= outputs; i++) {
            double t = Math.Min(i * outStep, end);
            for (int k = 1; k <= sub; k++) Step((i - 1) * outStep + k * dt);
            samples.Add((t, Record(t)));
        }
        result.MaxTiltDegrees = maxTilt; result.Samples = samples.Count;
        var final = engine.GetPose(work.Id);
        if (final.Z < home.Z - 0.4) result.Warnings.Add("The workpiece ended more than 400 mm below its start (it fell off the table).");
        double finalTilt = Math.Acos(Math.Clamp(final.ToMatrix()[10], -1, 1)) * 180 / Math.PI;
        if (finalTilt > 30) result.Events.Add(new() { Time = end, Type = "tipped", Detail = $"final tilt {finalTilt:0.#}°" });
        result.FinalState = state == "moving" ? "moving" : finalTilt > 30 ? "tipped" : "resting";
        foreach (var axis in chain) {
            var raw = samples.Select(s => new JointPoint { Time = Math.Round(s.T, 6), Value = Math.Round(s.V[axis.Id], 4) }).ToList();
            var kept = new List<JointPoint> { raw[0] };
            for (int i = 1; i < raw.Count - 1; i++) {
                var p0 = kept[^1]; var n1 = raw[i + 1]; double u = (raw[i].Time - p0.Time) / (n1.Time - p0.Time);
                if (Math.Abs(raw[i].Value - (p0.Value + u * (n1.Value - p0.Value))) > 0.001) kept.Add(raw[i]);
            }
            kept.Add(raw[^1]);
            foreach (var p in kept) if (p.Value < axis.Minimum || p.Value > axis.Maximum) { result.Warnings.Add($"{axis.Id} leaves its range [{axis.Minimum}, {axis.Maximum}] at t={p.Time:0.###}s."); break; }
            result.Axes[axis.Id] = kept;
        }
        return result;
    }
}

public static partial class ConceptRigidBodyApi
{
    public static object Simulate(string manifestPath, string jointsJson, string physicsJson)
    {
        if (!Path.IsPathFullyQualified(manifestPath) || !string.Equals(Path.GetExtension(manifestPath), ".json", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("manifestPath must be an absolute .json path.");
        manifestPath = Path.GetFullPath(manifestPath);
        if (new FileInfo(manifestPath).Length > 1024 * 1024) throw new ArgumentException("Concept manifest exceeds 1 MB.");
        var model = System.Text.Json.JsonSerializer.Deserialize<ConceptMachine>(File.ReadAllText(manifestPath), ConceptMachine.JsonOptions) ?? throw new ArgumentException("Invalid concept manifest.");
        var joints = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, List<JointPoint>>>(string.IsNullOrWhiteSpace(jointsJson) ? "{}" : jointsJson, ConceptMachine.JsonOptions) ?? throw new ArgumentException("jointsJson must be an object {axisId:[{time,value},...]}.");
        var setup = string.IsNullOrWhiteSpace(physicsJson) ? new RigidBodySetup() : System.Text.Json.JsonSerializer.Deserialize<RigidBodySetup>(physicsJson, ConceptMachine.JsonOptions) ?? throw new ArgumentException("Invalid physicsJson.");
        var r = ConceptRigidBodySimulator.Run(model, setup, joints);
        return new {
            engine = r.Engine, finalState = r.FinalState, endTime = r.EndTime, samples = r.Samples, maxTiltDegrees = Math.Round(r.MaxTiltDegrees, 1), events = r.Events, warnings = r.Warnings, axisKeyframes = r.Axes,
            note = "Keyframes (mm / degrees) for the workpiece's free-body axes. Apply them with mechcue_set_keyframes after looking up each axis' trackId. Rigid-body model of boxes and cylinders; the hand's squeeze comes from the finger axes F1/F2 in the supplied trajectories."
        };
    }
}
