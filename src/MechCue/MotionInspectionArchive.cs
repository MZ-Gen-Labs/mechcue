using System.Text.Json;
namespace MechCue;

// Each series spills independently; summaries and MCP responses never materialize the full archive.
sealed class MotionInspectionSeries(string series,long memoryLimit=16*1024*1024,long diskLimit=256*1024*1024)
{
    readonly List<JsonElement> memory=[];readonly List<string> files=[];long bytes,memoryBytes;string? directory;
    public int Count {get;private set;}
    public bool Archived=>directory!=null;
    public void Add<T>(T value)
    {
        var json=JsonSerializer.SerializeToElement(value);long size=System.Text.Encoding.UTF8.GetByteCount(json.GetRawText());
        if(bytes+size>diskLimit)throw new IOException("Inspection archive capacity reached; remaining intervals are unverified");
        if(directory==null&&bytes+size>memoryLimit){directory=Path.Combine(Root,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);Flush();}
        memory.Add(json);bytes+=size;memoryBytes+=size;Count++;
        if(directory!=null&&memoryBytes>=1024*1024)Flush();
    }
    public static string Root=>Path.GetFullPath(Environment.GetEnvironmentVariable("MECHCUE_INSPECTION_RESULTS_DIRECTORY")??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"MechCue","InspectionResults"));
    void Flush(){if(memory.Count==0)return;string name=series+"-"+files.Count.ToString("D6")+".json";using(var stream=File.Create(Path.Combine(directory!,name)))JsonSerializer.Serialize(stream,memory);files.Add(name);memory.Clear();memoryBytes=0;}
    public JsonElement Preview(){if(directory==null)return JsonSerializer.SerializeToElement(memory);Flush();return JsonSerializer.SerializeToElement(ReadFiles(directory!,files).Take(100));}
    public object? Manifest(){if(directory==null)return null;Flush();return new{directory,files=files.ToArray(),count=Count,bytes};}
    static IEnumerable<JsonElement> ReadFiles(string folder,IEnumerable<string> names)
    {
        string path=Path.GetFullPath(folder);if(!path.StartsWith(Root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("Invalid inspection archive directory");
        foreach(string name in names){if(Path.GetFileName(name)!=name)throw new IOException("Invalid inspection archive file");using var stream=File.OpenRead(Path.Combine(path,name));using var json=JsonDocument.Parse(stream);foreach(var item in json.RootElement.EnumerateArray())yield return item.Clone();}
    }
    public static IEnumerable<JsonElement> Read(JsonElement report,string series)
    {
        if(report.TryGetProperty("detailArchive",out var archive)&&archive.TryGetProperty(series,out var manifest)&&manifest.ValueKind==JsonValueKind.Object)
            return ReadFiles(manifest.GetProperty("directory").GetString()!,manifest.GetProperty("files").EnumerateArray().Select(v=>v.GetString()!));
        return series=="samples"?report.GetProperty("samples").EnumerateArray().Select(v=>v.Clone()):report.GetProperty("continuousVerification").GetProperty("Segments").EnumerateArray().Select(v=>v.Clone());
    }
    public static int Total(JsonElement report,string series)=>report.TryGetProperty("detailArchive",out var archive)&&archive.TryGetProperty(series,out var m)&&m.ValueKind==JsonValueKind.Object?m.GetProperty("count").GetInt32():series=="samples"?report.GetProperty("samples").GetArrayLength():report.GetProperty("continuousVerification").GetProperty("Segments").GetArrayLength();
    public static void DeleteOwnedArchives(JsonElement report)
    {
        if(!report.TryGetProperty("detailArchive",out var archive))return;
        foreach(var item in archive.EnumerateObject()){
            if(item.Value.ValueKind!=JsonValueKind.Object)continue;
            try{string folder=Path.GetFullPath(item.Value.GetProperty("directory").GetString()!);
                if(!folder.StartsWith(Root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||!Guid.TryParseExact(Path.GetFileName(folder),"N",out _))continue;
                foreach(var file in item.Value.GetProperty("files").EnumerateArray()){string name=file.GetString()!;if(Path.GetFileName(name)!=name||!System.Text.RegularExpressions.Regex.IsMatch(name,"^(samples|segments)-[0-9]{6}\\.json$"))continue;File.Delete(Path.Combine(folder,name));}
                if(Directory.Exists(folder)&&!Directory.EnumerateFileSystemEntries(folder).Any())Directory.Delete(folder);
            }catch(Exception error)when(error is IOException or UnauthorizedAccessException){DiagnosticLog.Error("inspection-archive-cleanup",error);}
        }
    }
}
