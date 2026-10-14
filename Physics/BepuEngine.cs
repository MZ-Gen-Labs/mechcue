using System.Numerics;
using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.CollisionDetection;
using BepuPhysics.Constraints;
using BepuUtilities;
using BepuUtilities.Memory;
using BepuPose = BepuPhysics.RigidPose;

namespace MechCue.Physics;

/// <summary>
/// <see cref="IRigidBodyEngine"/> on BepuPhysics v2 (Apache-2.0, pure C#). Use 2.5.0-beta or newer: in 2.4.0 the effective friction came out at a quarter of the coefficient. Friction is a Coulomb coefficient per
/// material pair (geometric mean). Bepu has no restitution coefficient; it is approximated by the contact
/// spring's damping ratio, so bounces are weak and only qualitative.
/// </summary>
public sealed class BepuEngine : IRigidBodyEngine
{
    sealed class Gravity { public Vector3 Value; }
    sealed class Materials
    {
        public readonly List<PhysicsMaterial> Bodies = [], Statics = [];
        public PhysicsMaterial Of(CollidableReference c) => c.Mobility == CollidableMobility.Static ? Statics[c.StaticHandle.Value] : Bodies[c.BodyHandle.Value];
    }
    struct NarrowPhase(Materials materials) : INarrowPhaseCallbacks
    {
        public void Initialize(Simulation simulation) { }
        public bool AllowContactGeneration(int workerIndex, CollidableReference a, CollidableReference b, ref float speculativeMargin) => a.Mobility == CollidableMobility.Dynamic || b.Mobility == CollidableMobility.Dynamic;
        public bool AllowContactGeneration(int workerIndex, CollidablePair pair, int childIndexA, int childIndexB) => true;
        public bool ConfigureContactManifold<TManifold>(int workerIndex, CollidablePair pair, ref TManifold manifold, out PairMaterialProperties pairMaterial) where TManifold : unmanaged, IContactManifold<TManifold>
        {
            var a = materials.Of(pair.A); var b = materials.Of(pair.B);
            double e = Math.Clamp(Math.Max(a.Restitution, b.Restitution), 0, 0.95), ln = e > 0 ? Math.Log(e) : 0;
            double damping = e > 0 ? -ln / Math.Sqrt(Math.PI * Math.PI + ln * ln) : 1;
            pairMaterial.FrictionCoefficient = (float)Math.Sqrt(a.Friction * b.Friction);
            pairMaterial.MaximumRecoveryVelocity = 2f;
            pairMaterial.SpringSettings = new SpringSettings(30, (float)Math.Clamp(damping, 0.05, 10));
            return true;
        }
        public bool ConfigureContactManifold(int workerIndex, CollidablePair pair, int childIndexA, int childIndexB, ref ConvexContactManifold manifold) => true;
        public void Dispose() { }
    }
    struct PoseIntegrator(Gravity gravity) : IPoseIntegratorCallbacks
    {
        Vector3Wide gravityWide;
        public readonly AngularIntegrationMode AngularIntegrationMode => AngularIntegrationMode.Nonconserving;
        public readonly bool AllowSubstepsForUnconstrainedBodies => false;
        public readonly bool IntegrateVelocityForKinematics => false;
        public void Initialize(Simulation simulation) { }
        public void PrepareForIntegration(float dt) => gravityWide = Vector3Wide.Broadcast(gravity.Value);
        public void IntegrateVelocity(Vector<int> bodyIndices, Vector3Wide position, QuaternionWide orientation, BodyInertiaWide localInertia, Vector<int> integrationMask, int workerIndex, Vector<float> dt, ref BodyVelocityWide velocity)
            => velocity.Linear += gravityWide * dt;
    }
    sealed record Entry(int Handle, bool IsStatic, Quaternion ShapeRotation, bool Kinematic);

    readonly BufferPool pool = new();
    readonly Gravity gravity = new();
    readonly Materials materials = new();
    readonly Simulation simulation;
    readonly Dictionary<string, Entry> entries = [];
    readonly Dictionary<string, RigidPose> pending = [];
    static readonly Quaternion CylinderToZ = Quaternion.CreateFromAxisAngle(Vector3.UnitX, (float)(Math.PI / 2)); // Bepu cylinders run along local Y

    public string Name => "BepuPhysics 2";
    public BepuEngine() { simulation = Simulation.Create(pool, new NarrowPhase(materials), new PoseIntegrator(gravity), new SolveDescription(8, 4)); }
    public void SetGravity(double x, double y, double z) => gravity.Value = new((float)x, (float)y, (float)z);

    static Quaternion Q(RigidPose p) => new((float)p.Qx, (float)p.Qy, (float)p.Qz, (float)p.Qw);
    TypedIndex AddShape(PhysicsShape s, out Quaternion shapeRotation, out Func<float, BodyInertia> inertia)
    {
        if (s.Kind == PhysicsShapeKind.Box) { var b = new Box((float)s.SizeX, (float)s.SizeY, (float)s.SizeZ); shapeRotation = Quaternion.Identity; inertia = m => b.ComputeInertia(m); return simulation.Shapes.Add(b); }
        var c = new Cylinder((float)(s.SizeX / 2), (float)s.SizeZ); shapeRotation = CylinderToZ; inertia = m => c.ComputeInertia(m); return simulation.Shapes.Add(c);
    }
    static BepuPose ToBepu(RigidPose p, Quaternion shapeRotation) => new(new Vector3((float)p.X, (float)p.Y, (float)p.Z), Quaternion.Normalize(Quaternion.Concatenate(shapeRotation, Q(p))));
    static void Put(List<PhysicsMaterial> list, int index, PhysicsMaterial m) { while (list.Count <= index) list.Add(m); list[index] = m; }
    void Check(string id) { if (entries.ContainsKey(id)) throw new ArgumentException("Duplicate body id: " + id); }

    public void AddStatic(string id, PhysicsShape shape, RigidPose pose, PhysicsMaterial material)
    {
        Check(id); var index = AddShape(shape, out var rot, out _); var p = ToBepu(pose, rot);
        var handle = simulation.Statics.Add(new StaticDescription(p.Position, p.Orientation, index));
        Put(materials.Statics, handle.Value, material); entries[id] = new(handle.Value, true, rot, false);
    }
    public void AddKinematic(string id, PhysicsShape shape, RigidPose pose, PhysicsMaterial material)
    {
        Check(id); var index = AddShape(shape, out var rot, out _);
        var handle = simulation.Bodies.Add(BodyDescription.CreateKinematic(ToBepu(pose, rot), new CollidableDescription(index, 0.05f), new BodyActivityDescription(-1)));
        Put(materials.Bodies, handle.Value, material); entries[id] = new(handle.Value, false, rot, true);
    }
    public void AddDynamic(string id, PhysicsShape shape, RigidPose pose, PhysicsMaterial material, double massKg)
    {
        Check(id); if (!(massKg > 0)) throw new ArgumentException("Dynamic bodies need a positive mass.");
        var index = AddShape(shape, out var rot, out var inertia);
        var handle = simulation.Bodies.Add(BodyDescription.CreateDynamic(ToBepu(pose, rot), inertia((float)massKg), new CollidableDescription(index, 0.05f), new BodyActivityDescription(-1)));
        Put(materials.Bodies, handle.Value, material); entries[id] = new(handle.Value, false, rot, false);
    }
    public void SetKinematicTarget(string id, RigidPose pose)
    {
        if (!entries[id].Kinematic) throw new ArgumentException(id + " is not kinematic.");
        pending[id] = pose;
    }

    public void Step(double dt)
    {
        foreach (var (id, target) in pending) {
            var e = entries[id]; var body = simulation.Bodies.GetBodyReference(new BodyHandle(e.Handle)); var goal = ToBepu(target, e.ShapeRotation);
            body.Velocity.Linear = (goal.Position - body.Pose.Position) / (float)dt;
            // World-frame rotation taking the current orientation to the goal, spread over the step.
            var dq = Quaternion.Normalize(goal.Orientation * Quaternion.Inverse(body.Pose.Orientation));
            if (dq.W < 0) dq = new(-dq.X, -dq.Y, -dq.Z, -dq.W);
            double angle = 2 * Math.Acos(Math.Clamp(dq.W, -1, 1)), s = Math.Sqrt(Math.Max(0, 1 - dq.W * dq.W));
            body.Velocity.Angular = s < 1e-6 ? Vector3.Zero : new Vector3(dq.X, dq.Y, dq.Z) / (float)s * (float)(angle / dt);
        }
        pending.Clear();
        simulation.Timestep((float)dt);
    }
    public RigidPose GetPose(string id)
    {
        var e = entries[id];
        return Back(e.IsStatic ? simulation.Statics[new StaticHandle(e.Handle)].Pose : simulation.Bodies.GetBodyReference(new BodyHandle(e.Handle)).Pose, e);
    }
    static RigidPose Back(BepuPose p, Entry e)
    {
        var q = Quaternion.Normalize(Quaternion.Concatenate(Quaternion.Inverse(e.ShapeRotation), p.Orientation));
        return new(p.Position.X, p.Position.Y, p.Position.Z, q.X, q.Y, q.Z, q.W);
    }
    public (double X, double Y, double Z) GetLinearVelocity(string id)
    {
        var v = simulation.Bodies.GetBodyReference(new BodyHandle(entries[id].Handle)).Velocity.Linear; return (v.X, v.Y, v.Z);
    }
    public void Dispose() { simulation.Dispose(); pool.Clear(); }
}
