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
            if(kind=="gantry") {
                Check(model.Axes.Single(a=>a.Id=="Y").Parent=="" && model.Axes.Single(a=>a.Id=="X").Parent=="Y" && model.Axes.Single(a=>a.Id=="Z").Parent=="X","Gantry Y/X/Z hierarchy");
                Check(Math.Abs(moved["bridge"][12]-home["bridge"][12])<1e-10 && Math.Abs(moved["bridge"][13]-home["bridge"][13]-.05)<1e-10,"Bridge must follow Y only, never trolley X");
                Check(Math.Abs(moved["x-carriage"][12]-home["x-carriage"][12]-.1)<1e-10 && Math.Abs(moved["x-carriage"][13]-home["x-carriage"][13]-.05)<1e-10 && moved["x-carriage"][14]==home["x-carriage"][14],"Trolley follows XY only");
                Check(moved["z-guide-left"][14]==home["z-guide-left"][14],"Guide must not follow slide Z");
                foreach(var rail in model.Bodies.Where(b=>b.Id.EndsWith("-y-rail"))) Check(home[rail.Id].SequenceEqual(moved[rail.Id]),"Y rails stay fixed");
                foreach(double x in new[]{-250d,250d}) foreach(double y in new[]{-150d,150d}) foreach(double z in new[]{-150d,150d}) {
                    var pose=model.Poses(new Dictionary<string,double>{{"X",x},{"Y",y},{"Z",z}});
                    var beam=model.Bodies.Single(b=>b.Id=="bridge");
                    Check(Math.Abs(pose["bridge"][12]-home["bridge"][12])<1e-10,"Bridge leaves supports at X limit");
                    foreach(var end in model.Bodies.Where(b=>b.Id.StartsWith("bridge-") && b.Id.EndsWith("-end"))) {
                        var rail=model.Bodies.Single(b=>b.Id==end.Id.Replace("bridge-","").Replace("-end","-y-rail"));
                        Check(Math.Abs(y)+end.SizeMm[1]/2<=rail.SizeMm[1]/2,"Bridge end leaves Y rail at travel limit");
                    }
                    var grip=model.Bodies.Single(b=>b.Id=="gripper"); var slide=model.Bodies.Single(b=>b.Id=="z-slide");
                    Check(Math.Abs((pose["gripper"][14]+grip.SizeMm[2]/2000)-(pose["z-slide"][14]-slide.SizeMm[2]/2000))<1e-10,"Slide/gripper detached");
                    Check(pose["z-slide"][14]+slide.SizeMm[2]/2000>home["x-carriage"][14],"Slide withdrawn from trolley at Z limit");
                }
            }
            if(kind=="mill5") {
                values["A"]=90; values["C"]=90; moved=model.Poses(values);
                Check(Math.Abs(moved["workpiece"][13]-(.05-.08))<1e-10 && Math.Abs(moved["workpiece"][14]-.35)<1e-10,"A pivot and child C preserve parent-local rotation");
                measured=Bridge.MeasureConcept(model,moved);Check(values.All(p=>Math.Abs(measured[p.Key]-p.Value)<1e-7),"Rotated concept measurement");
                Check(Math.Abs(moved["workpiece"][2]-1)<1e-10,"Nested A/C orientation");
            }
            // No two bodies may overlap at any combination of linear travel limits (rotary axes at 0).
            // Touching faces (stacked bodies) are allowed.
            var linear=model.Axes.Where(a=>a.Kind=="linear").ToList();
            for(int mask=0;mask<1<<linear.Count;mask++) {
                var limits=model.Values.ToDictionary(p=>p.Key,_=>0d);
                for(int i=0;i<linear.Count;i++) limits[linear[i].Id]=(mask&(1<<i))==0?linear[i].Minimum:linear[i].Maximum;
                var pose=model.Poses(limits);
                double[] Box(ConceptBody b) {
                    var m=pose[b.Id];var corners=Enumerable.Range(0,8).Select(k=>new[]{((k&1)==0?-1:1)*b.SizeMm[0]/2000,((k&2)==0?-1:1)*b.SizeMm[1]/2000,((k&4)==0?-1:1)*b.SizeMm[2]/2000})
                        .Select(c=>Enumerable.Range(0,3).Select(r=>m[r]*c[0]+m[4+r]*c[1]+m[8+r]*c[2]+m[12+r]).ToArray()).ToList();
                    return [..Enumerable.Range(0,3).Select(r=>corners.Min(c=>c[r])),..Enumerable.Range(0,3).Select(r=>corners.Max(c=>c[r]))];
                }
                var boxes=model.Bodies.ToDictionary(b=>b.Id,Box);
                foreach(var a in model.Bodies) foreach(var b in model.Bodies.SkipWhile(x=>x!=a).Skip(1)) {
                    var p=boxes[a.Id];var q=boxes[b.Id];
                    Check(!Enumerable.Range(0,3).All(r=>Math.Min(p[r+3],q[r+3])-Math.Max(p[r],q[r])>1e-7),
                        $"{kind}: {a.Id} overlaps {b.Id} at {string.Join(",",limits.Select(l=>l.Key+"="+l.Value))}");
                }
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
                // Every body is driven by named variables and stays centred when a size changes.
                foreach(var b in model.Bodies) {
                    var part=Get(CadOccurrence(document,model.Bodies.IndexOf(b)+1),"OccurrenceDocument");var variables=Get(part,"Variables");
                    var names=ConceptVariableNames(b).Distinct().ToArray();
                    var list=Call(variables,"Query","*",Type.Missing,Type.Missing,Type.Missing);
                    var found=Enumerable.Range(1,Convert.ToInt32(Get(list,"Count"))).Select(i=>GetItem(list,i)).ToDictionary(v=>Convert.ToString(Call(variables,"GetName",v))!,v=>Convert.ToDouble(Get(v,"Value"))*1000);
                    foreach(var (name,index) in names.Select(n=>(n,Array.IndexOf(ConceptVariableNames(b),n))))
                        if(!found.TryGetValue(name,out double mm)||Math.Abs(mm-b.SizeMm[index])>.001)throw new Exception($"Concept variable {kind}/{b.Id}/{name} missing or wrong");
                }
                {
                    var probe=model.Bodies.First(b=>b.Shape=="box");var part=Get(CadOccurrence(document,model.Bodies.IndexOf(probe)+1),"OccurrenceDocument");var variables=Get(part,"Variables");
                    var resized=new ConceptBody{Id=probe.Id,Shape=probe.Shape,SizeMm=[probe.SizeMm[0]+37,probe.SizeMm[1]-23,probe.SizeMm[2]+11],GeometryMatrix=probe.GeometryMatrix};
                    foreach(var (name,i) in new[]{("SizeX",0),("SizeY",1),("SizeZ",2)})Call(variables,"Edit",name,resized.SizeMm[i].ToString("R",System.Globalization.CultureInfo.InvariantCulture)+" mm");
                    CadConceptVerifyBody(part,resized);
                    foreach(var (name,i) in new[]{("SizeX",0),("SizeY",1),("SizeZ",2)})Call(variables,"Edit",name,probe.SizeMm[i].ToString("R",System.Globalization.CultureInfo.InvariantCulture)+" mm");
                    CadConceptVerifyBody(part,probe);
                }
                checks.Add("PASS: "+kind+" bodies are centred, driven by named variables, and stay centred when resized");
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
                var witness=model.Bodies.First(b=>b.Parent=="X");var expectedX=new Dictionary<string,double>(values){["X"]=150};
                if(Math.Abs(Matrix(CadOccurrence(document,model.Bodies.IndexOf(witness)+1))[12]-model.Poses(expectedX)[witness.Id][12])>1e-8)throw new Exception("Chart X movement failed");
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
