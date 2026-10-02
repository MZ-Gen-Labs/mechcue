using System.Text.Json;
namespace MechCue;

public sealed partial class Bridge
{
    internal static (object Occurrence,object Parent) ResolveOccurrenceKeyPath(object document,string keyPath)
    {
        var keys=keyPath.Split('/',StringSplitOptions.RemoveEmptyEntries);
        if(!keyPath.StartsWith('/')||keys.Length is <1 or >32)throw new ArgumentException("Use native keyPath from get_assembly_tree");
        object parent=document,occurrence=document;
        for(int index=0;index<keys.Length;index++)
        {
            string hex=keys[index];
            byte[] key=Convert.FromHexString(hex);if(key.Length==0)throw new ArgumentException("Empty reference key");
            object[] args=[key,null!];
            if(System.Runtime.InteropServices.Marshal.IsComObject(parent))
            {Array nativeKey=key;((IAssemblyKeyResolver)parent).BindKeyToObject(ref nativeKey,out var resolved);args[1]=resolved;}
            else CadCallRef(parent,"BindKeyToObject",[0,1],args);
            occurrence=args[1]??throw new InvalidOperationException("Occurrence reference no longer resolves");
            if(index<keys.Length-1)
            {
                if(!Convert.ToBoolean(Get(occurrence,"Subassembly")))throw new InvalidOperationException("Key path traverses a non-assembly");
                parent=Get(occurrence,"OccurrenceDocument");
            }
        }
        return (occurrence,parent);
    }
    public static object CadPositionNested(string expectedDocument,string keyPath,double xMm,double yMm,double zMm,double rxDeg,double ryDeg,double rzDeg,bool modifySharedSubassembly)
    {
        var desired=CadTransform(xMm,yMm,zMm,rxDeg,ryDeg,rzDeg);
        var root=CadDocument(CadApplication(),expectedDocument,".asm");
        if(keyPath.Split('/',StringSplitOptions.RemoveEmptyEntries).Length>1&&!modifySharedSubassembly)throw new InvalidOperationException("Nested edits modify a shared subassembly file. Explicitly set modifySharedSubassembly=true.");
        var (occurrence,parent)=ResolveOccurrenceKeyPath(root,keyPath);
        if(Convert.ToBoolean(Get(parent,"ReadOnly")))throw new InvalidOperationException("Parent subassembly is read-only");
        var original=Matrix(occurrence);
        try
        {
            CadCallRef(occurrence,"PutMatrix",[0],desired,true);
            if(Matrix(occurrence).Zip(desired).Any(p=>Math.Abs(p.First-p.Second)>1e-8))throw new InvalidOperationException("Assembly solver rejected requested pose");
        }
        catch(Exception error)
        {
            try {CadCallRef(occurrence,"PutMatrix",[0],original,true);if(Matrix(occurrence).Zip(original).Any(p=>Math.Abs(p.First-p.Second)>1e-8))throw new InvalidOperationException("Original pose not retained");}
            catch(Exception rollback){throw new InvalidOperationException("Pose failed: "+(error.InnerException??error).Message+"; ROLLBACK FAILED: "+(rollback.InnerException??rollback).Message);}
            throw new InvalidOperationException("Pose failed; original pose restored: "+(error.InnerException??error).Message);
        }
        return new {document=expectedDocument,keyPath,modifiedDocument=CadName(parent),localMatrix=Matrix(occurrence),sharedDocumentEdit=keyPath.Count(c=>c=='/')>1,saved=false};
    }
    public static object CadSetVariables(string expectedDocument,string variablesJson)
    {
        var document=CadDocument(CadApplication(),expectedDocument);
        using var parsed=JsonDocument.Parse(variablesJson);var edits=parsed.RootElement.EnumerateArray().ToArray();
        if(edits.Length is <1 or >128)throw new ArgumentException("Supply 1–128 distinct variables");
        var list=Call(Get(document,"Variables"),"Query","*",Type.Missing,Type.Missing,Type.Missing);
        var all=Enumerable.Range(1,Convert.ToInt32(Get(list,"Count"))).Select(i=>GetItem(list,i)).ToArray();
        var changes=new List<(object Variable,string Name,double Original,double Value)>();var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var edit in edits)
        {
            string name=edit.GetProperty("name").GetString()!;if(!names.Add(name))throw new ArgumentException("Duplicate variable: "+name);
            var variable=all.Single(v=>string.Equals(Convert.ToString(Get(v,"Name")),name,StringComparison.OrdinalIgnoreCase));
            double value=edit.GetProperty("value").GetDouble();CadFinite(value,"value");
            int type=Convert.ToInt32(Get(variable,"UnitsType"));string unit=edit.GetProperty("unit").GetString()!;
            value=unit switch {"native"=>value,"mm" when type==1=>value/1000,"deg" when type==2=>value*Math.PI/180,_=>throw new ArgumentException("Unit incompatible with variable "+name+" (native unitsType="+type+")")};
            changes.Add((variable,name,Convert.ToDouble(Get(variable,"Value")),value));
        }
        var assigned=new List<(object Variable,string Name,double Original,double Value)>();
        try
        {
            foreach(var change in changes){assigned.Add(change);Set(change.Variable,"Value",change.Value);}
            foreach(var change in changes)if(Math.Abs(Convert.ToDouble(Get(change.Variable,"Value"))-change.Value)>1e-9)throw new InvalidOperationException("Variable did not retain value: "+change.Name);
        }
        catch(Exception error)
        {
            var failures=new List<string>();foreach(var change in assigned.AsEnumerable().Reverse())try{Set(change.Variable,"Value",change.Original);if(Math.Abs(Convert.ToDouble(Get(change.Variable,"Value"))-change.Original)>1e-9)failures.Add(change.Name+": value not restored");}catch(Exception ex){failures.Add(change.Name+": "+(ex.InnerException??ex).Message);}
            throw new InvalidOperationException("Variable edit failed: "+(error.InnerException??error).Message+(failures.Count==0?"; previous values restored":"; ROLLBACK FAILED: "+string.Join(" / ",failures)));
        }
        return new {document=expectedDocument,changes=changes.Select(c=>new {name=c.Name,beforeNative=c.Original,afterNative=Convert.ToDouble(Get(c.Variable,"Value"))}),saved=false};
    }
    public static object CadCustomProperties(string expectedDocument)
    {
        var doc=CadDocument(CadApplication(),expectedDocument,write:false);var custom=Call(Get(doc,"Properties"),"Item","Custom");
        var properties=Enumerable.Range(1,Convert.ToInt32(Get(custom,"Count"))).Select(i=>{var p=GetItem(custom,i);return new {name=Convert.ToString(Get(p,"Name")),value=Get(p,"Value")};}).ToArray();
        return new {document=expectedDocument,properties};
    }
    public static object CadSetCustomProperty(string expectedDocument,string name,string value)
    {
        if(string.IsNullOrWhiteSpace(name)||name.Length>128||value.Length>4000)throw new ArgumentException("Property name 1–128 chars; value <=4000 chars");
        var doc=CadDocument(CadApplication(),expectedDocument);var custom=Call(Get(doc,"Properties"),"Item","Custom");
        var matches=Enumerable.Range(1,Convert.ToInt32(Get(custom,"Count"))).Select(i=>GetItem(custom,i)).Where(p=>string.Equals(Convert.ToString(Get(p,"Name")),name,StringComparison.OrdinalIgnoreCase)).ToArray();
        if(matches.Length>1)throw new InvalidOperationException("Duplicate custom property names");
        if(matches.Length==0)Call(custom,"Add",name,value);else Set(matches[0],"Value",value);
        return new {document=expectedDocument,name,value,saved=false};
    }
    public static object CadBom(string expectedDocument)
    {
        var tree=JsonSerializer.SerializeToElement(CadAssemblyTree(expectedDocument));
        var nodes=tree.GetProperty("nodes").Deserialize<CadAssemblyNode[]>()!;
        var rows=nodes.Where(n=>!n.IsAssembly).GroupBy(n=>n.FileName,StringComparer.OrdinalIgnoreCase).Select(g=>new {fileName=g.Key,quantity=g.Count(),occurrenceKeyPaths=g.Select(n=>n.KeyPath).ToArray()}).ToArray();
        return new {document=expectedDocument,rows,leafOccurrenceCount=nodes.Count(n=>!n.IsAssembly),uniquePartCount=rows.Length,truncated=tree.GetProperty("truncated"),warnings=tree.GetProperty("warnings"),scope="As-placed rigid occurrences, not manufacturing BOM. No suppressed/alternate assembly configuration evaluation or material/mass calculation."};
    }
}
