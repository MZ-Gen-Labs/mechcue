using System.Text.Json;
namespace MechCue;
public sealed partial class Bridge
{
    public static void VerifyCadInSolidEdge(string outputDirectory, string mcpExecutable = "")
    {
        outputDirectory = Path.GetFullPath(outputDirectory);
        if (Directory.Exists(outputDirectory)) throw new IOException("Use a new test directory.");
        Directory.CreateDirectory(outputDirectory);
        var owned = new List<object>(); var checks = new List<string>();
        var application = CadApplication(); object? original = null;
        try { original = Get(application, "ActiveDocument"); } catch { }
        string Name() => CadName(Get(application, "ActiveDocument"));
        string Template(string file) => Path.Combine("C:\\Siemens\\Solid Edge 2026\\Template\\ISO Metric", file);
        void New(string kind, string file) { CadNew(kind, Template(file)); owned.Add(Get(application, "ActiveDocument")); }
        void Verify(bool condition, string message) { if (!condition) throw new Exception(message); checks.Add(message); }
        try {
            New("part", "iso metric part.par");
            string part = Name();
            var planes = Get(Get(application, "ActiveDocument"), "RefPlanes");
            Verify(Convert.ToInt32(Get(planes, "Count")) >= 3, "Reference planes read");
            CadExtrude(part, "rectangle", 60, 40, 0, 20, featureName: "MCP block");
            CadExtrude(part, "circle", 0, 0, 5, 20, xMm: 20, yMm: 20, operation: "cut", featureName: "MCP hole");
            var models = Get(Get(application, "ActiveDocument"), "Models");
            Verify(Convert.ToInt32(Get(Get(GetItem(models, 1), "Features"), "Count")) == 2, "Ordered block and circular cut created");
            var partPath = Path.Combine(outputDirectory, "block.par"); CadSave(part, partPath);
            Verify(File.Exists(partPath), "Part saved");
            try { CadSave(partPath, partPath); throw new Exception("Overwrite was allowed"); } catch (IOException) { checks.Add("Save-as overwrite rejected"); }
            try { CadExtrude("wrong.par", "rectangle", 10, 10, 0, 10); throw new Exception("Wrong document was allowed"); } catch (InvalidOperationException) { checks.Add("Wrong document rejected"); }
            New("part", "iso metric part.par");
            CadExtrude(Name(), "circle", 0, 0, 8, 30, featureName: "MCP pin");
            CadListFeatures(Name());
            var pinPath = Path.Combine(outputDirectory, "pin.par"); CadSave(Name(), pinPath);
            checks.Add("Ordered cylindrical part saved");
            New("part", "iso metric part.par");
            CadExtrude(Name(), "polygon", 0, 0, 0, 10, direction: "symmetric", pointsJson: "[{\"x\":0,\"y\":0},{\"x\":20,\"y\":0},{\"x\":10,\"y\":20}]", featureName: "MCP triangle");
            CadExtrude(Name(), "circle", 0, 0, 2, 10, xMm: 10, yMm: 5, direction: "negative", featureName: "MCP boss");
            CadListFeatures(Name()); checks.Add("Polygon symmetric extrusion and additional negative extrusion verified");
            New("assembly", "iso metric assembly.asm"); string assembly = Name();
            CadPlacePart(assembly, partPath); CadPlacePart(assembly, pinPath, 100, 0, 0, ground: false);
            CadPositionPart(assembly, 2, 80, 0, 0, rzDeg: 90);
            var matrix = Matrix(CadOccurrence(Get(application, "ActiveDocument"), 2));
            Verify(Math.Abs(matrix[12] - .08) < 1e-8 && Math.Abs(matrix[1] - 1) < 1e-8, "Assembly placement and rotation verified");
            CadMatePlanes(assembly, 1, 1, 2, 1, true);
            Verify(Convert.ToInt32(Get(Get(Get(application, "ActiveDocument"), "Relations3d"), "Count")) >= 2, "Assembly ground and plane relation created");
            var assemblyPath = Path.Combine(outputDirectory, "assembly.asm"); CadSave(assembly, assemblyPath);
            New("draft", "iso metric draft.dft"); string draft = Name();
            CadAddDrawingView(draft, partPath, "front", 1, 80, 120);
            CadAddDrawingView(draft, partPath, "top", 1, 80, 190);
            CadAddDrawingView(draft, assemblyPath, "isometric", .5, 220, 150);
            CadUpdateDrawing(draft);
            Verify(Convert.ToInt32(Get(Get(Get(Get(application, "ActiveDocument"), "ActiveSheet"), "DrawingViews"), "Count")) == 3, "Part and assembly drawing views created and updated");
            var draftPath = Path.Combine(outputDirectory, "drawing.dft"); CadSave(draft, draftPath);
            CadExportPdf(draftPath, Path.Combine(outputDirectory, "drawing.pdf"));
            Verify(File.Exists(Path.Combine(outputDirectory, "drawing.pdf")), "Drawing PDF exported");
            if (mcpExecutable != "") VerifyCadMcp(mcpExecutable, outputDirectory, owned, application, checks);
            File.WriteAllLines(Path.Combine(outputDirectory, "result.txt"), checks.Select(c => "PASS: " + c));
        } catch (Exception error) {
            File.WriteAllText(Path.Combine(outputDirectory, "result.txt"), string.Join("\n", checks.Select(c => "PASS: " + c)) + "\nFAIL: " + error); throw;
        } finally {
            foreach (var document in owned.AsEnumerable().Reverse()) try { Call(document, "Close", false); } catch (Exception error) { DiagnosticLog.Error("cad-test-close", error); }
            if (original != null) try { Call(original, "Activate"); } catch { }
        }
    }
    static void VerifyCadMcp(string executable, string directory, List<object> owned, object application, List<string> checks, bool concept = false)
    {
        var start = new System.Diagnostics.ProcessStartInfo(Path.GetFullPath(executable)) {
            UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true,
            RedirectStandardError = true, CreateNoWindow = true, WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
        };
        string accessPath = Path.Combine(directory, "test-access.json");
        File.WriteAllText(accessPath, JsonSerializer.Serialize(new { schema = 1, mode = "write" }));
        start.Environment["MECHCUE_MCP_NO_TRAY"] = "1";
        start.Environment["MECHCUE_MCP_SETTINGS_PATH"] = accessPath;
        using var server = System.Diagnostics.Process.Start(start)!;
        var stderr = server.StandardError.ReadToEndAsync(); int sequence = 0;
        JsonElement Request(string method, object parameters) {
            int id = ++sequence;
            server.StandardInput.WriteLine(JsonSerializer.Serialize(new { jsonrpc = "2.0", id, method, @params = parameters })); server.StandardInput.Flush();
            while (true) {
                var read = server.StandardOutput.ReadLineAsync();
                if (!read.Wait(TimeSpan.FromSeconds(concept ? 180 : 40))) throw new TimeoutException("MCP native call timed out: " + method);
                using var json = JsonDocument.Parse(read.Result ?? throw new IOException("MCP disconnected"));
                if (!json.RootElement.TryGetProperty("id", out var replyId) || replyId.GetInt32() != id) continue;
                if (json.RootElement.TryGetProperty("error", out var error)) throw new Exception(error.ToString());
                return json.RootElement.GetProperty("result").Clone();
            }
        }
        JsonElement Tool(string name, object arguments) {
            var result = Request("tools/call", new { name, arguments });
            if (result.TryGetProperty("isError", out var error) && error.GetBoolean()) throw new Exception(result.ToString());
            using var value = JsonDocument.Parse(result.GetProperty("content")[0].GetProperty("text").GetString()!);
            return value.RootElement.Clone();
        }
        try {
            Request("initialize", new { protocolVersion = "2025-11-25", capabilities = new { }, clientInfo = new { name = "MechCue native CAD verification", version = "1" } });
            server.StandardInput.WriteLine("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}"); server.StandardInput.Flush();
            var doc = Tool("solidedge_new_document", new { kind = "part", templatePath = "C:\\Siemens\\Solid Edge 2026\\Template\\ISO Metric\\iso metric part.par" });
            owned.Add(Get(application, "ActiveDocument")); string expected = doc.GetProperty("fullName").GetString()!;
            if (Tool("solidedge_get_document", new { }).GetProperty("fullName").GetString() != expected) throw new Exception("Unsaved document identity mismatch");
            Tool("solidedge_list_planes", new { expectedDocument = expected });
            Tool("solidedge_extrude_profile", new { expectedDocument = expected, shape = "circle", radiusMm = 12d, depthMm = 25d, featureName = "MCP protocol cylinder" });
            if (Tool("solidedge_list_features", new { expectedDocument = expected }).GetProperty("features").GetArrayLength() != 1) throw new Exception("MCP extrusion verification failed");
            string outputPath = Path.Combine(directory, "mcp-protocol.par");
            Tool("solidedge_save_document", new { expectedDocument = expected, outputPath });
            Tool("solidedge_save_document", new { expectedDocument = outputPath });
            if (!File.Exists(outputPath)) throw new Exception("MCP save failed");
            checks.Add("Actual MCP write-enabled STA, unsaved identity, planes, extrusion, feature status and save verified");
            if (concept) {
                var created=Tool("solidedge_create_concept_machine",new {type="mill5",outputDirectory=Path.Combine(directory,"mcp-mill5"),partTemplate="C:\\Siemens\\Solid Edge 2026\\Template\\ISO Metric\\iso metric part.par",assemblyTemplate="C:\\Siemens\\Solid Edge 2026\\Template\\ISO Metric\\iso metric assembly.asm"});
                owned.Add(Get(application,"ActiveDocument"));expected=created.GetProperty("fullName").GetString()!;
                string manifestPath=created.GetProperty("manifestPath").GetString()!;
                Tool("solidedge_get_concept_machine",new {expectedDocument=expected,manifestPath});
                Tool("solidedge_set_concept_pose",new {expectedDocument=expected,manifestPath,valuesJson="{\"X\":100,\"Y\":50,\"Z\":30,\"A\":45,\"C\":90}"});
                var state=Tool("solidedge_get_concept_machine",new {expectedDocument=expected,manifestPath});
                var workpiece=state.GetProperty("bodies").EnumerateArray().Single(b=>b.GetProperty("id").GetString()=="workpiece").GetProperty("actualMatrix");
                if(Math.Abs(workpiece[12].GetDouble()-.1)>1e-8 || Math.Abs(workpiece[13].GetDouble()-(.05-.08/Math.Sqrt(2)))>1e-8)throw new Exception("MCP concept pose mismatch");
                var invalid=Request("tools/call",new {name="solidedge_set_concept_pose",arguments=new {expectedDocument=expected,manifestPath,valuesJson="{\"X\":999,\"Y\":50,\"Z\":30,\"A\":45,\"C\":90}"}});
                if(!invalid.GetProperty("isError").GetBoolean())throw new Exception("MCP concept travel limit accepted");
                var after=Tool("solidedge_get_concept_machine",new {expectedDocument=expected,manifestPath});
                if(state.GetProperty("bodies").GetRawText()!=after.GetProperty("bodies").GetRawText())throw new Exception("Rejected MCP pose changed parts");
                checks.Add("Actual MCP concept generation/read/parent-axis movement and range rejection verified");
            }
        } finally {
            server.StandardInput.Close();
            if (!server.WaitForExit(3000)) server.Kill();
            File.WriteAllText(Path.Combine(directory, "mcp-stderr.txt"), stderr.GetAwaiter().GetResult());
        }
    }

}
