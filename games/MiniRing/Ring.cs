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
    private const float RopeThickness = 0.0338f;

    /// <summary>ロープの当たりの箱の、ロープの線から内側への半分の厚み [m]（見た目の太さの 2.5 倍: 細い箱をすり抜けないよう太くした当たり）。</summary>
    public const float RopeCollisionHalf = RopeThickness * 2.5f;
    private readonly List<PrimitiveInstance> _visuals = [];

    /// <param name="world">物理のワールド（台・エプロン・コーナーポスト・ロープを静的な箱で置く）。</param>
    /// <param name="center">リングの中心（床の上の位置）。</param>
    /// <param name="size">リングの 1 辺 [m]。</param>
    /// <param name="surfaceHeight">キャンバスの高さ（床から）。</param>
    /// <param name="baseHeight">リングの台が立つ高さ（テーブルに置いた台座の上面）。</param>
    public Ring(PhysicsWorld world, Vector3 center, float size, float surfaceHeight, float baseHeight)
        : this(world, center, size, surfaceHeight, baseHeight, null)
    {
    }

    /// <summary>場の形（<see cref="RingLayout.Shape"/>）で作る: 四角いリングは上と同じ、檻は六角形の金網（<see cref="HexCage"/>）。</summary>
    public Ring(PhysicsWorld world, RingLayout layout, float surfaceHeight, float baseHeight)
        : this(world, layout?.Center ?? throw new ArgumentNullException(nameof(layout)), layout.Size, surfaceHeight, baseHeight, layout.IsCage ? layout : null)
    {
    }

    private Ring(PhysicsWorld world, Vector3 center, float size, float surfaceHeight, float baseHeight, RingLayout? cage)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (!(baseHeight >= 0f && baseHeight < surfaceHeight - 0.169f))
        {
            throw new ArgumentOutOfRangeException(nameof(baseHeight), baseHeight, "The ring's stand must start below its canvas.");
        }

        Center = center;
        Size = size;
        SurfaceHeight = surfaceHeight;
        var half = size * 0.5f;
        if (cage is not null)
        {
            HexCage.Build(world, cage, surfaceHeight, baseHeight, _visuals);
            return;
        }

        // 台（箱庭の台座の上から）・エプロン・キャンバス。床とテーブルは部屋（LivingRoom）。
        var stand = surfaceHeight - 0.169f - baseHeight;
        Box(world, new Vector3(center.X, baseHeight + (stand * 0.5f), center.Z), new Vector3(half * 0.9f, stand * 0.5f, half * 0.9f), new Vector4(0.12f, 0.12f, 0.14f, 1f), solid: true);
        Box(world, new Vector3(center.X, surfaceHeight - 0.08451f, center.Z), new Vector3(half + 0.05634f, 0.08451f, half + 0.05634f), new Vector4(0.15f, 0.25f, 0.6f, 1f), solid: true);
        Box(world, new Vector3(center.X, surfaceHeight - 0.00845f, center.Z), new Vector3(half, 0.01127f, half), new Vector4(0.64f, 0.63f, 0.61f, 1f), solid: false);

        const float postHeight = 1.014f;
        foreach (var x in new[] { -1f, 1f })
        {
            foreach (var z in new[] { -1f, 1f })
            {
                var p = new Vector3(center.X + (x * half), surfaceHeight + (postHeight * 0.5f), center.Z + (z * half));
                Box(world, p, new Vector3(0.04225f, postHeight * 0.5f, 0.04225f), new Vector4(0.75f, 0.75f, 0.78f, 1f), solid: true);
            }
        }

        // ロープ 3 段（当たり判定は見た目より太め）。
        Vector4[] ropeColors = [new(0.85f, 0.15f, 0.15f, 1f), new(0.95f, 0.95f, 0.95f, 1f), new(0.15f, 0.3f, 0.85f, 1f)];
        for (var level = 0; level < 3; level++)
        {
            var y = surfaceHeight + 0.338f + (level * 0.2254f);
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
    public bool IsOut(Vector3 pelvis) => pelvis.Y < SurfaceHeight - 0.4225f;

    private void Rope(PhysicsWorld world, Vector3 position, Vector3 visualHalf, Vector4 color)
    {
        world.AddStaticBox(position, new Vector3(visualHalf.X > RopeThickness ? visualHalf.X : RopeCollisionHalf, RopeCollisionHalf, visualHalf.Z > RopeThickness ? visualHalf.Z : RopeCollisionHalf));
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
