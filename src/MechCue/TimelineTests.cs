namespace MechCue;

public static partial class SelfTest
{
    static void TestTimelineEditing()
    {
        void Assert(bool value, string message) { if (!value) throw new Exception(message); }
        using var host = new Form { ClientSize = new(1000, 400) };
        using var plot = new TestPlot { Dock = DockStyle.Fill, Tracks = [new Track { Points = [new(0, 0), new(45, 10), new(100, 0)] }] };
        host.Controls.Add(plot); host.Show(); Application.DoEvents();
        var original = plot.Tracks[0].Points.ToArray();
        plot.ZoomTime(10); plot.PanTo(40);
        Assert(plot.ViewStart == 40 && plot.ViewEnd == 50, "Zoom/pan time interval mismatch");
        double sought = -1; plot.Seek = t => sought = t;
        plot.TestModifiers = Keys.Shift; plot.Down(65, 150); plot.Up(65, 150);
        Assert(sought == 40 && plot.Tracks[0].Points.SequenceEqual(original), "Zoomed scrub must use absolute time without editing");
        var point = plot.PointLocation(0, 1);
        plot.TestModifiers = Keys.Control; plot.Down((int)point.X, (int)point.Y); plot.DragTo((int)point.X + 40, (int)point.Y); plot.Up((int)point.X + 40, (int)point.Y);
        Assert(plot.Tracks[0].Points[1].Time > 45 && plot.Tracks[0].Points[1].Time < 47, "Zoomed Ctrl drag used full timeline scale");
        plot.FollowTime(80); Assert(plot.ViewStart <= 80 && plot.ViewEnd >= 80, "Playback cursor outside zoomed view");
        plot.FollowTime(100); Assert(plot.ViewEnd == 100, "End pose outside zoomed view");
        plot.FollowTime(0); Assert(plot.ViewStart == 0, "Loop rewind did not return to beginning");
        plot.PanTo(-100); Assert(plot.ViewStart == 0, "Negative pan not clamped");
        plot.PanTo(10000); Assert(plot.ViewEnd == 100, "Pan beyond chart not clamped");
        double anchor = plot.ViewStart + plot.ViewSpan * .3;
        plot.ZoomTime(2, anchor); Assert(Math.Abs((anchor - plot.ViewStart) / plot.ViewSpan - .3) < 1e-9, "Mouse zoom anchor moved");
        plot.FitTime(); Assert(plot.ViewStart == 0 && plot.ViewEnd == 100, "Fit did not restore entire chart");
        plot.Tracks = [new Track { Points = [new(0, 0), new(1, 0), new(2, 0), new(3, 10), new(4, 10), new(5, 10), new(6, 30)] }];
        plot.UpdateViewport();
        original = plot.Tracks[0].Points.ToArray();
        Assert(Plot.HorizontalNeighbors(original, 2) == (0, 5), "Ramp must include both adjoining plateaus");
        var a = plot.PointLocation(0, 2); var b = plot.PointLocation(0, 3);
        int x = (int)((a.X + b.X) / 2), y = (int)((a.Y + b.Y) / 2);
        plot.TestModifiers = Keys.Alt; plot.Down(x, y); plot.DragTo(x, y + 25);
        var once = plot.Tracks[0].Points.ToArray(); plot.DragTo(x, y + 25); plot.Up(x, y + 25);
        var changed = plot.Tracks[0].Points;
        double delta = changed[0].Value - original[0].Value;
        Assert(delta < 0 && changed.SequenceEqual(once), "Group drag accumulated or did not move");
        Assert(changed.Take(6).Select((p, i) => Math.Abs(p.Value - original[i].Value - delta) < 1e-9 && p.Time == original[i].Time).All(p => p)
            && changed[6] == original[6], "Group drag broke plateau continuity, changed times or moved beyond the plateau");
        a = plot.PointLocation(0, 2); b = plot.PointLocation(0, 3); x = (int)((a.X + b.X) / 2); y = (int)((a.Y + b.Y) / 2);
        plot.Down(x, y); plot.DragTo(x, y - 20); plot.Capture = false;
        Assert(plot.Tracks[0].Points.SequenceEqual(once), "Capture cancellation did not restore entire group");
        Assert(Plot.HorizontalNeighbors([new(0, 3), new(1, 3), new(2, 3), new(3, 3)], 1) == (0, 3), "Flat run did not include all points");
        host.Close();
    }
}

public partial class MainForm
{
    internal void VerifyTimelineInterface()
    {
        void Assert(bool value, string message) { if (!value) throw new Exception(message); }
        using var form = new MainForm(); form.Show(); Application.DoEvents();
        form.grid.Rows[1].Cells[1].Value = 75d; form.Commit();
        form.Undo(); Assert(form.Current.Points[1].Value == 100 && form.redoHistory.Count == 1, "Undo did not retain redo state");
        form.Redo(); Assert(form.Current.Points[1].Value == 75, "Redo did not restore edited graph");
        form.Undo(); form.grid.Rows[1].Cells[1].Value = 60d; form.Commit();
        Assert(form.redoHistory.Count == 0, "New edit did not clear redo branch");
        foreach (var tag in new[] { "元に戻す", "やり直す", "最小表示" })
        {
            var button = form.top.Controls.OfType<Button>().Single(b => Equals(b.Tag, tag));
            Assert(button.Text == "" && button.Image != null && !string.IsNullOrEmpty(button.AccessibleName) && !string.IsNullOrEmpty(form.commandHints.GetToolTip(button)), "Icon command lacks image, accessible name or tooltip");
        }
        form.Current.Points = Enumerable.Range(0, 1001).Select(i => new KeyPoint(i * .1, i < 200 ? 10 : i < 600 ? 30 : 10)).ToList();
        form.RefreshTracks(0, false); form.plot.ZoomTime(10); form.plot.PanTo(70);
        var before = form.Current.Points.ToArray();
        Assert(form.timeScroll.Enabled && form.plot.ViewStart == 70 && form.plot.ViewSpan == 10, "Time navigation not enabled at zoom");
        form.timeScroll.Value = 0;
        Assert(form.plot.ViewStart == 0 && form.Current.Points.SequenceEqual(before), "Scrolling edited graph instead of moving view");
        form.timeScroll.Value = form.timeScroll.Maximum - form.timeScroll.LargeChange + 1;
        Assert(form.plot.ViewEnd == 100, "Scrollbar did not reach final interval");
        form.SetCompact(true); Assert(form.plot.ViewEnd == 100 && form.timeScroll.Visible, "Compact mode lost viewport controls");
        form.SetCompact(false); Assert(form.plot.ViewEnd == 100, "Full mode reset viewport");
        form.time.Value = 20; form.StartPlayback();
        Assert(form.timer.Enabled && form.plot.ViewStart <= 20 && form.plot.ViewEnd >= 20, "Play did not follow initial cursor"); form.PausePlayback();
        form.plot.PanTo(50); form.time.Value = 5;
        Assert(form.plot.ViewStart == 50, "Stopped time changes unexpectedly forced playback following");
        form.plot.FitTime(); Assert(!form.timeScroll.Enabled, "Fit left horizontal scroll enabled");
        using var image = new Bitmap(form.Width, form.Height);
        form.plot.ZoomTime(10); form.plot.PanTo(18); form.DrawToBitmap(image, new Rectangle(Point.Empty, form.Size));
        image.Save(Path.Combine(AppContext.BaseDirectory, "timeline-preview.png"));
        form.Close();
    }
}
