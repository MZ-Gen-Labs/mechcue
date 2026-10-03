using System.Runtime.InteropServices;

namespace MechCue;

public sealed partial class Bridge
{
    internal IntPtr ApplicationWindowHandle { get { Check(); return new IntPtr(Convert.ToInt64(Get(app!, "hWnd"))); } }
}

public partial class MainForm
{
    readonly ContextMenuStrip cornerMenu = new();
    static readonly int[] WindowPositions = [7, 8, 9, 4, 5, 6, 1, 2, 3];
    internal static readonly Keys[] PositionShortcutKeys = [Keys.M, Keys.Oemcomma, Keys.OemPeriod, Keys.J, Keys.K, Keys.L, Keys.U, Keys.I, Keys.O];
    const Keys PositionModifiers = Keys.Control | Keys.Alt | Keys.Shift;
    int lastShortcutPosition;
    Rectangle lastShortcutBounds;
    bool lastShortcutCompact;
    bool deferPlacementBounds;
    protected override void SetBoundsCore(int x, int y, int width, int height, BoundsSpecified specified)
    {
        // MinimumSize changes during an anchored mode switch must not expose
        // intermediate sizes or the saved editor location to the native window.
        if (!deferPlacementBounds) base.SetBoundsCore(x, y, width, height, specified);
    }
    static string PositionKeyLabel(int position) => PositionShortcutKeys[position - 1] switch
    { Keys.Oemcomma => ",", Keys.OemPeriod => ".", var key => key.ToString() };
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll", SetLastError = true)] static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr window, int id);
    readonly HashSet<int> positionHotkeys = new();
    const int PositionHotkeyBase = 0x4d00;

    // Solid Edge owns the message loop and does not run WinForms keyboard
    // preprocessing for this modeless form. WM_HOTKEY reaches WndProc directly.
    // Register only while this chart is active, leaving other apps' keys alone.
    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        editShortcutWindowActive = true; RefreshEditShortcuts();
        foreach (Keys shortcut in PositionShortcutKeys)
            {
                int key = (int)shortcut;
                int id = PositionHotkeyBase + key;
                if (!positionHotkeys.Contains(id))
                {
                    if (RegisterHotKey(Handle, id, 0x4007, (uint)key)) positionHotkeys.Add(id);
                    else DiagnosticLog.Write("window-shortcut-unavailable", new { key = shortcut.ToString(), error = Marshal.GetLastWin32Error() });
                }
            }
        DiagnosticLog.Write("window-shortcuts-active", new { registered = positionHotkeys.Count });
    }
    void ReleasePositionHotkeys()
    {
        foreach (int id in positionHotkeys) UnregisterHotKey(Handle, id);
        positionHotkeys.Clear();
    }
    protected override void OnDeactivate(EventArgs e)
    {
        editShortcutWindowActive = false; ReleaseEditShortcuts();
        ReleasePositionHotkeys(); base.OnDeactivate(e);
    }
    protected override void OnHandleDestroyed(EventArgs e)
    {
        ReleaseEditShortcuts();
        ReleasePositionHotkeys(); base.OnHandleDestroyed(e);
    }
    protected override void WndProc(ref Message message)
    {
        if (message.Msg == 0x0312 && positionHotkeys.Contains(message.WParam.ToInt32()))
        {
            int position = WindowPositionShortcut(PositionModifiers | (Keys)(message.WParam.ToInt32() - PositionHotkeyBase));
            DiagnosticLog.Write("window-shortcut", new { position });
            Guard(() => PlaceWindow(position, true)); return;
        }
        base.WndProc(ref message);
    }

    internal static Rectangle PositionBounds(Rectangle work, Size size, int position)
    {
        if (position is < 1 or > 9) throw new ArgumentException("position must be 1 through 9 (numeric keypad layout).");
        int margin = Math.Min(12, Math.Max(0, Math.Min(work.Width - size.Width, work.Height - size.Height) / 2));
        int width = Math.Min(size.Width, work.Width - margin * 2), height = Math.Min(size.Height, work.Height - margin * 2);
        int column = (position - 1) % 3, row = (position - 1) / 3;
        return new Rectangle(column == 0 ? work.Left + margin : column == 1 ? work.Left + (work.Width - width) / 2 : work.Right - width - margin,
            row == 0 ? work.Bottom - height - margin : row == 1 ? work.Top + (work.Height - height) / 2 : work.Top + margin, width, height);
    }
    static string PositionLabel(int position) => PositionKeyLabel(position) + "  " + (UiText.IsJapanese ? new[] { "左下", "下中央", "右下", "左中央", "中央", "右中央", "左上", "上中央", "右上" } :
        new[] { "Bottom left", "Bottom center", "Bottom right", "Middle left", "Center", "Middle right", "Top left", "Top center", "Top right" })[position - 1];

    void ConfigureWindowCorners()
    {
        var parent = new ToolStripMenuItem { Tag = "window-corners" };
        foreach (var position in WindowPositions)
        {
            ToolStripMenuItem Create()
            {
                var item = new ToolStripMenuItem { Tag = position, ShortcutKeyDisplayString = "Ctrl+Alt+Shift+" + PositionKeyLabel(position) };
                item.Click += (_, _) => Guard(() => PlaceWindow(position)); return item;
            }
            parent.DropDownItems.Add(Create()); cornerMenu.Items.Add(Create());
        }
        viewMenu.Items.Add(new ToolStripSeparator()); viewMenu.Items.Add(parent);
        var button = new Button { Text = "▦", Tag = "ウィンドウ位置", AutoSize = false, Size = new Size(32, 28) };
        button.Click += (_, _) => { RefreshCornerMenu(); cornerMenu.Show(button, new Point(0, button.Height)); };
        compactBar.Controls.Add(button);
        Disposed += (_, _) => cornerMenu.Dispose();
    }
    void RefreshCornerMenu()
    {
        foreach (var parent in viewMenu.Items.OfType<ToolStripMenuItem>().Where(i => Equals(i.Tag, "window-corners")))
        {
            parent.Text = UiText.IsJapanese ? "CADを最大化して表示位置を選ぶ" : "Maximize CAD and choose chart position";
            foreach (ToolStripMenuItem item in parent.DropDownItems) item.Text = PositionLabel((int)item.Tag!);
        }
        foreach (ToolStripMenuItem item in cornerMenu.Items) item.Text = PositionLabel((int)item.Tag!);
    }
    internal static int WindowPositionShortcut(Keys keys)
    {
        if ((keys & Keys.Modifiers) != PositionModifiers) return 0;
        return Array.IndexOf(PositionShortcutKeys, keys & Keys.KeyCode) + 1;
    }
    protected override bool ProcessCmdKey(ref Message message, Keys keys)
    {
        if (TryEditShortcut(keys)) return true;
        int position = WindowPositionShortcut(keys);
        if (position == 0) return base.ProcessCmdKey(ref message, keys);
        Guard(() => PlaceWindow(position, true)); return true;
    }
    object PlaceWindow(int position, bool shortcut = false)
    {
        if (position is < 1 or > 9) throw new ArgumentException("position must be 1 through 9.");
        var cad = bridge.ApplicationWindowHandle;
        if (!IsWindow(cad)) throw new InvalidOperationException("Solid Edge window is unavailable.");
        var work = Screen.FromHandle(cad).WorkingArea;
        bool toggled = shortcut && lastShortcutPosition == position && lastShortcutBounds == Bounds &&
            lastShortcutCompact == compact && WindowState == FormWindowState.Normal;
        if (toggled)
        {
            SetCompact(!compact, work, position);
        }
        ShowWindow(cad, 3); // SW_MAXIMIZE, on the CAD window's current monitor.
        var size = WindowState == FormWindowState.Normal ? Size : RestoreBounds.Size;
        WindowState = FormWindowState.Normal;
        MinimumSize = new Size(Math.Min(MinimumSize.Width, work.Width), Math.Min(MinimumSize.Height, work.Height));
        Bounds = PositionBounds(work, size, position);
        Show(); Activate(); SaveWindowPlacement();
        lastShortcutPosition = shortcut ? position : 0;
        lastShortcutBounds = Bounds; lastShortcutCompact = compact;
        return new { position, compact, toggled, cadMaximized = true, bounds = Bounds, workingArea = work, saved = placementReady };
    }
}
