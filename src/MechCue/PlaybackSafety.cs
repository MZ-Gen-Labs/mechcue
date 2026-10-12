using System.Text.Json;

namespace MechCue;

public partial class MainForm
{
    bool playbackTickActive;
    CancellationTokenSource? mcpInspectionCancellation;

    void PlaybackTick()
    {
        // CAD inspection time must not become a larger catch-up interval on the
        // next frame. Otherwise repeated refinement can grow without bound.
        bool checkedPlayback = collision.Checked;
        playbackTickActive = true;
        try {
            Guard(() => {
                double end = tracks.Max(t => t.Points[^1].Time);
                double next = playStart + watch.Elapsed.TotalSeconds * (double)speed.Value;
                bool checkedLoopEnd = next >= end && loop.Checked && checkedPlayback;
                if (next >= end) {
                    if (loop.Checked && end > 0 && !checkedPlayback) next %= end;
                    else { next = end; PausePlayback(); }
                }
                plot.FollowTime(next);
                time.Value = (decimal)next;
                if (checkedPlayback && timer.Enabled) { playStart = next; watch.Restart(); }
                if (checkedLoopEnd) status.Text = UiText.IsJapanese
                    ? "検証再生は終端で停止しました。終端から始点へのループ移動は未検証です。"
                    : "Checked playback stopped at the end; loop reset transition is unverified.";
            });
        }
        finally { playbackTickActive = false; }
    }

    object SetPlaybackOptions(JsonElement args)
    {
        bool? apply = args.TryGetProperty("applyToCad", out var a) && a.ValueKind != JsonValueKind.Null ? a.GetBoolean() : null;
        bool? interfere = args.TryGetProperty("stopOnInterference", out var c) && c.ValueKind != JsonValueKind.Null ? c.GetBoolean() : null;
        bool? subdivide = args.TryGetProperty("subdivideInspection", out var s) && s.ValueKind != JsonValueKind.Null ? s.GetBoolean() : null;
        if (apply == null && interfere == null && subdivide == null) throw new ArgumentException("Specify applyToCad, stopOnInterference or subdivideInspection");
        string expected = args.GetProperty("expectedDocument").GetString()!;
        if (!bridge.Connected || !string.Equals(bridge.Document.GetType().InvokeMember("FullName", System.Reflection.BindingFlags.GetProperty, null, bridge.Document, null)?.ToString(), expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Connected document differs from expectedDocument");
        if (reviewMode.Checked) throw new InvalidOperationException("Creation mode required");
        PausePlayback();
        // Enabling Apply is a selection, not an implicit seek or CAD mutation.
        using (PauseCadReflection()) {
            if (apply is bool enabled) live.Checked = enabled;
            if (interfere is bool enabledCollision) collision.Checked = enabledCollision;
            if (subdivide is bool enabledSubdivision) subdivideInspection.Checked = enabledSubdivision;
        }
        StoreActivePattern(); MarkDocumentSettingsChanged();
        return AiState();
    }

    async Task<object> RunAiMotionInspectionAsync(JsonElement request, CancellationToken cancellation)
    {
        var args = request.GetProperty("args");
        if (motionInspectionBusy || videoExport?.Running == true) throw new InvalidOperationException("Another CAD inspection/export is running");
        double start = args.GetProperty("startTime").GetDouble(), end = args.GetProperty("endTime").GetDouble(), step = args.GetProperty("step").GetDouble();
        bool adaptive = args.TryGetProperty("adaptive", out var ad) && ad.GetBoolean();
        double linear = args.TryGetProperty("maxLinearStepMm", out var l) ? l.GetDouble() : 5;
        double angular = args.TryGetProperty("maxAngularStepDeg", out var ang) ? ang.GetDouble() : 1;
        int limit = args.TryGetProperty("maxSamples", out var max) ? max.GetInt32() : 5001;
        bool surface = !args.TryGetProperty("surfaceBased", out var sf) || sf.GetBoolean();
        double surfaceStep = args.TryGetProperty("maxSurfaceStepMm", out var ss) ? ss.GetDouble() : 5;
        double clearance = args.TryGetProperty("requiredClearanceMm", out var gap) ? gap.GetDouble() : 0;
        bool stop = args.TryGetProperty("stopOnInterference", out var st) && st.GetBoolean();
        var policy = args.TryGetProperty("inspectionPolicy", out var ip) ? ip.Deserialize<MotionInspectionPolicy>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })! : bridge.InspectionPolicy;
        if (!double.IsFinite(clearance) || clearance < 0 || clearance > 10000) throw new ArgumentException("Clearance must be 0..10000 mm");
        if (!double.IsFinite(start) || !double.IsFinite(end) || !double.IsFinite(step) || start < 0 || end <= start || end > tracks.Max(t => t.Points[^1].Time) || step <= 0) throw new ArgumentException("Use a finite positive chart interval and step");
        policy.Validate(); if (limit is < 2 or > 10001) throw new ArgumentException("maxSamples must be 2..10001 per batch");
        using var reflection = PauseCadReflection();
        PausePlayback(); Commit(); bridge.ValidateMotionInspectionPolicy(policy);
        int totalLimit = policy.AutoSplit ? policy.MaxTotalSamples : limit;
        double[] plan = adaptive ? bridge.PlanMotionSamples(start, end, step, linear, angular, totalLimit, surface, surfaceStep) : bridge.PlanCoarseMotionSamples(start, end, step, totalLimit);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        mcpInspectionCancellation = deadline; motionInspectionBusy = true;
        try {
            var result = await bridge.CheckContinuousMotionAsync(plan, clearance, policy, limit, deadline.Token, stopOnInterference: stop);
            RecordMotionInspection(result);
            return InspectionOperationSummary(motionReports[^1]);
        }
        finally { motionInspectionBusy = false; mcpInspectionCancellation = null; }
    }
}
