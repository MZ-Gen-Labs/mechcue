namespace MechCue;

public partial class MainForm
{
    void DeleteSelectedTrack(bool confirm)
    {
        if (tracks.Count <= 1) throw new InvalidOperationException(UiText.IsJapanese ? "最後の機構は削除できません。別の機構を追加してから削除してください。" : "Keep at least one track. Add another track before deleting this one.");
        var track = Current;
        if (bridge.IsConcept(track)) throw new InvalidOperationException(UiText.IsJapanese ? "連動する概略軸は個別に削除できません。通常の機構は削除できます。" : "Coupled concept axes cannot be deleted individually. Ordinary tracks can be deleted.");
        if (confirm && MessageBox.Show(this, UiText.IsJapanese ? $"機構「{track.Name}」をすべての動作パターンから削除します。登録済みの駆動先は基準状態へ戻して解除します。CADの部品は削除しません。続けますか？" : $"Delete track '{track.Name}' from all motion patterns? Its assigned target will be restored and released; CAD parts remain.", "MechCue", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        using var reflection = PauseCadReflection();
        PausePlayback(); CommitEditor(false); StoreActivePattern();
        // Release before removing graph data; a failed CAD restore must retain it.
        bridge.DeleteTrackBinding(track);
        int selected = tracks.IndexOf(track);
        tracks.Remove(track); plot.Hidden.Remove(track);
        foreach (var pattern in patterns) pattern.Points.Remove(track.Id);
        ClearEditHistory(); lastCheckedTime = null;
        RefreshTracks(Math.Min(selected, tracks.Count - 1)); RefreshPatternChoice(); MarkDocumentSettingsChanged();
        status.Text = UiText.IsJapanese ? $"機構「{track.Name}」を削除しました。" : $"Deleted track '{track.Name}'.";
    }
}

public sealed partial class Bridge
{
    internal void DeleteTrackBinding(Track track)
    {
        if (bindings.ContainsKey(track)) Unbind(track);
        unresolved.Remove(track); nestedKeyPaths.Remove(track);
    }
}
