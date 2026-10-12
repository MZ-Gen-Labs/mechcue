using System.Diagnostics;

namespace MechCue;

internal static class SolidEdgeLaunch
{
    // MCP hosts may supply a restricted or stale environment. Only restore licensing
    // settings, without changing either the host environment or Windows settings.
    internal static readonly string[] LicenseVariables =
        ["SE_LICENSE_SERVER", "SALT_LICENSE_SERVER", "SPLM_LICENSE_SERVER", "UGS_LICENSE_SERVER", "LM_LICENSE_FILE"];

    // A child started with UseShellExecute=false inherits the parent's standard handles. Under MCP
    // stdout is the JSON-RPC channel, so anything the child writes (Solid Edge printed directory
    // listings during concept generation) would corrupt the protocol. Give the child private pipes;
    // the caller closes them after start, so later child output is discarded.
    internal static void DetachStandardStreams(ProcessStartInfo start)
    {
        start.RedirectStandardInput = true; start.RedirectStandardOutput = true; start.RedirectStandardError = true;
    }
    internal static (ProcessStartInfo Start, Dictionary<string, string> Sources) Prepare(
        string executable, Func<string, EnvironmentVariableTarget, string?>? read = null)
    {
        read ??= Environment.GetEnvironmentVariable;
        var start = new ProcessStartInfo(executable) {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(executable))!
        };
        DetachStandardStreams(start);
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
