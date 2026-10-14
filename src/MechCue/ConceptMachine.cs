using System.Text.Json;
namespace MechCue;

public sealed class ConceptAxis
{
    public string Id { get; set; } = "";
    public string Parent { get; set; } = "";
    public string Kind { get; set; } = "linear";
    public string Direction { get; set; } = "X";
    public double Minimum { get; set; }
    public double Maximum { get; set; }
    public double[] OriginMm { get; set; } = [0, 0, 0];
}
public sealed class ConceptBody
{
    public string Id { get; set; } = "";
    public string Parent { get; set; } = "";
    public string Shape { get; set; } = "box";
    public double[] SizeMm { get; set; } = [100, 100, 100];
    public double[] CenterMm { get; set; } = [0, 0, 0];
    public string File { get; set; } = "";
    public string Occurrence { get; set; } = "";
    public double[] GeometryMatrix { get; set; } = Bridge.CadTransform(0, 0, 0, 0, 0, 0);
}
public sealed class ConceptMachine
{
    public int Schema { get; set; } = 1;
    public string Type { get; set; } = "";
    public string AssemblyFile { get; set; } = "concept.asm";
    public List<ConceptAxis> Axes { get; set; } = [];
    public List<ConceptBody> Bodies { get; set; } = [];
    public Dictionary<string, double> Values { get; set; } = [];
    public static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, WriteIndented = true };
    /// <summary>Builds a concept machine from its template (<see cref="ConceptTemplates"/>) and validates it.</summary>
    public static ConceptMachine Create(string type, double xTravelMm, double yTravelMm, double zTravelMm)
    {
        foreach (double length in new[] { xTravelMm, yTravelMm, zTravelMm })
            if (!double.IsFinite(length) || length < 10 || length > 5000) throw new ArgumentException("Axis travel must be between 10 and 5000 mm.");
        var types = ConceptTemplates.All.Select(t => t.Type).ToList();
        var template = ConceptTemplates.Find(type) ?? throw new ArgumentException($"type must be {string.Join(", ", types.SkipLast(1))} or {types[^1]}.");
        var model = new ConceptMachine { Type = type };
        template(new ConceptBuilder(model), xTravelMm, yTravelMm, zTravelMm);
        model.Validate(); return model;
    }
    public void Validate()
    {
        if (Schema != 1 || Axes.Count is < 1 or > 32 || Bodies.Count is < 1 or > 128) throw new ArgumentException("Unsupported concept schema or model size.");
        bool Id(string s) => s.Length is > 0 and <= 64 && s.All(c => char.IsAsciiLetterOrDigit(c) || c == '-');
        if (Axes.Select(a => a.Id).Concat(Bodies.Select(b => b.Id)).Any(id => !Id(id)) || Axes.Select(a => a.Id).Concat(Bodies.Select(b => b.Id)).Distinct().Count() != Axes.Count+Bodies.Count) throw new ArgumentException("Concept IDs must be unique ASCII names.");
        var known = new HashSet<string> { "" };
        foreach (var axis in Axes) {
            if (!known.Contains(axis.Parent) || axis.Kind is not ("linear" or "rotary") || axis.Direction is not ("X" or "Y" or "Z") || !double.IsFinite(axis.Minimum) || !double.IsFinite(axis.Maximum) || Math.Abs(axis.Minimum)>1000000 || Math.Abs(axis.Maximum)>1000000 || axis.Minimum > 0 || axis.Maximum < 0 || axis.Maximum <= axis.Minimum) throw new ArgumentException("Invalid axis, range or parent order.");
            Vector(axis.OriginMm); known.Add(axis.Id);
        }
        foreach (var body in Bodies) {
            if (!known.Contains(body.Parent) || body.Shape is not ("box" or "cylinder")) throw new ArgumentException("Invalid body parent or shape.");
            Vector(body.CenterMm); Vector(body.SizeMm);
            if (body.SizeMm.Any(v => v <= 0) || (body.Shape == "cylinder" && body.SizeMm[0] != body.SizeMm[1])) throw new ArgumentException("Invalid body dimensions.");
            var m = body.GeometryMatrix;
            if (m.Length == 16) {
                if (Math.Abs(m[3])+Math.Abs(m[7])+Math.Abs(m[11])+Math.Abs(m[15]-1)>1e-8) throw new ArgumentException("Geometry matrix must be affine.");
                for (int a=0;a<3;a++) for (int b=0;b<3;b++)
                    if(Math.Abs(Enumerable.Range(0,3).Sum(k=>m[a*4+k]*m[b*4+k])-(a==b?1:0))>1e-8) throw new ArgumentException("Geometry matrix must be rigid.");
                double determinant=m[0]*(m[5]*m[10]-m[9]*m[6])-m[4]*(m[1]*m[10]-m[9]*m[2])+m[8]*(m[1]*m[6]-m[5]*m[2]);
                if(Math.Abs(determinant-1)>1e-8) throw new ArgumentException("Geometry matrix cannot reflect geometry.");
            }
            if (body.GeometryMatrix.Length != 16 || body.GeometryMatrix.Any(v => !double.IsFinite(v) || Math.Abs(v)>1000000)) throw new ArgumentException("Invalid geometry matrix.");
        }
        ValidateValues(Values);
    }
    static void Vector(double[] v) { if (v.Length != 3 || v.Any(x => !double.IsFinite(x) || Math.Abs(x) > 100000)) throw new ArgumentException("Invalid concept vector."); }
    public void ValidateValues(IReadOnlyDictionary<string, double> values) {
        if (values.Count != Axes.Count || values.Keys.Any(k => !Axes.Any(a => a.Id == k))) throw new ArgumentException("Supply exactly the model's axis IDs.");
        foreach (var axis in Axes) if (!values.TryGetValue(axis.Id, out double value) || !double.IsFinite(value) || value < axis.Minimum || value > axis.Maximum) throw new ArgumentException($"Axis {axis.Id} must be within [{axis.Minimum}, {axis.Maximum}] {(axis.Kind == "linear" ? "mm" : "degrees")}.");
    }
    public Dictionary<string, double[]> Poses(IReadOnlyDictionary<string, double> values) {
        var joints = AxisPoses(values);
        return Bodies.ToDictionary(b => b.Id, b => Multiply(Multiply(joints[b.Parent], Bridge.CadTransform(b.CenterMm[0],b.CenterMm[1],b.CenterMm[2],0,0,0)), b.GeometryMatrix));
    }
    public Dictionary<string, double[]> AxisPoses(IReadOnlyDictionary<string, double> values) {
        ValidateValues(values);
        var joints = new Dictionary<string, double[]> { [""] = Bridge.CadTransform(0,0,0,0,0,0) };
        foreach (var axis in Axes) {
            double v = values[axis.Id]; int n = axis.Direction == "X" ? 0 : axis.Direction == "Y" ? 1 : 2;
            double[] xyz = (double[])axis.OriginMm.Clone(), r = new double[3];
            if (axis.Kind == "linear") xyz[n] += v; else r[n] = v;
            joints[axis.Id] = Multiply(joints[axis.Parent], Bridge.CadTransform(xyz[0],xyz[1],xyz[2],r[0],r[1],r[2]));
        }
        return joints;
    }
    public static double[] Multiply(double[] a, double[] b) {
        var result = new double[16];
        for (int col=0; col<4; col++) for (int row=0; row<4; row++) for(int k=0;k<4;k++) result[col*4+row] += a[k*4+row]*b[col*4+k];
        return result;
    }
}
