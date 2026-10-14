namespace MechCue;

/// <summary>Adds axes and bodies to a concept machine being built by a template.</summary>
public sealed class ConceptBuilder(ConceptMachine model)
{
    public void Axis(string id, string parent, string direction, double travel, string kind = "linear", double[]? origin = null, double? minimum = null, double? maximum = null)
    {
        model.Axes.Add(new() { Id = id, Parent = parent, Direction = direction, Kind = kind, Minimum = minimum ?? -travel / 2, Maximum = maximum ?? travel / 2, OriginMm = origin ?? [0, 0, 0] });
        model.Values[id] = 0;
    }
    public void Box(string id, string parent, double x, double y, double z, double cx, double cy, double cz)
        => model.Bodies.Add(new() { Id = id, Parent = parent, SizeMm = [x, y, z], CenterMm = [cx, cy, cz] });
    public void Cylinder(string id, string parent, double diameter, double depth, double cx, double cy, double cz)
        => model.Bodies.Add(new() { Id = id, Parent = parent, Shape = "cylinder", SizeMm = [diameter, diameter, depth], CenterMm = [cx, cy, cz] });
}

/// <summary>
/// The concept machine types. Each template builds one type from the X/Y/Z travels (the arms ignore them);
/// a new machine type is a new entry here.
/// </summary>
public static class ConceptTemplates
{
    public delegate void Template(ConceptBuilder b, double xTravelMm, double yTravelMm, double zTravelMm);

    /// <summary>One machine type: how to build it, and how solidedge_list_concept_templates describes it.</summary>
    public sealed record Entry(string Type, Template Build, string Description, string Axes, string Purpose);

    /// <summary>Types in the order they are listed to users.</summary>
    public static readonly IReadOnlyList<Entry> All =
    [
        new("mill3", (b, x, y, z) => Mill(b, x, y, z, rotaryAxes: 0),
            "Vertical mill: table X on saddle Y; spindle Z",
            "XYZ",
            "Layout, travel and motion concept"),
        new("mill4", (b, x, y, z) => Mill(b, x, y, z, rotaryAxes: 1),
            "Vertical mill with table rotation C about local Z",
            "XYZC",
            "Indexing and workpiece orientation"),
        new("mill5", (b, x, y, z) => Mill(b, x, y, z, rotaryAxes: 2),
            "Vertical mill with trunnion A about local X and child table C about local Z",
            "XYZAC",
            "Table-table five-axis motion concept; no TCP or inverse kinematics"),
        new("gantry", Gantry,
            "Cartesian handling: Y bridge, child X carriage, child Z and simplified gripper",
            "XYZ",
            "Handling layout and travel; gripper has no open/close joint"),
        new("arm5", (b, _, _, _) => Arm5(b),
            "Table-mounted five-axis arm: J1 turret about Z, J2 shoulder, J3 elbow and J4 wrist pitch about local Y, J5 wrist roll about local X, two-finger hand and a workpiece on the table",
            "J1-J5",
            "Reach, pick-and-place posture and motion concept; travel arguments do not apply, fingers have no open/close joint; the workpiece moves on its own world X/Y/Z carrier axes WX/WY/WZ (0 = resting on the table), so a chart carries it by keyframing those axes along the hand; no inverse kinematics"),
        new("arm5p", (b, _, _, _) => Arm5p(b),
            "arm5 prepared for physics runs: adds finger slide axes F1/F2 (each along the hand Y axis; F1 closes with negative values, F2 with positive) and gives the workpiece a free-body chain WX, WY, WZ then yaw WRZ, pitch WRY, roll WRX (degrees, about the workpiece centre)",
            "J1-J5, F1, F2, WX, WY, WZ, WRZ, WRY, WRX",
            "Pick-and-place with friction grip, lift, release, drop and tipping computed by solidedge_simulate_concept_rigid_body; no inverse kinematics"),
    ];

    public static Template? Find(string? type) => All.FirstOrDefault(t => t.Type == type)?.Build;

    /// <summary>Machining centre: Y saddle carrying the X table, Z head; mill4 adds a C table, mill5 an A trunnion under it.</summary>
    static void Mill(ConceptBuilder b, double xTravelMm, double yTravelMm, double zTravelMm, int rotaryAxes)
    {
        double w = xTravelMm + 400, d = yTravelMm + 400, h = zTravelMm + 600;
        b.Axis("Y", "", "Y", yTravelMm); b.Axis("X", "Y", "X", xTravelMm); b.Axis("Z", "", "Z", zTravelMm);
        // The column stands behind the saddle's full Y travel (saddle is the deepest Y-moving body),
        // and the base extends to carry it.
        const double saddleDepth = 260, columnDepth = 180, columnClearance = 20;
        double columnFront = saddleDepth / 2 + yTravelMm / 2 + columnClearance, baseFront = -d / 2, baseBack = columnFront + columnDepth;
        b.Box("base", "", w, baseBack - baseFront, 100, 0, (baseFront + baseBack) / 2, 50);
        b.Box("column", "", 200, columnDepth, h, 0, columnFront + columnDepth / 2, 100 + h / 2);
        b.Box("saddle", "Y", 320, saddleDepth, 80, 0, 0, 140);
        b.Box("table", "X", xTravelMm + 200, 220, 60, 0, 0, 210);
        b.Box("head", "Z", 180, 180, 220, 0, 0, h - 50);
        b.Cylinder("spindle", "Z", 80, 100, 0, 0, h - 210);
        b.Cylinder("tool", "Z", 20, 100, 0, 0, h - 310);
        string workParent = "X"; double workZ = 290;
        if (rotaryAxes > 0) {
            if (rotaryAxes == 2) {
                b.Axis("A", "X", "X", 220, "rotary", [0, 0, 350]);
                // The trunnion top meets the rotary table bottom instead of overlapping it.
                b.Box("trunnion", "A", 280, 200, 60, 0, 0, -60);
                b.Axis("C", "A", "Z", 720, "rotary");
            } else b.Axis("C", "X", "Z", 720, "rotary", [0, 0, 290]);
            b.Cylinder("rotary-table", "C", 200, 60, 0, 0, 0);
            workParent = "C"; workZ = 80;
        }
        b.Box("workpiece", workParent, 100, 100, 100, 0, 0, workZ);
    }

    static void Gantry(ConceptBuilder b, double xTravelMm, double yTravelMm, double zTravelMm)
    {
        double w = xTravelMm + 400, d = yTravelMm + 400, h = zTravelMm + 600;
        b.Box("base", "", w, d, 100, 0, 0, 50);
        // The bridge travels along the two Y rails; the trolley travels on the bridge.
        b.Axis("Y", "", "Y", yTravelMm); b.Axis("X", "Y", "X", xTravelMm); b.Axis("Z", "X", "Z", zTravelMm);
        foreach (int side in new[] { -1, 1 }) {
            string tag = side < 0 ? "left" : "right";
            double x = side * (w / 2 - 50);
            b.Box(tag + "-column-front", "", 100, 100, h, x, -d / 2 + 50, 100 + h / 2);
            b.Box(tag + "-column-back", "", 100, 100, h, x, d / 2 - 50, 100 + h / 2);
            b.Box(tag + "-y-rail", "", 100, d, 20, x, 0, h + 110);
            b.Box("bridge-" + tag + "-end", "Y", 100, 200, 100, x, 0, h + 170);
        }
        // Twin beams leave room for the vertical slide rather than intersecting it.
        b.Box("bridge", "Y", w - 200, 35, 100, 0, -82.5, h + 170);
        b.Box("bridge-back", "Y", w - 200, 35, 100, 0, 82.5, h + 170);
        b.Box("x-carriage", "X", 30, 140, 80, -65, 0, h + 260);
        b.Box("x-carriage-right", "X", 30, 140, 80, 65, 0, h + 260);
        // Four guide walls provide a 70 mm square opening around the 60 mm slide.
        double guideHeight = h - 300, guideZ = h + 300 - guideHeight / 2;
        b.Box("z-guide-left", "X", 15, 70, guideHeight, -42.5, 0, guideZ);
        b.Box("z-guide-right", "X", 15, 70, guideHeight, 42.5, 0, guideZ);
        b.Box("z-guide-front", "X", 100, 15, guideHeight, 0, -42.5, guideZ);
        b.Box("z-guide-back", "X", 100, 15, guideHeight, 0, 42.5, guideZ);
        double gripZ = 180 + zTravelMm / 2, slideBottom = gripZ + 30;
        double slideHeight = h + 300 + zTravelMm / 2 + 50 - slideBottom;
        b.Box("z-slide", "Z", 60, 60, slideHeight, 0, 0, slideBottom + slideHeight / 2);
        b.Box("gripper", "Z", 140, 80, 60, 0, 0, gripZ);
        b.Box("workpiece", "", 100, 100, 100, 0, 0, 150);
    }

    /// <summary>
    /// Table-mounted five-axis arm up to the hand (travel arguments do not apply). Joint frames follow the parent
    /// link: J1 turret about Z, J2 shoulder / J3 elbow / J4 wrist pitch about Y, J5 wrist roll about X.
    /// Adjacent links sit side by side in Y, so pitch rotations never sweep a link through its neighbour.
    /// </summary>
    static void ArmToHand(ConceptBuilder b)
    {
        b.Box("base", "", 200, 200, 100, -250, 0, 790);
        b.Box("table-top", "", 1000, 700, 40, 0, 0, 720);
        foreach (var (tag, x, y) in new[] { ("front-left", -440, -290), ("front-right", 440, -290), ("back-left", -440, 290), ("back-right", 440, 290) })
            b.Box("table-leg-" + tag, "", 60, 60, 700, x, y, 350);
        b.Axis("J1", "", "Z", 340, "rotary", [-250, 0, 840]);
        b.Cylinder("turret", "J1", 180, 80, 0, 0, 40);
        b.Box("shoulder", "J1", 120, 190, 60, 0, 0, 110);
        b.Axis("J2", "J1", "Y", 180, "rotary", [0, 0, 110]);
        b.Box("upper-arm", "J2", 80, 80, 400, 0, 135, 160);
        b.Axis("J3", "J2", "Y", 270, "rotary", [0, 135, 320]);
        b.Box("forearm", "J3", 360, 70, 70, 140, 75, 0);
        b.Axis("J4", "J3", "Y", 240, "rotary", [300, 75, 0]);
        b.Box("wrist", "J4", 160, 60, 60, 50, 65, 0);
        b.Axis("J5", "J4", "X", 360, "rotary", [130, 65, 0]);
        b.Box("hand", "J5", 40, 100, 60, 20, 0, 0);
    }

    /// <summary>arm5: fixed fingers, and a workpiece on world-aligned X/Y/Z carrier axes (0 = resting at its table spot),
    /// so a chart can carry it with the hand without re-parenting bodies.</summary>
    static void Arm5(ConceptBuilder b)
    {
        ArmToHand(b);
        b.Box("finger-1", "J5", 60, 15, 30, 70, 40, 0); // 65 mm between fingers grips the 60 mm workpiece with clearance
        b.Box("finger-2", "J5", 60, 15, 30, 70, -40, 0);
        b.Axis("WX", "", "X", 0, minimum: -700, maximum: 200);
        b.Axis("WY", "WX", "Y", 0, minimum: -300, maximum: 300);
        b.Axis("WZ", "WY", "Z", 0, minimum: 0, maximum: 500);
        b.Box("workpiece", "WZ", 60, 60, 60, 250, 0, 770);
    }

    /// <summary>arm5p: arm5 for rigid-body physics. The fingers slide so a physics run can close them on the workpiece,
    /// and the workpiece is a free body whose axes are physics tracks.</summary>
    static void Arm5p(ConceptBuilder b)
    {
        ArmToHand(b);
        // F1 closes with negative values and F2 with positive ones (each travels along the hand's Y axis).
        b.Axis("F1", "J5", "Y", 40, minimum: -20, maximum: 20); b.Axis("F2", "J5", "Y", 40, minimum: -20, maximum: 20);
        b.Box("finger-1", "F1", 60, 15, 30, 70, 40, 0); b.Box("finger-2", "F2", 60, 15, 30, 70, -40, 0);
        // World X/Y/Z translation, then yaw (Z), pitch (Y) and roll (X) about the workpiece centre,
        // so a physics run can place it anywhere, including tilted or fallen over (0 = resting on the table).
        b.Axis("WX", "", "X", 0, minimum: -1500, maximum: 1500);
        b.Axis("WY", "WX", "Y", 0, minimum: -1500, maximum: 1500);
        b.Axis("WZ", "WY", "Z", 0, minimum: -1000, maximum: 1000);
        b.Axis("WRZ", "WZ", "Z", 0, "rotary", [250, 0, 770], -720, 720); // yaw/roll accept more than one turn so tumbling stays continuous
        b.Axis("WRY", "WRZ", "Y", 0, "rotary", null, -180, 180);
        b.Axis("WRX", "WRY", "X", 0, "rotary", null, -720, 720);
        b.Box("workpiece", "WRX", 60, 60, 60, 0, 0, 0);
    }
}
