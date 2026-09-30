using System.Runtime.InteropServices;

namespace MechCue;
public sealed partial class Bridge
{
    internal static void VerifyDocumentPersistenceInSolidEdge()
    {
        Marshal.ThrowExceptionForHR(CLSIDFromProgID("SolidEdge.Application", out var id));
        GetActiveObject(ref id, IntPtr.Zero, out object application);
        object? previous = null; try { previous = Get(application, "ActiveDocument"); } catch { }
        var documents = Get(application, "Documents");
        string directory = Path.Combine(AppContext.BaseDirectory, "persistence-live-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(directory);
        var results = new List<string>();
        object? testDocument = null; Bridge? testBridge = null;
        void Assert(bool result, string message) { if (!result) throw new Exception(message); results.Add("PASS: " + message); }
        T WaitForRead<T>(Func<T> read)
        {
            var wait = System.Diagnostics.Stopwatch.StartNew();
            while (true)
            {
                try { return read(); }
                catch (Exception ex) when ((ex.InnerException ?? ex).HResult is unchecked((int)0x80010001) or unchecked((int)0x8001010A) && wait.ElapsedMilliseconds < 5000) { Application.DoEvents(); Thread.Sleep(50); }
            }
        }
        try
        {
            string training = @"C:\Siemens\Solid Edge 2026\Training\Motion\Dynamic Motion\Fourbar";
            if (!Directory.Exists(training)) throw new DirectoryNotFoundException("Installed Siemens Fourbar training sample was not found.");
            // Test only private copies. Never save or modify the user's originally open document.
            foreach (var file in Directory.GetFiles(training)) File.Copy(file, Path.Combine(directory, Path.GetFileName(file)));
            string assemblyPath = Path.Combine(directory, "fourbar.asm");
            testDocument = Call(documents, "Open", assemblyPath);
            testBridge = new Bridge(application); testBridge.Connect();
            Assert(DocumentStorage.Read(testDocument) == null, "Clean sample has no MechCue settings");
            var targets = testBridge.Targets("距離拘束");
            var driver = targets.First(t => Math.Abs(Convert.ToDouble(Get(t.Com, t.Property))) > .0001);
            double value = testBridge.CurrentValue(new Track(), driver);
            var track = new Track { Name = "Persisted distance", Points = [new(0, value), new(1,value + 1)] };
            testBridge.Bind(track, driver);
            var settings = new DocumentSettings { Speed = 2, Loop = true, Collision = true, Tracks = [new() { Track = track, Target = testBridge.CaptureTarget(track) }] };
            var angularTarget = testBridge.Targets("角度拘束").FirstOrDefault(t => !Equals(t.Com, driver.Com));
            if (angularTarget != null)
            {
                double angle = testBridge.CurrentValue(new Track { Kind = "角度拘束" }, angularTarget);
                var angleTrack = new Track { Name = "Persisted angle", Kind = "角度拘束", Points = [new(0, angle), new(1, angle + (angle > 90 ? -1 : 1))] };
                testBridge.Bind(angleTrack, angularTarget);
                settings.Tracks.Add(new SavedTrack { Track = angleTrack, Target = testBridge.CaptureTarget(angleTrack) });
            }
            int saves = 0;
            Assert(testBridge.AttachDocumentEvents(() => { testBridge.WriteSettings(settings.Json()); saves++; }), "BeforeSave event attached");
            testBridge.WriteSettings(settings.Json());
            Set(testDocument, "Dirty", true);
            testBridge.SaveDocument(); Application.DoEvents();
            results.Add(saves > 0 ? "PASS: Native BeforeSave callback received" : "NOTE: API Save did not raise BeforeSave; explicit save verified. UI Ctrl+S needs manual verification.");
            Assert(DocumentSettings.Parse(testBridge.ReadSettings()!).Speed == 2, "Native AddInsStorage JSON read/write");
            int beforeCommandSaves = saves;
            settings.Speed = 3;
            Set(testDocument, "Dirty", true);
            Call(application, "StartCommand", 57603); // AssemblyFileSave from Solid Edge 2026 constants.
            var limit = System.Diagnostics.Stopwatch.StartNew();
            while (saves == beforeCommandSaves && limit.ElapsedMilliseconds < 3000) { Application.DoEvents(); Thread.Sleep(25); }
            Assert(saves > beforeCommandSaves, "Solid Edge Save command invokes BeforeDocumentSave");
            Assert(DocumentSettings.Parse(WaitForRead(() => testBridge.ReadSettings())!).Speed == 3, "Save command captures updated settings");
            testBridge.Disconnect(); testBridge = null;
            Call(testDocument, "Close", false); testDocument = null;
            testDocument = Call(documents, "Open", assemblyPath);
            testBridge = new Bridge(application); testBridge.Connect();
            var restored = DocumentSettings.Parse(testBridge.ReadSettings()!);
            foreach (var entry in restored.Tracks) Assert(testBridge.RestoreTarget(entry.Track, entry.Target!) == null, "Constraint ID restored after close/reopen: " + entry.Track.Name);
            testBridge.Apply(1);
            var newDriver = testBridge.Targets("距離拘束").Single(t => AttributeId(t.Com) == restored.Tracks[0].Target!.AttributeId);
            Assert(Math.Abs(testBridge.CurrentValue(track, newDriver) - value - 1) < 1e-6, "Restored constraint drives +1 mm");
            if (restored.Tracks.Count > 1)
            {
                var entry = restored.Tracks[1];
                var angleDriver = testBridge.Targets("角度拘束").Single(t => AttributeId(t.Com) == entry.Target!.AttributeId);
                Assert(Math.Abs(testBridge.CurrentValue(entry.Track, angleDriver) - entry.Track.Points[1].Value) < 1e-6, "Restored angle drives one degree");
            }
            testBridge.Restore();
            string copyPath = Path.Combine(directory, "fourbar-copy.asm");
            Call(testDocument, "SaveCopyAs", copyPath);
            testBridge.Disconnect(); testBridge = null; Call(testDocument, "Close", false); testDocument = null;
            testDocument = Call(documents, "Open", copyPath); testBridge = new Bridge(application); testBridge.Connect();
            restored = DocumentSettings.Parse(testBridge.ReadSettings()!);
            Assert(testBridge.RestoreTarget(restored.Tracks[0].Track, restored.Tracks[0].Target!) == null, "SaveCopyAs preserves data and constraint IDs");
            testBridge.Disconnect(); testBridge = null; Call(testDocument, "Close", false); testDocument = null;

            testDocument = Call(documents, "Add", "SolidEdge.AssemblyDocument");
            var part = Call(Get(testDocument, "Occurrences"), "AddByFilename", Path.Combine(directory, "link.par"));
            string partAssemblyPath = Path.Combine(directory, "grounded-test.asm");
            Call(testDocument, "SaveAs", partAssemblyPath);
            testBridge = new Bridge(application); testBridge.Connect();
            var coordinate = new Track { Name = "Persistent part", Kind = "部品座標", Axis = "X" };
            var partTarget = testBridge.Targets(coordinate.Kind).Single();
            testBridge.Highlight(partTarget);
            Assert(Convert.ToInt32(Get(Get(testDocument,"SelectSet"),"Count")) == 1,"Native CAD part selection created");
            Assert(testBridge.TargetsFromSelection(coordinate.Kind).Count == 1,"Candidate lookup retains native selection");
            testBridge.ClearSelection();
            Assert(Convert.ToInt32(Get(Get(testDocument,"SelectSet"),"Count")) == 0,"Native CAD selection cleared");
            double x = testBridge.CurrentValue(coordinate, partTarget);
            coordinate.Points = [new(0,x),new(1,x + 10)];
            var before = Matrix(part); testBridge.Bind(coordinate, partTarget);
            settings = new DocumentSettings { Tracks = [new() { Track = coordinate, Target = testBridge.CaptureTarget(coordinate) }] };
            Set(part, "Name", "MechCueRenamedPart:1");
            testBridge.WriteSettings(settings.Json()); testBridge.SaveDocument();
            testBridge.Disconnect(); testBridge = null;
            Call(testDocument, "Save"); Call(testDocument, "Close", false); testDocument = null;
            testDocument = Call(documents, "Open", partAssemblyPath); testBridge = new Bridge(application); testBridge.Connect();
            restored = DocumentSettings.Parse(testBridge.ReadSettings()!);
            var restoredPart = GetItem(Get(testDocument, "Occurrences"), 1);
            var beforeRestore = Matrix(restoredPart);
            var restoreError = testBridge.RestoreTarget(restored.Tracks[0].Track, restored.Tracks[0].Target!);
            Assert(restoreError == null, "Part reference key and ground IDs restored after reopen: " + restoreError);
            Assert(Matrix(restoredPart).SequenceEqual(beforeRestore), "Restoring assignment leaves part pose unchanged");
            testBridge.Apply(1);
            Assert(Math.Abs(Matrix(restoredPart)[12] - before[12] - .01) < 1e-8, "Restored absolute coordinate drives +10 mm");
            testBridge.Disconnect(); testBridge = null;
            Assert(Matrix(restoredPart).Zip(before).All(p => Math.Abs(p.First-p.Second) < 1e-8), "Saved baseline restored on disconnect");
            Assert(!Convert.ToBoolean(Get(GetItem(Get(restoredPart, "Relations3d"),1),"Suppress")), "Ground constraint restored on disconnect");
            Call(testDocument, "Close", false); testDocument = null;
            File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "persistence-integration-result.txt"), results.Append("Test files: " + directory));
        }
        catch (Exception ex)
        {
            File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "persistence-integration-result.txt"), results.Append(ex.ToString()).Append("Test files: " + directory));
            throw;
        }
        finally
        {
            testBridge?.Disconnect();
            if (testDocument != null) try { Call(testDocument, "Close", false); } catch { }
            if (previous != null) try { Call(previous, "Activate"); } catch { }
        }
    }
}
