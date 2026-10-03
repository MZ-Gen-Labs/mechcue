using System.Text.Json;
namespace MechCue;
public static partial class SelfTest
{
    static void TestDetailPlanning()
    {
        void Check(bool ok,string message){if(!ok)throw new Exception(message);}
        void Reject(Action action,string message){try{action();}catch(ArgumentException){return;}throw new Exception(message);}
        foreach(var kind in new[]{"mill3","mill4","mill5","gantry"}) {
            var source=ConceptMachine.Create(kind,600,400,400);
            var groups=Bridge.DetailGroups(source,"");Check(groups.Sum(g=>g.BodyIds.Length)==source.Bodies.Count,"All detail bodies covered");
            var values=new Dictionary<string,double>(source.Values){["X"]=120,["Y"]=-50,["Z"]=100};
            if(kind=="mill5"){values["A"]=70;values["C"]=-45;}
            var world=source.Poses(values);var joints=source.AxisPoses(values);
            foreach(var group in groups)foreach(var id in group.BodyIds) {
                var local=ConceptMachine.Multiply(Bridge.InvertRigidMatrix(joints[group.Parent]),world[id]);
                var composed=ConceptMachine.Multiply(joints[group.Parent],local);
                Check(composed.Zip(world[id]).All(p=>Math.Abs(p.First-p.Second)<1e-10),"Non-home rotated A/C world pose preserved");
            }
            var bad=groups.Select(g=>g with{BodyIds=g.BodyIds.ToArray()}).ToArray();bad[0]=bad[0] with{BodyIds=[]};
            Reject(()=>Bridge.DetailGroups(source,JsonSerializer.Serialize(bad,ConceptMachine.JsonOptions)),"Missing body accepted");
            bad=groups.ToArray();bad[0]=bad[0] with{Id="../escape"};Reject(()=>Bridge.DetailGroups(source,JsonSerializer.Serialize(bad,ConceptMachine.JsonOptions)),"Unsafe group path accepted");
            bad=groups.ToArray();bad[0]=bad[0] with{BodyIds=[groups[1].BodyIds[0],..groups[0].BodyIds.Skip(1)]};Reject(()=>Bridge.DetailGroups(source,JsonSerializer.Serialize(bad,ConceptMachine.JsonOptions)),"Duplicate/wrong-axis body accepted");
            bad=groups.ToArray();bad[0]=bad[0] with{Id="detail"};Reject(()=>Bridge.DetailGroups(source,JsonSerializer.Serialize(bad,ConceptMachine.JsonOptions)),"Root file collision accepted");
            foreach(var reserved in new[]{"CON","com9","LPT8"}) {
                bad=groups.ToArray();bad[0]=bad[0] with{Id=reserved};Reject(()=>Bridge.DetailGroups(source,JsonSerializer.Serialize(bad,ConceptMachine.JsonOptions)),"Reserved file name accepted");
            }
        }
    }
}
