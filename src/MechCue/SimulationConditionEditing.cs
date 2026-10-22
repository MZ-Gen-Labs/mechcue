using System.Text.Json;

namespace MechCue;
public sealed partial class Bridge
{
    static void SimulationConditionInput(string category,int conditionNumber,string expectedName)
    {
        if(category is not ("load" or "constraint"))throw new ArgumentException("category must be load or constraint.");
        if(conditionNumber<1||string.IsNullOrWhiteSpace(expectedName))throw new ArgumentException("conditionNumber and expectedName from list_simulation_studies are required.");
    }
    static (object Document,object Owner,object Condition) SimulationConditionTarget(string expectedDocument,int studyNumber,string category,int conditionNumber,string expectedName)
    {
        var doc=SimulationDocument(expectedDocument);SimulationRequireModelEnvironment();var study=SimulationStudy(doc,studyNumber);
        var owner=SimulationOwner(study,category=="load"?"GetLoadOwner":"GetConstraintOwner");
        int count=Convert.ToInt32(Get(owner,"Count"));if(conditionNumber>count)throw new ArgumentException("Condition no longer exists; list simulation studies again.");
        var condition=GetItem(owner,conditionNumber);
        if(Convert.ToString(Get(condition,"Name"))!=expectedName)throw new InvalidOperationException("Condition name differs from expectedName; list simulation studies again.");
        if(Enumerable.Range(1,count).Count(i=>Convert.ToString(Get(GetItem(owner,i),"Name"))==expectedName)!=1)throw new InvalidOperationException("Condition names must be unique before editing/deleting by number.");
        return(doc,owner,condition);
    }
    public static object CadEditSimulationCondition(string expectedDocument,int studyNumber,string category,int conditionNumber,string expectedName,string? name=null,bool? suppressed=null,double? value=null,string directionJson="")
    {
        SimulationConditionInput(category,conditionNumber,expectedName);
        if(name==null&&suppressed==null&&value==null&&directionJson=="")throw new ArgumentException("Specify name, suppressed, value or directionJson.");
        if(name!=null&&(string.IsNullOrWhiteSpace(name)||name.Length>100))throw new ArgumentException("name must contain 1..100 characters.");
        if(category=="constraint"&&(value!=null||directionJson!=""))throw new ArgumentException("Fixed constraints support name and suppressed only.");
        if(value is double v&&(!double.IsFinite(v)||v<=0||v>1e9))throw new ArgumentException("value must be positive and finite (N for force, MPa for pressure).");
        double[]? direction=null;
        if(directionJson!="") {
            direction=JsonSerializer.Deserialize<double[]>(directionJson)??throw new ArgumentException("directionJson must contain a nonzero XYZ vector.");
            if(direction.Length!=3||direction.Any(x=>!double.IsFinite(x)||Math.Abs(x)>1e6)||direction.Sum(x=>x*x)<1e-24)throw new ArgumentException("directionJson must contain a finite nonzero XYZ vector.");
            double norm=Math.Sqrt(direction.Sum(x=>x*x));direction=direction.Select(x=>x/norm).ToArray();
        }
        var (doc,owner,condition)=SimulationConditionTarget(expectedDocument,studyNumber,category,conditionNumber,expectedName);
        if(name!=null&&Enumerable.Range(1,Convert.ToInt32(Get(owner,"Count"))).Any(i=>i!=conditionNumber&&Convert.ToString(Get(GetItem(owner,i),"Name"))==name))throw new ArgumentException("Condition name already exists.");
        int type=category=="load"?SimulationInt(condition,"GetLoadType"):0;
        if((value!=null||direction!=null)&&type is not (1 or 2))throw new ArgumentException("Only force/pressure values can be edited.");
        if(direction!=null&&type!=1)throw new ArgumentException("directionJson is supported for force loads only; pressure uses face normals.");
        var undo=new Stack<Action>();
        try {
            if(name!=null){string old=Convert.ToString(Get(condition,"Name"))!;undo.Push(()=>Set(condition,"Name",old));Set(condition,"Name",name);if(Convert.ToString(Get(condition,"Name"))!=name)throw new InvalidOperationException("Condition name not retained");}
            if(suppressed is bool s){bool old=Convert.ToBoolean(Get(condition,"Suppress"));undo.Push(()=>Set(condition,"Suppress",old));Set(condition,"Suppress",s);if(Convert.ToBoolean(Get(condition,"Suppress"))!=s)throw new InvalidOperationException("Suppression not retained");}
            if(value is double amount){double old=SimulationDouble(condition,"GetLoadValue"),native=type==2?amount*1e6:amount;undo.Push(()=>Call(condition,"SetLoadValue",old));Call(condition,"SetLoadValue",native);if(Math.Abs(SimulationDouble(condition,"GetLoadValue")-native)>Math.Max(1e-8,Math.Abs(native)*1e-9))throw new InvalidOperationException("Load value not retained");}
            if(direction!=null){object[] old=[0d,0d,0d];CadCallRef(condition,"GetLoadDirection",[0,1,2],old);undo.Push(()=>Call(condition,"SetLoadDirection",old));Call(condition,"SetLoadDirection",direction[0],direction[1],direction[2]);object[] actual=[0d,0d,0d];CadCallRef(condition,"GetLoadDirection",[0,1,2],actual);if(actual.Select(Convert.ToDouble).Zip(direction).Any(p=>Math.Abs(p.First-p.Second)>1e-8))throw new InvalidOperationException("Load direction not retained");}
        }catch(Exception error){var failures=new List<string>();while(undo.TryPop(out var restore))try{restore();}catch(Exception rollback){failures.Add(rollback.GetBaseException().Message);}throw new InvalidOperationException(failures.Count==0?"Condition edit failed; original values restored, document may be dirty.":"Condition edit failed; ROLLBACK FAILED: "+string.Join("; ",failures),error);}
        return new{fullName=CadName(doc),studyNumber,category,conditionNumber,conditions=SimulationConditions(SimulationStudy(doc,studyNumber),category=="constraint"),saved=false};
    }
    public static object CadDeleteSimulationCondition(string expectedDocument,int studyNumber,string category,int conditionNumber,string expectedName)
    {
        SimulationConditionInput(category,conditionNumber,expectedName);
        var (doc,owner,condition)=SimulationConditionTarget(expectedDocument,studyNumber,category,conditionNumber,expectedName);
        var before=Enumerable.Range(1,Convert.ToInt32(Get(owner,"Count"))).Select(i=>Convert.ToString(Get(GetItem(owner,i),"Name"))).ToArray();
        Call(condition,"Delete");
        var after=Enumerable.Range(1,Convert.ToInt32(Get(owner,"Count"))).Select(i=>Convert.ToString(Get(GetItem(owner,i),"Name"))).ToArray();
        if(!after.SequenceEqual(before.Where((_,i)=>i!=conditionNumber-1)))throw new InvalidOperationException("Condition deletion did not produce the expected list. Inspect the study before retrying; it may have changed.");
        return new{fullName=CadName(doc),studyNumber,category,removed=expectedName,remaining=after.Length,saved=false};
    }
}
