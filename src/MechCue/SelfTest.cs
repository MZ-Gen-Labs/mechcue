namespace MechCue;
public static partial class SelfTest
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
        TestConceptMachines();
        TestDrawingPlanning();
        TestDrag();
        TestTimelineEditing();
        TestOverlay();
        TestConnectionRecovery();
        TestReassignment();
        TestCoordinateAndCollision();
        TestNativeInterferenceArray();
        TestInspectionResults();
        TestDocumentPersistence();
        TestChartTables();
        TestVideoExport();
        TestWorkflowExtensions();
        TestWindowPlacement();
        TestWindowPlacementUi();
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
        public void RemoveAll() => Items.Clear();
        public object Item(int index) => Items[index - 1];
    }
    public class FakeDocument
    {
        public string Name => "Test.asm";
        public string FullName { get; set; } = "C:/Test.asm";
        public int Type => 3;
        public void Save() { Dirty = false; }
        public bool ReadOnly { get; set; }
        public bool Dirty { get; set; }
        public string StoragePath { get; } = Path.Combine(AppContext.BaseDirectory, "fake-document-" + Guid.NewGuid() + ".storage");
        [System.Runtime.CompilerServices.IndexerName("AddInsStorage")]
        public object this[string name, int mode] => StorageTestFactory.Open(StoragePath, mode);
        public void BindKeyToObject(ref byte[] key, out object value)
        {
            var sought = key;
            value = Occurrences.Items.OfType<FakePart>().Single(p => p.PersistentId.ToByteArray().SequenceEqual(sought));
        }
        public FakeCollection Occurrences { get; } = new();
        public FakeCollection Relations3d { get; } = new();
        public FakeCollection SelectSet { get; } = new();
    }
    [System.Runtime.InteropServices.ComVisible(true)]
    [System.Runtime.InteropServices.ClassInterface(System.Runtime.InteropServices.ClassInterfaceType.AutoDispatch)]
    public class FakePart
    {
        public string Name { get; set; } = "Part";
        public int Type => 54;
        public Guid PersistentId = Guid.NewGuid();
        public void GetReferenceKey(ref byte[] key, ref object size) { key = PersistentId.ToByteArray(); size = key.Length; }
        public FakeCollection Relations3d { get; } = new();
        public double[] Pose = [1,0,0,0, 0,1,0,0, 0,0,1,0, 0.5,0.6,0.7,1];
        public void GetMatrix(ref double[] matrix) => matrix = (double[])Pose.Clone();
        public void PutMatrix(double[] matrix, bool replace) => Pose = (double[])matrix.Clone();
    }
    public class FakeGround { public FakeAttributes AttributeSets { get; } = new(); public int Type => 1959028688; public bool Suppress { get; set; } }
    public class FakeOtherConstraint { public int Type => 123; public bool Suppress { get; set; } }
    public class FakeCollisionDocument : FakeDocument
    {
        public int ForcedStatus;
        public bool FailCheck;
        public int ChecksUntilFailure;
        public void CheckInterference(int count, ref Array parts, ref int status,
            [System.Runtime.InteropServices.Optional] object? comparison, [System.Runtime.InteropServices.Optional] object? count2, [System.Runtime.InteropServices.Optional] object? set2,
            [System.Runtime.InteropServices.Optional] object? addOccurrence, [System.Runtime.InteropServices.Optional] object? report, [System.Runtime.InteropServices.Optional] object? reportType,
            [System.Runtime.InteropServices.Optional] ref object number,
            [System.Runtime.InteropServices.Optional] ref object first,
            [System.Runtime.InteropServices.Optional] ref object second,
            [System.Runtime.InteropServices.Optional] ref object confirmed,
            [System.Runtime.InteropServices.Optional] ref object occurrence,
            object? ignoreThreads = null)
        {
            if (parts.GetLowerBound(0) != 1 || parts.GetValue(1) is not System.Runtime.InteropServices.DispatchWrapper) throw new Exception("Wrong automation array type");
            var input = parts;
            object Part(int index) => ((System.Runtime.InteropServices.DispatchWrapper)input.GetValue(index)!).WrappedObject!;
            if (ChecksUntilFailure > 0 && --ChecksUntilFailure == 0) throw new InvalidOperationException("Fake post-update API failure");
            if (FailCheck) throw new InvalidOperationException("Fake API failure");
            if (addOccurrence is not false) throw new Exception("Interference geometry creation must be disabled");
            status = ForcedStatus != 0 ? ForcedStatus : ((FakePart)Part(1)).Pose[12] > 0.55 ? 2 : 1;
            number = status == 1 ? 0 : 1;
            first = new object[] { Part(1) }; second = new object[] { Part(count) }; confirmed = new bool[] { status == 2 };
        }
    }
    public class FakeRelation { public int Type => 55; public FakeAttributes AttributeSets { get; set; } = new(); public double Offset { get; set; } public double Angle { get; set; } }
    public class FakeApplication
    {
        public bool Dead;
        public FakeCollection OpenDocuments = new();
        public FakeCollection Documents => Dead ? throw new System.Runtime.InteropServices.COMException("Disconnected", unchecked((int)0x80010108)) : OpenDocuments;
        public object? ActiveDocument { get; set; }
        public FakeWindow ActiveWindow { get; } = new();
    }
    public class FakeWindow { public FakeView View { get; } = new(); }
    public class FakeView {
        public Action? Capture;
        public void Update() { }
        public void SaveAsImage(string path,object width,object height,[System.Runtime.InteropServices.Optional] object? style,object resolution,object depth,object quality,object invert) {
            Capture?.Invoke();using var image=new Bitmap(Convert.ToInt32(width),Convert.ToInt32(height));
            using(var g=Graphics.FromImage(image))g.Clear(Color.CornflowerBlue);image.Save(path,System.Drawing.Imaging.ImageFormat.Jpeg);
        }
    }
    static void TestCoordinateAndCollision()
    {
        void Assert(bool ok, string message) { if (!ok) throw new Exception(message); }
        var doc = new FakeCollisionDocument(); var app = new FakeApplication { ActiveDocument = doc }; app.OpenDocuments.Items.Add(doc);
        var bridge = new Bridge(app, doc); var part = new FakePart { Name = "Moving" }; var ground = new FakeGround();
        part.Relations3d.Items.Add(ground); doc.Occurrences.Items.AddRange([part, new FakePart { Name = "Obstacle" }]);
        var target = new Target("Moving", part, "Matrix");
        var track = new Track { Kind = "部品座標", Axis = "X", Points = [new(0, 500), new(1, 600)] };
        Assert(bridge.CurrentValue(track, target) == 500, "Absolute current coordinate uses mm");
        bridge.Bind(track, target); Assert(ground.Suppress, "Ground suppressed during registered direct drive");
        bridge.ApplyChecked(0.25); Assert(Math.Abs(part.Pose[12] - 0.525) < 1e-10 && part.Pose[13] == 0.6, "Checked absolute drive preserves other axes");
        try { bridge.ApplyChecked(1); throw new Exception("Collision accepted"); } catch (InvalidOperationException) { }
        Assert(Math.Abs(part.Pose[12] - 0.525) < 1e-10, "Collision restores last checked pose");
        doc.ForcedStatus = 5;
        try { bridge.ApplyChecked(0); throw new Exception("Incomplete analysis accepted"); } catch (InvalidOperationException) { }
        Assert(Math.Abs(part.Pose[12] - 0.525) < 1e-10, "Incomplete check cannot move CAD");
        doc.ForcedStatus = 3;
        try { bridge.ApplyChecked(0); throw new Exception("Probable interference accepted"); } catch (InvalidOperationException) { }
        doc.ForcedStatus = 0; doc.FailCheck = true;
        try { bridge.ApplyChecked(0); throw new Exception("Check failure accepted"); } catch (System.Reflection.TargetInvocationException) { }
        doc.FailCheck = false; doc.ChecksUntilFailure = 2;
        try { bridge.ApplyChecked(0); throw new Exception("Post-update failure accepted"); } catch (System.Reflection.TargetInvocationException) { }
        Assert(Math.Abs(part.Pose[12] - 0.525) < 1e-10, "Post-update API failure rolls back pose");
        bridge.Unbind(track); Assert(!ground.Suppress && part.Pose[12] == 0.5, "Unbind restores original pose and ground");
        part.Relations3d.Items.Clear(); part.Relations3d.Items.Add(new FakeOtherConstraint());
        try { bridge.Bind(track, target); throw new Exception("Non-ground constraint accepted"); } catch (InvalidOperationException) { }
        var otherConstraint = (FakeOtherConstraint)part.Relations3d.Items[0];
        part.Relations3d.Items.Add(ground);
        bridge.Bind(track, target);
        Assert(ground.Suppress && !otherConstraint.Suppress, "Coordinate registration suppresses only ground, retaining other constraints");
        bridge.Apply(0);
        Assert(part.Pose[12] == 0.5 && !otherConstraint.Suppress, "Coordinate drive retains other constraints");
        bridge.Unbind(track);
        Assert(!ground.Suppress && !otherConstraint.Suppress, "Unbind restores ground without changing other constraints");
        otherConstraint.Suppress = true;
        bridge.Bind(track, target); bridge.Apply(1);
        Assert(otherConstraint.Suppress && bridge.Disconnect() == null && !ground.Suppress && otherConstraint.Suppress, "Disconnect retains initially suppressed other constraint");
        bridge = new Bridge(app, doc);
        part.Relations3d.Items.Clear();
        foreach (var axis in new[] { "X", "Y", "Z" })
        {
            track.Axis = axis;
            bridge.Bind(track, target); bridge.Apply(0);
            int index = axis == "X" ? 12 : axis == "Y" ? 13 : 14;
            Assert(part.Pose[index] == 0.5 && bridge.CurrentValue(track, target) == 500, "Absolute axis and readback " + axis);
            bridge.Unbind(track);
        }
        track.Kind = "部品移動"; track.Axis = "X"; track.Points = [new(0, 20), new(1, 20)];
        bridge.Bind(track, target); bridge.Apply(0);
        Assert(Math.Abs(bridge.CurrentValue(track, target) - 20) < 1e-8, "Registered relative translation current value");
        bridge.Unbind(track);
        track.Kind = "部品回転"; track.Axis = "Z"; track.Points = [new(0, 45), new(1, 45)];
        bridge.Bind(track, target); bridge.Apply(0);
        Assert(Math.Abs(bridge.CurrentValue(track, target) - 45) < 1e-8, "Registered relative rotation current value");
        bridge.Unbind(track);
        var distance = new FakeRelation { Offset = 0.0125 };
        Assert(bridge.CurrentValue(new Track(), new Target("Distance", distance, "Offset")) == 12.5, "Distance current units");
        Assert(Math.Abs(bridge.CurrentValue(new Track { Kind = "角度拘束" }, new Target("Angle", new FakeRelation { Angle = Math.PI / 4 }, "Angle")) - 45) < 1e-8, "Angle current units");
        part.Relations3d.Items.Add(ground); track.Kind = "部品座標";
        bridge.Bind(track, target); bridge.Apply(0);
        Assert(bridge.Disconnect() == null && !ground.Suppress && part.Pose[12] == 0.5, "Disconnect restores grounding");
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "coordinate-collision-test-result.txt"), "PASS: XYZ absolute/readback, relative readback, ground restoration, grounded coordinate drive with other constraints preserved, ungrounded constraint rejection, confirmed/probable interference, incomplete/API failure stop, collision rollback");
    }
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
        public Keys TestModifiers;
        protected override Keys DragModifiers => TestModifiers;
        public void Down(int x, int y) => OnMouseDown(new MouseEventArgs(MouseButtons.Left, 1, x, y, 0));
        public void DragTo(int x, int y) => OnMouseMove(new MouseEventArgs(MouseButtons.Left, 0, x, y, 0));
        public void Up(int x, int y) => OnMouseUp(new MouseEventArgs(MouseButtons.Left, 1, x, y, 0));
    }
    [System.Runtime.InteropServices.DllImport("oleaut32.dll")] static extern int VariantClear(IntPtr variant);
    [System.Runtime.InteropServices.DllImport("oleaut32.dll")] static extern int SafeArrayGetVartype(IntPtr array, out ushort type);
    static void TestNativeInterferenceArray()
    {
        var variant = System.Runtime.InteropServices.Marshal.AllocCoTaskMem(24);
        for (int i = 0; i < 24; i++) System.Runtime.InteropServices.Marshal.WriteByte(variant, i, 0);
        try
        {
            System.Runtime.InteropServices.Marshal.GetNativeVariantForObject(Bridge.InterferenceSet([new FakePart()]), variant);
            int type = System.Runtime.InteropServices.Marshal.ReadInt16(variant);
            var array = System.Runtime.InteropServices.Marshal.ReadIntPtr(variant, 8);
            if (type != 0x2009 || SafeArrayGetVartype(array, out var element) != 0 || element != 9)
                throw new Exception($"Expected SAFEARRAY(IDispatch), got variant {type:X4}");
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "native-interference-test-result.txt"), "PASS: native VARIANT is VT_ARRAY|VT_DISPATCH, SAFEARRAY element type IDispatch");
        }
        finally { VariantClear(variant); System.Runtime.InteropServices.Marshal.FreeCoTaskMem(variant); }
    }
    static void TestOverlay()
    {
        void Assert(bool ok, string message) { if (!ok) throw new Exception(message); }
        using var host = new Form { ClientSize = new(495, 335) };
        using var p = new TestPlot { Size = new(495, 335), Overlay = true, Tracks = [new Track(), new Track(), new Track { Kind = "角度拘束", Points = [new(0, 0), new(2, 9000), new(4, 0)] }] };
        host.Controls.Add(p); host.Show(); Application.DoEvents();
        var original = p.Tracks[1].Points.ToArray();
        var point = p.PointLocation(0, 1); var anglePoint = p.PointLocation(2, 1);
        Assert(Math.Abs(point.Y - anglePoint.Y) < 1, "Mixed units have independent axes");
        p.Down((int)point.X, (int)point.Y); p.DragTo((int)point.X, (int)point.Y + 20); p.Up((int)point.X, (int)point.Y + 20);
        Assert(p.Tracks[0].Points[1].Value < 100 && p.Tracks[1].Points.SequenceEqual(original), "Overlapping curves edit selected track only");
        p.Selected = 1; p.Hidden.Add(p.Tracks[1]);
        point = p.PointLocation(1, 1); p.Down((int)point.X, (int)point.Y); p.DragTo((int)point.X, (int)point.Y + 20); p.Up((int)point.X, (int)point.Y + 20);
        Assert(p.Tracks[1].Points.SequenceEqual(original), "Hidden selection cannot be edited");
        p.Hidden.Clear(); p.Selected = 0;
        p.Tracks[1].Points = [new(0, 0), new(2, 20000), new(4, 0)];
        float compressed = p.PointLocation(0, 1).Y;
        p.Hidden.Add(p.Tracks[1]);
        Assert(Math.Abs(p.PointLocation(0, 1).Y - compressed) > 30, "Hidden tracks excluded from shared axis range");
        var before = p.PointLocation(0, 1); p.Tracks.AddRange(Enumerable.Range(0, 30).Select(i => new Track { Kind = "角度拘束" }));
        Assert(p.PointLocation(0, 1) == before, "Overlay height independent of variable count");
        p.Overlay = false;
        Assert(p.PointLocation(0, 1).Y != before.Y, "Individual view remains available");
        host.Close();
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "overlay-test-result.txt"), "PASS: independent mm/degree axes, selected-only overlapping drag, hidden editing rejection, visible-only scale, fixed plot height, individual view");
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
        Assert(edits == 1 && seeks == 0 && p.Tracks[0].Points[1].Time == 2 && p.Tracks[0].Points[1].Value < 100, "Default point drag keeps time unchanged");
        p.Tracks = [new Track()];
        p.TestModifiers = Keys.Control;
        p.Down(265, 62); p.DragTo(285, 82); p.Up(285, 82);
        Assert(p.Tracks[0].Points[1].Time > 2 && p.Tracks[0].Points[1].Value < 100, "Ctrl point drag edits both coordinates");
        edits = 1; p.TestModifiers = Keys.None;
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
        p.EditMode = true; p.TestModifiers = Keys.None;
        p.Tracks = [new Track { Points = [new(0, 215.9), new(2, 215.9), new(4, 215.9)] }];
        var position = p.PointLocation(0, 1);
        p.Down((int)position.X, (int)position.Y); p.DragTo((int)position.X + 30, (int)position.Y - 30); p.Up((int)position.X + 30, (int)position.Y - 30);
        Assert(p.Tracks[0].Points[1].Value > 219 && p.Tracks[0].Points[1].Time == 2, "Constant graph has useful vertical editing range");
        p.Tracks = [new Track()]; p.ValueStep = 0.1;
        position = p.PointLocation(0,1);
        p.Down((int)position.X, (int)position.Y); p.DragTo((int)position.X, (int)position.Y+23); p.Up((int)position.X, (int)position.Y+23);
        Assert(Math.Abs(p.Tracks[0].Points[1].Value*10-Math.Round(p.Tracks[0].Points[1].Value*10)) < 1e-8, "Point respects 0.1 drag step");
        p.Tracks = [new Track { Points = [new(0,0),new(1,50),new(3,50),new(4,0)] }];
        p.TestModifiers = Keys.Control;
        var a = p.PointLocation(0,1); var b = p.PointLocation(0,2);
        p.Down((int)((a.X+b.X)/2), (int)a.Y); p.DragTo((int)((a.X+b.X)/2)+20, (int)a.Y-20); p.Up((int)((a.X+b.X)/2)+20, (int)a.Y-20);
        Assert(p.Tracks[0].Points[1].Time > 1 && Math.Abs(p.Tracks[0].Points[2].Time-p.Tracks[0].Points[1].Time-2) < 1e-9 && p.Tracks[0].Points[1].Value > 50, "Ctrl segment translates time and value preserving duration");
        Assert(Math.Abs(p.Tracks[0].Points[1].Value*10-Math.Round(p.Tracks[0].Points[1].Value*10)) < 1e-8, "Segment respects drag step");
        host.Close();
    }
}
