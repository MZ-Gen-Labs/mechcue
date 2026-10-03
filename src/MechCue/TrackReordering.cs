namespace MechCue;

partial class MechanismList
{
    internal Func<bool>? BeforeSelectItem;
    internal Action<int, int>? ReorderRequested;
    readonly Panel insertionLine = new() { Height = 2, BackColor = Color.DodgerBlue, Visible = false, Enabled = false };
    int reorderSource = -1, insertionIndex;
    Point reorderOrigin;
    bool reordering;
    public MechanismList() { Controls.Add(insertionLine); }
    internal void BeginReorder(Point point)
    {
        CancelReorder();
        int index = IndexFromPoint(point);
        if (point.X < SystemInformation.MenuCheckSize.Width + 8 || index < 0 || SelectedIndex != index) return;
        reorderSource = index; reorderOrigin = point; Capture = true;
    }
    internal void CancelReorder()
    {
        reorderSource = -1; reordering = false; insertionLine.Visible = false;
        Capture = false; Cursor = Cursors.Default;
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (reorderSource < 0) return;
        var threshold = new Rectangle(reorderOrigin.X - SystemInformation.DragSize.Width / 2, reorderOrigin.Y - SystemInformation.DragSize.Height / 2, SystemInformation.DragSize.Width, SystemInformation.DragSize.Height);
        if (!reordering && threshold.Contains(e.Location)) return;
        reordering = true; Cursor = Cursors.Hand;
        if (e.Y < ItemHeight && TopIndex > 0) TopIndex--;
        else if (e.Y > ClientSize.Height - ItemHeight && TopIndex < Items.Count - 1) TopIndex++;
        int target = IndexFromPoint(new Point(Math.Clamp(e.X, 0, Math.Max(0, ClientSize.Width - 1)), Math.Clamp(e.Y, 0, Math.Max(0, ClientSize.Height - 1))));
        insertionIndex = target < 0 ? (e.Y <= 0 ? 0 : Items.Count) : target + (e.Y >= GetItemRectangle(target).Top + ItemHeight / 2 ? 1 : 0);
        int y = insertionIndex < Items.Count ? GetItemRectangle(insertionIndex).Top : GetItemRectangle(Items.Count - 1).Bottom;
        insertionLine.SetBounds(2, Math.Clamp(y - 1, 0, Math.Max(0, ClientSize.Height - 2)), Math.Max(1, ClientSize.Width - 4), 2);
        insertionLine.Visible = true; insertionLine.BringToFront();
    }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left) return;
        int source = reorderSource, destination = insertionIndex;
        bool drop = reordering && ClientRectangle.Contains(e.Location);
        CancelReorder();
        if (drop && source >= 0) ReorderRequested?.Invoke(source, destination);
    }
    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (!Capture) CancelReorder();
    }
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape && reorderSource >= 0) { CancelReorder(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }
    internal void DragReorderForTest(Point from, Point to)
    {
        ClickAt(from); BeginReorder(from);
        OnMouseMove(new MouseEventArgs(MouseButtons.Left, 0, to.X, to.Y, 0));
        OnMouseUp(new MouseEventArgs(MouseButtons.Left, 1, to.X, to.Y, 0));
    }
}

public partial class MainForm
{
    void ConfigureTrackReordering()
    {
        trackList.BeforeSelectItem = () =>
        {
            bool ready = false; Guard(() => { PausePlayback(); CommitEditor(false); ready = true; }); return ready;
        };
        trackList.ReorderRequested = (source, insertion) => Guard(() => ReorderTrack(source, insertion));
        commandHints.SetToolTip(trackList, UiText.IsJapanese ? "名前をドラッグして並べ替え。チェック欄はグラフの表示切り替え。Escで並べ替え取消。" : "Drag names to reorder. Checkboxes control chart visibility. Esc cancels dragging.");
    }
    void ReorderTrack(int source, int insertion)
    {
        if (source < 0 || source >= tracks.Count || insertion < 0 || insertion > tracks.Count) throw new ArgumentOutOfRangeException(nameof(source));
        int destination = insertion > source ? insertion - 1 : insertion;
        if (source == destination) return;
        using var reflection = PauseCadReflection();
        PausePlayback(); CommitEditor(false); StoreActivePattern();
        var selected = Current; var moved = tracks[source];
        tracks.RemoveAt(source); tracks.Insert(destination, moved);
        RefreshTracks(tracks.IndexOf(selected), false); MarkDocumentSettingsChanged();
        status.Text = UiText.IsJapanese ? $"機構「{moved.Name}」の表示順を変更しました。" : $"Reordered track '{moved.Name}'.";
    }
}
