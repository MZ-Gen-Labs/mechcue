namespace MechCue;

public partial class MainForm
{
    void SetCompact(bool enabled)
    {
        if (compact == enabled) return;
        timer.Stop(); Commit(); compact = enabled;
        var playback = new Control[] { time, speed, loop, live, collision };
        if (enabled)
        {
            editBounds = Bounds; editWindowState = WindowState;
            playbackPositions.Clear();
            foreach (var control in playback) playbackPositions[control] = top.Controls.GetChildIndex(control);
            compactBar.Controls.Add(new Label { Text = "時刻 [s]", AutoSize = true, Name = "compactTime" });
            compactBar.Controls.Add(time);
            compactBar.Controls.Add(new Label { Text = "速度", AutoSize = true, Name = "compactSpeed" });
            compactBar.Controls.Add(speed); compactBar.Controls.Add(loop); compactBar.Controls.Add(live); compactBar.Controls.Add(collision);
            WindowState = FormWindowState.Normal; MinimumSize = new(650, 400); Size = new(900, 550);
        }
        else
        {
            foreach (var control in playback.OrderBy(c => playbackPositions[c]))
            {
                top.Controls.Add(control); top.Controls.SetChildIndex(control, playbackPositions[control]);
            }
            foreach (Control label in compactBar.Controls.Cast<Control>().Where(c => c.Name.StartsWith("compact")).ToArray()) label.Dispose();
            MinimumSize = new(1180, 740); Bounds = editBounds; WindowState = editWindowState;
        }
        split.Panel1Collapsed = enabled; workspace.Panel2Collapsed = enabled; vertical.Panel2Collapsed = enabled;
        top.Visible = !enabled; compactBar.Visible = enabled; status.Visible = connection.Visible = !enabled;
        plot.EditMode = !enabled && !reviewMode.Checked;
        plot.Invalidate();
    }
    void ShowQuickStart()
    {
        MessageBox.Show(this,
            "① グラフを編集\n点・線分をドラッグ：上下移動。Ctrl＋ドラッグ：時間と値を変更。Shift：時刻変更。Esc：取消。\n\n" +
            "② Solid Edgeに接続し、機構ごとに駆動先を登録\nCADで部品を選択して候補を表示し、対象を強調して確認します。割り当て変更は、その機構の解除だけで行えます。\n\n" +
            "③『Solid Edgeへ反映』をオンにして動作確認\n時間カーソルを動かすか再生します。反映がオフならCADは動きません。\n\n" +
            "値：距離はmm、角度は度。拘束は絶対値、部品移動・回転は登録時からの変化量、部品座標はアセンブリ内の絶対座標です。\n\n" +
            "保存されるのはグラフです。駆動先は接続ごとに登録します。PLC読み込みは未対応です。",
            "MechCue — はじめての操作", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
    readonly Label axisHelp = new() { Width = 255, Height = 110, ForeColor = Color.FromArgb(80, 100, 125) };
    void UpdateAxisHelp()
    {
        bool direct = kind.Text.StartsWith("部品");
        axis.Enabled = direct;
        axisHelp.Text = kind.Text == "部品座標" ? "グラフは選択軸の絶対座標 [mm]。他の座標・姿勢は登録時の値を保持します。固定拘束は登録中だけ抑制し、割り当て解除時に復元します。" : direct ? "X/Y/Zはアセンブリ座標の方向です。回転は部品原点を中心に行います。"
            : "拘束の向きは選んだ面・拘束で決まります。X/Y/Zの選択は拘束駆動には使いません。候補をCADで強調して確認してください。";
    }
    readonly RadioButton editMode = new() { Text = "編集", Checked = true, AutoSize = true };
    readonly RadioButton reviewMode = new() { Text = "動作確認", AutoSize = true };
    readonly Stack<(Track Track, List<KeyPoint> Points)> history = new();
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
            timer.Stop(); plot.EditMode = !reviewMode.Checked;
            status.Text = reviewMode.Checked ? "動作確認：グラフをクリック・ドラッグして時刻を変更します。" : "編集：点・線分を上下移動。Ctrlで時間も移動。Shiftで時刻変更、Escで取消。";
        };
        Add(toolbar, "元に戻す", Undo);
        plot.PointSelected = (index, point) =>
        {
            if (point < grid.Rows.Count && !grid.Rows[point].IsNewRow)
            {
                grid.ClearSelection(); grid.Rows[point].Selected = true;
                grid.FirstDisplayedScrollingRowIndex = point;
            }
        };
    }
    void Remember(Track track)
    {
        if (history.Count > 0 && ReferenceEquals(history.Peek().Track, track) && history.Peek().Points.SequenceEqual(track.Points)) return;
        history.Push((track, track.Points.ToList()));
    }
    void Undo()
    {
        timer.Stop();
        if (history.Count == 0) { status.Text = "戻せる編集はありません。"; return; }
        var change = history.Pop(); change.Track.Points = change.Points;
        int index = tracks.IndexOf(change.Track); RefreshTracks(Math.Max(0, index)); ApplyPreview();
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
            LoadTrack(); plot.Invalidate(); ApplyPreview(); status.Text = "選択機構に往復動作を設定しました。元に戻す操作ができます。";
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
        timer.Stop(); live.Checked = false; Commit();
        double value = bridge.CurrentValue(Current, chosen);
        Remember(Current);
        Current.Points = Current.Points.Select(p => p with { Value = value }).ToList();
        LoadTrack(); plot.Invalidate();
        status.Text = $"現在値 {value:0.###} をこのグラフの全点へ設定しました。時刻は保持しています。";
    }
    void FromCadSelection()
    {
        timer.Stop(); live.Checked = false; Commit();
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
        if (kind.Text != "距離拘束" || axis.Text != "X") throw new Exception("Initial mechanism settings were not loaded");
        if (axis.Enabled) throw new Exception("Constraint mode must not expose direct motion axis");
        grid.Rows[1].Cells[1].Value = 75d; Commit();
        if (Current.Points[1].Value != 75) throw new Exception("Table edit did not commit");
        Undo(); if (Current.Points[1].Value != 100) throw new Exception("Undo did not restore graph");
        trackList.SelectedIndex = 1;
        if (kind.Text != "角度拘束" || Current.Name != "回転軸") throw new Exception("Mechanism selection settings mismatch");
        reviewMode.Checked = true; if (plot.EditMode) throw new Exception("Review mode still permits edit");
        editMode.Checked = true; if (!plot.EditMode) throw new Exception("Edit mode did not resume");
        trackList.SelectedIndex = 2; if (!axis.Enabled) throw new Exception("Direct motion axis disabled");
        trackList.SelectedIndex = 0;
        if (kind.Text != "距離拘束") throw new Exception("Returning to track lost driver kind");
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
        SetCompact(true);
        if (!split.Panel1Collapsed || !workspace.Panel2Collapsed || !vertical.Panel2Collapsed || plot.EditMode || time.Parent != compactBar) throw new Exception("Compact graph/playback layout failed");
        Application.DoEvents();
        using (var snapshot = new Bitmap(Width, Height))
        {
            DrawToBitmap(snapshot, new Rectangle(Point.Empty, Size));
            snapshot.Save(Path.Combine(AppContext.BaseDirectory, "compact-preview.png"));
        }
        SetCompact(false);
        if (split.Panel1Collapsed || workspace.Panel2Collapsed || vertical.Panel2Collapsed || time.Parent != top || !plot.EditMode) throw new Exception("Edit layout restoration failed");
        overlay.Checked = false; if (plot.Overlay) throw new Exception("Individual display toggle failed");
        overlay.Checked = true;
        if (!kind.Items.Contains("部品座標") || collision.Checked) throw new Exception("New mode/options defaults mismatch");
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
        form.Close();
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
