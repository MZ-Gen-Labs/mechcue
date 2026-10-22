namespace MechCue;

/// <summary>A keyframe of an axis passed to or returned from the physics calculator (seconds; mm or degrees).</summary>
public sealed class JointPoint { public double Time { get; set; } public double Value { get; set; } }
/// <summary>Something that happened during a physics run (lift-off, settled, tipped).</summary>
public sealed class PhysicsEvent { public double Time { get; init; } public string Type { get; init; } = ""; public string Detail { get; init; } = ""; }
