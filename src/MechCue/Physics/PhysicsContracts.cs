namespace MechCue.Physics;

// Data exchanged with the physics calculator (MechCue.Physics.exe). No engine code here: the add-in and the
// standalone app only prepare inputs and read results; the engine runs in a separate process.

public sealed class RigidBodySetup
{
    public string Workpiece { get; set; } = "workpiece";
    /// <summary>World direction of gravity (any non-zero vector; normalised).</summary>
    public double[] Gravity { get; set; } = [0, 0, -1];
    public double GravityMps2 { get; set; } = 9.81;
    public double WorkpieceMassKg { get; set; } = 0.5;
    public double WorkpieceFriction { get; set; } = 0.5;
    public double Restitution { get; set; }
    /// <summary>Bodies moved by the arm axes (hand, fingers, links) use this friction unless overridden in <see cref="Friction"/>.</summary>
    public double ArmFriction { get; set; } = 0.8;
    /// <summary>Fixed bodies (table, base) use this friction unless overridden in <see cref="Friction"/>.</summary>
    public double SupportFriction { get; set; } = 0.6;
    /// <summary>Friction per body id; overrides the defaults above.</summary>
    public Dictionary<string, double> Friction { get; set; } = [];
    /// <summary>Optional floor: a large static slab whose top is at this world height (mm). Null = no floor, things fall forever.</summary>
    public double? FloorZMm { get; set; }
    public double EndTime { get; set; }
    /// <summary>Spacing of the produced keyframes.</summary>
    public double OutputStepSeconds { get; set; } = 0.02;
    /// <summary>Physics steps per second (rounded up to a whole number of steps per output step).</summary>
    public double PhysicsHz { get; set; } = 240;
    /// <summary>The workpiece first rests for this long (with the arm held at t=0) so it settles before the chart starts.</summary>
    public double PreSettleSeconds { get; set; } = 0.3;
}

public sealed class RigidBodyResult
{
    public Dictionary<string, List<JointPoint>> Axes { get; set; } = [];
    public List<PhysicsEvent> Events { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
    public string Engine { get; set; } = "";
    public string FinalState { get; set; } = "";
    public double EndTime { get; set; }
    public int Samples { get; set; }
    public double MaxTiltDegrees { get; set; }
}
