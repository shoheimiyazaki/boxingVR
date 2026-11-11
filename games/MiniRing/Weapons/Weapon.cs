using System.Numerics;
using Aigis.Foundation.Voxels;
using Aigis.Physics;
using Aigis.Physics.Voxels;
using Aigis.Render.Voxels;
using MiniRing.Voxels;

namespace MiniRing.Weapons;

/// <summary>武器の種類。</summary>
public enum WeaponKind
{
    /// <summary>鉄の剣: 菱形の断面の刃（中央の鎬が厚く、両側の刃先は 1 セルの薄さ）と尖った切っ先、鍔、木の握り、柄頭。</summary>
    IronSword,

    /// <summary>木の棍棒: 握りから先へ太くなる丸い棒。刃はない。</summary>
    WoodenClub,

    /// <summary>
    /// 日本刀（両手）: 反りのある片刃の刀身（刃は −Y、峰は +Y。断面は峰が厚く刃先へ薄くなる楔）、切っ先、鍔、長い柄。
    /// 右手は鍔のすぐ下、左手は柄頭を握る。
    /// </summary>
    Katana,

    /// <summary>
    /// ロングソード（両手）: まっすぐな両刃の刀身（菱形の断面）、十字の鍔、長い握り、柄頭。右手は鍔のすぐ下、左手は柄頭の手前を握る。
    /// </summary>
    Longsword,
}

/// <summary>
/// ボクセルでできた武器 1 本（または折れた破片）。長さはローカル X（+X が切っ先・打つ側）、剣の刃の幅は Y、厚さは Z。
/// 物理の箱の当たり判定はリング（床）とだけぶつかり、体や他の武器とはボクセル同士で当たる（<see cref="VoxelContact"/>）。
/// </summary>
public sealed class Weapon
{
    private Weapon(WeaponKind kind, VoxelBody body)
    {
        Kind = kind;
        Body = body;
        Volume = VoxelVolume.For(body.Grid);
    }

    public WeaponKind Kind { get; }

    public VoxelBody Body { get; }

    /// <summary>描画用の 3D テクスチャ。</summary>
    public VoxelVolume Volume { get; }

    /// <summary>この武器を描く指示（格子の変化をテクスチャへ写す）。</summary>
    public VoxelVolumeDraw Draw()
    {
        Volume.Sync(Body.Grid);
        return new VoxelVolumeDraw(Volume, VoxelVolume.CellToWorld(Body.Grid, Body.GridToWorld()));
    }

    /// <summary>折れて飛んだ破片を、同じ種類の武器として包む。</summary>
    internal static Weapon PieceOf(WeaponKind kind, VoxelBody piece) => new(kind, piece);

    /// <summary>
    /// 武器を <paramref name="world"/> に置く。質量はボクセルの材質の密度 × 体積の合計（剛体の箱は格子の大きさ）。
    /// </summary>
    public static Weapon Create(PhysicsWorld world, WeaponKind kind, Vector3 position, Quaternion orientation)
    {
        ArgumentNullException.ThrowIfNull(world);
        var grid = Build(kind);
        var mass = 0f;
        for (var m = 1; m < grid.Palette.Count; m++)
        {
            mass += grid.Remaining((byte)m) * grid.CellVolume * grid.Palette[(byte)m].MechanicsOrDefault.Density;
        }

        var id = world.AddDynamicBox(position, orientation, grid.HalfExtents, mass);
        return new Weapon(kind, new VoxelBody(world, id, grid));
    }

    /// <summary>
    /// 片手で握る所（武器のローカル）: 剣は握りの真ん中、棍棒は握りの端から 1.5cm、両手持ちの刀剣は上の手（鍔の下）の所。
    /// </summary>
    public static Vector3 GripOf(WeaponKind kind) => kind switch
    {
        WeaponKind.IronSword => new Vector3((GuardFrom + PommelAt) * 0.5f, 0f, 0f),
        WeaponKind.WoodenClub => new Vector3(-ClubHalfLength + 0.015f, 0f, 0f),
        WeaponKind.Katana or WeaponKind.Longsword => TwoHandGripsOf(kind).Top,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "知らない武器です。"),
    };

    /// <summary>両手で握れる武器か（棍棒・日本刀・ロングソード。片手剣の握りは片手ぶんの長さ）。</summary>
    public static bool CanTwoHand(WeaponKind kind) => kind is WeaponKind.WoodenClub or WeaponKind.Katana or WeaponKind.Longsword;

    /// <summary>刃の無い（打って潰す）武器か。刃のある武器は、刃の形と材質だけで斬る（<see cref="VoxelContact"/>）。</summary>
    public static bool IsBlunt(WeaponKind kind) => kind == WeaponKind.WoodenClub;

    /// <summary>両手で握るときの、2 つの拳の間隔 [m]（素手の拳の幅ほど）。</summary>
    public const float HandSpacing = 0.028f;

    /// <summary>
    /// 両手で握る所（武器のローカル）: 下の手（添える手）は 1 本で握るときの所、上の手（利き手）はその 1 拳ぶん先。
    /// 野球のバットと同じく、右利きなら左手が下、右手が上。
    /// </summary>
    public static (Vector3 Top, Vector3 Bottom) TwoHandGripsOf(WeaponKind kind)
    {
        switch (kind)
        {
            case WeaponKind.Katana:
                // 右手は鍔のすぐ下、左手は柄頭（剣道の握り: 両手は柄の両端）。
                return (new Vector3(KatanaGuardAt - 0.012f, 0f, 0f), new Vector3(-KatanaHalfLength + 0.011f, 0f, 0f));
            case WeaponKind.Longsword:
                return (new Vector3(LongGuardAt - 0.011f, 0f, 0f), new Vector3(LongPommelAt + LongPommelRadius + 0.008f, 0f, 0f));
            case WeaponKind.WoodenClub:
                var bottom = GripOf(kind);
                return (bottom + new Vector3(HandSpacing, 0f, 0f), bottom);
            default:
                throw new ArgumentException($"{kind} は両手で握れません。", nameof(kind));
        }
    }

    /// <summary>打つ側の先（武器のローカル）: 剣の切っ先、棍棒の頭の端の少し手前。振りの速さはここで測る。</summary>
    public static Vector3 TipOf(WeaponKind kind) => kind switch
    {
        WeaponKind.IronSword => new Vector3(SwordHalfLength, 0f, 0f),
        WeaponKind.WoodenClub => new Vector3(ClubHalfLength - 0.01f, 0f, 0f),
        WeaponKind.Katana => new Vector3(KatanaHalfLength, KatanaSori, 0f),
        WeaponKind.Longsword => new Vector3(LongHalfLength, 0f, 0f),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "知らない武器です。"),
    };

    /// <summary>武器の格子（材質は <see cref="BoxerAnatomy.Palette"/> の鉄と木）。</summary>
    public static VoxelGrid Build(WeaponKind kind) => kind switch
    {
        WeaponKind.IronSword => Sword(),
        WeaponKind.WoodenClub => Club(),
        WeaponKind.Katana => Katana(),
        WeaponKind.Longsword => Longsword(),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "知らない武器です。"),
    };

    // ── 剣（人形の縮尺: 実物の片手剣のおよそ 0.36 倍）。刃の薄さを形で表すため、体より細かい 1.25mm のセル。
    private const float SwordCell = 0.00125f;
    private const float SwordHalfLength = 0.165f;
    private const float BladeFrom = -0.1f;
    private const float BladeHalfWidth = 0.009f;
    private const float BladeSpine = 0.00375f;
    private const float PointLength = 0.03f;
    private const float GuardFrom = -0.106f;
    private const float GuardHalfWidth = 0.02f;
    private const float GuardHalfThickness = 0.0025f;
    private const float GripRadius = 0.004f;
    private const float PommelAt = -0.159f;
    private const float PommelRadius = 0.006f;

    /// <summary>
    /// 刃（x が <see cref="BladeFrom"/> から先）: 幅は切っ先の手前 <see cref="PointLength"/> から 0 へ細り、断面は菱形
    /// （中央の鎬で <see cref="BladeSpine"/>、刃先へ向かって薄くなる。刃先の 1/3 は 1 セルの薄さ）。
    /// 鍔は鉄の板、握りは木の丸棒、柄頭は鉄の玉。
    /// </summary>
    private static VoxelGrid Sword()
    {
        var half = new Vector3(SwordHalfLength, GuardHalfWidth, 0.0045f);
        return new VoxelGrid(BoxerAnatomy.Palette, half, SwordCell, (p, _) =>
        {
            if (p.X >= BladeFrom)
            {
                var toPoint = SwordHalfLength - p.X;
                var width = BladeHalfWidth * MathF.Min(1f, toPoint / PointLength);
                var across = MathF.Abs(p.Y);
                if (across > width)
                {
                    return 0;
                }

                // 菱形: 鎬から刃先へ厚さが直線で減る。最も薄い所でも 1 セル（中央の列）は残る。
                var thickness = BladeSpine * (1f - (across / MathF.Max(width, 1e-6f)));
                return MathF.Abs(p.Z) <= MathF.Max(0.5f * thickness, 0.5f * SwordCell) ? BoxerAnatomy.Iron : (byte)0;
            }

            if (p.X >= GuardFrom)
            {
                return MathF.Abs(p.Y) <= GuardHalfWidth && MathF.Abs(p.Z) <= GuardHalfThickness ? BoxerAnatomy.Iron : (byte)0;
            }

            if (Vector3.Distance(p, new Vector3(PommelAt, 0f, 0f)) <= PommelRadius)
            {
                return BoxerAnatomy.Iron;
            }

            return (p.Y * p.Y) + (p.Z * p.Z) <= GripRadius * GripRadius ? BoxerAnatomy.Wood : (byte)0;
        });
    }

    // ── 棍棒（人形の縮尺: 長さ 21cm、握りの太さ 1.4cm から頭の太さ 2.4cm）。刃がないので 2.5mm のセル。
    private const float ClubCell = 0.0025f;
    private const float ClubHalfLength = 0.105f;
    private const float ClubGripRadius = 0.007f;
    private const float ClubHeadRadius = 0.012f;

    /// <summary>握り（−X）から頭（+X）へ太くなる丸い木の棒。頭の端は丸い。</summary>
    private static VoxelGrid Club()
    {
        var half = new Vector3(ClubHalfLength, ClubHeadRadius, ClubHeadRadius);
        return new VoxelGrid(BoxerAnatomy.Palette, half, ClubCell, (p, _) =>
        {
            var along = (p.X + ClubHalfLength) / (2f * ClubHalfLength);
            var radius = ClubGripRadius + ((ClubHeadRadius - ClubGripRadius) * along * along);
            // 頭の端を丸める（端から頭の半径ぶんは半球）。
            var fromEnd = ClubHalfLength - p.X;
            if (fromEnd < ClubHeadRadius)
            {
                radius = MathF.Min(radius, MathF.Sqrt(MathF.Max(0f, (ClubHeadRadius * ClubHeadRadius) - ((ClubHeadRadius - fromEnd) * (ClubHeadRadius - fromEnd)))));
            }

            return (p.Y * p.Y) + (p.Z * p.Z) <= radius * radius ? BoxerAnatomy.Wood : (byte)0;
        });
    }

    // ── 日本刀（人形の縮尺: 実物のおよそ 0.32 倍。刃長 72 cm → 23 cm、柄 27 cm → 8.5 cm、反り 1.8 cm → 0.6 cm）。1.25mm のセル。
    private const float KatanaCell = 0.00125f;
    private const float KatanaHalfLength = 0.159f;
    private const float KatanaGuardAt = -KatanaHalfLength + 0.085f;
    private const float KatanaBladeWidth = 0.0095f;
    private const float KatanaBack = 0.0025f;
    private const float KatanaSori = 0.006f;
    private const float KatanaPoint = 0.02f;
    private const float KatanaTsubaRadius = 0.013f;
    private const float KatanaTsubaHalf = 0.00125f;
    private const float KatanaGripHalfWidth = 0.0045f;
    private const float KatanaGripHalfThick = 0.0035f;

    /// <summary>
    /// 刀身（鍔から先）: 中心線は鍔から切っ先へ放物線で +Y（峰の側）へ反る。幅（Y）は刃（−Y の縁）から峰まで、厚さ（Z）は峰で
    /// <see cref="KatanaBack"/>、刃先へ向かって 1 セルまで薄くなる楔。切っ先の手前 <see cref="KatanaPoint"/> から刃の側が峰へ向かって
    /// 丸く削られて尖る。鍔は鉄の円盤、柄は木（柄巻き）の楕円の棒。
    /// </summary>
    private static VoxelGrid Katana()
    {
        var half = new Vector3(KatanaHalfLength, KatanaTsubaRadius + KatanaSori, KatanaTsubaRadius);
        return new VoxelGrid(BoxerAnatomy.Palette, half, KatanaCell, (p, _) =>
        {
            if (p.X > KatanaGuardAt + KatanaTsubaHalf)
            {
                var t = (p.X - KatanaGuardAt) / (KatanaHalfLength - KatanaGuardAt);
                var center = KatanaSori * t * t;
                var back = center + (0.5f * KatanaBladeWidth);
                var edge = center - (0.5f * KatanaBladeWidth);
                // 切っ先: 最後の KatanaPoint で、刃の縁が峰へ向かって弧を描いて上がる。
                var toTip = KatanaHalfLength - p.X;
                if (toTip < KatanaPoint)
                {
                    var u = 1f - (toTip / KatanaPoint);
                    edge += KatanaBladeWidth * (1f - MathF.Sqrt(MathF.Max(0f, 1f - (u * u))));
                }

                if (p.Y < edge || p.Y > back)
                {
                    return 0;
                }

                var fromEdge = (p.Y - edge) / MathF.Max(1e-6f, back - edge);
                var thickness = KatanaBack * MathF.Min(1f, fromEdge * 1.6f);
                return MathF.Abs(p.Z) <= MathF.Max(0.5f * thickness, 0.5f * KatanaCell) ? BoxerAnatomy.Iron : (byte)0;
            }

            if (p.X >= KatanaGuardAt - KatanaTsubaHalf)
            {
                return (p.Y * p.Y) + (p.Z * p.Z) <= KatanaTsubaRadius * KatanaTsubaRadius ? BoxerAnatomy.Iron : (byte)0;
            }

            var ey = p.Y / KatanaGripHalfWidth;
            var ez = p.Z / KatanaGripHalfThick;
            return (ey * ey) + (ez * ez) <= 1f ? BoxerAnatomy.Wood : (byte)0;
        });
    }

    // ── ロングソード（人形の縮尺 0.32 倍: 刃長 90 cm → 29 cm、鍔 22 cm → 7 cm、握り 20 cm → 6.5 cm）。
    private const float LongCell = 0.00125f;
    private const float LongHalfLength = 0.19f;
    private const float LongPommelRadius = 0.0075f;
    private const float LongPommelAt = -LongHalfLength + LongPommelRadius;
    private const float LongGuardAt = LongPommelAt + LongPommelRadius + 0.065f;
    private const float LongGuardHalfWidth = 0.035f;
    private const float LongGuardHalfThick = 0.0016f;
    private const float LongBladeBase = 0.008f;
    private const float LongBladeTip = 0.004f;
    private const float LongSpine = 0.002f;
    private const float LongPoint = 0.03f;
    private const float LongGripRadius = 0.0045f;

    /// <summary>
    /// 刀身（鍔から先）: 幅は根元の <see cref="LongBladeBase"/> から先の <see cref="LongBladeTip"/> へ細り、切っ先の手前 <see cref="LongPoint"/>
    /// から 0 へ尖る。断面は菱形（中央の鎬で <see cref="LongSpine"/>、両刃の縁は 1 セル）。鍔は鉄の棒、握りは木、柄頭は鉄の玉。
    /// </summary>
    private static VoxelGrid Longsword()
    {
        var half = new Vector3(LongHalfLength, LongGuardHalfWidth, 0.0045f);
        return new VoxelGrid(BoxerAnatomy.Palette, half, LongCell, (p, _) =>
        {
            if (p.X > LongGuardAt + LongGuardHalfThick)
            {
                var t = (p.X - LongGuardAt) / (LongHalfLength - LongGuardAt);
                var width = LongBladeBase + ((LongBladeTip - LongBladeBase) * t);
                width *= MathF.Min(1f, (LongHalfLength - p.X) / LongPoint);
                var across = MathF.Abs(p.Y);
                if (across > width)
                {
                    return 0;
                }

                var thickness = LongSpine * (1f - (across / MathF.Max(width, 1e-6f)));
                return MathF.Abs(p.Z) <= MathF.Max(0.5f * thickness, 0.5f * LongCell) ? BoxerAnatomy.Iron : (byte)0;
            }

            if (p.X >= LongGuardAt - LongGuardHalfThick)
            {
                return MathF.Abs(p.Y) <= LongGuardHalfWidth && MathF.Abs(p.Z) <= 0.0016f ? BoxerAnatomy.Iron : (byte)0;
            }

            if (Vector3.Distance(p, new Vector3(LongPommelAt, 0f, 0f)) <= LongPommelRadius)
            {
                return BoxerAnatomy.Iron;
            }

            return (p.Y * p.Y) + (p.Z * p.Z) <= LongGripRadius * LongGripRadius ? BoxerAnatomy.Wood : (byte)0;
        });
    }
}
