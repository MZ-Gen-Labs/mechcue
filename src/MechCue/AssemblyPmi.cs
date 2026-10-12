using System.Globalization;
namespace MechCue;

public sealed partial class Bridge
{
    internal static double[] PmiTransform(double[] matrix,double[] point,bool direction=false)
    {
        if(point.Length!=3||point.Any(v=>!double.IsFinite(v))||!MotionBoundMath.Rigid(matrix))throw new InvalidOperationException("Assembly PMI requires finite coordinates and rigid occurrence transforms");
        return Enumerable.Range(0,3).Select(k=>matrix[k]*point[0]+matrix[k+4]*point[1]+matrix[k+8]*point[2]+(direction?0:matrix[k+12])).ToArray();
    }
    static (List<PmiPoint> points,List<PmiEdge> lines,List<PmiCircle> circles) PmiAssemblyGeometry(object assembly,out double[] bounds)
    {
        var points=new List<PmiPoint>();var lines=new List<PmiEdge>();var circles=new List<PmiCircle>();
        int occurrences=0;var ancestors=new List<object>();var corners=new List<double[]>();
        void Visit(object owner,double[] parent,List<(object Owner,object Occurrence)> chain,int depth)
        {
            if(depth>16||ancestors.Any(d=>Equals(d,owner)))throw new InvalidOperationException("Assembly PMI hierarchy is cyclic or exceeds 16 levels");
            ancestors.Add(owner);
            try {
                var items=Get(owner,"Occurrences");
                for(int i=1;i<=Convert.ToInt32(Get(items,"Count"));i++) {
                    if(++occurrences>1000)throw new InvalidOperationException("Assembly PMI supports at most 1000 occurrences");
                    var occurrence=GetItem(items,i);
                    if(Convert.ToBoolean(Get(occurrence,"Adjustable"))||Convert.ToBoolean(Get(occurrence,"IsAdjustablePart"))||
                        Convert.ToBoolean(Get(occurrence,"UseSimplified"))||Convert.ToBoolean(Get(occurrence,"HasBodyOverride")))
                        throw new InvalidOperationException("Assembly PMI requires rigid design-state occurrences without adjustable or assembly-overridden geometry");
                    var document=Get(occurrence,"OccurrenceDocument");
                    var matrix=ConceptMachine.Multiply(parent,Matrix(occurrence));
                    if(!MotionBoundMath.Rigid(matrix))throw new InvalidOperationException("Scaled/non-rigid occurrences are unsupported for associated PMI");
                    var path=chain.Append((owner,occurrence)).ToList();
                    if(CadExtension(document)==".asm"){Visit(document,matrix,path,depth+1);continue;}
                    if(CadExtension(document) is not (".par" or ".psm"))throw new InvalidOperationException("Unsupported occurrence document for assembly PMI: "+CadName(document));
                    object Reference(object entity) {
                        for(int j=path.Count-1;j>=0;j--)entity=Call(path[j].Item1,"CreateReference",path[j].Item2,entity);
                        return entity;
                    }
                    var models=Get(document,"Models");
                    if(Convert.ToInt32(Get(models,"Count"))==0)throw new InvalidOperationException("Assembly PMI component has no loaded solid body: "+CadName(document));
                    for(int m=1;m<=Convert.ToInt32(Get(models,"Count"));m++) {
                        var body=Get(GetItem(models,m),"Body");
                        // AssemblyDocument.Range includes construction geometry (including reference planes).
                        // Use conservative solid-body ranges to reject unsupported overall extrema instead.
                        object[] range=[new double[3],new double[3]];CadCallRef(body,"GetRange",[0,1],range);
                        var min=((Array)range[0]).Cast<object>().Select(Convert.ToDouble).ToArray();
                        var max=((Array)range[1]).Cast<object>().Select(Convert.ToDouble).ToArray();
                        for(int mask=0;mask<8;mask++)corners.Add(PmiTransform(matrix,Enumerable.Range(0,3).Select(k=>(mask&(1<<k))==0?min[k]:max[k]).ToArray()));
                        var geometry=PmiBodyGeometry(body);
                        if(points.Count+lines.Count+circles.Count+geometry.points.Count+geometry.lines.Count+geometry.circles.Count>100000)
                            throw new InvalidOperationException("Assembly PMI geometry budget exceeded before creating dimensions");
                        var references=new Dictionary<object,object>();
                        object Ref(object entity){if(!references.TryGetValue(entity,out var reference))references.Add(entity,reference=Reference(entity));return reference;}
                        points.AddRange(geometry.points.Select(p=>new PmiPoint(Ref(p.Parent),PmiTransform(matrix,p.Position))));
                        lines.AddRange(geometry.lines.Select(e=>new PmiEdge(Ref(e.Parent),PmiTransform(matrix,e.A),PmiTransform(matrix,e.B),Ref(e.StartParent!),Ref(e.EndParent!))));
                        circles.AddRange(geometry.circles.Select(c=>new PmiCircle(Ref(c.Parent),PmiTransform(matrix,c.Center),PmiTransform(matrix,c.Normal,true),c.Radius)));
                    }
                }
            } finally { ancestors.RemoveAt(ancestors.Count-1); }
        }
        Visit(assembly,CadTransform(0,0,0,0,0,0),[],1);
        if(corners.Count==0)throw new InvalidOperationException("Assembly PMI requires solid occurrences");
        bounds=Enumerable.Range(0,3).Select(k=>corners.Min(p=>p[k])).Concat(Enumerable.Range(0,3).Select(k=>corners.Max(p=>p[k]))).ToArray();
        return(points,lines,circles);
    }
    internal sealed record PmiPointSpec(bool Circle,int[] Corner,int Axis,bool Max);
    // "vertex:min,mid,max" = solid vertex nearest that corner of the part's world vertex extents;
    // "circle:zmin" = centre of the closed circular edge with the lowest (or highest) world Z.
    internal static PmiPointSpec PmiParsePoint(string spec)
    {
        var text=(spec??"").Trim().ToLowerInvariant();
        if(text.StartsWith("vertex:")) {
            var parts=text[7..].Split(',');
            if(parts.Length==3&&parts.All(p=>p is "min" or "mid" or "max"))return new(false,parts.Select(p=>p=="min"?-1:p=="mid"?0:1).ToArray(),-1,false);
        } else if(text.StartsWith("circle:")&&text.Length==11&&"xyz".Contains(text[7])&&text[8..] is "min" or "max")
            return new(true,[],"xyz".IndexOf(text[7]),text[8..]=="max");
        throw new ArgumentException("point must be vertex:<min|mid|max>,<min|mid|max>,<min|mid|max> (world X,Y,Z) or circle:<x|y|z><min|max>.");
    }
    internal static string PmiDistancePlane(string axis,string plane)
    {
        if(axis is not ("X" or "Y" or "Z"))throw new ArgumentException("axis must be X/Y/Z.");
        if(plane=="AUTO")return axis=="Z"?"XZ":"XY";
        if(plane is not ("XY" or "XZ" or "YZ"))throw new ArgumentException("plane must be auto/XY/XZ/YZ.");
        if(!plane.Contains(axis))throw new ArgumentException($"plane {plane} does not contain axis {axis}.");
        return plane;
    }
    // part: 1-based top-level number, or occurrence names separated by '/' for nested occurrences.
    static (PmiPoint point,string part) PmiResolvePoint(object assembly,string part,PmiPointSpec spec)
    {
        if(string.IsNullOrWhiteSpace(part))throw new ArgumentException("part is required: a top-level number or occurrence name path.");
        object owner=assembly;var chain=new List<(object Owner,object Occurrence)>();var matrix=CadTransform(0,0,0,0,0,0);var names=new List<string>();
        foreach(var segment in part.Split('/').Select(t=>t.Trim())) {
            if(CadExtension(owner)!=".asm")throw new ArgumentException("Only assemblies contain occurrences: "+CadName(owner));
            var items=Get(owner,"Occurrences");int count=Convert.ToInt32(Get(items,"Count"));object? found=null;
            if(int.TryParse(segment,NumberStyles.None,CultureInfo.InvariantCulture,out int number)&&number>=1&&number<=count)found=GetItem(items,number);
            else for(int i=1;i<=count&&found==null;i++){var item=GetItem(items,i);if(string.Equals(Convert.ToString(Get(item,"Name")),segment,StringComparison.OrdinalIgnoreCase))found=item;}
            if(found==null)throw new ArgumentException($"Occurrence '{segment}' was not found in {CadName(owner)}. Read solidedge_get_assembly_tree.");
            if(Convert.ToBoolean(Get(found,"Adjustable"))||Convert.ToBoolean(Get(found,"IsAdjustablePart"))||Convert.ToBoolean(Get(found,"UseSimplified"))||Convert.ToBoolean(Get(found,"HasBodyOverride")))
                throw new InvalidOperationException("Assembly PMI requires rigid design-state occurrences without adjustable or assembly-overridden geometry");
            matrix=ConceptMachine.Multiply(matrix,Matrix(found));
            if(!MotionBoundMath.Rigid(matrix))throw new InvalidOperationException("Scaled/non-rigid occurrences are unsupported for associated PMI");
            chain.Add((owner,found));names.Add(Convert.ToString(Get(found,"Name"))!);owner=Get(found,"OccurrenceDocument");
        }
        if(CadExtension(owner) is not (".par" or ".psm"))throw new ArgumentException("part must end at a .par/.psm occurrence: "+CadName(owner));
        var candidates=new List<PmiPoint>();var models=Get(owner,"Models");
        for(int m=1;m<=Convert.ToInt32(Get(models,"Count"));m++) {
            var geometry=PmiBodyGeometry(Get(GetItem(models,m),"Body"));
            if(spec.Circle)candidates.AddRange(geometry.circles.Select(c=>new PmiPoint(c.Parent,PmiTransform(matrix,c.Center))));
            else candidates.AddRange(geometry.points.Select(p=>new PmiPoint(p.Parent,PmiTransform(matrix,p.Position))));
        }
        if(candidates.Count==0)throw new InvalidOperationException($"{string.Join('/',names)} has no {(spec.Circle?"closed circular edges":"solid vertices")}.");
        PmiPoint chosen;
        if(spec.Circle)chosen=spec.Max?candidates.OrderByDescending(c=>c.Position[spec.Axis]).First():candidates.OrderBy(c=>c.Position[spec.Axis]).First();
        else {
            var target=Enumerable.Range(0,3).Select(k=>{double lo=candidates.Min(c=>c.Position[k]),hi=candidates.Max(c=>c.Position[k]);return spec.Corner[k]<0?lo:spec.Corner[k]>0?hi:(lo+hi)/2;}).ToArray();
            chosen=candidates.OrderBy(c=>c.Position.Zip(target).Sum(t=>(t.First-t.Second)*(t.First-t.Second))).First();
        }
        object entity=chosen.Parent;for(int j=chain.Count-1;j>=0;j--)entity=Call(chain[j].Owner,"CreateReference",chain[j].Occurrence,entity);
        return(new PmiPoint(entity,chosen.Position),string.Join('/',names));
    }
    public static object CadAddPmiDistance(string expectedDocument,string id,string fromPart,string fromPoint,string toPart,string toPoint,string axis,string plane="auto",double lineOffsetMm=0,bool replace=false,bool addToModelView=true)
    {
        id=(id??"").Trim();if(id.Length is 0 or >100)throw new ArgumentException("id is required (1..100 characters).");
        axis=(axis??"").Trim().ToUpperInvariant();var planeName=PmiDistancePlane(axis,(plane??"auto").Trim().ToUpperInvariant());
        var fromSpec=PmiParsePoint(fromPoint);var toSpec=PmiParsePoint(toPoint);CadFinite(lineOffsetMm,nameof(lineOffsetMm));
        var doc=PmiDocument(expectedDocument);if(CadExtension(doc)!=".asm")throw new InvalidOperationException("solidedge_add_pmi_distance requires an .asm assembly.");
        var target=PmiPlanes(doc).FirstOrDefault(p=>p.Name==planeName)??throw new InvalidOperationException("The model requires the global "+planeName+" reference plane.");
        var pmi=PmiDesign(doc);var dims=Get(pmi,"Dimensions");
        var (a,fromName)=PmiResolvePoint(doc,fromPart,fromSpec);var (b,toName)=PmiResolvePoint(doc,toPart,toSpec);
        int k="XYZ".IndexOf(axis[0]);double expected=Math.Abs(b.Position[k]-a.Position[k]);
        if(expected<1e-7)throw new InvalidOperationException($"The selected points coincide along {axis}; no distance PMI was created.");
        bool replaced=false;
        for(int i=Convert.ToInt32(Get(dims,"Count"));i>=1;i--){var d=GetItem(dims,i);if(PmiTag(d)!=id)continue;if(!replace)throw new InvalidOperationException($"PMI id '{id}' already exists; pass replace=true to recreate it.");Call(d,"Delete");replaced=true;}
        int xAxis=Enumerable.Range(0,3).OrderByDescending(n=>Math.Abs(target.X[n])).First();bool horizontal=k==xAxis;
        string stage="initialize";var (dimension,rebuilt,fallback)=PmiCreateDimension(doc,dims,target,1,a,b,true,expected,horizontal,ref stage);
        var warnings=new List<string>();string? viewName=null;
        try {
            double Across(double[] p)=>PmiDot(p,horizontal?target.Y:target.X);
            int lane=Enumerable.Range(1,Convert.ToInt32(Get(dims,"Count"))).Select(i=>GetItem(dims,i)).Count(d=>PmiTag(d,"Kind")=="distance"&&PmiTag(d,"Plane")==planeName);
            // TrackDistance is the model-plane offset from the first point. Default: outside both
            // points, one lane per existing distance PMI on this plane.
            double track=lineOffsetMm!=0?lineOffsetMm/1000:Math.Min(Across(a.Position),Across(b.Position))-Across(a.Position)-(.02+.012*lane);
            Set(dimension,"TrackDistance",track);PmiTag(dimension,id,planeName,"distance");
        }catch{try{Call(dimension,"Delete");}catch{}throw;}
        if(rebuilt==false)warnings.Add("motion-rebuild: undo did not target the new PMI; it may keep its creation-time value until save/reopen.");
        if(addToModelView)try {
            viewName="MechCue PMI "+planeName;var view=PmiModelView(Get(pmi,"PMIModelViews"),target,out bool fresh);
            if(fresh) {
                var remove=new List<object>();for(int i=1;i<=Convert.ToInt32(Get(view,"DimensionCount"));i++)remove.Add(DrawingIndexed(view,"DimensionItem",i));
                foreach(var d in remove)Call(view,"RemoveDimAnnotOrSectionFromView",d);
                for(int i=1;i<=Convert.ToInt32(Get(dims,"Count"));i++){var d=GetItem(dims,i);if(PmiTag(d,"Plane")==planeName)Call(view,"AddDimAnnotOrSectionToView",d);}
            } else Call(view,"AddDimAnnotOrSectionToView",dimension);
        }catch(Exception e){warnings.Add($"{viewName}: {e.GetBaseException().Message}");}
        double Mm(double v)=>Math.Round(v*1000,6);
        return new {fullName=CadName(doc),id,plane=planeName,axis,valueMm=Mm(Convert.ToDouble(Get(dimension,"Value"))),
            from=new {part=fromName,point=fromPoint,positionMm=a.Position.Select(Mm).ToArray()},to=new {part=toName,point=toPoint,positionMm=b.Position.Select(Mm).ToArray()},
            motionRebuilt=rebuilt,orientationFallback=fallback,replaced,modelView=viewName,warnings,geometryModified=false,saved=false,motionFollowVerified=false,
            note="Associated assembly distance PMI rebuilt once after creation so it follows occurrence motion without recalculation. Verify motion before claiming success. The model view is not applied (use solidedge_show_pmi_view). Save explicitly after review."};
    }
}
