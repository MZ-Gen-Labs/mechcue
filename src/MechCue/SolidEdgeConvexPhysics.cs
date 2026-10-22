using System.Text.Json;
using MechCue.Physics;

namespace MechCue;

public sealed partial class Bridge
{
    /// <summary>Capture exact planar convex solids without changing their geometry or saving CAD.</summary>
    public static object CadCaptureConvexPhysics(string expectedDocument, int workpieceNumber, string outputPath, double workpieceMassKg = 0)
    {
        outputPath = CadPath(outputPath, ".json");
        if (!Directory.Exists(Path.GetDirectoryName(outputPath))) throw new DirectoryNotFoundException("The output parent must exist.");
        if (File.Exists(outputPath)) throw new IOException("The physics manifest already exists; choose a new path.");
        if (!double.IsFinite(workpieceMassKg) || workpieceMassKg < 0 || workpieceMassKg > 1000) throw new ArgumentException("workpieceMassKg must be 0 (CAD density) or positive up to 1000 kg.");
        var app = CadApplication(); var doc = CadDocument(app, expectedDocument, ".asm", false);
        if (!Path.IsPathFullyQualified(expectedDocument) || !File.Exists(expectedDocument)) throw new ArgumentException("Save the assembly before capturing physics.");
        var occurrences = Get(doc, "Occurrences"); int count = Convert.ToInt32(Get(occurrences, "Count"));
        if (count is < 2 or > 128 || workpieceNumber < 1 || workpieceNumber > count) throw new ArgumentException("Use an assembly with 2..128 root parts and a valid workpieceNumber.");
        var model = new ConceptMachine { Type = "convex-assembly", AssemblyFile = Path.GetFullPath(expectedDocument) };
        double mass = workpieceMassKg; var summary = new List<object>();
        for (int i = 1; i <= count; i++) {
            var occurrence = GetItem(occurrences, i); string name = Convert.ToString(Get(occurrence, "Name")) ?? "";
            try {
                if (Convert.ToBoolean(Get(occurrence, "Subassembly")) || Convert.ToBoolean(Get(occurrence, "Adjustable")) || Convert.ToBoolean(Get(occurrence, "IsAdjustablePart")) || Convert.ToBoolean(Get(occurrence, "UseSimplified")) || Convert.ToBoolean(Get(occurrence, "HasBodyOverride")))
                    throw new ArgumentException("Only root rigid parts with their complete original geometry are supported.");
                if (!Convert.ToBoolean(Get(occurrence, "IncludeInInterference"))) throw new ArgumentException("Enable IncludeInInterference for every captured body.");
                var part = Get(occurrence, "OccurrenceDocument");
                if (CadExtension(part) != ".par" || Convert.ToInt32(Get(Get(part, "Models"), "Count")) != 1) throw new ArgumentException("Use single-solid .par parts.");
                var native = Get(GetItem(Get(part, "Models"), 1), "Body");
                if(!Convert.ToBoolean(Get(native,"IsSolid")))throw new ArgumentException("Only closed solid bodies are supported.");
                var vertices = Get(native, "Vertices"); int vertexCount = Convert.ToInt32(Get(vertices, "Count"));
                if (vertexCount is < 4 or > 256) throw new ArgumentException("Each part must have 4..256 vertices.");
                var points = Enumerable.Range(1, vertexCount).Select(n => PmiVector(GetItem(vertices, n), "GetPointData")).ToArray();
                ConvexGeometry.ValidateVertices(points);
                var faces = DrawingIndexed(native, "Faces", 1); int faceCount = Convert.ToInt32(Get(faces, "Count"));
                if (faceCount is < 4 or > 512) throw new ArgumentException("Use a closed polyhedron with 4..512 planar faces.");
                for (int f = 1; f <= faceCount; f++) {
                    var face = GetItem(faces, f); var geometry = Get(face, "Geometry");
                    double[] origin, normal;
                    try { origin = PmiVector(geometry, "GetRootPoint"); normal = PmiVector(geometry, "GetNormalVector"); }
                    catch (Exception error) { throw new ArgumentException("Curved faces are not supported in this phase; no bounding-box or convex-hull approximation was substituted.", error); }
                    ConvexGeometry.ValidateSupportingPlane(points, origin, normal);
                    var fv = Get(face, "Vertices");
                    for (int n = 1; n <= Convert.ToInt32(Get(fv, "Count")); n++) {
                        var p = PmiVector(GetItem(fv, n), "GetPointData");
                        if (Math.Abs(Enumerable.Range(0, 3).Sum(k => (p[k]-origin[k])*normal[k])) > 1e-7 * Math.Sqrt(normal.Sum(v => v*v))) throw new ArgumentException("Face is not planar.");
                    }
                }
                // ComputePhysicalProperties updates the cached density: preserve the CAD value, including zero
                // for unassigned static parts. Read it before computing; a unit-density call would lose the material density.
                var materialTable = Call(app, "GetMaterialTable"); object[] densityArgs = [part, 23, null!]; CadCallRef(materialTable, "GetMaterialPropValueFromDoc", [2], densityArgs);
                double density = Convert.ToDouble(densityArgs[2]);
                if(!double.IsFinite(density)||density<0)throw new ArgumentException("Invalid CAD material density.");
                if(i==workpieceNumber && mass==0 && density<=0)throw new ArgumentException("The workpiece needs a valid CAD material density or explicit workpieceMassKg.");
                object[] props = [density, 1e-6, 0d, 0d, 0d, new double[3], new double[3], new double[6], new double[3], new double[9], new double[3], 0d, 0];
                CadCallRef(native, "ComputePhysicalProperties", Enumerable.Range(2, 11).ToArray(), props);
                double volume = Convert.ToDouble(props[2]); var localCenter = ((Array)props[6]).Cast<object>().Select(Convert.ToDouble).ToArray();
                if (!double.IsFinite(volume) || volume <= 0 || localCenter.Length != 3 || localCenter.Any(v => !double.IsFinite(v))) throw new ArgumentException("Solid volume and centroid could not be verified.");
                var matrix = Matrix(occurrence);
                if (!MotionBoundMath.Rigid(matrix)) throw new ArgumentException("Scaled or sheared part placements are unsupported.");
                double[] World(double[] p) => Enumerable.Range(0, 3).Select(k => matrix[k]*p[0]+matrix[k+4]*p[1]+matrix[k+8]*p[2]+matrix[k+12]).ToArray();
                var center = World(localCenter); var hull = points.Select(p => World(p).Zip(center).Select(v => (v.First-v.Second)*1000).ToArray()).ToArray();
                if (i == workpieceNumber && mass == 0) {
                    mass = volume * density;
                }
                string id = i == workpieceNumber ? "workpiece" : "part-" + i;
                model.Bodies.Add(new() { Id = id, Parent = i == workpieceNumber ? "WRX" : "", Shape = "convex", VerticesMm = hull,
                    CenterMm = i == workpieceNumber ? [0,0,0] : center.Select(v => v*1000).ToArray(), SizeMm = Enumerable.Range(0,3).Select(k => (points.Max(p => p[k])-points.Min(p => p[k]))*1000).ToArray(),
                    GeometryMatrix = ConceptMachine.Multiply(CadTransform(-center[0]*1000,-center[1]*1000,-center[2]*1000,0,0,0), matrix),
                    File = Path.GetFullPath(CadName(part)), Occurrence = name });
                summary.Add(new { number=i, id, name, vertexCount, volumeM3=volume });
                if (i == workpieceNumber) {
                    string parent = "";
                    foreach (var (axisId, direction, rotary) in new[] { ("WX","X",false),("WY","Y",false),("WZ","Z",false),("WRZ","Z",true),("WRY","Y",true),("WRX","X",true) }) {
                        model.Axes.Add(new() { Id=axisId, Parent=parent, Kind=rotary?"rotary":"linear", Direction=direction, Minimum=rotary?-720:-100000, Maximum=rotary?720:100000, OriginMm=axisId=="WRZ"?center.Select(v=>v*1000).ToArray():[0,0,0] });
                        model.Values[axisId]=0; parent=axisId;
                    }
                }
            } catch (Exception error) { throw new InvalidOperationException($"Cannot capture occurrence {i} ({name}): {error.GetBaseException().Message}", error); }
        }
        if (!(mass > 0 && mass <= 1000)) throw new ArgumentException("Calculated workpiece mass must be positive and at most 1000 kg; specify workpieceMassKg if needed.");
        model.PhysicsDefaults = new() { WorkpieceMassKg=mass, EndTime=2 };
        model.Validate();
        using (var file = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write)) JsonSerializer.Serialize(file, model, ConceptMachine.JsonOptions);
        return new { manifestPath=outputPath, fullName=expectedDocument, bodies=summary, physicsSetup=model.PhysicsDefaults,
            note="Exact planar convex geometry snapshot; one free workpiece, other bodies fixed. Gravity/contact only; no mechanism or gripper trajectories inferred. CAD poses/materials were not changed or saved. Recapture after geometry edits. Import this manifest into MechCue for computed workpiece tracks, or simulate_concept_rigid_body with the returned physicsSetup." };
    }
}
