namespace MechCue;

public sealed class MotionPattern
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public Dictionary<Guid, List<KeyPoint>> Points { get; set; } = new();
    public decimal Speed { get; set; } = 1;
    public bool Loop { get; set; }
    public bool Collision { get; set; }
    public override string ToString() => Name;
    public void Validate(IEnumerable<Guid> trackIds)
    {
        if (Id == Guid.Empty || string.IsNullOrWhiteSpace(Name) || Name.Length > 100 || Description == null || Description.Length > 2000 || Speed is < 0.1m or > 10 || Points == null || !Points.Keys.ToHashSet().SetEquals(trackIds)) throw new InvalidDataException("Invalid motion pattern");
        foreach (var points in Points.Values) { if(points == null) throw new InvalidDataException("Invalid pattern points"); new Track { Points = points }.Validate(); if(points[^1].Time > 100000) throw new InvalidDataException("Pattern time exceeds range"); }
    }
}

public partial class MainForm
{
    readonly List<MotionPattern> patterns = new();
    Guid activePattern;
    bool updatingPatterns;
    readonly ComboBox patternChoice = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 210 };
    readonly ContextMenuStrip patternMenu = new();
    string PatternText(string ja, string en) => UiText.IsJapanese ? ja : en;
    MotionPattern ActivePattern => patterns.Single(p => p.Id == activePattern);
    void EnsurePatterns()
    {
        if (patterns.Count == 0) { var p = new MotionPattern { Name = PatternText("通常運転", "Normal operation") }; patterns.Add(p); activePattern = p.Id; }
        var ids = tracks.Select(t => t.Id).ToHashSet();
        foreach (var p in patterns) {
            foreach(var id in p.Points.Keys.Where(id => !ids.Contains(id)).ToArray()) p.Points.Remove(id);
            foreach(var t in tracks.Where(t => !p.Points.ContainsKey(t.Id))) { double value = t.At((double)time.Value); p.Points[t.Id] = [new(0, value), new(Math.Max(1, tracks.Max(t => t.Points[^1].Time)), value)]; }
        }
    }
    void StoreActivePattern()
    {
        EnsurePatterns(); var p = ActivePattern;
        p.Points = tracks.ToDictionary(t => t.Id, t => t.Points.ToList()); p.Speed = speed.Value; p.Loop = loop.Checked; p.Collision = collision.Checked;
    }
    void RefreshPatternChoice()
    {
        EnsurePatterns(); updatingPatterns = true;
        try { patternChoice.Items.Clear(); patternChoice.Items.AddRange(patterns.Cast<object>().ToArray()); patternChoice.SelectedItem = ActivePattern; }
        finally { updatingPatterns = false; }
    }
    void ConfigurePatterns()
    {
        var row = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36, WrapContents = false, BackColor = chartHeading.BackColor, Padding = new Padding(8, 3, 0, 0) };
        chartHeading.Dock = DockStyle.None; chartHeading.AutoSize = true; chartHeading.Padding = new Padding(0, 2, 8, 0); chartHeading.Height = 28;
        vertical.Panel1.Controls.Remove(chartHeading); row.Controls.Add(chartHeading); row.Controls.Add(patternChoice);
        var menuButton = new Button { Text = "⋯", Width = 30, Height = 27 };
        row.Controls.Add(menuButton); vertical.Panel1.Controls.Add(row);
        foreach (var action in new[] { HostAction.PatternNew, HostAction.PatternDuplicate, HostAction.PatternRename, HostAction.PatternDelete, HostAction.PatternManage }) AddMenu(patternMenu, action);
        menuButton.Click += (_, _) => { RefreshMenus(); patternMenu.Show(menuButton, new Point(0, menuButton.Height)); };
        patternChoice.SelectionChangeCommitted += (_, _) => { if (!updatingPatterns && patternChoice.SelectedItem is MotionPattern p) Guard(() => SwitchPattern(p.Id)); RefreshPatternChoice(); };
        commandHints.SetToolTip(menuButton, PatternText("動作の新規作成・複製・名前変更・削除・管理", "Create, duplicate, rename, delete and manage motions"));
        commandHints.SetToolTip(patternChoice, PatternText("動作パターンを切り替えます。再生を停止し、反映をオフにして先頭へ戻ります。", "Switch motion pattern; stop playback, disable CAD reflection and rewind."));
        Disposed += (_, _) => patternMenu.Dispose(); RefreshPatternChoice();
    }
    void SwitchPattern(Guid id)
    {
        EnsurePatterns(); var next = patterns.Single(p => p.Id == id); if(id == activePattern) return;
        PausePlayback(); CommitEditor(false); StoreActivePattern();
        next.Validate(tracks.Select(t => t.Id)); foreach(var t in tracks) bridge.ValidateConceptPoints(t,next.Points[t.Id]);
        live.Checked = false; activePattern = id;
        foreach(var t in tracks) t.Points = next.Points[t.Id].ToList();
        speed.Value = next.Speed; loop.Checked = next.Loop; collision.Checked = next.Collision;
        history.Clear(); lastCheckedTime = null; time.Value = 0; RefreshTracks(Math.Max(0,trackList.SelectedIndex),false); RefreshPatternChoice(); MarkDocumentSettingsChanged();
        status.Text = PatternText("動作切替：", "Motion: ") + next.Name;
    }
    string CheckedPatternName(string name, Guid? except = null)
    {
        name = name.Trim(); if(name.Length is < 1 or > 100 || patterns.Any(p => p.Id != except && string.Equals(p.Name,name,StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException(PatternText("名前は1～100文字で、他の動作と重複しない名前を指定してください。", "Use a unique name of 1–100 characters.")); return name;
    }
    MotionPattern CreatePattern(string name, bool duplicate)
    {
        EnsurePatterns(); if(patterns.Count >= 500) throw new InvalidOperationException("Maximum 500 motion patterns"); name = CheckedPatternName(name); PausePlayback(); CommitEditor(false); StoreActivePattern();
        var source = ActivePattern; var p = new MotionPattern { Name = name, Speed = source.Speed, Loop = source.Loop, Collision = source.Collision, Description = duplicate ? source.Description : "", Points = tracks.ToDictionary(t => t.Id,t => duplicate ? t.Points.ToList() : t.Points.Select(k => k with { Value = t.At((double)time.Value) }).ToList()) };
        patterns.Add(p); SwitchPattern(p.Id); return p;
    }
    void RenamePattern(Guid id, string name, string? description = null)
    {
        EnsurePatterns(); var p = patterns.Single(p => p.Id == id); name = CheckedPatternName(name,id);
        if(description != null && description.Length > 2000) throw new ArgumentException("Description exceeds 2000 characters");
        p.Name = name; if(description != null) p.Description = description; RefreshPatternChoice(); MarkDocumentSettingsChanged();
    }
    void DeletePattern(Guid id)
    {
        EnsurePatterns(); if(patterns.Count == 1) throw new InvalidOperationException(PatternText("最後の動作は削除できません。", "The last pattern cannot be deleted."));
        var p = patterns.Single(p => p.Id == id); if(activePattern == id) SwitchPattern(patterns.First(p => p.Id != id).Id); patterns.Remove(p); RefreshPatternChoice(); MarkDocumentSettingsChanged();
    }
    string? PromptPatternName(string title, string initial, int maxLength = 100)
    {
        using var dialog = new Form { Text = title, Width = 390, Height = 155, StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false };
        var input = new TextBox { Text = initial, Left = 15, Top = 15, Width = 340, MaxLength = maxLength };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Left = 190, Top = 55 };
        var cancel = new Button { Text = PatternText("取消", "Cancel"), DialogResult = DialogResult.Cancel, Left = 275, Top = 55 };
        dialog.Controls.AddRange([input,ok,cancel]); dialog.AcceptButton = ok; dialog.CancelButton = cancel;
        return dialog.ShowDialog(this) == DialogResult.OK ? input.Text : null;
    }
    void PatternAction(HostAction action)
    {
        EnsurePatterns();
        if(action is HostAction.PatternSwitch or HostAction.PatternManage) { ShowPatternManager(); return; }
        if(action == HostAction.PatternDelete) { if(MessageBox.Show(this,PatternText("この動作を削除しますか？", "Delete this motion pattern?"), ActivePattern.Name,MessageBoxButtons.YesNo,MessageBoxIcon.Question) == DialogResult.Yes) DeletePattern(activePattern); return; }
        string? name = PromptPatternName(HostCommands.Caption(action),action == HostAction.PatternRename ? ActivePattern.Name : ActivePattern.Name + PatternText(" コピー", " copy"));
        if(name == null) return; if(action == HostAction.PatternRename) RenamePattern(activePattern,name); else CreatePattern(name,action == HostAction.PatternDuplicate);
    }
    void ShowPatternManager()
    {
        PausePlayback(); CommitEditor(false); StoreActivePattern();
        using var dialog = new Form { Text = PatternText("動作管理", "Motion patterns"), Size = new Size(620,380), StartPosition = FormStartPosition.CenterParent };
        var list = new ListBox { Dock = DockStyle.Fill };
        var info = new Label { Dock = DockStyle.Bottom, Height = 65 };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 65 };
        void RefreshList() { list.Items.Clear(); list.Items.AddRange(patterns.Cast<object>().ToArray()); list.SelectedItem = ActivePattern; }
        list.SelectedIndexChanged += (_,_) => { if(list.SelectedItem is MotionPattern p) info.Text = $"{p.Points.Values.Max(ps=>ps[^1].Time):0.###} s   /   {p.Description}"; };
        foreach(var action in new[] { HostAction.PatternSwitch, HostAction.PatternNew, HostAction.PatternDuplicate, HostAction.PatternRename, HostAction.PatternDelete }) {
            var b = new Button { Text = HostCommands.Caption(action), AutoSize = true }; buttons.Controls.Add(b);
            b.Click += (_,_) => Guard(() => { if(list.SelectedItem is not MotionPattern p) return; if(action == HostAction.PatternSwitch) { SwitchPattern(p.Id); dialog.Close(); return; } SwitchPattern(p.Id); PatternAction(action); RefreshList(); });
        }
        var description = new Button { Text = PatternText("説明", "Description"), AutoSize = true }; buttons.Controls.Add(description);
        description.Click += (_,_) => Guard(() => { if(list.SelectedItem is not MotionPattern p) return; var text = PromptPatternName(PatternText("説明", "Description"),p.Description,2000); if(text != null) RenamePattern(p.Id,p.Name,text); RefreshList(); });
        dialog.Controls.Add(list); dialog.Controls.Add(info); dialog.Controls.Add(buttons); RefreshList(); dialog.ShowDialog(this);
    }
    DocumentSettings CaptureChartPatterns()
    {
        StoreActivePattern(); return new DocumentSettings { Version=2,Patterns=patterns,ActivePatternId=activePattern,Speed=speed.Value,Loop=loop.Checked,Collision=collision.Checked,DragStep=dragStep.Value,DisplayMode=ChartDisplayMode,Tracks=tracks.Select(t=>new SavedTrack { Track=t,Hidden=plot.Hidden.Contains(t) }).ToList() };
    }
    void ResetPatterns() { patterns.Clear(); activePattern = Guid.Empty; StoreActivePattern(); RefreshPatternChoice(); }
}
