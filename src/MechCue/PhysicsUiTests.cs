using System.Text.Json;
using MechCue.Physics;

namespace MechCue;

public partial class MainForm
{
    /// <summary>
    /// Physics tracks in the chart window, with a stand-in engine: read-only physics tracks (drag, table, name, MCP),
    /// the status label, the manual button, the automatic recomputation after the quiet period, and results discarded
    /// when the commanded tracks change while the engine runs.
    /// </summary>
    internal void VerifyPhysicsUi()
    {
        void Assert(bool ok, string message) { if (!ok) throw new Exception("Physics UI: " + message); }
        string folder = Path.Combine(Path.GetTempPath(), "mechcue-physics-ui-test-" + Guid.NewGuid());
        Directory.CreateDirectory(folder);
        try
        {
            var model = ConceptMachine.Create("arm5p", 500, 300, 300);
            var doc = new SelfTest.FakeDocument { FullName = Path.Combine(folder, model.AssemblyFile) };
            var app = new SelfTest.FakeApplication { ActiveDocument = doc }; app.OpenDocuments.Items.Add(doc);
            var poses = model.Poses(model.Values);
            foreach (var body in model.Bodies)
            {
                body.File = body.Id + ".par"; body.Occurrence = body.Id;
                doc.Occurrences.Items.Add(new SelfTest.MigrationPart { Name = body.Id, Pose = poses[body.Id],
                    OccurrenceDocument = new SelfTest.InspectionDocument(Path.Combine(folder, body.File)) });
            }
            string manifest = Path.Combine(folder, "mechcue-concept.json");
            File.WriteAllText(manifest, JsonSerializer.Serialize(model, ConceptMachine.JsonOptions));
            using var form = new MainForm(hostedApplication: app); form.Show(); Application.DoEvents();

            int runs = 0; Dictionary<string, List<JointPoint>>? lastJoints = null; ManualResetEventSlim? gate = null;
            form.physicsRunner = (m, setup, joints) => {
                gate?.Wait(TimeSpan.FromSeconds(10));
                Interlocked.Increment(ref runs); lastJoints = joints;
                var result = new RigidBodyResult { Engine = "test", FinalState = "resting" };
                foreach (var axis in PhysicsTracks.FreeBodyAxes(m)!) result.Axes[axis] = [new() { Time = 0, Value = 0 }, new() { Time = setup.EndTime, Value = 1 }];
                result.FingerAxes["F1"] = [new() { Time = 0, Value = 0 }, new() { Time = setup.EndTime, Value = -3 }];
                result.Events.Add(new() { Time = 0.5, Type = "finger-stopped", Detail = "F1 contact" });
                result.Warnings.Add("contact warning");
                return result;
            };
            string Status() => form.physicsStatus.Text;
            string upToDate = form.PatternText("物理：最新", "Physics: up to date"), stale = form.PatternText("物理：要再計算", "Physics: out of date");
            void WaitIdle() { var until = DateTime.UtcNow.AddSeconds(10); while ((form.physicsBusy || form.physicsTimer.Enabled) && DateTime.UtcNow < until) { Application.DoEvents(); Thread.Sleep(10); } }
            var j1 = model.Axes.Single(a => a.Id == "J1"); int edits = 0;
            double NextValue() => (j1.Minimum + j1.Maximum) / 2 + ++edits;
            void Tick() { form.physicsTimer.Stop(); _ = form.RecomputePhysicsInBackground(); }

            form.HandleAi(JsonSerializer.SerializeToElement(new { method = "import_concept", args = new { manifestPath = manifest, unboundTracks = "hide" } }));
            Assert(form.PhysicsAvailable && form.PhysicsStale && Status() == stale && form.physicsStatus.ForeColor == Color.Firebrick, "imported arm5p shows physics: out of date");
            Assert(form.physicsAuto.Checked && form.physicsTimer.Enabled && form.physicsTimer.Interval == 700, "auto recomputation waits for the quiet period");
            form.physicsTimer.Stop();

            // Physics tracks are read-only everywhere.
            var derived = form.tracks.Single(t => t.Name == "WZ"); int derivedIndex = form.tracks.IndexOf(derived);
            Assert(derived.Derived && !form.plot.EditStarting!(derivedIndex), "dragging a physics track is refused");
            form.trackList.SelectedIndex = derivedIndex; Application.DoEvents();
            Assert(form.grid.ReadOnly && form.name.ReadOnly, "table and name of a physics track are read-only");
            bool rejected = false;
            try { form.HandleAi(JsonSerializer.SerializeToElement(new { method = "set_keyframe", args = new { trackId = derived.Id.ToString(), time = 1, value = 5 } })); } catch (InvalidOperationException) { rejected = true; }
            Assert(rejected, "MCP cannot edit a physics track");
            var commanded = form.tracks.Single(t => t.Name == "J1"); form.trackList.SelectedIndex = form.tracks.IndexOf(commanded); Application.DoEvents();
            Assert(!form.grid.ReadOnly && !form.name.ReadOnly, "commanded tracks stay editable");

            // Manual button: only commanded tracks are inputs; the result lands in the physics tracks.
            form.physicsButton.PerformClick(); Application.DoEvents();
            Assert(runs == 1 && lastJoints != null && lastJoints.ContainsKey("J1") && !lastJoints.ContainsKey("WZ"), "button runs the engine with commanded tracks only");
            Assert(!form.PhysicsStale && Status() == upToDate && derived.Points[^1].Value == 1, "button applies the result and shows up to date");
            var finger = form.tracks.Single(t => t.Name == "F1");
            Assert(finger.At(4) == -3 && finger.Points.All(p => p.Value == 0), "playback uses actual finger travel without overwriting commands");
            form.bridge.ApplyConcept(4);
            var fingerPart = doc.Occurrences.Items.Cast<SelfTest.MigrationPart>().Single(p => p.Name == "finger-1");
            var actualValues = model.Axes.ToDictionary(a => a.Id, a => form.tracks.Single(t => t.Name == a.Id).At(4));
            Assert(fingerPart.Pose.Zip(model.Poses(actualValues)["finger-1"]).All(p => Math.Abs(p.First - p.Second) < 1e-8), "CAD occurrence receives the actual finger pose");
            Assert(form.physicsReportButton.Enabled && form.PhysicsReportText().Contains("F1 contact") && form.PhysicsReportText().Contains("contact warning"), "report exposes events and warnings");
            var state = JsonSerializer.SerializeToElement(form.PhysicsState());
            Assert(state.GetProperty("report").GetProperty("Events").GetArrayLength() == 1 && state.GetProperty("fingerAxisKeyframes").GetProperty("F1").GetArrayLength() == 2, "MCP state exposes report and actual travel");

            // Editing a commanded keyframe makes it stale and schedules the automatic run.
            form.HandleAi(JsonSerializer.SerializeToElement(new { method = "set_keyframe", args = new { trackId = commanded.Id.ToString(), time = 1, value = NextValue() } }));
            Assert(form.PhysicsStale && Status() == stale && form.physicsTimer.Enabled, "edit schedules recomputation");
            Assert(ReferenceEquals(finger.PlaybackPoints,finger.Points) && form.PhysicsReportText().Contains(form.PatternText("要再計算", "Out of date")), "editing another axis invalidates actual fingers and labels the old report");
            form.HandleAi(JsonSerializer.SerializeToElement(new { method = "undo", args = new {} }));
            Assert(!form.PhysicsStale && finger.At(4)==-3, "undo to the computed inputs restores actual finger playback without losing the cached result");
            form.HandleAi(JsonSerializer.SerializeToElement(new { method = "set_keyframe", args = new { trackId = commanded.Id.ToString(), time = 1, value = NextValue() } }));
            Tick(); WaitIdle();
            Assert(runs == 2 && !form.PhysicsStale && Status() == upToDate, "automatic run brings physics up to date");

            // A result computed from inputs edited meanwhile is discarded and the run is scheduled again.
            gate = new ManualResetEventSlim(false);
            form.HandleAi(JsonSerializer.SerializeToElement(new { method = "set_keyframe", args = new { trackId = commanded.Id.ToString(), time = 1, value = NextValue() } }));
            Tick(); Application.DoEvents();
            Assert(form.physicsBusy && Status() == form.PatternText("物理：計算中…", "Physics: computing…"), "busy state shown while computing");
            form.HandleAi(JsonSerializer.SerializeToElement(new { method = "set_keyframe", args = new { trackId = commanded.Id.ToString(), time = 1, value = NextValue() } }));
            gate.Set();
            var until = DateTime.UtcNow.AddSeconds(10); while (form.physicsBusy && DateTime.UtcNow < until) { Application.DoEvents(); Thread.Sleep(10); }
            Assert(runs == 3 && form.PhysicsStale && form.physicsTimer.Enabled, "stale result is discarded and rescheduled");
            gate = null; Tick(); WaitIdle();
            Assert(runs == 4 && !form.PhysicsStale, "rescheduled run applies");

            // Auto off: edits only mark the result out of date; the setting is saved with the concept.
            form.physicsAuto.Checked = false; Application.DoEvents();
            Assert(form.bridge.CaptureConcept()!.Physics!.Auto == false, "auto setting is stored");
            form.HandleAi(JsonSerializer.SerializeToElement(new { method = "set_keyframe", args = new { trackId = commanded.Id.ToString(), time = 1, value = NextValue() } }));
            Assert(form.PhysicsStale && Status() == stale && !form.physicsTimer.Enabled, "auto off does not schedule");
            form.Close();
        }
        finally { Directory.Delete(folder, true); }
    }
}
