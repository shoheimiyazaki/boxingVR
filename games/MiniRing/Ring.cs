using System.Numerics;
using Aigis.Physics;
using Aigis.Render;

namespace MiniRing;

/// <summary>
/// 卓上サイズのリング（静的ボディ）と、その描画。リング面はテーブル程度の高さにし、
/// 立った大人がファイターを見下ろせる配置にする。
/// </summary>
public sealed class Ring
{
    private const float RopeThickness = 0.012f;
    private readonly List<PrimitiveInstance> _visuals = [];

    /// <param name="world">物理のワールド（台・エプロン・コーナーポスト・ロープを静的な箱で置く）。</param>
    /// <param name="center">リングの中心（床の上の位置）。</param>
    /// <param name="size">リングの 1 辺 [m]。</param>
    /// <param name="surfaceHeight">キャンバスの高さ（床から）。</param>
    /// <param name="baseHeight">リングの台が立つ高さ（テーブルに置いた台座の上面）。</param>
    public Ring(PhysicsWorld world, Vector3 center, float size, float surfaceHeight, float baseHeight)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (!(baseHeight >= 0f && baseHeight < surfaceHeight - 0.06f))
        {
            throw new ArgumentOutOfRangeException(nameof(baseHeight), baseHeight, "The ring's stand must start below its canvas.");
        }

        Center = center;
        Size = size;
        SurfaceHeight = surfaceHeight;
        var half = size * 0.5f;

        // 台（箱庭の台座の上から）・エプロン・キャンバス。床とテーブルは部屋（LivingRoom）。
        var stand = surfaceHeight - 0.06f - baseHeight;
        Box(world, new Vector3(center.X, baseHeight + (stand * 0.5f), center.Z), new Vector3(half * 0.9f, stand * 0.5f, half * 0.9f), new Vector4(0.12f, 0.12f, 0.14f, 1f), solid: true);
        Box(world, new Vector3(center.X, surfaceHeight - 0.03f, center.Z), new Vector3(half + 0.02f, 0.03f, half + 0.02f), new Vector4(0.15f, 0.25f, 0.6f, 1f), solid: true);
        Box(world, new Vector3(center.X, surfaceHeight - 0.003f, center.Z), new Vector3(half, 0.004f, half), new Vector4(0.64f, 0.63f, 0.61f, 1f), solid: false);

        const float postHeight = 0.36f;
        foreach (var x in new[] { -1f, 1f })
        {
            foreach (var z in new[] { -1f, 1f })
            {
                var p = new Vector3(center.X + (x * half), surfaceHeight + (postHeight * 0.5f), center.Z + (z * half));
                Box(world, p, new Vector3(0.015f, postHeight * 0.5f, 0.015f), new Vector4(0.75f, 0.75f, 0.78f, 1f), solid: true);
            }
        }

        // ロープ 3 段（当たり判定は見た目より太め）。
        Vector4[] ropeColors = [new(0.85f, 0.15f, 0.15f, 1f), new(0.95f, 0.95f, 0.95f, 1f), new(0.15f, 0.3f, 0.85f, 1f)];
        for (var level = 0; level < 3; level++)
        {
            var y = surfaceHeight + 0.12f + (level * 0.08f);
            foreach (var sign in new[] { -1f, 1f })
            {
                Rope(world, new Vector3(center.X, y, center.Z + (sign * half)), new Vector3(half, RopeThickness, RopeThickness), ropeColors[level]);
                Rope(world, new Vector3(center.X + (sign * half), y, center.Z), new Vector3(RopeThickness, RopeThickness, half), ropeColors[level]);
            }
        }
    }

    public Vector3 Center { get; }
    public float Size { get; }
    public float SurfaceHeight { get; }
    public Vector3 SurfaceCenter => new(Center.X, SurfaceHeight, Center.Z);
    public IReadOnlyList<PrimitiveInstance> Visuals => _visuals;

    /// <summary>リング面より下へ落ちたらリングアウト。</summary>
    public bool IsOut(Vector3 pelvis) => pelvis.Y < SurfaceHeight - 0.15f;

    private void Rope(PhysicsWorld world, Vector3 position, Vector3 visualHalf, Vector4 color)
    {
        world.AddStaticBox(position, visualHalf * new Vector3(visualHalf.X > RopeThickness ? 1f : 2.5f, 2.5f, visualHalf.Z > RopeThickness ? 1f : 2.5f));
        _visuals.Add(Cube(position, visualHalf, color));
    }

    private void Box(PhysicsWorld world, Vector3 position, Vector3 halfExtents, Vector4 color, bool solid)
    {
        if (solid)
        {
            world.AddStaticBox(position, halfExtents);
        }

        _visuals.Add(Cube(position, halfExtents, color));
    }

    private static PrimitiveInstance Cube(Vector3 position, Vector3 halfExtents, Vector4 color)
        => new(PrimitiveKind.Cube, Matrix4x4.CreateScale(halfExtents * 2f) * Matrix4x4.CreateTranslation(position), Vector3.Zero, color);
}
