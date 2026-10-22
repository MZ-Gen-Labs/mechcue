using System.Text.Json;

namespace MechCue;
public partial class MainForm
{
    internal void VerifyCapturedPhysics(string manifest, string output)
    {
        using var busyRetry = new CadBusyRetry();
        ConnectDocument(); ImportConceptAxes(manifest, "hide");
        RecomputePhysics();
        if (PhysicsStale || bridge.BindingCount != 6 || tracks.Count(t => t.Derived) != 6) throw new Exception("Captured assembly physics tracks were not imported/computed");
        var concept = bridge.CaptureConcept()!;
        if (concept.Physics!.Setup.WorkpieceMassKg != concept.Model.PhysicsDefaults!.WorkpieceMassKg) throw new Exception("Captured CAD mass was not retained");
        var policy = new MotionInspectionPolicy();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var result = bridge.CheckContinuousMotion(bridge.PlanCoarseMotionSamples(0, 4, 1), 0, policy, cancellation: timeout.Token);
        File.WriteAllText(output, result.GetRawText());
        if (!result.GetProperty("allSamplesClear").GetBoolean() || !result.GetProperty("samplingComplete").GetBoolean() || !result.GetProperty("continuousPathCertified").GetBoolean()) throw new Exception("Captured convex path not certified: " + result.GetRawText());
        if (policy.AllowedContacts.Length != 0) throw new Exception("Contact exclusions were unexpectedly required");
        SaveToDocument();
        var saved = DocumentSettings.Parse(bridge.ReadSettings()!);
        if (saved.Concept?.Physics?.Report == null || saved.Concept.Model.Bodies.Any(b => b.Shape != "convex")) throw new Exception("Captured geometry/report did not persist");
        bridge.Disconnect(); documentReady = false; documentSettingsDirty = false;
        ConnectDocument();
        if (restorationWarnings.Count != 0 || bridge.BindingCount != 6 || PhysicsStale || !physicsReportButton.Enabled) throw new Exception("Captured physics settings did not restore");
        using var image = new Bitmap(Width, Height); DrawToBitmap(image, new Rectangle(Point.Empty, Size)); image.Save(Path.ChangeExtension(output, ".png"));
        PausePlayback(); bridge.Disconnect(); documentReady = false; documentSettingsDirty = false;
    }
}
