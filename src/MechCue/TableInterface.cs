namespace MechCue;
public enum HostAction {
    Open=1, Play=2, Stop=3, Maximize=4, Minimize=5, Compact=6, Save=7,
    Connect=8, Disconnect=9, Restore=10, JsonOpen=11, JsonSave=12, TableExport=13, TableImport=14,
    Apply=15, Collision=16, Loop=17, Overlay=18, Edit=19, Review=20, Undo=21, Help=22,
    AddTrack=23, Update=24, SelectedPart=25, AllTargets=26, ReadCurrent=27, Highlight=28,
    Assign=29, Unassign=30, Preset=31, Ai=32, TracksPanel=33, SettingsPanel=34, PointsPanel=35
}
public partial class MainForm
{
    public void ExecuteHostAction(HostAction action) => Guard(() =>
    {
        DiagnosticLog.Write("host-command", new { action = action.ToString(), layout = DiagnosticState() });
        switch(action)
        {
            case HostAction.Play: TogglePlayback(); break;
            case HostAction.Stop: PausePlayback(); break;
            case HostAction.Maximize: WindowState=FormWindowState.Maximized; break;
            case HostAction.Minimize: WindowState=FormWindowState.Minimized; break;
            case HostAction.Compact: SetCompact(!compact); break;
            case HostAction.Save: SaveToDocument(); break;
            case HostAction.Connect: ConnectDocument(); break;
            case HostAction.Disconnect: InvokeEditorButton("切断"); break;
            case HostAction.Restore: InvokeEditorButton("基準状態に戻す"); break;
            case HostAction.JsonOpen: LoadFile(); break;
            case HostAction.JsonSave: SaveFile(); break;
            case HostAction.TableExport: ExportTable(); break;
            case HostAction.TableImport: ImportTable(); break;
            case HostAction.Apply: live.Checked = !live.Checked; break;
            case HostAction.Collision: collision.Checked = !collision.Checked; break;
            case HostAction.Loop: loop.Checked = !loop.Checked; break;
            case HostAction.Overlay: overlay.Checked = !overlay.Checked; break;
            case HostAction.Edit: if (compact) SetCompact(false); editMode.Checked = true; break;
            case HostAction.Review: reviewMode.Checked = true; break;
            case HostAction.Undo: Undo(); break;
            case HostAction.Help: ShowQuickStart(); break;
            case HostAction.AddTrack: InvokeEditorButton("＋ 機構を追加"); break;
            case HostAction.Update: Commit(); break;
            case HostAction.SelectedPart: FromCadSelection(); break;
            case HostAction.AllTargets: PopulateTargets(); break;
            case HostAction.ReadCurrent: ReadCurrentValues(); break;
            case HostAction.Highlight: HighlightTarget(); break;
            case HostAction.Assign: InvokeEditorButton("駆動先を登録・変更"); break;
            case HostAction.Unassign: InvokeEditorButton("この機構の割り当てを解除"); break;
            case HostAction.Preset: InvokeEditorButton("移動 → 保持 → 戻る を挿入"); break;
            case HostAction.Ai: aiAccess.Checked = !aiAccess.Checked; break;
            case HostAction.TracksPanel: TogglePanel(action); break;
            case HostAction.SettingsPanel: TogglePanel(action); break;
            case HostAction.PointsPanel: TogglePanel(action); break;
        }
    });
    void ExportTable()
    {
        Commit();
        using var dialog=new SaveFileDialog { Filter="Excel (*.xlsx)|*.xlsx|CSV (*.csv)|*.csv",FileName="MechCue.xlsx" };
        if(dialog.ShowDialog()!=DialogResult.OK)return;
        ChartTable.Write(dialog.FileName,tracks);status.Text="CSV・Excelへグラフを書き出しました。";
    }
    void ImportTable()
    {
        using var dialog=new OpenFileDialog { Filter="Excel / CSV|*.xlsx;*.csv" };
        if(dialog.ShowDialog()!=DialogResult.OK)return;
        ImportTableData(ChartTable.Read(dialog.FileName));
        status.Text="グラフを読み込みました。一致するIDの割り当てと、ファイルにない機構は保持しています。";
    }
    internal void ImportTableData(List<Track> loaded)
    {
        foreach(var track in loaded) { track.Validate(); if(!kind.Items.Contains(track.Kind) || !axis.Items.Contains(track.Axis) || track.Points[^1].Time>100000)throw new InvalidDataException("Invalid imported track metadata or time"); }
        if(loaded.Count==0 || loaded.Any(t=>t.Id==Guid.Empty) || loaded.Select(t=>t.Id).Distinct().Count()!=loaded.Count)throw new InvalidDataException("Invalid imported track IDs");
        if(bridge.Connected)
            foreach(var imported in loaded)
            {
                var current=tracks.SingleOrDefault(t=>t.Id==imported.Id);
                if(current!=null && (bridge.BoundLabel(current)!=null || bridge.PendingLabel(current)!=null) && (current.Kind!=imported.Kind || current.Axis!=imported.Axis))throw new InvalidOperationException("割り当て済みの機構の駆動方法・軸は変更できません。先に解除してください。");
            }
        PausePlayback();live.Checked=false;
        if(!bridge.Connected){ history.Clear();tracks.Clear();tracks.AddRange(loaded); }
        else foreach(var imported in loaded)
        {
            var current=tracks.SingleOrDefault(t=>t.Id==imported.Id);
            if(current==null){tracks.Add(imported);continue;}
            Remember(current);current.Name=imported.Name;current.Kind=imported.Kind;current.Axis=imported.Axis;current.Points=imported.Points;
        }
        RefreshTracks(0);MarkDocumentSettingsChanged();
    }
}
