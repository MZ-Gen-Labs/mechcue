namespace MechCue;

public partial class MainForm
{
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct NativeWindowRectangle { public int Left, Top, Right, Bottom; }
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool GetWindowRect(IntPtr window, out NativeWindowRectangle bounds);
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    static extern IntPtr GetProp(IntPtr window, string name);
    internal static bool RedrawDisabledForTest(Control control) => GetProp(control.Handle, "SysSetRedraw") != IntPtr.Zero;
    Rectangle NativeWindowBounds()
    {
        if (!GetWindowRect(Handle, out var bounds)) throw new Exception("Test window bounds unavailable");
        return Rectangle.FromLTRB(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
    }
    internal Rectangle VerifyWindowPositions()
    {
        var points = Current.Points.ToArray();
        Activate(); Application.DoEvents();
        // Windows may deny foreground activation to a hidden build process.
        // Exercise the lifecycle explicitly without stealing the user's focus.
        OnActivated(EventArgs.Empty);
        if (positionHotkeys.Count != 9) throw new Exception("Nine letter shortcuts must register; count=" + positionHotkeys.Count);
        var area = Screen.FromHandle(bridge.ApplicationWindowHandle).WorkingArea;
        var fullSize = Size;
        void Shortcut(int position)
        {
            // Background tests can lose activation while pumping native messages.
            OnActivated(EventArgs.Empty);
            var message = Message.Create(Handle, 0x0312, new IntPtr(PositionHotkeyBase + (int)PositionShortcutKeys[position - 1]), IntPtr.Zero);
            WndProc(ref message); Application.DoEvents();
        }
        void AnchoredToggle(int position)
        {
            var observed = new List<Rectangle>();
            // WinForms may update its managed size/location cache separately.
            // Inspect the native window to check what was actually displayed.
            EventHandler observe = (_, _) => observed.Add(NativeWindowBounds());
            LocationChanged += observe; SizeChanged += observe;
            try { Shortcut(position); }
            finally { LocationChanged -= observe; SizeChanged -= observe; }
            var finalBounds = NativeWindowBounds();
            if (!Visible || GetProp(Handle, "SysSetRedraw") != IntPtr.Zero)
                throw new Exception("Mode switch must restore chart visibility and redraw");
            if (observed.Count == 0 || observed.Any(bounds => bounds != finalBounds))
                throw new Exception("Mode toggle exposed intermediate native bounds: " + string.Join("; ", observed) + " final: " + finalBounds);
        }
        foreach (int position in Enumerable.Range(1, 9))
        {
            // Dispatch directly, as the native CAD loop does: no WinForms
            // ProcessCmdKey or PreProcessMessage keyboard preprocessing.
            Shortcut(position);
            var work = Screen.FromHandle(bridge.ApplicationWindowHandle).WorkingArea;
            if (compact || Bounds != PositionBounds(work, fullSize, position))
                throw new Exception($"First or different shortcut must move full chart without resizing or mode changes: position={position}, compact={compact}, bounds={Bounds}, expected={PositionBounds(work,fullSize,position)}, size={fullSize}, minimum={MinimumSize}, work={work}");
            if (!Current.Points.SequenceEqual(points) || live.Checked)
                throw new Exception("Window placement changed graph values or enabled reflection");
        }
        AnchoredToggle(9);
        if (!compact || Bounds != PositionBounds(area, MinimumSize, 9))
            throw new Exception("Repeated shortcut must toggle to compact at the same position");
        AnchoredToggle(9);
        if (compact || Bounds != PositionBounds(area, fullSize, 9))
            throw new Exception("Third shortcut must restore full size at the same position");
        AnchoredToggle(9); var compactSize = Size;
        Shortcut(1);
        if (!compact || Bounds != PositionBounds(area, compactSize, 1))
            throw new Exception("Different shortcut must move compact chart without toggling");
        AnchoredToggle(1);
        if (compact || Bounds != PositionBounds(area, fullSize, 1))
            throw new Exception("Full mode must resize at the current anchor, not the saved editor position");
        AnchoredToggle(1);
        Bounds = WindowPlacementStore.Fit(new Rectangle(area.Left + 30, area.Top + 50, Width + 80, Height + 40), area, MinimumSize);
        var resized = Size; Shortcut(1);
        if (!compact || Size != resized)
            throw new Exception("Manually moved/resized chart must start with move only again");
        PlaceWindow(1); PlaceWindow(1);
        if (!compact || Size != resized) throw new Exception("Repeated MCP/menu placement must move only");
        Shortcut(1);
        if (!compact) throw new Exception("MCP/menu placement must reset shortcut repeat state");
        if (!Current.Points.SequenceEqual(points) || live.Checked)
            throw new Exception("Mode toggles must retain graph data and CAD reflection setting");
        OnDeactivate(EventArgs.Empty);
        if (positionHotkeys.Count != 0) throw new Exception("Chart shortcuts must be released when another window is active");
        OnActivated(EventArgs.Empty);
        if (positionHotkeys.Count != 9) throw new Exception("Chart shortcuts must be restored when reactivated");
        Bounds = WindowPlacementStore.Fit(new Rectangle(area.Left + 30, area.Top + 50, Width + 80, Height + 40), area, MinimumSize);
        SaveWindowPlacement(); return Bounds;
    }
    internal void VerifyRestoredWindow(Rectangle expected)
    {
        if (!compact || Bounds != expected) throw new Exception("Previous compact size and position were not restored");
    }
}

public static partial class SelfTest
{
    public class FakeWindowApplication : FakeApplication { public long hWnd { get; set; } }
    static void TestWindowPlacementUi()
    {
        string? previousDirectory = Environment.GetEnvironmentVariable("MECHCUE_WINDOW_SETTINGS_DIRECTORY");
        AppContext.TryGetSwitch("MechCue.DisableWindowPlacement", out bool previousDisabled);
        string directory = Path.Combine(AppContext.BaseDirectory, "window-ui-test-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("MECHCUE_WINDOW_SETTINGS_DIRECTORY", directory);
        AppContext.SetSwitch("MechCue.DisableWindowPlacement", false);
        var previousKeys = MainForm.PositionShortcutKeys.ToArray();
        // An installed chart may be open during the build. Exercise native
        // registration/lifetime with a separate chord without taking its keys.
        for (int i = 0; i < MainForm.PositionShortcutKeys.Length; i++) MainForm.PositionShortcutKeys[i] = Keys.F13 + i;
        try
        {
            // Use only windows and documents owned by this test, never the live CAD instance.
            using var host = new Form { Text = "MechCue test host", Size = new Size(800, 600) }; host.Show();
            var document = new FakeDocument();
            var app = new FakeWindowApplication { ActiveDocument = document, hWnd = host.Handle.ToInt64() };
            app.OpenDocuments.Items.Add(document);
            Rectangle saved;
            using (var form = new MainForm(hostedApplication: app))
            {
                form.OpenForAutomation(document.FullName); Application.DoEvents();
                saved = form.VerifyWindowPositions();
                if (host.WindowState != FormWindowState.Maximized) throw new Exception("CAD host window must be maximized");
                form.ShutdownFromHost();
            }
            using var restored = new MainForm(hostedApplication: app);
            restored.OpenForAutomation(document.FullName); Application.DoEvents();
            restored.VerifyRestoredWindow(saved); restored.ShutdownFromHost();
            using var redrawTest = new Form { Text = "Redraw restoration test", Size = new Size(400, 300) };
            redrawTest.Show(); Application.DoEvents();
            int paints = 0;
            redrawTest.Paint += (_, _) => paints++;
            try
            {
                using var pause = new WindowRedrawPause(redrawTest);
                if (!MainForm.RedrawDisabledForTest(redrawTest)) throw new Exception("Visible chart drawing must be paused");
                redrawTest.Invalidate(); redrawTest.Update();
                if (paints != 0) throw new Exception("Intermediate drawing must be suppressed");
                throw new InvalidOperationException("Simulated layout failure");
            }
            catch (InvalidOperationException) { }
            if (!redrawTest.Visible || MainForm.RedrawDisabledForTest(redrawTest))
                throw new Exception("Drawing must resume after a layout exception");
            if (paints == 0) throw new Exception("Completed layout must repaint before returning");
            using var hidden = new Form();
            using (var pause = new WindowRedrawPause(hidden)) { }
            if (hidden.Visible || hidden.IsHandleCreated) throw new Exception("Redraw scope must not show or create a hidden window");
        }
        finally
        {
            Environment.SetEnvironmentVariable("MECHCUE_WINDOW_SETTINGS_DIRECTORY", previousDirectory);
            AppContext.SetSwitch("MechCue.DisableWindowPlacement", previousDisabled);
            previousKeys.CopyTo(MainForm.PositionShortcutKeys, 0);
        }
    }
}
