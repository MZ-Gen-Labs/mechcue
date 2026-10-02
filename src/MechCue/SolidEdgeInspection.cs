namespace MechCue;

public sealed record CadAssemblyNode(string Path, string? KeyPath, string Name, string FileName, int Number, int Depth, bool IsAssembly, double[] LocalMatrix, double[] WorldMatrix);
public sealed partial class Bridge
{
    public static object CadAssemblyTree(string expectedDocument, int maxDepth = 16, int maxOccurrences = 10000)
    {
        if (maxDepth is < 1 or > 32) throw new ArgumentOutOfRangeException(nameof(maxDepth));
        if (maxOccurrences is < 1 or > 10000) throw new ArgumentOutOfRangeException(nameof(maxOccurrences));
        var document = CadDocument(CadApplication(), expectedDocument, ".asm", false);
        return CadAssemblyTreeCore(document, maxDepth, maxOccurrences);
    }
    internal static object CadAssemblyTreeCore(object document, int maxDepth, int maxOccurrences)
    {
        var nodes = new List<CadAssemblyNode>(); var warnings = new List<string>(); bool truncated = false;
        void Visit(object current, string parent, string? keyParent, double[] parentMatrix, int depth, HashSet<string> ancestors)
        {
            var occurrences = Get(current, "Occurrences"); int count = Convert.ToInt32(Get(occurrences, "Count"));
            for (int i = 1; i <= count; i++)
            {
                if (nodes.Count >= maxOccurrences) { truncated = true; return; }
                string path = parent + "/" + i;
                try
                {
                    var occurrence = GetItem(occurrences, i); string name = Convert.ToString(Get(occurrence, "Name")) ?? "";
                    var local = Matrix(occurrence); var world = ConceptMachine.Multiply(parentMatrix, local);
                    string? keyPath = null;
                    try
                    {
                        object[] args = [Array.Empty<byte>(), Type.Missing]; CadCallRef(occurrence, "GetReferenceKey", [0, 1], args);
                        if (args[0] is Array key && key.Length > 0 && keyParent != null)
                            keyPath = keyParent + "/" + Convert.ToHexString(key.Cast<object>().Select(Convert.ToByte).ToArray());
                    }
                    catch (Exception error) { warnings.Add(path + ": reference-key API: " + (error.InnerException ?? error).Message); }
                    if (keyPath == null) warnings.Add(path + ": reference key unavailable; numeric path is not stable after reordering.");
                    bool assembly = Convert.ToBoolean(Get(occurrence, "Subassembly"));
                    string file = Convert.ToString(Get(occurrence, "OccurrenceFileName")) ?? "";
                    nodes.Add(new(path, keyPath, name, file, i, depth, assembly, local, world));
                    if (!assembly) continue;
                    if (depth >= maxDepth) { truncated = true; warnings.Add(path + ": depth limit reached."); continue; }
                    if (ancestors.Contains(file)) { truncated = true; warnings.Add(path + ": cyclic assembly reference."); continue; }
                    var next = new HashSet<string>(ancestors, StringComparer.OrdinalIgnoreCase) { file };
                    Visit(Get(occurrence, "OccurrenceDocument"), path, keyPath, world, depth + 1, next);
                }
                catch (Exception error) { warnings.Add(path + ": " + (error.InnerException ?? error).Message); truncated = true; }
            }
        }
        Visit(document, "", "", CadTransform(0, 0, 0, 0, 0, 0), 1, new(StringComparer.OrdinalIgnoreCase) { CadName(document) });
        return new { document = CadName(document), nodes, count = nodes.Count, truncated, warnings,
            matrixConvention = "Column-major 4x4, translation in metres. World matrices compose rigid document placements; flexible subassembly overrides are not evaluated.",
            identity = "KeyPath combines native occurrence reference keys in each parent. Path is a current 1-based numeric address, not persistent identity." };
    }
    static object CadView(object application, string expectedDocument)
    {
        var document = CadDocument(application, expectedDocument, write: false);
        if (CadExtension(document) == ".dft") throw new InvalidOperationException("This operation requires a 3D document.");
        return Get(Get(application, "ActiveWindow"), "View");
    }
    static object[] CadCamera(object view)
    {
        object[] camera = [0d, 0d, 0d, 0d, 0d, 0d, 0d, 0d, 0d, false, 0d];
        CadCallRef(view, "GetCamera", Enumerable.Range(0, 11).ToArray(), camera); return camera;
    }
    public static object CadGetView(string expectedDocument)
    {
        var camera = CadCamera(CadView(CadApplication(), expectedDocument));
        return new { document = expectedDocument, eyeMetres = camera.Take(3).ToArray(), targetMetres = camera.Skip(3).Take(3).ToArray(), up = camera.Skip(6).Take(3).ToArray(), perspective = camera[9], scaleOrAngle = camera[10] };
    }
    public static object CadSetView(string expectedDocument, string orientation = "current", bool fit = true, double zoomFactor = 1)
    {
        orientation = orientation.ToLowerInvariant();
        if (!new[] { "current", "front", "back", "top", "bottom", "right", "left", "isometric" }.Contains(orientation)) throw new ArgumentException("Invalid orientation.");
        if (!double.IsFinite(zoomFactor) || zoomFactor < .1 || zoomFactor > 10) throw new ArgumentOutOfRangeException(nameof(zoomFactor), "Use 0.1 to 10.");
        var view = CadView(CadApplication(), expectedDocument);
        if (orientation != "current")
        {
            var camera = CadCamera(view); double tx = Convert.ToDouble(camera[3]), ty = Convert.ToDouble(camera[4]), tz = Convert.ToDouble(camera[5]);
            double distance = Math.Sqrt(Enumerable.Range(0, 3).Sum(i => Math.Pow(Convert.ToDouble(camera[i]) - Convert.ToDouble(camera[i + 3]), 2)));
            if (!double.IsFinite(distance) || distance <= 0) throw new InvalidOperationException("Invalid native camera distance.");
            double[] direction = orientation switch { "front" => [0, -1, 0], "back" => [0, 1, 0], "top" => [0, 0, 1], "bottom" => [0, 0, -1], "right" => [1, 0, 0], "left" => [-1, 0, 0], _ => [1, -1, 1] };
            double norm = Math.Sqrt(direction.Sum(x => x * x));
            double[] up = orientation switch { "top" => [0, 1, 0], "bottom" => [0, -1, 0], _ => [0, 0, 1] };
            Call(view, "SetCamera", tx + direction[0] * distance / norm, ty + direction[1] * distance / norm, tz + direction[2] * distance / norm, tx, ty, tz, up[0], up[1], up[2], false, camera[10]);
        }
        if (fit) Call(view, "Fit");
        if (zoomFactor != 1) Call(view, "ZoomCamera", zoomFactor);
        Call(view, "Update"); return CadGetView(expectedDocument);
    }
    public static object CadExportView(string expectedDocument, string outputPath, int width = 1280, int height = 960)
    {
        if (width is < 64 or > 4096 || height is < 64 or > 4096) throw new ArgumentOutOfRangeException(nameof(width), "Image dimensions must be 64 to 4096 pixels.");
        outputPath = CadPath(outputPath, ".jpg", ".jpeg");
        if (File.Exists(outputPath)) throw new IOException("Output already exists.");
        if (!Directory.Exists(Path.GetDirectoryName(outputPath))) throw new DirectoryNotFoundException("Output folder must exist.");
        var view = CadView(CadApplication(), expectedDocument);
        Call(view, "SaveAsImage", outputPath, width, height, Type.Missing, 1, 24, 0, false);
        using var image = System.Drawing.Image.FromFile(outputPath);
        if (image.Width != width || image.Height != height) throw new IOException("Unexpected native image dimensions.");
        return new { outputPath, width, height, document = expectedDocument };
    }
    public static object CadCheckInterference(string expectedDocument, string set1Json = "[]", string set2Json = "[]", bool ignoreThreadInterferences = false, string reportPath = "")
    {
        int[] Parse(string text) { var ids = System.Text.Json.JsonSerializer.Deserialize<int[]>(text) ?? throw new ArgumentException("Sets must be JSON arrays."); if (ids.Length > 1000 || ids.Any(i => i < 1) || ids.Distinct().Count() != ids.Length) throw new ArgumentException("Use up to 1000 distinct positive top-level occurrence numbers."); return ids; }
        var first = Parse(set1Json); var second = Parse(set2Json);
        if (second.Length > 0 && first.Length == 0) throw new ArgumentException("set1Json is required for a two-set comparison.");
        if (first.Intersect(second).Any()) throw new ArgumentException("The two sets must not overlap.");
        if (reportPath != "")
        {
            reportPath = CadPath(reportPath, ".txt");
            if (File.Exists(reportPath)) throw new IOException("Output already exists.");
            if (!Directory.Exists(Path.GetDirectoryName(reportPath))) throw new DirectoryNotFoundException("Output folder must exist.");
        }
        var document = CadDocument(CadApplication(), expectedDocument, ".asm", false);
        if (first.Length == 0) first = Enumerable.Range(1, Convert.ToInt32(Get(Get(document, "Occurrences"), "Count"))).ToArray();
        if (first.Length == 0 || first.Length > 1000) throw new InvalidOperationException("Select 1 to 1000 occurrences.");
        // SE2026 constants: Set1vsSet2=1, Set1vsItself=4. Explicitly choose comparison.
        object[] args = [first.Length, InterferenceSet(first.Select(i => CadOccurrence(document, i)).ToArray()), 0, second.Length > 0 ? 1 : 4,
            second.Length > 0 ? second.Length : Type.Missing, second.Length > 0 ? InterferenceSet(second.Select(i => CadOccurrence(document, i)).ToArray()) : Type.Missing,
            false, reportPath == "" ? Type.Missing : reportPath, reportPath == "" ? Type.Missing : 15, 0, Type.Missing, Type.Missing, Type.Missing, Type.Missing, ignoreThreadInterferences];
        CadCallRef(document, "CheckInterference", [1, 2, 9, 10, 11, 12, 13], args);
        var result = CadInterferenceResult(expectedDocument, first, second, args);
        if (reportPath == "") return result;
        if (!File.Exists(reportPath)) throw new IOException("Solid Edge did not produce the requested report; analysis details remain unverified.");
        var text = File.ReadAllText(reportPath);
        return new { analysis = result, reportPath, bytes = new FileInfo(reportPath).Length, reportText = text[..Math.Min(text.Length, 65536)], reportTextTruncated = text.Length > 65536,
            format = "Native Solid Edge text report; includes occurrence names, centres and volumes when available. Units and formatting are native to the document." };
    }
    internal static object CadInterferenceResult(string document, int[] first, int[] second, object[] args)
    {
        int status = Convert.ToInt32(args[2]), count = Convert.ToInt32(args[9]);
        var a = args[10] is Array aa ? aa.Cast<object?>().ToArray() : [];
        var b = args[11] is Array bb ? bb.Cast<object?>().ToArray() : [];
        var confirmed = args[12] is Array cc ? cc.Cast<object?>().ToArray() : [];
        object? Describe(object? item)
        {
            if (item == null) return null;
            string? name = null; string? type = null;
            try { name = Convert.ToString(Get(item, "Name")); } catch { }
            try { type = Convert.ToString(Get(item, "Type")); } catch { }
            return new { name, nativeType = type, identityResolved = !string.IsNullOrWhiteSpace(name) };
        }
        var pairs = Enumerable.Range(0, Math.Min(count, Math.Min(a.Length, b.Length))).Select(i => new { first = Describe(a[i]), second = Describe(b[i]), confirmed = i < confirmed.Length ? (bool?)Convert.ToBoolean(confirmed[i]) : null }).ToArray();
        string state = status switch { 1 => "clear", 2 => "confirmed", 3 => "probable", 4 => "confirmed-and-probable", 5 => "incomplete", _ => "unknown" };
        return new { document, set1 = first, set2 = second, comparison = second.Length > 0 ? "set1-vs-set2" : "set1-vs-itself", status, state,
            analysisComplete = status is >= 1 and <= 4, clear = status == 1 && count == 0, count, pairs, pairDetailsComplete = pairs.Length == count,
            scope = "Current static pose only. Two-set comparison excludes within-set intersections. Nested assemblies are expanded by Solid Edge. No interference geometry is created. No swept-path verification." };
    }
}
