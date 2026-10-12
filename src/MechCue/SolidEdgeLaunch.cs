using System.Diagnostics;

namespace MechCue;

internal static class SolidEdgeLaunch
{
    // MCP hosts may supply a restricted or stale environment. Only restore licensing
    // settings, without changing either the host environment or Windows settings.
    internal static readonly string[] LicenseVariables =
        ["SE_LICENSE_SERVER", "SALT_LICENSE_SERVER", "SPLM_LICENSE_SERVER", "UGS_LICENSE_SERVER", "LM_LICENSE_FILE"];

    internal static (ProcessStartInfo Start, Dictionary<string, string> Sources) Prepare(
        string executable, Func<string, EnvironmentVariableTarget, string?>? read = null)
    {
        read ??= Environment.GetEnvironmentVariable;
        var start = new ProcessStartInfo(executable) {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(executable))!
        };
        var sources = new Dictionary<string, string>();
        foreach (string name in LicenseVariables) {
            start.Environment.Remove(name);
            foreach (var scope in new[] { EnvironmentVariableTarget.Process, EnvironmentVariableTarget.User, EnvironmentVariableTarget.Machine }) {
                string? value = read(name, scope);
                if (string.IsNullOrWhiteSpace(value)) continue;
                start.Environment[name] = value;
                sources[name] = scope.ToString();
                break;
            }
        }
        return (start, sources);
    }
}
