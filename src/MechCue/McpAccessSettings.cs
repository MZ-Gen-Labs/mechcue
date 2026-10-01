using System.Text.Json;
namespace MechCue;

public static class McpAccessSettings
{
    public static string SettingsPath => Environment.GetEnvironmentVariable("MECHCUE_MCP_SETTINGS_PATH") is { Length: > 0 } path
        ? Path.GetFullPath(path) : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MechCue", "mcp-access.json");
    public static string LegacyMode(string[] args) => args.Contains("--allow-solidedge-write") ? "write" : args.Contains("--allow-solidedge") ? "read" : "chart";
    public static string Read(string fallback = "chart")
    {
        try {
            if (!File.Exists(SettingsPath)) return fallback;
            using var stream = new FileStream(SettingsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var json = JsonDocument.Parse(stream);
            if (json.RootElement.GetProperty("schema").GetInt32() != 1) return "chart";
            return json.RootElement.GetProperty("mode").GetString() is "read" or "write" ? json.RootElement.GetProperty("mode").GetString()! : "chart";
        } catch { return "chart"; } // Invalid or temporarily inaccessible settings never grant CAD access.
    }
    public static void Save(string mode, bool onlyIfMissing = false)
    {
        if (mode is not ("chart" or "read" or "write")) throw new ArgumentException("Unknown MCP access mode");
        string path = SettingsPath; Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try {
            File.WriteAllText(temporary, JsonSerializer.Serialize(new { schema = 1, mode }));
            try { File.Move(temporary, path, !onlyIfMissing); }
            catch (IOException) when (onlyIfMissing && File.Exists(path)) { }
        } finally { if (File.Exists(temporary)) File.Delete(temporary); }
        DiagnosticLog.Write("mcp-access", new { mode = Read() });
    }
    public static void EnsureAllowed(bool write, string[]? args = null)
    {
        string mode = Read(LegacyMode(args ?? Environment.GetCommandLineArgs()));
        if (write ? mode != "write" : mode == "chart")
            throw new InvalidOperationException(UiText.IsJapanese
                ? "Solid Edgeの" + (write ? "作成・編集" : "読み取り・選択") + "は無効です。タスクトレイのMechCue MCP設定でアクセスモードを変更してください。"
                : "Solid Edge " + (write ? "editing" : "reading/selection") + " is disabled. Change access mode in the MechCue MCP tray settings.");
    }
    public static void StartTray()
    {
        if (Environment.GetEnvironmentVariable("MECHCUE_MCP_NO_TRAY") == "1") return;
        string executable = Path.Combine(AppContext.BaseDirectory, "control", "MechCue.Mcp.Control.exe");
        if (!File.Exists(executable)) return;
        try {
            var start = new System.Diagnostics.ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden };
            start.ArgumentList.Add("--initial-mode"); start.ArgumentList.Add(LegacyMode(Environment.GetCommandLineArgs()));
            System.Diagnostics.Process.Start(start)?.Dispose();
        } catch (Exception error) { DiagnosticLog.Error("mcp-start-tray", error); }
    }
}
