using System.Numerics;
using Aigis.Physics;
using Aigis.Render;

namespace MiniRing.Visual;

/// <summary>ファイターの各剛体に <see cref="BoxerModel"/> のメッシュを載せて描く。体は単色、グローブだけ色付き。</summary>
public static class BoxerRenderer
{
    /// <summary>tint は部位ごとの色の上書き（傷みの表示など）。null なら全身 body の色。</summary>
    public static void Add(List<PrimitiveInstance> instances, PhysicsWorld world, Fighter fighter, Vector4 body, Vector4 glove, Func<int, Vector4, Vector4>? tint = null)
    {
        ArgumentNullException.ThrowIfNull(instances);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(fighter);
        var model = BoxerModel.For(fighter.HalfExtents, fighter.GloveRadius);
        var p = fighter.Parts;
        Vector4 Color(int part) => tint is null ? body : tint(part, body);

        Part(instances, world, p.Head, model.Head, Color(p.Head), model.HeadLift);
        Part(instances, world, p.Chest, model.Torso, Color(p.Chest));
        Part(instances, world, p.Pelvis, model.Pelvis, Color(p.Pelvis));
        // Aigis の "Left*" 部位は −X 側。
        Part(instances, world, p.LeftUpperArm, model.UpperArmNegX, Color(p.LeftUpperArm));
        Part(instances, world, p.RightUpperArm, model.UpperArmPosX, Color(p.RightUpperArm));
        Part(instances, world, p.LeftForearm, model.ForearmNegX, Color(p.LeftForearm));
        Part(instances, world, p.RightForearm, model.ForearmPosX, Color(p.RightForearm));
        Part(instances, world, p.LeftForearm, model.GloveNegX, glove);
        Part(instances, world, p.RightForearm, model.GlovePosX, glove);
        Part(instances, world, p.LeftThigh, model.Thigh, Color(p.LeftThigh));
        Part(instances, world, p.RightThigh, model.Thigh, Color(p.RightThigh));
        Part(instances, world, p.LeftShin, model.Shin, Color(p.LeftShin));
        Part(instances, world, p.RightShin, model.Shin, Color(p.RightShin));
        Part(instances, world, p.LeftFoot, model.Foot, Color(p.LeftFoot));
        Part(instances, world, p.RightFoot, model.Foot, Color(p.RightFoot));
    }

    private static void Part(List<PrimitiveInstance> instances, PhysicsWorld world, int id, MeshData mesh, Vector4 color, float lift = 0f)
    {
        var s = world.GetState(id);
        var matrix = Matrix4x4.CreateTranslation(0f, lift, 0f) * Matrix4x4.CreateFromQuaternion(s.Orientation) * Matrix4x4.CreateTranslation(s.Position);
        instances.Add(new PrimitiveInstance(PrimitiveKind.Cube, matrix, Vector3.Zero, color, MaterialKind.Standard, Mesh: mesh));
    }
}
