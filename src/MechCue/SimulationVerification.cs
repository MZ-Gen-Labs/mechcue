using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace MechCue;

public sealed partial class Bridge
{
    static Dictionary<string,string> SimulationRecords(object doc)
    {
        IStorage? storage=null;
        try {
            storage=(IStorage)doc.GetType().InvokeMember("AddInsStorage",BindingFlags.GetProperty,null,doc,["MechCueSimulation",0x10])!;
            string? json=DocumentStorage.ReadStream(storage);
            return json==null?new():JsonSerializer.Deserialize<Dictionary<string,string>>(json)??throw new InvalidDataException("Invalid simulation verification metadata.");
        } catch(Exception ex) when((ex.InnerException??ex).HResult is unchecked((int)0x80030002) or unchecked((int)0x80030003)) {return new();}
        finally {DocumentStorage.Release(storage);}
    }
    static void SimulationRemember(object doc,object study,string fingerprint)
    {
        var records=SimulationRecords(doc);records[SimulationName(study)]=fingerprint;IStorage? storage=null;
        try {
            storage=(IStorage)doc.GetType().InvokeMember("AddInsStorage",BindingFlags.GetProperty,null,doc,["MechCueSimulation",0])!;
            DocumentStorage.WriteStream(storage,JsonSerializer.Serialize(records));
        } finally {DocumentStorage.Release(storage);}
        Set(doc,"Dirty",true);
    }
    static string SimulationFingerprint(object doc,object study)
    {
        var body=Get(GetItem(Get(doc,"Models"),1),"Body");var faces=SimulationFaces(doc);var geometry=new List<object>();
        for(int i=1;i<=Convert.ToInt32(Get(faces,"Count"));i++) {var face=GetItem(faces,i);geometry.Add(new {id=SimulationFaceId(face),range=SimulationRange(face).Select(r=>r.Select(v=>Math.Round(v,6)).ToArray()).ToArray(),area=Math.Round(Convert.ToDouble(Get(face,"Area")),12)});}
        var variables=Call(Get(doc,"Variables"),"Query","*",Type.Missing,Type.Missing,Type.Missing);var values=new List<object>();
        for(int i=1;i<=Convert.ToInt32(Get(variables,"Count"));i++) {var v=GetItem(variables,i);values.Add(new {name=Get(v,"Name"),value=Get(v,"Value")});}
        var conditions=new {loads=SimulationConditions(study,false),constraints=SimulationConditions(study,true)};
        var conditionJson=JsonSerializer.SerializeToElement(conditions);
        foreach(var category in new[]{"loads","constraints"})foreach(var item in conditionJson.GetProperty(category).EnumerateArray())
            if(item.GetProperty("geometryWarning").ValueKind!=JsonValueKind.Null || item.GetProperty("faceIds").GetArrayLength()==0)throw new InvalidOperationException("Cannot verify condition face assignments. Inspect the native study before solving again.");
        var mesh=SimulationOwner(study,"GetMeshOwner");
        object[] meshOptions=[false,0,false,0d,false,0,false,0d,false,0d,false,0d,false,false,false,0d,false,false,0,false,0,0d];
        CadCallRef(mesh,"GetMeshOptions",Enumerable.Range(0,meshOptions.Length).ToArray(),meshOptions);
        var json=JsonSerializer.Serialize(new {schema=1,geometry,volume=Math.Round(Convert.ToDouble(Get(body,"Volume")),12),variables=values,material=SimulationMaterial(doc),conditions,meshSize=SimulationDouble(mesh,"GetMeshSizeValue"),meshOptions,mesherType=SimulationInt(mesh,"GetMesherType")});
        return "v1:"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }
    static bool SimulationResultsCurrent(object doc,object study)
    {
        var records=SimulationRecords(doc);
        return records.TryGetValue(SimulationName(study),out string? hash)&&hash==SimulationFingerprint(doc,study);
    }
    static void SimulationRequireModelEnvironment()
    {
        if(Convert.ToString(Get(CadApplication(),"ActiveEnvironment"))?.StartsWith("FEAResults",StringComparison.Ordinal)==true)
            throw new InvalidOperationException("Close the Solid Edge Simulation Results environment before creating a new study. Use its Return/Close Results command, then retry. No study was created.");
    }
    static void SimulationCheckStudySwitch(object doc,object study)
    {
        if(Convert.ToString(Get(CadApplication(),"ActiveEnvironment"))?.StartsWith("FEAResults",StringComparison.Ordinal)!=true)return;
        var active=SimulationOwner(Get(doc,"StudyOwner"),"GetActiveStudy");
        if(SimulationName(active)!=SimulationName(study))throw new InvalidOperationException("Close the Solid Edge Simulation Results environment before switching studies to mesh/solve. No calculation was started.");
    }
}
