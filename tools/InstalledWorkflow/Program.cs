using MechCue;
using System.Reflection;
using System.Text.Json;
class Workflow {
static object Native(string name,params object[] args)=>typeof(Bridge).GetMethod(name,BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public)!.Invoke(null,args)!;
static object Get(object obj,string name)=>obj.GetType().InvokeMember(name,BindingFlags.GetProperty,null,obj,null)!;
static object Storage(string name,params object[] args)=>typeof(Bridge).Assembly.GetType("MechCue.DocumentStorage")!.GetMethod(name,BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,args)!;
[STAThread] static int Main(string[] args){try{
Application.OleRequired();string folder=Path.GetFullPath(args[0]),phase=args[1];string concept=Path.Combine(folder,"concept","concept.asm"),detail=Path.Combine(folder,"detail","detailed.asm");
string target=phase=="transfer"?detail:args.Length>2?Path.GetFullPath(args[2]):concept;
var document=Native("CadDocument",Native("CadApplication"),target,".asm",false);
if(Convert.ToBoolean(Get(document,"Dirty")))throw new Exception("Save the verification assembly first.");
if(phase=="snapshot"){
string json=(string)Storage("Read",document);var settings=DocumentSettings.Parse(json);if(settings.Concept?.Tracks.Count!=4)throw new Exception("Expected four saved axis mappings.");
File.WriteAllText(Path.Combine(folder,"concept-settings.json"),json);Console.WriteLine("PASS saved concept charts and four mappings captured");
}else if(phase=="reopen"){
string before=(string)Storage("Read",document);document.GetType().InvokeMember("Close",BindingFlags.InvokeMethod,null,document,[false]);Bridge.CadOpen(target);
document=Native("CadDocument",Native("CadApplication"),target,".asm",false);string after=(string)Storage("Read",document);
if(DocumentSettings.Parse(before).Json()!=DocumentSettings.Parse(after).Json())throw new Exception("Settings changed across reopen.");Console.WriteLine("PASS reopen retained patterns, track IDs, keyframes and mappings");
}else if(phase=="transfer"){
if(Storage("Read",document)!=null)throw new Exception("Refusing to overwrite existing detail chart settings.");
var settings=DocumentSettings.Parse(File.ReadAllText(Path.Combine(folder,"concept-settings.json")));var original=settings.Concept!;
var model=JsonSerializer.Deserialize<ConceptMachine>(File.ReadAllText(Path.Combine(folder,"detail","mechcue-concept.json")),ConceptMachine.JsonOptions)!;
if(JsonSerializer.Serialize(model.Axes,ConceptMachine.JsonOptions)!=JsonSerializer.Serialize(original.Model.Axes,ConceptMachine.JsonOptions))throw new Exception("Axis definitions changed.");
settings.Concept=new(){Model=model,Baseline=new(model.Values),Tracks=new(original.Tracks)};foreach(var entry in settings.Tracks)if(!original.Tracks.Values.Contains(entry.Track.Id))entry.Hidden=true;
string json=settings.Json();DocumentSettings.Parse(json);Bridge.CadReadConcept(detail,Path.Combine(folder,"detail","mechcue-concept.json"));Storage("Write",document,json);Bridge.CadSave(detail);
if((string)Storage("Read",document)!=json)throw new Exception("Migrated settings were overwritten.");File.WriteAllText(Path.Combine(folder,"migration-settings.json"),json);Console.WriteLine("PASS migration retained axis/track/pattern IDs and keyframes");
}else throw new Exception("Unknown phase");return 0;
}catch(Exception error){Console.Error.WriteLine(error.InnerException??error);return 1;}}
}
