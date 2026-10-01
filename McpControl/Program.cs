using MechCue;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

static class Program
{
    [STAThread] static void Main(string[] args)
    {
        if(args.Length==2 && args[0]=="--shutdown-for-update") { Environment.ExitCode=McpUpdate.StopForUpdate(args[1]); return; }
        if(McpUpdate.Updating()) return;
        ApplicationConfiguration.Initialize();
        if (args.Contains("--self-test")) { TrayContext.Verify(); return; }
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Environment.UserName + "|" + McpAccessSettings.SettingsPath)))[..24];
        using var mutex = new Mutex(true, "Local\\MechCue-Mcp-Control-" + key, out bool created);
        if (!created) return;
        string initial = args.Length == 2 && args[0] == "--initial-mode" && args[1] is "chart" or "read" or "write" ? args[1] : "read";
        try { McpAccessSettings.Save(initial, onlyIfMissing: true); }
        catch (Exception error) { MessageBox.Show(error.Message, "MechCue MCP", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
        try { using var context = new TrayContext(); Application.Run(context); }
        finally { mutex.ReleaseMutex(); }
    }
}
sealed class TrayContext : ApplicationContext
{
    readonly Control dispatcher = new();
    readonly McpShutdownSignal shutdown;
    readonly NotifyIcon tray;
    readonly ContextMenuStrip menu = new();
    readonly Dictionary<string, ToolStripMenuItem> choices = new();
    readonly System.Windows.Forms.Timer refresh = new() { Interval = 1000 };
    static string Label(string mode) => UiText.IsJapanese ? mode switch { "chart" => "Solid Edgeアクセスなし", "read" => "読み取り・選択のみ", _ => "作成・編集も許可" }
        : mode switch { "chart" => "No Solid Edge access", "read" => "Read and select only", _ => "Allow creation and editing" };
    public TrayContext(bool testing = false)
    {
        _ = dispatcher.Handle;
        shutdown = new McpShutdownSignal(()=> { if(!dispatcher.IsDisposed) dispatcher.BeginInvoke(()=>ExitThread()); });
        var version = typeof(TrayContext).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0];
        var title = new ToolStripMenuItem("MechCue MCP " + version) { Enabled = false }; menu.Items.Add(title);
        foreach (string mode in new[] { "chart", "read", "write" }) {
            var item = new ToolStripMenuItem(Label(mode)) { Tag = mode };
            item.Click += (_, _) => SetMode(mode); choices.Add(mode, item); menu.Items.Add(item);
        }
        menu.Items.Add(new ToolStripSeparator());
        var guide = new ToolStripMenuItem(UiText.IsJapanese ? "接続ガイド" : "Connection guide");
        guide.Click += (_, _) => Open(Path.Combine(AppContext.BaseDirectory, "..", "MCP-Guide.html")); menu.Items.Add(guide);
        var logs = new ToolStripMenuItem(UiText.IsJapanese ? "ログフォルダー" : "Log folder");
        logs.Click += (_, _) => { Directory.CreateDirectory(DiagnosticLog.DirectoryPath); Open(DiagnosticLog.DirectoryPath); }; menu.Items.Add(logs);
        var explanation = new ToolStripMenuItem(UiText.IsJapanese ? "設定について" : "About access settings");
        explanation.Click += (_, _) => MessageBox.Show(UiText.IsJapanese
            ? "MCPはAIアプリが自動起動します。このメニューでSolid Edgeへのアクセスを変更できます。\n\n設定は同じWindowsユーザーのMCPに共有され、次の操作から反映されます。処理中の操作は中断しません。MechCueのグラフ操作には影響しません。\n\nこの設定画面を終了しても、最後に選んだ設定は保持されます。"
            : "The AI app starts MCP automatically. This menu controls Solid Edge access for this Windows user. Changes apply to the next operation; operations already in progress are not interrupted. MechCue chart tools are unchanged. The selected mode remains after exiting this controller.", "MechCue MCP"); menu.Items.Add(explanation);
        var exit = new ToolStripMenuItem(UiText.IsJapanese ? "設定アイコンを終了" : "Exit settings icon"); exit.Click += (_, _) => ExitThread(); menu.Items.Add(exit);
        tray = new NotifyIcon { Icon = SystemIcons.Application, ContextMenuStrip = menu, Visible = !testing };
        tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) menu.Show(Cursor.Position); };
        menu.Opening += (_, _) => RefreshState(); refresh.Tick += (_, _) => RefreshState(); refresh.Start(); RefreshState();
    }
    static void Open(string path)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Path.GetFullPath(path)) { UseShellExecute = true }); }
        catch (Exception error) { MessageBox.Show(error.Message, "MechCue MCP", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
    void SetMode(string mode)
    {
        try { McpAccessSettings.Save(mode); RefreshState(); }
        catch (Exception error) { MessageBox.Show(error.Message, "MechCue MCP", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
    void RefreshState()
    {
        string mode = McpAccessSettings.Read();
        foreach (var choice in choices) { choice.Value.Checked = choice.Key == mode; choice.Value.Text = Label(choice.Key); }
        string text = "MechCue MCP: " + Label(mode); tray.Text = text[..Math.Min(text.Length, 63)];
        tray.Icon = mode == "write" ? SystemIcons.Warning : mode == "read" ? SystemIcons.Information : SystemIcons.Application;
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { shutdown.Dispose(); dispatcher.Dispose(); refresh.Stop(); refresh.Dispose(); tray.Visible = false; tray.Dispose(); menu.Dispose(); }
        base.Dispose(disposing);
    }
    internal static void Verify()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MECHCUE_MCP_SETTINGS_PATH"))) throw new InvalidOperationException("Tests require an isolated settings path.");
        using var context = new TrayContext(true);
        foreach (string mode in new[] { "chart", "read", "write", "chart" }) {
            context.choices[mode].PerformClick();
            if (McpAccessSettings.Read() != mode || context.choices.Values.Count(i => i.Checked) != 1 || !context.choices[mode].Checked) throw new Exception("Tray mode switching failed");
            if (mode == "write") McpAccessSettings.EnsureAllowed(true);
            else { try { McpAccessSettings.EnsureAllowed(true); throw new Exception("Editing was allowed"); } catch (InvalidOperationException) { } }
        }
        File.WriteAllText(McpAccessSettings.SettingsPath, "invalid");
        if (McpAccessSettings.Read("write") != "chart") throw new Exception("Corrupt settings granted access");
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "tray-test-result.txt"), "PASS: tray choices, one checked mode, persistent state, editing gate, malformed settings fail closed");
    }
}
