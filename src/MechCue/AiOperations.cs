using System.Text.Json;
namespace MechCue;

// Session-local idempotency protects slow saves from duplicate execution after a client timeout.
public sealed class AiOperations
{
    sealed class Entry(string id,string fingerprint)
    {
        public string Id=id, Fingerprint=fingerprint, State="running";
        public object? Result; public string? Error;
        public DateTimeOffset Started=DateTimeOffset.UtcNow; public DateTimeOffset? Finished;
    }
    readonly Dictionary<string,Entry> entries=new(StringComparer.Ordinal);
    readonly object gate=new();
    public object Start(string requestId,JsonElement command,Func<Task<object>> action)
    {
        if(!Guid.TryParse(requestId,out var guid))throw new ArgumentException("requestId must be a client-generated UUID; reuse the same UUID when retrying");
        string id=guid.ToString("N"),fingerprint=command.GetRawText();
        lock(gate)
        {
            if(entries.TryGetValue(id,out var existing))
            {
                if(existing.Fingerprint!=fingerprint)throw new InvalidOperationException("requestId already belongs to different arguments");
                return Snapshot(existing);
            }
            // Never silently evict IDs: a retry of an evicted mutation would execute twice.
            if(entries.Count>=128)throw new InvalidOperationException("Session operation limit (128) reached; open a new session");
            var entry=new Entry(id,fingerprint);entries.Add(id,entry);
            _=Complete(entry,action);return Snapshot(entry);
        }
    }
    async Task Complete(Entry entry,Func<Task<object>> action)
    {
        try {var result=await action().ConfigureAwait(false);lock(gate){entry.Result=result;entry.State="completed";entry.Finished=DateTimeOffset.UtcNow;}}
        catch(Exception error){lock(gate){entry.Error=(error.InnerException??error).Message;entry.State="failed";entry.Finished=DateTimeOffset.UtcNow;}}
    }
    public object Get(string operationId)
    {
        if(!Guid.TryParse(operationId,out var id))throw new ArgumentException("Invalid operationId");
        lock(gate)return entries.TryGetValue(id.ToString("N"),out var entry)?Snapshot(entry):throw new InvalidOperationException("Operation not found in this session");
    }
    static object Snapshot(Entry e)=>new {operationId=e.Id,state=e.State,started=e.Started,finished=e.Finished,result=e.Result,error=e.Error};
}
