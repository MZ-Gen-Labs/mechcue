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
    readonly Dictionary<int, HostAction> runtimeCommands = new();
    readonly HashSet<string> configured = new(StringComparer.OrdinalIgnoreCase);

    public void OnConnection(object Application, SE.SeConnectMode ConnectMode, SE.AddIn AddInInstance)
    {
        application = Application;
        addIn = (SE.ISEAddInEx)AddInInstance;
        addIn.GuiVersion = 12;
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
            foreach (var action in HostCommands.RibbonActions)
            {
                int resource=101+10*((int)action-1);
                string commandName=typeof(TimeChartAddIn).GUID.ToString("B") + "_" + (int)action + "\n" + Caption(action) + "\n" + Hint(action) + "\n" + Caption(action);
                Array names = new[] { commandName };
                Array ids = new[] { (int)action };
                addIn.SetAddInInfoEx(typeof(TimeChartAddIn).Assembly.Location, EnvCatID, "MechCue\n" + HostCommands.Group(action), resource, resource+1, resource+2, resource+3, 1, ref names, ref ids);
                int runtimeId = (int)ids.GetValue(0)!;
                runtimeCommands[runtimeId] = action;
                Log($"Command {action}: runtime ID {runtimeId}, firstTime={bFirstTime}");
                if (bFirstTime)
                {
                    var button = addIn.AddCommandBarButton(EnvCatID, "MechCue\n" + HostCommands.Group(action), (int)action);
                    ((SE.ICommandButtonStyle)button).Style = HostCommands.IsToggle(action) ? 7 : action == HostAction.Play ? 3 : 5;
                    if (Marshal.IsComObject(button)) Marshal.ReleaseComObject(button);
                }
            }
            Log("Assembly command registered");
        }
        catch (Exception ex) { configured.Remove(EnvCatID); Report(ex); }
    }
    public void OnCommand(int CommandID)
    {
        if (application == null || !TryAction(CommandID, out var action)) return;
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
    bool TryAction(int id, out HostAction action)
    {
        if(runtimeCommands.TryGetValue(id,out action))return true;
        action=(HostAction)id;return Enum.IsDefined(action);
    }
    static string Caption(HostAction action) => HostCommands.Caption(action);
    static string Hint(HostAction action) => HostCommands.Hint(action);
    public void OnCommandHelp(int hFrameWnd, int HelpCommandID, int CommandID) { }
    public void OnCommandUpdateUI(int CommandID, ref int CommandFlags, out string MenuItemText, ref int BitmapID)
    {
        if (!TryAction(CommandID, out var action)) { MenuItemText = "MechCue"; return; }
        MenuItemText = Caption(action);
        // Native flag values verified against Solid Edge 2026 SECommandActivation.
        CommandFlags = 1 | 4 | 16;
        BitmapID = 101 + 10 * ((int)action - 1);
        if (chart is { IsDisposed: false })
        {
            if (chart.IsHostActionChecked(action)) CommandFlags |= 2;
            if (action == HostAction.Play)
            {
                MenuItemText = UiText.IsJapanese ? (chart.IsPlaying ? "一時停止" : "再生") : (chart.IsPlaying ? "Pause" : "Play");
                if (chart.IsPlaying) BitmapID = 511;
            }
        }
    }
    public void OnDisconnection(SE.SeDisconnectMode DisconnectMode)
    {
        try { chart?.ShutdownFromHost(); } catch (Exception ex) { Log(ex.ToString()); }
        chart = null;
        try { if (cookie != 0) commands?.Unadvise(cookie); } catch (Exception ex) { Log(ex.ToString()); }
        cookie = 0; commands = null; addIn = null; application = null; configured.Clear(); runtimeCommands.Clear();
        Log("Disconnected");
    }
    sealed record HostWindow(IntPtr Handle) : IWin32Window;
    static void Report(Exception ex)
    {
        DiagnosticLog.Error("addin", ex); Log(ex.ToString()); MessageBox.Show(UiText.Text(ex.Message), UiText.Text("MechCue アドイン"), MessageBoxButtons.OK, MessageBoxIcon.Information);
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
