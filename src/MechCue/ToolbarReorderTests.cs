namespace MechCue;

public partial class MainForm
{
    internal void VerifyToolbarAndReorder()
    {
        void Assert(bool value, string message) { if (!value) throw new Exception(message); }
        var document = new SelfTest.FakeDocument(); var relation = new SelfTest.FakeRelation(); document.Relations3d.Items.Add(relation);
        var app = new SelfTest.FakeApplication { ActiveDocument = document }; app.OpenDocuments.Items.Add(document);
        using var form = new MainForm(hostedApplication: app); form.Show(); Application.DoEvents();
        form.Width = 1180;
        var savedLanguage = UiText.Mode;
        try
        {
            foreach (var languageMode in new[] { "ja", "en" })
            {
                UiText.SetMode(languageMode, false); Application.DoEvents();
                var controls = form.top.Controls.Cast<Control>().Where(c => c.Visible).ToArray();
                Assert(controls.Select(c => c.Top).Distinct().Count() == 1, "Full toolbar wraps at minimum width");
                Assert(controls.All(c => c.Right <= form.top.ClientSize.Width - form.top.Padding.Right), "Single-row toolbar clips a command: " + languageMode + "; width=" + form.Width + "; toolbar=" + form.top.ClientSize + "; dpi=" + form.DeviceDpi + "; " + string.Join("; ", controls.Select(c => $"{c.GetType().Name} '{c.Text}' {c.Bounds}")));
                Assert(form.reviewMode.Text == (languageMode == "ja" ? "表示" : "View"), "Display mode label mismatch");
                Assert(form.top.Controls.OfType<Button>().Single(b => Equals(b.Tag, "menu-settings")).Width < 100, "Settings button remains excessively wide");
                form.SetCompact(true); form.SetCompact(false); Application.DoEvents();
                Assert(form.top.Controls.Cast<Control>().All(c => c.Right <= form.top.ClientSize.Width - form.top.Padding.Right), "Restoring full view widened the toolbar");
            }
        }
        finally { UiText.SetMode(savedLanguage, false); }
        // A smaller monitor can constrain restored full bounds below the usual minimum.
        form.MinimumSize = new Size(1044, 740); form.Width = 1044;
        try
        {
            foreach (var languageMode in new[] { "ja", "en" })
            {
                UiText.SetMode(languageMode, false); Application.DoEvents();
                Assert(form.top.Controls.Cast<Control>().All(c => c.Right <= form.top.ClientSize.Width - form.top.Padding.Right), "Narrow-monitor toolbar clips after localization");
            }
        }
        finally { UiText.SetMode(savedLanguage, false); form.MinimumSize = new Size(1180, 740); }
        form.grid.Rows[1].Cells[1].Value = 77d; form.Commit();
        form.plot.Focus(); form.OnActivated(EventArgs.Empty); form.RefreshEditShortcuts();
        Assert(form.editShortcutWindows.ContainsKey(form.plot), "Native shortcut routing must attach to child handles");
        Assert(form.editShortcutWindows[form.plot].Route(0x0100,Keys.Control|Keys.Z)&&form.Current.Points[1].Value == 100, "Native child Ctrl+Z did not undo chart editing");
        Assert(form.editShortcutWindows[form.plot].Route(0x0100,Keys.Control|Keys.Y)&&form.Current.Points[1].Value == 77, "Native child Ctrl+Y did not redo chart editing");
        Assert(!form.editShortcutWindows[form.plot].Route(0x0101,Keys.Control|Keys.Z),"Key release must not execute a second undo");
        form.name.Focus(); Application.DoEvents();
        Assert(!form.TryEditShortcut(Keys.Control | Keys.Z), "Text editing must keep its own Ctrl+Z");
        form.plot.Focus(); form.OnActivated(EventArgs.Empty); form.RefreshEditShortcuts();
        form.OnDeactivate(EventArgs.Empty); Assert(!form.editShortcutWindowActive, "Undo routing leaked outside chart activation");
        Assert(!form.editShortcutWindows[form.plot].Route(0x0100,Keys.Control|Keys.Z),"Inactive child must not undo");
        var actions = HostCommands.RibbonActions;
        Assert(Array.IndexOf(actions, HostAction.Redo) == Array.IndexOf(actions, HostAction.Undo) + 1 && HostCommands.Group(HostAction.Redo) == HostCommands.Group(HostAction.Undo), "Ribbon redo must follow undo in the edit group");

        var originalOrder = form.tracks.ToArray();
        form.bridge.Bind(originalOrder[0], form.bridge.Targets(originalOrder[0].Kind).First());
        var variant = form.CreatePattern("Reorder copy", true);
        form.trackList.SetItemChecked(1, false);
        form.grid.Rows[1].Cells[1].Value = 33d;
        var source = form.trackList.GetItemRectangle(2); var target = form.trackList.GetItemRectangle(0);
        form.trackList.DragReorderForTest(new Point(45, source.Top + source.Height / 2), new Point(45, target.Top + 2));
        Assert(form.tracks.SequenceEqual(new[] { originalOrder[2], originalOrder[0], originalOrder[1] }) && ReferenceEquals(form.Current, originalOrder[2]), "Drag did not reorder or retain its selected track");
        Assert(originalOrder[0].Points[1].Value == 33 && form.ActivePattern.Points[originalOrder[0].Id][1].Value == 33, "Drag lost a pending table edit");
        Assert(form.plot.Hidden.Contains(originalOrder[1]) && !form.trackList.GetItemChecked(2), "Reordering changed checked/hidden state");
        Assert(form.bridge.BindingCount == 1 && form.bridge.BoundLabel(originalOrder[0]) != null && relation.Offset == 0, "Reordering altered CAD bindings or pose");
        Assert(form.activePattern == variant.Id && form.patterns.All(p => p.Points.Keys.ToHashSet().SetEquals(originalOrder.Select(t => t.Id))), "Reordering changed pattern identity or mappings");
        var stored = DocumentSettings.Parse(form.CaptureDocumentSettings().Json());
        Assert(stored.Tracks.Select(t => t.Track.Id).SequenceEqual(form.tracks.Select(t => t.Id)), "Saved order differs from displayed order");
        using var reopened = new MainForm(hostedApplication: app); reopened.bridge.Connect(); reopened.RestoreDocumentSettings(stored);
        Assert(reopened.tracks.Select(t => t.Id).SequenceEqual(form.tracks.Select(t => t.Id)), "Reload lost mechanism ordering");
        var beforeCheckbox = form.tracks.ToArray(); var item = form.trackList.GetItemRectangle(1);
        form.trackList.DragReorderForTest(new Point(5, item.Top + item.Height / 2), new Point(5, 2));
        Assert(form.tracks.SequenceEqual(beforeCheckbox), "Checkbox gesture reordered mechanisms");
        source = form.trackList.GetItemRectangle(0);
        form.trackList.ClickAt(new Point(45, source.Top + 10)); form.trackList.BeginReorder(new Point(45, source.Top + 10)); form.trackList.CancelReorder();
        Assert(form.tracks.SequenceEqual(beforeCheckbox), "Cancelled drag changed ordering");
        form.SetCompact(true); form.SetCompact(false);
        using var image = new Bitmap(form.Width, form.Height); form.DrawToBitmap(image, new Rectangle(Point.Empty, form.Size));
        image.Save(Path.Combine(AppContext.BaseDirectory, "toolbar-minimum-preview.png"));
        form.Close();
    }
}
