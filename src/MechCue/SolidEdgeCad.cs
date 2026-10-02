using System.Text.Json;
namespace MechCue;

public sealed record CadPoint(double X, double Y);
public sealed partial class Bridge
{
    static Array CadProfiles(object profile)
    {
        var array = Array.CreateInstance(typeof(System.Runtime.InteropServices.DispatchWrapper), [1], [1]);
        array.SetValue(new System.Runtime.InteropServices.DispatchWrapper(profile), 1); return array;
    }
    static object CadCallRef(object instance, string method, int[] byRef, params object[] args)
    {
        var modifier = new System.Reflection.ParameterModifier(args.Length);
        foreach (int index in byRef) modifier[index] = true;
        return instance.GetType().InvokeMember(method, System.Reflection.BindingFlags.InvokeMethod, null, instance, args, [modifier], null, null)!;
    }
    static object CadApplication()
    {
        System.Runtime.InteropServices.Marshal.ThrowExceptionForHR(CLSIDFromProgID("SolidEdge.Application", out var id));
        GetActiveObject(ref id, IntPtr.Zero, out var application); return application;
    }
    static string CadName(object document)
    {
        string name = Convert.ToString(Get(document, "FullName")) ?? "";
        return string.IsNullOrWhiteSpace(name) ? Convert.ToString(Get(document, "Name"))! : name;
    }
    static string CadExtension(object document) => Convert.ToInt32(Get(document, "Type")) switch { 1 or 8 => ".par", 2 => ".dft", 3 or 10 => ".asm", 4 or 9 => ".psm", _ => throw new InvalidOperationException("Unsupported document type") };
    static object CadDocument(object application, string expected, string? extension = null, bool write = true)
    {
        if (string.IsNullOrWhiteSpace(expected)) throw new ArgumentException("expectedDocument is required. Read solidedge_get_document first.");
        var document = Get(application, "ActiveDocument");
        if (!string.Equals(CadName(document), expected, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Active document differs from expectedDocument. Read the active document again.");
        if (extension != null && CadExtension(document) != extension) throw new InvalidOperationException("This operation requires a " + extension + " document.");
        if (write && Convert.ToBoolean(Get(document, "ReadOnly"))) throw new InvalidOperationException("Document is read-only.");
        return document;
    }
    static object CadInfo(object document) => new { name = Convert.ToString(Get(document, "Name")), fullName = CadName(document), readOnly = Convert.ToBoolean(Get(document, "ReadOnly")), dirty = Convert.ToBoolean(Get(document, "Dirty")) };
    static void CadFinite(double value, string name, bool positive = false)
    {
        if (!double.IsFinite(value) || Math.Abs(value) > 1000000 || (positive && value <= 0)) throw new ArgumentOutOfRangeException(name, "Use finite values within ±1000000; dimensions must be positive.");
    }
    static string CadPath(string path, params string[] extensions)
    {
        if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("Use an absolute file path.");
        path = Path.GetFullPath(path);
        if (!extensions.Contains(Path.GetExtension(path).ToLowerInvariant())) throw new ArgumentException("Unsupported extension: " + Path.GetExtension(path));
        return path;
    }
    static object CadPlane(object document, int number)
    {
        var planes = Get(document, "RefPlanes");
        if (number < 1 || number > Convert.ToInt32(Get(planes, "Count"))) throw new ArgumentOutOfRangeException(nameof(number));
        return GetItem(planes, number);
    }
    static object CadOccurrence(object document, int number)
    {
        var occurrences = Get(document, "Occurrences");
        if (number < 1 || number > Convert.ToInt32(Get(occurrences, "Count"))) throw new ArgumentOutOfRangeException(nameof(number));
        return GetItem(occurrences, number);
    }
    public static object CadNew(string kind, string templatePath = "")
    {
        kind = kind.ToLowerInvariant();
        string progId = kind switch { "part" => "SolidEdge.PartDocument", "assembly" => "SolidEdge.AssemblyDocument", "draft" => "SolidEdge.DraftDocument", _ => throw new ArgumentException("kind must be part, assembly or draft") };
        if (templatePath != "") {
            templatePath = CadPath(templatePath, kind == "part" ? ".par" : kind == "assembly" ? ".asm" : ".dft");
            if (!File.Exists(templatePath)) throw new FileNotFoundException("Template not found", templatePath);
        }
        var document = Call(Get(CadApplication(), "Documents"), "Add", progId, templatePath == "" ? Type.Missing : templatePath);
        if (kind == "part") Set(document, "ModelingMode", 2); // seModelingModeOrdered, SE2026 typelib
        return CadInfo(document);
    }
    public static object CadOpen(string path)
    {
        path = CadPath(path, ".par", ".asm", ".dft", ".psm");
        if (!File.Exists(path)) throw new FileNotFoundException("Document not found", path);
        var documents=Get(CadApplication(),"Documents");
        // OccurrenceDocument can already be loaded without its own visible window.
        // Re-opening that shared child through Documents.Open can block native automation.
        var document=Enumerable.Range(1,Convert.ToInt32(Get(documents,"Count"))).Select(i=>GetItem(documents,i)).FirstOrDefault(d=>string.Equals(CadName(d),path,StringComparison.OrdinalIgnoreCase))
            ?? Call(documents, "Open", path, Type.Missing);
        CadActivateDocument(document);
        return CadInfo(document);
    }
    static void CadActivateDocument(object document)
    {
        var windows=Get(document,"Windows");
        // Mesh deletion in Results can leave the document loaded without a window.
        if(Convert.ToInt32(Get(windows,"Count"))==0)Call(document,"NewWindow",Type.Missing,Type.Missing);
        Call(document,"Activate");
        windows=Get(document,"Windows");
        if(Convert.ToInt32(Get(windows,"Count"))>0)Call(GetItem(windows,1),"Activate");
        Call(CadApplication(),"DoIdle");
    }
    public static object CadSave(string expectedDocument, string outputPath = "")
    {
        var document = CadDocument(CadApplication(), expectedDocument);
        if (outputPath == "") {
            if (!Path.IsPathFullyQualified(CadName(document)) || !File.Exists(CadName(document))) throw new ArgumentException("Unsaved documents require outputPath.");
            Call(document, "Save");
        } else {
            outputPath = CadPath(outputPath, CadExtension(document));
            if (File.Exists(outputPath)) throw new IOException("Output already exists; use Save with no outputPath to explicitly save the current file.");
            if (!Directory.Exists(Path.GetDirectoryName(outputPath))) throw new DirectoryNotFoundException("Output folder must exist.");
            Call(document, "SaveAs", outputPath, Type.Missing, Type.Missing, Type.Missing, Type.Missing, Type.Missing, Type.Missing, Type.Missing, Type.Missing);
        }
        return CadInfo(document);
    }
    public static object CadListPlanes(string expectedDocument, int partNumber = 0)
    {
        var document = CadDocument(CadApplication(), expectedDocument, write: false);
        if (partNumber != 0) {
            if (CadExtension(document) != ".asm") throw new InvalidOperationException("partNumber requires an assembly");
            document = Get(CadOccurrence(document, partNumber), "OccurrenceDocument");
        }
        var planes = Get(document, "RefPlanes");
        return new { partNumber, fullName = CadName(document), planes = Enumerable.Range(1, Convert.ToInt32(Get(planes, "Count"))).Select(i => new { number = i, name = Convert.ToString(Get(GetItem(planes, i), "Name")) }).ToArray() };
    }
    static int CadFeatureStatus(object feature)
    {
        object[] args = [null!]; var modifier = new System.Reflection.ParameterModifier(1); modifier[0] = true;
        return Convert.ToInt32(feature.GetType().InvokeMember("Status", System.Reflection.BindingFlags.GetProperty, null, feature, args, [modifier], null, null));
    }
    public static object CadListFeatures(string expectedDocument)
    {
        var document = CadDocument(CadApplication(), expectedDocument, ".par", false);
        var models = Get(document, "Models"); var result = new List<object>();
        for (int m = 1; m <= Convert.ToInt32(Get(models, "Count")); m++) {
            var model = GetItem(models, m); var features = Get(model, "Features");
            for (int i = 1; i <= Convert.ToInt32(Get(features, "Count")); i++) result.Add(new { modelNumber = m, featureNumber = i, name = Convert.ToString(Get(GetItem(features, i), "Name")), status = CadFeatureStatus(GetItem(features, i)) });
        }
        return new { fullName = CadName(document), modelingMode = Convert.ToInt32(Get(document, "ModelingMode")), features = result };
    }
    public static object CadExtrude(string expectedDocument, string shape, double widthMm, double heightMm, double radiusMm, double depthMm, int planeNumber = 1, double xMm = 0, double yMm = 0, string operation = "add", string direction = "positive", string pointsJson = "", string featureName = "")
    {
        foreach (var value in new[] { xMm, yMm }) CadFinite(value, "profile origin");
        CadFinite(depthMm, nameof(depthMm), true);
        int side = direction switch { "positive" => 2, "negative" => 1, "symmetric" => 3, _ => throw new ArgumentException("direction must be positive, negative or symmetric") };
        if (operation is not ("add" or "cut")) throw new ArgumentException("operation must be add or cut");
        var vertices = new List<CadPoint>();
        if (shape == "rectangle") { CadFinite(widthMm, nameof(widthMm), true); CadFinite(heightMm, nameof(heightMm), true); vertices.AddRange([new(xMm, yMm), new(xMm + widthMm, yMm), new(xMm + widthMm, yMm + heightMm), new(xMm, yMm + heightMm)]); }
        else if (shape == "circle") CadFinite(radiusMm, nameof(radiusMm), true);
        else if (shape == "polygon") {
            vertices = JsonSerializer.Deserialize<List<CadPoint>>(pointsJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new ArgumentException("Missing polygon points");
            if (vertices.Count < 3 || vertices.Count > 256) throw new ArgumentException("A polygon requires 3 to 256 points, without a repeated endpoint.");
            foreach (var p in vertices) { CadFinite(p.X, "point X"); CadFinite(p.Y, "point Y"); }
            if (vertices.Distinct().Count() != vertices.Count) throw new ArgumentException("Polygon points must be distinct.");
        } else throw new ArgumentException("shape must be rectangle, circle or polygon");
        var application = CadApplication(); var document = CadDocument(application, expectedDocument, ".par");
        var plane = CadPlane(document, planeNumber); var models = Get(document, "Models"); int modelCount = Convert.ToInt32(Get(models, "Count"));
        CadValidateFeatureName(models, featureName);
        if (modelCount > 1) throw new InvalidOperationException("Multiple-body parts are not supported by this tool.");
        if (operation == "cut" && modelCount == 0) throw new InvalidOperationException("A cut requires an existing solid model.");
        if (Convert.ToInt32(Get(document, "ModelingMode")) != 2) throw new InvalidOperationException("Use an Ordered part document. This tool does not switch an existing document's modeling mode.");
        object? profileSet = null; bool created = false;
        try {
            profileSet = Call(Get(document, "ProfileSets"), "Add"); var profile = Call(Get(profileSet, "Profiles"), "Add", plane);
            if (shape == "circle") Call(Get(profile, "Circles2d"), "AddByCenterRadius", xMm / 1000, yMm / 1000, radiusMm / 1000);
            else {
                var lines = new List<object>();
                for (int i = 0; i < vertices.Count; i++) { var a = vertices[i]; var b = vertices[(i + 1) % vertices.Count]; lines.Add(Call(Get(profile, "Lines2d"), "AddBy2Points", a.X / 1000, a.Y / 1000, b.X / 1000, b.Y / 1000)); }
                var relations = Get(profile, "Relations2d");
                for (int i = 0; i < lines.Count; i++) Call(relations, "AddKeypoint", lines[i], 1, lines[(i + 1) % lines.Count], 0, Type.Missing);
            }
            if (Convert.ToInt32(Call(profile, "End", 1)) != 0) throw new InvalidOperationException("Solid Edge rejected the closed profile.");
            object feature;
            if (modelCount == 0) {
                var model = CadCallRef(models, "AddFiniteExtrudedProtrusion", [1], 1, CadProfiles(profile), side, depthMm / 1000, Type.Missing, Type.Missing, Type.Missing, Type.Missing);
                created = true; feature = GetItem(Get(model, "ExtrudedProtrusions"), 1);
            } else {
                var collection = Get(GetItem(models, 1), operation == "add" ? "ExtrudedProtrusions" : "ExtrudedCutouts");
                feature = CadCallRef(collection, "AddFiniteMulti", [1], 1, CadProfiles(profile), side, depthMm / 1000); created = true;
            }
            int featureStatus = CadFeatureStatus(feature);
            if (featureStatus != 1216476310) throw new InvalidOperationException("Solid Edge created a feature with non-OK status " + featureStatus + ". Inspect the feature tree; the partial feature has not been removed.");
            Set(profile, "Visible", false);
            if (featureName != "") Set(feature, "Name", featureName);
            return new { fullName = CadName(document), operation, shape, featureName = Convert.ToString(Get(feature, "Name")), depthMm, planeNumber, saved = false };
        } catch (Exception error) {
            if (!created && profileSet != null) { try { Call(profileSet, "Delete"); } catch { /* Keep the original COM diagnostic. */ } }
            DiagnosticLog.Error("cad-extrude", error, new { expectedDocument, operation, shape, featureCreated = created });
            throw new InvalidOperationException((created ? "PARTIAL SUCCESS: feature created; do not retry extrusion. Inspect solidedge_list_features before further edits. " : "Feature creation failed; inspect document before retrying. ") + (error.InnerException ?? error).Message);
        }
    }
    internal static void CadValidateFeatureName(object models, string featureName)
    {
        if (featureName == "") return;
        if (string.IsNullOrWhiteSpace(featureName) || featureName.Length > 255 || featureName.Any(char.IsControl))
            throw new ArgumentException("Use a nonblank feature name of at most 255 characters without control characters.");
        for (int m = 1; m <= Convert.ToInt32(Get(models, "Count")); m++)
        {
            var features = Get(GetItem(models, m), "Features");
            for (int i = 1; i <= Convert.ToInt32(Get(features, "Count")); i++)
                if (string.Equals(Convert.ToString(Get(GetItem(features, i), "Name"))?.Trim(), featureName.Trim(), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Duplicate feature name; no profile or geometry was created: " + featureName);
        }
    }
    public static object CadPlacePart(string expectedDocument, string filePath, double xMm = 0, double yMm = 0, double zMm = 0, double rxDeg = 0, double ryDeg = 0, double rzDeg = 0, bool ground = true)
    {
        filePath = CadPath(filePath, ".par", ".psm", ".asm"); if (!File.Exists(filePath)) throw new FileNotFoundException("Part not found", filePath);
        var matrix = CadTransform(xMm, yMm, zMm, rxDeg, ryDeg, rzDeg);
        var document = CadDocument(CadApplication(), expectedDocument, ".asm");
        if (string.Equals(filePath, CadName(document), StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("An assembly cannot contain itself.");
        var occurrence = Call(Get(document, "Occurrences"), "AddByFilename", filePath, Type.Missing);
        CadCallRef(occurrence, "PutMatrix", [0], matrix, true);
        // AddByFilename can automatically ground the occurrence, depending on CAD settings.
        var relations = Get(occurrence, "Relations3d");
        var grounds = Enumerable.Range(1, Convert.ToInt32(Get(relations, "Count"))).Select(i => GetItem(relations, i)).Where(r => Convert.ToInt32(Get(r, "Type")) == 1959028688).ToList();
        if (ground) {
            if (grounds.Count == 0) Call(Get(document, "Relations3d"), "AddGround", occurrence);
            else foreach (var relation in grounds) Set(relation, "Suppress", false);
        } else foreach (var relation in grounds) Call(relation, "Delete");
        var actual = Matrix(occurrence);
        if (actual.Zip(matrix).Any(pair => Math.Abs(pair.First - pair.Second) > 1e-8)) throw new InvalidOperationException("The inserted part did not retain the requested pose. Inspect the resulting assembly.");
        return new { fullName = CadName(document), number = Convert.ToInt32(Get(Get(document, "Occurrences"), "Count")), name = Convert.ToString(Get(occurrence, "Name")), saved = false };
    }
    public static double[] CadTransform(double xMm, double yMm, double zMm, double rxDeg, double ryDeg, double rzDeg)
    {
        foreach (double value in new[] { xMm, yMm, zMm, rxDeg, ryDeg, rzDeg }) CadFinite(value, "placement");
        double a = rxDeg * Math.PI / 180, b = ryDeg * Math.PI / 180, c = rzDeg * Math.PI / 180;
        double ca = Math.Cos(a), sa = Math.Sin(a), cb = Math.Cos(b), sb = Math.Sin(b), cc = Math.Cos(c), sc = Math.Sin(c);
        // Column-major Rz * Ry * Rx, metres; matching Occurrence.GetMatrix/PutMatrix.
        return [cc*cb, sc*cb, -sb, 0, cc*sb*sa-sc*ca, sc*sb*sa+cc*ca, cb*sa, 0, cc*sb*ca+sc*sa, sc*sb*ca-cc*sa, cb*ca, 0, xMm/1000, yMm/1000, zMm/1000, 1];
    }
    public static object CadPositionPart(string expectedDocument, int partNumber, double xMm, double yMm, double zMm, double rxDeg = 0, double ryDeg = 0, double rzDeg = 0)
    {
        var matrix = CadTransform(xMm, yMm, zMm, rxDeg, ryDeg, rzDeg);
        var document = CadDocument(CadApplication(), expectedDocument, ".asm"); var occurrence = CadOccurrence(document, partNumber);
        // This API can move grounded parts; existing assembly relations may limit the final pose.
        CadCallRef(occurrence, "PutMatrix", [0], matrix, true); var actual = Matrix(occurrence);
        if (actual.Zip(matrix).Any(pair => Math.Abs(pair.First - pair.Second) > 1e-8)) throw new InvalidOperationException("The assembly solver did not retain the requested pose. Existing constraints may prevent movement; inspect the resulting assembly.");
        return new { fullName = CadName(document), partNumber, xMm, yMm, zMm, saved = false };
    }
    public static object CadMatePlanes(string expectedDocument, int partA, int planeA, int partB, int planeB, bool normalsAligned = false, double offsetMm = 0)
    {
        CadFinite(offsetMm, nameof(offsetMm)); if (partA == partB) throw new ArgumentException("Choose two different occurrences.");
        var document = CadDocument(CadApplication(), expectedDocument, ".asm");
        var a = CadOccurrence(document, partA); var b = CadOccurrence(document, partB);
        var refA = Call(document, "CreateReference", a, CadPlane(Get(a, "OccurrenceDocument"), planeA));
        var refB = Call(document, "CreateReference", b, CadPlane(Get(b, "OccurrenceDocument"), planeB));
        var relation = CadCallRef(Get(document, "Relations3d"), "AddPlanar", [3, 4], refA, refB, normalsAligned, new double[3], new double[3]);
        Set(relation, "Offset", offsetMm / 1000);
        Call(document, "UpdateAll");
        int status = Convert.ToInt32(Get(relation, "Status"));
        int detailedStatus = Convert.ToInt32(Get(relation, "DetailedStatus"));
        if (status != 1 || detailedStatus != 1) throw new InvalidOperationException($"The new plane relation is not solved (status={status}, detailedStatus={detailedStatus}). Inspect the partial relation; it was not automatically removed.");
        return new { fullName = CadName(document), status, detailedStatus, partA, planeA, partB, planeB, offsetMm, saved = false };
    }
    public static object CadAddDrawingView(string expectedDocument, string modelPath, string orientation, double scale, double xMm, double yMm)
    {
        modelPath = CadPath(modelPath, ".par", ".psm", ".asm"); if (!File.Exists(modelPath)) throw new FileNotFoundException("Model not found", modelPath);
        CadFinite(scale, nameof(scale), true); CadFinite(xMm, nameof(xMm)); CadFinite(yMm, nameof(yMm));
        int viewOrientation = orientation switch { "top" => 1, "right" => 2, "left" => 3, "front" => 4, "bottom" => 5, "back" => 6, "isometric" => 9, _ => throw new ArgumentException("Unknown orientation") };
        var document = CadDocument(CadApplication(), expectedDocument, ".dft"); var links = Get(document, "ModelLinks"); object? link = null;
        for (int i = 1; i <= Convert.ToInt32(Get(links, "Count")); i++) {
            var candidate = GetItem(links, i);
            if (string.Equals(Convert.ToString(Get(candidate, "FileName")), modelPath, StringComparison.OrdinalIgnoreCase)) { link = candidate; break; }
        }
        link ??= Call(links, "Add", modelPath); var views = Get(Get(document, "ActiveSheet"), "DrawingViews");
        string extension = Path.GetExtension(modelPath).ToLowerInvariant();
        var view = extension == ".asm" ? Call(views, "AddAssemblyView", link, viewOrientation, scale, xMm / 1000, yMm / 1000, 0, "", false, 0)
            : extension == ".psm" ? Call(views, "AddSheetMetalView", link, viewOrientation, scale, xMm / 1000, yMm / 1000, 0)
            : Call(views, "AddPartView", link, viewOrientation, scale, xMm / 1000, yMm / 1000, 0);
        Call(view, "Update");
        return new { fullName = CadName(document), name = Convert.ToString(Get(view, "Name")), number = Convert.ToInt32(Get(views, "Count")), orientation, scale, saved = false };
    }
    public static object CadUpdateDrawing(string expectedDocument)
    {
        var document = CadDocument(CadApplication(), expectedDocument, ".dft"); int updated = 0;
        var sheets = Get(document, "Sheets");
        for (int s = 1; s <= Convert.ToInt32(Get(sheets, "Count")); s++) {
            var views = Get(GetItem(sheets, s), "DrawingViews");
            for (int v = 1; v <= Convert.ToInt32(Get(views, "Count")); v++) { Call(GetItem(views, v), "Update"); updated++; }
        }
        return new { fullName = CadName(document), updated, saved = false };
    }
    public static object CadExportPdf(string expectedDocument, string outputPath)
    {
        outputPath = CadPath(outputPath, ".pdf");
        if (File.Exists(outputPath)) throw new IOException("Output already exists.");
        if (!Directory.Exists(Path.GetDirectoryName(outputPath))) throw new DirectoryNotFoundException("Output folder must exist.");
        var document = CadDocument(CadApplication(), expectedDocument, ".dft", false);
        Call(document, "SaveAs", outputPath, Type.Missing, Type.Missing, Type.Missing, Type.Missing, Type.Missing, Type.Missing, Type.Missing, Type.Missing);
        if (!File.Exists(outputPath) || new FileInfo(outputPath).Length == 0) throw new IOException("Solid Edge did not produce a PDF.");
        return new { fullName = CadName(document), outputPath, bytes = new FileInfo(outputPath).Length };
    }
}
