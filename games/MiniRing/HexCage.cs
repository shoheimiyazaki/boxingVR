using System.Numerics;
using Aigis.Physics;
using Aigis.Render;

namespace MiniRing;

/// <summary>
/// 六角形の金網の檻（VFC、<see cref="ArenaShape.HexCage"/>）の静的な当たり判定と見た目。
/// 台・エプロン・キャンバスは六角形（幅 2·(Size/2)、長さ R の箱を 0°・60°・120° に回して 3 つ重ねる）。壁は 6 つの辺それぞれに静的な箱（金網）を 1 枚ずつ置き、
/// 見た目は細い暗い棒の菱形の格子（隙間は広め: テレビのカメラ越しに選手が見える）・頂点の柱・上端と下端のパッド。
/// 座標は水平を (X, Z)、X 軸から Z 軸の向きへ測った角で表す。面の法線は 90°・150°…、頂点は 0°・60°…（<see cref="RingLayout.Cage"/>）。
/// </summary>
internal static class HexCage
{
    /// <summary>
    /// 金網の高さ [m]（キャンバスの面から）。UFC の金網は人 1.8 m に対し約 1.75 m（≈ 1 倍）、身長 60 cm の人形では 0.6 m だが、
    /// 拳・武器の見た目を隠しすぎないよう、0.5 m（人形の身長の 0.83 倍。振り回す剣の上端・倒れた体は越えない高さ）に縮めた。
    /// </summary>
    public const float WallHeight = 0.5f;

    /// <summary>
    /// 壁の当たり判定の厚み [m]。固定ステップ 240 Hz で、拳・武器・破片が 1 歩で進む距離（拳 6 m/s で 2.5 cm、剣の先 10 m/s で 4 cm）より厚くして抜けさせない。
    /// 見た目の金網はこれより細いが、当たりは箱 1 枚（金網の網目では当たらない）。
    /// </summary>
    public const float WallThickness = 0.06f;

    private const float PostHalf = 0.02f;
    private const float RailHalf = 0.016f;
    private const float RodThickness = 0.006f;

    /// <summary>棒の菱形の格子の間隔 [m]（隣り合う平行な棒の垂直な距離。ボクセルの図柄に合う太さに対して広め）。</summary>
    private const float RodPitch = 0.06f;

    private static readonly Vector4 StandColor = new(0.1f, 0.1f, 0.11f, 1f);
    private static readonly Vector4 ApronColor = new(0.08f, 0.08f, 0.09f, 1f);
    private static readonly Vector4 CanvasColor = new(0.7f, 0.7f, 0.7f, 1f);
    private static readonly Vector4 FenceColor = new(0.13f, 0.13f, 0.14f, 1f);
    private static readonly Vector4 PadColor = new(0.03f, 0.03f, 0.035f, 1f);

    /// <summary>面（壁）の法線の角 [rad]: 90° から 60° ずつ。</summary>
    public static float NormalAngle(int edge) => (MathF.PI / 2f) + (edge * MathF.PI / 3f);

    /// <summary>頂点の角 [rad]: 0° から 60° ずつ。</summary>
    public static float VertexAngle(int vertex) => vertex * MathF.PI / 3f;

    /// <summary>水平の角（X 軸から Z 軸の向き）の方向を、<c>Y</c> 軸まわりの回転で箱の局所 Z（法線）へ向ける回転。局所 X は辺の接線になる。</summary>
    private static Quaternion FacingNormal(float angle) => Quaternion.CreateFromAxisAngle(Vector3.UnitY, (MathF.PI / 2f) - angle);

    public static void Build(PhysicsWorld world, RingLayout layout, float surfaceHeight, float baseHeight, List<PrimitiveInstance> visuals)
    {
        var center = layout.Center;
        var apothem = layout.Half;
        var radius = layout.Circumradius;
        var stand = surfaceHeight - 0.06f - baseHeight;

        // 台・エプロン・キャンバス: 長さ R の箱（半分 R/2）を 0°・60°・120° に回した 3 枚で六角形になる。当たりは面の高さで支える（キャンバスは見た目だけ）。
        for (var i = 0; i < 3; i++)
        {
            var q = FacingNormal(NormalAngle(i));
            var halfLength = radius * 0.5f;
            AddBox(world, visuals, new Vector3(center.X, baseHeight + (stand * 0.5f), center.Z), q, new Vector3(halfLength * 0.9f, stand * 0.5f, apothem * 0.9f), StandColor, solid: true);
            AddBox(world, visuals, new Vector3(center.X, surfaceHeight - 0.03f, center.Z), q, new Vector3(halfLength + 0.02f, 0.03f, apothem + 0.02f), ApronColor, solid: true);
            AddBox(world, visuals, new Vector3(center.X, surfaceHeight - 0.003f, center.Z), q, new Vector3(halfLength, 0.004f, apothem), CanvasColor, solid: false);
        }

        for (var edge = 0; edge < 6; edge++)
        {
            BuildWall(world, visuals, center, apothem, radius, surfaceHeight, edge);
        }

        // 頂点の柱（パッド付きの見た目。当たりは壁の箱が受ける）。
        for (var vertex = 0; vertex < 6; vertex++)
        {
            var a = VertexAngle(vertex);
            var postHeight = WallHeight + 0.04f;
            var p = new Vector3(center.X + (radius * MathF.Cos(a)), surfaceHeight + (postHeight * 0.5f) - 0.01f, center.Z + (radius * MathF.Sin(a)));
            AddBox(world, visuals, p, FacingNormal(a), new Vector3(PostHalf, postHeight * 0.5f, PostHalf), PadColor, solid: false);
        }
    }

    private static void BuildWall(PhysicsWorld world, List<PrimitiveInstance> visuals, Vector3 center, float apothem, float radius, float surfaceHeight, int edge)
    {
        var angle = NormalAngle(edge);
        var normal = new Vector3(MathF.Cos(angle), 0f, MathF.Sin(angle));
        var q = FacingNormal(angle);

        // 壁の箱: 内側の面が辺（キャンバスの縁）に付く。角の外側の隙間は、外側の面を伸ばした分（t·tan30°）の長さで埋める。
        var halfLength = (radius * 0.5f) + (WallThickness * MathF.Tan(MathF.PI / 6f));
        var bottom = surfaceHeight - 0.03f; // 床のエプロンに重ねて隙間を作らない
        var halfY = (surfaceHeight + WallHeight - bottom) * 0.5f;
        var wallCenter = center + (normal * (apothem + (WallThickness * 0.5f)));
        wallCenter.Y = bottom + halfY;
        world.AddStaticBox(wallCenter, q, new Vector3(halfLength, halfY, WallThickness * 0.5f));

        // 金網の見た目: 辺の内側の面に、辺に沿った幅 L・高さ H の面の中に斜め 45° の細い棒を 2 方向に並べる。
        var length = radius;
        var inner = center + (normal * (apothem - 0.004f));
        var step = RodPitch * MathF.Sqrt(2f);
        for (var sign = -1; sign <= 1; sign += 2)
        {
            // 棒は面の局所座標 (u: 辺に沿う, v: 上) で v = sign·u + c の線。面の長方形 [-L/2, L/2] × [0, H] で切る。
            for (var c = -length * 0.5f; c <= WallHeight + (length * 0.5f); c += step)
            {
                var offset = c;
                float u0, u1;
                if (sign > 0)
                {
                    u0 = MathF.Max(-length * 0.5f, -offset);
                    u1 = MathF.Min(length * 0.5f, WallHeight - offset);
                }
                else
                {
                    u0 = MathF.Max(-length * 0.5f, offset - WallHeight);
                    u1 = MathF.Min(length * 0.5f, offset);
                }

                if (u1 - u0 < RodThickness)
                {
                    continue;
                }

                var mid = (u0 + u1) * 0.5f;
                var v = (sign * mid) + offset;
                var rodLength = (u1 - u0) * MathF.Sqrt(2f);
                var local = new Vector3(mid, v, 0f);
                var position = inner + Vector3.Transform(new Vector3(local.X, 0f, 0f), q) + (Vector3.UnitY * (surfaceHeight + v));
                var rotation = Quaternion.Concatenate(Quaternion.CreateFromAxisAngle(Vector3.UnitZ, sign * MathF.PI / 4f), q);
                visuals.Add(Instance(position, rotation, new Vector3(rodLength * 0.5f, RodThickness * 0.5f, RodThickness * 0.5f), FenceColor));
            }
        }

        // 上端・下端のパッド（辺に沿う長い箱、黒）。
        var railLength = (radius * 0.5f) + 0.02f;
        var top = center + (normal * (apothem + 0.004f)) + (Vector3.UnitY * (surfaceHeight + WallHeight));
        visuals.Add(Instance(top, q, new Vector3(railLength, RailHalf, RailHalf + 0.008f), PadColor));
        var foot = center + (normal * (apothem + 0.004f)) + (Vector3.UnitY * (surfaceHeight + 0.01f));
        visuals.Add(Instance(foot, q, new Vector3(railLength, 0.012f, RailHalf), PadColor));
    }

    private static void AddBox(PhysicsWorld world, List<PrimitiveInstance> visuals, Vector3 position, Quaternion orientation, Vector3 halfExtents, Vector4 color, bool solid)
    {
        if (solid)
        {
            world.AddStaticBox(position, orientation, halfExtents);
        }

        visuals.Add(Instance(position, orientation, halfExtents, color));
    }

    private static PrimitiveInstance Instance(Vector3 position, Quaternion orientation, Vector3 halfExtents, Vector4 color)
        => new(PrimitiveKind.Cube, Matrix4x4.CreateScale(halfExtents * 2f) * Matrix4x4.CreateFromQuaternion(orientation) * Matrix4x4.CreateTranslation(position), Vector3.Zero, color);
}
