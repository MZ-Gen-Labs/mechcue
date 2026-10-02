using System.Text.Json;
namespace MechCue;

public partial class MainForm
{
    internal void VerifyConceptWorkflowFixes()
    {
        void Assert(bool ok,string message) { if (!ok) throw new Exception(message); }
        string folder = Path.Combine(Path.GetTempPath(), "mechcue-migration-test-" + Guid.NewGuid());
        Directory.CreateDirectory(folder);
        try
        {
            var model = ConceptMachine.Create("mill4",500,300,300);
            var doc = new SelfTest.FakeDocument { FullName = Path.Combine(folder,model.AssemblyFile) };
            var app = new SelfTest.FakeApplication { ActiveDocument = doc }; app.OpenDocuments.Items.Add(doc);
            var poses = model.Poses(model.Values);
            foreach (var body in model.Bodies)
            {
                body.File = body.Id + ".par"; body.Occurrence = body.Id;
                doc.Occurrences.Items.Add(new SelfTest.MigrationPart { Name = body.Id, Pose = poses[body.Id],
                    OccurrenceDocument = new SelfTest.InspectionDocument(Path.Combine(folder,body.File)) });
            }
            string manifest = Path.Combine(folder,"mechcue-concept.json");
            File.WriteAllText(manifest,JsonSerializer.Serialize(model,ConceptMachine.JsonOptions));
            using var imported = new MainForm(hostedApplication:app); imported.Show(); Application.DoEvents();
            var originals = imported.tracks.Select(t=>t.Id).ToArray();
            imported.HandleAi(JsonSerializer.SerializeToElement(new { method="import_concept",args=new { manifestPath=manifest,unboundTracks="hide" } }));
            Assert(originals.All(id=>imported.plot.Hidden.Contains(imported.tracks.Single(t=>t.Id==id))),"Import hides unassigned samples without deleting them.");
            Assert(imported.bridge.BindingCount==4,"Four concept axes imported.");
            var first = imported.CaptureDocumentSettings();
            imported.ImportConceptAxes(manifest,"hide");
            Assert(imported.CaptureDocumentSettings().Json()==first.Json(),"Repeat import preserves all graph settings.");
            imported.CreatePattern("Second",true);
            var source = imported.CaptureDocumentSettings(); string json=source.Json();
            imported.Close();
            // A new destination document has the same axes and rigid placements, but no stored settings.
            var target = new SelfTest.FakeDocument { FullName=doc.FullName };
            foreach(var occurrence in doc.Occurrences.Items) target.Occurrences.Items.Add(occurrence);
            app.OpenDocuments.Items.Remove(doc); app.OpenDocuments.Items.Add(target); app.ActiveDocument=target;
            using var detail = new MainForm(hostedApplication:app); detail.Show(); Application.DoEvents();
            detail.HandleAi(JsonSerializer.SerializeToElement(new { method="migrate_concept",args=new { expectedDocument=target.FullName,manifestPath=manifest,settingsJson=json,hideUnboundTracks=true } }));
            Assert(detail.restorationWarnings.Count==0 && detail.bridge.BindingCount==4 && !detail.live.Checked,"Migration restores mappings without warnings or reflection.");
            var saved=detail.CaptureDocumentSettings();
            Assert(JsonSerializer.Serialize(saved.Patterns)==JsonSerializer.Serialize(source.Patterns),"All patterns and points preserved by live migration.");
            Assert(saved.Tracks.Select(t=>t.Track.Id).SequenceEqual(source.Tracks.Select(t=>t.Track.Id)),"Track IDs preserved by live migration.");
            bool rejected=false;
            try { detail.MigrateConceptSettings(target.FullName,manifest,json,true); } catch(InvalidOperationException) { rejected=true; }
            Assert(rejected,"Repeat migration never overwrites mapped destination.");
            detail.HandleAi(JsonSerializer.SerializeToElement(new { method="save_document",args=new { expectedDocument=target.FullName } }));
            Assert(!target.Dirty && DocumentSettings.Parse(detail.bridge.ReadSettings()!).Concept!.Tracks.Count==4,"Explicit session save persists migrated mappings.");
            detail.Close();
        }
        finally { Directory.Delete(folder,true); }
    }
}
public static partial class SelfTest
{
    public sealed class MigrationPart : FakePart
    {
        public InspectionDocument OccurrenceDocument { get; set; } = new("part.par");
    }
}
