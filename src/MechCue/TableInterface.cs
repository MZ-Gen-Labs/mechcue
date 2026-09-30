namespace MechCue;
public enum HostAction { Open=1, Play=2, Stop=3, Maximize=4, Minimize=5, Compact=6, Save=7 }
public partial class MainForm
{
    public void ExecuteHostAction(HostAction action) => Guard(() =>
    {
        switch(action)
        {
            case HostAction.Play: StartPlayback(); break;
            case HostAction.Stop: timer.Stop(); break;
            case HostAction.Maximize: WindowState=FormWindowState.Maximized; break;
            case HostAction.Minimize: WindowState=FormWindowState.Minimized; break;
            case HostAction.Compact: SetCompact(!compact); break;
            case HostAction.Save: SaveToDocument(); break;
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
        timer.Stop();live.Checked=false;
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
