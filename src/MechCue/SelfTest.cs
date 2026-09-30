namespace MechCue;
static class SelfTest
{
    public static void Run()
    {
        void Assert(bool result, string message) { if (!result) throw new Exception(message); }
        var t = new Track();
        Assert(t.At(-1) == 0 && t.At(1) == 50 && t.At(2) == 100 && t.At(3) == 50 && t.At(5) == 0, "Interpolation");
        t.Points = [new(0, 0), new(0, 1)];
        bool rejected = false; try { t.Validate(); } catch (InvalidOperationException) { rejected = true; }
        Assert(rejected, "Duplicate time validation");
        var editable = new Track();
        editable.MovePoint(1, 9, 60, 4); editable.Validate();
        Assert(editable.Points[1].Time < 4 && editable.Points[1].Value == 60, "Point cannot cross next keyframe");
        editable.MovePoint(0, -1, -20, 4); editable.Validate();
        Assert(editable.Points[0].Time == 0, "Nonnegative time");
        editable = new Track(); editable.ShiftSegment(0, 25);
        Assert(editable.Points.SequenceEqual(new[] { new KeyPoint(0, 25), new KeyPoint(2, 125), new KeyPoint(4, 0) }), "Segment keeps times, slope, and other endpoint");
        TestDrag();
        TestConnectionRecovery();
        TestReassignment();
        var preset = MotionPreset.OutAndBack(10, 50, 2, 1);
        var presetTrack = new Track { Points = preset };
        Assert(presetTrack.At(0) == 10 && presetTrack.At(2.5) == 60 && presetTrack.At(5) == 10, "Preset origin, hold, and return");
        double[] basis = [1,0,0,0, 0,1,0,0, 0,0,1,0, 0.5,0.6,0.7,1];
        var moved = Transform.Apply(basis, "部品移動", "X", 100);
        Assert(Math.Abs(moved[12] - 0.6) < 1e-10 && basis[12] == 0.5, "Units and immutable baseline");
        foreach (var (axis, index) in new[] { ("X", 12), ("Y", 13), ("Z", 14) })
        {
            var matrix = Transform.Apply(basis, "部品移動", axis, 100);
            for (int i = 0; i < 16; i++)
                Assert(Math.Abs(matrix[i] - basis[i] - (i == index ? 0.1 : 0)) < 1e-10, "Direct translation axis " + axis);
        }
        var rotated = Transform.Apply(basis, "部品回転", "Z", 90);
        Assert(Math.Abs(rotated[1] - 1) < 1e-10 && Math.Abs(rotated[4] + 1) < 1e-10 && rotated[12] == 0.5, "Rotation around occurrence origin");
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "self-test-result.txt"), "PASS: interpolation, validation, units, baseline, rotation, point bounds, segment translation, mouse point/segment drag, scrub, cancellation");
    }
    public class FakeCollection
    {
        public List<object> Items = [];
        public int Count => Items.Count;
        public object Item(int index) => Items[index - 1];
    }
    public class FakeDocument
    {
        public FakeCollection Occurrences { get; } = new();
        public FakeCollection Relations3d { get; } = new();
        public FakeCollection SelectSet { get; } = new();
    }
    public class FakePart { public string Name { get; set; } = "Part"; public FakeCollection Relations3d { get; } = new(); }
    public class FakeRelation { public double Offset { get; set; } }
    public class FakeApplication
    {
        public bool Dead;
        public FakeCollection OpenDocuments = new();
        public FakeCollection Documents => Dead ? throw new System.Runtime.InteropServices.COMException("Disconnected", unchecked((int)0x80010108)) : OpenDocuments;
        public object? ActiveDocument { get; set; }
        public FakeWindow ActiveWindow { get; } = new();
    }
    public class FakeWindow { public FakeView View { get; } = new(); }
    public class FakeView { public void Update() { } }
    static void TestReassignment()
    {
        void Assert(bool ok, string error) { if (!ok) throw new Exception(error); }
        var doc = new FakeDocument(); var app = new FakeApplication { ActiveDocument = doc }; app.OpenDocuments.Items.Add(doc);
        var bridge = new Bridge(app, doc);
        var a = new FakeRelation { Offset = 0.01 }; var b = new FakeRelation { Offset = 0.02 }; var c = new FakeRelation { Offset = 0.05 };
        var t1 = new Track { Points = [new(0, 30), new(1, 30)] }; var t2 = new Track { Points = [new(0, 40), new(1, 40)] };
        bridge.Bind(t1, new Target("A", a, "Offset")); bridge.Bind(t2, new Target("B", b, "Offset")); bridge.Apply(0);
        try { bridge.Bind(t1, new Target("B", b, "Offset")); throw new Exception("Conflicting assignment accepted"); } catch (InvalidOperationException) { }
        Assert(bridge.BoundLabel(t1) == "A" && a.Offset == 0.03 && b.Offset == 0.04, "Rejected rebind must preserve previous assignments and values");
        bridge.Bind(t1, new Target("C", c, "Offset"));
        Assert(a.Offset == 0.01 && b.Offset == 0.04 && bridge.BoundLabel(t1) == "C" && bridge.BindingCount == 2, "Rebind restores only old target");
        bridge.Apply(0); bridge.Unbind(t1);
        Assert(c.Offset == 0.05 && b.Offset == 0.04 && bridge.Connected && bridge.BindingCount == 1, "Unbind preserves connection and other driver");
        bridge.Unbind(t1); bridge.Unbind(t2);
        Assert(b.Offset == 0.02 && bridge.Connected && bridge.BindingCount == 0, "All unbound does not disconnect");
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "assignment-test-result.txt"), "PASS: rebind, duplicate rejection, selected-only restore, unbind, preserved connection, XYZ translation");
    }
    static void TestConnectionRecovery()
    {
        var doc = new FakeDocument(); var app = new FakeApplication { ActiveDocument = doc };
        app.OpenDocuments.Items.Add(doc);
        var bridge = new Bridge(app, doc);
        var selected = new FakePart { Name = "Selected" }; var other = new FakePart { Name = "Other" };
        var relation = new FakeRelation { Offset = 0.01 }; var unrelated = new FakeRelation { Offset = 0.03 };
        doc.Occurrences.Items.AddRange([selected, other]); doc.Relations3d.Items.AddRange([relation, unrelated]);
        selected.Relations3d.Items.Add(relation); doc.SelectSet.Items.Add(selected);
        var candidates = bridge.TargetsFromSelection("距離拘束");
        if (candidates.Count != 1 || !ReferenceEquals(candidates[0].Com, relation)) throw new Exception("CAD selection filtering mismatch");
        if (!candidates[0].ToString().Contains("10 mm")) throw new Exception("Target units were not converted");
        doc.SelectSet.Items.Clear();
        try { bridge.TargetsFromSelection("距離拘束"); throw new Exception("Empty selection accepted"); } catch (InvalidOperationException) { }
        app.OpenDocuments.Items.Clear();
        try { bridge.Targets("部品移動"); throw new Exception("Closed document accepted"); }
        catch (InvalidOperationException) { if (bridge.Connected) throw new Exception("Closed connection retained"); }
        if (bridge.Disconnect() != null) throw new Exception("Repeated disconnect must succeed");
        app.OpenDocuments.Items.Add(doc); app.Dead = true; bridge = new Bridge(app, doc);
        try { bridge.Targets("部品移動"); throw new Exception("Dead application accepted"); }
        catch (InvalidOperationException) { if (bridge.Connected) throw new Exception("Dead application retained"); }
        app.Dead = false; app.ActiveDocument = new FakeDocument(); bridge = new Bridge(app, doc);
        try { bridge.Targets("部品移動"); throw new Exception("Inactive document driven"); }
        catch (InvalidOperationException) { if (!bridge.Connected) throw new Exception("Inactive live document incorrectly forgotten"); }
        bridge.Disconnect();
        if (bridge.Connected) throw new Exception("Disconnect cannot be trapped by restore failure");
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "connection-test-result.txt"), "PASS: closed assembly, restarted application, inactive assembly, repeat disconnect, restore failure releases connection");
    }
    sealed class TestPlot : Plot
    {
        public void Down(int x, int y) => OnMouseDown(new MouseEventArgs(MouseButtons.Left, 1, x, y, 0));
        public void DragTo(int x, int y) => OnMouseMove(new MouseEventArgs(MouseButtons.Left, 0, x, y, 0));
        public void Up(int x, int y) => OnMouseUp(new MouseEventArgs(MouseButtons.Left, 1, x, y, 0));
    }
    static void TestDrag()
    {
        void Assert(bool ok, string error) { if (!ok) throw new Exception(error); }
        using var host = new Form { ClientSize = new(495, 335) };
        using var p = new TestPlot { Size = new(495, 335), Tracks = [new Track()] };
        host.Controls.Add(p); host.Show(); Application.DoEvents();
        int edits = 0, seeks = 0; p.Edited = _ => edits++; p.Seek = _ => seeks++;
        // Default graph: point at 2 s / 100; middle of first segment at 1 s / 50.
        p.Down(265, 62); p.DragTo(285, 82); p.Up(285, 82);
        Assert(edits == 1 && seeks == 0 && p.Tracks[0].Points[1].Time > 2 && p.Tracks[0].Points[1].Value < 100, "Point drag edits both coordinates without seeking");
        p.Tracks = [new Track()];
        p.Down(165, 155); p.DragTo(195, 185);
        var once = p.Tracks[0].Points.ToArray(); p.DragTo(195, 185); p.Up(195, 185);
        var points = p.Tracks[0].Points;
        Assert(edits == 2 && seeks == 0 && points.SequenceEqual(once), "Segment drag does not accumulate");
        Assert(points[0].Time == 0 && points[1].Time == 2 && points[0].Value < 0 && Math.Abs(points[1].Value - points[0].Value - 100) < 1e-9 && points[2] == new KeyPoint(4, 0), "Segment vertical drag preserves time and slope");
        p.Tracks = [new Track()]; p.Down(265, 62); p.DragTo(265, -200); p.Up(265, -200);
        Assert(p.Tracks[0].Points[1].Value > 120, "Point can exceed original upper axis limit");
        p.Tracks = [new Track()]; p.Down(265, 62); p.DragTo(265, 600); p.Up(265, 600);
        Assert(p.Tracks[0].Points[1].Value < -20, "Point can exceed original lower axis limit");
        p.Tracks = [new Track()]; p.Down(165, 155); p.DragTo(165, -300); p.Up(165, -300);
        Assert(p.Tracks[0].Points[1].Value > 120 && p.Tracks[0].Points[0].Time == 0 && p.Tracks[0].Points[1].Time == 2, "Segment can exceed upper axis limit without changing time");
        p.Tracks = [new Track()]; p.Down(165, 155); p.DragTo(165, 600); p.Up(165, 600);
        Assert(p.Tracks[0].Points[0].Value < -20 && Math.Abs(p.Tracks[0].Points[1].Value - p.Tracks[0].Points[0].Value - 100) < 1e-9, "Segment can exceed lower axis limit without changing slope");
        edits = 2;
        p.Tracks = [new Track()]; p.Down(165, 155); p.DragTo(165, 180); p.Capture = false;
        Assert(p.Tracks[0].Points.SequenceEqual(new Track().Points) && edits == 2, "Capture loss cancels both endpoints");
        p.Down(120, 35); p.DragTo(140, 35); p.Up(140, 35);
        Assert(seeks == 2 && edits == 2, "Empty space still scrubs time");
        p.EditMode = false; p.Tracks = [new Track()];
        p.Down(265, 62); p.DragTo(265, 90); p.Up(265, 90);
        Assert(p.Tracks[0].Points.SequenceEqual(new Track().Points) && edits == 2 && seeks == 4, "Review mode never edits a point");
        host.Close();
    }
}
