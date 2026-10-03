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
    [McpServerTool(ReadOnly=true),Description("List exact assembly instance leaf key paths, names, native bounds and current inspection policy. Repeated subassembly instances are distinct. Use these keys for explicit allowed-contact pairs. No CAD movement.")]
    public static Task<string> mechcue_list_inspection_parts(string sessionId="",CancellationToken cancellationToken=default)=>Send("list_inspection_parts",new{},sessionId,cancellationToken);
    [McpServerTool(ReadOnly=true),Description("Read bounded inspection overview pages: offset>=0, limit=1..20. Detailed sample/interval pages require reportId, includeSamples=true, sampleOffset>=0, sampleLimit=1..100. Follow nextSampleOffset and continuousVerification.nextSegmentOffset separately (both use sampleOffset). Total flags refer to the full recorded run, not the page. Responses capped at 4 MiB; actual page size may shrink. Failures-only pairs reference partTable IDs; normal full pair distances are working data, not persisted. History retains <=500 records/32 MiB of summaries and previews. Archived details are paged transparently; JSON export includes all details. Records are snapshots; repeat after CAD/chart changes.")]
    public static Task<string> mechcue_get_inspection_reports(string sessionId="",int offset=0,int limit=20,string reportId="",bool includeSamples=false,int sampleOffset=0,int sampleLimit=100,CancellationToken cancellationToken=default)=>Send("get_inspection_reports",new{offset,limit,reportId,includeSamples,sampleOffset,sampleLimit},sessionId,cancellationToken);
    [McpServerTool,Description("Set document inspection policy. policyJson: IncludeNested=true, VerifyContinuous=true, NumericalMarginMm=0.01 (0.001..10), MaxRefinementDepth=20 (0..30), AutoSplit=true, MaxTotalSamples=100001 (2..100001), AllowedContacts=[{FirstKeyPath,SecondKeyPath,Reason}]. Exact distinct leaf keys from list_inspection_parts and nonempty reasons required; stale keys/duplicates refused. Named pairs alone are excluded. Pauses playback; persisted on CAD save. Requires Creation mode; no automatic CAD save.")]
    public static Task<string> mechcue_set_inspection_policy(string policyJson,string sessionId="",CancellationToken cancellationToken=default){EnsureWrite();return Send("set_inspection_policy",new{policy=JsonSerializer.Deserialize<JsonElement>(policyJson)},sessionId,cancellationToken);}
    static void EnsureWrite(){try{McpAccessSettings.EnsureAllowed(true);}catch(InvalidOperationException error){throw new ModelContextProtocol.McpException(error.Message);}}
    [McpServerTool,Description("Maximize connected Solid Edge on its current monitor and move the chart at numeric-keypad position 1..9: 1 bottom-left, 2 bottom-center, 3 bottom-right, 4 middle-left, 5 center, 6 middle-right, 7 top-left, 8 top-center, 9 top-right. Preserves full/compact mode and size (clamps to monitor work area if necessary). Repeated MCP calls move only; no display-mode toggle. Saves local position/size, keeps taskbar visible. Requires connected session and edit permission; no CAD pose or document save.")]
    public static Task<string> mechcue_place_window(int position=3,string sessionId="",CancellationToken cancellationToken=default)
    {EnsureWrite();return Send("place_window",new{position},sessionId,cancellationToken);}
    [McpServerTool(ReadOnly=true),Description("List running MechCue windows that have AI access enabled. If several sessions exist, select an explicit sessionId before editing.")]
    public static object mechcue_list_sessions()
    {
        if(!Directory.Exists(AiEndpoint.SessionDirectory))return Array.Empty<AiSession>();
        var sessions=new List<AiSession>();
        foreach(string file in Directory.GetFiles(AiEndpoint.SessionDirectory,"*.json"))
            try{var session=JsonSerializer.Deserialize<AiSession>(File.ReadAllText(file));if(session!=null && session.Pipe=="MechCue-"+session.Id && Guid.TryParseExact(session.Id,"N",out _) && !System.Diagnostics.Process.GetProcessById(session.ProcessId).HasExited)sessions.Add(session);}catch(IOException){}catch(JsonException){}catch(ArgumentException){}catch(System.ComponentModel.Win32Exception){}
        return sessions;
    }
    [McpServerTool,Description("Import concept-machine axes into an enabled MechCue window connected to that assembly. Creates constant tracks from measured current pose without moving CAD; repeated import preserves existing tracks. unboundTracks=preserve (default) or hide hides unassigned tracks without deleting them; assigned and unresolved tracks are retained. Stops playback and disables CAD reflection. manifestPath absolute; optional sessionId. Save to CAD persists mappings.")]
    public static Task<string> mechcue_import_concept_axes(string manifestPath,string sessionId="",string unboundTracks="preserve",CancellationToken cancellationToken=default)=>Send("import_concept",new{manifestPath,unboundTracks},sessionId,cancellationToken);
    [McpServerTool,Description("Save the connected MechCue session's current charts, patterns and bindings into its assembly and save CAD. expectedDocument must match the connected active assembly. Stops playback and temporarily suspends reflection while restoring reference poses; preserves the Apply selection. Requires Creation/edit mode.")]
    public static Task<string> mechcue_save_document(string expectedDocument,string sessionId="",CancellationToken cancellationToken=default)
    {
        McpAccessSettings.EnsureAllowed(true);
        return Send("save_document",new{expectedDocument},sessionId,cancellationToken);
    }
    [McpServerTool,Description("Migrate persisted concept chart settings into a connected new detail assembly using its manifest. Supply settingsJson from solidedge_get_mechcue_settings on the saved source. Preserves all track/pattern IDs and keyframes; requires identical axes and source baseline pose in destination. Rejects destinations with existing embedded settings/bindings and sources with non-concept bindings. Stops playback/reflection; no CAD movement or automatic save. hideUnboundTracks hides unassigned source tracks without deleting them. Requires Creation/edit mode. Save using mechcue_save_document afterward.")]
    public static Task<string> mechcue_migrate_concept_settings(string expectedDocument,string manifestPath,string settingsJson,bool hideUnboundTracks=true,string sessionId="",CancellationToken cancellationToken=default)
    {
        McpAccessSettings.EnsureAllowed(true);
        return Send("migrate_concept",new{expectedDocument,manifestPath,settingsJson,hideUnboundTracks},sessionId,cancellationToken);
    }
    [McpServerTool(ReadOnly=true),Description("List named motion patterns, their IDs, descriptions, durations and playback options; target bindings are shared across patterns.")]
    public static Task<string> mechcue_list_patterns(string sessionId="",CancellationToken cancellationToken=default)=>Send("list_patterns",new{},sessionId,cancellationToken);
    [McpServerTool,Description("Switch named motion pattern by stable patternId or exact patternName. Retains current edits, CAD bindings and current Apply/Collision/Loop selections, stops playback and rewinds. Does not move CAD. Subsequent play/seek uses the retained Apply selection.")]
    public static Task<string> mechcue_switch_pattern(string patternId="",string patternName="",string sessionId="",CancellationToken cancellationToken=default)=>Send("switch_pattern",new{patternId,patternName},sessionId,cancellationToken);
    [McpServerTool,Description("Create and select a uniquely named motion pattern. duplicate=true copies current keyframes; false creates constant graphs at the current chart values. Shares CAD bindings; stops playback while preserving Apply/Collision/Loop selections without moving CAD. Save to CAD persists all patterns.")]
    public static Task<string> mechcue_create_pattern(string name,bool duplicate=true,string sessionId="",CancellationToken cancellationToken=default)=>Send("create_pattern",new{name,duplicate},sessionId,cancellationToken);
    [McpServerTool,Description("Rename a motion pattern and optionally edit its description. Select by ID or exact current name; defaults to active pattern. Names must be unique, 1–100 characters.")]
    public static Task<string> mechcue_rename_pattern(string name,string patternId="",string patternName="",string? description=null,string sessionId="",CancellationToken cancellationToken=default)=>Send("rename_pattern",new{name,patternId,patternName,description},sessionId,cancellationToken);
    [McpServerTool(Destructive=true),Description("Delete a named motion pattern by ID or exact name; defaults to active pattern. Cannot delete the last pattern. If deleting the active pattern, stops playback and selects another pattern, preserving Apply/Collision/Loop selections without moving CAD. Shared CAD bindings remain.")]
    public static Task<string> mechcue_delete_pattern(string patternId="",string patternName="",string sessionId="",CancellationToken cancellationToken=default)=>Send("delete_pattern",new{patternId,patternName},sessionId,cancellationToken);
    [McpServerTool,Description("Start an asynchronous video export of the active motion pattern through the connected Solid Edge view (MJPEG AVI, no audio). Requires Creation/edit mode, writable active assembly and assigned chart targets. Explicitly moves CAD even if reflection is off; pauses playback and restores original CAD pose/cursor/reflection on finish, failure or cancel. Absolute new .avi path in an existing folder. endTime=-1 uses chart end. 1–30 fps; at most 300 seconds / 1 GiB; 320–1920 by 240–1080 pixels. checkInterference stops on collision/unknown check. Returns jobId immediately; poll mechcue_get_video_export. Does not save CAD. MechCue editing is locked until finished. Other Solid Edge/MCP editing must wait.")]
    public static Task<string> mechcue_export_video(string outputPath,double startTime=0,double endTime=-1,int fps=15,int width=1280,int height=720,bool checkInterference=false,string sessionId="",CancellationToken cancellationToken=default)
    {
        McpAccessSettings.EnsureAllowed(true);
        return Send("export_video",new{outputPath,startTime,endTime,fps,width,height,checkInterference},sessionId,cancellationToken);
    }
    [McpServerTool(ReadOnly=true),Description("Read an asynchronous video export's progress, state (running/completed/failed/cancelled), frame counts, output path and errors. Supply jobId and the same MechCue sessionId. Only the latest job is retained; completed means final AVI and pose restoration succeeded.")]
    public static Task<string> mechcue_get_video_export(string jobId,string sessionId="",CancellationToken cancellationToken=default)=>Send("get_video_export",new{jobId},sessionId,cancellationToken);
    [McpServerTool,Description("Cancel a video export and restore original CAD pose/cursor/reflection. Deletes incomplete video; existing files are never overwritten. Supply jobId and the same sessionId.")]
    public static Task<string> mechcue_cancel_video_export(string jobId,string sessionId="",CancellationToken cancellationToken=default)=>Send("cancel_video_export",new{jobId},sessionId,cancellationToken);
    static async Task<string> Send(string method,object args,string sessionId,CancellationToken cancellationToken)
    {
        try {
        var sessions=mechcue_list_sessions() is List<AiSession> found ? found : new List<AiSession>();
        var session=string.IsNullOrEmpty(sessionId)?sessions.Count==1?sessions[0]:throw new InvalidOperationException("Enable AI access in MechCue. If multiple windows are enabled, supply sessionId."):sessions.Single(s=>s.Id==sessionId);
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);deadline.CancelAfter(TimeSpan.FromSeconds(method=="save_document"?120:20));
        using var pipe=new NamedPipeClientStream(".",session.Pipe,PipeDirection.InOut,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(3000,deadline.Token);
        using var writer=new StreamWriter(pipe,new UTF8Encoding(false),4096,true){AutoFlush=true};using var reader=new StreamReader(pipe,new UTF8Encoding(false),false,4096,true);
        await writer.WriteLineAsync(Json(new{method,args}).AsMemory(),deadline.Token);
        string result=await reader.ReadLineAsync(deadline.Token) ?? throw new IOException("MechCue disconnected");
        using var parsed=JsonDocument.Parse(result);
        if(!parsed.RootElement.GetProperty("ok").GetBoolean())throw new InvalidOperationException(parsed.RootElement.GetProperty("error").GetString());
        return parsed.RootElement.GetProperty("result").GetRawText();
        } catch(Exception error) when(error is not ModelContextProtocol.McpException) {
            throw new ModelContextProtocol.McpException(error is OperationCanceledException?"MechCue request timed out or was cancelled. CAD may still be completing; use start_operation/get_operation for reliable save completion.":(error.InnerException??error).Message);
        }
    }
    [McpServerTool(ReadOnly=true),Description("Read tracks, stable IDs, 1-based track numbers, units, keyframes, target assignments and playback state.")]
    public static Task<string> mechcue_get_state(string sessionId="",CancellationToken cancellationToken=default)=>Send("get_state",new{},sessionId,cancellationToken);
    [McpServerTool,Description("Set or insert one keyframe. Time is seconds; value uses the track's mm/degree unit. Prefer stable trackId; otherwise use a 1-based trackNumber. Stops playback, preserves Apply selection and does not move CAD during editing. Undo is supported.")]
    public static Task<string> mechcue_set_keyframe(double time,double value,int trackNumber=1,string trackId="",string sessionId="",CancellationToken cancellationToken=default)=>Send("set_keyframe",new{time,value,trackNumber,trackId},sessionId,cancellationToken);
    [McpServerTool,Description("Set all values of one track to a constant; preserve its keyframe times and CAD target. Stops playback, preserves Apply selection and does not move CAD during editing.")]
    public static Task<string> mechcue_reset_values(double value,int trackNumber=1,string trackId="",string sessionId="",CancellationToken cancellationToken=default)=>Send("reset_values",new{value,trackNumber,trackId},sessionId,cancellationToken);
    [McpServerTool,Description("Resample ALL tracks from zero to endTime seconds at a step interval, including the endpoint. Preserve their shapes by linear interpolation; beyond the original end hold the last value. This does not stretch time. Maximum 10001 points per track and 100000 total points. A single undo restores the whole change.")]
    public static Task<string> mechcue_resample(double endTime,double step,string sessionId="",CancellationToken cancellationToken=default)=>Send("resample",new{endTime,step},sessionId,cancellationToken);
    [McpServerTool,Description("Undo the latest graph edit, including a batch AI edit. Stops playback, preserves Apply selection and does not move CAD during editing.")]
    public static Task<string> mechcue_undo(string sessionId="",CancellationToken cancellationToken=default)=>Send("undo",new{},sessionId,cancellationToken);
    [McpServerTool,Description("Set the time cursor within the chart range. Uses the existing Apply to Solid Edge setting: if enabled, this moves the CAD assembly.")]
    public static Task<string> mechcue_seek(double time,string sessionId="",CancellationToken cancellationToken=default)=>Send("seek",new{time},sessionId,cancellationToken);
    [McpServerTool,Description("Start playback using the current Apply selection, or enable Apply when the user has checked Auto in the chart. Restart from zero if already at the end. A failed initial CAD pose aborts playback and disables Apply and Auto.")]
    public static Task<string> mechcue_play(string sessionId="",CancellationToken cancellationToken=default)=>Send("play",new{},sessionId,cancellationToken);
    [McpServerTool,Description("Stop playback at the current time.")]
    public static Task<string> mechcue_stop(string sessionId="",CancellationToken cancellationToken=default)=>Send("stop",new{},sessionId,cancellationToken);
    [McpServerTool(ReadOnly=true),Description("Probe the selected session's live pipe and UI message processing independently of CAD commands. uiReady means a window handle exists; uiResponsive is a one-second UI probe (false may indicate a busy or stranded UI, null means an older endpoint without a probe). Explicit sessionId recommended.")]
    public static Task<string> mechcue_get_session_health(string sessionId="",CancellationToken cancellationToken=default)=>Send("ping",new{},sessionId,cancellationToken);
    [McpServerTool,Description("Start save_document, migrate_concept or check_motion; returns operationId immediately. argumentsJson excludes sessionId. check_motion requires startTime,endTime,step seconds. Default adaptive=false starts with coarse steps plus reversals/stops; original full keys remain in conservative travel bounds, not automatic CAD evaluations. adaptive=true opts into dense extent sampling (maxLinearStepMm=5,maxAngularStepDeg=1,surfaceBased=true,maxSurfaceStepMm=5). maxSamples=5001 per working batch (2..10001),stopOnInterference=false,requiredClearanceMm=0. Optional inspectionPolicy defaults to document policy: nested leaves and continuous verification enabled. Continuous checks bisect unresolved intervals using rigid relative-travel bounds; unsupported motion, cancellation and budgets never certify. Independent pair verification continues despite other collisions. AutoSplit=true recycles bounded working batches (1,000,000 pair values) and archives detail beyond 16 MiB; MaxTotalSamples bounds the whole run. Fixed/common-motion measurements are reused only within this run, guarded against saved/unsaved component geometry changes. Automatic batches retain complete coverage; exhausted total/disk/depth budgets remain unverified. Restores pose and records compact failures-only result in UI. Completion result is an OVERVIEW with reportId; fetch detailed pages using get_inspection_reports. Named contacts alone are excluded. requestId UUID: reuse SAME ID/arguments and poll get_operation; 128 idempotent jobs. Disconnect does not cancel mutation. Requires Creation/edit mode; avoid simultaneous CAD editing.")]
    public static Task<string> mechcue_start_operation(string operation,string argumentsJson,string requestId,string sessionId="",CancellationToken cancellationToken=default)
    {
        EnsureWrite();using var json=JsonDocument.Parse(argumentsJson);
        return Send("start_operation",new{operation,arguments=json.RootElement.Clone(),requestId},sessionId,cancellationToken);
    }
    [McpServerTool(ReadOnly=true),Description("Read save/migration job state and actual completion result or actionable failure, independently of a busy CAD UI thread. Requires original sessionId and operationId.")]
    public static Task<string> mechcue_get_operation(string operationId,string sessionId="",CancellationToken cancellationToken=default)=>Send("get_operation",new{operationId},sessionId,cancellationToken);
    [McpServerTool,Description("Replace keyframes of multiple tracks atomically with one undo. tracksJson=[{trackId,points:[{time,value},...]}], or trackNumber instead of ID. All times/limits are validated before changing any track. 1–128 distinct tracks, >=2 strictly increasing points per track, <=100000 total. Stops playback, preserves Apply selection; doesn't move or save CAD during editing.")]
    public static Task<string> mechcue_set_keyframes(string tracksJson,string sessionId="",CancellationToken cancellationToken=default)
    {using var json=JsonDocument.Parse(tracksJson);return Send("set_keyframes",new{tracks=json.RootElement.Clone()},sessionId,cancellationToken);}
    [McpServerTool,Description("Delete an exact keyframe time from a track, retaining at least two points. One undo restores it. Stops playback, preserves Apply selection; doesn't move or save CAD during editing.")]
    public static Task<string> mechcue_delete_keyframe(double time,int trackNumber=1,string trackId="",string sessionId="",CancellationToken cancellationToken=default)=>Send("delete_keyframe",new{time,trackNumber,trackId},sessionId,cancellationToken);
    [McpServerTool,Description("Bind an unassigned existing track to a rigid nested part by stable keyPath, including alongside concept axes. kind=部品移動/部品回転/部品座標, axis=X/Y/Z in parent frame. Local mm/deg. Requires modifySharedSubassembly=true because playback edits the shared child document, affecting ALL references. Free/grounded parts only; flexible overrides unsupported. Stops playback/reflection; save_document persists reference keys and baseline. Set suitable keyframes before enabling reflection. Requires Creation/edit mode.")]
    public static Task<string> mechcue_bind_nested_part(string expectedDocument,string keyPath,string kind="部品移動",string axis="X",bool modifySharedSubassembly=false,int trackNumber=1,string trackId="",string sessionId="",CancellationToken cancellationToken=default)
    {EnsureWrite();return Send("bind_nested",new{expectedDocument,keyPath,kind,axis,modifySharedSubassembly,trackNumber,trackId},sessionId,cancellationToken);}
}

[McpServerToolType]
public static class SolidEdgeTools
{
    static readonly Lazy<StaWorker> worker=new(()=>new());
    internal static Task<string> ExecuteCad(string operation, Func<object> action, bool write = true)
    {
        try { McpAccessSettings.EnsureAllowed(write); }
        catch (InvalidOperationException error) { throw new ModelContextProtocol.McpException(error.Message); }
        return worker.Value.Run(() => {
            try { McpAccessSettings.EnsureAllowed(write); var result = action(); DiagnosticLog.Write("mcp-cad-" + operation); return JsonSerializer.Serialize(result); }
            catch (Exception error) { DiagnosticLog.Error("mcp-cad-" + operation, error); throw new ModelContextProtocol.McpException((error is System.Reflection.TargetInvocationException ? error.InnerException ?? error : error).Message); }
        });
    }
    static Task<string> Read(string method,int part=0,string expected="")
    {
        return ExecuteCad(method, () => Bridge.ReadSolidEdge(method,part,expected), false);
    }
    [McpServerTool(ReadOnly=true),Description("Read the active Solid Edge document's name, fullName, read-only and dirty status. Requires Read/select or Creation/edit mode in MechCue MCP tray settings; does not open or save files.")]
    public static Task<string> solidedge_get_document()=>Read("document");
    [McpServerTool(ReadOnly=true),Description("List top-level assembly parts and absolute XYZ position in millimetres. Requires Read/select or Creation/edit mode in MechCue MCP tray settings. Set expectedDocument to fullName from solidedge_get_document to detect active-document changes.")]
    public static Task<string> solidedge_list_parts(string expectedDocument="")=>Read("parts",expected:expectedDocument);
    [McpServerTool(ReadOnly=true),Description("Read the active document's variable table. Values use Solid Edge native internal units (metres/radians), not MechCue display units; includes native unitsType. Requires Read/select or Creation/edit mode in MechCue MCP tray settings.")]
    public static Task<string> solidedge_list_variables(string expectedDocument="")=>Read("variables",expected:expectedDocument);
    [McpServerTool,Description("Select and highlight one top-level assembly part by 1-based partNumber. Replaces CAD selection; does not move geometry. Required expectedDocument must equal the current document fullName. Requires Read/select or Creation/edit mode in MechCue MCP tray settings.")]
    public static Task<string> solidedge_select_part(int partNumber,string expectedDocument)=>Read("select_part",partNumber,expectedDocument);
}
sealed class StaWorker
{
    readonly BlockingCollection<Action> queue=new();
    public StaWorker(){var thread=new Thread(()=>{foreach(var action in queue.GetConsumingEnumerable())action();}){IsBackground=true,Name="Solid Edge MCP STA"};thread.SetApartmentState(ApartmentState.STA);thread.Start();}
    public Task<string> Run(Func<string> action){var completion=new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);queue.Add(()=>{try{completion.SetResult(action());}catch(Exception ex){completion.SetException(ex.InnerException ?? ex);}});return completion.Task;}
}
