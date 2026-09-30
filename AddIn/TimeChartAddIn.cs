using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using SE = MechCue.AddIn.Interop;

namespace MechCue.AddIn;

[ComVisible(true)]
[Guid("79B86022-7C7D-4768-A3A7-CF8EBD1F7826")]
[ProgId("MechCue.TimeChartAddIn")]
[ClassInterface(ClassInterfaceType.None)]
public sealed class TimeChartAddIn : SE.ISolidEdgeAddIn, SE.ISEAddInEvents
{
    const string AssemblyEnvironment = "{26618395-09D6-11D1-BA07-080036230602}";
    object? application;
    SE.ISEAddInEx? addIn;
    IConnectionPoint? commands;
    int cookie;
    MainForm? chart;
    readonly HashSet<string> configured = new(StringComparer.OrdinalIgnoreCase);

    public void OnConnection(object Application, SE.SeConnectMode ConnectMode, SE.AddIn AddInInstance)
    {
        application = Application;
        addIn = (SE.ISEAddInEx)AddInInstance;
        addIn.GuiVersion = 5;
        addIn.Description = "\nMechCue";
        var container = (IConnectionPointContainer)addIn.AddInEvents;
        var eventsId = typeof(SE.ISEAddInEvents).GUID;
        container.FindConnectionPoint(ref eventsId, out commands);
        if (commands == null) throw new InvalidOperationException("Solid Edgeのコマンドイベントへ接続できません。");
        commands.Advise(this, out cookie);
        Log("Connected to Solid Edge");
    }
    public void OnConnectToEnvironment(string EnvCatID, object pEnvironmentDispatch, bool bFirstTime)
    {
        if (!string.Equals(EnvCatID, AssemblyEnvironment, StringComparison.OrdinalIgnoreCase) || addIn == null || !configured.Add(EnvCatID)) return;
        try
        {
            Array names = Enum.GetValues<HostAction>().Select(action => "\n" + Caption(action) + "\n" + Hint(action) + "\n" + Caption(action)).ToArray();
            Array ids = Enum.GetValues<HostAction>().Select(action => (int)action).ToArray();
            addIn.SetAddInInfoEx(typeof(TimeChartAddIn).Assembly.Location, EnvCatID, "MechCue", 101, 102, 103, 104, names.Length, ref names, ref ids);
            if (bFirstTime)
                foreach (var action in Enum.GetValues<HostAction>())
                {
                    var button = addIn.AddCommandBarButton(EnvCatID, "MechCue", (int)action);
                    ((SE.ICommandButtonStyle)button).Style = 5; // seButtonIconAndCaptionBelow: large icon.
                }
            Log("Assembly command registered");
        }
        catch (Exception ex) { configured.Remove(EnvCatID); Report(ex); }
    }
    public void OnCommand(int CommandID)
    {
        if (!Enum.IsDefined(typeof(HostAction), CommandID) || application == null) return;
        var action = (HostAction)CommandID;
        if (action == HostAction.Stop && (chart == null || chart.IsDisposed)) return;
        try
        {
            if (chart == null || chart.IsDisposed)
            {
                chart = new MainForm(hostedApplication: application);
                dynamic app = application;
                chart.Show(new HostWindow(new IntPtr((int)app.hWnd)));
            }
            else if (action is not (HostAction.Stop or HostAction.Minimize)) { chart.Show(); if (chart.WindowState == FormWindowState.Minimized) chart.WindowState = FormWindowState.Normal; chart.Activate(); }
            chart.ExecuteHostAction(action);
            Log("Chart opened in Solid Edge process");
        }
        catch (Exception ex) { Report(ex); }
    }
    static string Caption(HostAction action) => UiText.Text(action switch { HostAction.Open => "タイムチャート", HostAction.Play => "再生", HostAction.Stop => "停止", HostAction.Maximize => "最大化", HostAction.Minimize => "最小化", HostAction.Compact => "最小表示", _ => "CAD保存" });
    static string Hint(HostAction action) => UiText.Text(action switch { HostAction.Open => "タイムチャートを開きます。", HostAction.Play => "現在のCAD反映設定で再生します。", HostAction.Stop => "再生を停止します。", HostAction.Maximize => "MechCueの画面を最大化します。", HostAction.Minimize => "MechCueの画面を最小化します。", HostAction.Compact => "編集画面と最小表示を切り替えます。", _ => "設定をアセンブリへ保存します。" });
    public void OnCommandHelp(int hFrameWnd, int HelpCommandID, int CommandID) { }
    public void OnCommandUpdateUI(int CommandID, ref int CommandFlags, out string MenuItemText, ref int BitmapID)
    {
        MenuItemText = Enum.IsDefined(typeof(HostAction), CommandID) ? Caption((HostAction)CommandID) : "MechCue";
        // Solid Edge's default command state is retained.
    }
    public void OnDisconnection(SE.SeDisconnectMode DisconnectMode)
    {
        try { chart?.ShutdownFromHost(); } catch (Exception ex) { Log(ex.ToString()); }
        chart = null;
        try { if (cookie != 0) commands?.Unadvise(cookie); } catch (Exception ex) { Log(ex.ToString()); }
        cookie = 0; commands = null; addIn = null; application = null; configured.Clear();
        Log("Disconnected");
    }
    sealed record HostWindow(IntPtr Handle) : IWin32Window;
    static void Report(Exception ex)
    {
        Log(ex.ToString()); MessageBox.Show(UiText.Text(ex.Message), UiText.Text("MechCue アドイン"), MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
    static void Log(string message)
    {
        try
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MechCue", "Logs");
            Directory.CreateDirectory(directory);
            File.AppendAllText(Path.Combine(directory, "addin-log.txt"), $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
        }
        catch { }
    }
}
