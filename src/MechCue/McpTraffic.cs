using System.Diagnostics;
using System.Text;
using System.Text.Json;
namespace MechCue;

public sealed record McpTrafficEntry(DateTimeOffset Time, int ProcessId, string Session, string Direction, string Id, string Method, string Status, double? DurationMs, string Json);
public sealed record McpMonitorOptions(bool Capture = true, bool TopMost = false);
public static class McpTraffic
{
    public static string DirectoryPath => McpAccessSettings.SettingsPath + ".traffic";
    public static string OptionsPath => McpAccessSettings.SettingsPath + ".monitor.json";
    public static McpMonitorOptions Options()
    {
        try { using var stream = new FileStream(OptionsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); return JsonSerializer.Deserialize<McpMonitorOptions>(stream) ?? new(); }
        catch { return new(); }
    }
    public static void SaveOptions(McpMonitorOptions options)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(OptionsPath)!);
        string temporary = OptionsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, JsonSerializer.Serialize(options)); File.Move(temporary, OptionsPath, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
// An independent journal never writes to protocol stdout. Failure must not interrupt MCP.
public sealed class McpTrafficJournal
{
    const int MaxBytes = 4 * 1024 * 1024;
    readonly object gate = new();
    readonly string session = Guid.NewGuid().ToString("N");
    readonly Dictionary<string, (string Method, long Start)> pending = new();
    int segment;
    DateTime nextCleanup;
    string PathFor(int index) => Path.Combine(McpTraffic.DirectoryPath, $"{session}-{index}.jsonl");
    public void Record(string direction, string json)
    {
        try {
            lock (gate) {
                if (!McpTraffic.Options().Capture) { pending.Clear(); return; }
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                string id = root.TryGetProperty("id", out var value) ? value.GetRawText() : "";
                string method = root.TryGetProperty("method", out value) ? value.GetString() ?? "" : "";
                string status = method.Length > 0 ? (id.Length > 0 ? "request" : "notification") : "success";
                if (root.TryGetProperty("monitor", out _)) status = "omitted";
                double? duration = null;
                if (method.Length > 0 && root.TryGetProperty("params", out var parameters) && parameters.TryGetProperty("name", out value)) method += ": " + value.GetString();
                if (method.Length > 0 && id.Length > 0) {
                    if (pending.Count > 2000) pending.Clear();
                    pending[direction + id] = (method, Stopwatch.GetTimestamp());
                } else if (id.Length > 0 && pending.Remove((direction == "in" ? "out" : "in") + id, out var request)) {
                    method = request.Method; duration = Stopwatch.GetElapsedTime(request.Start).TotalMilliseconds;
                }
                if (root.TryGetProperty("error", out _) || (root.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.Object && result.TryGetProperty("isError", out value) && value.ValueKind == JsonValueKind.True)) status = "error";
                var entry = new McpTrafficEntry(DateTimeOffset.Now, Environment.ProcessId, session, direction, id, method, status, duration, json);
                Directory.CreateDirectory(McpTraffic.DirectoryPath);
                string file = PathFor(segment);
                if (File.Exists(file) && new FileInfo(file).Length >= MaxBytes) {
                    segment++; file = PathFor(segment);
                    if (segment >= 3) File.Delete(PathFor(segment - 3));
                }
                using (var stream = new FileStream(file, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete)) {
                    var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(entry) + "\n"); stream.Write(bytes);
                }
                if (DateTime.UtcNow >= nextCleanup) {
                    nextCleanup = DateTime.UtcNow.AddMinutes(10);
                    var files = Directory.GetFiles(McpTraffic.DirectoryPath, "*.jsonl").OrderByDescending(File.GetLastWriteTimeUtc).ToArray();
                    foreach (var old in files.Skip(64).Concat(files.Where(p => File.GetLastWriteTimeUtc(p) < DateTime.UtcNow.AddDays(-7))).Distinct())
                        if (!Path.GetFileName(old).StartsWith(session, StringComparison.Ordinal)) try { File.Delete(old); } catch (IOException) { }
                }
            }
        } catch { } // Disk-full, invalid JSON, and monitor failures do not affect the wire.
    }
}
public sealed class McpTrafficStream(Stream inner, McpTrafficJournal journal, string direction) : Stream
{
    const int Limit = 256 * 1024;
    readonly MemoryStream line = new();
    bool overflow;
    void Observe(ReadOnlySpan<byte> bytes)
    {
        foreach (byte b in bytes) {
            if (b == 10) {
                journal.Record(direction, overflow ? "{\"monitor\":\"message exceeds 256 KiB; detail omitted\"}" : Encoding.UTF8.GetString(line.GetBuffer(), 0, (int)line.Length));
                line.SetLength(0); overflow = false;
            } else if (!overflow) {
                if (line.Length >= Limit) { overflow = true; line.SetLength(0); } else line.WriteByte(b);
            }
        }
    }
    public override bool CanRead => inner.CanRead;
    public override bool CanWrite => inner.CanWrite;
    public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() => inner.Flush();
    public override Task FlushAsync(CancellationToken ct) => inner.FlushAsync(ct);
    public override int Read(byte[] buffer, int offset, int count) { int n = inner.Read(buffer, offset, count); Observe(buffer.AsSpan(offset, n)); return n; }
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) { int n = await inner.ReadAsync(buffer, ct); Observe(buffer.Span[..n]); return n; }
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) => ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();
    public override void Write(byte[] buffer, int offset, int count) { inner.Write(buffer, offset, count); Observe(buffer.AsSpan(offset, count)); }
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default) { await inner.WriteAsync(buffer, ct); Observe(buffer.Span); }
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct) => WriteAsync(buffer.AsMemory(offset, count), ct).AsTask();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    protected override void Dispose(bool disposing) { if (disposing) line.Dispose(); base.Dispose(disposing); } // The host owns standard streams.
}
