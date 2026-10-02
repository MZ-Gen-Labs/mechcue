using System.Text.Json;
using System.IO.Pipes;
using System.Text;
namespace MechCue;
public static partial class SelfTest
{
    static void TestWorkflowExtensions()
    {
        void Stage(string name)=>File.AppendAllText(Path.Combine(AppContext.BaseDirectory,"workflow-test-progress.txt"),name+Environment.NewLine);
        Stage("operations");
        void Assert(bool condition,string message){if(!condition)throw new Exception(message);}
        var operations=new AiOperations();int calls=0;
        var pending=new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        string id=Guid.NewGuid().ToString();var command=JsonSerializer.SerializeToElement(new {method="save_document",args=new {expectedDocument="test"}});
        operations.Start(id,command,()=>{calls++;return pending.Task;});operations.Start(id,command,()=>{calls++;return pending.Task;});
        Assert(calls==1,"Retry must not execute a second mutation");
        bool rejected=false;try{operations.Start(id,JsonSerializer.SerializeToElement(new {method="different"}),()=>Task.FromResult<object>(0));}catch(InvalidOperationException){rejected=true;}
        Assert(rejected,"Idempotency must reject changed arguments");pending.SetResult(new {saved=true});
        Assert(SpinWait.SpinUntil(()=>JsonSerializer.SerializeToElement(operations.Get(id)).GetProperty("state").GetString()=="completed",2000),"Operation completion retained");
        string failed=Guid.NewGuid().ToString();operations.Start(failed,command,()=>Task.FromException<object>(new InvalidOperationException("specific failure")));
        Assert(JsonSerializer.SerializeToElement(operations.Get(failed)).GetProperty("error").GetString()=="specific failure","Operation error retained");

        Stage("nested");var child=new FakeDocument();var leaf=new FakePart();child.Occurrences.Items.Add(leaf);
        var root=new FakeDocument();var parent=new FakeNestedAssembly(child);root.Occurrences.Items.Add(parent);
        string path="/"+Convert.ToHexString(parent.PersistentId.ToByteArray())+"/"+Convert.ToHexString(leaf.PersistentId.ToByteArray());
        var resolved=Bridge.ResolveOccurrenceKeyPath(root,path);
        Assert(ReferenceEquals(resolved.Occurrence,leaf)&&ReferenceEquals(resolved.Parent,child),"Stable nested keys resolve per parent document");
        var app=new FakeApplication{ActiveDocument=root};app.OpenDocuments.Items.Add(root);
        var bridge=new Bridge(app);bridge.Connect();var track=new Track{Kind="部品移動",Axis="X",Points=[new(0,0),new(1,10)]};
        rejected=false;try{bridge.BindNested(track,path,false);}catch(InvalidOperationException){rejected=true;}Assert(rejected,"Shared nested edits require explicit scope");
        var original=leaf.Pose.ToArray();bridge.BindNested(track,path,true);bridge.Apply(1);Assert(Math.Abs(leaf.Pose[12]-original[12]-.01)<1e-9,"Nested local motion");
        var saved=bridge.CaptureTarget(track)!;Assert(saved.KeyPath==path,"Persist nested identity");bridge.Disconnect();Assert(leaf.Pose.SequenceEqual(original),"Nested disconnect restores baseline");
        var settings=new DocumentSettings{Version=3,Tracks=[new SavedTrack{Track=track,Target=saved}]};
        Assert(DocumentSettings.Parse(settings.Json()).Tracks[0].Target!.KeyPath==path,"Nested v3 settings roundtrip");
        settings.Version=2;rejected=false;try{DocumentSettings.Parse(settings.Json());}catch(InvalidDataException){rejected=true;}Assert(rejected,"Nested data cannot masquerade as legacy settings");
        var restored=new Bridge(app);restored.Connect();Assert(restored.RestoreTarget(track,saved)==null,"Restore nested binding by key path");restored.Apply(1);restored.Disconnect();

        Stage("batch");using var form=new MainForm();
        JsonElement State()=>JsonSerializer.SerializeToElement(form.HandleAi(JsonSerializer.SerializeToElement(new {method="get_state"})));
        var initial=State();string trackId=initial.GetProperty("tracks")[0].GetProperty("id").GetString()!;
        var before=initial.GetProperty("tracks")[0].GetProperty("points").GetRawText();
        rejected=false;try{form.HandleAi(JsonSerializer.SerializeToElement(new {method="set_keyframes",args=new {tracks=new[]{new {trackId,points=new[]{new {time=0d,value=5d},new {time=0d,value=10d}}}}}}));}catch(InvalidOperationException){rejected=true;}
        Assert(rejected&&State().GetProperty("tracks")[0].GetProperty("points").GetRawText()==before,"Invalid batch leaves graph unchanged");
        form.HandleAi(JsonSerializer.SerializeToElement(new {method="set_keyframes",args=new {tracks=new[]{new {trackId,points=new[]{new {time=0d,value=5d},new {time=1d,value=10d},new {time=2d,value=5d}}}}}}));
        form.HandleAi(JsonSerializer.SerializeToElement(new {method="undo"}));Assert(State().GetProperty("tracks")[0].GetProperty("points").GetRawText()==before,"Batch one-step undo");

        // Reproduce a native save longer than the old 10-second endpoint deadline.
        Stage("pipe");using var endpoint=new AiEndpoint("long-operation-test",async (_,_)=>{await Task.Delay(11000);return new {saved=true};});
        async Task<JsonElement> Request(object request)
        {
            using var pipe=new NamedPipeClientStream(".",endpoint.Session.Pipe,PipeDirection.InOut,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(2000);using var writer=new StreamWriter(pipe,new UTF8Encoding(false),4096,true){AutoFlush=true};using var reader=new StreamReader(pipe,new UTF8Encoding(false),false,4096,true);
            await writer.WriteLineAsync(JsonSerializer.Serialize(request));return JsonSerializer.Deserialize<JsonElement>((await reader.ReadLineAsync())!);
        }
        var slow=Task.Run(()=>Request(new {method="save_document",args=new{}}));Thread.Sleep(250);
        var ping=Task.Run(()=>Request(new {method="ping"})).GetAwaiter().GetResult();Assert(ping.GetProperty("ok").GetBoolean(),"Health remains responsive during slow save");
        Assert(slow.GetAwaiter().GetResult().GetProperty("result").GetProperty("saved").GetBoolean(),"Slow CAD completion reply survives read deadline");
        Stage("complete");
        endpoint.Dispose();Stage("endpoint disposed");form.Dispose();Stage("form disposed");
    }
    public class FakeNestedAssembly(FakeDocument child):FakePart
    {
        public bool Subassembly=>true;
        public FakeDocument OccurrenceDocument=>child;
    }
}
