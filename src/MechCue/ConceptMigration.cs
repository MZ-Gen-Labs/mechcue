using System.Text.Json;
namespace MechCue;

public sealed partial class Bridge
{
    public static object CadReadMechCueSettings(string expectedDocument)
    {
        var document = CadDocument(CadApplication(), expectedDocument, ".asm", false);
        string? json = DocumentStorage.Read(document);
        if (json != null) DocumentSettings.Parse(json);
        return new { document = CadName(document), dirty = Convert.ToBoolean(Get(document, "Dirty")), settingsJson = json,
            note = "Persisted embedded settings only; save the MechCue session first to include pending chart edits." };
    }
    public static object CadReopen(string expectedDocument)
    {
        var document = CadDocument(CadApplication(), expectedDocument, write: false);
        if (!Path.IsPathFullyQualified(expectedDocument) || !File.Exists(expectedDocument)) throw new InvalidOperationException("Reopen requires a saved file.");
        if (Convert.ToBoolean(Get(document, "Dirty"))) throw new InvalidOperationException("Save the document first; reopen never discards dirty changes.");
        string? before = CadExtension(document) == ".asm" ? DocumentStorage.Read(document) : null;
        Call(document, "Close", false);
        CadOpen(expectedDocument);
        var reopened = CadDocument(CadApplication(), expectedDocument, write: false);
        string? after = CadExtension(reopened) == ".asm" ? DocumentStorage.Read(reopened) : null;
        if (before != after) throw new InvalidOperationException("Reopened, but embedded settings differ. Inspect document before editing.");
        return new { document = CadInfo(reopened), embeddedSettingsPreserved = true,
            note = "Closing may replace the add-in session; list sessions again before further MechCue operations." };
    }
    internal static DocumentSettings BuildConceptMigration(string settingsJson, ConceptMachine model, bool hideUnboundTracks)
    {
        var settings = DocumentSettings.Parse(settingsJson);
        var original = settings.Concept ?? throw new InvalidOperationException("Source has no concept-axis mappings.");
        model.Validate();
        if (JsonSerializer.Serialize(original.Model.Axes, ConceptMachine.JsonOptions) != JsonSerializer.Serialize(model.Axes, ConceptMachine.JsonOptions))
            throw new InvalidOperationException("Axis definitions differ; migration requires identical IDs, hierarchy, origins, directions and ranges.");
        if (settings.Tracks.Any(t => t.Target != null)) throw new InvalidOperationException("Source contains non-concept CAD bindings; remap these separately.");
        foreach (var axis in model.Axes)
        {
            var track = settings.Tracks.Single(t => t.Track.Id == original.Tracks[axis.Id]).Track;
            if (track.Kind != (axis.Kind == "rotary" ? "部品回転" : "部品移動") || track.Axis != axis.Direction)
                throw new InvalidOperationException("Source concept track driver differs from its axis.");
            foreach (var points in settings.Patterns.Select(p => p.Points[track.Id]).Append(track.Points))
                if (points.Any(p => p.Value < axis.Minimum || p.Value > axis.Maximum)) throw new InvalidOperationException("Source keyframes exceed axis limits.");
        }
        settings.Concept = new SavedConcept { Model = model, Baseline = new(original.Baseline), Tracks = new(original.Tracks) };
        if (hideUnboundTracks) foreach (var track in settings.Tracks)
            if (!original.Tracks.Values.Contains(track.Track.Id)) track.Hidden = true;
        return DocumentSettings.Parse(settings.Json());
    }
    internal DocumentSettings PrepareConceptMigration(string expectedDocument, string manifestPath, string settingsJson, bool hideUnboundTracks)
    {
        Check(); EnsureWritableDocument();
        if (!string.Equals(CadName(doc!), expectedDocument, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Connected document differs from expectedDocument.");
        if (CaptureConcept() != null || BindingCount != 0 || DocumentStorage.Read(doc!) != null)
            throw new InvalidOperationException("Destination already has MechCue settings or bindings; migration does not overwrite them.");
        var (model, _, parts) = ConceptLoad(expectedDocument, manifestPath, false,app!);
        var settings = BuildConceptMigration(settingsJson, model, hideUnboundTracks);
        var poses = model.Poses(settings.Concept!.Baseline);
        if (poses.Any(p => !ConceptSame(Matrix(parts[p.Key]), p.Value))) throw new InvalidOperationException("Destination must be at the source baseline pose before migration.");
        return settings;
    }
}
public partial class MainForm
{
    void MigrateConceptSettings(string expectedDocument, string manifestPath, string settingsJson, bool hideUnboundTracks)
    {
        if (!documentReady || !bridge.Connected) throw new InvalidOperationException("Connect to the destination assembly first.");
        if (documentSettingsDirty) throw new InvalidOperationException("Destination has pending MechCue edits; migration does not replace them.");
        var settings = bridge.PrepareConceptMigration(expectedDocument, manifestPath, settingsJson, hideUnboundTracks);
        PausePlayback(); live.Checked = false;
        RestoreDocumentSettings(settings);
        MarkDocumentSettingsChanged();
        if (restorationWarnings.Count > 0)
            throw new InvalidOperationException("PARTIAL SUCCESS: chart settings loaded, but bindings need inspection. Do not retry migration: " + string.Join(" / ",restorationWarnings));
        status.Text = "概略軸の駆動先を移行しました。グラフ・動作IDを保持しています。CAD保存してください。";
    }
}
