namespace MechCue;
public static partial class SelfTest
{
    static void TestConceptMachines()
    {
        void Check(bool condition,string message) { if(!condition) throw new Exception(message); }
        foreach(string kind in new[]{"mill3","mill4","mill5","gantry"}) {
            var model=ConceptMachine.Create(kind,500,300,300); var home=model.Poses(model.Values);
            var values=new Dictionary<string,double>(model.Values) { ["X"]=100,["Y"]=50,["Z"]=20 };
            var moved=model.Poses(values);
            var measured=Bridge.MeasureConcept(model,moved);
            Check(values.All(p=>Math.Abs(measured[p.Key]-p.Value)<1e-7),"Concept current-value measurement");
            Check(home["base"].SequenceEqual(moved["base"]),"Concept base stays fixed");
            string dependent=kind=="gantry"?"gripper":"workpiece";
            Check(Math.Abs(moved[dependent][12]-home[dependent][12]-.1)<1e-10,"Nested X motion");
            Check(Math.Abs(moved[dependent][13]-home[dependent][13]-.05)<1e-10,"Nested Y motion");
            string vertical=kind=="gantry"?"gripper":"tool";
            Check(Math.Abs(moved[vertical][14]-home[vertical][14]-.02)<1e-10,"Z motion");
            if(kind=="mill5") {
                values["A"]=90; values["C"]=90; moved=model.Poses(values);
                Check(Math.Abs(moved["workpiece"][13]-(.05-.08))<1e-10 && Math.Abs(moved["workpiece"][14]-.35)<1e-10,"A pivot and child C preserve parent-local rotation");
                measured=Bridge.MeasureConcept(model,moved);Check(values.All(p=>Math.Abs(measured[p.Key]-p.Value)<1e-7),"Rotated concept measurement");
                Check(Math.Abs(moved["workpiece"][2]-1)<1e-10,"Nested A/C orientation");
            }
            try { values["X"]=251; model.Poses(values); throw new Exception("Travel limit accepted"); } catch(ArgumentException) { }
            values.Remove("X"); try { model.Poses(values); throw new Exception("Missing axis accepted"); } catch(ArgumentException) { }
            string json=System.Text.Json.JsonSerializer.Serialize(model,ConceptMachine.JsonOptions);
            var restored=System.Text.Json.JsonSerializer.Deserialize<ConceptMachine>(json,ConceptMachine.JsonOptions)!;restored.Validate();
            restored.Axes[0].Parent=restored.Axes[^1].Id;
            try { restored.Validate(); throw new Exception("Cycle accepted"); } catch(ArgumentException) { }
        }
    }
}
public sealed partial class Bridge
{
    public static void VerifyConceptInSolidEdge(string outputDirectory, string mcpExecutable = "")
    {
        if(Directory.Exists(outputDirectory)) throw new IOException("Use a new test directory.");
        Directory.CreateDirectory(outputDirectory); var checks=new List<string>(); var owned=new List<object>();
        var application=CadApplication();object? original=null;try{original=Get(application,"ActiveDocument");}catch{}
        string templates="C:\\Siemens\\Solid Edge 2026\\Template\\ISO Metric";
        try {
            foreach(string kind in new[]{"mill3","mill4","mill5","gantry"}) {
                string directory=Path.Combine(outputDirectory,kind);
                CadCreateConcept(kind,directory,partTemplate:Path.Combine(templates,"iso metric part.par"),assemblyTemplate:Path.Combine(templates,"iso metric assembly.asm"));
                var document=Get(application,"ActiveDocument");owned.Add(document);
                string expected=CadName(document),manifest=Path.Combine(directory,"mechcue-concept.json");
                CadReadConcept(expected,manifest);
                var model=System.Text.Json.JsonSerializer.Deserialize<ConceptMachine>(File.ReadAllText(manifest),ConceptMachine.JsonOptions)!;
                // Native solid dimensions, transformed from plane-local geometry to concept XYZ.
                foreach(var b in model.Bodies) {
                    var part=Get(CadOccurrence(document,model.Bodies.IndexOf(b)+1),"OccurrenceDocument");
                    var body=Get(GetItem(Get(part,"Models"),1),"Body"); object[] range=[new double[3],new double[3]];CadCallRef(body,"GetRange",[0,1],range);
                    double[] lo=((Array)range[0]).Cast<object>().Select(Convert.ToDouble).ToArray(), hi=((Array)range[1]).Cast<object>().Select(Convert.ToDouble).ToArray();
                    var corners=new List<double[]>();
                    for(int k=0;k<8;k++) {
                        double[] p=[(k&1)==0?lo[0]:hi[0],(k&2)==0?lo[1]:hi[1],(k&4)==0?lo[2]:hi[2]];
                        corners.Add(Enumerable.Range(0,3).Select(r=>b.GeometryMatrix[r]*p[0]+b.GeometryMatrix[4+r]*p[1]+b.GeometryMatrix[8+r]*p[2]).ToArray());
                    }
                    for(int i=0;i<3;i++) if(Math.Abs((corners.Max(p=>p[i])-corners.Min(p=>p[i]))*1000-b.SizeMm[i])>.01 || Math.Abs(corners.Max(p=>p[i])+corners.Min(p=>p[i]))>1e-5) throw new Exception($"Native geometry mismatch {kind}/{b.Id} axis {i}: {string.Join(",",lo)} to {string.Join(",",hi)}");
                }
                var values=new Dictionary<string,double>(model.Values){["X"]=100,["Y"]=50,["Z"]=30};
                if(kind=="mill5")values["A"]=45;if(kind is "mill4" or "mill5")values["C"]=90;
                CadPoseConcept(expected,manifest,System.Text.Json.JsonSerializer.Serialize(values));
                var wanted=model.Poses(values);
                foreach(var b in model.Bodies)if(!ConceptSame(Matrix(CadOccurrence(document,model.Bodies.IndexOf(b)+1)),wanted[b.Id]))throw new Exception("Native dependent pose mismatch");
                var chartBridge=new Bridge(application,document);var chartTracks=chartBridge.ImportConcept(manifest,[new Track()]);
                if(chartBridge.BindingCount!=model.Axes.Count || chartBridge.ImportConcept(manifest,chartTracks).Count!=chartTracks.Count)throw new Exception("Chart import/duplicate count mismatch");
                foreach(var a in model.Axes){var t=chartTracks.Single(t=>t.Name==a.Id);if(t.Points.Any(p=>Math.Abs(p.Value-values[a.Id])>1e-6))throw new Exception("Chart current value mismatch");}
                chartBridge.Apply(0);
                foreach(var b in model.Bodies)if(!ConceptSame(Matrix(CadOccurrence(document,model.Bodies.IndexOf(b)+1)),wanted[b.Id]))throw new Exception("Chart import jumped");
                var xTrack=chartTracks.Single(t=>t.Name=="X");xTrack.Points=[new(0,100),new(4,150)];chartBridge.Apply(4);
                if(Math.Abs(Matrix(CadOccurrence(document,model.Bodies.FindIndex(b=>b.Parent=="X")+1))[12]-.15)>1e-8)throw new Exception("Chart X movement failed");
                xTrack.Points=[new(0,999),new(4,999)];
                try{chartBridge.Apply(0);throw new Exception("Chart range accepted");}catch(ArgumentException){}
                xTrack.Points=[new(0,100),new(4,150)];
                var settings=new DocumentSettings{Concept=chartBridge.CaptureConcept(),Tracks=chartTracks.Select(t=>new SavedTrack{Track=t}).ToList()};
                DocumentSettings.Parse(settings.Json());chartBridge.PrepareDocumentSave();chartBridge.WriteSettings(settings.Json());chartBridge.SaveDocument();chartBridge.Disconnect();
                if(kind=="mill3") { using var form=new MainForm(hostedApplication:application);form.VerifyConceptUi(manifest); }
                checks.Add("PASS: "+kind+" chart import at current pose, repeat without duplicates, playback/range, embedded settings");
                wanted=model.Poses(values);
                values["X"]=999;
                try{CadPoseConcept(expected,manifest,System.Text.Json.JsonSerializer.Serialize(values));throw new Exception("Travel limit accepted");}catch(ArgumentException){}
                foreach(var b in model.Bodies)if(!ConceptSame(Matrix(CadOccurrence(document,model.Bodies.IndexOf(b)+1)),wanted[b.Id]))throw new Exception("Invalid pose changed geometry");
                try{CadReadConcept("wrong.asm",manifest);throw new Exception("Wrong assembly accepted");}catch(InvalidOperationException){}
                CadPoseConcept(expected,manifest,System.Text.Json.JsonSerializer.Serialize(model.Values));
                Call(document,"Close",false);owned.Remove(document);
                CadOpen(expected);document=Get(application,"ActiveDocument");owned.Add(document);CadReadConcept(expected,manifest);
                var restoredBridge=new Bridge(application,document);var loaded=DocumentSettings.Parse(restoredBridge.ReadSettings()!);restoredBridge.RestoreConcept(loaded.Concept!,loaded.Tracks.Select(t=>t.Track));if(restoredBridge.BindingCount!=model.Axes.Count)throw new Exception("Restored chart assignments missing");restoredBridge.Apply(4);restoredBridge.Disconnect();
                checks.Add("PASS: "+kind+" saved/reopened, native dimensions, dependent axis poses, range/identity rejection");
                Call(document,"Close",false);owned.Remove(document);
            }
            if(mcpExecutable!="") VerifyCadMcp(mcpExecutable,outputDirectory,owned,application,checks,concept:true);
            File.WriteAllLines(Path.Combine(outputDirectory,"result.txt"),checks);
        }catch(Exception error){File.WriteAllText(Path.Combine(outputDirectory,"result.txt"),string.Join("\n",checks)+"\nFAIL: "+error);throw;}
        finally{foreach(var doc in owned.AsEnumerable().Reverse())try{Call(doc,"Close",false);}catch{}if(original!=null)try{Call(original,"Activate");}catch{}}
    }
}
