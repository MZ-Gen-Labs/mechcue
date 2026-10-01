using System.Reflection;
using System.Runtime.InteropServices;

namespace MechCue;
public sealed partial class Bridge
{
    readonly Dictionary<Track, SavedTarget> unresolved = new();
    object? documentEvents;
    System.Runtime.InteropServices.ComTypes.IConnectionPoint? saveConnection;
    ApplicationEventSink? saveSink;
    int saveCookie;
    internal object Document { get { Check(false); return doc!; } }
    public string? PendingLabel(Track track) => unresolved.TryGetValue(track, out var saved) ? saved.Label : null;
    internal string? ReadSettings() { Check(); return DocumentStorage.Read(doc!); }
    internal void WriteSettings(string json) { Check(false); DocumentSettings.Parse(json); DocumentStorage.Write(doc!, json); }
    internal void SaveDocument() { Check(); Call(doc!, "Save"); }
    internal bool AttachDocumentEvents(Action beforeSave, Action? beforeClose = null, Action? afterDeactivate = null)
    {
        DetachDocumentEvents();
        if (!Marshal.IsComObject(doc!)) return false;
        try
        {
            documentEvents = Get(app!, "ApplicationEvents");
            var container = (System.Runtime.InteropServices.ComTypes.IConnectionPointContainer)documentEvents;
            var id = typeof(IMechCueApplicationEvents).GUID;
            container.FindConnectionPoint(ref id, out saveConnection);
            saveSink = new ApplicationEventSink(doc!, beforeSave, beforeClose ?? (() => { }), afterDeactivate);
            saveConnection!.Advise(saveSink, out saveCookie);
            return true;
        }
        catch { DetachDocumentEvents(); throw; }
    }
    void DetachDocumentEvents()
    {
        if (saveConnection != null)
        {
            try { if (saveCookie != 0) saveConnection.Unadvise(saveCookie); } catch (COMException) { }
            DocumentStorage.Release(saveConnection);
        }
        DocumentStorage.Release(documentEvents);
        saveCookie = 0; saveConnection = null; documentEvents = null; saveSink = null;
    }
    internal void EnsureWritableDocument()
    {
        Check(false);
        if (Convert.ToBoolean(Get(doc!, "ReadOnly"))) throw new InvalidOperationException("読み取り専用のアセンブリには設定を保存できません。");
    }
    internal void MarkSettingsDirty()
    {
        Check(false);
        if (!Convert.ToBoolean(Get(doc!, "ReadOnly"))) Set(doc!, "Dirty", true);
    }
    internal void PrepareDocumentSave()
    {
        Check(false);
        RestoreConceptBaseline();
        foreach (var binding in bindings.Values)
        {
            RestoreBinding(binding);
            binding.Active = false;
        }
    }
    static object NamedItem(object collection, string name) => collection.GetType().InvokeMember("Item", BindingFlags.InvokeMethod | BindingFlags.GetProperty, null, collection, [name])!;
    static string? AttributeId(object target)
    {
        try
        {
            var set = NamedItem(Get(target, "AttributeSets"), "MechCueIdentity");
            if (set == null) return null;
            var attribute = NamedItem(set, "Id");
            return attribute == null ? null : Convert.ToString(Get(attribute, "Value"));
        }
        catch (Exception ex) when (ex is COMException or TargetInvocationException or MissingMethodException or KeyNotFoundException) { return null; }
    }
    static string EnsureAttributeId(object target)
    {
        var existing = AttributeId(target);
        if (Guid.TryParse(existing, out _)) return existing!;
        var sets = Get(target, "AttributeSets");
        object set;
        try { set = NamedItem(sets, "MechCueIdentity") ?? Call(sets, "Add", "MechCueIdentity"); }
        catch (Exception ex) when (ex is COMException or TargetInvocationException or KeyNotFoundException) { set = Call(sets, "Add", "MechCueIdentity"); }
        object attribute;
        try { attribute = NamedItem(set, "Id") ?? Call(set, "Add", "Id", 8); }
        catch (Exception ex) when (ex is COMException or TargetInvocationException or KeyNotFoundException) { attribute = Call(set, "Add", "Id", 8); } // seStringANSI: GUID is ASCII.
        string id = Guid.NewGuid().ToString("D"); Set(attribute, "Value", id); return id;
    }
    static byte[] ReferenceKey(object target)
    {
        object[] args = [Array.Empty<byte>(), 0]; var modifier = new ParameterModifier(2); modifier[0] = true; modifier[1] = true;
        target.GetType().InvokeMember("GetReferenceKey", BindingFlags.InvokeMethod, null, target, args, [modifier], null, null);
        return ((Array)args[0]).Cast<byte>().ToArray();
    }
    internal SavedTarget? CaptureTarget(Track track)
    {
        Check(false);
        if (!bindings.TryGetValue(track, out var binding)) return unresolved.GetValueOrDefault(track);
        var target = binding.Target;
        var saved = new SavedTarget { Property = target.Property, Label = target.Label, ObjectType = Convert.ToInt32(Get(target.Com, "Type")), Baseline = target.Property == "Matrix" ? (double[])((double[])binding.Original).Clone() : [Convert.ToDouble(binding.Original)] };
        if (target.Property == "Matrix") saved.ReferenceKey = Convert.ToBase64String(ReferenceKey(target.Com));
        else
        {
            saved.AttributeId = EnsureAttributeId(target.Com);
            if (Targets(track.Kind).Count(t => AttributeId(t.Com) == saved.AttributeId) != 1) throw new InvalidOperationException("駆動先の識別IDが重複しています。対象のコピーを確認してください。");
        }
        foreach (var ground in binding.Grounds)
        {
            string id = EnsureAttributeId(ground.Relation);
            if (!saved.Grounds.TryAdd(id, ground.Suppress)) throw new InvalidOperationException("固定拘束の識別IDが重複しています。");
        }
        return saved;
    }
    internal string? RestoreTarget(Track track, SavedTarget saved)
    {
        Check();
        unresolved[track] = saved;
        try
        {
            var candidates = Targets(track.Kind);
            Target? target;
            if (saved.ReferenceKey != null)
            {
                object[] args = [Convert.FromBase64String(saved.ReferenceKey), null!]; var modifier = new ParameterModifier(2); modifier[0] = true; modifier[1] = true;
                if (Marshal.IsComObject(doc!))
                {
                    Array key = Convert.FromBase64String(saved.ReferenceKey);
                    ((IAssemblyKeyResolver)doc!).BindKeyToObject(ref key, out var resolved);
                    args[1] = resolved;
                }
                else doc!.GetType().InvokeMember("BindKeyToObject", BindingFlags.InvokeMethod, null, doc, args, [modifier], null, null);
                target = candidates.SingleOrDefault(t => Equals(t.Com, args[1]));
            }
            else
            {
                var matches = candidates.Where(t => AttributeId(t.Com) == saved.AttributeId).ToList();
                if (matches.Count != 1) throw new InvalidOperationException("駆動先が見つからないか、識別IDが重複しています。");
                target = matches[0];
            }
            if (target == null || target.Property != saved.Property || Convert.ToInt32(Get(target.Com, "Type")) != saved.ObjectType) throw new InvalidOperationException("保存した駆動先と現在の対象が一致しません。");
            // Read-only restoration: prepare the assignment without moving parts or suppressing grounds.
            var grounds = new List<(object Relation, bool Suppress)>();
            if (target.Property == "Matrix")
            {
                var relations = Get(target.Com, "Relations3d");
                for (int i = 1; i <= Convert.ToInt32(Get(relations, "Count")); i++)
                {
                    var relation = GetItem(relations, i);
                    if (Convert.ToInt32(Get(relation, "Type")) != 1959028688) continue;
                    string? id = AttributeId(relation);
                    if (id == null || !saved.Grounds.TryGetValue(id, out bool suppress) || grounds.Any(g => AttributeId(g.Relation) == id)) throw new InvalidOperationException("固定拘束が変更されています。再割り当てしてください。");
                    grounds.Add((relation, suppress));
                }
                if (grounds.Count != saved.Grounds.Count) throw new InvalidOperationException("固定拘束が変更されています。再割り当てしてください。");
            }
            Bind(track, target, false);
            bindings[track] = new Binding(target, target.Property == "Matrix" ? (object)saved.Baseline.Clone() : saved.Baseline[0], grounds) { Active = false };
            unresolved.Remove(track);
            return null;
        }
        catch (Exception ex) { return track.Name + ": " + (ex.InnerException ?? ex).Message; }
    }
}

public partial class MainForm
{
    bool writingDocument;
    bool documentReady;
    bool documentSettingsDirty;
    readonly List<string> restorationWarnings = [];
    void ConfigureDocumentPersistence()
    {
        foreach (var control in new Control[] { name, kind, axis }) control.TextChanged += (_, _) => MarkDocumentSettingsChanged();
        grid.CellValueChanged += (_, _) => MarkDocumentSettingsChanged();
        grid.UserDeletedRow += (_, _) => MarkDocumentSettingsChanged();
        foreach (var number in new[] { speed, dragStep }) number.ValueChanged += (_, _) => MarkDocumentSettingsChanged();
        foreach (var option in new[] { loop, collision }) option.CheckedChanged += (_, _) => MarkDocumentSettingsChanged();
    }
    void MarkDocumentSettingsChanged()
    {
        if (!documentReady || loading || writingDocument || !bridge.Connected) return;
        documentSettingsDirty = true;
        try { bridge.MarkSettingsDirty(); }
        catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
    }
    void ConnectDocument()
    {
        PausePlayback(); live.Checked = false;
        documentReady = false;
        string title = bridge.Connect();
        status.Text = "接続：" + title;
        var json = bridge.ReadSettings();
        if (json != null) RestoreDocumentSettings(DocumentSettings.Parse(json));
        bridge.AttachDocumentEvents(BeforeDocumentSave, () =>
        {
            PausePlayback(); live.Checked = false;
            if (hostedDocumentWindow) ShutdownFromHost();
        }, () => { if (hostedDocumentWindow && !IsDisposed) ShutdownFromHost(); });
        documentReady = true; documentSettingsDirty = false;
        PopulateTargets();
        if (json == null) status.Text = "接続：" + title + " / CAD保存で設定をアセンブリ内に保存できます。";
    }
    void RestoreDocumentSettings(DocumentSettings settings)
    {
        PausePlayback(); live.Checked = false; history.Clear(); restorationWarnings.Clear();
        tracks.Clear(); tracks.AddRange(settings.Tracks.Select(entry => entry.Track));
        plot.Hidden.Clear(); foreach (var entry in settings.Tracks.Where(entry => entry.Hidden)) plot.Hidden.Add(entry.Track);
        speed.Value = settings.Speed; dragStep.Value = settings.DragStep; loop.Checked = settings.Loop; collision.Checked = settings.Collision; overlay.SelectedIndex = (settings.DisplayMode ?? (settings.Overlay ? "checked" : "selected")) switch { "selected" => 0, "all" => 2, _ => 1 }; plot.DisplayMode = ChartDisplayMode; RefreshLegend();
        foreach (var entry in settings.Tracks)
            if (entry.Target != null && bridge.RestoreTarget(entry.Track, entry.Target) is string warning) restorationWarnings.Add(warning);
        if(settings.Concept!=null) try { bridge.RestoreConcept(settings.Concept,tracks); } catch(Exception ex) { bridge.RetainConcept(settings.Concept); restorationWarnings.Add("概略軸："+ex.Message); }
        time.Value = 0; lastCheckedTime = null; RefreshTracks(0);
        patterns.Clear(); patterns.AddRange(settings.Patterns); activePattern = settings.ActivePatternId; StoreActivePattern(); RefreshPatternChoice();
        status.Text = restorationWarnings.Count == 0 ? "アセンブリからグラフと駆動先を復元しました。反映はオフです。" : "要再割り当て：" + string.Join(" / ", restorationWarnings);
    }
    DocumentSettings CaptureDocumentSettings()
    {
        // Validate the editor without refreshing CAD controls inside the BeforeSave callback.
        grid.EndEdit();
        var points = grid.Rows.Cast<DataGridViewRow>().Where(r => !r.IsNewRow).Select(r => new KeyPoint(double.Parse(Convert.ToString(r.Cells[0].Value)!, System.Globalization.CultureInfo.CurrentCulture), double.Parse(Convert.ToString(r.Cells[1].Value)!, System.Globalization.CultureInfo.CurrentCulture))).ToList();
        new Track { Points = points }.Validate();
        if (bridge.BoundLabel(Current) != null && (Current.Kind != CurrentKind || Current.Axis != axis.Text)) throw new InvalidOperationException("駆動方法・軸を変更する前に割り当てを解除してください。");
        Current.Name = name.Text; Current.Kind = CurrentKind; Current.Axis = axis.Text; Current.Points = points;
        StoreActivePattern();
        return new DocumentSettings { Version = 2, Patterns = patterns, ActivePatternId = activePattern, Concept = bridge.CaptureConcept(), Speed = speed.Value, DragStep = dragStep.Value, Loop = loop.Checked, Collision = collision.Checked, Overlay = ChartDisplayMode != "selected", DisplayMode = ChartDisplayMode, Tracks = tracks.Select(track => new SavedTrack { Track = track, Hidden = plot.Hidden.Contains(track), Target = bridge.CaptureTarget(track) }).ToList() };
    }
    void WriteDocumentSettings()
    {
        if (writingDocument) return;
        writingDocument = true;
        try { bridge.EnsureWritableDocument(); var settings = CaptureDocumentSettings(); bridge.PrepareDocumentSave(); bridge.WriteSettings(settings.Json()); documentSettingsDirty = false; UpdateConnection(); }
        finally { writingDocument = false; }
    }
    void BeforeDocumentSave()
    {
        if (writingDocument || !documentReady || !bridge.Connected) return;
        try { PausePlayback(); live.Checked = false; WriteDocumentSettings(); status.Text = "MechCueの設定をアセンブリへ反映しました。"; }
        catch (Exception ex) { status.Text = "MechCue設定の保存に失敗しました：" + (ex.InnerException ?? ex).Message; MessageBox.Show(this, UiText.Text(status.Text), "MechCue", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }
    void SaveToDocument()
    {
        if (!documentReady) throw new InvalidOperationException("先にSolid Edgeに接続してください。");
        PausePlayback(); live.Checked = false; WriteDocumentSettings(); bridge.SaveDocument();
        status.Text = "グラフと駆動先をアセンブリに保存しました。";
    }
    void StageOnClose()
    {
        if (!documentReady || !documentSettingsDirty || !bridge.Connected) return;
        // Store before disconnecting so current assignments and their reference baselines are retained.
        WriteDocumentSettings();
        status.Text = "設定をアセンブリへ反映しました。Solid Edgeでファイルを保存してください。";
    }
}
