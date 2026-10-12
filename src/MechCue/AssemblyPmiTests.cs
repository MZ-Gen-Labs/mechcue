namespace MechCue;
public static partial class SelfTest
{
    static void TestAssemblyPmiTransforms()
    {
        void Check(double[] actual,double[] expected,string message) {
            if(actual.Zip(expected).Any(p=>Math.Abs(p.First-p.Second)>1e-10))throw new Exception(message);
        }
        var child=Bridge.CadTransform(250,30,40,0,0,90);
        var parent=Bridge.CadTransform(1000,500,100,0,0,90);
        Check(Bridge.PmiTransform(child,[.12,.08,.02]),[.17,.15,.06],"PMI occurrence vertex uses metres and rotation");
        Check(Bridge.PmiTransform(ConceptMachine.Multiply(parent,child),[.12,.08,.02]),[.85,.67,.16],"Nested PMI vertex composes parent and local transforms");
        Check(Bridge.PmiTransform(child,[1,0,0],true),[0,1,0],"PMI circle normal excludes translation");
        foreach(var (matrix,point) in new[]{(new double[16],new[]{0d,0d,0d}),(child,new[]{double.NaN,0d,0d}),(child,new[]{0d,0d})}) {
            bool rejected=false;try{Bridge.PmiTransform(matrix,point);}catch(InvalidOperationException){rejected=true;}
            if(!rejected)throw new Exception("Unsafe PMI coordinates/transform accepted");
        }
        var corner=Bridge.PmiParsePoint(" Vertex:min,MID,max ");
        if(corner.Circle||!corner.Corner.SequenceEqual([-1,0,1]))throw new Exception("PMI vertex corner spec parsed");
        var circle=Bridge.PmiParsePoint("circle:zmin");
        if(!circle.Circle||circle.Axis!=2||circle.Max)throw new Exception("PMI circle spec parsed");
        if(Bridge.PmiDistancePlane("Z","AUTO")!="XZ"||Bridge.PmiDistancePlane("Y","AUTO")!="XY"||Bridge.PmiDistancePlane("Z","YZ")!="YZ")throw new Exception("PMI distance plane selection");
        foreach(var action in new Action[]{()=>Bridge.PmiParsePoint("vertex:min,max"),()=>Bridge.PmiParsePoint("circle:wmin"),()=>Bridge.PmiParsePoint(""),
            ()=>Bridge.PmiDistancePlane("X","YZ"),()=>Bridge.PmiDistancePlane("W","AUTO"),()=>Bridge.PmiDistancePlane("X","XW")}) {
            bool rejected=false;try{action();}catch(ArgumentException){rejected=true;}
            if(!rejected)throw new Exception("Invalid PMI distance specification accepted");
        }
    }
}
