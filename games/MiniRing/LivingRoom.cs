using System.Numerics;
using Aigis.Physics;
using Aigis.Render;

namespace MiniRing;

/// <summary>
/// リングの外の部屋（実物の大きさ、単位 m）。リングは部屋の真ん中のローテーブルに置いた箱庭: 板の台座に載ったリングを、
/// 床に立った人が見下ろす（VR のプレイヤーは原点に立ち、+Z のリングを向く）。
/// <list type="bullet">
/// <item>部屋: 幅 5.4 m × 奥行き 6.4 m、天井 2.4 m。木の床、白い壁と巾木、奥の壁に窓（昼の空）とカーテン、左の壁にドア、天井に照明。</item>
/// <item>ローテーブル: リングに合わせた正方形（<see cref="RingLayout"/>。拳の試合で 1.5 m 角）、天板の上面が床から <see cref="TableTop"/>。脚は 4 本。テーブルの下にラグ。</item>
/// <item>箱庭の台座: テーブルの上に板（<see cref="PlinthHeight"/>）。その上にリングの台（<see cref="Ring"/>）。</item>
/// </list>
/// 当たり判定は床とテーブルの天板だけ（落ちた破片が床・天板に転がる）。壁・家具は見た目だけ。
/// </summary>
public static class LivingRoom
{
    /// <summary>部屋の床の範囲（X: 左右、Z: 手前 → 奥）と天井の高さ。</summary>
    public const float MinX = -2.7f;
    public const float MaxX = 2.7f;
    public const float MinZ = -2.0f;
    public const float MaxZ = 4.4f;
    public const float CeilingHeight = 2.4f;

    /// <summary>ローテーブルの天板の上面の高さ（床から）。</summary>
    public const float TableTop = 0.40f;

    /// <summary>箱庭の台座（テーブルに置いた板）の厚み。</summary>
    public const float PlinthHeight = 0.035f;

    /// <summary>リングの台が立つ高さ（台座の上面）。</summary>
    public const float StageBase = TableTop + PlinthHeight;

    private const float TableThickness = 0.04f;
    private const float WallThickness = 0.1f;

    private static readonly Vector4 Wall = new(0.93f, 0.91f, 0.86f, 1f);
    private static readonly Vector4 Ceiling = new(1f, 1f, 0.98f, 1f);
    private static readonly Vector4 Baseboard = new(0.95f, 0.94f, 0.92f, 1f);
    private static readonly Vector4 TableWood = new(0.42f, 0.27f, 0.16f, 1f);
    private static readonly Vector4 PlinthWood = new(0.62f, 0.47f, 0.32f, 1f);
    private static readonly Vector4 Rug = new(0.36f, 0.42f, 0.46f, 1f);
    private static readonly Vector4 RugBorder = new(0.78f, 0.74f, 0.66f, 1f);
    private static readonly Vector4 Sky = new(0.72f, 0.86f, 1f, 1f);
    private static readonly Vector4 Frame = new(0.96f, 0.96f, 0.95f, 1f);
    private static readonly Vector4 Curtain = new(0.82f, 0.76f, 0.64f, 1f);
    private static readonly Vector4 Door = new(0.6f, 0.45f, 0.32f, 1f);

    /// <summary>窓（奥の壁）の中心と半分の大きさ。昼の光がここから入る。</summary>
    public static Vector3 WindowCenter { get; } = new(0f, 1.45f, MaxZ);

    public static Vector2 WindowHalf { get; } = new(1.1f, 0.65f);

    /// <summary>天井の照明の位置（テーブルの真上）。</summary>
    public static Vector3 CeilingLamp(Vector3 tableCenter) => new(tableCenter.X, CeilingHeight - 0.08f, tableCenter.Z);

    /// <summary>床とテーブルの天板の当たり判定を置く（破片が転がる）。</summary>
    public static void AddColliders(PhysicsWorld world, RingLayout layout)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(layout);
        var tableCenter = layout.Center;
        var tableHalf = layout.TableHalf;
        var width = (MaxX - MinX) * 0.5f;
        var depth = (MaxZ - MinZ) * 0.5f;
        world.AddStaticBox(new Vector3((MinX + MaxX) * 0.5f, -0.05f, (MinZ + MaxZ) * 0.5f), new Vector3(width, 0.05f, depth));
        world.AddStaticBox(new Vector3(tableCenter.X, TableTop - (TableThickness * 0.5f), tableCenter.Z), new Vector3(tableHalf, TableThickness * 0.5f, tableHalf));
        world.AddStaticBox(new Vector3(tableCenter.X, TableTop + (PlinthHeight * 0.5f), tableCenter.Z), PlinthHalf(layout));
    }

    /// <summary>
    /// 部屋の見た目（床・壁・天井・巾木・窓・カーテン・ドア・ラグ・ローテーブル・台座・天井の照明・家具）。
    /// 床・壁・布・木には <paramref name="assets"/> の色のテクスチャを貼る（床の板は写真の色、ほかは塗った色に写真の木目・織り目を乗せる）。
    /// </summary>
    public static IEnumerable<PrimitiveInstance> Visuals(RingLayout layout, RoomAssets assets)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(assets);
        var tableCenter = layout.Center;
        var tableHalf = layout.TableHalf;
        var cx = (MinX + MaxX) * 0.5f;
        var cz = (MinZ + MaxZ) * 0.5f;
        var width = (MaxX - MinX) * 0.5f;
        var depth = (MaxZ - MinZ) * 0.5f;
        var h = CeilingHeight * 0.5f;

        // 床: フローリングの写真（板 1 枚の幅が実物の 10cm ほどになる繰り返し）。
        yield return assets.Floor.On(Box(new Vector3(cx, -0.005f, cz), new Vector3(width, 0.005f, depth), Vector4.One), FloorTile);

        // 壁（窓の壁は窓の穴を空けて 4 枚に分ける）と天井。
        yield return Box(new Vector3(MinX - (WallThickness * 0.5f), h, cz), new Vector3(WallThickness * 0.5f, h, depth), Wall, wall: assets);
        yield return Box(new Vector3(MaxX + (WallThickness * 0.5f), h, cz), new Vector3(WallThickness * 0.5f, h, depth), Wall, wall: assets);
        yield return Box(new Vector3(cx, h, MinZ - (WallThickness * 0.5f)), new Vector3(width, h, WallThickness * 0.5f), Wall, wall: assets);
        foreach (var wall in WindowWall(cx, width))
        {
            yield return assets.Wall.Over(wall, WallTile);
        }

        yield return assets.Wall.Over(Box(new Vector3(cx, CeilingHeight + 0.05f, cz), new Vector3(width, 0.05f, depth), Ceiling), WallTile);

        // 巾木（壁の下の白い帯）。
        const float board = 0.07f;
        yield return Box(new Vector3(MinX + 0.006f, board * 0.5f, cz), new Vector3(0.006f, board * 0.5f, depth), Baseboard);
        yield return Box(new Vector3(MaxX - 0.006f, board * 0.5f, cz), new Vector3(0.006f, board * 0.5f, depth), Baseboard);
        yield return Box(new Vector3(cx, board * 0.5f, MinZ + 0.006f), new Vector3(width, board * 0.5f, 0.006f), Baseboard);
        yield return Box(new Vector3(cx, board * 0.5f, MaxZ - 0.006f), new Vector3(width, board * 0.5f, 0.006f), Baseboard);

        // 窓: 光る空、白い枠と桟、両脇のカーテン。
        var w = WindowCenter;
        var wh = WindowHalf;
        yield return Box(w with { Z = MaxZ + 0.02f }, new Vector3(wh.X, wh.Y, 0.005f), Sky, material: MaterialKind.Emissive);
        foreach (var (center, half) in new (Vector3, Vector3)[]
                 {
                     (w with { Y = w.Y + wh.Y }, new Vector3(wh.X + 0.03f, 0.03f, 0.04f)),
                     (w with { Y = w.Y - wh.Y }, new Vector3(wh.X + 0.03f, 0.03f, 0.06f)),
                     (w with { X = w.X - wh.X }, new Vector3(0.03f, wh.Y, 0.04f)),
                     (w with { X = w.X + wh.X }, new Vector3(0.03f, wh.Y, 0.04f)),
                     (w, new Vector3(0.015f, wh.Y, 0.03f)),
                 })
        {
            yield return Box(center, half, Frame);
        }

        foreach (var side in new[] { -1f, 1f })
        {
            for (var fold = 0; fold < 4; fold++)
            {
                var x = w.X + (side * (wh.X + 0.12f + (fold * 0.07f)));
                var z = MaxZ - 0.12f - (0.03f * (fold % 2));
                yield return Box(new Vector3(x, (CeilingHeight - 0.05f) * 0.5f, z), new Vector3(0.04f, (CeilingHeight - 0.1f) * 0.5f, 0.03f), (Curtain * (0.92f + (0.08f * (fold % 2)))) with { W = 1f }).Over(assets.Fabric, FabricTile);
            }
        }

        yield return Box(new Vector3(w.X, CeilingHeight - 0.04f, MaxZ - 0.12f), new Vector3(wh.X + 0.45f, 0.012f, 0.012f), Frame);

        // ドア（左の壁）: 板・枠・取っ手。
        var doorZ = 3.1f;
        yield return Box(new Vector3(MinX + 0.01f, 1.0f, doorZ), new Vector3(0.012f, 1.0f, 0.42f), Door).Over(assets.Wood, WoodTile);
        yield return Box(new Vector3(MinX + 0.015f, 2.03f, doorZ), new Vector3(0.015f, 0.03f, 0.47f), Frame);
        yield return Box(new Vector3(MinX + 0.015f, 1.0f, doorZ - 0.45f), new Vector3(0.015f, 1.0f, 0.03f), Frame);
        yield return Box(new Vector3(MinX + 0.015f, 1.0f, doorZ + 0.45f), new Vector3(0.015f, 1.0f, 0.03f), Frame);
        yield return new PrimitiveInstance(PrimitiveKind.Sphere, Matrix4x4.CreateTranslation(new Vector3(MinX + 0.05f, 0.95f, doorZ - 0.33f)), new Vector3(0.025f, 0f, 0f), new Vector4(0.8f, 0.75f, 0.6f, 1f));

        // ラグ（テーブルの下）と縁。
        var t = tableCenter;
        yield return assets.Fabric.Over(Box(new Vector3(t.X, 0.006f, t.Z), new Vector3(tableHalf + 0.55f, 0.006f, tableHalf + 0.35f), RugBorder), FabricTile);
        yield return assets.Fabric.Over(Box(new Vector3(t.X, 0.008f, t.Z), new Vector3(tableHalf + 0.47f, 0.006f, tableHalf + 0.27f), Rug), FabricTile);

        // ローテーブル: 天板と 4 本の脚。
        yield return Box(new Vector3(t.X, TableTop - (TableThickness * 0.5f), t.Z), new Vector3(tableHalf, TableThickness * 0.5f, tableHalf), TableWood).Over(assets.Wood, WoodTile);
        const float leg = 0.035f;
        var legHeight = (TableTop - TableThickness) * 0.5f;
        foreach (var x in new[] { -1f, 1f })
        {
            foreach (var z in new[] { -1f, 1f })
            {
                yield return Box(new Vector3(t.X + (x * (tableHalf - 0.08f)), legHeight, t.Z + (z * (tableHalf - 0.08f))), new Vector3(leg, legHeight, leg), TableWood).Over(assets.Wood, WoodTile);
            }
        }

        // 箱庭の台座（テーブルに置いた明るい板）。
        yield return assets.Wood.Over(Box(new Vector3(t.X, TableTop + (PlinthHeight * 0.5f), t.Z), PlinthHalf(layout), PlinthWood), WoodTile);

        // 天井の照明（丸い灯体）。
        var lamp = CeilingLamp(t);
        yield return new PrimitiveInstance(PrimitiveKind.Cylinder, Matrix4x4.CreateScale(0.5f, 0.06f, 0.5f) * Matrix4x4.CreateTranslation(lamp), Vector3.Zero, new Vector4(1f, 0.97f, 0.9f, 1f), MaterialKind.Emissive);

        foreach (var piece in Furniture)
        {
            foreach (var instance in piece.Instances(assets))
            {
                yield return instance;
            }
        }
    }

    /// <summary>
    /// 家具の置き場所（Kenney Furniture Kit、実物の大きさ）。モデルの正面は +Z なので、Yaw で部屋の内側へ向ける。
    /// 奥の窓の下にソファ、その両脇にサイドテーブルとフロアランプ、右の壁にテレビ台とテレビとスピーカー、左の壁に本棚、
    /// 手前の左に 1 人掛けの椅子、隅に鉢植え。プレイヤーの立つ所（原点）とテーブルのまわりは空ける。
    /// </summary>
    public static IReadOnlyList<PlacedFurniture> Furniture { get; } =
    [
        new("loungeSofa", new Vector3(0f, 0f, MaxZ - 0.5f), MathF.PI),
        new("sideTable", new Vector3(-1.55f, 0f, MaxZ - 0.3f), MathF.PI, 0.6f),
        new("plantSmall1", new Vector3(-1.55f, 0.48f, MaxZ - 0.3f), 0f),
        new("lampRoundFloor", new Vector3(1.55f, 0f, MaxZ - 0.35f), MathF.PI),
        new("cabinetTelevision", new Vector3(MaxX - 0.3f, 0f, 1.0f), -MathF.PI / 2f),
        new("televisionModern", new Vector3(MaxX - 0.3f, 0.64f, 1.0f), -MathF.PI / 2f),
        new("speaker", new Vector3(MaxX - 0.2f, 0f, -0.2f), -MathF.PI / 2f),
        new("speaker", new Vector3(MaxX - 0.2f, 0f, 2.2f), -MathF.PI / 2f),
        new("bookcaseOpen", new Vector3(MinX + 0.3f, 0f, 0.7f), MathF.PI / 2f),
        new("bookcaseOpen", new Vector3(MinX + 0.3f, 0f, 1.55f), MathF.PI / 2f),
        new("books", new Vector3(MinX + 0.3f, 1.8f, 0.7f), MathF.PI / 2f),
        new("loungeChair", new Vector3(-1.8f, 0f, -1.2f), MathF.Atan2(1.8f, 2.15f)),
        new("pottedPlant", new Vector3(MaxX - 0.35f, 0f, MaxZ - 0.4f), 0f),
        new("pottedPlant", new Vector3(MinX + 0.35f, 0f, MinZ + 0.4f), 0f),
    ];

    private static Vector3 PlinthHalf(RingLayout layout) => new(layout.PlinthHalf, PlinthHeight * 0.5f, layout.PlinthHalf);

    /// <summary>
    /// 飛び散った破片が止まる高さ: リングの上（縁から 2 cm 外まで）はキャンバス、その外は台座の上、テーブルの天板の上、それより外は床。
    /// </summary>
    public static float GroundAt(Vector3 p, RingLayout layout, float canvasHeight)
    {
        ArgumentNullException.ThrowIfNull(layout);
        var c = layout.Center;
        var d = MathF.Max(MathF.Abs(p.X - c.X), MathF.Abs(p.Z - c.Z));
        return d < layout.Half + 0.02f ? canvasHeight
            : d < layout.PlinthHalf ? StageBase
            : d < layout.TableHalf ? TableTop
            : 0f;
    }

    /// <summary>奥の壁を、窓の穴の上・下・左・右の 4 枚に分ける。</summary>
    private static IEnumerable<PrimitiveInstance> WindowWall(float cx, float width)
    {
        var z = MaxZ + (WallThickness * 0.5f);
        var half = WallThickness * 0.5f;
        var w = WindowCenter;
        var wh = WindowHalf;
        var bottom = w.Y - wh.Y;
        var top = w.Y + wh.Y;
        yield return Box(new Vector3(cx, bottom * 0.5f, z), new Vector3(width, bottom * 0.5f, half), Wall);
        yield return Box(new Vector3(cx, (top + CeilingHeight) * 0.5f, z), new Vector3(width, (CeilingHeight - top) * 0.5f, half), Wall);
        var leftWidth = (w.X - wh.X - MinX) * 0.5f;
        var rightWidth = (MaxX - (w.X + wh.X)) * 0.5f;
        yield return Box(new Vector3(MinX + leftWidth, w.Y, z), new Vector3(leftWidth, wh.Y, half), Wall);
        yield return Box(new Vector3(MaxX - rightWidth, w.Y, z), new Vector3(rightWidth, wh.Y, half), Wall);
    }

    private static PrimitiveInstance Box(Vector3 center, Vector3 half, Vector4 color, SurfaceDetail detail = SurfaceDetail.None, MaterialKind material = MaterialKind.Standard)
        => new(PrimitiveKind.Cube, Matrix4x4.CreateScale(half * 2f) * Matrix4x4.CreateTranslation(center), Vector3.Zero, color, material, detail);

    /// <summary>漆喰を塗った壁の箱。</summary>
    private static PrimitiveInstance Box(Vector3 center, Vector3 half, Vector4 color, RoomAssets wall)
        => wall.Wall.Over(Box(center, half, color), WallTile);

    private static PrimitiveInstance Over(this PrimitiveInstance instance, ColourTexture texture, float tile) => texture.Over(instance, tile);

    /// <summary>テクスチャ 1 枚が覆う長さ [m]。</summary>
    private const float FloorTile = 1.6f;
    private const float WallTile = 1.5f;
    private const float FabricTile = 0.6f;
    private const float WoodTile = 1.0f;

}
