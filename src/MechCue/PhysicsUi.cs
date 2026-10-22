using System.Text.Json;
using MechCue.Physics;

namespace MechCue;

/// <summary>
/// Physics tracks in the chart window: the workpiece's free-body axes (arm5p) are computed from the commanded tracks,
/// shown apart and read-only, and recomputed after the commanded tracks change (automatically, or on request).
/// </summary>
public partial class MainForm
{
    readonly System.Windows.Forms.Timer physicsTimer = new() { Interval = 700 };
    readonly Button physicsButton = new() { AutoSize = true, Height = 27, Visible = false };
    readonly Button physicsReportButton = new() { AutoSize = true, Height = 27, Visible = false };
    readonly CheckBox physicsAuto = new() { AutoSize = true, Visible = false };
    readonly Label physicsStatus = new() { AutoSize = true, Visible = false, Margin = new Padding(6, 6, 0, 0) };
    /// <summary>The engine call; the UI tests replace it so they do not need the physics calculator.</summary>
    internal Func<ConceptMachine, RigidBodySetup, Dictionary<string, List<JointPoint>>, RigidBodyResult> physicsRunner = (m, s, j) => PhysicsProcess.Run(m, s, j);
    int physicsGeneration;
    bool physicsBusy, updatingPhysicsControls;

    SavedConcept? PhysicsConcept()
    {
        var c = bridge.CaptureConcept();
        return c != null && PhysicsTracks.FreeBodyAxes(c.Model) != null && c.Tracks.Values.All(id => tracks.Any(t => t.Id == id)) ? c : null;
    }
    Dictionary<Guid, Track> TrackMap() => tracks.ToDictionary(t => t.Id);
    string PhysicsHashNow(SavedConcept c) => PhysicsTracks.Hash(c, TrackMap(), activePattern);

    internal bool PhysicsAvailable => PhysicsConcept() != null;
    internal bool PhysicsStale => PhysicsConcept() is { } c && PhysicsHashNow(c) != c.Physics?.ComputedHash;

    void ConfigurePhysicsControls(FlowLayoutPanel row)
    {
        physicsButton.Text = PatternText("物理を再計算", "Recompute physics"); physicsAuto.Text = PatternText("自動", "Auto");
        physicsReportButton.Text = PatternText("物理の結果", "Physics results");
        row.Controls.Add(physicsButton); row.Controls.Add(physicsAuto); row.Controls.Add(physicsStatus); row.Controls.Add(physicsReportButton);
        physicsReportButton.Click += (_, _) => Guard(ShowPhysicsReport);
        physicsButton.Click += (_, _) => Guard(() => { PausePlayback(); Commit(); RecomputePhysics(); });
        physicsAuto.CheckedChanged += (_, _) => {
            if (updatingPhysicsControls || PhysicsConcept() is not { } c) return;
            c.Physics ??= new(); c.Physics.Auto = physicsAuto.Checked; MarkDocumentSettingsChanged(); RefreshPhysicsControls(); SchedulePhysics();
        };
        commandHints.SetToolTip(physicsButton, PatternText("腕・指のキーフレームから、ワークの動き（物理トラック）を剛体物理で計算し直します。", "Recompute the workpiece motion (physics tracks) from the arm and finger keyframes."));
        commandHints.SetToolTip(physicsAuto, PatternText("腕・指のキーフレームを編集すると、物理トラックを自動で再計算します。", "Recompute the physics tracks automatically after the arm or finger keyframes change."));
        physicsTimer.Tick += (_, _) => { physicsTimer.Stop(); Guard(() => _ = RecomputePhysicsInBackground()); };
        Disposed += (_, _) => { physicsTimer.Stop(); physicsTimer.Dispose(); };
    }

    void RefreshPhysicsControls()
    {
        var c = PhysicsConcept(); bool available = c != null;
        physicsButton.Visible = physicsAuto.Visible = physicsStatus.Visible = physicsReportButton.Visible = available;
        physicsReportButton.Enabled = c?.Physics?.Report != null;
        if (c == null) return;
        updatingPhysicsControls = true;
        try { physicsAuto.Checked = c.Physics?.Auto ?? true; } finally { updatingPhysicsControls = false; }
        bool stale = PhysicsStale;
        foreach (var id in c.Tracks.Values) tracks.Single(t => t.Id == id).ActualMotionCurrent = !stale;
        physicsStatus.Text = physicsBusy ? PatternText("物理：計算中…", "Physics: computing…") : stale ? PatternText("物理：要再計算", "Physics: out of date") : PatternText("物理：最新", "Physics: up to date");
        physicsStatus.ForeColor = physicsBusy ? Color.DimGray : stale ? Color.Firebrick : Color.SeaGreen;
    }

    /// <summary>Flags the workpiece axes as physics results (after importing or restoring a concept).</summary>
    void MarkPhysicsTracks()
    {
        if (PhysicsConcept() is { } c) PhysicsTracks.MarkDerived(c, tracks);
        RefreshPhysicsControls();
    }

    /// <summary>Starts the quiet period before an automatic recomputation when the commanded tracks changed.</summary>
    void SchedulePhysics()
    {
        if (!documentReady || loading || !bridge.Connected || PhysicsConcept() is not { } c) { return; }
        RefreshPhysicsControls();
        if (c.Physics?.Auto != true || timer.Enabled || !PhysicsStale) return;
        physicsTimer.Stop(); physicsTimer.Start();
    }

    /// <summary>Recomputes on a worker thread, then applies the result if the inputs did not change meanwhile.</summary>
    async Task RecomputePhysicsInBackground()
    {
        if (PhysicsConcept() is not { } c || physicsBusy || timer.Enabled) return;
        string hash = PhysicsHashNow(c); if (hash == c.Physics?.ComputedHash) return;
        var (setup, joints) = PhysicsTracks.Inputs(c, TrackMap());
        int generation = ++physicsGeneration; physicsBusy = true; RefreshPhysicsControls(); RigidBodyResult? result = null; Exception? failure = null;
        try { var model = c.Model; result = await Task.Run(() => physicsRunner(model, setup, joints)); } catch (Exception ex) { failure = ex; }
        physicsBusy = false;
        if (IsDisposed) return;
        if (failure != null) { status.Text = PatternText("物理の計算に失敗しました：", "Physics failed: ") + failure.Message; RefreshPhysicsControls(); return; }
        if (generation != physicsGeneration || PhysicsConcept() != c || PhysicsHashNow(c) != hash) { SchedulePhysics(); return; } // edited meanwhile
        ApplyPhysics(c, result!, hash);
    }

    /// <summary>Recomputes now, on the calling thread (button, MCP).</summary>
    string RecomputePhysics()
    {
        var c = PhysicsConcept() ?? throw new InvalidOperationException(PatternText("この概略モデルには物理トラックがありません（arm5pが対象です）。", "This concept has no physics tracks (arm5p only)."));
        physicsTimer.Stop(); physicsGeneration++;
        string hash = PhysicsHashNow(c); var (setup, joints) = PhysicsTracks.Inputs(c, TrackMap());
        var result = physicsRunner(c.Model, setup, joints);
        return ApplyPhysics(c, result, hash);
    }

    string ApplyPhysics(SavedConcept c, RigidBodyResult result, string hash)
    {
        var warnings = PhysicsTracks.Apply(c, result, TrackMap(), hash);
        StoreActivePattern(); ClearEditHistory(); RefreshTracks(trackList.SelectedIndex, false); MarkDocumentSettingsChanged(); RefreshPhysicsControls();
        string text = PatternText("物理を再計算しました", "Physics recomputed") + $"（{result.Engine}、{result.FinalState}）" + PatternText($" / イベント {result.Events.Count}件", $" / {result.Events.Count} events") + (warnings.Count > 0 ? " / " + warnings[0] : "");
        status.Text = text; return text;
    }

    internal object PhysicsState()
    {
        var c = PhysicsConcept();
        if (c == null) return new { available = false };
        return new { available = true, auto = c.Physics?.Auto ?? true, stale = PhysicsStale, busy = physicsBusy, setup = c.Physics?.Setup ?? new RigidBodySetup(), derivedAxes = PhysicsTracks.FreeBodyAxes(c.Model), report = c.Physics?.Report,
            fingerAxisKeyframes = c.Tracks.Where(p => {var t=tracks.Single(t => t.Id == p.Value);return !ReferenceEquals(t.PlaybackPoints,t.Points);}).ToDictionary(p => p.Key, p => tracks.Single(t => t.Id == p.Value).PlaybackPoints) };
    }

    internal string PhysicsReportText()
    {
        var report = PhysicsConcept()?.Physics?.Report;
        if (report == null) return PatternText("計算結果はまだありません。", "No computation results yet.");
        var lines = new List<string> { PhysicsStale ? PatternText("要再計算：以下は前回の結果です。", "Out of date: the previous result is shown below.") : PatternText("最新の計算結果", "Current computation result"),
            $"{report.Engine} / {report.FinalState}", PatternText($"計算時間範囲：0～{report.EndTime:0.###} 秒 / {report.Samples} 点 / 最大傾き {report.MaxTiltDegrees:0.###}°", $"Interval: 0–{report.EndTime:0.###} s / {report.Samples} samples / maximum tilt {report.MaxTiltDegrees:0.###}°"), "" };
        foreach (var e in report.Events.OrderBy(e => e.Time)) lines.Add($"{e.Time:0.###} s / {PhysicsEventName(e.Type)} / {e.Detail}");
        if (report.Events.Count == 0) lines.Add(PatternText("イベントなし", "No events"));
        foreach (var warning in report.Warnings) lines.Add(PatternText("警告：", "Warning: ") + warning);
        return string.Join(Environment.NewLine, lines);
    }
    string PhysicsEventName(string type) => type switch {
        "lift-off" => PatternText("持ち上げ", "Lift-off"), "settled" => PatternText("静止", "Settled"),
        "tipped" => PatternText("転倒", "Tipped"), "finger-stopped" => PatternText("指の停止", "Finger stopped"), _ => type
    };
    void ShowPhysicsReport()
    {
        using var dialog = new Form { Text = PatternText("物理の結果", "Physics results"), StartPosition = FormStartPosition.CenterParent, Size = new Size(760, 440), MinimumSize = new Size(420, 240) };
        dialog.Controls.Add(new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Text = PhysicsReportText() });
        dialog.ShowDialog(this);
    }

    /// <summary>MCP: change the physics settings (auto, setup) and/or recompute.</summary>
    object HandlePhysics(JsonElement args, string method)
    {
        var c = PhysicsConcept() ?? throw new InvalidOperationException(PatternText("この概略モデルには物理トラックがありません（arm5pが対象です）。", "This concept has no physics tracks (arm5p only)."));
        PausePlayback(); Commit();
        if (method == "set_physics") {
            c.Physics ??= new();
            if (args.TryGetProperty("auto", out var auto) && auto.ValueKind is JsonValueKind.True or JsonValueKind.False) c.Physics.Auto = auto.GetBoolean();
            if (args.TryGetProperty("setupJson", out var setupJson) && !string.IsNullOrWhiteSpace(setupJson.GetString())) {
                var setup = JsonSerializer.Deserialize<RigidBodySetup>(setupJson.GetString()!, ConceptMachine.JsonOptions) ?? throw new ArgumentException("Invalid setupJson");
                setup.EndTime = 0; // the chart length decides
                c.Physics.Setup = setup;
            }
            MarkDocumentSettingsChanged(); RefreshPhysicsControls(); SchedulePhysics();
        } else RecomputePhysics();
        return AiState();
    }
}
