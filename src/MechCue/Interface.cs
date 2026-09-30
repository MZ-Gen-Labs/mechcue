namespace MechCue;

public partial class MainForm
{
    void ShowQuickStart()
    {
        MessageBox.Show(this,
            "① グラフを編集\n点をドラッグ：時間と値を変更。線分をドラッグ：上下移動。Shift：時刻変更。Esc：取消。\n\n" +
            "② Solid Edgeに接続し、機構ごとに駆動先を登録\nCADで部品を選択して候補を表示し、対象を強調して確認します。割り当て変更は、その機構の解除だけで行えます。\n\n" +
            "③『Solid Edgeへ反映』をオンにして動作確認\n時間カーソルを動かすか再生します。反映がオフならCADは動きません。\n\n" +
            "値：距離はmm、角度は度。拘束は絶対値、部品の直接移動・回転は登録時からの変化量です。\n\n" +
            "保存されるのはグラフです。駆動先は接続ごとに登録します。PLC読み込みは未対応です。",
            "MechCue — はじめての操作", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
    readonly Label axisHelp = new() { Width = 255, Height = 75, ForeColor = Color.FromArgb(80, 100, 125) };
    void UpdateAxisHelp()
    {
        bool direct = kind.Text.StartsWith("部品");
        axis.Enabled = direct;
        axisHelp.Text = direct ? "X/Y/Zはアセンブリ座標の方向です。回転は部品原点を中心に行います。"
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
            status.Text = reviewMode.Checked ? "動作確認：グラフをクリック・ドラッグして時刻を変更します。" : "編集：点を移動、線分を上下移動。Shiftで時刻変更、Escで取消。";
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
    void ApplyPreview() { if (live.Checked) bridge.Apply((double)time.Value); }
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
    void FromCadSelection()
    {
        Commit();
        var candidates = bridge.TargetsFromSelection(Current.Kind);
        target.Items.Clear(); target.Items.AddRange(candidates.ToArray());
        if (candidates.Count > 0) target.SelectedIndex = 0;
        status.Text = $"選択部品に関連する候補：{candidates.Count}件。対象を確認して登録してください。";
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
