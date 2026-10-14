using System.Text.Json;
namespace MechCue;
public sealed class SavedConcept
{
    public ConceptMachine Model { get; set; } = new();
    public Dictionary<string,Guid> Tracks { get; set; } = [];
    public Dictionary<string,double> Baseline { get; set; } = [];
    public MechCue.Physics.ConceptPhysicsSettings? Physics { get; set; }
}
public sealed partial class Bridge
{
    SavedConcept? concept;
    readonly Dictionary<string,Track> conceptTracks=[];
    readonly Dictionary<string,object> conceptParts=[];
    bool conceptActive;
    internal double ConceptCurrentValue(Track t){Check();var values=MeasureConcept(concept!.Model,ConceptSnapshot());return values[conceptTracks.Single(p=>p.Value==t).Key];}
    internal bool IsConcept(Track t)=>conceptTracks.Values.Contains(t);
    internal string? ConceptLabel(Track t)=>conceptTracks.FirstOrDefault(p=>p.Value==t).Key is string id ? "概略軸 / "+id : null;
    internal SavedConcept? CaptureConcept()=>concept;
    internal void RetainConcept(SavedConcept saved){concept=saved;}
    internal void ValidateConceptPoints(Track track,IEnumerable<KeyPoint> points){if(concept==null)return;var key=concept.Tracks.FirstOrDefault(p=>p.Value==track.Id).Key;if(key==null)return;var a=concept.Model.Axes.Single(a=>a.Id==key);if(points.Any(p=>p.Value<a.Minimum || p.Value>a.Maximum))throw new InvalidOperationException($"概略軸 {key} の範囲は {a.Minimum} ～ {a.Maximum} です。");}
    internal void ClearConcept(){concept=null;conceptTracks.Clear();conceptParts.Clear();conceptActive=false;}
    static double[] InverseRigid(double[] m) {
        double[] r=[m[0],m[4],m[8],0,m[1],m[5],m[9],0,m[2],m[6],m[10],0,0,0,0,1];
        for(int i=0;i<3;i++)r[12+i]=-(r[i]*m[12]+r[4+i]*m[13]+r[8+i]*m[14]);return r;
    }
    // A body carried by linear X/Y/Z axes followed by yaw (Z), pitch (Y) and roll (X) rotary axes about its own centre
    // (arm5p workpiece). Only the last axis has a body, so the whole chain is read from that body's pose at once.
    static bool MeasureFreeBodyChain(ConceptMachine model,ConceptAxis start,Dictionary<string,double[]> actual,Dictionary<string,double[]> parents,Dictionary<string,double> values) {
        foreach(var body in model.Bodies){
            var chain=new List<ConceptAxis>();for(var id=body.Parent;id!="";){var axis=model.Axes.Single(x=>x.Id==id);chain.Insert(0,axis);id=axis.Parent;}
            if(chain.Count==0||chain[0].Id!=start.Id)continue;
            int linear=chain.TakeWhile(x=>x.Kind=="linear").Count();int rotary=chain.Count-linear;
            if(linear==0||rotary==0||chain.Skip(linear).Any(x=>x.Kind!="rotary")||chain.Take(linear).Select(x=>x.Direction).Distinct().Count()!=linear)return false;
            if(!chain.Skip(linear).Select(x=>x.Direction).SequenceEqual("ZYX".Take(rotary).Select(c=>c.ToString())))return false;
            if(chain.Skip(linear+1).Any(x=>x.OriginMm.Any(v=>v!=0)))return false;
            if(model.Bodies.Any(b=>b!=body&&chain.Any(x=>x.Id==b.Parent)))return false;
            var local=ConceptMachine.Multiply(ConceptMachine.Multiply(ConceptMachine.Multiply(InverseRigid(parents[start.Parent]),actual[body.Id]),InverseRigid(body.GeometryMatrix)),CadTransform(-body.CenterMm[0],-body.CenterMm[1],-body.CenterMm[2],0,0,0));
            double[] angles=[0,0,0]; // yaw, pitch, roll
            double R(int row,int col)=>local[col*4+row];
            if(rotary>=1){
                double pitch=rotary>=2?Math.Atan2(-R(2,0),Math.Sqrt(R(2,1)*R(2,1)+R(2,2)*R(2,2))):0;
                double yaw=Math.Atan2(R(1,0),R(0,0)),roll=rotary==3?Math.Atan2(R(2,1),R(2,2)):0;
                if(rotary==3&&Math.Cos(pitch)<1e-9){roll=0;yaw=Math.Atan2(-R(0,1),R(1,1));}
                angles=[yaw*180/Math.PI,pitch*180/Math.PI,roll*180/Math.PI];
            }
            double[] total=[0,0,0];foreach(var axis in chain)for(int k=0;k<3;k++)total[k]+=axis.OriginMm[k];
            var frame=parents[start.Parent];
            for(int i=0;i<chain.Count;i++){
                var axis=chain[i];int k=axis.Direction=="X"?0:axis.Direction=="Y"?1:2;double v;
                if(axis.Kind=="linear")v=local[12+k]*1000-total[k];else v=angles[axis.Direction=="Z"?0:axis.Direction=="Y"?1:2];
                if(Math.Abs(v-axis.Minimum)<1e-6)v=axis.Minimum;if(Math.Abs(v-axis.Maximum)<1e-6)v=axis.Maximum;if(Math.Abs(v)<1e-7)v=0;
                values[axis.Id]=v;
                double[] xyz=(double[])axis.OriginMm.Clone(),r=new double[3];if(axis.Kind=="linear")xyz[k]+=v;else r[k]=v;
                frame=ConceptMachine.Multiply(frame,CadTransform(xyz[0],xyz[1],xyz[2],r[0],r[1],r[2]));parents[axis.Id]=frame;
            }
            return true;
        }
        return false;
    }
    /// <summary>Axis values (mm / degrees) that put a free body (linear X/Y/Z axes, then yaw/pitch/roll) at an occurrence pose (4x4 column-major, metres). Angles are principal values; unwrap them over time yourself.</summary>
    public static Dictionary<string,double> MeasureFreeBody(ConceptMachine model,string bodyId,double[] occurrencePose) {
        var body=model.Bodies.Single(b=>b.Id==bodyId);var first=body.Parent;
        for(var id=body.Parent;id!="";id=model.Axes.Single(x=>x.Id==id).Parent)first=id;
        var values=new Dictionary<string,double>();var parents=new Dictionary<string,double[]>{[""]=CadTransform(0,0,0,0,0,0)};
        if(first==""||!MeasureFreeBodyChain(model,model.Axes.Single(x=>x.Id==first),new Dictionary<string,double[]>{[bodyId]=occurrencePose},parents,values))throw new ArgumentException("Body is not carried by a free-body axis chain: "+bodyId);
        return values;
    }
    internal static Dictionary<string,double> MeasureConcept(ConceptMachine model,Dictionary<string,double[]> actual) {
        var values=new Dictionary<string,double>();var parents=new Dictionary<string,double[]>{[""]=CadTransform(0,0,0,0,0,0)};
        foreach(var a in model.Axes){
            if(values.ContainsKey(a.Id))continue; // solved together with the rest of its free-body chain
            var witness=model.Bodies.FirstOrDefault(b=>b.Parent==a.Id);
            if(witness==null){
                if(MeasureFreeBodyChain(model,a,actual,parents,values))continue;
                // A linear axis carrying only further linear axes along other directions (e.g. arm5 WX->WY->WZ)
                // is measured from a descendant body: its direction's component comes from this axis alone.
                List<ConceptAxis>? Path(ConceptBody b){var path=new List<ConceptAxis>();for(var id=b.Parent;id!="";){var axis=model.Axes.Single(x=>x.Id==id);path.Add(axis);if(id==a.Id)return path;id=axis.Parent;}return null;}
                var (body,path)=model.Bodies.Select(b=>(b,Path(b))).FirstOrDefault(p=>p.Item2!=null);
                if(body==null||a.Kind!="linear"||path!.Any(x=>x.Kind!="linear")||path.Select(x=>x.Direction).Distinct().Count()!=path.Count)throw new InvalidOperationException("Axis has no measurement body: "+a.Id);
                var offset=ConceptMachine.Multiply(ConceptMachine.Multiply(ConceptMachine.Multiply(InverseRigid(parents[a.Parent]),actual[body.Id]),InverseRigid(body.GeometryMatrix)),CadTransform(-body.CenterMm[0],-body.CenterMm[1],-body.CenterMm[2],0,0,0));
                int k=a.Direction=="X"?0:a.Direction=="Y"?1:2;
                double measured=offset[12+k]*1000-path.Sum(x=>x.OriginMm[k]);
                if(Math.Abs(measured-a.Minimum)<1e-6)measured=a.Minimum;if(Math.Abs(measured-a.Maximum)<1e-6)measured=a.Maximum;if(Math.Abs(measured)<1e-7)measured=0;values[a.Id]=measured;
                double[] xyz=(double[])a.OriginMm.Clone();xyz[k]+=measured;parents[a.Id]=ConceptMachine.Multiply(parents[a.Parent],CadTransform(xyz[0],xyz[1],xyz[2],0,0,0));
                continue;
            }
            var local=ConceptMachine.Multiply(ConceptMachine.Multiply(ConceptMachine.Multiply(InverseRigid(parents[a.Parent]),actual[witness.Id]),InverseRigid(witness.GeometryMatrix)),CadTransform(-witness.CenterMm[0],-witness.CenterMm[1],-witness.CenterMm[2],0,0,0));
            int n=a.Direction=="X"?0:a.Direction=="Y"?1:2;
            double value=a.Kind=="linear"?local[12+n]*1000-a.OriginMm[n]:Math.Atan2(local[((n+1)%3)*4+(n+2)%3],local[((n+1)%3)*4+(n+1)%3])*180/Math.PI;
            if(Math.Abs(value-a.Minimum)<1e-6)value=a.Minimum;if(Math.Abs(value-a.Maximum)<1e-6)value=a.Maximum;if(Math.Abs(value)<1e-7)value=0;values[a.Id]=value;
            parents[a.Id]=ConceptMachine.Multiply(parents[a.Parent],local);
        }
        model.ValidateValues(values);var wanted=model.Poses(values);
        if(wanted.Any(p=>!ConceptSame(p.Value,actual[p.Key])))throw new InvalidOperationException("現在の部品配置を概略軸で再現できません。手動変更した配置を確認してください。");return values;
    }
    internal List<Track> ImportConcept(string manifest,IEnumerable<Track> existing) {
        Check();if(bindings.Count>0)throw new InvalidOperationException("既存の駆動先を解除してから概略軸を取り込んでください。");
        var (model,document,parts)=ConceptLoad(CadName(doc!),manifest,false,app!);
        var current=MeasureConcept(model,parts.ToDictionary(p=>p.Key,p=>Matrix(p.Value)));
        if(concept!=null){if(JsonSerializer.Serialize(concept.Model,ConceptMachine.JsonOptions)!=JsonSerializer.Serialize(model,ConceptMachine.JsonOptions))throw new InvalidOperationException("別の概略定義が登録済みです。");if(conceptTracks.Count==0)RestoreConcept(concept,existing);return conceptTracks.Values.ToList();}
        double end=Math.Max(4,existing.Max(t=>t.Points[^1].Time));
        var added=model.Axes.Select(a=>new Track{Name=a.Id,Kind=a.Kind=="rotary"?"部品回転":"部品移動",Axis=a.Direction,Points=[new(0,current[a.Id]),new(end,current[a.Id])]}).ToList();
        var saved=new SavedConcept{Model=model,Baseline=current,Tracks=model.Axes.Select((a,i)=>(a.Id,added[i].Id)).ToDictionary(p=>p.Item1,p=>p.Item2)};
        RestoreConcept(saved,added);return added;
    }
    internal void RestoreConcept(SavedConcept saved,IEnumerable<Track> tracks) {
        Check();saved.Model.Validate();saved.Model.ValidateValues(saved.Baseline);
        var all=tracks.ToDictionary(t=>t.Id);var bound=new Dictionary<string,Track>();var parts=new Dictionary<string,object>();
        foreach(var a in saved.Model.Axes){if(!saved.Tracks.TryGetValue(a.Id,out var id)||!all.TryGetValue(id,out var t)||t.Kind!=(a.Kind=="rotary"?"部品回転":"部品移動")||t.Axis!=a.Direction)throw new InvalidOperationException("概略軸の保存したグラフが一致しません。");bound[a.Id]=t;}
        string dir=Path.GetDirectoryName(CadName(doc!))!;var occurrences=Get(doc!,"Occurrences");
        foreach(var b in saved.Model.Bodies){if(Path.GetFileName(b.File)!=b.File)throw new InvalidDataException("Invalid concept part path");var matches=Enumerable.Range(1,Convert.ToInt32(Get(occurrences,"Count"))).Select(i=>GetItem(occurrences,i)).Where(o=>Convert.ToString(Get(o,"Name"))==b.Occurrence&&string.Equals(CadName(Get(o,"OccurrenceDocument")),Path.Combine(dir,b.File),StringComparison.OrdinalIgnoreCase)).ToList();if(matches.Count!=1)throw new InvalidOperationException("概略部品が見つからないか変更されています："+b.Id);parts[b.Id]=matches[0];}
        concept=saved;conceptTracks.Clear();foreach(var p in bound)conceptTracks[p.Key]=p.Value;conceptParts.Clear();foreach(var p in parts)conceptParts[p.Key]=p.Value;conceptActive=false;
    }
    internal void ApplyConcept(double time) { if(concept==null)return;var values=conceptTracks.ToDictionary(p=>p.Key,p=>p.Value.At(time));concept.Model.ValidateValues(values);foreach(var a in concept.Model.Axes)if(conceptTracks[a.Id].Kind!=(a.Kind=="rotary"?"部品回転":"部品移動")||conceptTracks[a.Id].Axis!=a.Direction)throw new InvalidOperationException("概略軸の駆動方法は変更できません。");SetConceptPose(values);conceptActive=true; }
    void SetConceptPose(Dictionary<string,double> values) {
        var poses=concept!.Model.Poses(values);var before=ConceptSnapshot();
        foreach(var p in conceptParts.Values){var rel=Get(p,"Relations3d");for(int i=1;i<=Convert.ToInt32(Get(rel,"Count"));i++){var r=GetItem(rel,i);if(Convert.ToInt32(Get(r,"Type"))!=1959028688&&!Convert.ToBoolean(Get(r,"Suppress")))throw new InvalidOperationException("概略軸に固定以外の有効な拘束があります。");}}
        try{foreach(var p in poses)CadCallRef(conceptParts[p.Key],"PutMatrix",[0],p.Value,true);if(poses.Any(p=>!ConceptSame(Matrix(conceptParts[p.Key]),p.Value)))throw new InvalidOperationException("概略軸の姿勢が指定値と一致しません。");}
        catch{RestoreConceptSnapshot(before);throw;}
    }
    Dictionary<string,double[]> ConceptSnapshot()=>conceptParts.ToDictionary(p=>p.Key,p=>Matrix(p.Value));
    void RestoreConceptSnapshot(Dictionary<string,double[]> before){foreach(var p in before)CadCallRef(conceptParts[p.Key],"PutMatrix",[0],p.Value,true);if(before.Any(p=>!ConceptSame(Matrix(conceptParts[p.Key]),p.Value)))throw new InvalidOperationException("概略部品の姿勢を復元できませんでした。");}
    internal void RestoreConceptBaseline(){if(concept!=null&&conceptActive){SetConceptPose(concept.Baseline);conceptActive=false;}}
}
public partial class MainForm
{
    void ImportConceptAxes(string path="", string unboundTracks="preserve") {
        if (unboundTracks is not ("preserve" or "hide")) throw new ArgumentException("unboundTracks must be preserve or hide.");
        if(!bridge.Connected)ConnectDocument();PausePlayback();live.Checked=false;Commit();
        if(path==""){string dir=Path.GetDirectoryName(bridge.DocumentPath)!;path=Path.Combine(dir,"mechcue-concept.json");if(!File.Exists(path)){using var dialog=new OpenFileDialog{Filter="Concept JSON|*.json"};if(dialog.ShowDialog(this)!=DialogResult.OK)return;path=dialog.FileName;}}
        var added=bridge.ImportConcept(path,tracks);foreach(var t in added)if(!tracks.Contains(t))tracks.Add(t);
        if (unboundTracks == "hide") foreach (var t in tracks)
            if (!bridge.IsConcept(t) && bridge.BoundLabel(t) == null && bridge.PendingLabel(t) == null) plot.Hidden.Add(t);
        MarkPhysicsTracks();ClearEditHistory();RefreshTracks(tracks.IndexOf(added[0]));MarkDocumentSettingsChanged();status.Text="概略軸を取り込みました。現在値で初期化し、反映はオフです。";
    }
}
