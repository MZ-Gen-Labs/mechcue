namespace MechCue;

public partial class MainForm
{
    void SetCompact(bool enabled)
    {
        if (compact == enabled) return;
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        PausePlayback();
        // Layout changes must not enumerate assembly targets through COM.
        if (enabled) CommitEditor(false);
        var layouts = new Control[] { this, top, compactBar, split, workspace, vertical };
        foreach (var control in layouts) control.SuspendLayout();
        try
        {
            compact = enabled;
            var playback = new Control[] { time, speed, loop, live, collision };
            if (enabled)
            {
                editBounds = Bounds; editWindowState = WindowState;
                panelSizes = (split.SplitterDistance, workspace.Panel2.Width, vertical.Panel2.Height);
                playbackPositions.Clear();
                foreach (var control in playback) playbackPositions[control] = top.Controls.GetChildIndex(control);
                compactBar.Controls.Add(new Label { Text = "時刻 [s]", AutoSize = true, Name = "compactTime" });
                compactBar.Controls.Add(time);
                compactBar.Controls.Add(new Label { Text = "速度", AutoSize = true, Name = "compactSpeed" });
                compactBar.Controls.Add(speed); compactBar.Controls.Add(loop); compactBar.Controls.Add(live); compactBar.Controls.Add(collision);
                int position = compactBar.Controls.OfType<Button>().Count();
                foreach (var control in new Control[] { compactBar.Controls["compactTime"]!, time, compactBar.Controls["compactSpeed"]!, speed, loop, live, collision })
                    compactBar.Controls.SetChildIndex(control, position++);
                time.Width = 85; speed.Width = 55;
                WindowState = FormWindowState.Normal; MinimumSize = new(650, 400); Size = new(900, 550);
            }
            else
            {
                foreach (var control in playback.OrderBy(c => playbackPositions[c]))
                {
                    top.Controls.Add(control); top.Controls.SetChildIndex(control, playbackPositions[control]);
                }
                foreach (Control label in compactBar.Controls.Cast<Control>().Where(c => c.Name.StartsWith("compact")).ToArray()) label.Dispose();
                time.Width = 100; speed.Width = 65;
                MinimumSize = new(1180, 740); Bounds = editBounds; WindowState = editWindowState;
            }
            split.Panel1Collapsed = enabled || tracksHidden; workspace.Panel2Collapsed = enabled || settingsHidden; vertical.Panel2Collapsed = enabled || pointsHidden;
            chartHeading.Visible = !enabled;
            top.Visible = !enabled; compactBar.Visible = enabled; status.Visible = connection.Visible = !enabled;

            plot.EditMode = !enabled && !reviewMode.Checked;
            foreach (Control control in compactBar.Controls) ApplyText(control);
            RefreshPlayback();
            RefreshMenus();
            if (enabled) FitCompactBar();
        }
        finally
        {
            foreach (var control in layouts.Reverse()) control.ResumeLayout(true);

            if (!enabled) {
                split.SplitterDistance = Math.Clamp(panelSizes.Tracks, split.Panel1MinSize, Math.Max(split.Panel1MinSize, split.Width - split.SplitterWidth - split.Panel2MinSize));
                workspace.SplitterDistance = Math.Clamp(workspace.Width - workspace.SplitterWidth - panelSizes.Settings, workspace.Panel1MinSize, Math.Max(workspace.Panel1MinSize, workspace.Width - workspace.SplitterWidth - workspace.Panel2MinSize));
                vertical.SplitterDistance = Math.Clamp(vertical.Height - vertical.SplitterWidth - panelSizes.Points, vertical.Panel1MinSize, Math.Max(vertical.Panel1MinSize, vertical.Height - vertical.SplitterWidth - vertical.Panel2MinSize));
            }
            Invalidate(true);
            DiagnosticLog.Write("compact-switch", new { enabled, elapsedMs = elapsed.ElapsedMilliseconds });
        }
    }
    void FitCompactBar()
    {
        compactBar.SuspendLayout();
        foreach (Control control in compactBar.Controls)
        {
            control.Margin = new Padding(2, 3, 2, 3);
            if (control is Button button)
            {
                button.AutoSize = false;
                button.Size = new Size(Equals(button.Tag, "編集画面へ戻る") ? 65 : 32, 28);
                button.AccessibleName = UiText.CommandLabel(button.Tag as string ?? "");
            }
        }
        commandHints.SetToolTip(live, UiText.IsJapanese ? "現在時刻の値をSolid Edgeへ反映します。" : "Apply the current time values to Solid Edge.");
        commandHints.SetToolTip(collision, UiText.IsJapanese ? "干渉を検出したら動作を停止します。" : "Stop playback when interference is detected.");
        compactBar.ResumeLayout(true);
        int width = compactBar.Controls.Cast<Control>().Sum(c => c.Width + c.Margin.Horizontal) + compactBar.Padding.Horizontal;
        int height = compactBar.Controls.Cast<Control>().Max(c => c.Height + c.Margin.Vertical) + compactBar.Padding.Vertical;
        compactBar.Height = height;
        MinimumSize = new(Math.Max(650, width + Width - ClientSize.Width), 400);
    }
    void ShowQuickStart()
    {
        MessageBox.Show(this, UiText.IsJapanese ?
            "① グラフを編集\n点・線分をドラッグ：上下移動。Ctrl＋ドラッグ：時間と値を変更。Shift：時刻変更。Esc：取消。\n\n" +
            "② Solid Edgeに接続し、機構ごとに駆動先を登録\nCADで部品を選択して候補を表示し、対象を強調して確認します。割り当て変更は、その機構の解除だけで行えます。\n\n" +
            "③『Solid Edgeへ反映』をオンにして動作確認\n時間カーソルを動かすか再生します。反映がオフならCADは動きません。\n\n" +
            "値：距離はmm、角度は度。拘束は絶対値、部品移動・回転は登録時からの変化量、部品座標はアセンブリ内の絶対座標です。\n\n" +
            "CAD保存でグラフ・駆動先・再生設定をアセンブリ内へ保存できます。保存時は基準姿勢へ戻ります。次回接続時に復元し、反映はオフで開始します。JSON保存はグラフの書き出しです。PLC読み込みは未対応です。" :
            "1. Edit keyframes: drag vertically; Ctrl also moves time. Shift seeks; Esc cancels.\n\n2. Connect and assign a target to each track. Select a CAD part to find targets.\n\n3. Enable Apply to Solid Edge and play or move the time cursor.\n\nDistances use mm; angles use degrees. Constraints and Absolute position use absolute values. Relative translation/rotation use changes from the assigned pose.\n\nSave to CAD stores charts, targets and playback settings inside the assembly and restores the reference pose. Reconnect to restore settings with CAD reflection off. JSON Save exports charts only. PLC input is not supported.",
            "MechCue — Quick start", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
    readonly Label axisHelp = new() { Width = 255, Height = 150, ForeColor = Color.FromArgb(80, 100, 125) };
    void UpdateAxisHelp()
    {
        bool direct = CurrentKind.StartsWith("部品");
        axis.Enabled = direct;
        axisHelp.Text = CurrentKind == "部品座標" ? "グラフは選択軸の絶対座標 [mm]。他の座標・姿勢は登録時の値を保持します。固定拘束は登録中だけ抑制し、割り当て解除時に復元します。" : direct ? "X/Y/Zはアセンブリ座標の方向です。回転は部品原点を中心に行います。"
            : "拘束の向きは選んだ面・拘束で決まります。X/Y/Zの選択は拘束駆動には使いません。候補をCADで強調して確認してください。";
    }
    readonly RadioButton editMode = new() { Text = "編集", Checked = true, AutoSize = true };
    readonly RadioButton reviewMode = new() { Text = "動作確認", AutoSize = true };
    readonly Stack<List<(Track Track, List<KeyPoint> Points)>> history = new();
    readonly NumericUpDown origin = Number(0, -1000000, 1000000);
    readonly NumericUpDown stroke = Number(100, -1000000, 1000000);
    readonly NumericUpDown moveSeconds = Number(1, 0.001m, 10000);
    readonly NumericUpDown holdSeconds = Number(1, 0.001m, 10000);
    static NumericUpDown Number(decimal value, decimal min, decimal max) => new() { DecimalPlaces = 3, Minimum = min, Maximum = max, Value = value, Width = 240 };
    static Label Heading(string text) => new() { Text = text, Dock = DockStyle.Top, Height = 42, Padding = new Padding(12, 10, 0, 0), BackColor = Color.FromArgb(226, 234, 244), Font = new Font("Yu Gothic UI", 11, FontStyle.Bold) };
    static void AddField(FlowLayoutPanel panel, string label, Control input)
    {
        panel.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(3, 10, 3, 2), ForeColor = Color.FromArgb(70, 85, 105) });
        input.Width = 255; panel.Controls.Add(input);
    }
    void ConfigureModes(FlowLayoutPanel toolbar)
    {
        var modes = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        modes.Controls.Add(editMode); modes.Controls.Add(reviewMode); toolbar.Controls.Add(modes);
        reviewMode.CheckedChanged += (_, _) =>
        {
            PausePlayback(); plot.EditMode = !reviewMode.Checked;
            status.Text = reviewMode.Checked ? "動作確認：グラフをクリック・ドラッグして時刻を変更します。" : "編集：点・線分を上下移動。Ctrlで時間も移動。Shiftで時刻変更、Escで取消。";
        };
        Add(toolbar, "元に戻す", Undo);
        plot.PointSelected = (index, point) =>
        {
            DiagnosticLog.Write("select-point", new { index, point, layout = DiagnosticState() });
            if (point >= 0 && point < grid.Rows.Count && !grid.Rows[point].IsNewRow)
            {
                grid.ClearSelection(); grid.Rows[point].Selected = true;
                // WinForms throws when the grid has only enough space for its header.
                if (grid.Visible && grid.ClientSize.Height > grid.ColumnHeadersHeight + grid.Rows[point].Height + 4)
                {
                    try { grid.FirstDisplayedScrollingRowIndex = point; }
                    catch (InvalidOperationException ex) { DiagnosticLog.Error("scroll-point", ex, DiagnosticState()); }
                }
            }
        };
    }
    void Remember(Track track)
    {
        if (history.Count > 0 && history.Peek().Count == 1 && ReferenceEquals(history.Peek()[0].Track, track) && history.Peek()[0].Points.SequenceEqual(track.Points)) return;
        history.Push([(track, track.Points.ToList())]);
    }
    void Undo()
    {
        PausePlayback();
        if (history.Count == 0) { status.Text = "戻せる編集はありません。"; return; }
        var changes = history.Pop(); foreach(var change in changes)change.Track.Points = change.Points;
        int index = tracks.IndexOf(changes[0].Track); RefreshTracks(Math.Max(0, index)); ApplyPreview(); MarkDocumentSettingsChanged();
        status.Text = "グラフの編集を元に戻しました。";
    }
    void ApplyPreview() { if (live.Checked) Drive((double)time.Value); }
    void ConfigurePreset(FlowLayoutPanel panel)
    {
        AddField(panel, "原点 [mm / °]", origin); AddField(panel, "移動量 [mm / °]", stroke);
        AddField(panel, "片道時間 [s]", moveSeconds); AddField(panel, "保持時間 [s]", holdSeconds);
        Add(panel, "移動 → 保持 → 戻る を挿入", () =>
        {
            Commit(); Remember(Current);
            Current.Points = MotionPreset.OutAndBack((double)origin.Value, (double)stroke.Value, (double)moveSeconds.Value, (double)holdSeconds.Value);
            LoadTrack(); plot.Invalidate(); ApplyPreview(); MarkDocumentSettingsChanged(); status.Text = "選択機構に往復動作を設定しました。元に戻す操作ができます。";
        });
    }
    double? lastCheckedTime;
    void Drive(double requestedTime)
    {
        if (!collision.Checked) { lastCheckedTime = null; bridge.Apply(requestedTime); return; }
        try { bridge.ApplyChecked(requestedTime); lastCheckedTime = requestedTime; }
        catch
        {
            if (lastCheckedTime is double previous)
            {
                // Restore the cursor without triggering a second CAD write.
                live.Checked = false; time.Value = (decimal)previous;
            }
            throw;
        }
    }
    void ReadCurrentValues()
    {
        if (target.SelectedItem is not Target chosen) throw new InvalidOperationException("駆動先を選んでください。");
        PausePlayback(); live.Checked = false; Commit();
        double value = bridge.CurrentValue(Current, chosen);
        Remember(Current);
        Current.Points = Current.Points.Select(p => p with { Value = value }).ToList();
        LoadTrack(); plot.Invalidate(); MarkDocumentSettingsChanged();
        status.Text = $"現在値 {value:0.###} をこのグラフの全点へ設定しました。時刻は保持しています。";
    }
    void FromCadSelection()
    {
        PausePlayback(); live.Checked = false; Commit();
        var candidates = bridge.TargetsFromSelection(Current.Kind);
        target.Items.Clear(); target.Items.AddRange(candidates.ToArray());
        if (candidates.Count > 0) target.SelectedIndex = 0;
        if (candidates.Count == 1) ReadCurrentValues();
        else status.Text = $"選択部品に関連する候補：{candidates.Count}件。対象を選び『現在値を読み込み全点に設定』を押してください。";
    }
    void HighlightTarget()
    {
        if (target.SelectedItem is not Target chosen) throw new InvalidOperationException("駆動先の候補を選んでください。");
        bridge.Highlight(chosen); status.Text = "Solid Edge上で対象を選択しました。";
    }
    internal void VerifyInterface()
    {
        if (CurrentKind != "距離拘束" || axis.Text != "X") throw new Exception("Initial mechanism settings were not loaded");
        if (axis.Enabled) throw new Exception("Constraint mode must not expose direct motion axis");
        grid.Rows[1].Cells[1].Value = 75d; Commit();
        if (Current.Points[1].Value != 75) throw new Exception("Table edit did not commit");
        Undo(); if (Current.Points[1].Value != 100) throw new Exception("Undo did not restore graph");
        trackList.SelectedIndex = 1;
        if (CurrentKind != "角度拘束" || Current.Name != UiText.Text("回転軸")) throw new Exception("Mechanism selection settings mismatch");
        reviewMode.Checked = true; if (plot.EditMode) throw new Exception("Review mode still permits edit");
        editMode.Checked = true; if (!plot.EditMode) throw new Exception("Edit mode did not resume");
        trackList.SelectedIndex = 2; if (!axis.Enabled) throw new Exception("Direct motion axis disabled");
        trackList.SelectedIndex = 0;
        if (CurrentKind != "距離拘束") throw new Exception("Returning to track lost driver kind");
        if (!plot.Overlay || trackList.CheckedItems.Count != tracks.Count) throw new Exception("Overlay must initially show all tracks");
        trackList.SetItemChecked(2, false); Commit();
        if (!plot.Hidden.Contains(tracks[2]) || trackList.GetItemChecked(2)) throw new Exception("Visibility choice lost after commit");
        trackList.SetItemChecked(2, true);
        var checkbox = trackList.GetItemRectangle(2);
        trackList.ClickAt(new Point(5, checkbox.Top + checkbox.Height / 2));
        if (trackList.SelectedIndex != 0 || trackList.GetItemChecked(2)) throw new Exception("Checkbox must toggle without changing edit selection");
        trackList.ClickAt(new Point(45, checkbox.Top + checkbox.Height / 2));
        if (trackList.SelectedIndex != 2 || trackList.GetItemChecked(2)) throw new Exception("Name click must select without toggling visibility");
        trackList.SetItemChecked(2, true); trackList.SelectedIndex = 0;
        if (top.Controls.OfType<Button>().Concat(compactBar.Controls.OfType<Button>()).Any(b => Equals(b.Tag,"停止")) || HostCommands.RibbonActions.Any(a => a is HostAction.Stop or HostAction.Minimize or HostAction.Maximize)) throw new Exception("Redundant stop button remains");
        if (!aiAccess.Checked || aiEndpoint == null) throw new Exception("AI access must default on");
        time.Value = 1; TogglePlayback();
        if (!timer.Enabled) throw new Exception("Playback toggle did not start");
        var playButton = top.Controls.OfType<Button>().Single(b => Equals(b.Tag, "▶ 再生"));
        if (playButton.Text != "" || playButton.Image == null || !(playButton.AccessibleName == "Pause" || playButton.AccessibleName == "一時停止")) throw new Exception("Pause button UI did not update");
        TogglePlayback();
        if (!(playButton.AccessibleName == "Play" || playButton.AccessibleName == "再生")) throw new Exception("Play button UI did not update");
        if (timer.Enabled || time.Value != 1) throw new Exception("Pause lost time");
        TogglePlayback();
        if (!timer.Enabled || playStart != 1) throw new Exception("Resume lost current time");
        PausePlayback();
        foreach (var action in new[] { HostAction.TracksPanel, HostAction.SettingsPanel, HostAction.PointsPanel })
        {
            ExecuteHostAction(action); if (IsHostActionChecked(action)) throw new Exception("Panel hide failed");
            SetCompact(true); SetCompact(false); if (IsHostActionChecked(action)) throw new Exception("Compact reset hidden panel choice");
            ExecuteHostAction(action); if (!IsHostActionChecked(action)) throw new Exception("Panel restore failed");
        }
        split.SplitterDistance = 180; workspace.SplitterDistance = Math.Max(300, workspace.Width - 260); vertical.SplitterDistance = Math.Max(150, vertical.Height - 180);
        if (split.IsSplitterFixed || workspace.IsSplitterFixed || vertical.IsSplitterFixed || vertical.Panel2.Height < 60) throw new Exception("Resizable panel splitters failed");
        if (dataMenu.Items.Count != 6 || top.Controls.OfType<Button>().Any(b => Equals(b.Tag,"保存") || Equals(b.Tag,"表を書き出し"))) throw new Exception("Data menu consolidation failed");
        int originalDistance = vertical.SplitterDistance;
        vertical.Panel2MinSize = 0; vertical.SplitterDistance = vertical.Height - vertical.SplitterWidth - 42;
        PerformLayout(); Application.DoEvents();
        plot.PointSelected?.Invoke(0, 1);
        if (!grid.Rows[1].Selected) throw new Exception("Tiny grid point selection failed");
        vertical.SplitterDistance = originalDistance; vertical.Panel2MinSize = 60;
        var retainedCandidate = new object();
        target.Items.Add(retainedCandidate); target.SelectedItem = retainedCandidate;
        grid.Rows[1].Cells[1].Value = 80d;
        SetCompact(true);
        if (Current.Points[1].Value != 80d || !ReferenceEquals(target.SelectedItem, retainedCandidate))
            throw new Exception("Compact switch lost pending values or reloaded CAD targets");
        plot.PointSelected?.Invoke(0, 1);
        if (!split.Panel1Collapsed || !workspace.Panel2Collapsed || !vertical.Panel2Collapsed || plot.EditMode || time.Parent != compactBar) throw new Exception("Compact graph/playback layout failed");
        Application.DoEvents();
        var originalCompactSize = Size;
        var compactLanguage = UiText.Mode;
        foreach (var lang in new[] { "ja", "en" })
        {
            UiText.SetMode(lang, false);
            Size = MinimumSize; PerformLayout(); Application.DoEvents();
            if (compactBar.WrapContents || compactBar.AutoScroll) throw new Exception("Compact toolbar wraps or scrolls");
            foreach (Control control in compactBar.Controls)
                if (control.Right > compactBar.ClientSize.Width || control.Bottom > compactBar.ClientSize.Height)
                    throw new Exception("Compact toolbar clips: " + control.Text);
            foreach (var button in compactBar.Controls.OfType<Button>().Where(b => Equals(b.Tag, "▶ 再生") || Equals(b.Tag, "停止")))
                if (button.Text != "" || button.Image == null || string.IsNullOrEmpty(commandHints.GetToolTip(button)))
                    throw new Exception("Compact playback icon/hint missing");
        }
        UiText.SetMode(compactLanguage, false); Size = originalCompactSize;
        object firstDocument = new(), otherDocument = new();
        int closed = 0, switched = 0;
        var sink = new ApplicationEventSink(firstDocument, () => { }, () => closed++, () => switched++);
        sink.AfterActiveDocumentChange(firstDocument); sink.BeforeDocumentClose(otherDocument);
        if (closed != 0 || switched != 0) throw new Exception("Unrelated document event closed window");
        sink.AfterActiveDocumentChange(otherDocument); sink.BeforeDocumentClose(firstDocument);
        if (closed != 1 || switched != 1) throw new Exception("Target lifecycle events missed");
        sink.AfterActiveDocumentChange(null!); if (switched != 2) throw new Exception("No active document event missed");
        sink.BeforeQuit(); if (closed != 2) throw new Exception("Host quit event missed");
        using (var snapshot = new Bitmap(Width, Height))
        {
            DrawToBitmap(snapshot, new Rectangle(Point.Empty, Size));
            snapshot.Save(Path.Combine(AppContext.BaseDirectory, "compact-preview.png"));
        }
        SetCompact(false);
        if (!ReferenceEquals(target.SelectedItem, retainedCandidate)) throw new Exception("Editor restore reloaded CAD targets");
        target.Items.Remove(retainedCandidate);
        if (split.Panel1Collapsed || workspace.Panel2Collapsed || vertical.Panel2Collapsed || time.Parent != top || !plot.EditMode) throw new Exception("Edit layout restoration failed");
        overlay.SelectedIndex = 0;
        if (!plot.ShownIndices.SequenceEqual([trackList.SelectedIndex])) throw new Exception("Selected-only mode shows other tracks");
        trackList.SetItemChecked(trackList.SelectedIndex, false);
        overlay.SelectedIndex = 1;
        if (!plot.ShownIndices.Contains(trackList.SelectedIndex)) throw new Exception("Unchecked edit target disappeared");
        foreach (int index in Enumerable.Range(0, tracks.Count)) trackList.SetItemChecked(index, false);
        if (!plot.ShownIndices.SequenceEqual([trackList.SelectedIndex])) throw new Exception("Checked mode shows unchecked other tracks");
        overlay.SelectedIndex = 2;
        if (plot.ShownIndices.Count() != tracks.Count) throw new Exception("All mode respects checkboxes unexpectedly");
        var settingsForMode = new DocumentSettings { DisplayMode = ChartDisplayMode, Tracks = tracks.Select(t => new SavedTrack { Track = t }).ToList() };
        if (DocumentSettings.Parse(settingsForMode.Json()).DisplayMode != "all") throw new Exception("Graph display mode did not persist");
        foreach (int index in Enumerable.Range(0, tracks.Count)) trackList.SetItemChecked(index, true);
        overlay.SelectedIndex = 1;
        if (!kind.Items.Contains("部品座標") || collision.Checked) throw new Exception("New mode/options defaults mismatch");
        time.Value = (decimal)tracks.Max(t => t.Points[^1].Time);
        StartPlayback(); PausePlayback();
        if (time.Value != 0 || playStart != 0) throw new Exception("Play at end must restart at beginning");
        time.Value = 1; StartPlayback(); PausePlayback();
        if (playStart != 1) throw new Exception("Play before end must retain current time");
        var savedLanguage = UiText.Mode;
        try
        {
            UiText.SetMode("en", false); Commit();
            if (kind.Text != "Distance constraint") throw new Exception("Driver display did not change to English");
            if (Current.Kind != "距離拘束" || CurrentKind != "距離拘束") throw new Exception("Language changed internal driver identifiers");
            var connectButton = top.Controls.OfType<Button>().Single(b => Equals(b.Tag, "Solid Edgeに接続"));
            if (connectButton.Text != "Connect" || !(commandHints.GetToolTip(connectButton) ?? "").StartsWith("Connect")) throw new Exception("English command/hint mismatch");
            if (grid.Columns[0].HeaderText != "Time [s]") throw new Exception("English column header missing");
            using (var snapshot = new Bitmap(Width, Height)) { DrawToBitmap(snapshot, new Rectangle(Point.Empty, Size)); snapshot.Save(Path.Combine(AppContext.BaseDirectory, "english-preview.png")); }
            UiText.SetMode("ja", false);
            if (connectButton.Text != "接続") throw new Exception("Japanese switch did not restore short label");
            if (UiText.Resolve("auto", new System.Globalization.CultureInfo("ja-JP")) != "ja" || UiText.Resolve("auto", new System.Globalization.CultureInfo("de-DE")) != "en" || UiText.Resolve("en", new System.Globalization.CultureInfo("ja-JP")) != "en") throw new Exception("Automatic/manual language resolution mismatch");
        }
        finally { UiText.SetMode(savedLanguage, false); }
        var doc = new SelfTest.FakeDocument(); var app = new SelfTest.FakeApplication { ActiveDocument = doc }; app.OpenDocuments.Items.Add(doc);
        var part = new SelfTest.FakePart(); doc.Occurrences.Items.Add(part); doc.SelectSet.Items.Add(part);
        using var form = new MainForm(hostedApplication: app);
        form.Show(); Application.DoEvents();
        form.kind.SelectedItem = "部品座標"; form.axis.SelectedItem = "Y"; form.Commit();
        form.FromCadSelection();
        if (form.Current.Points.Any(p => p.Value != 600) || form.Current.Points.Select(p => p.Time).SequenceEqual(new[] { 0d,2d,4d }) == false)
            throw new Exception("CAD coordinate read must fill values and preserve times");
        var relation = new SelfTest.FakeRelation { Offset = 0.0125 }; part.Relations3d.Items.Add(relation); doc.Relations3d.Items.Add(relation);
        form.kind.SelectedItem = "距離拘束"; form.Commit(); form.FromCadSelection();
        if (form.Current.Points.Any(p => p.Value != 12.5)) throw new Exception("CAD distance read must fill all values");
        form.grid.Rows[1].Cells[1].Value = 25d; form.Commit(); form.ReadCurrentValues();
        if (form.Current.Points.Any(p => p.Value != 12.5)) throw new Exception("Explicit current value read mismatch");
        form.Undo(); if (form.Current.Points[1].Value != 25) throw new Exception("Current value fill must be undoable");
        if (doc.SelectSet.Count != 1) throw new Exception("Candidate lookup cleared CAD selection too early");
        form.target.SelectedItem=form.bridge.Targets(form.Current.Kind).Single();
        IEnumerable<Control> Descendants(Control control) => control.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(Descendants(c)));
        var assignButton=Descendants(form).OfType<Button>().Single(b => Equals(b.Tag,"駆動先を登録・変更"));
        assignButton.PerformClick();
        if(doc.SelectSet.Count!=0 || form.bridge.BindingCount!=1)throw new Exception("Registration must clear CAD selection after binding");
        var bound=form.Current;var label=form.bridge.BoundLabel(bound);
        var replacement=new Track { Id=bound.Id,Name="Externally edited",Kind=bound.Kind,Axis=bound.Axis,Points=[new(0,10),new(2,25),new(4,10)] };
        form.ImportTableData([replacement]);
        if(!ReferenceEquals(form.Current,bound) || form.bridge.BoundLabel(bound)!=label || form.bridge.BindingCount!=1 || form.tracks.Count!=3 || form.live.Checked || form.timer.Enabled)throw new Exception("Connected table import lost assignment or omitted tracks");
        bool metadataRejected=false;try { form.ImportTableData([new Track { Id=bound.Id,Kind="部品座標",Axis="Y" }]); }catch(InvalidOperationException){ metadataRejected=true; }
        if(!metadataRejected || bound.Kind!="距離拘束" || bound.Points[1].Value!=25)throw new Exception("Bound target metadata must reject before modification");
        form.HandleAi(System.Text.Json.JsonSerializer.SerializeToElement(new {method="set_keyframe",args=new {trackId=bound.Id.ToString(),time=2,value=100}}));
        if(bound.Points[1].Value!=100 || form.bridge.BoundLabel(bound)!=label || form.bridge.BindingCount!=1 || form.live.Checked)throw new Exception("AI editing must preserve CAD target and disable reflection");
        form.HandleAi(System.Text.Json.JsonSerializer.SerializeToElement(new {method="undo"}));
        if(bound.Points[1].Value!=25)throw new Exception("AI edit undo on a connected target");
        var originalAll=form.tracks.Select(t=>t.Points.ToList()).ToArray();
        form.HandleAi(System.Text.Json.JsonSerializer.SerializeToElement(new {method="resample",args=new {endTime=10,step=1}}));
        if(form.tracks.Any(t=>t.Points.Count!=11))throw new Exception("AI batch resampling");
        form.HandleAi(System.Text.Json.JsonSerializer.SerializeToElement(new {method="undo"}));
        if(form.tracks.Where((t,i)=>!t.Points.SequenceEqual(originalAll[i])).Any())throw new Exception("AI batch undo must restore all tracks");
        form.ExecuteHostAction(HostAction.Play);if(!form.timer.Enabled)throw new Exception("Ribbon play action");
        form.ExecuteHostAction(HostAction.Stop);if(form.timer.Enabled)throw new Exception("Ribbon stop action");
        form.ExecuteHostAction(HostAction.Compact);if(!form.compact)throw new Exception("Ribbon compact action");
        form.ExecuteHostAction(HostAction.Compact);if(form.compact)throw new Exception("Ribbon edit action");
        form.ExecuteHostAction(HostAction.Maximize);if(form.WindowState!=FormWindowState.Maximized)throw new Exception("Ribbon maximize action");
        form.ExecuteHostAction(HostAction.Minimize);if(form.WindowState!=FormWindowState.Minimized)throw new Exception("Ribbon minimize action");
        form.WindowState=FormWindowState.Normal;
        form.Close();
        using var reopened = new MainForm(hostedApplication: app);
        reopened.Show(); Application.DoEvents();
        if (reopened.Current.Kind != "距離拘束" || reopened.Current.Axis != "Y" || reopened.Current.Points[1].Value != 25 || reopened.live.Checked || reopened.timer.Enabled || reopened.bridge.BindingCount != 1) throw new Exception($"Embedded chart did not restore safely on reconnect: Kind={reopened.Current.Kind}, Axis={reopened.Current.Axis}, Value={reopened.Current.Points[1].Value}, Live={reopened.live.Checked}, Timer={reopened.timer.Enabled}, Ready={reopened.documentReady}, Dirty={form.documentSettingsDirty}, Status={reopened.status.Text}");
        reopened.Close();
    }
}

public static class MotionPreset
{
    public static List<KeyPoint> OutAndBack(double origin, double stroke, double move, double hold)
    {
        if (!double.IsFinite(origin) || !double.IsFinite(stroke) || !double.IsFinite(move) || !double.IsFinite(hold) || move <= 0 || hold <= 0)
            throw new ArgumentException("片道時間と保持時間は0より大きい数値にしてください。");
        var result = new List<KeyPoint> { new(0, origin), new(move, origin + stroke), new(move + hold, origin + stroke), new(2 * move + hold, origin) };
        new Track { Points = result }.Validate(); return result;
    }
}
