using MechCue;

namespace MechCue.Physics;

/// <summary>Engine-level checks (run with <c>MechCue.Mcp.exe --physics-self-test</c>). They only use <see cref="IRigidBodyEngine"/>, so any backend can be tested the same way.</summary>
public static class PhysicsSelfTest
{
    const double Dt = 1.0 / 120;
    static readonly PhysicsShape Floor = new(PhysicsShapeKind.Box, 40, 40, 0.1);
    static readonly PhysicsShape Cube = new(PhysicsShapeKind.Box, 0.06, 0.06, 0.06);

    static void Check(bool condition, string message) { if (!condition) throw new Exception("Physics self-test failed: " + message); }
    static double Z(IRigidBodyEngine e, string id) => e.GetPose(id).Z;
    static void Run(IRigidBodyEngine e, double seconds, Action<double>? each = null) { for (int i = 0; i < (int)Math.Round(seconds / Dt); i++) { each?.Invoke(i * Dt); e.Step(Dt); } }
    static IRigidBodyEngine World(Func<IRigidBodyEngine> create, double gx = 0, double gz = -9.81, double floorFriction = 0.6)
    {
        var e = create(); e.SetGravity(gx, 0, gz);
        e.AddStatic("floor", Floor, new(0, 0, -0.05, 0, 0, 0, 1), new(floorFriction));
        return e;
    }

    /// <summary>Concept-model runs on the arm5p template (no engine details beyond <see cref="IRigidBodyEngine"/>).</summary>
    public static void RunConceptChecks(Func<IRigidBodyEngine>? create = null)
    {
        var model = ConceptMachine.Create("arm5p", 500, 300, 300);
        var none = new Dictionary<string, List<JointPoint>>();
        var rest = ConceptRigidBodySimulator.Run(model, new RigidBodySetup { EndTime = 1.5 }, none, create);
        Check(rest.FinalState == "resting" && rest.Axes.Values.All(a => a.All(p => Math.Abs(p.Value) < 0.5)), "arm5p workpiece rests on the table: " + string.Join(",", rest.Axes.Select(a => a.Key + "=" + a.Value[^1].Value)));
        Check(rest.Axes["WZ"][^1].Value > 0, "with the default skin the resting workpiece does not sink into the table: WZ=" + rest.Axes["WZ"][^1].Value);
        Console.WriteLine($"concept rest: ok ({rest.Engine}, WZ {rest.Axes["WZ"][^1].Value:+0.0000;-0.0000} mm)");
        // Gravity tilted 45° toward +X: the workpiece slides off the table edge (700 mm in front of... beyond X=+500) and falls to the floor/limits.
        var slide = ConceptRigidBodySimulator.Run(model, new RigidBodySetup { EndTime = 3, Gravity = [1, 0, -1], SupportFriction = 0.2, WorkpieceFriction = 0.2 }, none, create);
        Check(slide.Axes["WX"][^1].Value > 100, "a workpiece on a steep slope slides: WX=" + slide.Axes["WX"][^1].Value);
        Console.WriteLine($"concept slide: WX={slide.Axes["WX"][^1].Value:0.0} mm, WZ={slide.Axes["WZ"][^1].Value:0.0} mm");
        // The same chart with a sticky table: it holds.
        var hold = ConceptRigidBodySimulator.Run(model, new RigidBodySetup { EndTime = 2, Gravity = [0.3, 0, -1], SupportFriction = 1.0, WorkpieceFriction = 1.0 }, none, create);
        Check(Math.Abs(hold.Axes["WX"][^1].Value) < 1, "friction holds a workpiece on a gentle slope: WX=" + hold.Axes["WX"][^1].Value);
        Console.WriteLine("concept hold: ok");
        // Force-driven fingers at rest: nothing moves and the fingers stay on the hand.
        var force = ConceptRigidBodySimulator.Run(model, new RigidBodySetup { EndTime = 1, GripForceN = 20 }, none, create);
        Check(force.FinalState == "resting" && force.Axes.Values.All(a => a.All(p => Math.Abs(p.Value) < 0.5)) && force.Events.All(x => x.Type != "finger-stopped"), "arm5p with force-driven fingers rests: " + string.Join(",", force.Events.Select(x => x.Type)));
        Console.WriteLine("concept grip force at rest: ok");
        // Out-of-range input is rejected rather than simulated.
        bool rejected = false; try { ConceptRigidBodySimulator.Run(ConceptMachine.Create("arm5", 500, 300, 300), new RigidBodySetup { EndTime = 1 }, none, create); } catch (ArgumentException) { rejected = true; }
        Check(rejected, "arm5 (no free-body workpiece axes) is rejected");
    }

    public static int Run(Func<IRigidBodyEngine>? create = null)
    {
        create ??= () => new BepuEngine();
        try {
            using (var e = World(create)) {
                e.AddDynamic("cube", Cube, new(0, 0, 0.03, 0, 0, 0, 1), new(0.5), 0.5);
                Run(e, 2); Check(Math.Abs(Z(e, "cube") - 0.03) < 0.0001, "a cube resting on the floor stays still and sinks less than 0.1 mm: z=" + Z(e, "cube"));
                Console.WriteLine($"rest: ok (sinks {1000 * (0.03 - Z(e, "cube")):0.000} mm)");
            }
            using (var e = World(create)) {
                // With a 0.05 mm skin the resting cube stays clear of the floor: the CAD parts would not overlap.
                e.SetContact(120, 2, 0.00005);
                e.AddDynamic("cube", Cube, new(0, 0, 0.03, 0, 0, 0, 1), new(0.5), 0.5);
                Run(e, 2); double gap = Z(e, "cube") - 0.03;
                Check(gap > 0.00001 && gap < 0.00005, $"a 0.05 mm skin keeps a resting cube 0.01..0.05 mm above the floor: gap {1000 * gap:0.000} mm");
                Console.WriteLine($"skin: ok (rests {1000 * gap:0.000} mm above the floor)");
            }
            using (var e = World(create)) {
                e.AddDynamic("cube", Cube, new(0, 0, 0.13, 0, 0, 0, 1), new(0.5), 0.5);
                double landed = -1; Run(e, 1.5, t => { if (landed < 0 && Z(e, "cube") < 0.0305) landed = t; });
                Check(Math.Abs(landed - Math.Sqrt(2 * 0.1 / 9.81)) < 0.03, $"free-fall landing time {landed}");
                Check(Math.Abs(Z(e, "cube") - 0.03) < 0.001 && Math.Abs(e.GetLinearVelocity("cube").Z) < 0.01, "the dropped cube settles on the floor");
                Console.WriteLine($"drop: landed at {landed:0.000}s (ideal 0.143)");
            }
            foreach (var (restitution, low, high) in new[] { (0.0, 0.0, 0.003), (0.5, 0.06, 0.09) }) {
                // Dropped from 0.3 m: an ideal bounce rises e²·0.3 m (75 mm for e=0.5).
                using var e = World(create);
                e.AddDynamic("cube", Cube, new(0, 0, 0.33, 0, 0, 0, 1), new(0.5, restitution), 0.5);
                double peak = 0; bool landed = false; Run(e, 1.5, _ => { double z = Z(e, "cube") - 0.03; if (z < 0.001) landed = true; else if (landed) peak = Math.Max(peak, z); });
                Check(peak >= low && peak <= high, $"restitution {restitution}: rebound {1000 * peak:0.0} mm, expected {1000 * low:0}..{1000 * high:0} mm");
                Check(Math.Abs(Z(e, "cube") - 0.03) < 0.001, "the bouncing cube comes to rest");
                Console.WriteLine($"bounce e={restitution}: rebound {1000 * peak:0.0} mm (ideal {1000 * restitution * restitution * 0.3:0.0})");
            }
            foreach (var (mu, acc) in new[] { (1.0, 4.0), (0.5, 8.0) }) {
                // A plate accelerating sideways drags the cube by friction only: it holds while mu*g > a and otherwise lags at mu*g.
                using var e = create(); e.SetGravity(0, 0, -9.81);
                e.AddKinematic("plate", new(PhysicsShapeKind.Box, 40, 40, 0.1), new(0, 0, -0.05, 0, 0, 0, 1), new(mu));
                e.AddDynamic("cube", Cube, new(0, 0, 0.03, 0, 0, 0, 1), new(mu), 0.5);
                Run(e, 1.0, t => e.SetKinematicTarget("plate", new(0.5 * acc * (t + Dt) * (t + Dt), 0, -0.05, 0, 0, 0, 1)));
                double lag = e.GetPose("plate").X - e.GetPose("cube").X, expected = mu * 9.81 >= acc ? 0 : 0.5 * (acc - mu * 9.81);
                Check(Math.Abs(lag - expected) < 0.03, $"friction mu={mu}, plate a={acc}: lag {lag:0.000} m, expected {expected:0.000} m");
                Console.WriteLine($"friction mu={mu} plate a={acc}: lag {lag:0.000} m (expected {expected:0.000})");
            }
            foreach (var (mu, slides) in new[] { (0.6, false), (0.15, true) }) {
                double a = 20 * Math.PI / 180; // floor seems tilted by tilting gravity: tan(20°)=0.36
                using var e = World(create, 9.81 * Math.Sin(a), -9.81 * Math.Cos(a), mu);
                e.AddDynamic("cube", Cube, new(0, 0, 0.03, 0, 0, 0, 1), new(mu), 0.5);
                Run(e, 1.5); double x = e.GetPose("cube").X;
                Check(slides ? x > 0.3 : Math.Abs(x) < 0.01, $"friction {mu} on a 20° slope: moved {x:0.000} m");
                Console.WriteLine($"slope mu={mu}: moved {x:0.000} m ({(slides ? "slides" : "holds")})");
            }
            using (var e = World(create, 9.81 * Math.Sin(20 * Math.PI / 180), -9.81 * Math.Cos(20 * Math.PI / 180), 1.0)) {
                // 40 x 40 x 200 mm: tips at atan(0.04/0.2)=11°, so a 20° slope with high friction topples it.
                e.AddDynamic("post", new(PhysicsShapeKind.Box, 0.04, 0.04, 0.2), new(0, 0, 0.1, 0, 0, 0, 1), new(1.0), 0.5);
                Run(e, 2); Check(Z(e, "post") < 0.06, "a tall post on a steep slope topples: z=" + Z(e, "post"));
                Console.WriteLine($"tipping: centre height fell from 0.100 to {Z(e, "post"):0.000} m");
            }
            foreach (var (mu, lifts) in new[] { (0.8, true), (0.02, false) }) {
                using var e = World(create);
                e.AddDynamic("work", Cube, new(0, 0, 0.03, 0, 0, 0, 1), new(mu), 0.5);
                var finger = new PhysicsShape(PhysicsShapeKind.Box, 0.03, 0.01, 0.05);
                // Fingers 10 mm thick; open their inner faces sit 7.5 mm off the 60 mm cube, closed they squeeze it by 0.1 mm each.
                // Position-driven fingers press with squeeze x contact stiffness: about 28 N each at 120 Hz on 0.5 kg.
                RigidPose Left(double close, double z) => new(0, 0.0425 - close, z, 0, 0, 0, 1);
                RigidPose Right(double close, double z) => new(0, -(0.0425 - close), z, 0, 0, 0, 1);
                e.AddKinematic("f1", finger, Left(0, 0.04), new(mu)); e.AddKinematic("f2", finger, Right(0, 0.04), new(mu));
                // 0-0.5 s: close; 0.7-1.7 s: lift 0.2 m.
                Run(e, 2.5, t => {
                    double close = Math.Clamp(t / 0.5, 0, 1) * 0.0076, lift = Math.Clamp((t - 0.7) / 1.0, 0, 1) * 0.2;
                    e.SetKinematicTarget("f1", Left(close, 0.04 + lift)); e.SetKinematicTarget("f2", Right(close, 0.04 + lift));
                });
                double z = Z(e, "work");
                Check(lifts ? z > 0.2 : z < 0.1, $"grip friction {mu}: workpiece height {z:0.000} m");
                Console.WriteLine($"grip mu={mu}: workpiece height {z:0.000} m ({(lifts ? "lifted" : "slipped")})");
            }
            foreach (var (force, lifts) in new[] { (3.0, false), (10.0, true) }) {
                // Force-limited fingers on a palm: lifting 0.5 kg with friction 0.5 on two fingers needs more than 4.9 N per finger.
                using var e = World(create);
                e.AddDynamic("work", Cube, new(0, 0, 0.03, 0, 0, 0, 1), new(0.5), 0.5);
                var finger = new PhysicsShape(PhysicsShapeKind.Box, 0.03, 0.01, 0.05);
                e.AddKinematic("palm", new(PhysicsShapeKind.Box, 0.03, 0.12, 0.01), new(0, 0, 0.075, 0, 0, 0, 1), new(0.5));
                e.AddSlider("f1", finger, new(0, 0.0425, 0.04, 0, 0, 0, 1), new(0.5), 0.5, "palm", (0, -1, 0), force);
                e.AddSlider("f2", finger, new(0, -0.0425, 0.04, 0, 0, 0, 1), new(0.5), 0.5, "palm", (0, 1, 0), force);
                // 0-0.5 s: close 20 mm (past the cube faces); 0.7-1.7 s: lift 0.2 m.
                Run(e, 2.5, t => {
                    double close = Math.Clamp(t / 0.5, 0, 1) * 0.02, lift = Math.Clamp((t - 0.7) / 1.0, 0, 1) * 0.2;
                    e.SetSliderTarget("f1", close); e.SetSliderTarget("f2", close); e.SetKinematicTarget("palm", new(0, 0, 0.075 + lift, 0, 0, 0, 1));
                });
                double z = Z(e, "work");
                Check(lifts ? z > 0.2 : z < 0.05, $"grip force {force} N: workpiece height {z:0.000} m");
                if (lifts) Check(Math.Abs(e.GetPose("f1").Y - 0.035) < 0.002, "a force-driven finger stops at the workpiece face: y=" + e.GetPose("f1").Y);
                Console.WriteLine($"grip force {force} N: workpiece height {z:0.000} m ({(lifts ? "lifted" : "slipped")})");
            }
            RunConceptChecks(create);
            Console.WriteLine("physics self-test passed");
            return 0;
        } catch (Exception error) { Console.Error.WriteLine(error.Message); return 1; }
    }
}
