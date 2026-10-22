using MechCue.Physics;

namespace MechCue;
public static partial class SelfTest
{
    static void TestConvexGeometry()
    {
        double[][] tetra = [[0,0,0],[10,0,0],[0,10,0],[0,0,10]];
        ConvexGeometry.ValidateVertices(tetra);
        ConvexGeometry.ValidateSupportingPlane(tetra, [0,0,0], [1,0,0]);
        bool concave = false; try { ConvexGeometry.ValidateSupportingPlane(tetra, [5,0,0], [1,0,0]); } catch (ArgumentException) { concave = true; }
        if (!concave) throw new Exception("A face that cuts through vertices must not be treated as a convex hull");
        bool flat = false; try { ConvexGeometry.ValidateVertices([[0,0,0],[1,0,0],[0,1,0],[1,1,0]]); } catch (ArgumentException) { flat = true; }
        if (!flat) throw new Exception("A flat hull must be rejected before engine allocation");
        var model = new ConceptMachine { Type="convex-assembly", PhysicsDefaults=new() { WorkpieceMassKg=1.25 } };
        string parent="";
        foreach(var (id,direction,rotary) in new[]{("WX","X",false),("WY","Y",false),("WZ","Z",false),("WRZ","Z",true),("WRY","Y",true),("WRX","X",true)}) {
            model.Axes.Add(new(){Id=id,Parent=parent,Kind=rotary?"rotary":"linear",Direction=direction,Minimum=-100,Maximum=100}); model.Values[id]=0; parent=id;
        }
        model.Bodies.Add(new(){Id="workpiece",Parent="WRX",Shape="convex",VerticesMm=tetra,SizeMm=[10,10,10]}); model.Validate();
        var tracks=model.Axes.Select(a=>new Track{Name=a.Id,Kind=a.Kind=="rotary"?"部品回転":"部品移動",Axis=a.Direction,Points=[new(0,0),new(4,0)]}).ToList();
        var concept=new SavedConcept{Model=model,Tracks=model.Axes.Zip(tracks).ToDictionary(p=>p.First.Id,p=>p.Second.Id)};
        PhysicsTracks.MarkDerived(concept,tracks);
        var input=PhysicsTracks.Inputs(concept,tracks.ToDictionary(t=>t.Id));
        if(input.Joints.Count!=0 || input.Setup.EndTime!=4 || input.Setup.WorkpieceMassKg!=1.25)throw new Exception("Captured assemblies with no commanded axes must retain CAD mass and chart duration");
    }
}
