using MechCue;
using System.Text;
using System.Text.Json;

sealed class TrafficReader
{
    sealed class CursorState { public long Offset; public List<byte> Partial = []; public bool SkipFirst; }
    readonly Dictionary<string, CursorState> cursors = new();
    public List<McpTrafficEntry> Poll()
    {
        var entries = new List<McpTrafficEntry>();
        if (!Directory.Exists(McpTraffic.DirectoryPath)) return entries;
        var files = Directory.GetFiles(McpTraffic.DirectoryPath, "*.jsonl").OrderBy(File.GetLastWriteTimeUtc).TakeLast(48).ToArray();
        foreach (var gone in cursors.Keys.Except(files).ToArray()) cursors.Remove(gone);
        foreach (string file in files) {
            try {
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (!cursors.TryGetValue(file, out var cursor)) {
                    cursor = new() { Offset = Math.Max(0, stream.Length - 256 * 1024) }; cursor.SkipFirst = cursor.Offset > 0; cursors.Add(file, cursor);
                }
                if (stream.Length < cursor.Offset) { cursor.Offset = 0; cursor.Partial.Clear(); cursor.SkipFirst = false; }
                stream.Position = cursor.Offset;
                var buffer = new byte[128 * 1024]; int count = stream.Read(buffer); cursor.Offset += count;
                foreach (byte b in buffer.AsSpan(0, count)) {
                    if (b == 10) {
                        if (!cursor.SkipFirst && cursor.Partial.Count > 0) try {
                            var entry = JsonSerializer.Deserialize<McpTrafficEntry>(Encoding.UTF8.GetString(cursor.Partial.ToArray()));
                            if (entry != null) entries.Add(entry);
                        } catch (JsonException) { }
                        cursor.Partial.Clear(); cursor.SkipFirst = false;
                    } else if (!cursor.SkipFirst) {
                        if (cursor.Partial.Count >= 2 * 1024 * 1024) { cursor.Partial.Clear(); cursor.SkipFirst = true; }
                        else cursor.Partial.Add(b);
                    }
                }
            } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        return entries.OrderBy(e => e.Time).ToList();
    }
}
sealed class TrafficMonitorForm : Form
{
    readonly List<McpTrafficEntry> entries = [];
    readonly TrafficReader reader = new();
    readonly DataGridView list = new() { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false, MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, BackgroundColor = SystemColors.Window, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
    readonly TextBox detail = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Font = new Font("Consolas", 10) };
    readonly TextBox search = new() { Width = 180 };
    readonly CheckBox paused = new() { AutoSize = true };
    readonly CheckBox top = new() { AutoSize = true };
    readonly CheckBox capture = new() { AutoSize = true };
    readonly Label status = new() { Dock = DockStyle.Bottom, Height = 24, AutoEllipsis = true };
    readonly System.Windows.Forms.Timer timer = new() { Interval = 500 };
    static string T(string ja, string en) => UiText.IsJapanese ? ja : en;
    public TrafficMonitorForm()
    {
        Text = T("MechCue MCP 通信モニター", "MechCue MCP Traffic Monitor"); Size = new(1080, 680); MinimumSize = new(720, 440); StartPosition = FormStartPosition.CenterScreen;
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new(6) };
        var options = McpTraffic.Options();
        paused.Text = T("表示を一時停止", "Pause display"); top.Text = T("常に手前に表示", "Always on top"); capture.Text = T("通信を記録", "Record traffic");
        top.Checked = TopMost = options.TopMost; capture.Checked = options.Capture;
        bar.Controls.AddRange([capture, paused, top, new Label { Text = T("検索", "Filter"), AutoSize = true, Margin = new(8, 5, 3, 3) }, search]);
        var clear = new Button { Text = T("一覧クリア", "Clear list"), AutoSize = true }; clear.Click += (_, _) => { entries.Clear(); RefreshList(); }; bar.Controls.Add(clear);
        var save = new Button { Text = T("ログ保存", "Save log"), AutoSize = true }; save.Click += (_, _) => Export(); bar.Controls.Add(save);
        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, Panel1MinSize = 100, Panel2MinSize = 80, Size = new(1000, 550), SplitterDistance = 280 };
        foreach (string label in new[] { T("時刻", "Time"), "PID", T("方向", "Direction"), T("機能 / メソッド", "Tool / method"), T("状態", "Status"), "ms" }) list.Columns.Add(label, label);
        list.Columns[0].FillWeight = 100; list.Columns[1].FillWeight = 45; list.Columns[2].FillWeight = 110; list.Columns[3].FillWeight = 240; list.Columns[4].FillWeight = 65; list.Columns[5].FillWeight = 55;
        split.Panel1.Controls.Add(list); split.Panel2.Controls.Add(detail); Controls.Add(split); Controls.Add(status); Controls.Add(bar);
        list.SelectionChanged += (_, _) => ShowDetail(); search.TextChanged += (_, _) => RefreshList();
        top.CheckedChanged += (_, _) => { TopMost = top.Checked; SaveOptions(); }; capture.CheckedChanged += (_, _) => SaveOptions();
        paused.CheckedChanged += (_, _) => UpdateStatus();
        timer.Tick += (_, _) => Poll(); timer.Start(); Poll();
    }
    void SaveOptions() { try { McpTraffic.SaveOptions(new(capture.Checked, top.Checked)); UpdateStatus(); } catch (Exception e) { status.Text = e.Message; } }
    IEnumerable<McpTrafficEntry> Filtered() => entries.Where(e => string.IsNullOrWhiteSpace(search.Text) || (e.Method + " " + e.Id + " " + e.Json).Contains(search.Text, StringComparison.OrdinalIgnoreCase));
    void Poll()
    {
        if (paused.Checked) return;
        try {
            var added = reader.Poll(); if (added.Count == 0) { UpdateStatus(); return; }
            entries.AddRange(added); if (entries.Count > 500) entries.RemoveRange(0, entries.Count - 500); RefreshList();
        } catch (Exception e) { status.Text = e.Message; }
    }
    void RefreshList()
    {
        var selected = list.SelectedRows.Count > 0 ? list.SelectedRows[0].Tag as McpTrafficEntry : null;
        list.SuspendLayout();
        try {
            list.Rows.Clear();
            foreach (var e in Filtered()) {
                int index = list.Rows.Add(e.Time.ToLocalTime().ToString("HH:mm:ss.fff"), e.ProcessId, e.Direction == "in" ? T("AI → MCP", "Client → MCP") : T("MCP → AI", "MCP → Client"), e.Method, State(e.Status), e.DurationMs?.ToString("0.0")); list.Rows[index].Tag = e;
            }
            list.ClearSelection(); var row = list.Rows.Cast<DataGridViewRow>().FirstOrDefault(r => ReferenceEquals(r.Tag, selected)) ?? list.Rows.Cast<DataGridViewRow>().LastOrDefault();
            if (row != null) { row.Selected = true; list.CurrentCell = row.Cells[0]; }
        } finally { list.ResumeLayout(); }
        ShowDetail(); UpdateStatus();
    }
    static string State(string status) => status switch { "request" => T("依頼", "Request"), "notification" => T("通知", "Notification"), "error" => T("失敗", "Error"), "omitted" => T("詳細省略", "Omitted"), _ => T("成功", "Success") };
    void ShowDetail()
    {
        if (list.SelectedRows.Count == 0 || list.SelectedRows[0].Tag is not McpTrafficEntry e) { detail.Clear(); return; }
        try { using var json = JsonDocument.Parse(e.Json); detail.Text = JsonSerializer.Serialize(json.RootElement, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }); }
        catch { detail.Text = e.Json; }
    }
    void UpdateStatus() => status.Text = T("最新500件", "Latest 500 entries") + $" / {list.Rows.Count}" + (paused.Checked ? T(" / 表示停止中", " / Display paused") : "") + (!capture.Checked ? T(" / 記録オフ", " / Recording off") : "") + T(" — 引数・結果・ファイルパスを含みます", " — Includes arguments, results and file paths");
    void Export()
    {
        using var dialog = new SaveFileDialog { Filter = "JSON Lines (*.jsonl)|*.jsonl", FileName = "MechCue-MCP-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".jsonl" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try { File.WriteAllLines(dialog.FileName, Filtered().Select(e => JsonSerializer.Serialize(e)), new UTF8Encoding(false)); status.Text = T("保存しました: ", "Saved: ") + dialog.FileName; }
        catch (Exception e) { MessageBox.Show(this, e.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
    protected override void Dispose(bool disposing) { if (disposing) timer.Dispose(); base.Dispose(disposing); }
    internal static void Verify()
    {
        var journal = new McpTrafficJournal();
        using var bytes = new MemoryStream(); using var input = new McpTrafficStream(bytes, journal, "in");
        byte[] request = Encoding.UTF8.GetBytes("{\"jsonrpc\":\"2.0\",\"id\":77,\"method\":\"tools/call\",\"params\":{\"name\":\"test_日本語\",\"arguments\":{\"meshLevel\":8}}}\n");
        input.Write(request, 0, 13); input.Write(request, 13, request.Length - 13);
        using var outBytes = new MemoryStream(); using var output = new McpTrafficStream(outBytes, journal, "out");
        byte[] reply = Encoding.UTF8.GetBytes("{\"jsonrpc\":\"2.0\",\"id\":77,\"result\":{\"isError\":true,\"content\":[]}}\n"); output.Write(reply, 0, reply.Length);
        if (!bytes.ToArray().SequenceEqual(request) || !outBytes.ToArray().SequenceEqual(reply)) throw new Exception("Monitor modified wire bytes");
        using var form = new TrafficMonitorForm(); form.Show(); Application.DoEvents();
        if (form.entries.Count != 2 || form.entries[1].Status != "error" || form.entries[1].DurationMs == null || !form.entries[1].Method.Contains("日本語")) throw new Exception("Traffic correlation failed");
        form.search.Text = "meshLevel"; if (form.list.Rows.Count != 1 || !form.detail.Text.Contains("8")) throw new Exception("Filter/detail failed");
        form.top.Checked = true; if (!form.TopMost || !McpTraffic.Options().TopMost) throw new Exception("Topmost persistence failed");
        form.paused.Checked = true; journal.Record("in", "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/test\"}"); form.Poll(); if (form.entries.Count != 2) throw new Exception("Display pause failed");
        form.paused.Checked = false; form.Poll(); if (form.entries.Count != 3) throw new Exception("Resume lost messages");
        form.capture.Checked = false; journal.Record("in", "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/hidden\"}"); form.Poll(); if (form.entries.Count != 3) throw new Exception("Capture off failed");
        form.search.Clear(); using var image = new Bitmap(form.Width, form.Height); form.DrawToBitmap(image, new Rectangle(Point.Empty, form.Size)); image.Save(Path.Combine(AppContext.BaseDirectory, "monitor-test-preview.png"));
        form.Close(); using var reopened = new TrafficMonitorForm(); if (!reopened.TopMost || reopened.capture.Checked) throw new Exception("Monitor options not restored");
        McpTraffic.SaveOptions(new());
    }
}
