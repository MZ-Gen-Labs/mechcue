using System.Text.Json;
using System.Reflection;
namespace MechCue;
public static class DiagnosticLog
{
    public static string DirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MechCue", "Logs");
    static readonly object gate = new();
    static readonly string version = typeof(DiagnosticLog).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
    public static void Write(string operation, object? detail = null)
    {
        try {
            lock(gate) {
                Directory.CreateDirectory(DirectoryPath);
                string path = Path.Combine(DirectoryPath, $"mechcue-{DateTime.Now:yyyyMMdd}-{Environment.ProcessId}.jsonl");
                if (File.Exists(path) && new FileInfo(path).Length > 5 * 1024 * 1024) File.Move(path, path + ".previous", true);
                File.AppendAllText(path, JsonSerializer.Serialize(new { at = DateTimeOffset.Now, version, process = Environment.ProcessId, thread = Environment.CurrentManagedThreadId, operation, detail }) + Environment.NewLine);
                foreach (var old in new DirectoryInfo(DirectoryPath).GetFiles("mechcue-*.jsonl*").OrderByDescending(f => f.LastWriteTimeUtc).Skip(20))
                    if (string.Equals(old.DirectoryName, DirectoryPath, StringComparison.OrdinalIgnoreCase)) old.Delete();
            }
        } catch { /* Logging must never interrupt CAD or editing. */ }
    }
    public static void Error(string operation, Exception error, object? state = null) => Write(operation, new { error = error.ToString(), state });
}
public partial class MainForm
{
    object DiagnosticState() => new { window = GetHashCode(), hosted = hostedDocumentWindow, compact, playing = timer.Enabled, apply = live.Checked,
        time = time.Value, track = trackList.SelectedIndex + 1, tracks = tracks.Count, bindings = bridge.BindingCount,
        size = new { Width, Height }, grid = new { grid.Width, grid.Height, visible = grid.Visible, rows = grid.Rows.Count }, tracksHidden, settingsHidden, pointsHidden };
    void ConfigureDiagnostics()
    {
        DiagnosticLog.Write("window-open", DiagnosticState());
        EventHandler resize = (_, _) => DiagnosticLog.Write("resize", DiagnosticState());
        ResizeEnd += resize;
        foreach (var panel in new[] { split, workspace, vertical }) panel.SplitterMoved += (_, _) => DiagnosticLog.Write("splitter", DiagnosticState());
        foreach (var option in new[] { loop, live, collision, autoApply, aiAccess }) option.CheckedChanged += (_, _) => DiagnosticLog.Write("option", new { option = option.Text, value = option.Checked, state = DiagnosticState() });
        System.Threading.ThreadExceptionEventHandler error = (_, e) => {
            if (!(e.Exception.StackTrace?.Contains("MechCue", StringComparison.Ordinal) ?? false)) {
                using var dialog = new ThreadExceptionDialog(e.Exception); dialog.ShowDialog(); return;
            }
            try { PausePlayback(); live.Checked = false; Error(e.Exception); }
            catch (Exception reportingError) { DiagnosticLog.Error("exception-reporting", reportingError); }
        };
        Application.ThreadException += error;
        Disposed += (_, _) => { Application.ThreadException -= error; DiagnosticLog.Write("window-closed", new { window = GetHashCode() }); };
    }
}
