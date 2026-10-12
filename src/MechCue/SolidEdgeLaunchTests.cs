using System.Diagnostics;

namespace MechCue;

public static partial class SelfTest
{
    static void TestSolidEdgeLaunch()
    {
        void Assert(bool value, string message) { if (!value) throw new Exception(message); }
        var values = new Dictionary<(string, EnvironmentVariableTarget), string> {
            [("SE_LICENSE_SERVER", EnvironmentVariableTarget.Machine)] = "machine-test",
            [("SALT_LICENSE_SERVER", EnvironmentVariableTarget.User)] = "user-test",
            [("SALT_LICENSE_SERVER", EnvironmentVariableTarget.Machine)] = "other-machine-test",
            [("SPLM_LICENSE_SERVER", EnvironmentVariableTarget.Process)] = "process-test",
            [("SPLM_LICENSE_SERVER", EnvironmentVariableTarget.User)] = "other-user-test",
            [("SE_LICENSE_SERVER", EnvironmentVariableTarget.Process)] = " "
        };
        var launch = SolidEdgeLaunch.Prepare(Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            (name, scope) => values.GetValueOrDefault((name, scope)));
        Assert(launch.Start.Environment["SE_LICENSE_SERVER"] == "machine-test", "Missing MCP licensing environment restored from machine");
        Assert(launch.Start.Environment["SALT_LICENSE_SERVER"] == "user-test", "User licensing setting takes precedence over machine");
        Assert(launch.Start.Environment["SPLM_LICENSE_SERVER"] == "process-test", "Explicit MCP licensing setting retained");
        Assert(!launch.Start.Environment.ContainsKey("UGS_LICENSE_SERVER"), "Absent licensing setting is not invented");
        Assert(launch.Sources["SE_LICENSE_SERVER"] == "Machine" && launch.Sources["SPLM_LICENSE_SERVER"] == "Process", "Launch reports sources without license values");
        Assert(launch.Start.WorkingDirectory == Environment.SystemDirectory, "Launch uses executable directory");
        // Exercise the actual process boundary: shell launch cannot carry custom Environment.
        launch.Start.Arguments = "/d /c set SE_LICENSE_SERVER";
        launch.Start.RedirectStandardOutput = true;
        launch.Start.CreateNoWindow = true;
        using var child = Process.Start(launch.Start)!;
        Assert(child.WaitForExit(5000), "Launch environment child finishes");
        Assert(child.ExitCode == 0 && child.StandardOutput.ReadToEnd().Trim() == "SE_LICENSE_SERVER=machine-test", "Child receives restored licensing setting");
    }
}
