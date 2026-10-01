using System.Reflection;
using System.Text.Json;
namespace MechCue;

public sealed partial class Bridge
{
    static double[] DrawingRange(object item)
    {
        object[] a=[0d,0d,0d,0d];CadCallRef(item,"Range",[0,1,2,3],a);return a.Select(Convert.ToDouble).ToArray();
    }
    static object DrawingIndexed(object item,string name,object index)=>item.GetType().InvokeMember(name,BindingFlags.GetProperty|BindingFlags.InvokeMethod,null,item,[index])!;
    static int DrawingOrientation(object view)
    {
        object[] a=[0d,0d,0d,0d,0d,0d,0];CadCallRef(view,"ViewOrientation",[0,1,2,3,4,5,6],a);return Convert.ToInt32(a[6]);
    }
    static double[] DrawingModelRange(object doc)
    {
        if(CadExtension(doc)==".asm") {object[] a=[0d,0d,0d,0d,0d,0d];CadCallRef(doc,"Range",[0,1,2,3,4,5],a);return a.Select(Convert.ToDouble).ToArray();}
        var models=Get(doc,"Models");var lows=new List<double[]>();var highs=new List<double[]>();
        for(int i=1;i<=Convert.ToInt32(Get(models,"Count"));i++) {object[] a=[new double[3],new double[3]];CadCallRef(Get(GetItem(models,i),"Body"),"GetRange",[0,1],a);lows.Add(((Array)a[0]).Cast<object>().Select(Convert.ToDouble).ToArray());highs.Add(((Array)a[1]).Cast<object>().Select(Convert.ToDouble).ToArray());}
        if(lows.Count==0)throw new InvalidOperationException("The model has no solid bodies.");
        return Enumerable.Range(0,3).Select(i=>lows.Min(p=>p[i])).Concat(Enumerable.Range(0,3).Select(i=>highs.Max(p=>p[i]))).ToArray();
    }
    static int[] DrawingCylinderDirections(object doc)
    {
        int[] counts=[0,0,0];if(CadExtension(doc)==".asm")return counts;
        var models=Get(doc,"Models");for(int m=1;m<=Convert.ToInt32(Get(models,"Count"));m++) {
            var faces=DrawingIndexed(Get(GetItem(models,m),"Body"),"Faces",10);
            for(int i=1;i<=Convert.ToInt32(Get(faces,"Count"));i++) {object[] a=[new double[3]];CadCallRef(Get(GetItem(faces,i),"Geometry"),"GetAxisVector",[0],a);var axis=((Array)a[0]).Cast<object>().Select(Convert.ToDouble).ToArray();int n=Enumerable.Range(0,3).OrderByDescending(k=>Math.Abs(axis[k])).First();if(Math.Abs(axis[n])>.99)counts[n]++;}
        }return counts;
    }
    static (DrawingPlan plan,object model) DrawingAnalyze(object document,string modelPath,string front,bool iso)
    {
        object model=document;
        string existing="";
        if(CadExtension(document)==".dft") {
            var links=Get(document,"ModelLinks");if(modelPath=="") {if(Convert.ToInt32(Get(links,"Count"))!=1)throw new ArgumentException("Supply modelPath when a draft has zero or multiple linked models.");modelPath=Convert.ToString(Get(GetItem(links,1),"FileName"))!;}
            object? link=null;for(int i=1;i<=Convert.ToInt32(Get(links,"Count"));i++){var candidate=GetItem(links,i);if(string.Equals(Convert.ToString(Get(candidate,"FileName")),modelPath,StringComparison.OrdinalIgnoreCase))link=candidate;}
            if(link==null)throw new ArgumentException("modelPath must already be linked to this draft.");model=Get(link,"ModelDocument");
            var views=Get(Get(document,"ActiveSheet"),"DrawingViews");for(int i=1;i<=Convert.ToInt32(Get(views,"Count"));i++){var v=GetItem(views,i);if(!string.Equals(Convert.ToString(Get(Get(v,"ModelLink"),"FileName")),modelPath,StringComparison.OrdinalIgnoreCase))continue;int n=DrawingOrientation(v);existing=DrawingPlanning.Orientations.FirstOrDefault(p=>p.Value==n && p.Key!="isometric").Key??"";if(existing!="")break;}
        } else if(modelPath!=""&&!string.Equals(CadName(document),modelPath,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("modelPath differs from active model.");
        string manifest=Path.Combine(Path.GetDirectoryName(CadName(model))??"","mechcue-concept.json");bool concept=false;
        if(File.Exists(manifest)) {var c=JsonSerializer.Deserialize<ConceptMachine>(File.ReadAllText(manifest),ConceptMachine.JsonOptions);concept=c!=null&&string.Equals(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(manifest)!,c.AssemblyFile)),CadName(model),StringComparison.OrdinalIgnoreCase);}
        double[] range=DrawingModelRange(model),size=Enumerable.Range(0,3).Select(i=>(range[i+3]-range[i])*1000).ToArray();
        bool axisymmetric=false;
        if(CadExtension(model)!=".asm") {var models=Get(model,"Models");if(Convert.ToInt32(Get(models,"Count"))==1){var body=Get(GetItem(models,1),"Body");axisymmetric=Convert.ToInt32(Get(DrawingIndexed(body,"Faces",1),"Count"))==3&&DrawingCylinderDirections(model).Sum()==1;}}
        var plan=DrawingPlanning.Plan(size,front=="auto"&&existing!=""?existing:front,concept,DrawingCylinderDirections(model),iso,axisymmetric);
        if(front=="auto"&&existing!="")plan=plan with {Reason="Existing orthographic drawing view retained as the front reference."};return(plan,model);
    }
    public static object CadPlanDrawing(string expectedDocument,string modelPath="",string frontOrientation="auto",bool includeIsometric=true)
    {
        if(frontOrientation!="auto"&&(!DrawingPlanning.Orientations.ContainsKey(frontOrientation)||frontOrientation=="isometric"))throw new ArgumentException("Invalid frontOrientation.");
        var doc=CadDocument(CadApplication(),expectedDocument,write:false);var (plan,model)=DrawingAnalyze(doc,modelPath,frontOrientation,includeIsometric);return new {fullName=CadName(doc),modelPath=CadName(model),plan,saveRequired=Convert.ToBoolean(Get(model,"Dirty")),note="Read-only proposal. No views or dimensions were added."};
    }
    public static object CadListDrawingViews(string expectedDocument)
    {
        var doc=CadDocument(CadApplication(),expectedDocument,".dft",false);var sheet=Get(doc,"ActiveSheet");var views=Get(sheet,"DrawingViews");var result=new List<object>();
        for(int i=1;i<=Convert.ToInt32(Get(views,"Count"));i++){var v=GetItem(views,i);int n=DrawingOrientation(v);result.Add(new {number=i,name=Get(v,"Name"),modelPath=Get(Get(v,"ModelLink"),"FileName"),orientation=DrawingPlanning.Orientations.FirstOrDefault(p=>p.Value==n).Key??"custom",orientationId=n,scale=Get(v,"ScaleFactor"),sheetRangeMm=DrawingRange(v).Select(x=>x*1000),lines=Get(Get(v,"DVLines2d"),"Count"),circles=Get(Get(v,"DVCircles2d"),"Count")});}
        var dims=Get(sheet,"Dimensions");var details=new List<object>();for(int i=1;i<=Convert.ToInt32(Get(dims,"Count"));i++){var dimension=GetItem(dims,i);details.Add(new {number=i,valueNative=Convert.ToDouble(Get(dimension,"Value")),unitsType=Get(dimension,"UnitsType"),statusId=Get(dimension,"StatusOfDimension"),mechCueId=DrawingTag(dimension),sheetLineMm=DrawingDimensionLine(dimension,DrawingTag(dimension))});}
        return new {fullName=CadName(doc),sheet=Get(sheet,"Name"),views=result,dimensions=Get(dims,"Count"),dimensionDetails=details};
    }
    public static object CadAutoDrawing(string expectedDocument,string outputPath,string templatePath="",string frontOrientation="auto",bool includeIsometric=true,string dimensionMode="features")
    {
        outputPath=CadPath(outputPath,".dft");if(File.Exists(outputPath))throw new IOException("Output already exists.");if(!Directory.Exists(Path.GetDirectoryName(outputPath)))throw new DirectoryNotFoundException("Output folder must exist.");
        if(dimensionMode is not ("overall" or "features" or "none"))throw new ArgumentException("dimensionMode must be overall/features/none.");
        var source=CadDocument(CadApplication(),expectedDocument,write:false);if(CadExtension(source)==".dft")throw new ArgumentException("Activate the saved 3D model before creating an automatic draft.");
        string modelPath=CadName(source);if(!Path.IsPathFullyQualified(modelPath)||!File.Exists(modelPath)||Convert.ToBoolean(Get(source,"Dirty")))throw new InvalidOperationException("Save the 3D model first. Drawing generation does not save or modify the source model.");
        var (plan,_)=DrawingAnalyze(source,"",frontOrientation,includeIsometric);
        CadNew("draft",templatePath);var doc=Get(CadApplication(),"ActiveDocument");var sheet=Get(doc,"ActiveSheet");var setup=Get(sheet,"SheetSetup");double w=Convert.ToDouble(Get(setup,"SheetWidth")),h=Convert.ToDouble(Get(setup,"SheetHeight"));
        var created=new List<object>();var dimensions=new List<object>();var warnings=plan.Warnings.ToList();
        if(Convert.ToInt32(Get(Get(sheet,"DrawingViews"),"Count"))!=0)throw new InvalidOperationException("Use a blank drawing template without existing model views. The new draft remains open for inspection.");
        try {
            // Reserve border, dimensions and the lower-right title block. Third-angle projection.
            double cellW=(w-.08)/2,cellH=(h-.11)/2;if(cellW<.04||cellH<.04)throw new InvalidOperationException("Sheet is too small; use an A3 or larger landscape template.");
            
            double sizeMax=plan.SizeMm.Max();double scale=DrawingPlanning.FitScale(sizeMax,sizeMax,cellW*1000,cellH*1000);
            double left=.04+cellW/2,right=w-.04-cellW/2,bottom=.07+cellH/2,top=h-.04-cellH/2;
            CadAddDrawingView(CadName(doc),modelPath,plan.FrontOrientation,scale,left*1000,bottom*1000);
            var views=Get(sheet,"DrawingViews");var front=GetItem(views,1);created.Add(front);
            var up=Call(views,"AddByFold",front,2,left,top);Call(up,"Update");created.Add(up);
            if(plan.Views.Contains("fold-right")){var side=Call(views,"AddByFold",front,4,right,bottom);Call(side,"Update");created.Add(side);}
            if(includeIsometric){CadAddDrawingView(CadName(doc),modelPath,"isometric",DrawingPlanning.FitScale(sizeMax*1.75,sizeMax*1.75,cellW*1000,cellH*1000),right*1000,top*1000);created.Add(GetItem(views,Convert.ToInt32(Get(views,"Count"))));}
            // Range includes Solid Edge's view margin. Recenter each view in its cell.
            for(int i=0;i<created.Count;i++){var r=DrawingRange(created[i]);object[] a=[0d,0d];CadCallRef(created[i],"GetOrigin",[0,1],a);double x=i is 0 or 1?left:right,y=i==1||(includeIsometric&&i==created.Count-1)?top:bottom;Call(created[i],"SetOrigin",Convert.ToDouble(a[0])+x-(r[0]+r[2])/2,Convert.ToDouble(a[1])+y-(r[1]+r[3])/2);}
            for(int i=0;i<created.Count;i++){Set(created[i],"CaptionDefinitionTextPrimary",i==0?"FRONT":i==1?"TOP":includeIsometric&&i==created.Count-1?"ISOMETRIC":"RIGHT");Set(created[i],"DisplayCaptionPrimary",true);var r=DrawingRange(created[i]);Call(created[i],"SetCaptionPosition",(r[0]+r[2])/2,r[3]+.005);}
            if(dimensionMode!="none")for(int i=0;i<created.Count-(includeIsometric?1:0);i++){var result=DrawingDimensions(sheet,created[i],i+1,dimensionMode,i==0,i==0||i==1,12);dimensions.AddRange(result.dimensions);warnings.AddRange(result.warnings);}
            CadSave(CadName(doc),outputPath);
            return new {fullName=CadName(doc),modelPath,plan,views=CadListDrawingViews(CadName(doc)),dimensions,warnings,saved=true};
        } catch(Exception e) {throw new InvalidOperationException("Automatic drawing was not completed: "+e.Message+" The partial draft remains open for inspection; source model was not changed.",e);}
    }
    sealed record DrawingAnchor(object Reference,double X,double Y);
    sealed record DrawingLine(object Element,DrawingAnchor Start,DrawingAnchor End);
    static (double x,double y) DrawingSheetPoint(object view,double x,double y){object[] a=[x,y,0d,0d];CadCallRef(view,"ViewToSheet",[2,3],a);return(Convert.ToDouble(a[2]),Convert.ToDouble(a[3]));}
    static string? DrawingTag(object dimension){try{return Convert.ToString(Get(NamedItem(NamedItem(Get(dimension,"AttributeSets"),"MechCueDrawing"),"Id"),"Value"));}catch{return null;}}
    static void DrawingTag(object dimension,string id){var set=Call(Get(dimension,"AttributeSets"),"Add","MechCueDrawing");var a=Call(set,"Add","Id",8);Set(a,"Value",id);}
    static double[] DrawingRelatedPoint(object dimension,int index)
    {
        object[] a=[index,new System.Runtime.InteropServices.DispatchWrapper(null),0d,0d,0d,false];
        CadCallRef(dimension,"GetRelated",[1,2,3,4,5],a);return [Convert.ToDouble(a[2]),Convert.ToDouble(a[3])];
    }
    static bool DrawingDimensionHorizontal(string id,double[] a,double[] b) => id.EndsWith("overall-width")||id.EndsWith("-x")||(!id.EndsWith("overall-height")&&!id.EndsWith("-y")&&Math.Abs(b[0]-a[0])>Math.Abs(b[1]-a[1]));
    static double? DrawingDimensionLine(object dimension,string? id)
    {
        if(id==null||id.Contains("/diameter/"))return null;
        try {var a=DrawingRelatedPoint(dimension,0);var b=DrawingRelatedPoint(dimension,1);return (a[DrawingDimensionHorizontal(id,a,b)?1:0]+Convert.ToDouble(Get(dimension,"TrackDistance")))*1000;}catch{return null;}
    }
    static (List<object> placements,List<string> warnings) DrawingArrange(object sheet,int selectedView=0)
    {
        var placements=new List<object>();var warnings=new List<string>();var views=Get(sheet,"DrawingViews");
        var bounds=new List<(int number,string key,double[] box)>();
        for(int i=1;i<=Convert.ToInt32(Get(views,"Count"));i++) {
            var view=GetItem(views,i);if(DrawingOrientation(view)==9)continue;var points=new List<(double x,double y)>();
            var lines=Get(view,"DVLines2d");for(int j=1;j<=Convert.ToInt32(Get(lines,"Count"));j++){var line=GetItem(lines,j);foreach(string method in new[]{"GetStartPoint","GetEndPoint"}){object[] a=[0d,0d];CadCallRef(line,method,[0,1],a);points.Add(DrawingSheetPoint(view,Convert.ToDouble(a[0]),Convert.ToDouble(a[1])));}}
            if(points.Count==0)continue;
            bounds.Add((i,Convert.ToString(Get(view,"Key"))??i.ToString(),[points.Min(p=>p.x),points.Min(p=>p.y),points.Max(p=>p.x),points.Max(p=>p.y)]));
        }
        var rows=new List<(object dimension,int view,string id,bool horizontal,double value,double origin,double edge)>();var dimensions=Get(sheet,"Dimensions");
        for(int i=1;i<=Convert.ToInt32(Get(dimensions,"Count"));i++) {
            var dimension=GetItem(dimensions,i);string? id=DrawingTag(dimension);if(id==null||id.Contains("/diameter/"))continue;
            try {
                var a=DrawingRelatedPoint(dimension,0);var b=DrawingRelatedPoint(dimension,1);
                var candidates=bounds.Where(v=>id.StartsWith(v.key+"/",StringComparison.Ordinal)).ToList();
                if(candidates.Count==0&&id.Contains("/step-")) {
                    // Legacy step IDs identify the model, not the view. Require an unambiguous geometric match.
                    candidates=bounds.Where(v=>new[]{a,b}.All(p=>p[0]>=v.box[0]-1e-7&&p[0]<=v.box[2]+1e-7&&p[1]>=v.box[1]-1e-7&&p[1]<=v.box[3]+1e-7)).ToList();
                }
                if(candidates.Count!=1){warnings.Add($"Dimension {i}: view ownership is ambiguous; placement retained.");continue;}
                var owner=candidates[0];if(selectedView!=0&&owner.number!=selectedView)continue;
                bool horizontal=DrawingDimensionHorizontal(id,a,b);rows.Add((dimension,owner.number,id,horizontal,Math.Abs(Convert.ToDouble(Get(dimension,"Value"))),a[horizontal?1:0],owner.box[horizontal?1:0]));
            }catch(Exception e){warnings.Add($"Dimension {i}: {e.GetBaseException().Message}");}
        }
        foreach(var group in rows.GroupBy(r=>(r.view,r.horizontal))) {
            int lane=0;foreach(var row in group.OrderBy(r=>r.value).ThenBy(r=>r.id,StringComparer.Ordinal)) {
                double offset=.009+.007*lane++,track=row.edge-row.origin-offset;
                try {if(Math.Abs(Convert.ToDouble(Get(row.dimension,"TrackDistance"))-track)>1e-7)Set(row.dimension,"TrackDistance",track);
                    placements.Add(new {viewNumber=row.view,mechCueId=row.id,horizontal=row.horizontal,valueMm=row.value*1000,sheetLineMm=DrawingDimensionLine(row.dimension,row.id),distanceFromGeometryMm=offset*1000});
                }catch(Exception e){warnings.Add($"{row.id}: {e.GetBaseException().Message}");}
            }
        }
        return(placements,warnings);
    }
    public static object CadArrangeDrawingDimensions(string expectedDocument,int viewNumber=0)
    {
        if(viewNumber<0)throw new ArgumentOutOfRangeException(nameof(viewNumber));
        var doc=CadDocument(CadApplication(),expectedDocument,".dft");var sheet=Get(doc,"ActiveSheet");
        if(viewNumber>Convert.ToInt32(Get(Get(sheet,"DrawingViews"),"Count")))throw new ArgumentOutOfRangeException(nameof(viewNumber));
        var result=DrawingArrange(sheet,viewNumber);return new {fullName=CadName(doc),placements=result.placements,warnings=result.warnings,saved=false};
    }
    static (List<object> dimensions,List<string> warnings) DrawingDimensions(object sheet,object view,int number,string mode,bool horizontal,bool vertical,int maximum)
    {
        var results=new List<object>();var warnings=new List<string>();var collection=Get(sheet,"Dimensions");var existing=new HashSet<string>();for(int i=1;i<=Convert.ToInt32(Get(collection,"Count"));i++){string? id=DrawingTag(GetItem(collection,i));if(id!=null)existing.Add(id);}
        string key=Convert.ToString(Get(view,"Key"))??number.ToString();var lines=new List<DrawingLine>();var anchors=new List<DrawingAnchor>();var lineCollection=Get(view,"DVLines2d");
        for(int i=1;i<=Convert.ToInt32(Get(lineCollection,"Count"));i++){var l=GetItem(lineCollection,i);object[] a=[0d,0d],b=[0d,0d];CadCallRef(l,"GetStartPoint",[0,1],a);CadCallRef(l,"GetEndPoint",[0,1],b);var reference=Get(l,"Reference");var start=new DrawingAnchor(reference,Convert.ToDouble(a[0]),Convert.ToDouble(a[1]));var end=new DrawingAnchor(reference,Convert.ToDouble(b[0]),Convert.ToDouble(b[1]));lines.Add(new(l,start,end));anchors.Add(start);anchors.Add(end);}
        var circles=new List<(object element,DrawingAnchor center,double radius)>();var cs=Get(view,"DVCircles2d");for(int i=1;i<=Convert.ToInt32(Get(cs,"Count"));i++){var c=GetItem(cs,i);object[] a=[0d,0d];CadCallRef(c,"GetCenterPoint",[0,1],a);circles.Add((c,new(Get(c,"Reference"),Convert.ToDouble(a[0]),Convert.ToDouble(a[1])),Convert.ToDouble(Get(c,"Radius"))));}
        if(anchors.Count==0&&circles.Count==0){warnings.Add($"View {number}: no straight or circular edges; overall dimensions skipped.");return(results,warnings);}
        double xmin=anchors.Count>0?anchors.Min(a=>a.X):circles.Min(c=>c.center.X-c.radius),xmax=anchors.Count>0?anchors.Max(a=>a.X):circles.Max(c=>c.center.X+c.radius);
        double ymin=anchors.Count>0?anchors.Min(a=>a.Y):circles.Min(c=>c.center.Y-c.radius),ymax=anchors.Count>0?anchors.Max(a=>a.Y):circles.Max(c=>c.center.Y+c.radius);
        double scale=Convert.ToDouble(Get(view,"ScaleFactor"));int xLane=0,yLane=0;
        void Linear(DrawingAnchor a,DrawingAnchor b,bool x,string label,double target,int lane){
            if(target<1e-7||results.Count>=maximum)return;string id=label.StartsWith("step-")?$"{Get(Get(view,"ModelLink"),"FileName")}/{label}":$"{key}/{label}";if(existing.Contains(id))return;object? dim=null;
            try {
                var pa=DrawingSheetPoint(view,a.X,a.Y);var pb=DrawingSheetPoint(view,b.X,b.Y);
                dim=Call(collection,"AddDistanceBetweenObjects",a.Reference,pa.x,pa.y,0d,true,b.Reference,pb.x,pb.y,0d,true);
                Call(dim,"SetOrientation",x?0:1);Set(dim,"Constraint",false);
                double edge=x?ymin:xmin,origin=x?a.Y:a.X;Set(dim,"TrackDistance",(edge-origin)*scale-(.009+.007*lane));
                double value=Convert.ToDouble(Get(dim,"Value"));if(Math.Abs(value-target)>Math.Max(1e-6,target*1e-5))throw new InvalidOperationException($"dimension value {value*1000:g6} differs from intended {target*1000:g6} mm");
                DrawingTag(dim,id);existing.Add(id);results.Add(new {viewNumber=number,kind=label,valueMm=value*1000,associated=true});
            }catch(Exception e){if(dim!=null)try{Call(dim,"Delete");}catch{}warnings.Add($"View {number}, {label}: {e.GetBaseException().Message}");}
        }
        DrawingAnchor? left=anchors.OrderBy(a=>a.X).ThenBy(a=>a.Y).FirstOrDefault(),rightAnchor=anchors.OrderByDescending(a=>a.X).ThenBy(a=>a.Y).FirstOrDefault(),low=anchors.OrderBy(a=>a.Y).ThenBy(a=>a.X).FirstOrDefault(),high=anchors.OrderByDescending(a=>a.Y).ThenBy(a=>a.X).FirstOrDefault();
        if(horizontal&&left!=null&&rightAnchor!=null)Linear(left,rightAnchor,true,"overall-width",xmax-xmin,xLane++);
        if(vertical&&low!=null&&high!=null)Linear(low,high,false,"overall-height",ymax-ymin,yLane++);
        if(mode=="features" || anchors.Count==0) {
            var unique=circles.GroupBy(c=>$"{c.center.X:F8}/{c.center.Y:F8}/{c.radius:F8}").Select(g=>g.First()).OrderBy(c=>c.center.X).ThenBy(c=>c.center.Y).Take(4).ToList();var diameters=new HashSet<string>();int hole=0;
            foreach(var c in unique){hole++;string diam=$"{c.radius:F8}";string id=$"{key}/diameter/{diam}";if(results.Count<maximum&&diameters.Add(diam)&&!existing.Contains(id)){object? dim=null;try{dim=Call(collection,"AddRadialDiameter",c.center.Reference);Set(dim,"Constraint",false);Set(dim,"TrackAngle",Math.PI/4);Set(dim,"TrackDistance",c.radius*scale+.018);Call(dim,"SetTextOffsets",.018,.012+.007*(hole-1));double v=Convert.ToDouble(Get(dim,"Value"));if(Math.Abs(v-c.radius*2)>1e-6)throw new InvalidOperationException("Circular dimension value mismatch.");DrawingTag(dim,id);existing.Add(id);results.Add(new {viewNumber=number,kind="diameter",valueMm=v*1000,associated=true});}catch(Exception e){if(dim!=null)try{Call(dim,"Delete");}catch{}warnings.Add($"View {number}, diameter: {e.GetBaseException().Message}");}}
                if(mode=="features"&&left!=null)Linear(left,c.center,true,$"circle-{hole}-x",c.center.X-xmin,xLane++);if(mode=="features"&&low!=null)Linear(low,c.center,false,$"circle-{hole}-y",c.center.Y-ymin,yLane++);
            }
        }
        if(mode=="features"&&anchors.Count>0){
            var lengths=new HashSet<string>();var axisDone=new HashSet<bool>();var basis=DrawingVectors(view);double[] localY=[basis[1]*basis[5]-basis[2]*basis[4],basis[2]*basis[3]-basis[0]*basis[5],basis[0]*basis[4]-basis[1]*basis[3]];
            foreach(var line in lines.OrderByDescending(l=>Math.Max(Math.Abs(l.End.X-l.Start.X),Math.Abs(l.End.Y-l.Start.Y)))){
                double dx=Math.Abs(line.End.X-line.Start.X),dy=Math.Abs(line.End.Y-line.Start.Y);bool x=dy<1e-8;double length=x?dx:dy,extent=x?xmax-xmin:ymax-ymin;
                if((!x&&dx>1e-8)||length<extent*.1||length>=extent-1e-7)continue;
                int axis=Enumerable.Range(0,3).OrderByDescending(k=>Math.Abs(x?basis[k+3]:localY[k])).First();string id=$"{"XYZ"[axis]}/{length:F8}";if(!lengths.Add(id)||!axisDone.Add(x))continue;
                Linear(line.Start,line.End,x,"step-"+id,length,x?xLane++:yLane++);
            }
        }
        var arrangement=DrawingArrange(sheet,number);warnings.AddRange(arrangement.warnings);
        if(horizontal&&left==null&&circles.Count>0)warnings.Add($"View {number}: round silhouette; diameter replaces linear extent.");return(results,warnings);
    }
    public static object CadDimensionDrawingView(string expectedDocument,int viewNumber,string dimensionMode="features",int maxDimensions=12)
    {
        if(dimensionMode is not ("overall" or "features"))throw new ArgumentException("dimensionMode must be overall/features.");if(maxDimensions<1||maxDimensions>30)throw new ArgumentException("maxDimensions must be 1..30.");
        var doc=CadDocument(CadApplication(),expectedDocument,".dft");var sheet=Get(doc,"ActiveSheet");var views=Get(sheet,"DrawingViews");if(viewNumber<1||viewNumber>Convert.ToInt32(Get(views,"Count")))throw new ArgumentOutOfRangeException(nameof(viewNumber));var view=GetItem(views,viewNumber);if(DrawingOrientation(view)==9)throw new ArgumentException("Dimension an orthographic view, not an isometric view.");
        var result=DrawingDimensions(sheet,view,viewNumber,dimensionMode,true,true,maxDimensions);return new {fullName=CadName(doc),viewNumber,added=result.dimensions,warnings=result.warnings,saved=false};
    }
    static double[] DrawingVectors(object view){object[] a=[0d,0d,0d,0d,0d,0d,0];CadCallRef(view,"ViewOrientation",[0,1,2,3,4,5,6],a);return a.Take(6).Select(Convert.ToDouble).ToArray();}
    public static object CadCompleteDrawing(string expectedDocument,int frontViewNumber=1,bool includeIsometric=true,string dimensionMode="features")
    {
        if(dimensionMode is not ("overall" or "features" or "none"))throw new ArgumentException("dimensionMode must be overall/features/none.");
        var doc=CadDocument(CadApplication(),expectedDocument,".dft");var sheet=Get(doc,"ActiveSheet");var views=Get(sheet,"DrawingViews");if(frontViewNumber<1||frontViewNumber>Convert.ToInt32(Get(views,"Count")))throw new ArgumentOutOfRangeException(nameof(frontViewNumber));
        var front=GetItem(views,frontViewNumber);int direction=DrawingOrientation(front);if(!DrawingPlanning.Orientations.Any(p=>p.Value==direction&&p.Key!="isometric"))throw new ArgumentException("Choose a standard orthographic front view.");
        var link=Get(front,"ModelLink");string modelPath=Convert.ToString(Get(link,"FileName"))!;
        var (plan,_)=DrawingAnalyze(doc,modelPath,DrawingPlanning.Orientations.First(p=>p.Value==direction).Key,includeIsometric);
        var main=DrawingRange(front);double cx=(main[0]+main[2])/2,cy=(main[1]+main[3])/2,scale=Convert.ToDouble(Get(front,"ScaleFactor"));double gap=.04+plan.SizeMm.Max()*scale/2000;
        var vector=DrawingVectors(front);double[] upNormal=[vector[1]*vector[5]-vector[2]*vector[4],vector[2]*vector[3]-vector[0]*vector[5],vector[0]*vector[4]-vector[1]*vector[3]];double[] sideNormal=vector.Skip(3).ToArray();
        var chosen=new List<(object view,int number,string role)>{(front,frontViewNumber,"front")};var added=new List<object>();
        void Fold(double[] normal,int fold,string role,double x,double y){
            for(int i=1;i<=Convert.ToInt32(Get(views,"Count"));i++){var candidate=GetItem(views,i);if(!string.Equals(Convert.ToString(Get(Get(candidate,"ModelLink"),"FileName")),modelPath,StringComparison.OrdinalIgnoreCase))continue;var v=DrawingVectors(candidate);if(Enumerable.Range(0,3).All(k=>Math.Abs(v[k]-normal[k])<1e-6)){chosen.Add((candidate,i,role));return;}}
            var view=Call(views,"AddByFold",front,fold,x,y);Call(view,"Update");int number=Convert.ToInt32(Get(views,"Count"));chosen.Add((view,number,role));added.Add(new {number,role});
        }
        Fold(upNormal,2,"top",cx,main[3]+gap);if(plan.Views.Contains("fold-right"))Fold(sideNormal,4,"right",main[2]+gap,cy);
        if(includeIsometric){bool found=false;for(int i=1;i<=Convert.ToInt32(Get(views,"Count"));i++){var view=GetItem(views,i);if(DrawingOrientation(view)==9&&string.Equals(Convert.ToString(Get(Get(view,"ModelLink"),"FileName")),modelPath,StringComparison.OrdinalIgnoreCase)){found=true;break;}}
            if(!found){CadAddDrawingView(CadName(doc),modelPath,"isometric",scale*.5,(main[2]+gap)*1000,(main[3]+gap)*1000);added.Add(new {number=Convert.ToInt32(Get(views,"Count")),role="isometric"});}
        }
        var dimensions=new List<object>();var warnings=new List<string>();
        if(dimensionMode!="none")foreach(var (view,number,role) in chosen){var result=DrawingDimensions(sheet,view,number,dimensionMode,role=="front",role is "front" or "top",12);dimensions.AddRange(result.dimensions);warnings.AddRange(result.warnings);}
        double width=Convert.ToDouble(Get(Get(sheet,"SheetSetup"),"SheetWidth")),height=Convert.ToDouble(Get(Get(sheet,"SheetSetup"),"SheetHeight"));
        foreach(var (view,number,_) in chosen){var r=DrawingRange(view);if(r[0]<.01||r[1]<.01||r[2]>width-.01||r[3]>height-.01)warnings.Add($"View {number} reaches the sheet border; adjust scale or position. Existing layout was retained.");}
        return new {fullName=CadName(doc),modelPath,frontViewNumber,addedViews=added,dimensions,warnings,views=CadListDrawingViews(CadName(doc)),saved=false};
    }}


