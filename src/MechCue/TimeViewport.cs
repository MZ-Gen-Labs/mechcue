namespace MechCue;

partial class Plot
{
    double zoom = 1, viewStart;
    internal double TimelineEnd => End;
    internal double ViewStart => viewStart;
    internal double ViewSpan => End / zoom;
    internal double ViewEnd => ViewStart + ViewSpan;
    internal event Action? ViewportChanged;
    internal void UpdateViewport()
    {
        viewStart = Math.Clamp(viewStart, 0, Math.Max(0, End - ViewSpan));
        ViewportChanged?.Invoke(); Invalidate();
    }
    internal void PanTo(double start)
    {
        if (dragTrack >= 0 || scrubbing) return;
        if (!double.IsFinite(start)) throw new ArgumentOutOfRangeException(nameof(start));
        viewStart = Math.Clamp(start, 0, Math.Max(0, End - ViewSpan));
        UpdateViewport();
    }
    internal void ZoomTime(double factor, double? anchor = null)
    {
        if (dragTrack >= 0 || scrubbing) return;
        if (!double.IsFinite(factor) || factor <= 0) throw new ArgumentOutOfRangeException(nameof(factor));
        double focus = Math.Clamp(anchor ?? (ViewStart + ViewSpan / 2), ViewStart, ViewEnd);
        double fraction = (focus - ViewStart) / ViewSpan;
        zoom = Math.Clamp(zoom * factor, 1, 10000);
        PanTo(focus - fraction * ViewSpan);
    }
    internal void FitTime()
    {
        if (dragTrack >= 0 || scrubbing) return;
        zoom = 1; viewStart = 0; UpdateViewport();
    }
    internal void FollowTime(double time)
    {
        if (time < ViewStart || time > ViewEnd - ViewSpan * .1)
            PanTo(time - ViewSpan * .2);
    }
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (dragTrack >= 0 || scrubbing) return;
        if ((DragModifiers & Keys.Control) != 0) ZoomTime(Math.Pow(1.5, e.Delta / 120.0), GetTime(e.X));
        else PanTo(ViewStart - e.Delta / 120.0 * ViewSpan * .15);
    }
    // Include adjoining constant runs at both ends, even when the selected
    // segment is a ramp between two different plateaus. Times stay unchanged.
    internal static (int First, int Last) HorizontalNeighbors(IReadOnlyList<KeyPoint> points, int segment)
    {
        if (segment < 0 || segment + 1 >= points.Count) throw new ArgumentOutOfRangeException(nameof(segment));
        int first = segment, last = segment + 1;
        while (first > 0 && points[first - 1].Value == points[segment].Value) first--;
        while (last + 1 < points.Count && points[last + 1].Value == points[segment + 1].Value) last++;
        return (first, last);
    }
}

public partial class MainForm
{
    readonly HScrollBar timeScroll = new() { Dock = DockStyle.Fill, Minimum = 0, Maximum = 999999 };
    readonly Label timeRange = new() { AutoSize = true, Margin = new Padding(6, 7, 0, 0) };
    readonly Button zoomIn = new() { Text = "+", Width = 30 }, zoomOut = new() { Text = "−", Width = 30 }, zoomFit = new() { Text = "全体", Width = 55 };
    bool updatingTimeScroll;
    void ConfigureTimeNavigation()
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 36, ColumnCount = 2, RowCount = 1, Padding = new Padding(4, 2, 4, 2) };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var tools = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Dock = DockStyle.Fill };
        tools.Controls.AddRange([zoomOut, zoomIn, zoomFit, timeRange]);
        row.Controls.Add(tools, 0, 0); row.Controls.Add(timeScroll, 1, 0);
        vertical.Panel1.Controls.Add(row);
        zoomIn.Click += (_, _) => plot.ZoomTime(2);
        zoomOut.Click += (_, _) => plot.ZoomTime(.5);
        zoomFit.Click += (_, _) => plot.FitTime();
        timeScroll.ValueChanged += (_, _) =>
        {
            if (updatingTimeScroll) return;
            double max = timeScroll.Maximum - timeScroll.LargeChange + 1;
            plot.PanTo(max > 0 ? (plot.TimelineEnd - plot.ViewSpan) * timeScroll.Value / max : 0);
        };
        plot.ViewportChanged += RefreshTimeNavigation;
        RefreshTimeNavigation();
    }
    void RefreshTimeNavigation()
    {
        updatingTimeScroll = true;
        try
        {
            timeScroll.LargeChange = Math.Clamp((int)Math.Round(plot.ViewSpan / plot.TimelineEnd * 1000000), 1, 1000000);
            timeScroll.SmallChange = Math.Max(1, timeScroll.LargeChange / 10);
            int max = timeScroll.Maximum - timeScroll.LargeChange + 1;
            double range = plot.TimelineEnd - plot.ViewSpan;
            timeScroll.Value = range > 0 ? Math.Clamp((int)Math.Round(plot.ViewStart / range * max), 0, max) : 0;
            timeScroll.Enabled = range > 0;
            timeRange.Text = $"{plot.ViewStart:0.######}–{plot.ViewEnd:0.######} s";
            zoomFit.Text = UiText.IsJapanese ? "全体" : "Fit";
            commandHints.SetToolTip(zoomIn, UiText.IsJapanese ? "時間軸を拡大（Ctrl＋ホイールでも操作できます）" : "Zoom time in (Ctrl + wheel)");
            commandHints.SetToolTip(zoomOut, UiText.IsJapanese ? "時間軸を縮小" : "Zoom time out");
            commandHints.SetToolTip(zoomFit, UiText.IsJapanese ? "時間軸の全体を表示" : "Show the full timeline");
            commandHints.SetToolTip(timeScroll, UiText.IsJapanese ? "表示する時間帯を左右に移動" : "Scroll the visible time interval");
        }
        finally { updatingTimeScroll = false; }
    }
}
