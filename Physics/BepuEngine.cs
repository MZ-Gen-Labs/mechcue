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
/// <see cref="IRigidBodyEngine"/> on BepuPhysics v2 (Apache-2.0, pure C#). Use 2.5.0-beta or newer: 2.4.0 limits the friction
/// of a multi-point contact by one point's share of the normal force (a box on its face gets a quarter of its friction).
/// Friction is a Coulomb coefficient per material pair (geometric mean). Contacts are springs (<see cref="SetContact"/>) with a skin:
/// every contact is reported deeper by the skin, so bodies come to rest that far apart and a spring's sag stays inside it;
/// Bepu has no restitution, so impacts are made to bounce here: after a step in which a contact stopped a body that
/// approached faster than <see cref="BounceThreshold"/>, an impulse at the contact gives it e times the approach speed.
/// </summary>
public sealed class BepuEngine : IRigidBodyEngine
{
    /// <summary>Approach speeds (m/s) below this do not bounce, so resting and sliding contacts stay calm.</summary>
    public const double BounceThreshold = 0.1;

    sealed class Gravity { public Vector3 Value; }
    /// <summary>A contact on a bouncy dynamic body, recorded by the narrow phase at the start of a step (world frame; normal points into the body).</summary>
    record struct Impact(int Body, CollidableReference Other, Vector3 Point, Vector3 Normal);
    sealed class Materials
    {
        public readonly List<PhysicsMaterial> Bodies = [], Statics = [];
        /// <summary>Contact spring frequency (Hz) and the fastest speed (m/s) at which overlapping bodies are pushed apart.</summary>
        public float ContactHz = 120, MaximumRecoveryVelocity = 2, Skin;
        /// <summary>Body pairs that never collide (a slider and its carrier).</summary>
        public readonly HashSet<(int, int)> NoContact = [];
        public readonly List<Impact> Impacts = [];
        public Simulation? Simulation;
        public PhysicsMaterial Of(CollidableReference c) => c.Mobility == CollidableMobility.Static ? Statics[c.StaticHandle.Value] : Bodies[c.BodyHandle.Value];
        public bool Bouncy(CollidableReference c) => c.Mobility == CollidableMobility.Dynamic && Bodies[c.BodyHandle.Value].Restitution > 0;
    }
    struct NarrowPhase(Materials materials) : INarrowPhaseCallbacks
    {
        public void Initialize(Simulation simulation) => materials.Simulation = simulation;
        public bool AllowContactGeneration(int workerIndex, CollidableReference a, CollidableReference b, ref float speculativeMargin)
        {
            if (a.Mobility != CollidableMobility.Dynamic && b.Mobility != CollidableMobility.Dynamic) return false;
            if (a.Mobility == CollidableMobility.Static || b.Mobility == CollidableMobility.Static) return true;
            int x = a.BodyHandle.Value, y = b.BodyHandle.Value;
            return !materials.NoContact.Contains((Math.Min(x, y), Math.Max(x, y)));
        }
        public bool AllowContactGeneration(int workerIndex, CollidablePair pair, int childIndexA, int childIndexB) => true;
        public bool ConfigureContactManifold<TManifold>(int workerIndex, CollidablePair pair, ref TManifold manifold, out PairMaterialProperties pairMaterial) where TManifold : unmanaged, IContactManifold<TManifold>
        {
            var a = materials.Of(pair.A); var b = materials.Of(pair.B);
            pairMaterial.FrictionCoefficient = (float)Math.Sqrt(a.Friction * b.Friction);
            pairMaterial.MaximumRecoveryVelocity = materials.MaximumRecoveryVelocity;
            pairMaterial.SpringSettings = new SpringSettings(materials.ContactHz, 1);
            // The skin works on any shape (boxes, cylinders, hulls, meshes): it shifts the contact, not the geometry.
            if (materials.Skin > 0) for (int i = 0; i < manifold.Count; i++) TManifold.GetDepthReference(ref manifold, i) += materials.Skin;
            if (manifold.Count > 0 && (materials.Bouncy(pair.A) || materials.Bouncy(pair.B))) {
                // Contact offsets are from A's position and the normal points from B to A.
                var simulation = materials.Simulation!;
                var positionA = pair.A.Mobility == CollidableMobility.Static ? simulation.Statics[pair.A.StaticHandle].Pose.Position : simulation.Bodies.GetBodyReference(pair.A.BodyHandle).Pose.Position;
                Vector3 point = Vector3.Zero, normal = Vector3.Zero;
                for (int i = 0; i < manifold.Count; i++) { manifold.GetContact(i, out var offset, out var n, out _, out _); point += offset; normal = n; }
                point = positionA + point / manifold.Count;
                lock (materials.Impacts) {
                    if (materials.Bouncy(pair.A)) materials.Impacts.Add(new(pair.A.BodyHandle.Value, pair.B, point, normal));
                    if (materials.Bouncy(pair.B)) materials.Impacts.Add(new(pair.B.BodyHandle.Value, pair.A, point, -normal));
                }
            }
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
    sealed record Entry(int Handle, bool IsStatic, Quaternion ShapeRotation, bool Kinematic, Vector3 ShapeCenter);
    /// <summary>A force-limited slider: the drive constraint whose target is updated, and its description.</summary>
    sealed class Slider(ConstraintHandle drive, LinearAxisServo description) { public readonly ConstraintHandle Drive = drive; public LinearAxisServo Description = description; }

    readonly BufferPool pool = new();
    readonly Gravity gravity = new();
    readonly Materials materials = new();
    readonly Simulation simulation;
    readonly Dictionary<string, Entry> entries = [];
    readonly Dictionary<string, RigidPose> pending = [];
    readonly Dictionary<string, Slider> sliders = [];
    /// <summary>Approach speed of a bouncy body when a contact began to slow it (a speculative contact may stop it over two steps).</summary>
    readonly Dictionary<int, float> approaching = [];
    static readonly Quaternion CylinderToZ = Quaternion.CreateFromAxisAngle(Vector3.UnitX, (float)(Math.PI / 2)); // Bepu cylinders run along local Y

    public string Name => "BepuPhysics 2";
    public BepuEngine() { simulation = Simulation.Create(pool, new NarrowPhase(materials), new PoseIntegrator(gravity), new SolveDescription(8, 4)); }
    public void SetGravity(double x, double y, double z) => gravity.Value = new((float)x, (float)y, (float)z);
    public void SetContact(double frequencyHz, double maximumRecoveryVelocity, double skin = 0) { materials.ContactHz = (float)frequencyHz; materials.MaximumRecoveryVelocity = (float)maximumRecoveryVelocity; materials.Skin = (float)skin; }

    static Quaternion Q(RigidPose p) => new((float)p.Qx, (float)p.Qy, (float)p.Qz, (float)p.Qw);
    TypedIndex AddShape(PhysicsShape s, out Quaternion shapeRotation, out Func<float, BodyInertia> inertia, out Vector3 center)
    {
        center = Vector3.Zero;
        if (s.Kind == PhysicsShapeKind.ConvexHull) {
            ConvexGeometry.ValidateVertices(s.Vertices);
            var vertices = s.Vertices!.Select(p => new Vector3((float)p[0], (float)p[1], (float)p[2])).ToArray();
            var hull = new ConvexHull(vertices.AsSpan(), pool, out center);
            shapeRotation = Quaternion.Identity; inertia = m => hull.ComputeInertia(m); return simulation.Shapes.Add(hull);
        }
        if (s.Kind == PhysicsShapeKind.Box) { var b = new Box((float)s.SizeX, (float)s.SizeY, (float)s.SizeZ); shapeRotation = Quaternion.Identity; inertia = m => b.ComputeInertia(m); return simulation.Shapes.Add(b); }
        var c = new Cylinder((float)(s.SizeX / 2), (float)s.SizeZ); shapeRotation = CylinderToZ; inertia = m => c.ComputeInertia(m); return simulation.Shapes.Add(c);
    }
    static BepuPose ToBepu(RigidPose p, Quaternion shapeRotation, Vector3 center) => new(new Vector3((float)p.X, (float)p.Y, (float)p.Z) + Vector3.Transform(center, Q(p)), Quaternion.Normalize(Quaternion.Concatenate(shapeRotation, Q(p))));
    static void Put(List<PhysicsMaterial> list, int index, PhysicsMaterial m) { while (list.Count <= index) list.Add(m); list[index] = m; }
    void Check(string id) { if (entries.ContainsKey(id)) throw new ArgumentException("Duplicate body id: " + id); }
    BodyReference Body(string id) => simulation.Bodies.GetBodyReference(new BodyHandle(entries[id].Handle));

    public void AddStatic(string id, PhysicsShape shape, RigidPose pose, PhysicsMaterial material)
    {
        Check(id); var index = AddShape(shape, out var rot, out _, out var center); var p = ToBepu(pose, rot, center);
        var handle = simulation.Statics.Add(new StaticDescription(p.Position, p.Orientation, index));
        Put(materials.Statics, handle.Value, material); entries[id] = new(handle.Value, true, rot, false, center);
    }
    public void AddKinematic(string id, PhysicsShape shape, RigidPose pose, PhysicsMaterial material)
    {
        Check(id); var index = AddShape(shape, out var rot, out _, out var center);
        var handle = simulation.Bodies.Add(BodyDescription.CreateKinematic(ToBepu(pose, rot, center), new CollidableDescription(index, 0.05f), new BodyActivityDescription(-1)));
        Put(materials.Bodies, handle.Value, material); entries[id] = new(handle.Value, false, rot, true, center);
    }
    public void AddDynamic(string id, PhysicsShape shape, RigidPose pose, PhysicsMaterial material, double massKg)
    {
        Check(id); if (!(massKg > 0)) throw new ArgumentException("Dynamic bodies need a positive mass.");
        var index = AddShape(shape, out var rot, out var inertia, out var center);
        var handle = simulation.Bodies.Add(BodyDescription.CreateDynamic(ToBepu(pose, rot, center), inertia((float)massKg), new CollidableDescription(index, 0.05f), new BodyActivityDescription(-1)));
        Put(materials.Bodies, handle.Value, material); entries[id] = new(handle.Value, false, rot, false, center);
    }
    public void AddSlider(string id, PhysicsShape shape, RigidPose pose, PhysicsMaterial material, double massKg, string carrierId, (double X, double Y, double Z) axis, double maxForceN)
    {
        if (!(maxForceN > 0)) throw new ArgumentException("A slider needs a positive force limit.");
        if (!entries.TryGetValue(carrierId, out var carrier) || carrier.IsStatic) throw new ArgumentException("A slider's carrier must be a moving body: " + carrierId);
        AddDynamic(id, shape, pose, material, massKg);
        BodyReference a = Body(carrierId), b = Body(id);
        var toLocalA = Quaternion.Inverse(a.Pose.Orientation);
        var axisA = Vector3.Normalize(Vector3.Transform(new Vector3((float)axis.X, (float)axis.Y, (float)axis.Z), toLocalA));
        var anchorA = Vector3.Transform(b.Pose.Position - a.Pose.Position, toLocalA);
        // The two lock constraints are rigid (no force limit); only the drive along the axis is limited.
        var stiff = new SpringSettings(Math.Max(materials.ContactHz, 120), 1);
        simulation.Solver.Add(a.Handle, b.Handle, new PointOnLineServo { LocalOffsetA = anchorA, LocalOffsetB = Vector3.Zero, LocalDirection = axisA, ServoSettings = ServoSettings.Default, SpringSettings = stiff });
        simulation.Solver.Add(a.Handle, b.Handle, new AngularServo { TargetRelativeRotationLocalA = Quaternion.Concatenate(b.Pose.Orientation, toLocalA), ServoSettings = ServoSettings.Default, SpringSettings = stiff });
        var drive = new LinearAxisServo { LocalOffsetA = anchorA, LocalOffsetB = Vector3.Zero, LocalPlaneNormal = axisA, TargetOffset = 0, ServoSettings = new ServoSettings(10, 0, (float)maxForceN), SpringSettings = stiff };
        sliders[id] = new(simulation.Solver.Add(a.Handle, b.Handle, drive), drive);
        int x = a.Handle.Value, y = b.Handle.Value; materials.NoContact.Add((Math.Min(x, y), Math.Max(x, y)));
    }
    public void SetSliderTarget(string id, double travel)
    {
        var slider = sliders.TryGetValue(id, out var s) ? s : throw new ArgumentException(id + " is not a slider.");
        slider.Description.TargetOffset = (float)travel;
        simulation.Solver.ApplyDescription(slider.Drive, slider.Description);
    }
    public void SetKinematicTarget(string id, RigidPose pose)
    {
        if (!entries[id].Kinematic) throw new ArgumentException(id + " is not kinematic.");
        pending[id] = pose;
    }

    public void Step(double dt)
    {
        foreach (var (id, target) in pending) {
            var e = entries[id]; var body = simulation.Bodies.GetBodyReference(new BodyHandle(e.Handle)); var goal = ToBepu(target, e.ShapeRotation, e.ShapeCenter);
            body.Velocity.Linear = (goal.Position - body.Pose.Position) / (float)dt;
            // World-frame rotation taking the current orientation to the goal, spread over the step.
            var dq = Quaternion.Normalize(goal.Orientation * Quaternion.Inverse(body.Pose.Orientation));
            if (dq.W < 0) dq = new(-dq.X, -dq.Y, -dq.Z, -dq.W);
            double angle = 2 * Math.Acos(Math.Clamp(dq.W, -1, 1)), s = Math.Sqrt(Math.Max(0, 1 - dq.W * dq.W));
            body.Velocity.Angular = s < 1e-6 ? Vector3.Zero : new Vector3(dq.X, dq.Y, dq.Z) / (float)s * (float)(angle / dt);
        }
        pending.Clear();
        // Velocities before the step, for the bodies that bounce.
        var before = new Dictionary<int, BodyVelocity>();
        for (int i = 0; i < materials.Bodies.Count; i++)
            if (materials.Bodies[i].Restitution > 0 && simulation.Bodies.BodyExists(new BodyHandle(i)) && !simulation.Bodies.GetBodyReference(new BodyHandle(i)).Kinematic)
                before[i] = simulation.Bodies.GetBodyReference(new BodyHandle(i)).Velocity;
        // Sliders are part of the arm, which carries its own weight: cancel gravity on them so the force limit only acts on the grip.
        foreach (var id in sliders.Keys) Body(id).Velocity.Linear -= gravity.Value * (float)dt;
        materials.Impacts.Clear();
        simulation.Timestep((float)dt);
        Bounce(before);
    }

    /// <summary>For each bouncy body, the hardest impact of the step: if the contact stopped its approach, restore e times the approach speed.</summary>
    void Bounce(Dictionary<int, BodyVelocity> before)
    {
        foreach (var body in approaching.Keys.Except(materials.Impacts.Select(i => i.Body)).ToList()) approaching.Remove(body);
        foreach (var group in materials.Impacts.GroupBy(i => i.Body)) {
            if (!before.TryGetValue(group.Key, out var v0)) continue;
            var body = simulation.Bodies.GetBodyReference(new BodyHandle(group.Key));
            Vector3 OtherVelocity(CollidableReference other, Vector3 point)
            {
                if (other.Mobility == CollidableMobility.Static) return Vector3.Zero;
                var o = simulation.Bodies.GetBodyReference(other.BodyHandle);
                return o.Velocity.Linear + Vector3.Cross(o.Velocity.Angular, point - o.Pose.Position);
            }
            float Normal(BodyVelocity v, Impact i) { var r = i.Point - body.Pose.Position; return Vector3.Dot(v.Linear + Vector3.Cross(v.Angular, r) - OtherVelocity(i.Other, i.Point), i.Normal); }
            var impact = group.MinBy(i => Normal(v0, i));
            float approach = Math.Max(-Normal(v0, impact), approaching.GetValueOrDefault(group.Key)), after = Normal(body.Velocity, impact);
            if (approach < BounceThreshold) { approaching.Remove(group.Key); continue; }
            // Only a real impact: the contact took away at least half of the approach speed (otherwise it may finish next step).
            if (after < -0.5f * approach) { approaching[group.Key] = approach; continue; }
            approaching.Remove(group.Key);
            float e = (float)Math.Clamp(materials.Bodies[group.Key].Restitution, 0, 1), wanted = e * approach;
            if (after >= wanted) continue;
            var arm = impact.Point - body.Pose.Position; var axis = Vector3.Cross(arm, impact.Normal);
            body.ComputeInverseInertia(out var inverseInertia);
            Symmetric3x3.TransformWithoutOverlap(axis, inverseInertia, out var angular);
            float inverseEffectiveMass = body.LocalInertia.InverseMass + Vector3.Dot(axis, angular);
            body.ApplyImpulse(impact.Normal * ((wanted - after) / inverseEffectiveMass), arm);
        }
    }
    public RigidPose GetPose(string id)
    {
        var e = entries[id];
        return Back(e.IsStatic ? simulation.Statics[new StaticHandle(e.Handle)].Pose : simulation.Bodies.GetBodyReference(new BodyHandle(e.Handle)).Pose, e);
    }
    static RigidPose Back(BepuPose p, Entry e)
    {
        var q = Quaternion.Normalize(Quaternion.Concatenate(Quaternion.Inverse(e.ShapeRotation), p.Orientation));
        var origin = p.Position - Vector3.Transform(e.ShapeCenter, q);
        return new(origin.X, origin.Y, origin.Z, q.X, q.Y, q.Z, q.W);
    }
    public (double X, double Y, double Z) GetLinearVelocity(string id)
    {
        var v = Body(id).Velocity.Linear; return (v.X, v.Y, v.Z);
    }
    public void Dispose() { simulation.Dispose(); pool.Clear(); }
}
