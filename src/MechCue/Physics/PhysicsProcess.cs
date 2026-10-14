using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace MechCue.Physics;

public sealed class PhysicsRequest
{
    public ConceptMachine Model { get; set; } = new();
    public RigidBodySetup Setup { get; set; } = new();
    public Dictionary<string, List<JointPoint>> Joints { get; set; } = [];
}
public sealed class PhysicsResponse
{
    public bool Ok { get; set; }
    public string? Error { get; set; }
    public RigidBodyResult? Result { get; set; }
}

/// <summary>
/// Runs a rigid-body computation in the physics calculator (MechCue.Physics.exe), a separate process. The engine manages
/// native memory; keeping it out of this process means a fault there cannot take down MechCue or Solid Edge (the add-in
/// lives inside Solid Edge). A crash, a timeout or a missing calculator becomes an ordinary exception here.
/// </summary>
public static class PhysicsProcess
{
    public const string ExeName = "MechCue.Physics.exe";
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(120);

    /// <summary>The calculator next to MechCue.dll, or MECHCUE_PHYSICS_EXE (development builds).</summary>
    public static string Locate()
    {
        var overridden = Environment.GetEnvironmentVariable("MECHCUE_PHYSICS_EXE");
        if (!string.IsNullOrWhiteSpace(overridden)) return overridden;
        return Path.Combine(Path.GetDirectoryName(typeof(PhysicsProcess).Assembly.Location)!, ExeName);
    }

    public static RigidBodyResult Run(ConceptMachine model, RigidBodySetup setup, Dictionary<string, List<JointPoint>> joints, TimeSpan? timeout = null, string? executable = null, string extraArguments = "")
    {
        executable ??= Locate();
        if (!File.Exists(executable)) throw new InvalidOperationException((UiText.IsJapanese ? "物理計算プログラムが見つかりません：" : "Physics calculator not found: ") + executable);
        string input = JsonSerializer.Serialize(new PhysicsRequest { Model = model, Setup = setup, Joints = joints }, ConceptMachine.JsonOptions);
        var start = new ProcessStartInfo(executable, "--simulate" + extraArguments) {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8, StandardInputEncoding = new UTF8Encoding(false)
        };
        using var process = Process.Start(start) ?? throw new InvalidOperationException("The physics calculator did not start.");
        var output = process.StandardOutput.ReadToEndAsync(); var errors = process.StandardError.ReadToEndAsync();
        // Feed the request on a worker: a calculator that stops reading must not block this thread past the timeout.
        var feed = Task.Run(() => {
            try { process.StandardInput.Write(input); process.StandardInput.Close(); }
            catch (IOException) { } catch (ObjectDisposedException) { } // the calculator ended early; its exit code tells why
        });
        var limit = timeout ?? DefaultTimeout;
        if (!process.WaitForExit((int)limit.TotalMilliseconds)) {
            try { process.Kill(true); } catch (InvalidOperationException) { }
            feed.Wait(TimeSpan.FromSeconds(5));
            throw new TimeoutException((UiText.IsJapanese ? "物理計算が時間内に終わらないため中止しました（" : "The physics computation was stopped after ") + $"{limit.TotalSeconds:0} s" + (UiText.IsJapanese ? "）。" : "."));
        }
        process.WaitForExit(); feed.Wait(TimeSpan.FromSeconds(5));
        string text = output.Result, detail = errors.Result.Trim();
        PhysicsResponse? response = null;
        try { if (!string.IsNullOrWhiteSpace(text)) response = JsonSerializer.Deserialize<PhysicsResponse>(text, ConceptMachine.JsonOptions); } catch (JsonException) { }
        if (response == null) {
            string reason = detail.Split('\n').FirstOrDefault(l => l.Trim().Length > 0)?.Trim() ?? "";
            throw new InvalidOperationException((UiText.IsJapanese ? $"物理計算プログラムが異常終了しました（終了コード {process.ExitCode}）。" : $"The physics calculator ended abnormally (exit code {process.ExitCode}). ") + reason);
        }
        if (!response.Ok || response.Result == null) throw new InvalidOperationException(response.Error ?? "Physics computation failed.");
        return response.Result;
    }
}
