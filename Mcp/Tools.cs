using System.ComponentModel;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;
using ModelContextProtocol.Server;
using MechCue;

[McpServerToolType]
public static class MechCueTools
{
    static string Json(object value)=>JsonSerializer.Serialize(value);
    [McpServerTool(ReadOnly=true),Description("List running MechCue windows that have AI access enabled. If several sessions exist, select an explicit sessionId before editing.")]
    public static object mechcue_list_sessions()
    {
        if(!Directory.Exists(AiEndpoint.SessionDirectory))return Array.Empty<AiSession>();
        var sessions=new List<AiSession>();
        foreach(string file in Directory.GetFiles(AiEndpoint.SessionDirectory,"*.json"))
            try{var session=JsonSerializer.Deserialize<AiSession>(File.ReadAllText(file));if(session!=null && session.Pipe=="MechCue-"+session.Id && Guid.TryParseExact(session.Id,"N",out _) && !System.Diagnostics.Process.GetProcessById(session.ProcessId).HasExited)sessions.Add(session);}catch(IOException){}catch(JsonException){}catch(ArgumentException){}catch(System.ComponentModel.Win32Exception){}
        return sessions;
    }
    static async Task<string> Send(string method,object args,string sessionId,CancellationToken cancellationToken)
    {
        var sessions=mechcue_list_sessions() is List<AiSession> found ? found : new List<AiSession>();
        var session=string.IsNullOrEmpty(sessionId)?sessions.Count==1?sessions[0]:throw new InvalidOperationException("Enable AI access in MechCue. If multiple windows are enabled, supply sessionId."):sessions.Single(s=>s.Id==sessionId);
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);deadline.CancelAfter(TimeSpan.FromSeconds(20));
        using var pipe=new NamedPipeClientStream(".",session.Pipe,PipeDirection.InOut,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(3000,deadline.Token);
        using var writer=new StreamWriter(pipe,new UTF8Encoding(false),4096,true){AutoFlush=true};using var reader=new StreamReader(pipe,new UTF8Encoding(false),false,4096,true);
        await writer.WriteLineAsync(Json(new{method,args}).AsMemory(),deadline.Token);
        string result=await reader.ReadLineAsync(deadline.Token) ?? throw new IOException("MechCue disconnected");
        using var parsed=JsonDocument.Parse(result);
        if(!parsed.RootElement.GetProperty("ok").GetBoolean())throw new InvalidOperationException(parsed.RootElement.GetProperty("error").GetString());
        return parsed.RootElement.GetProperty("result").GetRawText();
    }
    [McpServerTool(ReadOnly=true),Description("Read tracks, stable IDs, 1-based track numbers, units, keyframes, target assignments and playback state.")]
    public static Task<string> mechcue_get_state(string sessionId="",CancellationToken cancellationToken=default)=>Send("get_state",new{},sessionId,cancellationToken);
    [McpServerTool,Description("Set or insert one keyframe. Time is seconds; value uses the track's mm/degree unit. Prefer stable trackId; otherwise use a 1-based trackNumber. Stops playback and turns off CAD reflection. Undo is supported.")]
    public static Task<string> mechcue_set_keyframe(double time,double value,int trackNumber=1,string trackId="",string sessionId="",CancellationToken cancellationToken=default)=>Send("set_keyframe",new{time,value,trackNumber,trackId},sessionId,cancellationToken);
    [McpServerTool,Description("Set all values of one track to a constant; preserve its keyframe times and CAD target. Stops playback and disables CAD reflection.")]
    public static Task<string> mechcue_reset_values(double value,int trackNumber=1,string trackId="",string sessionId="",CancellationToken cancellationToken=default)=>Send("reset_values",new{value,trackNumber,trackId},sessionId,cancellationToken);
    [McpServerTool,Description("Resample ALL tracks from zero to endTime seconds at a step interval, including the endpoint. Preserve their shapes by linear interpolation; beyond the original end hold the last value. This does not stretch time. Maximum 10001 points per track and 100000 total points. A single undo restores the whole change.")]
    public static Task<string> mechcue_resample(double endTime,double step,string sessionId="",CancellationToken cancellationToken=default)=>Send("resample",new{endTime,step},sessionId,cancellationToken);
    [McpServerTool,Description("Undo the latest graph edit, including a batch AI edit. Stops playback and disables CAD reflection.")]
    public static Task<string> mechcue_undo(string sessionId="",CancellationToken cancellationToken=default)=>Send("undo",new{},sessionId,cancellationToken);
    [McpServerTool,Description("Set the time cursor within the chart range. Uses the existing Apply to Solid Edge setting: if enabled, this moves the CAD assembly.")]
    public static Task<string> mechcue_seek(double time,string sessionId="",CancellationToken cancellationToken=default)=>Send("seek",new{time},sessionId,cancellationToken);
    [McpServerTool,Description("Start playback using the existing CAD reflection setting. Restart from zero if already at the end. Does not enable CAD reflection automatically.")]
    public static Task<string> mechcue_play(string sessionId="",CancellationToken cancellationToken=default)=>Send("play",new{},sessionId,cancellationToken);
    [McpServerTool,Description("Stop playback at the current time.")]
    public static Task<string> mechcue_stop(string sessionId="",CancellationToken cancellationToken=default)=>Send("stop",new{},sessionId,cancellationToken);
}

[McpServerToolType]
public static class SolidEdgeTools
{
    static readonly Lazy<StaWorker> worker=new(()=>new());
    static Task<string> Read(string method,int part=0,string expected="")
    {
        if(!Environment.GetCommandLineArgs().Contains("--allow-solidedge"))throw new InvalidOperationException("Direct Solid Edge access is disabled. Start MCP with --allow-solidedge to enable read and selection tools.");
        return worker.Value.Run(()=>JsonSerializer.Serialize(Bridge.ReadSolidEdge(method,part,expected)));
    }
    [McpServerTool(ReadOnly=true),Description("Read the active Solid Edge document's name, fullName, read-only and dirty status. Requires server option --allow-solidedge; does not open or save files.")]
    public static Task<string> solidedge_get_document()=>Read("document");
    [McpServerTool(ReadOnly=true),Description("List top-level assembly parts and absolute XYZ position in millimetres. Requires --allow-solidedge. Set expectedDocument to fullName from solidedge_get_document to detect active-document changes.")]
    public static Task<string> solidedge_list_parts(string expectedDocument="")=>Read("parts",expected:expectedDocument);
    [McpServerTool(ReadOnly=true),Description("Read the active document's variable table. Values use Solid Edge native internal units (metres/radians), not MechCue display units; includes native unitsType. Requires --allow-solidedge.")]
    public static Task<string> solidedge_list_variables(string expectedDocument="")=>Read("variables",expected:expectedDocument);
    [McpServerTool,Description("Select and highlight one top-level assembly part by 1-based partNumber. Replaces CAD selection; does not move geometry. Required expectedDocument must equal the current document fullName. Requires --allow-solidedge.")]
    public static Task<string> solidedge_select_part(int partNumber,string expectedDocument)=>Read("select_part",partNumber,expectedDocument);
}
sealed class StaWorker
{
    readonly BlockingCollection<Action> queue=new();
    public StaWorker(){var thread=new Thread(()=>{foreach(var action in queue.GetConsumingEnumerable())action();}){IsBackground=true,Name="Solid Edge MCP STA"};thread.SetApartmentState(ApartmentState.STA);thread.Start();}
    public Task<string> Run(Func<string> action){var completion=new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);queue.Add(()=>{try{completion.SetResult(action());}catch(Exception ex){completion.SetException(ex.InnerException ?? ex);}});return completion.Task;}
}