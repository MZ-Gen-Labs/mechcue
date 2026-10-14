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
    /// <summary>Workpiece restitution 0..1: separating / approaching speed in an impact (impacts slower than 0.1 m/s do not bounce).</summary>
    public double Restitution { get; set; }
    /// <summary>Contact stiffness as a spring frequency (Hz). A resting body sinks about g/(2π·f)²: 0.28 mm at 30 Hz, 0.02 mm at 120 Hz.
    /// At most twice <see cref="PhysicsHz"/> (the solver runs four substeps per physics step).</summary>
    public double ContactHz { get; set; } = 120;
    /// <summary>Contact skin (mm): bodies come to rest this far apart, so the contact spring's sag (about 0.02 mm at 120 Hz) does not make
    /// the CAD parts overlap (Solid Edge reports any overlap as interference). 0..5 mm; keep it above the sag.</summary>
    public double ContactSkinMm { get; set; } = 0.05;
    /// <summary>Close the fingers with a limited force (N per finger) instead of moving them exactly to the chart values. Null = position-driven
    /// (the squeeze then comes from how far the chart closes the fingers into the workpiece). With a force, the finger axes' values are targets:
    /// a finger stops at the workpiece and presses with at most this force.</summary>
    public double? GripForceN { get; set; }
    /// <summary>Axes that carry gripper fingers (used with <see cref="GripForceN"/>).</summary>
    public List<string> GripAxes { get; set; } = ["F1", "F2"];
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
