namespace MechCue;

public partial class MainForm
{
    readonly Dictionary<Control,EditShortcutWindow> editShortcutWindows = new();
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
        void Attach(Control control)
        {
            if(editShortcutWindows.ContainsKey(control))return;
            var window=new EditShortcutWindow(this);editShortcutWindows.Add(control,window);
            control.HandleCreated+=(_,_)=>window.AssignHandle(control.Handle);
            control.HandleDestroyed+=(_,_)=>window.ReleaseHandle();
            if(control.IsHandleCreated)window.AssignHandle(control.Handle);
            control.ControlAdded+=(_,e)=>{if(e.Control!=null)Attach(e.Control);};
            control.Enter += (_, _) => { if (control is TextBoxBase or NumericUpDown) ReleaseEditShortcuts(); else RefreshEditShortcuts(); };
            control.Leave += (_, _) => { if (IsHandleCreated && !IsDisposed) BeginInvoke(RefreshEditShortcuts); };
            foreach(Control child in control.Controls)Attach(child);
        }
        Attach(this);
        Disposed+=(_,_)=>{foreach(var window in editShortcutWindows.Values)window.ReleaseHandle();editShortcutWindows.Clear();};
        grid.CellBeginEdit += (_, _) => ReleaseEditShortcuts();
        grid.CellEndEdit += (_, _) => RefreshEditShortcuts();
        commandHints.SetToolTip(top.Controls.OfType<Button>().Single(b => Equals(b.Tag, "元に戻す")), HostCommands.Hint(HostAction.Undo));
        commandHints.SetToolTip(top.Controls.OfType<Button>().Single(b => Equals(b.Tag, "やり直す")), HostCommands.Hint(HostAction.Redo));
    }
    void RefreshEditShortcuts()
    {
        // Native child routing needs no global shortcut registration.
    }
    void ReleaseEditShortcuts()
    {
    }
    sealed class EditShortcutWindow(MainForm owner):NativeWindow
    {
        internal bool Route(int message,Keys keys)=>message is 0x0100 or 0x0104&&owner.editShortcutWindowActive&&owner.TryEditShortcut(keys);
        protected override void WndProc(ref Message message)
        {
            if(Route(message.Msg,ModifierKeys|(Keys)message.WParam.ToInt32()))return;
            base.WndProc(ref message);
        }
    }
}
