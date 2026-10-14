using System.Text.Json;
namespace MechCue;

public sealed class GripEvent { public double Time { get; set; } public bool Closed { get; set; } }
public sealed class JointPoint { public double Time { get; set; } public double Value { get; set; } }
public sealed class ConceptPhysicsSetup
{
    public string Workpiece { get; set; } = "workpiece";
    /// <summary>Direction of gravity in the world frame (any non-zero vector; normalised).</summary>
    public double[] Gravity { get; set; } = [0, 0, -1];
    public double GravityMps2 { get; set; } = 9.81;
    /// <summary>Bodies the workpiece can rest on. Empty = every fixed (parentless) body except the workpiece.</summary>
    public List<string> Supports { get; set; } = [];
    /// <summary>Gripping surfaces (bodies). Empty = finger-1 and finger-2. They must share one parent axis, which carries the held workpiece.</summary>
    public List<string> GripBodies { get; set; } = [];
    /// <summary>A close command grasps only if every grip body is at most this far from the workpiece.</summary>
    public double GripToleranceMm { get; set; } = 5;
    public List<GripEvent> Grip { get; set; } = [];
    public double EndTime { get; set; }
    public double StepSeconds { get; set; } = 0.02;
    /// <summary>true: the workpiece leaves the hand with the hand's velocity; false: it just drops.</summary>
    public bool InheritReleaseVelocity { get; set; }
}
public sealed class PhysicsEvent { public double Time { get; init; } public string Type { get; init; } = ""; public string Detail { get; init; } = ""; }
public sealed class ConceptPhysicsResult
{
    public Dictionary<string, List<JointPoint>> Axes { get; } = [];
    public List<PhysicsEvent> Events { get; } = [];
    public List<string> Warnings { get; } = [];
    public string FinalState { get; set; } = "";
    public double EndTime { get; set; }
    public int Samples { get; set; }
}

/// <summary>
/// Geometric rigid-body model of a pick-and-place: a workpiece rests on support bodies, is lifted by grip bodies on
/// a close command and falls along gravity after an open command until the first support. The result is a set of
/// keyframes for the workpiece's X/Y/Z carrier axes. No friction, bounce, tipping or contact forces.
/// </summary>
public static class ConceptPhysics
{
    enum State { Resting, Held, Falling, Lost }
    const double Tiny = 1e-9, Touch = 1e-6;

    public static ConceptPhysicsResult Run(ConceptMachine model, ConceptPhysicsSetup setup, IReadOnlyDictionary<string, List<JointPoint>> joints)
    {
        model.Validate();
        var work = model.Bodies.FirstOrDefault(b => b.Id == setup.Workpiece) ?? throw new ArgumentException("Workpiece body not found: " + setup.Workpiece);
        var carrier = new string?[3];
        for (var id = work.Parent; id != "";) {
            var a = model.Axes.Single(x => x.Id == id); int k = "XYZ".IndexOf(a.Direction);
            if (a.Kind != "linear" || carrier[k] != null || a.OriginMm.Any(v => v != 0)) throw new ArgumentException("The workpiece must hang from linear carrier axes with zero origin, one per X/Y/Z.");
            carrier[k] = a.Id; id = a.Parent;
        }
        if (carrier.Any(c => c == null)) throw new ArgumentException("The workpiece needs X, Y and Z carrier axes (arm5: WX, WY, WZ).");
        var carrierAxes = carrier.Select(id => model.Axes.Single(a => a.Id == id)).ToArray();

        if (setup.Gravity.Length != 3 || setup.Gravity.Any(v => !double.IsFinite(v)) || setup.Gravity.All(v => v == 0)) throw new ArgumentException("Gravity must be a non-zero 3-vector.");
        double gl = Math.Sqrt(setup.Gravity.Sum(v => v * v)); double[] gdir = setup.Gravity.Select(v => v / gl).ToArray();
        double G = setup.GravityMps2; if (!double.IsFinite(G) || G <= 0 || G > 100) throw new ArgumentException("GravityMps2 must be in (0, 100].");
        if (!double.IsFinite(setup.GripToleranceMm) || setup.GripToleranceMm < 0 || setup.GripToleranceMm > 100) throw new ArgumentException("GripToleranceMm must be 0..100.");
        double step = setup.StepSeconds; if (!double.IsFinite(step) || step < 0.005 || step > 1) throw new ArgumentException("StepSeconds must be 0.005..1.");

        var supportIds = setup.Supports.Count > 0 ? setup.Supports : model.Bodies.Where(b => b.Parent == "" && b.Id != work.Id).Select(b => b.Id).ToList();
        foreach (var id in supportIds) if (id == work.Id || model.Bodies.All(b => b.Id != id)) throw new ArgumentException("Unknown support body: " + id);
        if (supportIds.Count == 0) throw new ArgumentException("No support bodies.");
        var gripIds = setup.GripBodies.Count > 0 ? setup.GripBodies : new List<string> { "finger-1", "finger-2" };
        foreach (var id in gripIds) if (model.Bodies.All(b => b.Id != id) || id == work.Id) throw new ArgumentException("Unknown grip body: " + id + ". Specify gripBodies.");
        var frameAxes = gripIds.Select(id => model.Bodies.Single(b => b.Id == id).Parent).Distinct().ToList();
        if (frameAxes.Count != 1 || frameAxes[0] == "") throw new ArgumentException("Grip bodies must share one parent axis.");
        string frameAxis = frameAxes[0];

        var events = setup.Grip.OrderBy(e => e.Time).ToList();
        if (events.Any(e => !double.IsFinite(e.Time) || e.Time < 0)) throw new ArgumentException("Grip event times must be finite and nonnegative.");
        foreach (var key in joints.Keys) if (!model.Axes.Any(a => a.Id == key) || carrier.Contains(key)) throw new ArgumentException("Joint trajectory for an unknown or carrier axis: " + key);
        foreach (var points in joints.Values) {
            if (points.Count == 0 || points.Any(p => !double.IsFinite(p.Time) || !double.IsFinite(p.Value) || p.Time < 0) || points.Zip(points.Skip(1)).Any(p => p.Second.Time <= p.First.Time)) throw new ArgumentException("Joint keyframes need strictly increasing nonnegative times.");
        }
        double end = setup.EndTime > 0 ? setup.EndTime : Math.Max(joints.Values.Select(p => p[^1].Time).DefaultIfEmpty(0).Max(), events.Select(e => e.Time).DefaultIfEmpty(0).Max());
        if (!(end > 0 && end <= 3600)) throw new ArgumentException("EndTime must be 0..3600 seconds (or implied by joint keyframes / grip events).");
        if (end / step > 100000) throw new ArgumentException("Too many samples; increase StepSeconds.");

        var times = new List<double>();
        for (int k = 0; k * step < end - Tiny; k++) times.Add(k * step);
        times.Add(end); times.AddRange(events.Where(e => e.Time <= end).Select(e => e.Time));
        times.Sort(); var grid = new List<double>();
        foreach (double t in times) if (grid.Count == 0 || t - grid[^1] > Tiny) grid.Add(t);

        double Lerp(List<JointPoint> p, double t) {
            if (t <= p[0].Time) return p[0].Value; if (t >= p[^1].Time) return p[^1].Value;
            int i = p.FindIndex(x => x.Time >= t); double u = (t - p[i - 1].Time) / (p[i].Time - p[i - 1].Time); return p[i - 1].Value + u * (p[i].Value - p[i - 1].Value);
        }
        var w = new double[3];
        Dictionary<string, double> Values(double t) {
            var v = new Dictionary<string, double>();
            foreach (var a in model.Axes) { int k = Array.IndexOf(carrier, a.Id); v[a.Id] = k >= 0 ? w[k] : joints.TryGetValue(a.Id, out var p) ? Lerp(p, t) : model.Values[a.Id]; }
            return v;
        }
        var result = new ConceptPhysicsResult { EndTime = end };
        bool Clamp(string what, double t) {
            bool clamped = false;
            for (int k = 0; k < 3; k++) {
                double c = Math.Clamp(w[k], carrierAxes[k].Minimum, carrierAxes[k].Maximum);
                if (c != w[k]) { w[k] = c; clamped = true; }
            }
            if (clamped) result.Warnings.Add($"t={t:0.###}s: {what} left the carrier axis range; the workpiece is stopped at the limit and treated as lost.");
            return clamped;
        }
        double[] Aabb(double[] pose, ConceptBody b) {
            var corners = Enumerable.Range(0, 8).Select(k => new[] { ((k & 1) == 0 ? -1 : 1) * b.SizeMm[0] / 2000, ((k & 2) == 0 ? -1 : 1) * b.SizeMm[1] / 2000, ((k & 4) == 0 ? -1 : 1) * b.SizeMm[2] / 2000 })
                .Select(c => Enumerable.Range(0, 3).Select(r => pose[r] * c[0] + pose[4 + r] * c[1] + pose[8 + r] * c[2] + pose[12 + r]).ToArray()).ToList();
            return [.. Enumerable.Range(0, 3).Select(r => corners.Min(c => c[r])), .. Enumerable.Range(0, 3).Select(r => corners.Max(c => c[r]))];
        }
        var homePoses = model.Poses(Values(0));
        double[] homeBox = Aabb(homePoses[work.Id], work), homeCenter = [homePoses[work.Id][12], homePoses[work.Id][13], homePoses[work.Id][14]];
        double[] WorkBox() => [homeBox[0] + w[0] / 1000, homeBox[1] + w[1] / 1000, homeBox[2] + w[2] / 1000, homeBox[3] + w[0] / 1000, homeBox[4] + w[1] / 1000, homeBox[5] + w[2] / 1000];
        double[] Center() => [homeCenter[0] + w[0] / 1000, homeCenter[1] + w[1] / 1000, homeCenter[2] + w[2] / 1000];

        // First support hit along displacement d (metres) of the workpiece box: fraction 0..1 of d, or null. A start that already overlaps reports 0 and penetrating.
        (double S, string Body, bool Penetrating)? FirstHit(double[] d, Dictionary<string, double[]> poses) {
            double dLen = Math.Sqrt(d.Sum(v => v * v)); (double, string, bool)? best = null; var a = WorkBox();
            foreach (var id in supportIds) {
                var b = Aabb(poses[id], model.Bodies.Single(x => x.Id == id)); double enter = double.NegativeInfinity, exit = double.PositiveInfinity; bool miss = false;
                for (int r = 0; r < 3 && !miss; r++) {
                    if (Math.Abs(d[r]) < 1e-15) { if (a[r + 3] <= b[r] + Tiny || a[r] >= b[r + 3] - Tiny) miss = true; continue; }
                    double t0, t1;
                    if (d[r] > 0) { t0 = (b[r] - a[r + 3]) / d[r]; t1 = (b[r + 3] - a[r]) / d[r]; } else { t0 = (b[r + 3] - a[r]) / d[r]; t1 = (b[r] - a[r + 3]) / d[r]; }
                    enter = Math.Max(enter, t0); exit = Math.Min(exit, t1);
                }
                if (miss || enter > exit || enter > 1 || exit <= 0) continue;
                bool pen = enter * dLen < -Touch; double s = Math.Max(enter, 0);
                if (best == null || s < best.Value.Item1) best = (s, id, pen);
            }
            return best == null ? null : (best.Value.Item1, best.Value.Item2, best.Value.Item3);
        }
        (double S, string Body, bool Penetrating)? Supported(double t) => FirstHit(gdir.Select(v => v * Touch).ToArray(), model.Poses(Values(t)));
        void Penetration(double t, string body) => result.Warnings.Add($"t={t:0.###}s: the workpiece overlaps support {body}; check the placement height.");

        var state = State.Falling; double fallStart = 0; double[] v0 = [0, 0, 0], heldVelocity = [0, 0, 0], offsetLocal = [0, 0, 0], rot0 = new double[9];
        double maxDrift = 0; var samples = new List<(double T, double[] W)>();

        double[]? Frame(double t) => model.AxisPoses(Values(t))[frameAxis];
        double Gap(double[] a, double[] b) { double s = 0; for (int r = 0; r < 3; r++) { double g = Math.Max(0, Math.Max(a[r] - b[r + 3], b[r] - a[r + 3])); s += g * g; } return Math.Sqrt(s); }
        void Place(double t, string kind, string body, bool pen) {
            state = State.Resting; v0 = [0, 0, 0];
            result.Events.Add(new() { Time = t, Type = kind, Detail = body }); if (pen) Penetration(t, body);
        }
        void StartFall(double t) {
            fallStart = t; state = State.Falling;
            var hit = Supported(t); if (hit != null) Place(t, "placed", hit.Value.Body, hit.Value.Penetrating);
        }

        var first = Supported(0);
        if (first != null) { state = State.Resting; if (first.Value.Penetrating) Penetration(0, first.Value.Body); } else { state = State.Falling; fallStart = 0; result.Events.Add(new() { Time = 0, Type = "falling", Detail = "no support below the workpiece at t=0" }); }

        int nextEvent = 0;
        for (int i = 0; i < grid.Count; i++) {
            double t = grid[i];
            if (i > 0 && state == State.Held) {
                var f = Frame(t)!; var off = offsetLocal; double dt = t - grid[i - 1];
                var center = Enumerable.Range(0, 3).Select(r => f[12 + r] + f[r] * off[0] + f[4 + r] * off[1] + f[8 + r] * off[2]).ToArray();
                var before = (double[])w.Clone();
                for (int k = 0; k < 3; k++) w[k] = (center[k] - homeCenter[k]) * 1000;
                heldVelocity = Enumerable.Range(0, 3).Select(k => (w[k] - before[k]) / 1000 / dt).ToArray();
                if (Clamp("the hand carrying the workpiece", t)) state = State.Lost;
                double dot = 0; for (int k = 0; k < 9; k++) dot += rot0[k] * f[k / 3 * 4 + k % 3]; maxDrift = Math.Max(maxDrift, Math.Acos(Math.Clamp((dot - 1) / 2, -1, 1)) * 180 / Math.PI);
            } else if (i > 0 && state == State.Falling) {
                double tauPrev = grid[i - 1] - fallStart, tau = t - fallStart;
                double[] Disp(double q) => Enumerable.Range(0, 3).Select(r => v0[r] * q + 0.5 * G * gdir[r] * q * q).ToArray();
                var dPrev = Disp(tauPrev); var d = Disp(tau).Zip(dPrev, (x, y) => x - y).ToArray();
                var hit = FirstHit(d, model.Poses(Values(t)));
                if (hit != null) {
                    var (s, body, pen) = hit.Value;
                    for (int k = 0; k < 3; k++) w[k] += d[k] * s * 1000;
                    double v0g = Enumerable.Range(0, 3).Sum(r => v0[r] * gdir[r]);
                    double Lg(double q) => v0g * q + 0.5 * G * q * q;
                    double target = Lg(tauPrev) + s * (Lg(tau) - Lg(tauPrev)), disc = Math.Max(0, v0g * v0g + 2 * G * target);
                    double land = Math.Clamp((-v0g + Math.Sqrt(disc)) / G, tauPrev, tau) + fallStart;
                    if (land - grid[i - 1] > 1e-4 && t - land > 1e-4) samples.Add((land, (double[])w.Clone()));
                    Place(land, "landed", body, pen);
                } else {
                    for (int k = 0; k < 3; k++) w[k] += d[k] * 1000;
                    if (Clamp("the falling workpiece", t)) { state = State.Lost; result.Events.Add(new() { Time = t, Type = "lost", Detail = "no support below within the carrier range" }); }
                }
            }
            while (nextEvent < events.Count && events[nextEvent].Time <= t + Tiny) {
                var e = events[nextEvent++];
                if (state == State.Lost) continue;
                if (e.Closed && state != State.Held) {
                    var poses = model.Poses(Values(t)); var wb = WorkBox();
                    double gap = gripIds.Max(id => Gap(Aabb(poses[id], model.Bodies.Single(b => b.Id == id)), wb)) * 1000;
                    if (gap <= setup.GripToleranceMm + Tiny) {
                        var f = Frame(t)!; var c = Center();
                        offsetLocal = Enumerable.Range(0, 3).Select(k => (c[0] - f[12]) * f[k * 4] + (c[1] - f[13]) * f[k * 4 + 1] + (c[2] - f[14]) * f[k * 4 + 2]).ToArray();
                        for (int k = 0; k < 9; k++) rot0[k] = f[k / 3 * 4 + k % 3];
                        heldVelocity = [0, 0, 0]; state = State.Held; maxDrift = 0;
                        result.Events.Add(new() { Time = t, Type = "grasp", Detail = $"largest grip gap {gap:0.###} mm" });
                    } else result.Events.Add(new() { Time = t, Type = "grip-missed", Detail = $"largest grip gap {gap:0.###} mm exceeds {setup.GripToleranceMm:0.###} mm; the workpiece stays where it is" });
                } else if (!e.Closed && state == State.Held) {
                    v0 = setup.InheritReleaseVelocity ? (double[])heldVelocity.Clone() : [0, 0, 0];
                    result.Events.Add(new() { Time = t, Type = "release", Detail = "" });
                    StartFall(t);
                }
            }
            samples.Add((t, (double[])w.Clone()));
        }
        if (maxDrift > 1) result.Warnings.Add($"The hand rotated {maxDrift:0.#}° while holding. The workpiece follows the grip point but keeps its orientation (translation-only carrier axes).");
        result.FinalState = state switch { State.Resting => "resting", State.Held => "held", State.Falling => "falling", _ => "lost" };
        result.Samples = samples.Count;
        for (int k = 0; k < 3; k++) {
            var raw = samples.Select(s => new JointPoint { Time = Math.Round(s.T, 6), Value = Math.Round(s.W[k], 4) }).ToList();
            var kept = new List<JointPoint> { raw[0] };
            for (int i = 1; i < raw.Count - 1; i++) {
                var p = kept[^1]; var n = raw[i + 1]; double u = (raw[i].Time - p.Time) / (n.Time - p.Time);
                if (Math.Abs(raw[i].Value - (p.Value + u * (n.Value - p.Value))) > 0.001) kept.Add(raw[i]);
            }
            kept.Add(raw[^1]); result.Axes[carrier[k]!] = kept;
        }
        return result;
    }
}

public sealed partial class Bridge
{
    public static object SimulateConceptPhysics(string manifestPath, string jointsJson, string physicsJson = "")
    {
        manifestPath = CadPath(manifestPath, ".json");
        if (new FileInfo(manifestPath).Length > 1024 * 1024) throw new ArgumentException("Concept manifest exceeds 1 MB.");
        var model = JsonSerializer.Deserialize<ConceptMachine>(File.ReadAllText(manifestPath), ConceptMachine.JsonOptions) ?? throw new ArgumentException("Invalid concept manifest.");
        var joints = JsonSerializer.Deserialize<Dictionary<string, List<JointPoint>>>(jointsJson == "" ? "{}" : jointsJson, ConceptMachine.JsonOptions) ?? throw new ArgumentException("jointsJson must be an object {axisId:[{time,value},...]}.");
        var setup = physicsJson == "" ? new ConceptPhysicsSetup() : JsonSerializer.Deserialize<ConceptPhysicsSetup>(physicsJson, ConceptMachine.JsonOptions) ?? throw new ArgumentException("Invalid physicsJson.");
        var r = ConceptPhysics.Run(model, setup, joints);
        return new {
            finalState = r.FinalState, endTime = r.EndTime, samples = r.Samples, events = r.Events, warnings = r.Warnings, axisKeyframes = r.Axes,
            note = "Keyframes for the workpiece carrier axes (mm, world-aligned, 0 = original position). Apply them with mechcue_set_keyframes after looking up each axis' trackId. Geometric model only: no friction, bounce, tipping or contact forces."
        };
    }
}
