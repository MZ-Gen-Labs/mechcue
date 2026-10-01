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
    public static ConceptMachine Create(string type, double xTravelMm, double yTravelMm, double zTravelMm)
    {
        foreach (double length in new[] { xTravelMm, yTravelMm, zTravelMm })
            if (!double.IsFinite(length) || length < 10 || length > 5000) throw new ArgumentException("Axis travel must be between 10 and 5000 mm.");
        if (type is not ("mill3" or "mill4" or "mill5" or "gantry")) throw new ArgumentException("type must be mill3, mill4, mill5 or gantry.");
        var model = new ConceptMachine { Type = type };
        void Axis(string id, string parent, string direction, double travel, string kind = "linear", double[]? origin = null) {
            model.Axes.Add(new() { Id = id, Parent = parent, Direction = direction, Kind = kind, Minimum = -travel / 2, Maximum = travel / 2, OriginMm = origin ?? [0, 0, 0] }); model.Values[id] = 0;
        }
        void Box(string id, string parent, double x, double y, double z, double cx, double cy, double cz) => model.Bodies.Add(new() { Id = id, Parent = parent, SizeMm = [x, y, z], CenterMm = [cx, cy, cz] });
        void Cylinder(string id, string parent, double diameter, double depth, double cx, double cy, double cz) => model.Bodies.Add(new() { Id = id, Parent = parent, Shape = "cylinder", SizeMm = [diameter, diameter, depth], CenterMm = [cx, cy, cz] });
        double w = xTravelMm + 400, d = yTravelMm + 400, h = zTravelMm + 600;
        Box("base", "", w, d, 100, 0, 0, 50);
        if (type == "gantry") {
            Axis("X", "", "X", xTravelMm); Axis("Y", "X", "Y", yTravelMm); Axis("Z", "Y", "Z", zTravelMm);
            Box("left-column", "", 100, d, h, -w/2+50, 0, 100+h/2);
            Box("right-column", "", 100, d, h, w/2-50, 0, 100+h/2);
            Box("bridge", "Y", w-200, 100, 100, 0, 0, h+50);
            Box("x-carriage", "X", 120, d, 80, 0, 0, h+140);
            Box("z-slide", "Z", 80, 80, zTravelMm+200, 0, 0, 400+zTravelMm/2);
            Box("gripper", "Z", 140, 80, 60, 0, 0, 270);
            Box("workpiece", "", 100, 100, 100, 0, 0, 150);
        } else {
            Axis("Y", "", "Y", yTravelMm); Axis("X", "Y", "X", xTravelMm); Axis("Z", "", "Z", zTravelMm);
            Box("column", "", 200, 180, h, 0, d/2-90, 100+h/2);
            Box("saddle", "Y", 320, 260, 80, 0, 0, 140);
            Box("table", "X", xTravelMm+200, 220, 60, 0, 0, 210);
            Box("head", "Z", 180, 180, 220, 0, 0, h-50);
            Cylinder("spindle", "Z", 80, 100, 0, 0, h-210);
            Cylinder("tool", "Z", 20, 100, 0, 0, h-310);
            string workParent = "X"; double workZ = 290;
            if (type is "mill4" or "mill5") {
                if (type == "mill5") {
                    Axis("A", "X", "X", 220, "rotary", [0, 0, 350]);
                    Box("trunnion", "A", 280, 200, 60, 0, 0, -30);
                    Axis("C", "A", "Z", 720, "rotary");
                } else Axis("C", "X", "Z", 720, "rotary", [0, 0, 290]);
                Cylinder("rotary-table", "C", 200, 60, 0, 0, 0);
                workParent = "C"; workZ = 80;
            }
            Box("workpiece", workParent, 100, 100, 100, 0, 0, workZ);
        }
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
        ValidateValues(values);
        var joints = new Dictionary<string, double[]> { [""] = Bridge.CadTransform(0,0,0,0,0,0) };
        foreach (var axis in Axes) {
            double v = values[axis.Id]; int n = axis.Direction == "X" ? 0 : axis.Direction == "Y" ? 1 : 2;
            double[] xyz = (double[])axis.OriginMm.Clone(), r = new double[3];
            if (axis.Kind == "linear") xyz[n] += v; else r[n] = v;
            joints[axis.Id] = Multiply(joints[axis.Parent], Bridge.CadTransform(xyz[0],xyz[1],xyz[2],r[0],r[1],r[2]));
        }
        return Bodies.ToDictionary(b => b.Id, b => Multiply(Multiply(joints[b.Parent], Bridge.CadTransform(b.CenterMm[0],b.CenterMm[1],b.CenterMm[2],0,0,0)), b.GeometryMatrix));
    }
    public static double[] Multiply(double[] a, double[] b) {
        var result = new double[16];
        for (int col=0; col<4; col++) for (int row=0; row<4; row++) for(int k=0;k<4;k++) result[col*4+row] += a[k*4+row]*b[col*4+k];
        return result;
    }
}
