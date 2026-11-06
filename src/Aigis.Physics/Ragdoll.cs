using System.Numerics;

namespace Aigis.Physics;

/// <summary>
/// Assembles a small branched ragdoll (spine + head + two arms) from the joint
/// primitives, entirely through the public <see cref="PhysicsWorld"/> surface — no Bepu
/// types. Parts share one collision group (so they self-ignore) but collide with the
/// world. Uses BallSocket + AngularMotor per joint (a floppy-but-stable articulation);
/// swing/twist limits can be layered on later for posing.
/// </summary>
public static class Ragdoll
{
    /// <summary>The body ids of an assembled ragdoll, root first.</summary>
    public readonly record struct Parts(int Pelvis, int Chest, int Head, int LeftArm, int RightArm)
    {
        public int[] All => [Pelvis, Chest, Head, LeftArm, RightArm];
    }

    /// <summary>Renderable primitive metadata for one part.</summary>
    /// <param name="Id">Body id (query its pose from the world).</param>
    /// <param name="Shape">Physical primitive shape and dimensions.</param>
    /// <param name="Color">RGBA base color.</param>
    public readonly record struct RenderPart(int Id, BodyShape Shape, Vector4 Color);

    private static readonly Vector4 Torso = new(0.80f, 0.55f, 0.45f, 1f);
    private static readonly Vector4 Skin = new(0.90f, 0.70f, 0.58f, 1f);

    /// <summary>
    /// Primitive shapes for each part (render-agnostic:
    /// returns plain data so this assembly never depends on the render layer).
    /// </summary>
    public static IReadOnlyList<RenderPart> DescribeForRender(Parts parts) =>
    [
        new RenderPart(parts.Pelvis, new BodyShape(ShapeKind.Capsule, new Vector3(0.12f, 0.125f, 0f)), Torso),
        new RenderPart(parts.Chest, new BodyShape(ShapeKind.Capsule, new Vector3(0.15f, 0.15f, 0f)), Torso),
        new RenderPart(parts.Head, new BodyShape(ShapeKind.Sphere, new Vector3(0.12f, 0f, 0f)), Skin),
        new RenderPart(parts.LeftArm, new BodyShape(ShapeKind.Capsule, new Vector3(0.07f, 0.20f, 0f)), Skin),
        new RenderPart(parts.RightArm, new BodyShape(ShapeKind.Capsule, new Vector3(0.07f, 0.20f, 0f)), Skin),
    ];

    /// <summary>Builds a ragdoll with its pelvis at <paramref name="pelvisPosition"/>.</summary>
    public static Parts Build(PhysicsWorld world, Vector3 pelvisPosition, string namePrefix = "")
    {
        ArgumentNullException.ThrowIfNull(world);
        string Name(string part) => string.IsNullOrEmpty(namePrefix) ? part : $"{namePrefix}.{part}";
        var names = new[] { Name("pelvis"), Name("chest"), Name("head"), Name("l_arm"), Name("r_arm") };
        world.EnsureNamesAvailable(names);
        var group = world.NewCollisionGroup();
        Vector3 P(float x, float y, float z) => pelvisPosition + new Vector3(x, y, z);

        var pelvis = world.AddDynamicCapsule(P(0, 0f, 0), Quaternion.Identity, 0.12f, 0.25f, 6f, group, 0);
        var chest = world.AddDynamicCapsule(P(0, 0.45f, 0), Quaternion.Identity, 0.15f, 0.30f, 8f, group, 1);
        var head = world.AddDynamicSphere(P(0, 0.85f, 0), 0.12f, 4f, group, 2);
        var leftArm = world.AddDynamicCapsule(P(-0.30f, 0.35f, 0), Quaternion.Identity, 0.07f, 0.40f, 2f, group, 3);
        var rightArm = world.AddDynamicCapsule(P(0.30f, 0.35f, 0), Quaternion.Identity, 0.07f, 0.40f, 2f, group, 4);

        world.SetName(pelvis, Name("pelvis"));
        world.SetName(chest, Name("chest"));
        world.SetName(head, Name("head"));
        world.SetName(leftArm, Name("l_arm"));
        world.SetName(rightArm, Name("r_arm"));
        world.SetUprightAxis(pelvis, head);

        // Joints: (offsetOnA, offsetOnB) are body-local; bodies start identity-oriented,
        // so a local offset equals (jointLocal - bodyLocal).
        Joint(world, pelvis, chest, new Vector3(0, 0.22f, 0), new Vector3(0, -0.23f, 0));
        Joint(world, chest, head, new Vector3(0, 0.20f, 0), new Vector3(0, -0.20f, 0));
        Joint(world, chest, leftArm, new Vector3(-0.25f, 0.10f, 0), new Vector3(0.05f, 0.20f, 0));
        Joint(world, chest, rightArm, new Vector3(0.25f, 0.10f, 0), new Vector3(-0.05f, 0.20f, 0));

        // Adjacent (and near) parts must not collide with each other.
        world.DisableSelfCollision(pelvis, chest);
        world.DisableSelfCollision(chest, head);
        world.DisableSelfCollision(chest, leftArm);
        world.DisableSelfCollision(chest, rightArm);
        world.DisableSelfCollision(pelvis, leftArm);
        world.DisableSelfCollision(pelvis, rightArm);

        return new Parts(pelvis, chest, head, leftArm, rightArm);
    }

    private static void Joint(PhysicsWorld world, int bodyA, int bodyB, Vector3 offsetA, Vector3 offsetB)
    {
        world.AddBallSocket(bodyA, bodyB, offsetA, offsetB);
        world.AddAngularMotor(bodyA, bodyB); // damper: bleeds energy so the joint doesn't jitter
    }
}
