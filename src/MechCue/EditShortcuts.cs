namespace MechCue;

public partial class MainForm
{
    const int UndoHotkeyId = 0x5A01, RedoHotkeyId = 0x5A02;
    readonly HashSet<int> editShortcutIds = new();
    bool editShortcutWindowActive;
    Control? FocusedEditor()
    {
        Control focus = this;
        while (focus.Controls.Cast<Control>().FirstOrDefault(c => c.ContainsFocus) is Control child) focus = child;
        if (focus != this) return focus;
        Control? active = ActiveControl;
        while (active is ContainerControl container && container.ActiveControl != null) active = container.ActiveControl;
        return active;
    }
    bool IsTextEditing() => FocusedEditor() is TextBoxBase or NumericUpDown || grid.IsCurrentCellInEditMode;
    bool TryEditShortcut(Keys keys)
    {
        if (keys is not (Keys.Control | Keys.Z) and not (Keys.Control | Keys.Y) || IsTextEditing()) return false;
        ExecuteHostAction(keys == (Keys.Control | Keys.Z) ? HostAction.Undo : HostAction.Redo);
        return true;
    }
    void ConfigureEditShortcuts()
    {
        IEnumerable<Control> All(Control control) => new[] { control }.Concat(control.Controls.Cast<Control>().SelectMany(All));
        foreach (var control in All(this))
        {
            control.Enter += (_, _) => { if (control is TextBoxBase or NumericUpDown) ReleaseEditShortcuts(); else RefreshEditShortcuts(); };
            control.Leave += (_, _) => { if (IsHandleCreated && !IsDisposed) BeginInvoke(RefreshEditShortcuts); };
        }
        grid.CellBeginEdit += (_, _) => ReleaseEditShortcuts();
        grid.CellEndEdit += (_, _) => RefreshEditShortcuts();
        commandHints.SetToolTip(top.Controls.OfType<Button>().Single(b => Equals(b.Tag, "元に戻す")), HostCommands.Hint(HostAction.Undo));
        commandHints.SetToolTip(top.Controls.OfType<Button>().Single(b => Equals(b.Tag, "やり直す")), HostCommands.Hint(HostAction.Redo));
    }
    void RefreshEditShortcuts()
    {
        if (!editShortcutWindowActive || !IsHandleCreated || IsDisposed || IsTextEditing()) { ReleaseEditShortcuts(); return; }
        foreach (var (id, key) in new[] { (UndoHotkeyId, Keys.Z), (RedoHotkeyId, Keys.Y) })
            if (!editShortcutIds.Contains(id))
            {
                if (RegisterHotKey(Handle, id, 0x4002, (uint)key)) editShortcutIds.Add(id);
                else DiagnosticLog.Write("edit-shortcut-unavailable", new { key, error = System.Runtime.InteropServices.Marshal.GetLastWin32Error() });
            }
    }
    void ReleaseEditShortcuts()
    {
        foreach (int id in editShortcutIds) UnregisterHotKey(Handle, id);
        editShortcutIds.Clear();
    }
}
