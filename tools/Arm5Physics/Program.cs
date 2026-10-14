// Builds a pick-and-place chart for the arm5 concept (numeric inverse kinematics, hand pointing down),
// runs the physics model on it and writes joint + workpiece keyframes as JSON.
using System.Text.Json;
using MechCue;
using MechCue.Physics;

var model = ConceptMachine.Create("arm5p", 500, 300, 300);
string outDir = args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory, "out");
Directory.CreateDirectory(outDir);
double[] Hand(double[] j, out double[] yAxis, out double[] xAxis) {
    var v = new Dictionary<string, double>(model.Values) { ["J1"] = j[0], ["J2"] = j[1], ["J3"] = j[2], ["J4"] = j[3], ["J5"] = j[4] };
    double[] f;
    try { f = model.AxisPoses(v)["J5"]; } catch (ArgumentException) { xAxis = yAxis = [0, 0, 0]; return [1e6, 1e6, 1e6]; } // outside a joint range
    xAxis = [f[0], f[1], f[2]]; yAxis = [f[4], f[5], f[6]];
    return [(f[12] + f[0] * .07) * 1000, (f[13] + f[1] * .07) * 1000, (f[14] + f[2] * .07) * 1000]; // grip centre, 70 mm along the hand
}
double[] Full(double j1, double j2, double j3, double roll) => [j1, j2, j3, 90 - j2 - j3, roll];
(double[] J, double err)? Solve(double[] target, double rollSign) {
    var rnd = new Random(1); (double[], double)? best = null; var lo = new[] { -170d, -90, -135 }; var hi = new[] { 170d, 90, 135 };
    for (int rs = 0; rs < 200; rs++) {
        var q = new[] { rnd.NextDouble() * 340 - 170, rnd.NextDouble() * 180 - 90, rnd.NextDouble() * 270 - 135 };
        for (int it = 0; it < 60; it++) {
            double[] F(double[] x) { var p = Hand(Full(x[0], x[1], x[2], rollSign * x[0]), out _, out _); return [p[0] - target[0], p[1] - target[1], p[2] - target[2]]; }
            var r = F(q); var J = new double[3, 3];
            for (int c = 0; c < 3; c++) { var d = (double[])q.Clone(); d[c] += 1e-3; var r2 = F(d); for (int k = 0; k < 3; k++) J[k, c] = (r2[k] - r[k]) / 1e-3; }
            double det = J[0, 0] * (J[1, 1] * J[2, 2] - J[1, 2] * J[2, 1]) - J[0, 1] * (J[1, 0] * J[2, 2] - J[1, 2] * J[2, 0]) + J[0, 2] * (J[1, 0] * J[2, 1] - J[1, 1] * J[2, 0]);
            if (Math.Abs(det) < 1e-9) break;
            double[] dq = new double[3]; // Cramer's rule
            for (int c = 0; c < 3; c++) {
                var M = (double[,])J.Clone(); for (int k = 0; k < 3; k++) M[k, c] = -r[k];
                double dm = M[0, 0] * (M[1, 1] * M[2, 2] - M[1, 2] * M[2, 1]) - M[0, 1] * (M[1, 0] * M[2, 2] - M[1, 2] * M[2, 0]) + M[0, 2] * (M[1, 0] * M[2, 1] - M[1, 1] * M[2, 0]);
                dq[c] = dm / det;
            }
            for (int c = 0; c < 3; c++) q[c] = Math.Clamp(q[c] + Math.Clamp(dq[c], -20, 20), lo[c], hi[c]);
        }
        var e = Hand(Full(q[0], q[1], q[2], rollSign * q[0]), out _, out _); double err = Math.Sqrt(Enumerable.Range(0, 3).Sum(k => Math.Pow(e[k] - target[k], 2)));
        var j = Full(q[0], q[1], q[2], rollSign * q[0]);
        if (err < 0.01 && Math.Abs(j[3]) <= 120 && Math.Abs(j[4]) <= 180 && (best == null || q.Sum(v => v * v) < best.Value.Item1.Sum(v => v * v) + 0)) best = (j, err);
    }
    return best;
}
// Pick the roll sign that lines the fingers' Y axis up with the world Y axis (workpiece edges).
double sign = 1; { var j = Full(10, 30, -60, 10); Hand(j, out var y1, out _); Hand(Full(10, 30, -60, -10), out var y2, out _); sign = Math.Abs(y1[1]) > Math.Abs(y2[1]) ? 1 : -1; }
Console.WriteLine("roll sign J5 = " + sign + " * J1");

double zWork = 770;
(string Name, double[] Xyz)[] targets = [
    ("pick-above", [250, 0, zWork + 123]), ("pick", [250, 0, zWork + 3]),
    ("place-above", [-100, 260, zWork + 223]), ("place-drop", [-100, 260, zWork + 103]), ("place-clear", [-100, 260, zWork + 223])];
var poses = new Dictionary<string, double[]>();
foreach (var (name, xyz) in targets) {
    var s = Solve(xyz, sign) ?? throw new Exception("No IK solution: " + name);
    poses[name] = s.J; Console.WriteLine($"{name}: J=[{string.Join(", ", s.J.Select(v => v.ToString("0.00")))}] err={s.err:0.0000}");
}
var home = new double[] { 0, 0, 0, 0, 0 };
(double T, double[] J)[] timeline = [(0, home), (2, poses["pick-above"]), (3.5, poses["pick"]), (4.3, poses["pick"]), (5.5, poses["pick-above"]), (8, poses["place-above"]), (9, poses["place-drop"]), (9.7, poses["place-drop"]), (11, poses["place-clear"]), (13, home)];
var joints = new Dictionary<string, List<JointPoint>>();
for (int k = 0; k < 5; k++) joints["J" + (k + 1)] = timeline.Select(p => new JointPoint { Time = p.T, Value = Math.Round(p.J[k], 4) }).ToList();
// Fingers: open 10 mm during the approach, squeeze to 0.5 mm into the workpiece at 3.5-4.0 s, open again at 9.2-9.5 s.
double grip = double.Parse(Environment.GetEnvironmentVariable("ARM5_GRIP") ?? "3.0"), release = double.Parse(Environment.GetEnvironmentVariable("ARM5_RELEASE") ?? "9.2");
List<JointPoint> F(double sign2) => new() { new() { Time = 0, Value = sign2 * 10 }, new() { Time = 3.5, Value = sign2 * 10 }, new() { Time = 4.0, Value = -sign2 * grip }, new() { Time = release, Value = -sign2 * grip }, new() { Time = release + 0.3, Value = sign2 * 10 }, new() { Time = 13, Value = sign2 * 10 } };
joints["F1"] = F(1); joints["F2"] = F(-1);
var setup = new RigidBodySetup { EndTime = 13, FloorZMm = 0 };
var result = ConceptRigidBodySimulator.Run(model, setup, joints);
Console.WriteLine($"engine={result.Engine} final={result.FinalState} samples={result.Samples} maxTilt={result.MaxTiltDegrees:0.0}");
foreach (var e in result.Events) Console.WriteLine($"event t={e.Time:0.000} {e.Type} {e.Detail}");
foreach (var w in result.Warnings) Console.WriteLine("warning: " + w);
foreach (var a2 in result.Axes) Console.WriteLine($"{a2.Key}: {a2.Value.Count} points, last={a2.Value[^1].Value:0.00}");
foreach (double t in new[] { 3.0, 4.2, 5.0, 6.0, 8.0, 9.1, 9.3, 9.6, 10.0, 13.0 }) {
    string Row(string id) { var p = result.Axes[id]; int i = p.FindIndex(x => x.Time >= t - 1e-9); if (i <= 0) return p[0].Value.ToString("0.0"); double u = (t - p[i - 1].Time) / (p[i].Time - p[i - 1].Time); return (p[i - 1].Value + u * (p[i].Value - p[i - 1].Value)).ToString("0.0"); }
    Console.WriteLine($"t={t,5:0.0}  WX={Row("WX"),7} WY={Row("WY"),7} WZ={Row("WZ"),7}  yaw={Row("WRZ"),6} pitch={Row("WRY"),6} roll={Row("WRX"),6}");
}
var json = new { setup, joints, result = new { result.Engine, result.FinalState, result.EndTime, result.Events, result.Warnings, axisKeyframes = result.Axes } };
File.WriteAllText(Path.Combine(outDir, "arm5p-physics-chart.json"), JsonSerializer.Serialize(json, ConceptMachine.JsonOptions));
Console.WriteLine("wrote " + Path.Combine(outDir, "arm5p-physics-chart.json"));
