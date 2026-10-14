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
        Console.WriteLine($"concept rest: ok ({rest.Engine})");
        // Gravity tilted 45° toward +X: the workpiece slides off the table edge (700 mm in front of... beyond X=+500) and falls to the floor/limits.
        var slide = ConceptRigidBodySimulator.Run(model, new RigidBodySetup { EndTime = 3, Gravity = [1, 0, -1], SupportFriction = 0.2, WorkpieceFriction = 0.2 }, none, create);
        Check(slide.Axes["WX"][^1].Value > 100, "a workpiece on a steep slope slides: WX=" + slide.Axes["WX"][^1].Value);
        Console.WriteLine($"concept slide: WX={slide.Axes["WX"][^1].Value:0.0} mm, WZ={slide.Axes["WZ"][^1].Value:0.0} mm");
        // The same chart with a sticky table: it holds.
        var hold = ConceptRigidBodySimulator.Run(model, new RigidBodySetup { EndTime = 2, Gravity = [0.3, 0, -1], SupportFriction = 1.0, WorkpieceFriction = 1.0 }, none, create);
        Check(Math.Abs(hold.Axes["WX"][^1].Value) < 1, "friction holds a workpiece on a gentle slope: WX=" + hold.Axes["WX"][^1].Value);
        Console.WriteLine("concept hold: ok");
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
                Run(e, 2); Check(Math.Abs(Z(e, "cube") - 0.03) < 0.0005, "a cube resting on the floor stays still: z=" + Z(e, "cube"));
                Console.WriteLine("rest: ok");
            }
            using (var e = World(create)) {
                e.AddDynamic("cube", Cube, new(0, 0, 0.13, 0, 0, 0, 1), new(0.5), 0.5);
                double landed = -1; Run(e, 1.5, t => { if (landed < 0 && Z(e, "cube") < 0.0305) landed = t; });
                Check(Math.Abs(landed - Math.Sqrt(2 * 0.1 / 9.81)) < 0.03, $"free-fall landing time {landed}");
                Check(Math.Abs(Z(e, "cube") - 0.03) < 0.001 && Math.Abs(e.GetLinearVelocity("cube").Z) < 0.01, "the dropped cube settles on the floor");
                Console.WriteLine($"drop: landed at {landed:0.000}s (ideal 0.143)");
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
                // Fingers 10 mm thick; open their inner faces sit 7.5 mm off the 60 mm cube, closed they squeeze it by 1 mm each.
                RigidPose Left(double close, double z) => new(0, 0.0425 - close, z, 0, 0, 0, 1);
                RigidPose Right(double close, double z) => new(0, -(0.0425 - close), z, 0, 0, 0, 1);
                e.AddKinematic("f1", finger, Left(0, 0.04), new(mu)); e.AddKinematic("f2", finger, Right(0, 0.04), new(mu));
                // 0-0.5 s: close; 0.7-1.7 s: lift 0.2 m.
                Run(e, 2.5, t => {
                    double close = Math.Clamp(t / 0.5, 0, 1) * 0.0085, lift = Math.Clamp((t - 0.7) / 1.0, 0, 1) * 0.2;
                    e.SetKinematicTarget("f1", Left(close, 0.04 + lift)); e.SetKinematicTarget("f2", Right(close, 0.04 + lift));
                });
                double z = Z(e, "work");
                Check(lifts ? z > 0.2 : z < 0.1, $"grip friction {mu}: workpiece height {z:0.000} m");
                Console.WriteLine($"grip mu={mu}: workpiece height {z:0.000} m ({(lifts ? "lifted" : "slipped")})");
            }
            RunConceptChecks(create);
            Console.WriteLine("physics self-test passed");
            return 0;
        } catch (Exception error) { Console.Error.WriteLine(error.Message); return 1; }
    }
}
