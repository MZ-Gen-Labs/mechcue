using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace MechCue;

static class Program
{
    [STAThread] static void Main(string[] args)
    {
        if (args.Contains("--self-test")) { SelfTest.Run(); return; }
        if (args.Contains("--inspect-addins"))
        {
            try { var bridge = new Bridge(); bridge.Connect(); File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "addins-inspection.txt"), bridge.InspectAddIns()); }
            catch (Exception ex) { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "addins-inspection.txt"), ex.ToString()); Environment.ExitCode = 1; }
            return;
        }
        if (args.Contains("--load-addin"))
        {
            try { var bridge = new Bridge(); bridge.Connect(); File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "addin-load-result.txt"), bridge.LoadRegisteredAddIn()); }
            catch (Exception ex) { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "addin-load-result.txt"), ex.ToString()); Environment.ExitCode = 1; }
            return;
        }
        if (args.Contains("--verify-addin"))
        {
            try
            {
                var type = Type.GetTypeFromCLSID(new Guid("79B86022-7C7D-4768-A3A7-CF8EBD1F7826"), true)!;
                var instance = Activator.CreateInstance(type) ?? throw new InvalidOperationException("アドインの生成に失敗しました。");
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "addin-activation-result.txt"), "PASS: registered COM add-in instantiated. Solid Edge ribbon loading still requires verification.");
                MarshalRelease(instance);
            }
            catch (Exception ex) { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "addin-activation-result.txt"), ex.ToString()); Environment.ExitCode = 1; }
            return;
        }
        if (args.Contains("--inspect"))
        {
            try { var b = new Bridge(); var title = b.Connect(); File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "assembly-inspection.txt"), title + Environment.NewLine + b.Inspect()); }
            catch (Exception ex) { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "assembly-inspection.txt"), ex.ToString()); Environment.ExitCode = 1; }
            return;
        }
        if (args.Contains("--integration-test"))
        {
            try { var b = new Bridge(); b.Connect(); File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "integration-result.txt"), b.TestDistance()); }
            catch (Exception ex) { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "integration-result.txt"), ex.ToString()); Environment.ExitCode = 1; }
            return;
        }
        ApplicationConfiguration.Initialize();
        if (args.Contains("--smoke-test"))
        {
            using var form = new MainForm(); form.Show(); Application.DoEvents(); form.VerifyInterface();
            using var image = new Bitmap(form.ClientSize.Width, form.ClientSize.Height); form.DrawToBitmap(image, form.ClientRectangle);
            image.Save(Path.Combine(AppContext.BaseDirectory, "preview.png")); form.Close(); return;
        }
        Application.Run(new MainForm(args.Contains("--fourbar-demo")));
    }
    static void MarshalRelease(object instance) { if (System.Runtime.InteropServices.Marshal.IsComObject(instance)) System.Runtime.InteropServices.Marshal.ReleaseComObject(instance); }
}
public partial class MainForm : Form
{
    readonly List<Track> tracks = [new() { Name = "スライダー" }, new() { Name = "回転軸", Kind = "角度拘束", Points = [new(0, 0), new(2, 90), new(4, 0)] }, new() { Name = "搬送部品", Kind = "部品移動" }];
    readonly Bridge bridge;
    readonly CheckedListBox trackList = new() { Dock = DockStyle.Fill };
    readonly ComboBox kind = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130 };
    readonly ComboBox axis = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 60 };
    readonly ComboBox target = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
    readonly TextBox name = new() { Width = 160 };
    readonly DataGridView grid = new() { Dock = DockStyle.Fill, AllowUserToAddRows = true, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
    readonly CheckBox overlay = new() { Text = "重ねて表示", Checked = true, AutoSize = true };
    readonly FlowLayoutPanel legend = new() { Dock = DockStyle.Top, Height = 36, AutoScroll = true, WrapContents = false };
    readonly Plot plot = new() { Dock = DockStyle.Fill };
    readonly Label status = new() { Dock = DockStyle.Bottom, Height = 34, Text = "点・線分：上下へ移動 / Ctrl＋ドラッグ：時間も移動 / 空白・Shift：時刻変更 / Esc：取消" };
    readonly Label connection = new() { Dock = DockStyle.Bottom, Height = 30, Text = "未接続：Solid Edgeに接続 → 駆動先を登録 → Solid Edgeへ反映をオン", ForeColor = Color.DarkOrange };
    readonly NumericUpDown time = new() { DecimalPlaces = 3, Increment = 0.01m, Maximum = 100000, Width = 100 };
    readonly NumericUpDown speed = new() { DecimalPlaces = 1, Increment = 0.1m, Minimum = 0.1m, Maximum = 10, Value = 1, Width = 65 };
    readonly CheckBox live = new() { Text = "Solid Edgeへ反映", AutoSize = true };
    readonly CheckBox collision = new() { Text = "干渉したら停止", AutoSize = true };
    readonly CheckBox loop = new() { Text = "繰り返し", AutoSize = true };
    readonly System.Windows.Forms.Timer timer = new() { Interval = 50 };
    readonly Stopwatch watch = new();
    double playStart;
    bool loading;
    Track Current => tracks[Math.Max(0, trackList.SelectedIndex)];
    public MainForm(bool fourbarDemo = false, object? hostedApplication = null)
    {
        bridge = hostedApplication == null ? new Bridge() : new Bridge(hostedApplication);
        Text = hostedApplication == null ? "MechCue 独立版 — Solid Edge タイムチャート" : "MechCue アドイン版 — Solid Edge タイムチャート";
        Width = 1440; Height = 900; MinimumSize = new(1180, 740);
        BackColor = Color.FromArgb(239, 243, 248);
        Font = new Font("Yu Gothic UI", 10);
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 125, Padding = new Padding(10), BackColor = Color.White };
        Add(top, "Solid Edgeに接続", () => { status.Text = "接続：" + bridge.Connect(); PopulateTargets(); });
        Add(top, "基準状態に戻す", () => { live.Checked = false; timer.Stop(); bridge.Restore(); status.Text = "接続時の基準状態に戻しました。"; });
        Add(top, "切断", () => { live.Checked = false; timer.Stop(); status.Text = bridge.Disconnect() ?? "切断しました。グラフは保持しています。"; target.Items.Clear(); });
        Add(top, "開く", LoadFile); Add(top, "保存", SaveFile);
        top.Controls.Add(new Label { Text = "時刻 [s]", AutoSize = true }); top.Controls.Add(time);
        Add(top, "▶ 再生", () => { Commit(); playStart = (double)time.Value; watch.Restart(); timer.Start(); });
        Add(top, "停止", () => timer.Stop());
        top.Controls.Add(new Label { Text = "速度", AutoSize = true }); top.Controls.Add(speed); top.Controls.Add(loop); top.Controls.Add(live); top.Controls.Add(collision); top.Controls.Add(overlay);
        ConfigureModes(top);
        Add(top, "使い方", ShowQuickStart);
        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 230 };
        split.Panel1.Controls.Add(trackList);
        var trackButtons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 42 };
        Add(trackButtons, "＋ 機構を追加", () => { Commit(); tracks.Add(new() { Name = $"機構 {tracks.Count + 1}" }); RefreshTracks(tracks.Count - 1); });
        split.Panel1.Controls.Add(trackButtons);
        split.Panel1.Controls.Add(Heading("機構一覧"));
        trackList.BorderStyle = BorderStyle.None; trackList.ItemHeight = 32; trackList.IntegralHeight = false;
        var editor = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14), FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Color.White };
        AddField(editor, "機構名", name); AddField(editor, "駆動方法", kind); AddField(editor, "移動方向・回転軸", axis);
        Add(editor, "グラフを更新", Commit);
        AddField(editor, "駆動先", target);
        Add(editor, "CADで選んだ部品から候補表示", FromCadSelection);
        Add(editor, "すべての候補を表示", PopulateTargets);
        Add(editor, "現在値を読み込み全点に設定", ReadCurrentValues);
        Add(editor, "対象をCADで強調", HighlightTarget);
        Add(editor, "駆動先を登録・変更", () =>
        {
            if (target.SelectedItem is not Target t) throw new InvalidOperationException("駆動先を選んでください。");
            timer.Stop(); live.Checked = false; Commit(); bridge.Bind(Current, t); lastCheckedTime = null;
            PopulateTargets(); status.Text = $"登録：{Current.Name} → {t.Label}。固定拘束は直接駆動の登録中だけ抑制し、解除・切断時に復元します。";
        });
        Add(editor, "この機構の割り当てを解除", () =>
        {
            timer.Stop(); live.Checked = false; bridge.Unbind(Current);
            PopulateTargets(); status.Text = "この機構を基準状態に戻して解除しました。接続・他の登録・グラフは保持しています。";
        });
        editor.Controls.Add(axisHelp);
        ConfigurePreset(editor);
        var vertical = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 360 };
        vertical.Panel1.Controls.Add(plot); vertical.Panel1.Controls.Add(legend); vertical.Panel2.Controls.Add(grid);
        vertical.Panel1.Controls.Add(Heading("タイムチャート"));
        vertical.Panel2.Controls.Add(Heading("選択機構の点を数値で編集"));
        var workspace = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel2 };
        workspace.Panel1.Controls.Add(vertical); workspace.Panel2.Controls.Add(editor); workspace.Panel2.Controls.Add(Heading("選択中の設定"));
        split.Panel2.Controls.Add(workspace);
        Controls.Add(split); Controls.Add(top); Controls.Add(connection); Controls.Add(status);
        kind.Items.AddRange(["距離拘束", "角度拘束", "部品移動", "部品回転", "部品座標"]); axis.Items.AddRange(["X", "Y", "Z"]);
        grid.Columns.Add("Time", "時間 [s]"); grid.Columns.Add("Value", "変位 [mm] / 角度 [°]");
        plot.Overlay = overlay.Checked;
        overlay.CheckedChanged += (_, _) => { timer.Stop(); plot.Overlay = overlay.Checked; plot.Invalidate(); };
        trackList.ItemCheck += (_, e) =>
        {
            if (loading || e.Index >= tracks.Count) return;
            if (e.NewValue == CheckState.Checked) plot.Hidden.Remove(tracks[e.Index]); else plot.Hidden.Add(tracks[e.Index]);
            plot.Invalidate();
        };
        trackList.SelectedIndexChanged += (_, _) => LoadTrack();
        kind.SelectedIndexChanged += (_, _) => { UpdateAxisHelp(); if (!loading) Guard(PopulateTargets); };
        time.ValueChanged += (_, _) => Guard(() => { plot.Time = (double)time.Value; plot.Invalidate(); if (live.Checked) Drive(plot.Time); });
        plot.Seek = t => { timer.Stop(); time.Value = (decimal)Math.Clamp(t, 0, (double)time.Maximum); };
        plot.EditStarting = index =>
        {
            bool ready = false;
            Guard(() => { timer.Stop(); Commit(); trackList.SelectedIndex = index; Remember(Current); ready = true; });
            return ready;
        };
        plot.Edited = index => Guard(() =>
        {
            trackList.SelectedIndex = index; LoadTrack();
            ApplyPreview();
            status.Text = "点を変更しました。下の表にも反映済みです。";
        });
        live.CheckedChanged += (_, _) => { if (live.Checked) Guard(() => { Commit(); Drive((double)time.Value); }); UpdateConnection(); };
        collision.CheckedChanged += (_, _) => { timer.Stop(); lastCheckedTime = null; };
        timer.Tick += (_, _) => Guard(() => { var end = tracks.Max(t => t.Points[^1].Time); double t = playStart + watch.Elapsed.TotalSeconds * (double)speed.Value; if (t >= end) { if (loop.Checked && end > 0) t %= end; else { t = end; timer.Stop(); } } time.Value = (decimal)t; });
        FormClosing += (_, _) =>
        {
            timer.Stop(); live.Checked = false;
            string? warning = bridge.Disconnect();
            if (warning != null) MessageBox.Show(this, warning, "切断", MessageBoxButtons.OK, MessageBoxIcon.Information);
        };
        RefreshTracks(0);
        Shown += (_, _) =>
        {
            split.SplitterDistance = 200;
            workspace.SplitterDistance = Math.Max(400, workspace.Width - 310);
            vertical.SplitterDistance = Math.Max(200, vertical.Height - 230);
            if (hostedApplication != null && !fourbarDemo)
                Guard(() => { status.Text = "接続：" + bridge.Connect(); PopulateTargets(); });
            if (fourbarDemo) Guard(() =>
            {
                var title = bridge.Connect();
                if (!string.Equals(title, "fourbar.asm", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("fourbar.asmを開いてください。");
                tracks.Clear(); tracks.Add(new Track { Name = "四節リンク・支点間距離", Points = [new(0, 215.9), new(2, 216.9), new(4, 215.9)] });
                RefreshTracks(0);
                var driver = bridge.Targets("距離拘束").Single(t => t.Label == "拘束 #5 / Offset");
                bridge.Bind(Current, driver); target.SelectedItem = target.Items.Cast<Target>().First(t => t.Label == driver.Label);
                status.Text = "fourbar.asmに接続済み：拘束 #5 を登録。『Solid Edgeへ反映』をオンにしてグラフをドラッグしてください。";
            });
        };
    }
    public void ShutdownFromHost()
    {
        timer.Stop(); live.Checked = false; bridge.Disconnect(); Dispose();
    }
    void Add(Control parent, string label, Action action)
    {
        var b = new Button { Text = label, AutoSize = true }; b.Click += (_, _) => Guard(action); parent.Controls.Add(b);
    }
    void Guard(Action action) { try { action(); } catch (Exception ex) { timer.Stop(); live.Checked = false; Error(ex); } finally { if (!bridge.Connected) target.Items.Clear(); UpdateConnection(); } }
    void UpdateConnection()
    {
        string? bound = bridge.BoundLabel(Current);
        connection.Text = !bridge.Connected ? "未接続：Solid Edgeに接続 → 駆動先を登録 → Solid Edgeへ反映をオン"
            : $"接続済み / 登録 {bridge.BindingCount} 軸 / このグラフ：{bound ?? "未登録"} / {(live.Checked ? "Solid Edgeへ反映中（現在時刻の値）" : "反映オフ：グラフ編集のみ")}";
        connection.ForeColor = live.Checked ? Color.DarkGreen : Color.DarkOrange;
    }
    void Error(Exception ex) { status.Text = "停止：" + (ex.InnerException ?? ex).Message; MessageBox.Show(this, status.Text, "確認", MessageBoxButtons.OK, MessageBoxIcon.Information); }
    void RefreshTracks(int selected) { loading = true; plot.Hidden.IntersectWith(tracks); trackList.Items.Clear();
        foreach (var t in tracks) trackList.Items.Add(t.Name, !plot.Hidden.Contains(t));
        foreach (Control control in legend.Controls.Cast<Control>().ToArray()) control.Dispose();
        legend.Controls.Clear();
        for (int n = 0; n < tracks.Count; n++)
        {
            int index = n;
            var label = new Label { AutoSize = true, Text = $"━ {tracks[n].Name} [{(Plot.IsAngle(tracks[n]) ? "°" : "mm")}]", ForeColor = Plot.TrackColor(n), Margin = new Padding(8, 6, 8, 0), Cursor = Cursors.Hand };
            label.Click += (_, _) => trackList.SelectedIndex = index;
            legend.Controls.Add(label);
        }
        trackList.SelectedIndex = selected; loading = false; LoadTrack(); plot.Tracks = tracks; plot.Invalidate(); }
    void LoadTrack()
    {
        if (loading) return;
        loading = true; var t = Current; name.Text = t.Name; kind.SelectedItem = t.Kind; axis.SelectedItem = t.Axis;
        grid.Rows.Clear(); foreach (var p in t.Points) grid.Rows.Add(p.Time, p.Value);
        loading = false; UpdateAxisHelp(); plot.Selected = trackList.SelectedIndex; plot.Invalidate(); Guard(PopulateTargets);
    }
    void PopulateTargets()
    {
        string? previous = bridge.BoundLabel(Current) ?? (target.SelectedItem as Target)?.Label;
        target.Items.Clear();
        if (bridge.Connected) target.Items.AddRange(bridge.Targets(kind.Text).ToArray());
        if (previous != null) target.SelectedItem = target.Items.Cast<Target>().FirstOrDefault(t => t.Label == previous);
    }
    void Commit()
    {
        grid.EndEdit();
        var points = grid.Rows.Cast<DataGridViewRow>().Where(r => !r.IsNewRow).Select(r => new KeyPoint(double.Parse(Convert.ToString(r.Cells[0].Value)!, CultureInfo.CurrentCulture), double.Parse(Convert.ToString(r.Cells[1].Value)!, CultureInfo.CurrentCulture))).ToList();
        var candidate = new Track { Points = points }; candidate.Validate();
        if (bridge.BoundLabel(Current) != null && (kind.Text != Current.Kind || axis.Text != Current.Axis)) throw new InvalidOperationException("この機構の割り当てを解除してから、駆動方法・軸を変更してください。全体の切断は不要です。");
        if (!Current.Points.SequenceEqual(points)) Remember(Current);
        Current.Name = name.Text; Current.Kind = kind.Text; Current.Axis = axis.Text; Current.Points = points;
        int i = trackList.SelectedIndex; RefreshTracks(i); status.Text = "グラフを更新しました。";
    }
    void SaveFile() { Commit(); using var d = new SaveFileDialog { Filter = "タイムチャート|*.json", FileName = "motion.json" }; if (d.ShowDialog() == DialogResult.OK) File.WriteAllText(d.FileName, JsonSerializer.Serialize(tracks, new JsonSerializerOptions { WriteIndented = true })); }
    void LoadFile()
    {
        if (bridge.Connected) throw new InvalidOperationException("ファイルを開く前に切断してください。");
        using var d = new OpenFileDialog { Filter = "タイムチャート|*.json" }; if (d.ShowDialog() != DialogResult.OK) return;
        var loaded = JsonSerializer.Deserialize<List<Track>>(File.ReadAllText(d.FileName)) ?? throw new InvalidOperationException("ファイルが空です。");
        if (loaded.Count == 0) throw new InvalidOperationException("グラフがありません。");
        foreach (var t in loaded) { t.Validate(); if (!kind.Items.Contains(t.Kind) || !axis.Items.Contains(t.Axis)) throw new InvalidOperationException("駆動方法または軸が不正です。"); }
        timer.Stop(); history.Clear(); tracks.Clear(); tracks.AddRange(loaded); RefreshTracks(0);
    }
}
class Plot : Control
{
    public bool EditMode = true;
    public bool Overlay;
    public HashSet<Track> Hidden = [];
    IEnumerable<int> VisibleIndices => Enumerable.Range(0, Tracks.Count).Where(i => !Hidden.Contains(Tracks[i]));
    public static bool IsAngle(Track t) => t.Kind.Contains("角度") || t.Kind == "部品回転";
    IEnumerable<int> Editable => Overlay ? VisibleIndices.Where(i => i == Selected) : VisibleIndices;
    public Action<int, int>? PointSelected;
    public List<Track> Tracks = [];
    public int Selected;
    public double Time;
    public Action<double>? Seek;
    public Func<int, bool>? EditStarting;
    public Action<int>? Edited;
    int dragTrack = -1, dragPoint = -1;
    bool scrubbing;
    PlotScale? dragScale;
    PlotScale? dragViewScale;
    KeyPoint? originalPoint;
    KeyPoint? originalEndPoint;
    bool draggingSegment;
    bool freeDrag;
    protected virtual Keys DragModifiers => ModifierKeys;
    Point mouseOrigin;
    bool moved;
    readonly ToolTip hint = new();
    record PlotScale(double End, double Min, double Max, float Top, float Bottom, int Width, int RightMargin = 30)
    {
        public float X(double time) => (float)(65 + time / End * Math.Max(1, Width - 65 - RightMargin));
        public float Y(double value) => (float)(Bottom - (value - Min) / (Max - Min) * (Bottom - Top));
        public double Time(float x) => Math.Clamp((x - 65.0) / Math.Max(1, Width - 65 - RightMargin), 0, 1) * End;
        public double Value(float y) => Min + (Bottom - y) / Math.Max(1, Bottom - Top) * (Max - Min);
    }
    static readonly Color[] Colors = [Color.DodgerBlue, Color.DarkOrange, Color.MediumSeaGreen, Color.MediumPurple, Color.Firebrick, Color.Teal, Color.SaddleBrown, Color.DeepPink];
    public static Color TrackColor(int index) => Colors[index % Colors.Length];
    public Plot()
    {
        DoubleBuffered = true; BackColor = Color.FromArgb(246, 248, 252); TabStop = true;
        AccessibleName = "タイムチャート：点をドラッグして編集、線分を上下に移動、空白で時刻変更";
        hint.SetToolTip(this, "点・線分：上下移動 / Ctrl＋ドラッグ：時間も移動 / 空白・Shift＋ドラッグ：時刻変更 / Esc：編集取消");
    }
    PlotScale GetScale(int index)
    {
        if (dragViewScale != null && (index == dragTrack || (Overlay && IsAngle(Tracks[index]) == IsAngle(Tracks[dragTrack])))) return dragViewScale;
        var t = Tracks[index];
        var visible = VisibleIndices.ToArray();
        var values = Overlay ? visible.Where(i => IsAngle(Tracks[i]) == IsAngle(t)).SelectMany(i => Tracks[i].Points).ToArray() : t.Points.ToArray();
        double min = values.Length == 0 ? 0 : values.Min(p => p.Value), max = values.Length == 0 ? 1 : values.Max(p => p.Value);
        double padding = Math.Max((max - min) * 0.2, Math.Max(Math.Abs(max), Math.Abs(min)) * 0.001);
        padding = Math.Max(padding, 0.1);
        // Constant (or nearly constant) values still need a useful editing range.
        if (max - min <= Math.Max(1e-6, Math.Max(Math.Abs(max), Math.Abs(min)) * 1e-6))
            padding = Math.Max(padding, Math.Max(10, Math.Max(Math.Abs(max), Math.Abs(min)) * 0.1));
        if (Overlay) return new PlotScale(dragScale?.End ?? End, min - padding, max + padding, 32, Math.Max(33, Height - 30), Width, 65);
        float rowHeight = (Height - 35f) / Math.Max(1, visible.Length);
        int row = Array.IndexOf(visible, index);
        return new PlotScale(dragScale?.End ?? End, min - padding, max + padding, row * rowHeight + 25, Math.Max(row * rowHeight + 26, (row + 1) * rowHeight - 16), Width);
    }
    internal PointF PointLocation(int track, int point)
    {
        var scale = GetScale(track); var key = Tracks[track].Points[point];
        return new PointF(scale.X(key.Time), scale.Y(key.Value));
    }
    (int Track, int Point) Hit(Point mouse)
    {
        foreach (int n in Editable)
        {
            var scale = GetScale(n);
            for (int i = 0; i < Tracks[n].Points.Count; i++)
            {
                var p = Tracks[n].Points[i];
                if (Math.Pow(mouse.X - scale.X(p.Time), 2) + Math.Pow(mouse.Y - scale.Y(p.Value), 2) <= 100) return (n, i);
            }
        }
        return (-1, -1);
    }
    (int Track, int Point) HitSegment(Point mouse)
    {
        double nearest = 49;
        (int Track, int Point) result = (-1, -1);
        foreach (int n in Editable)
        {
            var scale = GetScale(n);
            for (int i = 0; i < Tracks[n].Points.Count - 1; i++)
            {
                var a = Tracks[n].Points[i]; var b = Tracks[n].Points[i + 1];
                double x = scale.X(a.Time), y = scale.Y(a.Value);
                double dx = scale.X(b.Time) - x, dy = scale.Y(b.Value) - y;
                double length = dx * dx + dy * dy; if (length <= 0) continue;
                double position = Math.Clamp(((mouse.X - x) * dx + (mouse.Y - y) * dy) / length, 0, 1);
                double distance = Math.Pow(mouse.X - x - position * dx, 2) + Math.Pow(mouse.Y - y - position * dy, 2);
                if (distance < nearest) { nearest = distance; result = (n, i); }
            }
        }
        return result;
    }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        Focus(); var hit = Hit(e.Location); bool segment = false;
        if (hit.Track < 0) { hit = HitSegment(e.Location); segment = hit.Track >= 0; }
        if (EditMode && hit.Track >= 0 && (DragModifiers & Keys.Shift) == 0)
        {
            if (EditStarting != null && !EditStarting(hit.Track)) return;
            // A pending table edit can have moved the point during EditStarting.
            hit = segment ? HitSegment(e.Location) : Hit(e.Location); if (hit.Track < 0) return;
            freeDrag = (DragModifiers & Keys.Control) != 0;
            dragScale = GetScale(hit.Track); dragTrack = hit.Track; dragPoint = hit.Point;
            dragViewScale = dragScale;
            originalPoint = Tracks[dragTrack].Points[dragPoint]; mouseOrigin = e.Location; moved = false;
            draggingSegment = segment; originalEndPoint = segment ? Tracks[dragTrack].Points[dragPoint + 1] : null;
            PointSelected?.Invoke(dragTrack, dragPoint);
            Selected = dragTrack; Cursor = freeDrag ? Cursors.SizeAll : Cursors.SizeNS; Capture = true; Invalidate();
        }
        else { scrubbing = true; Capture = true; Seek?.Invoke(GetTime(e.X)); }
    }
    double GetTime(int x) => Math.Clamp((x - 65.0) / Math.Max(1, Width - (Overlay ? 130 : 95)), 0, 1) * End;
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (dragTrack >= 0 && dragScale != null && originalPoint != null)
        {
            if (!moved && Math.Abs(e.X - mouseOrigin.X) + Math.Abs(e.Y - mouseOrigin.Y) < 3) return;
            moved = true;
            var track = Tracks[dragTrack];
            // Use deltas to avoid snapping a point to the initial grab position.
            double time = originalPoint.Time + (freeDrag ? dragScale.Time(e.X) - dragScale.Time(mouseOrigin.X) : 0);
            double value = originalPoint.Value + dragScale.Value(e.Y) - dragScale.Value(mouseOrigin.Y);
            if (draggingSegment && originalEndPoint != null)
            {
                double delta = value - originalPoint.Value;
                // Always calculate from the mouse-down values; motion must not accumulate.
                track.Points[dragPoint] = originalPoint;
                track.Points[dragPoint + 1] = originalEndPoint;
                track.ShiftSegment(dragPoint, delta);
                if (freeDrag)
                {
                    double dt = time - originalPoint.Time;
                    double lower = dragPoint == 0 ? 0 : Math.BitIncrement(track.Points[dragPoint - 1].Time);
                    double upper = dragPoint + 2 >= track.Points.Count ? dragScale.End : Math.BitDecrement(track.Points[dragPoint + 2].Time);
                    dt = Math.Clamp(dt, lower - originalPoint.Time, upper - originalEndPoint.Time);
                    track.Points[dragPoint] = track.Points[dragPoint] with { Time = originalPoint.Time + dt };
                    track.Points[dragPoint + 1] = track.Points[dragPoint + 1] with { Time = originalEndPoint.Time + dt };
                }
            }
            else track.MovePoint(dragPoint, time, value, dragScale.End);
            // Keep the mouse-to-value conversion fixed during the gesture, while
            // extending the visible range so points beyond the old limits stay visible.
            double low = track.Points.Min(p => p.Value), high = track.Points.Max(p => p.Value);
            double padding = (dragScale.Max - dragScale.Min) * 0.1;
            if (dragViewScale != null)
                dragViewScale = dragViewScale with
                {
                    Min = low < dragViewScale.Min ? low - padding : dragViewScale.Min,
                    Max = high > dragViewScale.Max ? high + padding : dragViewScale.Max
                };
            Invalidate();
        }
        else if (scrubbing) Seek?.Invoke(GetTime(e.X));
        else Cursor = Hit(e.Location).Track >= 0 || HitSegment(e.Location).Track >= 0 ? ((DragModifiers & Keys.Control) != 0 ? Cursors.SizeAll : Cursors.SizeNS) : Cursors.Cross;
    }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e); if (e.Button == MouseButtons.Left) FinishEdit(false);
    }
    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (!Capture) FinishEdit(true);
    }
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape && dragTrack >= 0) { FinishEdit(true); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }
    void FinishEdit(bool cancel)
    {
        int index = dragTrack; bool changed = moved;
        if (cancel && index >= 0 && originalPoint != null)
        {
            Tracks[index].Points[dragPoint] = originalPoint;
            if (draggingSegment && originalEndPoint != null) Tracks[index].Points[dragPoint + 1] = originalEndPoint;
        }
        dragTrack = dragPoint = -1; dragScale = dragViewScale = null; originalPoint = originalEndPoint = null;
        draggingSegment = false; moved = false; scrubbing = false;
        Capture = false; Cursor = Cursors.Cross; Invalidate();
        if (!cancel && index >= 0 && changed) Edited?.Invoke(index);
    }
    protected override void Dispose(bool disposing) { if (disposing) hint.Dispose(); base.Dispose(disposing); }
    double End => Math.Max(1, Tracks.Count == 0 ? 1 : Tracks.Max(t => t.Points[^1].Time));
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); var g = e.Graphics; g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var visible = VisibleIndices.ToArray();
        if (visible.Length == 0) { g.DrawString("機構一覧のチェックで表示する変数を選んでください。", Font, Brushes.Gray, 65, 35); return; }
        void Grid(PlotScale scale)
        {
            for (int i = 0; i <= 4; i++)
            {
                float x = scale.X(scale.End * i / 4);
                g.DrawLine(Pens.LightGray, x, scale.Top, x, scale.Bottom);
                g.DrawString($"{scale.End * i / 4:0.##} s", Font, Brushes.Gray, x - 10, scale.Bottom + 3);
            }
        }
        void Axis(PlotScale scale, bool right, string unit)
        {
            float x = right ? Width - 61 : 3;
            g.DrawString(unit, Font, Brushes.DimGray, x, 1);
            for (int i = 0; i <= 4; i++)
            {
                double value = scale.Min + (scale.Max - scale.Min) * i / 4;
                float y = scale.Y(value);
                g.DrawString($"{value:0.###}", Font, Brushes.Gray, x, y - 7);
                if (!right) g.DrawLine(Pens.Gainsboro, scale.X(0), y, scale.X(scale.End), y);
            }
        }
        if (Overlay)
        {
            Grid(GetScale(visible[0]));
            int distance = Array.FindIndex(visible, i => !IsAngle(Tracks[i]));
            int angle = Array.FindIndex(visible, i => IsAngle(Tracks[i]));
            if (distance >= 0) Axis(GetScale(visible[distance]), false, "mm");
            if (angle >= 0) Axis(GetScale(visible[angle]), true, "°");
            if (visible.Contains(Selected))
            {
                using var brush = new SolidBrush(TrackColor(Selected));
                g.DrawString($"編集対象：{Tracks[Selected].Name}   {Tracks[Selected].At(Time):0.###} {(IsAngle(Tracks[Selected]) ? "°" : "mm")}", Font, brush, 65, 2);
            }
        }
        foreach (int n in visible.OrderBy(i => i == Selected ? 1 : 0))
        {
            var t = Tracks[n]; var scale = GetScale(n); float top = scale.Top, bottom = scale.Bottom;
            float X(double x) => scale.X(x);
            float Y(double y) => scale.Y(y);
            if (!Overlay)
            {
                g.DrawString($"{t.Name}   {t.At(Time):0.###} {(IsAngle(t) ? "°" : "mm")}", Font, Brushes.DimGray, 65, top - 23);
                Grid(scale);
                g.DrawString($"{scale.Max:0.###}", Font, Brushes.Gray, 3, top);
                g.DrawString($"{scale.Min:0.###}", Font, Brushes.Gray, 3, bottom - 12);
            }
            using var pen = new Pen(TrackColor(n), n == Selected ? 3.5f : 1.5f);
            g.DrawLines(pen, t.Points.Select(p => new PointF(X(p.Time), Y(p.Value))).ToArray());
            if (dragTrack == n && draggingSegment)
            {
                using var selectedPen = new Pen(Color.Crimson, 5);
                var a = t.Points[dragPoint]; var b = t.Points[dragPoint + 1];
                g.DrawLine(selectedPen, X(a.Time), Y(a.Value), X(b.Time), Y(b.Value));
            }
            if (!Overlay || n == Selected)
                foreach (var p in t.Points) { g.FillEllipse(Brushes.White, X(p.Time) - 6, Y(p.Value) - 6, 12, 12); g.DrawEllipse(pen, X(p.Time) - 6, Y(p.Value) - 6, 12, 12); }
            if (dragTrack == n && dragPoint >= 0)
            {
                var p = t.Points[dragPoint];
                g.DrawString($"{p.Time:0.###} s / {p.Value:0.###}", Font, Brushes.Black, Math.Clamp(X(p.Time) + 12, 65, Math.Max(65, Width - 190)), Math.Max(top, Y(p.Value) - 24));
            }
            float cursor = X(Time); g.DrawLine(Pens.Crimson, cursor, top, cursor, bottom);
            g.FillEllipse(Brushes.Crimson, cursor - 4, Y(t.At(Time)) - 4, 8, 8);
        }
    }
}
