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
        if (type is not ("mill3" or "mill4" or "mill5" or "gantry" or "arm5" or "arm5p")) throw new ArgumentException("type must be mill3, mill4, mill5, gantry, arm5 or arm5p.");
        var model = new ConceptMachine { Type = type };
        void Axis(string id, string parent, string direction, double travel, string kind = "linear", double[]? origin = null, double? minimum = null, double? maximum = null) {
            model.Axes.Add(new() { Id = id, Parent = parent, Direction = direction, Kind = kind, Minimum = minimum ?? -travel / 2, Maximum = maximum ?? travel / 2, OriginMm = origin ?? [0, 0, 0] }); model.Values[id] = 0;
        }
        void Box(string id, string parent, double x, double y, double z, double cx, double cy, double cz) => model.Bodies.Add(new() { Id = id, Parent = parent, SizeMm = [x, y, z], CenterMm = [cx, cy, cz] });
        void Cylinder(string id, string parent, double diameter, double depth, double cx, double cy, double cz) => model.Bodies.Add(new() { Id = id, Parent = parent, Shape = "cylinder", SizeMm = [diameter, diameter, depth], CenterMm = [cx, cy, cz] });
        double w = xTravelMm + 400, d = yTravelMm + 400, h = zTravelMm + 600;
        if (type is "arm5" or "arm5p") {
            bool physics = type == "arm5p";
            // Table-mounted five-axis arm (travel arguments do not apply). Joint frames follow the parent
            // link: J1 turret about Z, J2 shoulder / J3 elbow / J4 wrist pitch about Y, J5 wrist roll about X.
            // Adjacent links sit side by side in Y, so pitch rotations never sweep a link through its neighbour.
            Box("base", "", 200, 200, 100, -250, 0, 790);
            Box("table-top", "", 1000, 700, 40, 0, 0, 720);
            foreach (var (tag, x, y) in new[] { ("front-left", -440, -290), ("front-right", 440, -290), ("back-left", -440, 290), ("back-right", 440, 290) })
                Box("table-leg-" + tag, "", 60, 60, 700, x, y, 350);
            Axis("J1", "", "Z", 340, "rotary", [-250, 0, 840]);
            Cylinder("turret", "J1", 180, 80, 0, 0, 40);
            Box("shoulder", "J1", 120, 190, 60, 0, 0, 110);
            Axis("J2", "J1", "Y", 180, "rotary", [0, 0, 110]);
            Box("upper-arm", "J2", 80, 80, 400, 0, 135, 160);
            Axis("J3", "J2", "Y", 270, "rotary", [0, 135, 320]);
            Box("forearm", "J3", 360, 70, 70, 140, 75, 0);
            Axis("J4", "J3", "Y", 240, "rotary", [300, 75, 0]);
            Box("wrist", "J4", 160, 60, 60, 50, 65, 0);
            Axis("J5", "J4", "X", 360, "rotary", [130, 65, 0]);
            Box("hand", "J5", 40, 100, 60, 20, 0, 0);
            if (physics) {
                // arm5p: the fingers slide on their own axes so a physics run can close them on the workpiece.
                // F1 closes with negative values and F2 with positive ones (each travels along the hand's Y axis).
                Axis("F1", "J5", "Y", 40, minimum: -20, maximum: 20); Axis("F2", "J5", "Y", 40, minimum: -20, maximum: 20);
                Box("finger-1", "F1", 60, 15, 30, 70, 40, 0); Box("finger-2", "F2", 60, 15, 30, 70, -40, 0);
            } else {
                Box("finger-1", "J5", 60, 15, 30, 70, 40, 0); // 65 mm between fingers grips the 60 mm workpiece with clearance
                Box("finger-2", "J5", 60, 15, 30, 70, -40, 0);
            }
            // The workpiece has its own world-aligned X/Y/Z carrier axes (0 = resting at its table spot),
            // so a chart can carry it with the hand without re-parenting bodies.
            if (physics) {
                // arm5p: a free body. World X/Y/Z translation, then yaw (Z), pitch (Y) and roll (X) about the workpiece centre,
                // so a physics run can place it anywhere, including tilted or fallen over (0 = resting on the table).
                Axis("WX", "", "X", 0, minimum: -1500, maximum: 1500);
                Axis("WY", "WX", "Y", 0, minimum: -1500, maximum: 1500);
                Axis("WZ", "WY", "Z", 0, minimum: -1000, maximum: 1000);
                Axis("WRZ", "WZ", "Z", 0, "rotary", [250, 0, 770], -720, 720); // yaw/roll accept more than one turn so tumbling stays continuous
                Axis("WRY", "WRZ", "Y", 0, "rotary", null, -180, 180);
                Axis("WRX", "WRY", "X", 0, "rotary", null, -720, 720);
                Box("workpiece", "WRX", 60, 60, 60, 0, 0, 0);
            } else {
                Axis("WX", "", "X", 0, minimum: -700, maximum: 200);
                Axis("WY", "WX", "Y", 0, minimum: -300, maximum: 300);
                Axis("WZ", "WY", "Z", 0, minimum: 0, maximum: 500);
                Box("workpiece", "WZ", 60, 60, 60, 250, 0, 770);
            }
        } else if (type == "gantry") {
            Box("base", "", w, d, 100, 0, 0, 50);
            // The bridge travels along the two Y rails; the trolley travels on the bridge.
            Axis("Y", "", "Y", yTravelMm); Axis("X", "Y", "X", xTravelMm); Axis("Z", "X", "Z", zTravelMm);
            foreach (int side in new[] { -1, 1 }) {
                string tag = side < 0 ? "left" : "right";
                double x = side * (w / 2 - 50);
                Box(tag + "-column-front", "", 100, 100, h, x, -d / 2 + 50, 100 + h / 2);
                Box(tag + "-column-back", "", 100, 100, h, x, d / 2 - 50, 100 + h / 2);
                Box(tag + "-y-rail", "", 100, d, 20, x, 0, h + 110);
                Box("bridge-" + tag + "-end", "Y", 100, 200, 100, x, 0, h + 170);
            }
            // Twin beams leave room for the vertical slide rather than intersecting it.
            Box("bridge", "Y", w - 200, 35, 100, 0, -82.5, h + 170);
            Box("bridge-back", "Y", w - 200, 35, 100, 0, 82.5, h + 170);
            Box("x-carriage", "X", 30, 140, 80, -65, 0, h + 260);
            Box("x-carriage-right", "X", 30, 140, 80, 65, 0, h + 260);
            // Four guide walls provide a 70 mm square opening around the 60 mm slide.
            double guideHeight = h - 300, guideZ = h + 300 - guideHeight / 2;
            Box("z-guide-left", "X", 15, 70, guideHeight, -42.5, 0, guideZ);
            Box("z-guide-right", "X", 15, 70, guideHeight, 42.5, 0, guideZ);
            Box("z-guide-front", "X", 100, 15, guideHeight, 0, -42.5, guideZ);
            Box("z-guide-back", "X", 100, 15, guideHeight, 0, 42.5, guideZ);
            double gripZ = 180 + zTravelMm / 2, slideBottom = gripZ + 30;
            double slideHeight = h + 300 + zTravelMm / 2 + 50 - slideBottom;
            Box("z-slide", "Z", 60, 60, slideHeight, 0, 0, slideBottom + slideHeight / 2);
            Box("gripper", "Z", 140, 80, 60, 0, 0, gripZ);
            Box("workpiece", "", 100, 100, 100, 0, 0, 150);
        } else {
            Axis("Y", "", "Y", yTravelMm); Axis("X", "Y", "X", xTravelMm); Axis("Z", "", "Z", zTravelMm);
            // The column stands behind the saddle's full Y travel (saddle is the deepest Y-moving body),
            // and the base extends to carry it.
            const double saddleDepth = 260, columnDepth = 180, columnClearance = 20;
            double columnFront = saddleDepth / 2 + yTravelMm / 2 + columnClearance, baseFront = -d / 2, baseBack = columnFront + columnDepth;
            Box("base", "", w, baseBack - baseFront, 100, 0, (baseFront + baseBack) / 2, 50);
            Box("column", "", 200, columnDepth, h, 0, columnFront + columnDepth / 2, 100+h/2);
            Box("saddle", "Y", 320, saddleDepth, 80, 0, 0, 140);
            Box("table", "X", xTravelMm+200, 220, 60, 0, 0, 210);
            Box("head", "Z", 180, 180, 220, 0, 0, h-50);
            Cylinder("spindle", "Z", 80, 100, 0, 0, h-210);
            Cylinder("tool", "Z", 20, 100, 0, 0, h-310);
            string workParent = "X"; double workZ = 290;
            if (type is "mill4" or "mill5") {
                if (type == "mill5") {
                    Axis("A", "X", "X", 220, "rotary", [0, 0, 350]);
                    // The trunnion top meets the rotary table bottom instead of overlapping it.
                    Box("trunnion", "A", 280, 200, 60, 0, 0, -60);
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
