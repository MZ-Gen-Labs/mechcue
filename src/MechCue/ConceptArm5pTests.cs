namespace MechCue;

public static partial class SelfTest
{
    static void TestConceptArm5p()
    {
        void Check(bool condition,string message) { if(!condition) throw new Exception(message); }
        var plain=ConceptMachine.Create("arm5",500,300,300);var model=ConceptMachine.Create("arm5p",500,300,300);
        Check(string.Join(",",model.Axes.Select(a=>a.Id+"<"+a.Parent+":"+a.Kind[0]+a.Direction))=="J1<:rZ,J2<J1:rY,J3<J2:rY,J4<J3:rY,J5<J4:rX,F1<J5:lY,F2<J5:lY,WX<:lX,WY<WX:lY,WZ<WY:lZ,WRZ<WZ:rZ,WRY<WRZ:rY,WRX<WRY:rX","arm5p axis chain");
        var home=model.Poses(model.Values);var plainHome=plain.Poses(plain.Values);
        foreach(var body in plain.Bodies)Check(home[body.Id].SequenceEqual(plainHome[body.Id]),"arm5p home pose equals arm5: "+body.Id);
        var values=new Dictionary<string,double>{["J1"]=20,["J2"]=-15,["J3"]=40,["J4"]=30,["J5"]=-25,["F1"]=-8,["F2"]=8,["WX"]=100,["WY"]=-50,["WZ"]=-300,["WRZ"]=30,["WRY"]=-20,["WRX"]=70};
        var moved=model.Poses(values);
        var measured=Bridge.MeasureConcept(model,moved);
        Check(values.All(p=>Math.Abs(measured[p.Key]-p.Value)<1e-6),"arm5p free-body and finger measurement: "+string.Join(",",values.Where(p=>Math.Abs(measured[p.Key]-p.Value)>=1e-6).Select(p=>p.Key+"="+measured[p.Key])));
        Check(Math.Abs(moved["finger-1"][13]-home["finger-1"][13])>1e-6&&moved["hand"].SequenceEqual(model.Poses(new Dictionary<string,double>(values){["F1"]=0,["F2"]=0})["hand"]),"finger axes move only the fingers");
        // The workpiece turns about its own centre: the centre depends only on WX/WY/WZ.
        Check(Math.Abs(moved["workpiece"][12]-home["workpiece"][12]-.1)<1e-10&&Math.Abs(moved["workpiece"][13]-home["workpiece"][13]+.05)<1e-10&&Math.Abs(moved["workpiece"][14]-home["workpiece"][14]+.3)<1e-10,"workpiece rotates about its centre");
    }
}
