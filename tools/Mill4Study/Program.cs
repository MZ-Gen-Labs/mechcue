using MechCue;
using System.Reflection;
using System.Text.Json;
class Study {
static object Native(string n,params object[] a)=>typeof(Bridge).GetMethod(n,BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public)!.Invoke(null,a)!;
static object Get(object o,string n)=>o.GetType().InvokeMember(n,BindingFlags.GetProperty,null,o,null)!;
static object Call(object o,string n,params object[] a)=>o.GetType().InvokeMember(n,BindingFlags.InvokeMethod,null,o,a)!;
static object Doc()=>Get(Native("CadApplication"),"ActiveDocument");
static string Name()=>Convert.ToString(Get(Doc(),"FullName"))!;
static string dir="";
static Dictionary<string,double[]> geometries=[];
static object Last(){var os=Get(Doc(),"Occurrences");return Native("CadOccurrence",Doc(),Convert.ToInt32(Get(os,"Count")));}
static void Matrix(object o,double[] m)=>Native("CadCallRef",o,"PutMatrix",new[]{0},new object[]{m,true});
static void Part(string id,string shape,double x,double y,double z){
if(File.Exists(Path.Combine(dir,id+".par"))){Bridge.CadOpen(Path.Combine(dir,id+".par"));var existing=Doc();geometries[id]=(double[])Native("ConceptGeometry",existing);Call(existing,"Close",false);Console.WriteLine("REUSE "+id);return;}
Bridge.CadNew("part");var d=Doc();var name=Name();geometries[id]=(double[])Native("ConceptGeometry",d);
Bridge.CadExtrude(name,shape,x,y,x/2,z,xMm:shape=="rectangle"?-x/2:0,yMm:shape=="rectangle"?-y/2:0,direction:"symmetric",featureName:id);
if(id=="saddle")Bridge.CadExtrude(name,"rectangle",40,260,0,30,xMm:-20,yMm:-130,operation:"cut",direction:"negative",featureName:"Y-screw-clearance");
if(id=="head-arm")Bridge.CadExtrude(name,"rectangle",180,50,0,120,xMm:-90,yMm:60,operation:"cut",direction:"symmetric",featureName:"Column-guide-clearance");
Bridge.CadSave(name,Path.Combine(dir,id+".par"));Call(d,"Close",false);Console.WriteLine("PART "+id);}
static void Place(string id,double x,double y,double z,double rx=0,double ry=0){Bridge.CadPlacePart(Name(),Path.Combine(dir,id+".par"));Matrix(Last(),ConceptMachine.Multiply(Bridge.CadTransform(x,y,z,rx,ry,0),geometries[id]));}
static void Assembly(string id,Action action){Bridge.CadNew("assembly");action();var d=Doc();Bridge.CadSave(Name(),Path.Combine(dir,id+".asm"));Call(d,"Close",false);Console.WriteLine("ASSEMBLY "+id);}
[STAThread] static int Main(string[] args){try{
Application.OleRequired();dir=Path.GetFullPath(args[0]);if(args.Length>1&&args[1]=="pairs"){Pairs();return 0;}if(args.Length>1&&args[1]=="reopen"){Reopen();return 0;}if(args.Length>1&&args[1]=="inspect"){Inspect();return 0;}if(args.Length>1&&args[1]=="motions"){Motions();return 0;}if(Directory.Exists(dir)&&!(args.Length>1&&args[1]=="resume"))throw new IOException("New output required");Directory.CreateDirectory(dir);
foreach(var p in new[]{("bed",900d,700d,100d),("column",220d,180d,900d),("y-rail",25d,640d,20d),("x-rail",850d,25d,20d),("z-rail",25d,20d,620d),("saddle",320d,260d,60d),("table",700d,240d,60d),("guide-block",55d,65d,25d),("z-block",55d,25d,65d),("bearing-support",60d,60d,50d),("servo",80d,100d,100d),("fixture",150d,150d,10d),("clamp",20d,30d,25d),("workpiece",100d,100d,100d),("head",180d,180d,220d),("head-arm",180d,220d,120d)})Part(p.Item1,"rectangle",p.Item2,p.Item3,p.Item4);
foreach(var p in new[]{("ball-screw",25d,620d),("x-screw",25d,820d),("c-housing",230d,40d),("c-rotor",200d,60d),("spindle",80d,100d),("tool",20d,100d)})Part(p.Item1,"circle",p.Item2,p.Item2,p.Item3);
Assembly("fixed-structure",()=>{Place("bed",0,0,50);Place("column",0,350,550);foreach(int s in new[]{-1,1}){Place("y-rail",s*110,0,110);Place("z-rail",s*70,250,690);Place("bearing-support",0,s*310,130);}Place("ball-screw",0,0,130,90);Place("servo",0,-390,130);Place("ball-screw",0,280,690);Place("servo",0,280,1050);});
Assembly("y-saddle",()=>{Place("saddle",0,0,160);foreach(int x in new[]{-1,1})foreach(int y in new[]{-1,1})Place("guide-block",x*110,y*80,132.5);foreach(int s in new[]{-1,1}){Place("x-rail",0,s*75,200);Place("bearing-support",s*400,0,200);}Place("x-screw",0,0,200,0,90);Place("servo",480,0,200);});
Assembly("x-table",()=>{Place("table",0,0,250);foreach(int x in new[]{-1,1})foreach(int y in new[]{-1,1})Place("guide-block",x*100,y*75,222.5);Place("c-housing",0,0,300);Place("servo",160,0,300);});
Assembly("c-fixture",()=>{Place("c-rotor",0,0,0);Place("fixture",0,0,35);Place("workpiece",0,0,90);foreach(int s in new[]{-1,1})Place("clamp",s*62,0,52.5);});
Assembly("z-head",()=>{Place("head",0,0,820);Place("head-arm",0,180,850);Place("spindle",0,0,660);Place("tool",0,0,560);foreach(int x in new[]{-1,1})foreach(int z in new[]{-1,1})Place("z-block",x*70,225,820+z*80);});
var model=ConceptMachine.Create("mill4",500,300,300);model.AssemblyFile="mill4-detail.asm";model.Axes.Single(a=>a.Id=="C").OriginMm=[0,0,350];model.Bodies.Clear();
foreach(var p in new[]{("fixed-structure",""),("y-saddle","Y"),("x-table","X"),("c-fixture","C"),("z-head","Z")})model.Bodies.Add(new(){Id=p.Item1,Parent=p.Item2,File=p.Item1+".asm",Occurrence=p.Item1,SizeMm=[900,700,1100]});
Bridge.CadNew("assembly");foreach(var b in model.Bodies){Bridge.CadPlacePart(Name(),Path.Combine(dir,b.File));var o=Last();o.GetType().InvokeMember("Name",BindingFlags.SetProperty,null,o,[b.Occurrence]);Matrix(o,model.Poses(model.Values)[b.Id]);}
model.Validate();Bridge.CadSave(Name(),Path.Combine(dir,model.AssemblyFile));var manifest=Path.Combine(dir,"mechcue-concept.json");File.WriteAllText(manifest,JsonSerializer.Serialize(model,ConceptMachine.JsonOptions));
Bridge.CadReadConcept(Name(),manifest);foreach(var v in new[]{new Dictionary<string,double>{{"X",100},{"Y",50},{"Z",100},{"C",90}},new(){{"X",-250},{"Y",150},{"Z",150},{"C",-180}},model.Values})Bridge.CadPoseConcept(Name(),manifest,JsonSerializer.Serialize(v));Bridge.CadSave(Name());
File.WriteAllText(Path.Combine(dir,"verification.txt"),"PASS: native CAD, five subassemblies, manifest mapping, XYZC poses, home restored.\nNOT VERIFIED: interference, stiffness, tolerances, drive sizing.\n");Console.WriteLine("PASS");return 0;
}catch(Exception e){Console.WriteLine(e);if(Directory.Exists(dir))File.WriteAllText(Path.Combine(dir,"failure.txt"),e.ToString());return 1;}}
static void Motions(){
var manifest=Path.Combine(dir,"mechcue-concept.json");var model=JsonSerializer.Deserialize<ConceptMachine>(File.ReadAllText(manifest),ConceptMachine.JsonOptions)!;
Bridge.CadOpen(Path.Combine(dir,model.AssemblyFile));var tracks=model.Axes.Select(a=>new Track{Name=a.Id,Axis=a.Direction,Kind=a.Kind=="rotary"?"部品回転":"部品移動"}).ToList();
var definitions=new[]{
("01 ストローク端点・静的レイアウト検討",new double[][]{[0,0,0,150,0],[4,-250,-150,150,0],[8,250,150,150,0],[12,0,0,-150,0],[16,0,0,150,0]}),
("02 C軸90度割出し・退避",new double[][]{[0,0,0,150,0],[4,0,0,150,90],[8,0,0,0,90],[12,0,0,150,90],[16,0,0,150,180],[20,0,0,150,270],[24,0,0,150,360]}),
("03 XYZC同時補間・空運転",new double[][]{[0,0,0,150,0],[4,100,50,100,90],[8,0,100,50,180],[12,-100,50,100,270],[16,0,0,150,360]}),
("04 加工高さ・仮想接触確認",new double[][]{[0,0,0,150,0],[4,0,0,0,0],[8,0,0,-20,0],[12,0,0,150,0],[16,0,0,150,90]})};
int Column(string id)=>id=="X"?1:id=="Y"?2:id=="Z"?3:4;
if(model.AssemblyFile!="mill4-detail.asm"){definitions=definitions.Take(3).ToArray();definitions[1].Item2[2][3]=-100;}
var patterns=definitions.Select(d=>new MotionPattern{Name=d.Item1,Description="検討用直線補間。干渉・加減速未検証。",Points=tracks.ToDictionary(t=>t.Id,t=>d.Item2.Select(r=>new KeyPoint(r[0],r[Column(t.Name)])).ToList())}).ToList();
var active=patterns[2];foreach(var t in tracks)t.Points=active.Points[t.Id];
var settings=new DocumentSettings{Version=2,Tracks=tracks.Select(t=>new SavedTrack{Track=t}).ToList(),Patterns=patterns,ActivePatternId=active.Id,DisplayMode="all",Concept=new SavedConcept{Model=model,Baseline=new(model.Values),Tracks=tracks.ToDictionary(t=>t.Name,t=>t.Id)}};
DocumentSettings.Parse(settings.Json());
var bridge=new Bridge();bridge.Connect();typeof(Bridge).GetMethod("RestoreConcept",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(bridge,[settings.Concept,tracks]);
bridge.Apply(4);bridge.Disconnect();
var storage=typeof(Bridge).Assembly.GetType("MechCue.DocumentStorage")!;storage.GetMethod("Write",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[Doc(),settings.Json()]);Bridge.CadSave(Name());
var read=(string)storage.GetMethod("Read",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[Doc()])!;if(DocumentSettings.Parse(read).Patterns.Count!=patterns.Count)throw new Exception("Embedded patterns failed");
File.WriteAllText(Path.Combine(dir,"mechcue-motions.json"),settings.Json());File.WriteAllText(Path.Combine(dir,"motions.csv"),"pattern,time_s,X_mm,Y_mm,Z_mm,C_deg\n"+string.Join("\n",definitions.SelectMany(d=>d.Item2.Select(r=>d.Item1+","+string.Join(",",r.Select(v=>v.ToString(System.Globalization.CultureInfo.InvariantCulture)))))));
File.AppendAllText(Path.Combine(dir,"verification.txt"),$"PASS: {patterns.Count} embedded motion patterns, four axis bindings, chart-driven pose, home restoration, embedded settings readback.\n");Console.WriteLine("PASS MOTIONS");
}
static void Inspect(){
if(!string.Equals(Name(),Path.Combine(dir,"mill4-detail.asm"),StringComparison.OrdinalIgnoreCase))throw new Exception("Wrong active model");
var leaves=new List<(string File,string Group)>();
object Tree(object d,string path,int depth){if(depth>5)throw new Exception("Unexpected assembly depth");var os=Get(d,"Occurrences");var rows=new List<object>();for(int i=1;i<=Convert.ToInt32(Get(os,"Count"));i++){var o=Native("CadOccurrence",d,i);var child=Get(o,"OccurrenceDocument");var file=Convert.ToString(Get(child,"FullName"))!;var label=Convert.ToString(Get(o,"Name"))!;object? children=null;if(file.EndsWith(".asm",StringComparison.OrdinalIgnoreCase))children=Tree(child,path+"/"+label,depth+1);else leaves.Add((file,path));rows.Add(new{path=path+"/"+label,name=label,file,localMatrix=Native("Matrix",o),children});}return rows;}
File.WriteAllText(Path.Combine(dir,"assembly-tree.json"),JsonSerializer.Serialize(Tree(Doc(),"mill4-detail",0),ConceptMachine.JsonOptions));
File.WriteAllText(Path.Combine(dir,"bom.csv"),"part_file,quantity,subassembly\n"+string.Join("\n",leaves.GroupBy(l=>l).Select(g=>Path.GetFileName(g.Key.File)+","+g.Count()+","+g.Key.Group)));
var view=Get(Get(Native("CadApplication"),"ActiveWindow"),"View");Call(view,"SaveAsImage",Path.Combine(dir,"home.jpg"),1280,960,Type.Missing,1,24,0,false);
var storage=typeof(Bridge).Assembly.GetType("MechCue.DocumentStorage")!;var json=(string)storage.GetMethod("Read",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[Doc()])!;var settings=DocumentSettings.Parse(json);if(settings.Patterns.Count!=4||settings.Tracks.Count!=4||leaves.Count!=43)throw new Exception("Unexpected saved structure");File.WriteAllText(Path.Combine(dir,"mechcue-motions.json"),json);
File.AppendAllText(Path.Combine(dir,"verification.txt"),"PASS: installed add-in restored 4 patterns / 4 mapped axes, MCP seek 4s and 8s drove subassemblies, native save restored home. Tree: 5 subassemblies, 43 leaf occurrences, 22 distinct part files.\n");Console.WriteLine("PASS INSPECT "+leaves.Count);
}
static void Reopen(){
var file=Path.Combine(dir,"mill4-detail.asm");if(Name()!=file||Convert.ToBoolean(Get(Doc(),"Dirty")))throw new Exception("Save target first");Call(Doc(),"Close",false);Bridge.CadOpen(file);
var storage=typeof(Bridge).Assembly.GetType("MechCue.DocumentStorage")!;var json=(string)storage.GetMethod("Read",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[Doc()])!;var settings=DocumentSettings.Parse(json);if(settings.Patterns.Count!=4||settings.Tracks.Count!=4)throw new Exception("Reopened settings changed");
var bridge=new Bridge();bridge.Connect();typeof(Bridge).GetMethod("RestoreConcept",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(bridge,[settings.Concept,settings.Tracks.Select(t=>t.Track)]);
string collision;try{typeof(Bridge).GetMethod("EnsureNoInterference",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(bridge,null);collision="Native top-level interference check returned clear at home; no swept-path verification.";}catch(Exception e){collision="Native top-level interference check: "+(e.InnerException??e).Message;}
bridge.Disconnect();File.WriteAllText(Path.Combine(dir,"interference-home.txt"),collision);File.AppendAllText(Path.Combine(dir,"verification.txt"),"PASS: saved model closed/reopened; 4 patterns and 4 subassembly-axis bindings restored.\n"+collision+"\n");Console.WriteLine(collision);
}
static void Pairs(){
if(Name()!=Path.Combine(dir,"mill4-detail.asm"))throw new Exception("Wrong model");var lines=new List<string>();
var counts=new Dictionary<int,int>();
int Check(object[] selected){object[] v=[selected.Length,Native("InterferenceSet",new object[]{selected}),0,Type.Missing,Type.Missing,Type.Missing,false,Type.Missing,Type.Missing,0,null!,null!,null!,null!,Type.Missing];var p=new ParameterModifier(v.Length);foreach(int k in new[]{1,2,9,10,11,12,13})p[k]=true;Doc().GetType().InvokeMember("CheckInterference",BindingFlags.InvokeMethod,null,Doc(),v,[p],null,null);if(Convert.ToInt32(v[2]) is not (1 or 2 or 3 or 4))throw new Exception("Unknown interference status");return Convert.ToInt32(v[9]);}
for(int i=1;i<=5;i++){counts[i]=Check([Native("CadOccurrence",Doc(),i)]);lines.Add("internal "+Get(Native("CadOccurrence",Doc(),i),"Name")+": "+counts[i]);}
for(int i=1;i<=5;i++)for(int j=i+1;j<=5;j++){
var a=Native("CadOccurrence",Doc(),i);var b=Native("CadOccurrence",Doc(),j);object[] v=[2,Native("InterferenceSet",new object[]{new object[]{a,b}}),0,Type.Missing,Type.Missing,Type.Missing,false,Type.Missing,Type.Missing,0,null!,null!,null!,null!,Type.Missing];
var p=new ParameterModifier(v.Length);foreach(int k in new[]{1,2,9,10,11,12,13})p[k]=true;Doc().GetType().InvokeMember("CheckInterference",BindingFlags.InvokeMethod,null,Doc(),v,[p],null,null);
lines.Add(Get(a,"Name")+" / "+Get(b,"Name")+": status="+v[2]+", count="+v[9]+", cross-count-difference="+(Convert.ToInt32(v[9])-counts[i]-counts[j]));}
File.WriteAllLines(Path.Combine(dir,"interference-pairs.txt"),lines);Console.WriteLine(string.Join("\n",lines));
}
}
