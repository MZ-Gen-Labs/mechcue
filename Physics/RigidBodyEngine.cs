namespace MechCue.Physics;

/// <summary>Rigid transform in metres (right-handed, Z up as in the concept models). Rotation is a unit quaternion (x,y,z,w).</summary>
public readonly record struct RigidPose(double X, double Y, double Z, double Qx, double Qy, double Qz, double Qw)
{
    public static readonly RigidPose Identity = new(0, 0, 0, 0, 0, 0, 1);
    /// <summary>From a MechCue 4x4 column-major rigid matrix (metres).</summary>
    public static RigidPose FromMatrix(double[] m)
    {
        double trace = m[0] + m[5] + m[10], qx, qy, qz, qw;
        // m[col*4+row]; R(row,col) = m[col*4+row]
        double R(int r, int c) => m[c * 4 + r];
        if (trace > 0) { double s = Math.Sqrt(trace + 1) * 2; qw = s / 4; qx = (R(2, 1) - R(1, 2)) / s; qy = (R(0, 2) - R(2, 0)) / s; qz = (R(1, 0) - R(0, 1)) / s; }
        else if (R(0, 0) > R(1, 1) && R(0, 0) > R(2, 2)) { double s = Math.Sqrt(1 + R(0, 0) - R(1, 1) - R(2, 2)) * 2; qw = (R(2, 1) - R(1, 2)) / s; qx = s / 4; qy = (R(0, 1) + R(1, 0)) / s; qz = (R(0, 2) + R(2, 0)) / s; }
        else if (R(1, 1) > R(2, 2)) { double s = Math.Sqrt(1 + R(1, 1) - R(0, 0) - R(2, 2)) * 2; qw = (R(0, 2) - R(2, 0)) / s; qx = (R(0, 1) + R(1, 0)) / s; qy = s / 4; qz = (R(1, 2) + R(2, 1)) / s; }
        else { double s = Math.Sqrt(1 + R(2, 2) - R(0, 0) - R(1, 1)) * 2; qw = (R(1, 0) - R(0, 1)) / s; qx = (R(0, 2) + R(2, 0)) / s; qy = (R(1, 2) + R(2, 1)) / s; qz = s / 4; }
        double n = Math.Sqrt(qx * qx + qy * qy + qz * qz + qw * qw);
        return new(m[12], m[13], m[14], qx / n, qy / n, qz / n, qw / n);
    }
    public double[] ToMatrix()
    {
        double x = Qx, y = Qy, z = Qz, w = Qw;
        return [1 - 2 * (y * y + z * z), 2 * (x * y + z * w), 2 * (x * z - y * w), 0,
                2 * (x * y - z * w), 1 - 2 * (x * x + z * z), 2 * (y * z + x * w), 0,
                2 * (x * z + y * w), 2 * (y * z - x * w), 1 - 2 * (x * x + y * y), 0, X, Y, Z, 1];
    }
}

public enum PhysicsShapeKind { Box, CylinderZ, ConvexHull }
/// <summary>Collision shape in the body's local frame (metres). Box: full extents x,y,z. CylinderZ: diameter in x,y, height in z.</summary>
public sealed record PhysicsShape(PhysicsShapeKind Kind, double SizeX, double SizeY, double SizeZ, double[][]? Vertices = null);
/// <summary>Surface material. Restitution (0..1) is the ratio of separating to approaching speed in an impact.</summary>
public sealed record PhysicsMaterial(double Friction = 0.5, double Restitution = 0);

public readonly record struct BodyContact(string Other, double Depth);

/// <summary>
/// Everything the simulator needs from a rigid-body engine. Implement this to swap the engine
/// (BepuPhysics today; Bullet, MuJoCo, ... can be added without touching scenario code).
/// </summary>
public interface IRigidBodyEngine : IDisposable
{
    string Name { get; }
    void SetGravity(double x, double y, double z);
    /// <summary>Contact stiffness as a spring frequency (Hz; higher = less sinking, needs a finer time step), the fastest speed (m/s) at which overlapping bodies
    /// are pushed apart, and a skin (metres): bodies rest this far apart, so a spring's sag does not make the CAD parts overlap.</summary>
    void SetContact(double frequencyHz, double maximumRecoveryVelocity, double skin = 0);
    void AddStatic(string id, PhysicsShape shape, RigidPose pose, PhysicsMaterial material);
    /// <summary>A body moved by prescribed poses (arm links, fingers); it pushes dynamic bodies but is never pushed.</summary>
    void AddKinematic(string id, PhysicsShape shape, RigidPose pose, PhysicsMaterial material);
    void AddDynamic(string id, PhysicsShape shape, RigidPose pose, PhysicsMaterial material, double massKg);
    /// <summary>
    /// A body that slides along one world axis of a moving carrier body and is pushed toward a target travel with at most
    /// <paramref name="maxForceN"/> (a force-limited gripper finger). Its rotation and the other two directions follow the carrier rigidly,
    /// it does not collide with the carrier, and gravity does not act on it (the arm carries its weight).
    /// </summary>
    void AddSlider(string id, PhysicsShape shape, RigidPose pose, PhysicsMaterial material, double massKg, string carrierId, (double X, double Y, double Z) axis, double maxForceN);
    /// <summary>Target travel (metres along the slider axis) measured from where the slider was added.</summary>
    void SetSliderTarget(string id, double travel);
    /// <summary>Pose the kinematic body must have after the next <see cref="Step"/>.</summary>
    void SetKinematicTarget(string id, RigidPose pose);
    void Step(double dt);
    RigidPose GetPose(string id);
    (double X, double Y, double Z) GetLinearVelocity(string id);
}
