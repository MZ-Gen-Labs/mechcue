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
    const int OpenChart = 1;
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
        addIn.GuiVersion = 4;
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
            Array names = new[] { UiText.IsJapanese ? "タイムチャート\n時間と変位を編集\nタイムチャート" : "Time chart\nEdit time and displacement\nTime chart" };
            Array ids = new[] { OpenChart };
            addIn.SetAddInInfoEx(typeof(TimeChartAddIn).Assembly.Location, EnvCatID, "MechCue", 101, 102, 103, 104, 1, ref names, ref ids);
            if (bFirstTime) addIn.AddCommandBarButton(EnvCatID, "MechCue", OpenChart);
            Log("Assembly command registered");
        }
        catch (Exception ex) { configured.Remove(EnvCatID); Report(ex); }
    }
    public void OnCommand(int CommandID)
    {
        if (CommandID != OpenChart || application == null) return;
        try
        {
            if (chart == null || chart.IsDisposed)
            {
                chart = new MainForm(hostedApplication: application);
                dynamic app = application;
                chart.Show(new HostWindow(new IntPtr((int)app.hWnd)));
            }
            else { chart.Show(); chart.Activate(); }
            Log("Chart opened in Solid Edge process");
        }
        catch (Exception ex) { Report(ex); }
    }
    public void OnCommandHelp(int hFrameWnd, int HelpCommandID, int CommandID) { }
    public void OnCommandUpdateUI(int CommandID, ref int CommandFlags, out string MenuItemText, ref int BitmapID)
    {
        MenuItemText = UiText.Text("タイムチャート");
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
