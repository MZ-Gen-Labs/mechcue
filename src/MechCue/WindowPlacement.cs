using System.Text.Json;

namespace MechCue;

internal sealed record WindowPlacement(Rectangle EditBounds, bool EditMaximized, Rectangle CompactBounds, bool Compact);

internal static class WindowPlacementStore
{
    internal static Rectangle Fit(Rectangle bounds, Rectangle work, Size minimum)
    {
        int width = Math.Min(work.Width, Math.Max(minimum.Width, bounds.Width));
        int height = Math.Min(work.Height, Math.Max(minimum.Height, bounds.Height));
        return new Rectangle(Math.Clamp(bounds.X, work.Left, work.Right - width),
            Math.Clamp(bounds.Y, work.Top, work.Bottom - height), width, height);
    }
    internal static WindowPlacement? Read(string path)
    {
        try
        {
            if (new FileInfo(path).Length > 65536) return null;
            var saved = JsonSerializer.Deserialize<WindowPlacement>(File.ReadAllText(path));
            return saved?.EditBounds.Width > 0 && saved.EditBounds.Height > 0 ? saved : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }
    internal static void Write(string path, WindowPlacement placement)
    {
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(temporary, JsonSerializer.Serialize(placement));
            File.Move(temporary, path, true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { DiagnosticLog.Error("window-placement", error); }
        finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }
}

public partial class MainForm
{
    bool placementReady;
    string PlacementPath => Path.Combine(Environment.GetEnvironmentVariable("MECHCUE_WINDOW_SETTINGS_DIRECTORY") ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MechCue"),
        hostedDocumentWindow ? "window-addin.json" : "window-standalone.json");

    void ConfigureWindowPlacement()
    {
        if (AppContext.TryGetSwitch("MechCue.DisableWindowPlacement", out bool disabled) && disabled) return;
        Shown += (_, _) =>
        {
            var saved = WindowPlacementStore.Read(PlacementPath);
            if (saved != null)
            {
                Rectangle Fit(Rectangle bounds, Size minimum) => WindowPlacementStore.Fit(bounds, Screen.FromRectangle(bounds).WorkingArea, minimum);
                StartPosition = FormStartPosition.Manual;
                var work = Screen.FromRectangle(saved.EditBounds).WorkingArea;
                MinimumSize = new Size(Math.Min(MinimumSize.Width, work.Width), Math.Min(MinimumSize.Height, work.Height));
                Bounds = Fit(saved.EditBounds, MinimumSize);
                if (saved.EditMaximized) WindowState = FormWindowState.Maximized;
                if (saved.Compact)
                {
                    SetCompact(true);
                    if (saved.CompactBounds.Width > 0 && saved.CompactBounds.Height > 0)
                        Bounds = Fit(saved.CompactBounds, MinimumSize);
                }
            }
            placementReady = true;
        };
        ResizeEnd += (_, _) => SaveWindowPlacement();
        FormClosed += (_, _) => SaveWindowPlacement();
    }
    void SaveWindowPlacement()
    {
        if (!placementReady || WindowState == FormWindowState.Minimized) return;
        var normal = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        WindowPlacementStore.Write(PlacementPath, new WindowPlacement(compact ? editBounds : normal,
            (compact ? editWindowState : WindowState) == FormWindowState.Maximized,
            compact ? normal : Rectangle.Empty, compact));
    }
}

public static partial class SelfTest
{
    static void TestWindowPlacement()
    {
        var work = new Rectangle(-1600, 30, 1600, 1000);
        var size = new Size(650, 400);
        var positions = Enumerable.Range(1, 9).Select(n => MainForm.PositionBounds(work, size, n)).ToArray();
        if (positions.Distinct().Count() != 9 || positions.Any(bounds => !work.Contains(bounds)))
            throw new Exception("Nine positions must remain distinct and inside monitor work area");
        if (positions[0].Left != work.Left + 12 || positions[0].Bottom != work.Bottom - 12 ||
            positions[8].Right != work.Right - 12 || positions[8].Top != work.Top + 12 ||
            positions[4].Left + positions[4].Width / 2 != work.Left + work.Width / 2)
            throw new Exception("Numeric keypad corner and center meanings are incorrect");
        Keys[] expectedKeys = [Keys.M, Keys.Oemcomma, Keys.OemPeriod, Keys.J, Keys.K, Keys.L, Keys.U, Keys.I, Keys.O];
        for (int n = 1; n <= 9; n++)
            if (MainForm.WindowPositionShortcut(Keys.Control | Keys.Alt | Keys.Shift | expectedKeys[n - 1]) != n)
                throw new Exception("Letter grid must map to the corresponding nine positions");
        if (MainForm.WindowPositionShortcut(Keys.Control | Keys.Shift | Keys.U) != 0 ||
            MainForm.WindowPositionShortcut(Keys.Control | Keys.Alt | Keys.U) != 0 ||
            MainForm.WindowPositionShortcut(Keys.Alt | Keys.Shift | Keys.U) != 0 ||
            MainForm.WindowPositionShortcut(Keys.Control | Keys.Shift | Keys.Alt | Keys.D1) != 0 ||
            MainForm.WindowPositionShortcut(Keys.Control | Keys.Shift | Keys.NumPad9) != 0 ||
            MainForm.WindowPositionShortcut(Keys.Control | Keys.Shift | Keys.Alt | Keys.Q) != 0)
            throw new Exception("Position shortcuts must not consume other modifier combinations");
        var leftMonitor = new Rectangle(-1920, 0, 1920, 1040);
        var original = new Rectangle(-1800, 100, 1300, 800);
        if (WindowPlacementStore.Fit(original, leftMonitor, new Size(1180, 740)) != original)
            throw new Exception("Placement must retain valid negative monitor coordinates");
        var primary = new Rectangle(0, 0, 1440, 860);
        var recovered = WindowPlacementStore.Fit(original, primary, new Size(1180, 740));
        if (!primary.Contains(recovered)) throw new Exception("Disconnected monitor placement must recover inside work area");
        var small = new Rectangle(0, 0, 1024, 700);
        if (!small.Contains(WindowPlacementStore.Fit(new Rectangle(4000, 4000, 2000, 1200), small, new Size(1180, 740))))
            throw new Exception("Oversized saved window must fit small work area");
        string path = Path.Combine(AppContext.BaseDirectory, "window-placement-test.json");
        var saved = new WindowPlacement(original, true, new Rectangle(-1700, 550, 900, 450), true);
        WindowPlacementStore.Write(path, saved);
        if (WindowPlacementStore.Read(path) != saved) throw new Exception("Position, size, maximized and compact state must roundtrip");
        File.WriteAllText(path, "{broken");
        if (WindowPlacementStore.Read(path) != null) throw new Exception("Corrupt placement must fall back safely");
        File.Delete(path);
    }
}
