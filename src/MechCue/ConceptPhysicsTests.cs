namespace MechCue;
public static partial class SelfTest
{
    static void TestConceptPhysics()
    {
        void Check(bool condition,string message) { if(!condition) throw new Exception(message); }
        // Table top at z=0, a 60 mm workpiece on it, a lift axis HZ carrying two fingers (2.5 mm clearance per side).
        var model=new ConceptMachine{Type="physics-test"};
        void Axis(string id,string parent,string dir,double min,double max)=>model.Axes.Add(new(){Id=id,Parent=parent,Direction=dir,Minimum=min,Maximum=max});
        void Box(string id,string parent,double x,double y,double z,double cx,double cy,double cz)=>model.Bodies.Add(new(){Id=id,Parent=parent,SizeMm=[x,y,z],CenterMm=[cx,cy,cz]});
        Axis("HZ","","Z",-500,500);Axis("WX","","X",-700,700);Axis("WY","WX","Y",-700,700);Axis("WZ","WY","Z",-100,500);
        Box("table-top","",1000,1000,40,0,0,-20);
        Box("finger-1","HZ",20,15,40,0,40,30);Box("finger-2","HZ",20,15,40,0,-40,30);
        Box("workpiece","WZ",60,60,60,0,0,30);
        foreach(var a in model.Axes)model.Values[a.Id]=0;
        model.Validate();
        List<JointPoint> P(params double[] tv)=>Enumerable.Range(0,tv.Length/2).Select(i=>new JointPoint{Time=tv[2*i],Value=tv[2*i+1]}).ToList();
        double At(List<JointPoint> p,double t){var i=p.FindIndex(x=>x.Time>=t-1e-9);if(i<=0)return p[0].Value;if(Math.Abs(p[i].Time-t)<1e-9)return p[i].Value;var u=(t-p[i-1].Time)/(p[i].Time-p[i-1].Time);return p[i-1].Value+u*(p[i].Value-p[i-1].Value);}

        // On the table, never gripped: it stays put.
        var still=ConceptPhysics.Run(model,new(){EndTime=2},new Dictionary<string,List<JointPoint>>{["HZ"]=P(0,0,2,0)});
        Check(still.FinalState=="resting"&&still.Axes["WZ"].All(p=>p.Value==0)&&still.Events.Count==0,"Workpiece on a support stays still");

        // Grasp, lift 200 mm (1..2 s), release at 3 s: free fall of 200 mm, landing on the table.
        var joints=new Dictionary<string,List<JointPoint>>{["HZ"]=P(0,0,1,0,2,200,4,200)};
        var run=ConceptPhysics.Run(model,new(){EndTime=4,Grip=[new(){Time=0.5,Closed=true},new(){Time=3,Closed=false}]},joints);
        var z=run.Axes["WZ"];
        Check(run.Events.Select(e=>e.Type).SequenceEqual(["grasp","release","landed"]),"Grasp, release and landing events: "+string.Join(",",run.Events.Select(e=>e.Type)));
        Check(Math.Abs(At(z,1))<1e-6&&Math.Abs(At(z,1.5)-100)<1e-3&&Math.Abs(At(z,2.5)-200)<1e-3,"Gripped workpiece follows the hand");
        double landing=run.Events.Single(e=>e.Type=="landed").Time,expected=3+Math.Sqrt(2*0.2/9.81);
        Check(Math.Abs(landing-expected)<1e-3,$"Free-fall landing time {landing} vs {expected}");
        Check(run.FinalState=="resting"&&Math.Abs(z[^1].Value)<1e-6&&Math.Abs(At(z,3.1)-(200-0.5*9.81*0.01*1000))<2,"Dropped workpiece settles on the table");

        // The fingers are too far from the workpiece: the close command is ignored.
        var missed=ConceptPhysics.Run(model,new(){EndTime=3,Grip=[new(){Time=1,Closed=true}]},new Dictionary<string,List<JointPoint>>{["HZ"]=P(0,0,0.5,300,3,300)});
        Check(missed.Events.Single().Type=="grip-missed"&&missed.Axes["WZ"].All(p=>p.Value==0),"Out-of-reach grip leaves the workpiece alone");

        // Gravity along +X: after release, the workpiece slides to the first support in that direction (none): lost at the carrier limit.
        var sideways=ConceptPhysics.Run(model,new(){EndTime=3,Gravity=[1,0,0],Grip=[new(){Time=0,Closed=true},new(){Time=1,Closed=false}]},new Dictionary<string,List<JointPoint>>{["HZ"]=P(0,0,3,0)});
        Check(sideways.FinalState=="lost"&&sideways.Warnings.Count>0,"Unsupported fall ends at the carrier limit");
        // Without grip commands and gravity +Z-negative the table keeps it, but upside-down gravity makes it fall away.
        var up=ConceptPhysics.Run(model,new(){EndTime=1,Gravity=[0,0,1]},new Dictionary<string,List<JointPoint>>{["HZ"]=P(0,0,1,0)});
        Check(up.FinalState=="lost","Reversed gravity lifts an unsupported workpiece away");
        // Same simulation on the real arm5 template: nothing to grip, so the workpiece stays on the table.
        var arm=ConceptMachine.Create("arm5",500,300,300);
        var armRun=ConceptPhysics.Run(arm,new(){EndTime=2},new Dictionary<string,List<JointPoint>>());
        Check(armRun.FinalState=="resting"&&armRun.Axes.Keys.OrderBy(x=>x).SequenceEqual(["WX","WY","WZ"]),"arm5 workpiece rests on the table top");
    }
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
