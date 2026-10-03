namespace MechCue;
public partial class MainForm
{
    readonly ContextMenuStrip dataMenu = new(), viewMenu = new(), cadMenu = new(), settingsMenu = new();
    (int Tracks, int Settings, int Points) panelSizes;
    bool tracksHidden, settingsHidden, pointsHidden;
    public bool IsPlaying => timer.Enabled;
    void PausePlayback() { timer.Stop(); plot.FollowPlayback = false; RefreshPlayback(); }
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
    void AddMenu(ContextMenuStrip menu, HostAction action) => AddMenu(menu.Items, action);
    void AddMenu(ToolStripItemCollection items, HostAction action)
    {
        var item = new ToolStripMenuItem { Tag = action };
        item.Click += (_, _) => ExecuteHostAction(action); items.Add(item);
    }
    void ConfigureDataMenu()
    {
        AddMenu(dataMenu, HostAction.Save);
        var json = new ToolStripMenuItem("JSON");
        foreach (var action in new[] { HostAction.JsonOpen, HostAction.JsonSave }) AddMenu(json.DropDownItems, action);
        dataMenu.Items.Add(json);
        var tables = new ToolStripMenuItem("CSV / Excel");
        foreach (var action in new[] { HostAction.TableImport, HostAction.TableExport }) AddMenu(tables.DropDownItems, action);
        dataMenu.Items.Add(tables);
        var conceptItem = new ToolStripMenuItem { Text = "概略軸を取り込む", Tag = "concept-import" };
        conceptItem.Click += (_,_)=>Guard(()=>ImportConceptAxes()); dataMenu.Items.Add(conceptItem);
        var logs = new ToolStripMenuItem { Text = "ログフォルダー", Tag = "logs" };
        logs.Click += (_, _) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", DiagnosticLog.DirectoryPath) { UseShellExecute = true });
        settingsMenu.Items.Add(logs);
        var button = new Button { Text = "データ ▾", Tag = "データ ▾", AutoSize = true };
        button.Click += (_, _) => dataMenu.Show(button, new Point(0, button.Height)); top.Controls.Add(button);
        Disposed += (_, _) => dataMenu.Dispose();
    }
    void ConfigureViewMenu()
    {
        AddMenu(viewMenu, HostAction.Compact);
        foreach (var action in new[] { HostAction.TracksPanel, HostAction.SettingsPanel, HostAction.PointsPanel }) AddMenu(viewMenu, action);
        ConfigureWindowCorners();
        var button = new Button { Text = "表示 ▾", Tag = "表示 ▾", AutoSize = true };
        button.Click += (_, _) => { RefreshMenus(); viewMenu.Show(button, new Point(0, button.Height)); }; top.Controls.Add(button);
        Disposed += (_, _) => viewMenu.Dispose();
    }
    void RefreshMenus()
    {
        RefreshCornerMenu();
        RefreshTimeNavigation();
        commandHints.SetToolTip(patternChoice, UiText.IsJapanese ? "動作を切り替えます。再生を停止して先頭へ戻り、反映・干渉・反復の選択を保持します。" : "Switch motion; stop and rewind, preserving Apply, Collision and Loop.");
        commandHints.SetToolTip(live, HostCommands.Hint(HostAction.Apply));
        commandHints.SetToolTip(collision, HostCommands.Hint(HostAction.Collision));
        commandHints.SetToolTip(loop, UiText.IsJapanese ? "最後まで再生したら先頭から繰り返します。" : "Repeat from the beginning after reaching the end.");
        commandHints.SetToolTip(autoApply, UiText.IsJapanese ? "再生時に反映を自動でオンにします。エラー時は自動をオフにします。次回起動時はオフです。" : "Enable Apply when starting playback. Disabled after an error and on next launch.");
        commandHints.SetToolTip(overlay, UiText.IsJapanese ? "選択のみ／選択＋チェック／全機構。編集対象はチェックを外しても表示します。" : "Selected only / Selected + checked / All tracks. The edit target stays visible even when unchecked.");
        commandHints.SetToolTip(aiAccess, HostCommands.Hint(HostAction.Ai));
        IEnumerable<ToolStripMenuItem> Items(ToolStripItemCollection items) => items.OfType<ToolStripMenuItem>().SelectMany(i => new[] { i }.Concat(Items(i.DropDownItems)));
        foreach (var menu in new[] { dataMenu, viewMenu, patternMenu, cadMenu, settingsMenu })
            foreach (var item in Items(menu.Items))
                if (item.Tag is HostAction action) { item.Text = HostCommands.Caption(action); item.ToolTipText = HostCommands.Hint(action); item.Checked = IsHostActionChecked(action); }
                else if (Equals(item.Tag,"concept-import")){item.Text=UiText.IsJapanese?"概略軸を取り込む":"Import concept axes";item.ToolTipText=UiText.IsJapanese?"概略モデルの全軸を現在値で登録します。":"Register all concept axes at their current values.";}
                else if (Equals(item.Tag, "logs")) item.Text = UiText.IsJapanese ? "ログフォルダー" : "Log folder";
                else if (Equals(item.Tag, "language")) item.Text = UiText.IsJapanese ? "言語" : "Language";
                else if (item.Tag is string mode && mode.StartsWith("language-")) item.Checked = UiText.Mode == mode[9..];
    }
    void ConfigureCadMenu()
    {
        foreach (var action in new[] { HostAction.Connect, HostAction.Restore, HostAction.Disconnect }) AddMenu(cadMenu, action);
        var button = new Button { Text = "CAD ▾", Tag = "menu-cad", AutoSize = true };
        button.Click += (_, _) => { RefreshMenus(); cadMenu.Show(button, new Point(0, button.Height)); }; top.Controls.Add(button);
        Disposed += (_, _) => cadMenu.Dispose();
    }
    void ConfigureSettingsMenu()
    {
        AddMenu(settingsMenu, HostAction.Ai);
        var languages = new ToolStripMenuItem { Text = "言語", Tag = "language" };
        foreach (var (mode, label, index) in new[] { ("auto", "Auto", 0), ("ja", "日本語", 1), ("en", "English", 2) })
        {
            var item = new ToolStripMenuItem(label) { Tag = "language-" + mode };
            item.Click += (_, _) => Guard(() => { language.SelectedIndex = index; RefreshMenus(); });
            languages.DropDownItems.Add(item);
        }
        settingsMenu.Items.Add(languages); AddMenu(settingsMenu, HostAction.Help);
        var button = new Button { Text = "設定 ▾", Tag = "menu-settings", AutoSize = true };
        button.Click += (_, _) => { RefreshMenus(); settingsMenu.Show(button, new Point(0, button.Height)); }; top.Controls.Add(button);
        Disposed += (_, _) => { settingsMenu.Dispose(); language.Dispose(); };
    }
    void RestoreReference()
    {
        live.Checked = autoApply.Checked = false; PausePlayback(); bridge.Restore();
        status.Text = "接続時の基準状態に戻しました。";
    }
    void DisconnectDocument()
    {
        live.Checked = autoApply.Checked = false; PausePlayback(); StageOnClose();
        status.Text = bridge.Disconnect() ?? "切断しました。グラフは保持しています。"; target.Items.Clear();
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
        HostAction.Overlay => ChartDisplayMode != "selected", HostAction.Edit => editMode.Checked, HostAction.Review => reviewMode.Checked,
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
