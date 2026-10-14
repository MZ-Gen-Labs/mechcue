using System.Text;
using System.Text.Json;
using MechCue;
using MechCue.Physics;

// MechCue.Physics.exe: the physics calculator, started by MechCue for each computation.
//   --simulate    read a PhysicsRequest (JSON) from stdin, write a PhysicsResponse (JSON) to stdout
//   --self-test   engine checks, concept runs, and the process boundary (crash, timeout) as seen from MechCue
Console.OutputEncoding = new UTF8Encoding(false);
if (args.Contains("--crash-test")) Environment.FailFast("MechCue.Physics crash test");
if (args.Contains("--hang-test")) { Thread.Sleep(Timeout.Infinite); return 0; }
if (args.Contains("--self-test")) {
    int code = PhysicsSelfTest.Run(); if (code != 0) return code;
    return PhysicsProcessSelfTest.Run(Environment.ProcessPath!);
}
if (args.Contains("--simulate")) {
    try {
        using var reader = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
        var request = JsonSerializer.Deserialize<PhysicsRequest>(reader.ReadToEnd(), ConceptMachine.JsonOptions) ?? throw new ArgumentException("Empty physics request.");
        var result = ConceptRigidBodySimulator.Run(request.Model, request.Setup, request.Joints);
        Console.Out.Write(JsonSerializer.Serialize(new PhysicsResponse { Ok = true, Result = result }, ConceptMachine.JsonOptions));
        return 0;
    } catch (Exception error) when (error is ArgumentException or InvalidOperationException or JsonException) {
        Console.Out.Write(JsonSerializer.Serialize(new PhysicsResponse { Ok = false, Error = error.Message }, ConceptMachine.JsonOptions));
        return 2;
    }
}
Console.Error.WriteLine("Usage: MechCue.Physics.exe --simulate | --self-test");
return 1;

static class PhysicsProcessSelfTest
{
    static void Check(bool condition, string message) { if (!condition) throw new Exception("Physics process self-test failed: " + message); }
    public static int Run(string self)
    {
        try {
            var model = ConceptMachine.Create("arm5p", 500, 300, 300);
            var none = new Dictionary<string, List<JointPoint>>();
            var rest = PhysicsProcess.Run(model, new RigidBodySetup { EndTime = 1 }, none, executable: self);
            Check(rest.FinalState == "resting" && rest.Axes.Count == 6 && rest.Engine.StartsWith("BepuPhysics"), "a computation through the process returns the six workpiece axes");
            Console.WriteLine("process round trip: ok");
            bool reported = false;
            try { PhysicsProcess.Run(model, new RigidBodySetup { EndTime = 1, WorkpieceMassKg = -1 }, none, executable: self); }
            catch (InvalidOperationException error) when (error.Message.Contains("WorkpieceMassKg")) { reported = true; }
            Check(reported, "invalid input comes back as the calculator's message");
            Console.WriteLine("invalid input reported: ok");
            bool crashed = false;
            try { PhysicsProcess.Run(model, new RigidBodySetup { EndTime = 1 }, none, executable: self, extraArguments: " --crash-test"); } catch (InvalidOperationException) { crashed = true; }
            Check(crashed, "a crashing calculator becomes an exception in the caller");
            Console.WriteLine("calculator crash contained: ok");
            bool timedOut = false; var watch = System.Diagnostics.Stopwatch.StartNew();
            try { PhysicsProcess.Run(model, new RigidBodySetup { EndTime = 1 }, none, TimeSpan.FromSeconds(2), self, " --hang-test"); } catch (TimeoutException) { timedOut = true; }
            Check(timedOut && watch.Elapsed < TimeSpan.FromSeconds(15), "a hanging calculator is stopped at the timeout");
            Console.WriteLine("calculator timeout: ok");
            bool missing = false;
            try { PhysicsProcess.Run(model, new RigidBodySetup { EndTime = 1 }, none, executable: Path.Combine(Path.GetTempPath(), "no-such-physics.exe")); } catch (InvalidOperationException) { missing = true; }
            Check(missing, "a missing calculator is reported");
            Console.WriteLine("physics process self-test passed");
            return 0;
        } catch (Exception error) { Console.Error.WriteLine(error.Message); return 1; }
    }
}
