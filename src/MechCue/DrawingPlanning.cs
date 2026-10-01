namespace MechCue;

public sealed record DrawingPlan(string FrontOrientation, string Reason, double[] SizeMm, string[] Views, string[] Warnings);
public static class DrawingPlanning
{
    public static readonly Dictionary<string,int> Orientations = new() {{"front",4},{"back",6},{"top",1},{"bottom",5},{"right",2},{"left",3},{"isometric",9}};
    public static DrawingPlan Plan(double[] size, string front="auto", bool concept=false, int[]? cylinders=null, bool isometric=true, bool axisymmetric=false)
    {
        if(size.Length!=3 || size.Any(v=>!double.IsFinite(v)||v<=0)) throw new ArgumentException("The model must have positive finite XYZ extents.");
        if(front!="auto" && (!Orientations.ContainsKey(front)||front=="isometric")) throw new ArgumentException("frontOrientation must be auto/front/back/top/bottom/right/left.");
        string reason="User-specified front orientation.";
        if(front=="auto") {
            if(concept) {front="front";reason="MechCue concept model: XYZ axes and machine front (-Y).";}
            else {
                int n=Enumerable.Range(0,3).OrderByDescending(i=>cylinders?[i]??0).ThenBy(i=>size[i]).First();
                front=n==0?"right":n==1?"front":"top";
                reason=(cylinders?.Sum()??0)>0?"Candidate normal aligned with the most cylindrical features; use frontOrientation to override.":"Candidate shows the largest projected bounding area; use frontOrientation to override.";
            }
        }
        var views=new List<string>{front,"fold-up"};if(!axisymmetric || cylinders==null || cylinders[front is "front" or "back"?1:front is "top" or "bottom"?2:0]!=1)views.Add("fold-right");if(isometric)views.Add("isometric");
        return new(front,reason,size,views.ToArray(),["Automatic direction is a geometric recommendation, not manufacturing intent.","Overall and circular-feature dimensions are reference dimensions. Sections, tolerances and hidden internal features require review."]);
    }
    public static double FitScale(double width,double height,double cellWidth,double cellHeight)
    {
        if(new[]{width,height,cellWidth,cellHeight}.Any(v=>!double.IsFinite(v)||v<=0))throw new ArgumentException("Invalid drawing layout dimensions.");
        double fit=Math.Min(cellWidth/width,cellHeight/height);
        double[] standard=[10,5,2,1,.5,.2,.1,.05,.02,.01,.005,.002,.001,.0005,.0002,.0001];
        return standard.FirstOrDefault(v=>v<=fit,fit);
    }
}
public static partial class SelfTest
{
    static void TestDrawingPlanning()
    {
        void Check(bool value,string message){if(!value)throw new Exception(message);}
        Check(DrawingPlanning.Plan([1200,900,1400],concept:true).FrontOrientation=="front","Concept front");
        Check(DrawingPlanning.Plan([120,80,10],cylinders:[0,0,2]).FrontOrientation=="top","Plate hole direction");
        Check(DrawingPlanning.Plan([100,5,60],isometric:false).Views.Length==3,"Thin part views");
        Check(DrawingPlanning.Plan([20,20,30],axisymmetric:true,cylinders:[0,0,1],isometric:false).Views.Length==2,"Cylinder avoids redundant side view");
        Check(DrawingPlanning.Plan([10,20,30],"back").FrontOrientation=="back","Explicit opposite front");
        foreach(var invalid in new[]{"isometric","bad"}){try{DrawingPlanning.Plan([1,2,3],invalid);throw new Exception("Invalid direction accepted");}catch(ArgumentException){}}
        Check(DrawingPlanning.FitScale(1200,1400,220,140)==.1,"Metric reduced scale fit");
        try{DrawingPlanning.Plan([1,0,3]);throw new Exception("Degenerate model accepted");}catch(ArgumentException){}
    }
}
