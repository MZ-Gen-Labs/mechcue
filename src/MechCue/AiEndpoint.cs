using System.IO.Pipes;
using System.Text;
using System.Text.Json;
namespace MechCue;

public sealed record AiSession(string Id,string Pipe,string Title,int ProcessId);
public sealed class AiEndpoint : IDisposable
{
    public static string SessionDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"MechCue","Sessions");
    readonly CancellationTokenSource stop=new();
    readonly Func<JsonElement,CancellationToken,Task<object>> handle;
    readonly string file;
    readonly Func<bool>? ready;
    readonly Func<CancellationToken,Task<bool>>? probeUi;
    public AiSession Session { get; }
    public AiEndpoint(string title,Func<JsonElement,CancellationToken,Task<object>> handle,Func<bool>? ready=null,Func<CancellationToken,Task<bool>>? probeUi=null)
    {
        this.handle=handle;this.ready=ready;this.probeUi=probeUi;string id=Guid.NewGuid().ToString("N");
        Session=new(id,"MechCue-"+id,title,Environment.ProcessId);
        Directory.CreateDirectory(SessionDirectory);file=Path.Combine(SessionDirectory,id+".json");
        File.WriteAllText(file,JsonSerializer.Serialize(Session));_ = Task.Run(Serve);
    }
    async Task Serve()
    {
        while(!stop.IsCancellationRequested)
        {
            try
            {
                var pipe=new NamedPipeServerStream(Session.Pipe,PipeDirection.InOut,16,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
                try{await pipe.WaitForConnectionAsync(stop.Token);}
                catch{pipe.Dispose();throw;}
                _ = Respond(pipe);
            }
            catch(OperationCanceledException) when(stop.IsCancellationRequested){break;}
            catch(Exception ex){System.Diagnostics.Trace.WriteLine(ex);if(!stop.IsCancellationRequested)await Task.Delay(100,stop.Token).ConfigureAwait(false);}
        }
    }
    readonly AiOperations operations = new();
    async Task Respond(NamedPipeServerStream pipe)
    {
        using (pipe)
        try
        {
            using var reader=new StreamReader(pipe,new UTF8Encoding(false),false,4096,true);
            using var writer=new StreamWriter(pipe,new UTF8Encoding(false),4096,true){AutoFlush=true};
            using var readTimeout=CancellationTokenSource.CreateLinkedTokenSource(stop.Token);readTimeout.CancelAfter(TimeSpan.FromSeconds(10));
            object response;
            try
            {
                string line=await reader.ReadLineAsync(readTimeout.Token) ?? throw new InvalidDataException("Empty request");
                if(line.Length>1048576)throw new InvalidDataException("Request too large (1 MiB maximum)");
                using var parsed=JsonDocument.Parse(line);var request=parsed.RootElement.Clone();
                string method=request.GetProperty("method").GetString()!;
                object result;
                if(method=="ping")result=new {session=Session,reachable=true,uiReady=ready?.Invoke()??true,uiResponsive=probeUi==null?(bool?)null:await probeUi(stop.Token).ConfigureAwait(false),protocolVersion=2};
                else if(method=="get_operation")result=operations.Get(request.GetProperty("args").GetProperty("operationId").GetString()!);
                else if(method=="start_operation")
                {
                    var args=request.GetProperty("args");string action=args.GetProperty("operation").GetString()!;
                    if(action is not ("save_document" or "migrate_concept" or "check_motion"))throw new ArgumentException("operation must be save_document, migrate_concept or check_motion");
                    var command=JsonSerializer.SerializeToElement(new {method=action,args=args.GetProperty("arguments")});
                    result=operations.Start(args.GetProperty("requestId").GetString()!,command,()=>handle(command,stop.Token));
                }
                else result=await handle(request,stop.Token);
                response=new {ok=true,result};
            }
            catch(Exception error){response=new {ok=false,error=(error.InnerException??error).Message};}
            // A read deadline must never cancel a CAD mutation or its completion reply.
            await writer.WriteLineAsync(JsonSerializer.Serialize(response).AsMemory(),stop.Token);
        }
        catch(Exception error){System.Diagnostics.Trace.WriteLine(error);}
    }
    public void Dispose(){_ = stop.CancelAsync();try{File.Delete(file);}catch(IOException){}catch(UnauthorizedAccessException){} }
}

public partial class MainForm
{
    readonly CheckBox aiAccess=new(){Text="AI接続",AutoSize=true};
    AiEndpoint? aiEndpoint;
    public string OpenForAutomation(string expectedDocument)
    {
        if(!bridge.Connected)ConnectDocument();
        if(!string.Equals(Convert.ToString(bridge.Document.GetType().InvokeMember("FullName",System.Reflection.BindingFlags.GetProperty,null,bridge.Document,null)),expectedDocument,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("MechCue is connected to another document");
        aiAccess.Checked=true;Show();Activate();
        return JsonSerializer.Serialize(new {sessionId=aiEndpoint!.Session.Id,document=expectedDocument,connected=true});
    }
    void ConfigureAi()
    {
        aiAccess.CheckedChanged+=(_,_)=>{
            aiEndpoint?.Dispose();aiEndpoint=null;
            if(aiAccess.Checked)aiEndpoint=new AiEndpoint(Text,DispatchAi,()=>IsHandleCreated&&!IsDisposed,ProbeAiUi);
        };
        Disposed+=(_,_)=>{CancelVideoExport();aiEndpoint?.Dispose();aiEndpoint=null;};
        aiAccess.Checked = true;
    }
    bool aiExecuting;
    Exception? aiError;
    readonly SemaphoreSlim aiDispatchGate=new(1,1);
    internal async Task<bool> ProbeAiUi(CancellationToken cancellationToken)
    {
        if (IsDisposed || !IsHandleCreated) return false;
        var response = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            BeginInvoke(() => response.TrySetResult(!IsDisposed));
            return await response.Task.WaitAsync(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException) { return false; }
        catch (InvalidOperationException) { return false; }
    }
    async Task<object> DispatchAi(JsonElement request,CancellationToken cancellationToken)
    {
        await aiDispatchGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try{return await DispatchAiCore(request,cancellationToken).ConfigureAwait(false);}
        finally{aiDispatchGate.Release();}
    }
    Task<object> DispatchAiCore(JsonElement request,CancellationToken cancellationToken)
    {
        var completion=new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        if(IsDisposed || !IsHandleCreated)return Task.FromException<object>(new InvalidOperationException("MechCue window is closed"));
        try { BeginInvoke(()=>{
            try {
                cancellationToken.ThrowIfCancellationRequested();
                if(!aiAccess.Checked || IsDisposed)throw new InvalidOperationException("AI access is disabled");
                aiExecuting=true;aiError=null;
                var result=HandleAi(request);if(aiError!=null)throw aiError;
                completion.TrySetResult(result);
            }
            catch(Exception ex){PausePlayback(); live.Checked = autoApply.Checked = false; DiagnosticLog.Error("mcp", ex, DiagnosticState());completion.TrySetException(ex);}
            finally{aiExecuting=false;aiError=null;}
        }); }catch(Exception ex){DiagnosticLog.Error("mcp", ex, DiagnosticState());completion.TrySetException(ex);}
        return completion.Task;
    }
    internal object HandleAi(JsonElement request)
    {
        string method=request.GetProperty("method").GetString() ?? "";
        if (method != "get_state") DiagnosticLog.Write("mcp-command", new { method, state = DiagnosticState() });
        var args=request.TryGetProperty("args",out var a)?a:JsonSerializer.SerializeToElement(new{});
        double Number(string key)=>args.GetProperty(key).GetDouble();
        Track Target()
        {
            if(args.TryGetProperty("trackId",out var id) && !string.IsNullOrEmpty(id.GetString()))return tracks.Single(t=>t.Id==Guid.Parse(id.GetString()!));
            int index=args.GetProperty("trackNumber").GetInt32();if(index<1 || index>tracks.Count)throw new ArgumentOutOfRangeException("trackNumber");return tracks[index-1];
        }
        if(motionInspectionBusy)throw new InvalidOperationException("Path inspection is running. Wait or cancel in the inspection window before other commands.");
        if(method=="get_state")return AiState();
        if(method=="get_video_export")return VideoStatus(args,false);
        if(method=="cancel_video_export")return VideoStatus(args,true);
        if(videoExport?.Running==true)throw new InvalidOperationException("Video export is running. Wait for completion or cancel it before editing or playing.");
        if(method=="export_video")return StartVideoExport(args);
        if(method=="place_window")return PlaceWindow(args.GetProperty("position").GetInt32());
        if(method=="check_motion")
        {
            double start=Number("startTime"),end=Number("endTime"),step=Number("step");
            bool adaptive=!args.TryGetProperty("adaptive",out var adaptiveValue)||adaptiveValue.GetBoolean();
            double linear=args.TryGetProperty("maxLinearStepMm",out var linearValue)?linearValue.GetDouble():5;
            double angular=args.TryGetProperty("maxAngularStepDeg",out var angularValue)?angularValue.GetDouble():1;
            int limit=args.TryGetProperty("maxSamples",out var limitValue)?limitValue.GetInt32():5001;
            bool stopOnInterference=args.TryGetProperty("stopOnInterference",out var stopValue)&&stopValue.GetBoolean();
            bool surfaceBased=!args.TryGetProperty("surfaceBased",out var surfaceValue)||surfaceValue.GetBoolean();
            double surfaceStep=args.TryGetProperty("maxSurfaceStepMm",out var surfaceStepValue)?surfaceStepValue.GetDouble():5;
            double clearance=args.TryGetProperty("requiredClearanceMm",out var clearanceValue)?clearanceValue.GetDouble():0;
            if(!double.IsFinite(clearance)||clearance<0||clearance>10000)throw new ArgumentException("Clearance must be 0..10000 mm");
            if(!double.IsFinite(start)||!double.IsFinite(end)||!double.IsFinite(step)||start<0||end<=start||end>tracks.Max(t=>t.Points[^1].Time)||step<=0)throw new ArgumentException("Use a finite positive chart interval and step");
            using var reflection = PauseCadReflection(); PausePlayback();Commit();
            double[] samples;
            if(adaptive)samples=bridge.PlanMotionSamples(start,end,step,linear,angular,limit,surfaceBased,surfaceStep);
            else {if(Math.Ceiling((end-start)/step)>500)throw new ArgumentException("Fixed mode supports at most 501 samples");samples=Enumerable.Range(0,(int)Math.Ceiling((end-start)/step)).Select(i=>start+i*step).Append(end).ToArray();}
            var result=bridge.CheckMotionSamples(samples,stopOnInterference,adaptive,step,linear,angular,adaptive&&surfaceBased,surfaceStep,clearance);
            RecordMotionInspection(System.Text.Json.JsonSerializer.SerializeToElement(result));return result;
        }
        if(method=="list_patterns") { StoreActivePattern(); return new { activePatternId=activePattern, patterns=patterns.Select(p=>new { id=p.Id,name=p.Name,description=p.Description,duration=p.Points.Values.Max(ps=>ps[^1].Time),speed=p.Speed,loop=p.Loop,collision=p.Collision }) }; }
        if(method is "switch_pattern" or "create_pattern" or "rename_pattern" or "delete_pattern") {
            EnsurePatterns();
            Guid PatternId() => args.TryGetProperty("patternId",out var pid) && !string.IsNullOrEmpty(pid.GetString()) ? Guid.Parse(pid.GetString()!) : args.TryGetProperty("patternName",out var pn) && !string.IsNullOrEmpty(pn.GetString()) ? patterns.Single(p=>string.Equals(p.Name,pn.GetString(),StringComparison.OrdinalIgnoreCase)).Id : activePattern;
            switch(method) {
                case "switch_pattern": SwitchPattern(PatternId()); break;
                case "create_pattern": CreatePattern(args.GetProperty("name").GetString()!,args.TryGetProperty("duplicate",out var dup) && dup.GetBoolean()); break;
                case "rename_pattern": RenamePattern(PatternId(),args.GetProperty("name").GetString()!,args.TryGetProperty("description",out var desc)?desc.GetString():null); break;
                case "delete_pattern": DeletePattern(PatternId()); break;
            }
            return AiState();
        }
        if(method=="import_concept"){if(!bridge.Connected)throw new InvalidOperationException("Connect MechCue to the concept assembly before importing axes.");ImportConceptAxes(args.GetProperty("manifestPath").GetString()!,args.TryGetProperty("unboundTracks",out var unbound)?unbound.GetString()!:"preserve");return AiState();}
        if(method=="save_document"){
            var expected=args.GetProperty("expectedDocument").GetString();
            if(!bridge.Connected || !string.Equals(Convert.ToString(bridge.Document.GetType().InvokeMember("FullName",System.Reflection.BindingFlags.GetProperty,null,bridge.Document,null)),expected,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Connected document differs from expectedDocument.");
            SaveToDocument();return AiState();
        }
        if(method=="migrate_concept"){MigrateConceptSettings(args.GetProperty("expectedDocument").GetString()!,args.GetProperty("manifestPath").GetString()!,args.GetProperty("settingsJson").GetString()!,args.TryGetProperty("hideUnboundTracks",out var hide) && hide.GetBoolean());return AiState();}
        if(method=="bind_nested")
        {
            using var reflection = PauseCadReflection(); PausePlayback();Commit();var track=Target();
            var expected=args.GetProperty("expectedDocument").GetString();
            if(!bridge.Connected||!string.Equals(Convert.ToString(bridge.Document.GetType().InvokeMember("FullName",System.Reflection.BindingFlags.GetProperty,null,bridge.Document,null)),expected,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Connected document differs from expectedDocument");
            string kind=args.GetProperty("kind").GetString()!,direction=args.GetProperty("axis").GetString()!;
            if(kind is not ("部品移動" or "部品回転" or "部品座標")||direction is not ("X" or "Y" or "Z"))throw new ArgumentException("Invalid nested drive kind/axis");
            if(bridge.BoundLabel(track)!=null||bridge.PendingLabel(track)!=null)throw new InvalidOperationException("Choose an unassigned track");
            string oldKind=track.Kind,oldAxis=track.Axis;track.Kind=kind;track.Axis=direction;
            try{bridge.BindNested(track,args.GetProperty("keyPath").GetString()!,args.GetProperty("modifySharedSubassembly").GetBoolean());}
            catch{track.Kind=oldKind;track.Axis=oldAxis;throw;}
            plot.Hidden.Remove(track);ClearEditHistory();RefreshTracks(tracks.IndexOf(track));MarkDocumentSettingsChanged();return AiState();
        }
        if(method=="stop"){PausePlayback();return AiState();}
        if(method=="play"){StartPlayback();return AiState();}
        if(method=="seek"){
            double position=Number("time");if(!double.IsFinite(position)||position<0||position>tracks.Max(t=>t.Points[^1].Time))throw new ArgumentOutOfRangeException("time");
            PausePlayback();time.Value=(decimal)position;return AiState();
        }
        if(method=="undo"){using var reflection = PauseCadReflection(); PausePlayback();Undo();return AiState();}
        if(method is not ("set_keyframe" or "reset_values" or "resample" or "set_keyframes" or "delete_keyframe"))throw new InvalidOperationException("Unknown MechCue command: "+method);
        using var editReflection = PauseCadReflection(); PausePlayback();Commit();
        if(bridge.Connected)_=bridge.Document;
        var changes=new Dictionary<Track,List<KeyPoint>>();
        switch(method)
        {
            case "set_keyframes":
                var edits=args.GetProperty("tracks").EnumerateArray().ToArray();
                if(edits.Length is <1 or >128)throw new ArgumentException("Supply 1–128 distinct tracks");
                foreach(var edit in edits)
                {
                    Track edited=edit.TryGetProperty("trackId",out var tid)?tracks.Single(t=>t.Id==Guid.Parse(tid.GetString()!)):tracks[edit.GetProperty("trackNumber").GetInt32()-1];
                    if(changes.ContainsKey(edited))throw new ArgumentException("Duplicate track in batch");
                    changes[edited]=edit.GetProperty("points").EnumerateArray().Select(p=>new KeyPoint(p.GetProperty("time").GetDouble(),p.GetProperty("value").GetDouble())).ToList();
                }
                if(changes.Values.Sum(ps=>ps.Count)>100000)throw new ArgumentException("At most 100000 points per batch");
                break;
            case "delete_keyframe":
                var deleted=Target();double deletedTime=Number("time");
                if(!deleted.Points.Any(p=>p.Time==deletedTime))throw new ArgumentException("Keyframe time not found");
                changes[deleted]=deleted.Points.Where(p=>p.Time!=deletedTime).ToList();break;
            case "set_keyframe":
                var track=Target();double moment=Number("time"),value=Number("value");
                var points=track.Points.Where(p=>p.Time!=moment).Append(new KeyPoint(moment,value)).OrderBy(p=>p.Time).ToList();changes[track]=points;break;
            case "reset_values":
                track=Target();value=Number("value");changes[track]=track.Points.Select(p=>p with {Value=value}).ToList();break;
            case "resample":
                double end=Number("endTime"),step=Number("step");
                if(!double.IsFinite(end)||!double.IsFinite(step)||end<=0||end>100000||step<=0||step>end||Math.Ceiling(end/step)>10000 || (Math.Ceiling(end/step)+1)*tracks.Count>100000)throw new ArgumentException("Invalid range; at most 10001 time points per track and 100000 total points");
                foreach(var t in tracks)
                {
                    t.Validate();int segment=0;
                    double At(double position)
                    {
                        if(position<=t.Points[0].Time)return t.Points[0].Value;
                        while(segment<t.Points.Count-2 && t.Points[segment+1].Time<position)segment++;
                        var first=t.Points[segment];var last=t.Points[segment+1];
                        if(position>=last.Time)return last.Value;
                        double ratio=(position-first.Time)/(last.Time-first.Time);
                        return first.Value*(1-ratio)+last.Value*ratio;
                    }
                    var sampled=new List<KeyPoint>();for(int i=0;i*step<end;i++)sampled.Add(new(i*step,At(i*step)));sampled.Add(new(end,At(end)));changes[t]=sampled;
                }break;
            default:throw new InvalidOperationException("Unknown MechCue command: "+method);
        }
        foreach(var points in changes.Values){new Track{Points=points}.Validate();if(points[^1].Time>100000)throw new ArgumentOutOfRangeException("time");}
        foreach(var change in changes)bridge.ValidateConceptPoints(change.Key,change.Value);
        RecordHistory(changes.Keys.Select(t=>(t,t.Points.ToList())).ToList());
        foreach(var change in changes)change.Key.Points=change.Value;
        RefreshTracks(trackList.SelectedIndex);MarkDocumentSettingsChanged();status.Text="AIからグラフを編集しました。";
        return AiState();
    }
    object AiState()=>new {
        activePatternId=activePattern,patternName=patterns.FirstOrDefault(p=>p.Id==activePattern)?.Name,sessionId=aiEndpoint?.Session.Id,connected=bridge.Connected,playing=timer.Enabled,applyToCad=live.Checked,autoApplyOnPlay=autoApply.Checked,time=(double)time.Value,status=status.Text,
        tracks=tracks.Select((t,i)=>new {number=i+1,id=t.Id,name=t.Name,kind=t.Kind,axis=t.Axis,hidden=plot.Hidden.Contains(t),unit=Plot.IsAngle(t)?"deg":"mm",target=bridge.BoundLabel(t) ?? bridge.PendingLabel(t),points=t.Points.Select(p=>new{time=p.Time,value=p.Value}).ToArray()}).ToArray()
    };
}
