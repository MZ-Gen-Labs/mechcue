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
    public AiSession Session { get; }
    public AiEndpoint(string title,Func<JsonElement,CancellationToken,Task<object>> handle)
    {
        this.handle=handle;string id=Guid.NewGuid().ToString("N");
        Session=new(id,"MechCue-"+id,title,Environment.ProcessId);
        Directory.CreateDirectory(SessionDirectory);file=Path.Combine(SessionDirectory,id+".json");
        File.WriteAllText(file,JsonSerializer.Serialize(Session));_ = Serve();
    }
    async Task Serve()
    {
        while(!stop.IsCancellationRequested)
        {
            try
            {
                using var pipe=new NamedPipeServerStream(Session.Pipe,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(stop.Token);
                using var reader=new StreamReader(pipe,new UTF8Encoding(false),false,4096,true);
                using var writer=new StreamWriter(pipe,new UTF8Encoding(false),4096,true){AutoFlush=true};
                using var timeout=CancellationTokenSource.CreateLinkedTokenSource(stop.Token);timeout.CancelAfter(TimeSpan.FromSeconds(10));
                object result;
                try {
                    string line=await reader.ReadLineAsync(timeout.Token) ?? throw new InvalidDataException("Empty request");
                    if(line.Length>65536)throw new InvalidDataException("Request too large");
                    using var request=JsonDocument.Parse(line);result=new {ok=true,result=await handle(request.RootElement.Clone(),timeout.Token)};
                } catch(Exception ex) {result=new {ok=false,error=(ex.InnerException ?? ex).Message};}
                await writer.WriteLineAsync(JsonSerializer.Serialize(result).AsMemory(),timeout.Token);
            }
            catch(OperationCanceledException) when(stop.IsCancellationRequested){break;}
            catch(Exception ex){System.Diagnostics.Trace.WriteLine(ex);if(!stop.IsCancellationRequested)await Task.Delay(100,stop.Token).ConfigureAwait(false);}
        }
    }
    public void Dispose(){stop.Cancel();try{File.Delete(file);}catch(IOException){}catch(UnauthorizedAccessException){} }
}

public partial class MainForm
{
    readonly CheckBox aiAccess=new(){Text="AI接続",AutoSize=true};
    AiEndpoint? aiEndpoint;
    void ConfigureAi()
    {
        top.Controls.Add(aiAccess);aiAccess.CheckedChanged+=(_,_)=>{
            aiEndpoint?.Dispose();aiEndpoint=null;
            if(aiAccess.Checked)aiEndpoint=new AiEndpoint(Text,DispatchAi);
        };
        Disposed+=(_,_)=>{aiEndpoint?.Dispose();aiEndpoint=null;};
        aiAccess.Checked = true;
    }
    bool aiExecuting;
    Exception? aiError;
    Task<object> DispatchAi(JsonElement request,CancellationToken cancellationToken)
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
            catch(Exception ex){DiagnosticLog.Error("mcp", ex, DiagnosticState());completion.TrySetException(ex);}
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
        if(method=="get_state")return AiState();
        if(method=="stop"){PausePlayback();return AiState();}
        if(method=="play"){StartPlayback();return AiState();}
        if(method=="seek"){
            double position=Number("time");if(!double.IsFinite(position)||position<0||position>tracks.Max(t=>t.Points[^1].Time))throw new ArgumentOutOfRangeException("time");
            PausePlayback();time.Value=(decimal)position;return AiState();
        }
        if(method=="undo"){PausePlayback();live.Checked=false;Undo();return AiState();}
        if(method is not ("set_keyframe" or "reset_values" or "resample"))throw new InvalidOperationException("Unknown MechCue command: "+method);
        PausePlayback();live.Checked=false;Commit();
        if(bridge.Connected)_=bridge.Document;
        var changes=new Dictionary<Track,List<KeyPoint>>();
        switch(method)
        {
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
        history.Push(changes.Keys.Select(t=>(t,t.Points.ToList())).ToList());
        foreach(var change in changes)change.Key.Points=change.Value;
        RefreshTracks(trackList.SelectedIndex);MarkDocumentSettingsChanged();status.Text="AIからグラフを編集しました。";
        return AiState();
    }
    object AiState()=>new {
        sessionId=aiEndpoint?.Session.Id,connected=bridge.Connected,playing=timer.Enabled,applyToCad=live.Checked,time=(double)time.Value,status=status.Text,
        tracks=tracks.Select((t,i)=>new {number=i+1,id=t.Id,name=t.Name,kind=t.Kind,axis=t.Axis,unit=Plot.IsAngle(t)?"deg":"mm",target=bridge.BoundLabel(t) ?? bridge.PendingLabel(t),points=t.Points.Select(p=>new{time=p.Time,value=p.Value}).ToArray()}).ToArray()
    };
}