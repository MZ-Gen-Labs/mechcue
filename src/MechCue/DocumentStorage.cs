using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Text.Json;

namespace MechCue;

// Standard Windows IStorage; all vtable slots are retained. No Siemens assembly dependency.
[ComImport, Guid("0000000B-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IStorage
{
    void CreateStream([MarshalAs(UnmanagedType.LPWStr)] string name, uint mode, uint reserved1, uint reserved2, out IStream stream);
    void OpenStream([MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr reserved1, uint mode, uint reserved2, out IStream stream);
    void CreateStorage([MarshalAs(UnmanagedType.LPWStr)] string name, uint mode, uint reserved1, uint reserved2, out IStorage storage);
    void OpenStorage([MarshalAs(UnmanagedType.LPWStr)] string name, IStorage? priority, uint mode, IntPtr exclude, uint reserved, out IStorage storage);
    void CopyTo(uint count, IntPtr interfaces, IntPtr exclude, IStorage destination);
    void MoveElementTo([MarshalAs(UnmanagedType.LPWStr)] string name, IStorage destination, [MarshalAs(UnmanagedType.LPWStr)] string newName, uint flags);
    void Commit(uint flags);
    void Revert();
    void EnumElements(uint reserved1, IntPtr reserved2, uint reserved3, out object enumerator);
    void DestroyElement([MarshalAs(UnmanagedType.LPWStr)] string name);
    void RenameElement([MarshalAs(UnmanagedType.LPWStr)] string oldName, [MarshalAs(UnmanagedType.LPWStr)] string newName);
    void SetElementTimes([MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr creation, IntPtr access, IntPtr modification);
    void SetClass(ref Guid clsid);
    void SetStateBits(uint bits, uint mask);
    void Stat(out STATSTG stat, uint flags);
}

internal static class DocumentStorage
{
    const string StreamName = "Configuration.json";
    const int MaximumBytes = 16 * 1024 * 1024;
    static bool Missing(Exception ex) { while (ex is TargetInvocationException && ex.InnerException != null) ex = ex.InnerException; return ex.HResult is unchecked((int)0x80030002) or unchecked((int)0x80030003); }
    internal static void Release(object? obj) { if (obj != null && Marshal.IsComObject(obj)) Marshal.ReleaseComObject(obj); }
    static IStorage Open(object document, bool write) => (IStorage)document.GetType().InvokeMember("AddInsStorage", BindingFlags.GetProperty, null, document, ["MechCue", write ? 0 : 0x10])!;
    internal static string? Read(object document)
    {
        IStorage? storage = null;
        try { storage = Open(document, false); return ReadStream(storage); }
        catch (Exception ex) when (Missing(ex)) { return null; }
        finally { Release(storage); }
    }
    internal static string? ReadStream(IStorage storage)
    {
        IStream? stream = null;
        try
        {
            try { storage.OpenStream(StreamName, IntPtr.Zero, 0x10, 0, out stream); }
            catch (Exception ex) when (Missing(ex)) { storage.OpenStream(StreamName + ".bak", IntPtr.Zero, 0x10, 0, out stream); }
            stream.Stat(out var stat, 1);
            if (stat.cbSize <= 0 || stat.cbSize > MaximumBytes) throw new InvalidDataException("Invalid MechCue storage size");
            byte[] bytes = new byte[(int)stat.cbSize]; var count = Marshal.AllocCoTaskMem(4);
            try { stream.Read(bytes, bytes.Length, count); if (Marshal.ReadInt32(count) != bytes.Length) throw new EndOfStreamException("Incomplete MechCue storage"); }
            finally { Marshal.FreeCoTaskMem(count); }
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (Exception ex) when (Missing(ex)) { return null; }
        finally { Release(stream); }
    }
    internal static void Write(object document, string json)
    {
        if (Convert.ToBoolean(document.GetType().InvokeMember("ReadOnly", BindingFlags.GetProperty, null, document, null))) throw new InvalidOperationException("読み取り専用のアセンブリには設定を保存できません。");
        IStorage? storage = null;
        try { storage = Open(document, true); WriteStream(storage, json); }
        finally { Release(storage); }
        document.GetType().InvokeMember("Dirty", BindingFlags.SetProperty, null, document, [true]);
    }
    static void RemoveIfPresent(IStorage storage, string name) { try { storage.DestroyElement(name); } catch (Exception ex) when (Missing(ex)) { } }
    internal static void WriteStream(IStorage storage, string json)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        if (bytes.Length > MaximumBytes) throw new InvalidDataException("MechCue settings exceed 16 MB");
        IStream? stream = null;
        try
        {
            storage.CreateStream(StreamName + ".new", 0x1012, 0, 0, out stream);
            var count = Marshal.AllocCoTaskMem(4);
            try { stream.Write(bytes, bytes.Length, count); if (Marshal.ReadInt32(count) != bytes.Length) throw new IOException("Incomplete MechCue write"); }
            finally { Marshal.FreeCoTaskMem(count); }
            stream.Commit(0);
        }
        finally { Release(stream); }
        RemoveIfPresent(storage, StreamName + ".bak");
        bool backup = false;
        try { storage.RenameElement(StreamName, StreamName + ".bak"); backup = true; }
        catch (Exception ex) when (Missing(ex)) { }
        try { storage.RenameElement(StreamName + ".new", StreamName); storage.Commit(0); }
        catch { if (backup) { RemoveIfPresent(storage, StreamName); storage.RenameElement(StreamName + ".bak", StreamName); } throw; }
    }
}

public sealed class SavedTarget
{
    public string Property { get; set; } = "";
    public string Label { get; set; } = "";
    public string? ReferenceKey { get; set; }
    public string? AttributeId { get; set; }
    public int ObjectType { get; set; }
    public double[] Baseline { get; set; } = [];
    public Dictionary<string, bool> Grounds { get; set; } = new();
}
public sealed class SavedTrack
{
    public Track Track { get; set; } = new();
    public SavedTarget? Target { get; set; }
    public bool Hidden { get; set; }
}
public sealed class DocumentSettings
{
    public int Version { get; set; } = 1;
    public List<SavedTrack> Tracks { get; set; } = [];
    public decimal Speed { get; set; } = 1;
    public decimal DragStep { get; set; } = 0.1m;
    public bool Loop { get; set; }
    public bool Collision { get; set; }
    public bool Overlay { get; set; } = true;
    public static DocumentSettings Parse(string json)
    {
        var settings = JsonSerializer.Deserialize<DocumentSettings>(json) ?? throw new InvalidDataException("Empty MechCue settings");
        if (settings.Version != 1) throw new InvalidDataException("Unsupported MechCue settings version: " + settings.Version);
        if (settings.Tracks == null || settings.Tracks.Count is < 1 or > 10000) throw new InvalidDataException("Invalid MechCue track count");
        if (settings.Speed is < 0.1m or > 10 || settings.DragStep is < 0 or > 10000) throw new InvalidDataException("Invalid playback settings");
        foreach (var entry in settings.Tracks)
        {
            if (entry?.Track == null || entry.Track.Points == null || entry.Track.Name == null) throw new InvalidDataException("Invalid MechCue track");
            entry.Track.Validate();
            if (entry.Track.Kind is not ("距離拘束" or "角度拘束" or "部品移動" or "部品回転" or "部品座標") || entry.Track.Axis is not ("X" or "Y" or "Z")) throw new InvalidDataException("Invalid MechCue driver");
            if (entry.Track.Points[^1].Time > 100000) throw new InvalidDataException("MechCue time exceeds UI range");
            if (entry.Target is { } target)
            {
                string expected = entry.Track.Kind.StartsWith("部品") ? "Matrix" : entry.Track.Kind == "角度拘束" ? "Angle" : "Offset";
                if (target.Property != expected || target.Baseline == null || target.Baseline.Length != (expected == "Matrix" ? 16 : 1) || target.Baseline.Any(v => !double.IsFinite(v)) || target.Grounds == null || (target.ReferenceKey == null && !Guid.TryParse(target.AttributeId, out _))) throw new InvalidDataException("Invalid MechCue target data");
                if (target.ReferenceKey != null && Convert.FromBase64String(target.ReferenceKey).Length == 0) throw new InvalidDataException("Empty reference key");
            }
        }
        return settings;
    }
    public string Json() => JsonSerializer.Serialize(this);
}
