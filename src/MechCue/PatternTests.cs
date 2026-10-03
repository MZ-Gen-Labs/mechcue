using System.Text.Json;
namespace MechCue;
public partial class MainForm
{
    internal void VerifyMotionPatterns()
    {
        void Assert(bool value,string message) { if(!value) throw new Exception(message); }
        using var form = new MainForm(); form.Show(); Application.DoEvents();
        Assert(form.patternChoice.Parent!.Bottom <= form.legendRow.Top && form.legendRow.Bottom <= form.plot.Top,"Pattern header overlaps chart or legend");
        var original = form.activePattern; var track = form.Current;
        form.grid.Rows[1].Cells[1].Value = 123d;
        form.speed.Value = 2; form.loop.Checked = true; form.collision.Checked = true;
        var second = form.CreatePattern("Collision demo",true);
        Assert(ReferenceEquals(track,form.Current) && form.Current.Points[1].Value == 123,"Duplicate lost shared track or editor changes");
        form.grid.Rows[1].Cells[1].Value = 456d; form.speed.Value = 3; form.loop.Checked = false; form.collision.Checked = false;
        form.StartPlayback(); form.SwitchPattern(original);
        Assert(!form.timer.Enabled && !form.live.Checked && form.time.Value == 0,"Switch must stop and rewind safely");
        Assert(form.Current.Points[1].Value == 123 && form.speed.Value == 2 && !form.loop.Checked && !form.collision.Checked,"Original pattern changed through duplicate or playback selections were reset");
        form.SwitchPattern(second.Id); Assert(form.Current.Points[1].Value == 456 && form.speed.Value == 3 && !form.collision.Checked,"Duplicate edits did not survive switching");
        form.time.Value = 1; var constant = form.CreatePattern("Constant",false);
        Assert(form.Current.Points.Select(p=>p.Value).Distinct().Count() == 1,"New pattern is not constant");
        var added = new Track { Name="Added axis", Points=[new(0,17),new(4,17)] }; form.tracks.Add(added); form.RefreshTracks(0,false);
        form.SwitchPattern(original); Assert(form.tracks.Single(t=>t.Id==added.Id).Points.All(p=>p.Value==17),"New axis not synchronized to other patterns");
        bool rejected=false; try { form.RenamePattern(original,"Collision demo"); } catch(InvalidOperationException) { rejected=true; } Assert(rejected,"Duplicate pattern name accepted");
        form.ImportTableData([new Track { Id=track.Id, Name=track.Name, Kind=track.Kind, Axis=track.Axis, Points=[new(0,11),new(4,11)] }]);
        Assert(form.patterns.Count==3 && form.tracks.Count==4,"Table import discarded patterns or unlisted shared tracks");
        form.SwitchPattern(second.Id); Assert(form.Current.Points[1].Value==456,"Table import changed another pattern"); form.SwitchPattern(original);
        var saved = DocumentSettings.Parse(form.CaptureChartPatterns().Json()); Assert(saved.Version==2,"Pattern file schema must prevent older apps discarding patterns");
        using var reopened = new MainForm(); reopened.RestoreDocumentSettings(saved);
        Assert(reopened.patterns.Count==3 && reopened.activePattern==original && !reopened.live.Checked,"All-pattern restore failed");
        reopened.SwitchPattern(second.Id); Assert(reopened.Current.Points[1].Value==456,"Inactive pattern not persisted");
        form.SetCompact(true); Application.DoEvents(); Assert(form.patternChoice.Parent!.Bottom <= form.legendRow.Top,"Compact pattern header overlaps legend"); Assert(form.chartHeading.Visible && form.patternChoice.Visible,"Compact pattern header missing"); form.SetCompact(false);
        form.DeletePattern(second.Id); form.DeletePattern(constant.Id);
        rejected=false; try { form.DeletePattern(original); } catch(InvalidOperationException) { rejected=true; } Assert(rejected,"Deleted last pattern");
        var legacy = new DocumentSettings { Tracks = form.tracks.Select(t=>new SavedTrack { Track=t }).ToList() };
        reopened.RestoreDocumentSettings(DocumentSettings.Parse(legacy.Json())); Assert(reopened.patterns.Count==1,"Legacy settings not migrated");
        var invalid = DocumentSettings.Parse(form.CaptureChartPatterns().Json()); invalid.Patterns[0].Points.Remove(track.Id);
        rejected=false; try { DocumentSettings.Parse(invalid.Json()); } catch(InvalidDataException) { rejected=true; } Assert(rejected,"Missing axis accepted in saved pattern");
        var doc = new SelfTest.FakeDocument(); var app = new SelfTest.FakeApplication { ActiveDocument=doc }; app.OpenDocuments.Items.Add(doc); doc.Relations3d.Items.Add(new SelfTest.FakeRelation());
        using var bound = new MainForm(hostedApplication:app); bound.Show(); Application.DoEvents();
        bound.bridge.Bind(bound.Current,bound.bridge.Targets(bound.Current.Kind).First()); var label=bound.bridge.BoundLabel(bound.Current);
        bound.VerifyPlaybackSelections((SelfTest.FakeRelation)doc.Relations3d.Items[0]);
        var bfirst=bound.activePattern; var copy=bound.CreatePattern("Bound copy",true); bound.SwitchPattern(bfirst);
        Assert(bound.bridge.BindingCount==1 && bound.bridge.BoundLabel(bound.Current)==label,"Switch lost assignment");
        bound.WriteDocumentSettings(); bound.Close();
        using var restored = new MainForm(hostedApplication:app); restored.Show(); Application.DoEvents();
        Assert(restored.patterns.Count==2 && restored.bridge.BindingCount==1,"Embedded patterns/target restoration failed");
        restored.SwitchPattern(copy.Id); Assert(restored.bridge.BindingCount==1 && !restored.live.Checked,"Restored inactive pattern lost target"); restored.Close();
        form.Close(); reopened.Dispose();
    }

    void VerifyPlaybackSelections(SelfTest.FakeRelation relation)
    {
        void Assert(bool value, string message) { if (!value) throw new Exception(message); }
        void Selections(Control parent)
        {
            Assert(live.Checked && collision.Checked && loop.Checked, "Playback selections were cleared by a routine command");
            Assert(parent.Controls.GetChildIndex(live) < parent.Controls.GetChildIndex(collision)
                && parent.Controls.GetChildIndex(collision) < parent.Controls.GetChildIndex(loop), "Apply / Collision / Loop order changed");
        }
        live.Checked = true; time.Value = 1;
        double pose = relation.Offset;
        Assert(Math.Abs(pose - .05) < 1e-9, "Apply did not drive the bound distance");
        collision.Checked = loop.Checked = true;
        var first = activePattern;
        var copy = CreatePattern("Selection retention", true);
        Selections(top);
        Assert(time.Value == 0 && relation.Offset == pose, "Pattern rewind wrote an unintended CAD pose");
        SwitchPattern(first); Selections(top);
        SetCompact(true); Selections(compactBar); SetCompact(false); Selections(top);
        Assert(relation.Offset == pose, "Display mode switch wrote a CAD pose");
        ImportTableData([new Track { Id = Current.Id, Name = Current.Name, Kind = Current.Kind, Axis = Current.Axis, Points = [new(0, 10), new(4, 90)] }]);
        Selections(top); Assert(relation.Offset == pose, "Table import wrote a CAD pose");
        SaveToDocument(); Selections(top);
        Assert(relation.Offset == 0, "Save did not restore the reference pose");
        collision.Checked = false;
        time.Value = 1;
        Assert(Math.Abs(relation.Offset - .03) < 1e-9, "CAD reflection did not resume after save");
        pose = relation.Offset;
        try
        {
            using var outer = PauseCadReflection();
            using (PauseCadReflection()) Drive(2);
            Drive(3);
            Assert(relation.Offset == pose && live.Checked, "Nested pause resumed CAD reflection prematurely");
            throw new InvalidOperationException("Simulated command failure");
        }
        catch (InvalidOperationException) { }
        Assert(cadReflectionPauseDepth == 0, "CAD reflection pause leaked after an exception");
        Drive(2); Assert(Math.Abs(relation.Offset - .05) < 1e-9, "CAD reflection did not resume after an exception");
        live.Checked = false; autoApply.Checked = true;
        StartPlayback(); Assert(timer.Enabled && live.Checked && autoApply.Checked, "Automatic Apply did not start bound playback"); PausePlayback();
        SetCompact(true);
        Assert(autoApply.Parent == compactBar && compactBar.Controls.GetChildIndex(autoApply) > compactBar.Controls.GetChildIndex(loop), "Auto must follow Loop in compact mode");
        SetCompact(false); Assert(autoApply.Checked && autoApply.Parent == top, "Mode switch lost Automatic Apply selection");
        // An initial interference-check failure must disable both Apply and Auto
        // and leave the timer stopped, even when Play is invoked through MCP.
        collision.Checked = true; bool failed = false;
        try { StartPlayback(); } catch (InvalidOperationException) { failed = true; }
        Assert(failed && !timer.Enabled && !live.Checked && !autoApply.Checked, "Failed initial pose left Automatic Apply armed");
        collision.Checked = false;
        DeletePattern(copy.Id);
        live.Checked = loop.Checked = collision.Checked = false;
        var deletionRelation = new SelfTest.FakeRelation();
        var deletionDocument = new SelfTest.FakeDocument(); deletionDocument.Relations3d.Items.Add(deletionRelation);
        var deletionApp = new SelfTest.FakeApplication { ActiveDocument = deletionDocument }; deletionApp.OpenDocuments.Items.Add(deletionDocument);
        using var deletion = new MainForm(hostedApplication: deletionApp); deletion.Show(); Application.DoEvents();
        var removedId = deletion.Current.Id; var variant = deletion.CreatePattern("Deletion variant", true);
        deletion.bridge.Bind(deletion.Current, deletion.bridge.Targets(deletion.Current.Kind).First());
        deletion.live.Checked = true; deletion.time.Value = 1;
        Assert(Math.Abs(deletionRelation.Offset - .05) < 1e-9, "Deletion fixture did not drive its bound target");
        deletion.DeleteSelectedTrack(false);
        Assert(deletion.tracks.Count == 2 && deletion.patterns.All(p => !p.Points.ContainsKey(removedId)) && deletion.history.Count == 0, "Default track deletion left pattern or undo references");
        Assert(deletionRelation.Offset == 0 && deletion.bridge.BindingCount == 0 && deletion.live.Checked, "Deleting a bound track must restore and release its target without clearing Apply");
        deletion.SwitchPattern(deletion.patterns.First(p => p.Id != variant.Id).Id);
        DocumentSettings.Parse(deletion.CaptureChartPatterns().Json());
        deletion.DeleteSelectedTrack(false);
        failed = false; try { deletion.DeleteSelectedTrack(false); } catch (InvalidOperationException) { failed = true; }
        Assert(failed && deletion.tracks.Count == 1, "Last-track protection failed");
        deletion.live.Checked = false; deletion.Close();
    }
}
