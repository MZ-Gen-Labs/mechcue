namespace MechCue;
public partial class MainForm
{
    readonly ContextMenuStrip dataMenu = new(), viewMenu = new();
    (int Tracks, int Settings, int Points) panelSizes;
    bool tracksHidden, settingsHidden, pointsHidden;
    public bool IsPlaying => timer.Enabled;
    void PausePlayback() { timer.Stop(); RefreshPlayback(); }
    void TogglePlayback() { if (timer.Enabled) PausePlayback(); else StartPlayback(); }
    void RefreshPlayback()
    {
        foreach (var bar in new Control?[] { top, compactBar })
        {
            if (bar == null) continue;
            foreach (var button in bar.Controls.OfType<Button>().Where(b => Equals(b.Tag, "▶ 再生")))
            {
                string command = timer.Enabled ? "一時停止" : "▶ 再生";
                button.Text = ""; button.AutoSize = false; button.Size = new(32, 28);
                var old = button.Image; button.Image = CommandIcons.Create(command); old?.Dispose();
                button.AccessibleName = UiText.IsJapanese ? (timer.Enabled ? "一時停止" : "再生") : (timer.Enabled ? "Pause" : "Play");
                commandHints.SetToolTip(button, UiText.IsJapanese ? (timer.Enabled ? "再生を一時停止します。再度押すと現在時刻から再開します。" : "現在時刻から再生します。") : (timer.Enabled ? "Pause playback. Press again to resume." : "Play from the current time."));
            }

        }
    }
    void AddMenu(ContextMenuStrip menu, HostAction action)
    {
        var item = new ToolStripMenuItem { Tag = action };
        item.Click += (_, _) => ExecuteHostAction(action); menu.Items.Add(item);
    }
    void ConfigureDataMenu()
    {
        foreach (var action in new[] { HostAction.Save, HostAction.JsonOpen, HostAction.JsonSave, HostAction.TableExport, HostAction.TableImport }) AddMenu(dataMenu, action);
        var button = new Button { Text = "データ ▾", Tag = "データ ▾", AutoSize = true };
        button.Click += (_, _) => dataMenu.Show(button, new Point(0, button.Height)); top.Controls.Add(button);
        Disposed += (_, _) => dataMenu.Dispose();
    }
    void ConfigureViewMenu()
    {
        foreach (var action in new[] { HostAction.TracksPanel, HostAction.SettingsPanel, HostAction.PointsPanel }) AddMenu(viewMenu, action);
        var button = new Button { Text = "表示 ▾", Tag = "表示 ▾", AutoSize = true };
        button.Click += (_, _) => { RefreshMenus(); viewMenu.Show(button, new Point(0, button.Height)); }; top.Controls.Add(button);
        Disposed += (_, _) => viewMenu.Dispose();
    }
    void RefreshMenus()
    {
        commandHints.SetToolTip(live, HostCommands.Hint(HostAction.Apply));
        commandHints.SetToolTip(collision, HostCommands.Hint(HostAction.Collision));
        commandHints.SetToolTip(loop, UiText.IsJapanese ? "最後まで再生したら先頭から繰り返します。" : "Repeat from the beginning after reaching the end.");
        commandHints.SetToolTip(aiAccess, HostCommands.Hint(HostAction.Ai));
        foreach (var menu in new[] { dataMenu, viewMenu })
            foreach (var item in menu.Items.OfType<ToolStripMenuItem>())
                if (item.Tag is HostAction action) { item.Text = HostCommands.Caption(action); item.ToolTipText = HostCommands.Hint(action); item.Checked = IsHostActionChecked(action); }
    }
    Control PanelHeading(string text, HostAction action)
    {
        var panel = new Panel { Dock = DockStyle.Top, Height = 42 };
        var label = Heading(text); label.Dock = DockStyle.Fill;
        var hide = new Button { Text = "−", Tag = "パネル非表示", Dock = DockStyle.Right, Width = 30, AccessibleName = text };
        commandHints.SetToolTip(hide, UiText.IsJapanese ? "パネルを隠します。「表示」から復元できます。" : "Hide panel; restore from View.");
        hide.Click += (_, _) => ExecuteHostAction(action);
        panel.Controls.Add(label); panel.Controls.Add(hide); return panel;
    }
    void ConfigureEditorWidth(FlowLayoutPanel editor)
    {
        void Fit()
        {
            int width = Math.Max(80, editor.ClientSize.Width - editor.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - 6);
            foreach (Control control in editor.Controls)
            {
                if (control is TextBoxBase or ComboBox or NumericUpDown) control.Width = width;
                if (control is Label label) { label.MaximumSize = new(width, 0); if (!label.AutoSize) label.Width = width; }
            }
        }
        editor.SizeChanged += (_, _) => Fit(); Fit();
    }
    void TogglePanel(HostAction action)
    {
        if (compact) SetCompact(false);
        switch(action) {
            case HostAction.TracksPanel: tracksHidden = !tracksHidden; split.Panel1Collapsed = tracksHidden; break;
            case HostAction.SettingsPanel: settingsHidden = !settingsHidden; workspace.Panel2Collapsed = settingsHidden; break;
            case HostAction.PointsPanel: pointsHidden = !pointsHidden; vertical.Panel2Collapsed = pointsHidden; break;
        }
        RefreshMenus(); plot.Invalidate();
    }
    public bool IsHostActionChecked(HostAction action) => action switch {
        HostAction.Apply => live.Checked, HostAction.Collision => collision.Checked, HostAction.Loop => loop.Checked,
        HostAction.Overlay => overlay.Checked, HostAction.Edit => editMode.Checked, HostAction.Review => reviewMode.Checked,
        HostAction.Ai => aiAccess.Checked, HostAction.TracksPanel => !tracksHidden, HostAction.SettingsPanel => !settingsHidden,
        HostAction.PointsPanel => !pointsHidden, HostAction.Compact => compact, _ => false
    };
    void InvokeEditorButton(string tag)
    {
        IEnumerable<Control> All(Control parent) => parent.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(All(c)));
        var button = All(this).OfType<Button>().Single(b => Equals(b.Tag, tag));
        // PerformClick ignores controls hidden by collapsed panels.
        bool wasCompact = compact; if (wasCompact) SetCompact(false);
        if (tracksHidden) TogglePanel(HostAction.TracksPanel);
        if (settingsHidden) TogglePanel(HostAction.SettingsPanel);
        button.PerformClick();
    }
}
