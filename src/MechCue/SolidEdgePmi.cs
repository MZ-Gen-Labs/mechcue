using System.Globalization;
namespace MechCue;

public sealed partial class Bridge
{
    sealed record PmiPoint(object Parent, double[] Position);
    sealed record PmiEdge(object Parent, double[] A, double[] B, object? StartParent=null, object? EndParent=null);
    sealed record PmiCircle(object Parent, double[] Center, double[] Normal, double Radius);
    sealed record PmiPlane(object Parent, string Name, double[] X, double[] Y, double[] Normal, int Orientation);
    sealed record PmiPlaced(object Dimension, string Id, string Plane, string Kind, bool Horizontal, double Origin, double Edge);
    const string PmiAttribute = "MechCuePMI";
    static readonly HashSet<string> PmiAutoKinds=["overall","diameter","hole-position","step","angle"];
    static double PmiDot(double[] a, double[] b) => a.Zip(b).Sum(p => p.First * p.Second);
    static double[] PmiCross(double[] a, double[] b) => [a[1]*b[2]-a[2]*b[1],a[2]*b[0]-a[0]*b[2],a[0]*b[1]-a[1]*b[0]];
    static double[] PmiVector(object item, string method) { object[] a=[new double[3]]; CadCallRef(item,method,[0],a); return ((Array)a[0]).Cast<object>().Select(Convert.ToDouble).ToArray(); }
    static object PmiDocument(string expected, bool write=true)
    {
        var doc=CadDocument(CadApplication(),expected,write:write);
        if(CadExtension(doc)==".asm")return doc;
        if(CadExtension(doc)!=".par")throw new InvalidOperationException("PMI automation requires a .par part or .asm assembly.");
        if(Convert.ToInt32(Get(Get(doc,"Models"),"Count"))!=1)throw new InvalidOperationException("PMI automation currently requires a single-body part.");
        return doc;
    }
    // Solid Edge may lazily initialize the PMI container on first access, marking the document dirty.
    // Never clear Dirty here: doing so could conceal unrelated unsaved user changes.
    static object PmiDesign(object document) { object[] a=[new System.Runtime.InteropServices.DispatchWrapper(null),1]; CadCallRef(document,"PMI_ByModelState",[0],a); return a[0]; }
    static string? PmiTag(object dim, string name="Id") { try { return Convert.ToString(Get(NamedItem(NamedItem(Get(dim,"AttributeSets"),PmiAttribute),name),"Value")); } catch { return null; } }
    static void PmiTag(object dim,string id,string plane,string kind)
    {
        var set=Call(Get(dim,"AttributeSets"),"Add",PmiAttribute);
        foreach(var pair in new[]{("Id",id),("Plane",plane),("Kind",kind)}) { var a=Call(set,"Add",pair.Item1,8); Set(a,"Value",pair.Item2); }
    }
    const int PmiEditUndo=57643, PmiEditRedo=57644; // Solid Edge 2026 Edit Undo/Redo command IDs.
    // SE2026 API-created assembly PMI keeps its creation-time points/value when its second parent's
    // occurrence moves, until it is rebuilt from persisted data (save/reopen). Undoing and redoing only
    // the creation step rebuilds it the same way, after which it follows motion with no UpdateStatus/UpdateAll.
    // Call immediately after AddDimension: the newest undo step is that creation and the redo stack is empty.
    static (object dimension,bool rebuilt) PmiRebuildForMotion(object dims,object dimension)
    {
        var app=CadApplication();string key=Convert.ToString(Get(dimension,"Key"))!;
        List<string> Keys()=>Enumerable.Range(1,Convert.ToInt32(Get(dims,"Count"))).Select(i=>Convert.ToString(Get(GetItem(dims,i),"Key"))!).ToList();
        bool Wait(Func<List<string>,bool> done){var limit=System.Diagnostics.Stopwatch.StartNew();while(true){Call(app,"DoIdle");if(done(Keys()))return true;if(limit.ElapsedMilliseconds>3000)return false;Thread.Sleep(25);}}
        var before=Keys();
        Call(app,"StartCommand",PmiEditUndo);
        bool undone=Wait(k=>k.Count==before.Count-1&&!k.Contains(key)&&before.Where(b=>b!=key).All(k.Contains));
        Call(app,"StartCommand",PmiEditRedo);
        if(!Wait(k=>k.Count==before.Count&&k.Contains(key)))throw new InvalidOperationException("Undo/Redo rebuild did not restore the new PMI; review the document's undo history.");
        // Undo reverted another step (redo restored it): keep the unrebuilt dimension and report it.
        if(!undone)return(dimension,false);
        for(int i=1;i<=Convert.ToInt32(Get(dims,"Count"));i++){var d=GetItem(dims,i);if(Convert.ToString(Get(d,"Key"))==key)return(d,true);}
        throw new InvalidOperationException("Rebuilt PMI was not found.");
    }
    static bool PmiValueMatches(object dimension,int type,double expected,out double value)
    {
        value=Convert.ToDouble(Get(dimension,"Value"));double actual=type==3?Math.Min(value,Math.PI-value):value;
        return double.IsFinite(value)&&Math.Abs(actual-expected)<=Math.Max(1e-6,expected*1e-5);
    }
    // Creates one associated reference dimension, rebuilds assembly PMI for motion, applies the
    // orientation and verifies the native value. Deletes the new dimension on failure.
    static (object dimension,bool? rebuilt,bool orientationFallback) PmiCreateDimension(object doc,object dims,PmiPlane plane,int type,PmiPoint a,PmiPoint? b,bool keyPoint,double expected,bool? horizontal,ref string stage)
    {
        object? dimension=null;string? key=null;
        try {
            stage="initialize";var init=Get(dims,"DimInitData");Call(init,"ClearCreationData");Call(init,"ClearParents");Call(init,"ClearAxis");Call(init,"SetType",type);Call(init,"SetAxisMode",type==1?2:1);Call(init,"SetPlane",plane.Parent);Call(init,"SetNumberOfParents",b==null?1:2);
            void Parent(int n,PmiPoint p)=>Call(init,"SetParentByIndex",n,p.Parent,keyPoint,false,false,false,p.Position[0],p.Position[1],p.Position[2]);
            Parent(0,a);if(b!=null)Parent(1,b);
            stage="create";dimension=Call(dims,"AddDimension",init);key=Convert.ToString(Get(dimension,"Key"));
            bool? rebuilt=null;if(CadExtension(doc)==".asm"){stage="motion-rebuild";(dimension,var ok)=PmiRebuildForMotion(dims,dimension);rebuilt=ok;}
            stage="reference";Set(dimension,"Constraint",false);Set(dimension,"HidePMI",false);
            stage="orientation";if(horizontal!=null)Call(dimension,"SetOrientation",horizontal.Value?0:1);
            stage="value";bool fallback=false;double value;
            // In some assembly flows SE2026 applies Horizontal/Vertical to the other plane axis
            // (isolated native tests do not reproduce it). Retry once and keep only a verified value.
            if(!PmiValueMatches(dimension,type,expected,out value)&&horizontal!=null){Call(dimension,"SetOrientation",horizontal.Value?1:0);fallback=PmiValueMatches(dimension,type,expected,out value);}
            if(!PmiValueMatches(dimension,type,expected,out value))throw new InvalidOperationException($"PMI value {value:g6} differs from intended {expected:g6}.");
            return(dimension,rebuilt,fallback);
        }catch{
            if(dimension!=null)try{Call(dimension,"Delete");}catch{}
            // An undo/redo failure can leave a rebuilt dimension behind a stale COM object; find it by Key.
            if(key!=null)try{for(int i=Convert.ToInt32(Get(dims,"Count"));i>=1;i--){var d=GetItem(dims,i);if(Convert.ToString(Get(d,"Key"))==key)Call(d,"Delete");}}catch{}
            throw;
        }
    }
    static object PmiModelView(object views,PmiPlane plane,out bool fresh)
    {
        string name="MechCue PMI "+plane.Name;
        for(int i=1;i<=Convert.ToInt32(Get(views,"Count"));i++){var candidate=GetItem(views,i);if(Convert.ToString(Get(candidate,"Name"))==name){fresh=false;return candidate;}}
        int count=Convert.ToInt32(Get(views,"Count"));var view=Call(views,"AddByStandardViewOrientation",plane.Orientation,4,0);
        if(Convert.ToInt32(Get(views,"Count"))<=count)throw new InvalidOperationException("Existing standard view retained; custom view could not be added.");
        Set(view,"Name",name);fresh=true;return view;
    }
    static List<PmiPlane> PmiPlanes(object doc)
    {
        var result=new List<PmiPlane>();var planes=Get(doc,CadExtension(doc)==".asm"?"AsmRefPlanes":"RefPlanes");
        for(int i=1;i<=Convert.ToInt32(Get(planes,"Count"));i++) {
            var plane=GetItem(planes,i);if(!Convert.ToBoolean(Get(plane,"Global")))continue;
            var normal=PmiVector(plane,"GetNormal");var x=PmiVector(plane,"GetReferenceDirection");var y=PmiCross(normal,x);
            int axis=Enumerable.Range(0,3).OrderByDescending(k=>Math.Abs(normal[k])).First();
            if(Math.Abs(normal[axis])<.999 || result.Any(p=>Math.Abs(PmiDot(p.Normal,normal))>.999))continue;
            result.Add(new(plane,axis==2?"XY":axis==1?"XZ":"YZ",x,y,normal,axis==2?4:axis==1?0:3));
        }
        return result.OrderBy(p=>p.Name=="XY"?0:p.Name=="XZ"?1:2).ToList();
    }
    static (List<PmiPoint> points,List<PmiEdge> lines,List<PmiCircle> circles) PmiGeometry(object doc,out double[]? assemblyBounds)
    {
        assemblyBounds=null;
        if(CadExtension(doc)==".asm") {var result=PmiAssemblyGeometry(doc,out var bounds);assemblyBounds=bounds;return result;}
        return PmiBodyGeometry(Get(GetItem(Get(doc,"Models"),1),"Body"));
    }
    static (List<PmiPoint> points,List<PmiEdge> lines,List<PmiCircle> circles) PmiBodyGeometry(object body)
    {
        var points=new List<PmiPoint>();var lines=new List<PmiEdge>();var circles=new List<PmiCircle>();
        var edges=DrawingIndexed(body,"Edges",1);
        for(int i=1;i<=Convert.ToInt32(Get(edges,"Count"));i++) {
            var edge=GetItem(edges,i);var geometry=Get(edge,"Geometry");int type=Convert.ToInt32(Get(geometry,"Type"));
            if(type==167551109) { // SE2026 analytic Line
                object[] a=[new double[3],new double[3]];CadCallRef(edge,"GetEndPoints",[0,1],a);
                var start=((Array)a[0]).Cast<object>().Select(Convert.ToDouble).ToArray();var end=((Array)a[1]).Cast<object>().Select(Convert.ToDouble).ToArray();
                var first=Get(edge,"StartVertex");var last=Get(edge,"EndVertex");
                start=PmiVector(first,"GetPointData");end=PmiVector(last,"GetPointData");
                lines.Add(new(edge,start,end,first,last));points.Add(new(first,start));points.Add(new(last,end));
            } else if(type==167551105 && Convert.ToBoolean(Get(edge,"IsClosed"))) {
                circles.Add(new(edge,PmiVector(geometry,"GetCenterPoint"),PmiVector(geometry,"GetAxisVector"),Convert.ToDouble(Get(geometry,"Radius"))));
            }
        }
        return(points,lines,circles);
    }
    public static object CadListPmi(string expectedDocument)
    {
        var doc=PmiDocument(expectedDocument,false);bool wasDirty=Convert.ToBoolean(Get(doc,"Dirty"));var pmi=PmiDesign(doc);var dimensions=Get(pmi,"Dimensions");var dims=new List<object>();
        for(int i=1;i<=Convert.ToInt32(Get(dimensions,"Count"));i++) { var d=GetItem(dimensions,i);dims.Add(new {number=i,valueNative=Convert.ToDouble(Get(d,"Value")),unitsType=Get(d,"UnitsType"),constraint=Get(d,"Constraint"),statusId=Get(d,"StatusOfDimension"),hidden=Get(d,"HidePMI"),mechCueId=PmiTag(d),plane=PmiTag(d,"Plane"),kind=PmiTag(d,"Kind")}); }
        var views=Get(pmi,"PMIModelViews");var list=new List<object>();
        for(int i=1;i<=Convert.ToInt32(Get(views,"Count"));i++) { var view=GetItem(views,i);list.Add(new {number=i,name=Get(view,"Name"),dimensions=Get(view,"DimensionCount")}); }
        return new {fullName=CadName(doc),dimensions=dims,modelViews=list,saved=!Convert.ToBoolean(Get(doc,"Dirty")),nativePmiInitialized=!wasDirty&&Convert.ToBoolean(Get(doc,"Dirty"))};
    }
    public static object CadShowPmiView(string expectedDocument,string viewName)
    {
        if(string.IsNullOrWhiteSpace(viewName))throw new ArgumentException("viewName is required.");
        var doc=PmiDocument(expectedDocument);var views=Get(PmiDesign(doc),"PMIModelViews");
        for(int i=1;i<=Convert.ToInt32(Get(views,"Count"));i++) { var view=GetItem(views,i);if(Convert.ToString(Get(view,"Name"))==viewName) {Call(view,"Apply");return new {fullName=CadName(doc),viewName};} }
        throw new ArgumentException("PMI model view was not found. Read solidedge_list_pmi first.");
    }
    public static object CadAutoPmi(string expectedDocument,string dimensionMode="features",int maxDimensions=30,bool createModelViews=true,bool recreateGenerated=false)
    {
        if(dimensionMode is not ("features" or "overall"))throw new ArgumentException("dimensionMode must be features/overall.");
        if(maxDimensions<3||maxDimensions>100)throw new ArgumentOutOfRangeException(nameof(maxDimensions),"maxDimensions must be 3..100.");
        var doc=PmiDocument(expectedDocument);var planes=PmiPlanes(doc);
        if(planes.Count!=3)throw new InvalidOperationException("The model requires the three global orthogonal reference planes.");
        var (points,lines,circles)=PmiGeometry(doc,out var assemblyBounds);if(points.Count==0&&circles.Count==0)throw new InvalidOperationException("No supported straight or circular edges were found.");
        var pmi=PmiDesign(doc);var dims=Get(pmi,"Dimensions");var removedGenerated=new List<string>();
        // Explicit request only: replaces dimensions this tool generates (user and distance PMI are kept),
        // e.g. assembly PMI created before the motion rebuild that still holds creation-time values.
        if(recreateGenerated)for(int i=Convert.ToInt32(Get(dims,"Count"));i>=1;i--){var d=GetItem(dims,i);var id=PmiTag(d);if(id!=null&&PmiAutoKinds.Contains(PmiTag(d,"Kind")??"")){Call(d,"Delete");removedGenerated.Add(id);}}
        var existing=new Dictionary<string,object>();for(int i=1;i<=Convert.ToInt32(Get(dims,"Count"));i++){var d=GetItem(dims,i);var id=PmiTag(d);if(id!=null)existing.TryAdd(id,d);}
        var added=new List<object>();var updated=new List<object>();var warnings=new List<string>();var placed=new List<PmiPlaced>();var extentDone=new HashSet<int>();
        object? Create(PmiPlane plane,string id,string kind,int type,PmiPoint a,PmiPoint? b,bool keyPoint,double expected,bool? horizontal=null)
        {
            if(existing.TryGetValue(id,out var old)) {
                try {
                    Call(old,"UpdateStatus");
                    double value=Convert.ToDouble(Get(old,"Value"));double actual=type==3?Math.Min(value,Math.PI-value):value;
                    if(!double.IsFinite(value)||Math.Abs(actual-expected)>Math.Max(1e-6,expected*1e-5))
                        throw new InvalidOperationException("Existing PMI association no longer measures the intended geometry; review it in Solid Edge.");
                    updated.Add(new {id,value=type==3?value*180/Math.PI:value*1000,unit=type==3?"deg":"mm"});return old;
                }catch(Exception e){warnings.Add($"{id}, update existing: {e.GetBaseException().Message}");return null;}
            }
            if(added.Count>=maxDimensions)return null;object? dimension=null;string stage="initialize";
            try {
                (dimension,var rebuilt,var fallback)=PmiCreateDimension(doc,dims,plane,type,a,b,keyPoint,expected,horizontal,ref stage);
                if(rebuilt==false)warnings.Add($"{id}, motion-rebuild: undo did not target the new PMI; it may keep its creation-time value until save/reopen.");
                stage="tag";double value=Convert.ToDouble(Get(dimension,"Value"));PmiTag(dimension,id,plane.Name,kind);existing.Add(id,dimension);
                added.Add(new {id,plane=plane.Name,kind,value=type==3?value*180/Math.PI:value*1000,unit=type==3?"deg":"mm",associated=true,motionRebuilt=rebuilt,orientationFallback=fallback});return dimension;
            }catch(Exception e){if(dimension!=null)try{Call(dimension,"Delete");}catch{}warnings.Add($"{id}, {stage}: {e.GetBaseException().Message}");return null;}
        }
        foreach(var plane in planes) {
            double X(double[] p)=>PmiDot(p,plane.X); double Y(double[] p)=>PmiDot(p,plane.Y);
            var all=points.Concat(circles.Select(c=>new PmiPoint(c.Parent,c.Center))).ToList();if(all.Count==0)continue;
            double xmin=all.Min(p=>X(p.Position)),xmax=all.Max(p=>X(p.Position)),ymin=all.Min(p=>Y(p.Position)),ymax=all.Max(p=>Y(p.Position));
            var left=all.OrderBy(p=>X(p.Position)).ThenBy(p=>Y(p.Position)).First();var right=all.OrderByDescending(p=>X(p.Position)).ThenBy(p=>Y(p.Position)).First();var low=all.OrderBy(p=>Y(p.Position)).ThenBy(p=>X(p.Position)).First();var high=all.OrderByDescending(p=>Y(p.Position)).ThenBy(p=>X(p.Position)).First();
            void Linear(string id,string kind,PmiPoint a,PmiPoint b,bool x,double length) {
                if(length<1e-7)return;var dim=Create(plane,id,kind,1,a,b,true,length,x);
                if(dim!=null)placed.Add(new(dim,id,plane.Name,kind,x,x?Y(a.Position):X(a.Position),x?ymin:xmin));
            }
            foreach(bool x in new[]{true,false}) {
                var vector=x?plane.X:plane.Y;int axis=Enumerable.Range(0,3).OrderByDescending(k=>Math.Abs(vector[k])).First();
                if(!extentDone.Add(axis))continue;
                double length=x?xmax-xmin:ymax-ymin;
                if(assemblyBounds!=null&&Math.Abs(length-(assemblyBounds[axis+3]-assemblyBounds[axis]))>1e-6) {
                    warnings.Add($"overall-{"XYZ"[axis]}: unsupported extremum; associated vertices/centers do not cover the assembly bounding extent. No approximate overall dimension was created.");continue;
                }
                Linear("overall-"+"XYZ"[axis],"overall",x?left:low,x?right:high,x,x?xmax-xmin:ymax-ymin);
            }
            if(dimensionMode!="features")continue;
            var visibleCircles=circles.Where(c=>Math.Abs(PmiDot(c.Normal,plane.Normal))>.999)
                .GroupBy(c=>$"{X(c.Center).ToString("F8",CultureInfo.InvariantCulture)}/{Y(c.Center).ToString("F8",CultureInfo.InvariantCulture)}/{c.Radius.ToString("F8",CultureInfo.InvariantCulture)}")
                .Select(g=>g.OrderByDescending(c=>PmiDot(c.Center,plane.Normal)).First()).OrderBy(c=>X(c.Center)).ThenBy(c=>Y(c.Center)).Take(8).ToList();
            var diameters=new HashSet<string>();int hole=0;var positions=new HashSet<string>();
            foreach(var circle in visibleCircles) {
                hole++;var center=new PmiPoint(circle.Parent,circle.Center);string radius=circle.Radius.ToString("F8",CultureInfo.InvariantCulture);
                if(diameters.Add(radius)){var dim=Create(plane,$"{plane.Name}/diameter/{radius}","diameter",4,center,null,false,2*circle.Radius);if(dim!=null){Set(dim,"TrackDistance",circle.Radius+.018);Call(dim,"SetTextOffsets",.018,.012);}}
                foreach(bool x in new[]{true,false}) {double length=x?X(circle.Center)-xmin:Y(circle.Center)-ymin;string position=$"{x}/{length.ToString("F8",CultureInfo.InvariantCulture)}";
                    if(positions.Add(position))Linear($"{plane.Name}/hole-{hole}-{(x?"x":"y")}","hole-position",x?left:low,center,x,length);
                }
            }
            var axisDone=new HashSet<bool>();var angleDone=false;
            foreach(var line in lines.OrderByDescending(l=>Math.Sqrt(l.A.Zip(l.B).Sum(p=>Math.Pow(p.First-p.Second,2))))) {
                var v=line.B.Zip(line.A).Select(p=>p.First-p.Second).ToArray();if(Math.Abs(PmiDot(v,plane.Normal))>1e-7)continue;
                double dx=Math.Abs(PmiDot(v,plane.X)),dy=Math.Abs(PmiDot(v,plane.Y));bool x=dy<1e-7;
                double length=x?dx:dy,extent=x?xmax-xmin:ymax-ymin;
                if((x||dx<1e-7)&&length>=extent*.1&&length<extent-1e-7&&axisDone.Add(x))Linear($"{plane.Name}/step-{(x?"x":"y")}","step",new(line.StartParent??Get(line.Parent,"StartVertex"),line.A),new(line.EndParent??Get(line.Parent,"EndVertex"),line.B),x,length);
                if(angleDone||dx<1e-7||dy<1e-7)continue;
                var partner=lines.FirstOrDefault(l=>new[]{l.A,l.B}.Any(p=>new[]{line.A,line.B}.Any(q=>p.Zip(q).Sum(t=>Math.Abs(t.First-t.Second))<1e-7))&&Math.Abs(PmiDot(l.B.Zip(l.A).Select(t=>t.First-t.Second).ToArray(),plane.Normal))<1e-7&&Math.Abs(Y(l.A)-Y(l.B))<1e-7&&Math.Abs(X(l.A)-X(l.B))>1e-7);
                if(partner!=null) {var angle=Math.Atan2(dy,dx);var dim=Create(plane,$"{plane.Name}/inclination","angle",3,new(line.Parent,line.A),new(partner.Parent,partner.A),false,angle);if(dim!=null){Set(dim,"TrackDistance",.025);angleDone=true;}}
            }
        }
        foreach(var group in placed.GroupBy(p=>(p.Plane,p.Horizontal))) {int lane=0;foreach(var row in group.OrderBy(p=>Convert.ToDouble(Get(p.Dimension,"Value"))).ThenBy(p=>p.Id,StringComparer.Ordinal)) {
            // TrackDistance is the model-plane offset; PMITrackDistance uses a different native placement convention.
            double track=row.Edge-row.Origin-(.015+.01*lane++);try {if(Math.Abs(Convert.ToDouble(Get(row.Dimension,"TrackDistance"))-track)>1e-7)Set(row.Dimension,"TrackDistance",track);}catch(Exception e){warnings.Add($"{row.Id}, placement: {e.GetBaseException().Message}");}
        }}
        Set(pmi,"ShowDimensions",true);var viewNames=new List<string>();
        if(createModelViews) {
            var views=Get(pmi,"PMIModelViews");
            foreach(var plane in planes) {
                var members=existing.Values.Where(d=>PmiTag(d,"Plane")==plane.Name).ToList();if(members.Count==0)continue;
                string name="MechCue PMI "+plane.Name;
                try {
                    var view=PmiModelView(views,plane,out bool fresh);
                    var remove=new List<object>();for(int i=1;i<=Convert.ToInt32(Get(view,"DimensionCount"));i++){var dim=DrawingIndexed(view,"DimensionItem",i);if(fresh||PmiTag(dim)!=null)remove.Add(dim);}
                    foreach(var dim in remove)Call(view,"RemoveDimAnnotOrSectionFromView",dim);
                    foreach(var dim in members)Call(view,"AddDimAnnotOrSectionToView",dim);
                    viewNames.Add(name);
                }catch(Exception e){warnings.Add($"{name}: {e.GetBaseException().Message}");}
            }
            if(viewNames.Count>0)CadShowPmiView(expectedDocument,viewNames[0]);
        }
        // New assembly PMI is rebuilt once at creation (PmiRebuildForMotion) so it follows motion.
        // Do not add per-frame UpdateStatus/UpdateAll, or implicitly save/close the document.
        bool newAssemblyPmi=CadExtension(doc)==".asm"&&added.Count>0;
        return new {fullName=CadName(doc),added,updated,removedGenerated,modelViews=viewNames,warnings,state=CadListPmi(expectedDocument),geometryModified=false,saved=false,motionFollowVerified=false,
            note=newAssemblyPmi
                ? "Associated assembly PMI created and rebuilt once via undo/redo of its creation step (motionRebuilt=true), which makes it follow occurrence motion without per-frame recalculation. Verify motion before claiming success. Dimensions with motionRebuilt=false, or generated by earlier versions (reported in updated), may keep creation-time values; rerun with recreateGenerated=true or save/reopen."
                : "Associated reference PMI; manufacturing datums and tolerances are not inferred. Save explicitly after review."};
    }
}
