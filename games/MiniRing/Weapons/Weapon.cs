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

    /// <summary>
    /// ボクシンググローブ: 拳を包む球。表は革（コーナーの色）、中は詰め物（発泡材）、芯は拳（骨）。拳（前腕の先）に付けて打つ。
    /// 当たりは武器と同じ圧力のモデルで、詰め物が広い面でたわんで衝撃を和らげる。
    /// </summary>
    Glove,

    /// <summary>
    /// 素手の拳（<see cref="HandWear.None"/>）: 薄い皮膚の下に骨の指を握り込み、人さし指・中指の拳頭を前へ出す。
    /// 皮膚同士は強さが同じなので、打った拳の皮膚も削れる。
    /// </summary>
    Fist,

    /// <summary>
    /// メリケンサック（<see cref="HandWear.KnuckleDuster"/>）: 素手の拳の前に、拳頭の 4 つの突起を持つ鉄の棒。両端の板で掌の側の握りの棒へつながる。
    /// 硬く狭い面（突起）に力が集まるので、同じ打撃でも皮膚と肉を押し潰す。
    /// </summary>
    KnuckleDuster,

    /// <summary>
    /// カタール（<see cref="HandWear.Katar"/>）: 拳で握る H 字の鉄の枠（拳の両脇の棒と、拳の前の横木）から、拳の向きへまっすぐ伸びる菱形の断面の鉄の刃。
    /// 拳を突き出すとそのまま刺さる。
    /// </summary>
    Katar,

    /// <summary>
    /// 籠手（<see cref="HandWear.Gauntlet"/>）: 拳の前半分（拳頭と指）を覆う鉄の板、その下に詰め物、掌の側は革（コーナーの色）、手首に鉄の輪。
    /// 刃は無く、グローブより小さく硬い面で打つ。
    /// </summary>
    Gauntlet,
}

/// <summary>
/// ボクセルでできた武器 1 本（または折れた破片）。長さはローカル X（+X が切っ先・打つ側）、剣の刃の幅は Y、厚さは Z。
/// 物理の箱の当たり判定はリング（床）とだけぶつかり、体や他の武器とはボクセル同士で当たる（<see cref="VoxelContact"/>）。
/// </summary>
public sealed class Weapon
{
    private Weapon(WeaponKind kind, VoxelBody body, bool initializeHand = true)
    {
        Kind = kind;
        Body = body;
        Volume = VoxelVolume.For(body.Grid);
        if (kind == WeaponKind.Fist && initializeHand)
        {
            HandState = new BareHandState();
            Body.ReplaceGrid(HandState.Build(HandShape.Seiken));
            Volume = VoxelVolume.For(Body.Grid);
        }
    }

    public WeaponKind Kind { get; }

    public VoxelBody Body { get; }

    /// <summary>描画用の 3D テクスチャ。</summary>
    public VoxelVolume Volume { get; private set; }

    public BareHandState? HandState { get; private set; }

    public void SetHandShape(HandShape shape)
    {
        if (!Enum.IsDefined(shape))
        {
            throw new ArgumentOutOfRangeException(nameof(shape));
        }

        if (HandState is null || HandState.Shape == shape)
        {
            return;
        }

        HandState = HandState.Capture(Body.Grid).WithShape(shape);
        Body.ReplaceGrid(HandState.Build(shape));
        Volume = VoxelVolume.For(Body.Grid);
    }

    /// <summary>この武器を描く指示（格子の変化をテクスチャへ写す）。</summary>
    public VoxelVolumeDraw Draw()
    {
        Volume.Sync(Body.Grid);
        return new VoxelVolumeDraw(Volume, VoxelVolume.CellToWorld(Body.Grid, Body.GridToWorld()));
    }

    /// <summary>折れて飛んだ破片を、同じ種類の武器として包む。</summary>
    internal static Weapon PieceOf(WeaponKind kind, VoxelBody piece) => new(kind, piece, initializeHand: false);

    /// <summary>
    /// 武器を <paramref name="world"/> に置く。質量はボクセルの材質の密度 × 体積の合計（剛体の箱は格子の大きさ）。
    /// </summary>
    public static Weapon Create(PhysicsWorld world, WeaponKind kind, Vector3 position, Quaternion orientation) =>
        Create(world, kind, Build(kind), position, orientation);

    /// <summary>
    /// 手にはめる物（<see cref="IsWorn"/>）を置く。<paramref name="leather"/> はグローブと籠手の革の材質（コーナーの色:
    /// <see cref="BoxerAnatomy.RedGlove"/> か <see cref="BoxerAnatomy.BlueGlove"/>。革を使わない物では使わない）。
    /// </summary>
    public static Weapon CreateWorn(PhysicsWorld world, WeaponKind kind, byte leather, Vector3 position, Quaternion orientation)
    {
        if (!IsWorn(kind))
        {
            throw new ArgumentException($"{kind} は手にはめる物ではありません。", nameof(kind));
        }

        return Create(world, kind, Build(kind, leather), position, orientation);
    }

    /// <summary>
    /// 手にはめる物か（グローブ・素手の拳・メリケンサック・カタール・籠手）。はめた物は拳から離れず、拳の打撃（パンチ）として数え、
    /// 握った本人の体・同じ人のもう一方の手にはめた物とは当たらない。
    /// </summary>
    public static bool IsWorn(WeaponKind kind) => kind is WeaponKind.Glove or WeaponKind.Fist or WeaponKind.KnuckleDuster or WeaponKind.Katar or WeaponKind.Gauntlet;

    private static Weapon Create(PhysicsWorld world, WeaponKind kind, VoxelGrid grid, Vector3 position, Quaternion orientation)
    {
        ArgumentNullException.ThrowIfNull(world);
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
        WeaponKind.Glove => Vector3.Zero, // 拳はグローブの真ん中
        WeaponKind.Fist or WeaponKind.KnuckleDuster or WeaponKind.Katar or WeaponKind.Gauntlet => -WornBoundsOf(kind).Center, // 拳の中心
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "知らない武器です。"),
    };

    /// <summary>両手で握れる武器か（棍棒・日本刀・ロングソード。片手剣の握りは片手ぶんの長さ）。</summary>
    public static bool CanTwoHand(WeaponKind kind) => kind is WeaponKind.WoodenClub or WeaponKind.Katana or WeaponKind.Longsword;

    /// <summary>刃の無い（打って潰す）武器か。刃のある武器は、刃の形と材質だけで斬る（<see cref="VoxelContact"/>）。</summary>
    public static bool IsBlunt(WeaponKind kind) => kind is WeaponKind.WoodenClub or WeaponKind.Glove or WeaponKind.Fist or WeaponKind.KnuckleDuster or WeaponKind.Gauntlet;

    private static readonly Lazy<bool>[] RigidFaces =
        Enum.GetValues<WeaponKind>().Select(k => new Lazy<bool>(() => StrikesWithRigidFace(k))).ToArray();

    /// <summary>
    /// 打つ所（<see cref="TipOf"/>）の表の材質が、曲がらない硬い物（ヤング率を持つ鉄・木）か。硬い面は、握った腕が押し込む力をそのまま
    /// 当たった所へ伝える（<see cref="VoxelContact.Resolve"/> の drive）。柔らかい面（グローブの革・素手の皮膚）は、その下の詰め物や肉が
    /// 腕の押す力を広い面で受けて散らす: 圧力のモデルは表の面だけを見るので、柔らかい面の下の腕の力を数えると、押し当てているだけで
    /// 肌が削れてしまう。種類ではなく、作った形の打つ所の材質から決まる。
    /// </summary>
    public static bool HasRigidFace(WeaponKind kind) => RigidFaces[(int)kind].Value;

    private static bool StrikesWithRigidFace(WeaponKind kind)
    {
        var grid = Build(kind);
        var tip = TipOf(kind);
        var step = grid.CellSize.X * 0.5f;
        // 打つ所から内へ、最初に埋まったセルの材質（尖った切っ先は先の数 mm がセルにならないので、2 cm 内まで見る）。
        for (var i = 0; step * i <= 0.02f; i++)
        {
            var at = grid.FilledCellAt(tip - new Vector3(step * i, 0f, 0f));
            if (at >= 0)
            {
                return grid.Palette[grid.MaterialAt(at)].MechanicsOrDefault.Stiffness is > 0f;
            }
        }

        return false;
    }

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
        WeaponKind.Glove => new Vector3(GloveRadius, 0f, 0f),
        WeaponKind.Fist => new Vector3(LeadingKnuckle + KnuckleRadius, 0.003f, -0.003f) - WornBoundsOf(kind).Center,
        WeaponKind.KnuckleDuster => new Vector3(DusterFront + DusterBoss, 0f, -0.5f * (2f * FistRadius / 4f)) - WornBoundsOf(kind).Center, // 中指の拳頭の突起の先
        WeaponKind.Katar => new Vector3(KatarTip, 0f, 0f) - WornBoundsOf(kind).Center,
        WeaponKind.Gauntlet => new Vector3(GauntletOuter, 0f, 0f) - WornBoundsOf(kind).Center,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "知らない武器です。"),
    };

    /// <summary>武器の格子（材質は <see cref="BoxerAnatomy.Palette"/> の鉄と木。手にはめる物の革は赤コーナーの色）。</summary>
    public static VoxelGrid Build(WeaponKind kind) => Build(kind, BoxerAnatomy.RedGlove);

    /// <summary>武器の格子。<paramref name="leather"/> はグローブと籠手の革の材質（他の物では使わない）。</summary>
    public static VoxelGrid Build(WeaponKind kind, byte leather) => kind switch
    {
        WeaponKind.IronSword => Sword(),
        WeaponKind.WoodenClub => Club(),
        WeaponKind.Katana => Katana(),
        WeaponKind.Longsword => Longsword(),
        WeaponKind.Glove => Glove(leather),
        WeaponKind.Fist => KarateFist(),
        WeaponKind.KnuckleDuster => Worn(kind, WornCell, KnuckleDusterAt),
        WeaponKind.Katar => Worn(kind, KatarCell, KatarAt),
        WeaponKind.Gauntlet => Worn(kind, WornCell, p => GauntletAt(p, leather)),
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

    // ── ボクシンググローブ（人形の縮尺: 半径 2.4 cm = 試合のグローブの球の大きさ）。4 mm のセル（刃の無い広い面なので粗くてよい。
    // 2 mm では、構えて触れ合っている間の接触の計算が重すぎた）。
    private const float GloveRadius = 0.024f;
    private const float GloveCell = 0.004f;
    private const float GloveLeather = 0.004f;
    private const float GloveFist = 0.012f;

    /// <summary>グローブ: 半径 <see cref="GloveRadius"/> の球。外側 <see cref="GloveLeather"/> は革、その内は詰め物、芯（半径 <see cref="GloveFist"/>）は拳（骨）。</summary>
    public static VoxelGrid Glove(byte leather)
    {
        var half = new Vector3(GloveRadius);
        return new VoxelGrid(BoxerAnatomy.Palette, half, GloveCell, (p, _) =>
        {
            var r = p.Length();
            return r > GloveRadius ? (byte)0 : r > GloveRadius - GloveLeather ? leather : r > GloveFist ? BoxerAnatomy.Padding : BoxerAnatomy.Bone;
        });
    }

    // ── 手にはめる物（グローブ以外）。どれも「拳の座標」で形を書く: 原点は拳の中心（前腕の先、握る所）、+X は拳を突き出す向き、
    // Z は拳頭の並ぶ向き（親指の側から小指の側）、Y は手の甲と掌を結ぶ向き。格子の中心はその形を囲む箱の中心。
    // 拳の大きさは描いてきた素手の拳（グローブの半径の 0.55 倍）と同じ。人形の縮尺はおよそ 0.3 倍（人の拳の幅 8.5 cm → 2.6 cm）。

    /// <summary>素手の拳の半径 [m]（グローブの半径 2.4 cm × 0.55。描いてきた素手の拳の大きさ）。</summary>
    private const float FistRadius = 0.013f;

    /// <summary>拳の芯の骨（中手骨と指の骨をまとめた球）の半径 [m]。その外の 4 mm は皮膚。</summary>
    private const float FistBone = 0.009f;

    /// <summary>拳・メリケンサック・籠手のセル [m]（2 mm: メリケンサックの突起が 1〜2 セルで形になる大きさ）。</summary>
    private const float WornCell = 0.002f;

    /// <summary>カタールのセル [m]（刃の薄さを形で表すため、剣と同じ 1.25 mm）。</summary>
    private const float KatarCell = 0.00125f;

    // メリケンサック（人の: 幅 9 cm、前の棒の高さ 1.3 cm・厚さ 1.2 cm → 人形の縮尺）。
    private const float DusterFront = 0.016f;       // 前の棒の前の面（拳の表から 3 mm 前）
    private const float DusterBar = 0.004f;         // 前の棒の厚さ（X）
    private const float DusterBarHalfHeight = 0.004f; // 前の棒の高さの半分（Y）
    private const float DusterHalfWidth = 0.0145f;  // 幅の半分（Z。拳の幅より少し広い）
    private const float DusterBoss = 0.0025f;       // 拳頭の前の 4 つの突起の半径（前の棒から前へ出る）
    private const float DusterPalm = -0.0145f;      // 掌の側の握りの棒の下の面（Y）

    // カタール（人の: 刃 25 cm・根元の幅 5 cm、枠 15 cm → 人形の縮尺。刃は拳の前 1.9 cm から 7 cm）。
    private const float KatarBladeFrom = 0.019f;
    private const float KatarTip = 0.089f;
    private const float KatarBladeHalfWidth = 0.0075f; // 刃の根元の幅の半分（Z）
    private const float KatarPoint = 0.02f;            // 切っ先: 最後の 2 cm で 0 へ尖る
    private const float KatarSpine = 0.003f;           // 鎬の厚さ（Y）
    private const float KatarBarZ = 0.014f;            // 拳の両脇の棒の内の面（|Z|。拳の外）
    private const float KatarBarThick = 0.0025f;       // 枠の棒の太さ
    private const float KatarBarBack = -0.024f;        // 両脇の棒の後ろの端（前腕に沿う）

    // 籠手（人の: 板 2 mm 前後の鋼を拳の上に、その下に詰め物 → 人形では 2.5 mm の鉄、2 mm の詰め物）。
    private const float GauntletPadding = 0.015f;   // 詰め物の外の面（皮膚の外 2 mm）
    private const float GauntletOuter = 0.0175f;    // 鉄の板・革の外の面
    private const float GauntletPlateFrom = -0.003f; // 鉄の板が覆う所（X がこれより前: 拳頭・指・甲の前半）
    private const float GauntletCuffFrom = -0.028f; // 手首の鉄の輪の後ろの端
    private const float GauntletCuffTo = -0.011f;
    private const float GauntletCuffInner = 0.0135f;
    private const float GauntletCuffOuter = 0.016f;

    /// <summary>手にはめる物の形を囲む箱（拳の座標）の中心と半分の大きさ。</summary>
    private static (Vector3 Center, Vector3 Half) WornBoundsOf(WeaponKind kind)
    {
        var (from, to) = kind switch
        {
            WeaponKind.Fist => (new Vector3(-FistRadius), new Vector3(LeadingKnuckle + KnuckleRadius, FistRadius, FistRadius)),
            WeaponKind.KnuckleDuster => (new Vector3(-FistRadius, DusterPalm, -DusterHalfWidth), new Vector3(DusterFront + DusterBoss, FistRadius, DusterHalfWidth)),
            WeaponKind.Katar => (new Vector3(KatarBarBack, -FistRadius, -(KatarBarZ + KatarBarThick)), new Vector3(KatarTip, FistRadius, KatarBarZ + KatarBarThick)),
            WeaponKind.Gauntlet => (new Vector3(GauntletCuffFrom, -GauntletOuter, -GauntletOuter), new Vector3(GauntletOuter, GauntletOuter, GauntletOuter)),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "拳の座標で書く手にはめる物ではありません。"),
        };
        // 端のセルの中心が形の端に乗るよう、半セル広げる。
        var pad = new Vector3(0.5f * (kind == WeaponKind.Katar ? KatarCell : WornCell));
        return ((from + to) * 0.5f, ((to - from) * 0.5f) + pad);
    }

    private static VoxelGrid Worn(WeaponKind kind, float cell, Func<Vector3, byte> fill)
    {
        var (center, half) = WornBoundsOf(kind);
        return new VoxelGrid(BoxerAnatomy.Palette, half, cell, (p, _) => fill(p + center));
    }

    /// <summary>素手の拳: 半径 <see cref="FistRadius"/> の皮膚の球、芯（半径 <see cref="FistBone"/>）は骨。</summary>
    private static byte FistAt(Vector3 p)
    {
        var r = p.Length();
        return r > FistRadius ? (byte)0 : r > FistBone ? BoxerAnatomy.Skin : BoxerAnatomy.Bone;
    }

    // 正拳: 中手骨の塊と、握り込んだ4本の指。人さし指・中指の拳頭が先行する。
    // 寸法は人形の拳幅26mmに合わせる。皮膚は1セルだけで、材質の強さは既存の皮膚と同じ。
    private const float KnuckleRadius = 0.005f;
    private const float KnuckleSkin = 0.0015f;
    private const float FingerSpacing = 0.006f;
    private const float LeadingKnuckle = 0.014f;
    private const float FoldedKnuckle = 0.008f;

    // 指の親データ（BareHandState）が元のセルを指・節に振り分けるための、正拳の寸法の公開名。
    internal const float FingerPitch = FingerSpacing;
    internal const float KnuckleFront = LeadingKnuckle;
    internal const float FoldedKnuckleFront = FoldedKnuckle;
    internal const float KnuckleRadiusOf = KnuckleRadius;

    private static VoxelGrid KarateFist()
    {
        var grid = Worn(WeaponKind.Fist, WornCell, KarateFistAt);
        // 格子の位相によって拳頭の端が骨のセルにならないよう、外側の1セルを必ず皮膚にする。
        grid.Remap((_, material, surface) => surface ? BoxerAnatomy.Skin : material);
        return grid;
    }

    private static byte KarateFistAt(Vector3 p)
    {
        // 掌・中手骨（丸みのある箱）。拳頭より後ろに収まり、広い掌側は筋肉で覆われる。
        var palm = Vector3.Abs(p - new Vector3(-0.003f, 0f, 0f)) - new Vector3(0.007f, 0.007f, 0.010f);
        var distance = Vector3.Max(palm, Vector3.Zero).Length() + MathF.Min(MathF.Max(palm.X, MathF.Max(palm.Y, palm.Z)), 0f) - 0.002f;
        var fingerDistance = float.PositiveInfinity;
        for (var finger = 0; finger < 4; finger++)
        {
            var z = (finger - 1.5f) * FingerSpacing;
            var front = finger < 2 ? LeadingKnuckle : FoldedKnuckle;
            var knuckle = Vector3.Distance(p, new Vector3(front, 0.003f, z)) - KnuckleRadius;
            // 握り込んだ中節・末節は掌側に折り返し、拳頭の後ろに隠れる。
            var a = new Vector3(0.007f, -0.004f, z);
            var b = new Vector3(front, 0.003f, z);
            var axis = b - a;
            var folded = Vector3.Distance(p, a + axis * Math.Clamp(Vector3.Dot(p - a, axis) / axis.LengthSquared(), 0f, 1f)) - KnuckleRadius;
            fingerDistance = MathF.Min(fingerDistance, MathF.Min(knuckle, folded));
        }

        var surface = MathF.Min(distance, fingerDistance);
        if (surface > 0f)
        {
            return 0;
        }

        return surface > -KnuckleSkin ? BoxerAnatomy.Skin
            : fingerDistance < 0f || p.Y >= -0.003f ? BoxerAnatomy.Bone : BoxerAnatomy.Muscle;
    }

    /// <summary>
    /// メリケンサック: 素手の拳の前に鉄の棒（拳頭の前、幅は拳より少し広い）と、拳頭ごとの 4 つの丸い突起。棒の両端の板が掌の側の握りの棒
    /// （指の付け根で握る所）へ回り込む。拳と重なる所は鉄が占める。
    /// </summary>
    private static byte KnuckleDusterAt(Vector3 p)
    {
        var across = MathF.Abs(p.Z);
        // 前の棒。
        if (p.X >= DusterFront - DusterBar && p.X <= DusterFront && MathF.Abs(p.Y) <= DusterBarHalfHeight && across <= DusterHalfWidth)
        {
            return BoxerAnatomy.Iron;
        }

        // 拳頭の前の 4 つの突起（拳頭の間隔は拳の幅の 1/4）。
        for (var k = 0; k < 4; k++)
        {
            var z = (k - 1.5f) * (2f * FistRadius / 4f);
            if (Vector3.Distance(p, new Vector3(DusterFront, 0f, z)) <= DusterBoss && p.X >= DusterFront - DusterBar)
            {
                return BoxerAnatomy.Iron;
            }
        }

        // 両端の板（前の棒から掌の側へ）と、掌の側の握りの棒。
        var endPlate = across >= DusterHalfWidth - DusterBar * 0.5f && across <= DusterHalfWidth && p.X >= -0.002f && p.X <= DusterFront && p.Y >= DusterPalm && p.Y <= DusterBarHalfHeight;
        var palmBar = p.Y >= DusterPalm && p.Y <= DusterPalm + DusterBar && p.X >= -0.002f && p.X <= 0.006f && across <= DusterHalfWidth;
        return endPlate || palmBar ? BoxerAnatomy.Iron : FistAt(p);
    }

    /// <summary>
    /// カタール: 拳が握る横木（拳の中、Z に沿う鉄の棒）、拳の両脇から前腕へ沿う 2 本の棒、拳の前の横木（刃の根元）、そこから +X へまっすぐ
    /// 伸びる刃。刃の幅（Z）は根元の <see cref="KatarBladeHalfWidth"/> から、切っ先の手前 <see cref="KatarPoint"/> までに半分へ細り、そこから 0 へ
    /// 尖る。断面は剣と同じ菱形（中央の鎬で <see cref="KatarSpine"/>、刃先へ薄く、最も薄い所で 1 セル）。
    /// </summary>
    private static byte KatarAt(Vector3 p)
    {
        var across = MathF.Abs(p.Z);
        if (p.X > KatarBladeFrom)
        {
            var toTip = KatarTip - p.X;
            var t = (p.X - KatarBladeFrom) / (KatarTip - KatarPoint - KatarBladeFrom);
            var width = toTip >= KatarPoint
                ? KatarBladeHalfWidth * (1f - (0.5f * t))
                : 0.5f * KatarBladeHalfWidth * MathF.Max(0f, toTip / KatarPoint);
            if (across > width)
            {
                return 0;
            }

            var thickness = KatarSpine * (1f - (across / MathF.Max(width, 1e-6f)));
            return MathF.Abs(p.Y) <= MathF.Max(0.5f * thickness, 0.5f * KatarCell) ? BoxerAnatomy.Iron : (byte)0;
        }

        var inFrame = MathF.Abs(p.Y) <= KatarBarThick * 0.6f;
        // 拳の前の横木（刃の根元）。
        if (inFrame && p.X >= KatarBladeFrom - (2f * KatarBarThick) && across <= KatarBarZ + KatarBarThick)
        {
            return BoxerAnatomy.Iron;
        }

        // 拳の両脇の棒。
        if (inFrame && across >= KatarBarZ && across <= KatarBarZ + KatarBarThick && p.X >= KatarBarBack)
        {
            return BoxerAnatomy.Iron;
        }

        // 拳が握る横木（拳の中を Z に沿って通る）。
        if ((p.X * p.X) + (p.Y * p.Y) <= 0.002f * 0.002f && across <= KatarBarZ + KatarBarThick)
        {
            return BoxerAnatomy.Iron;
        }

        return FistAt(p);
    }

    /// <summary>
    /// 籠手: 素手の拳の上に詰め物（外の面 <see cref="GauntletPadding"/>）、その上の殻（外の面 <see cref="GauntletOuter"/>）は、拳の前の側
    /// （X が <see cref="GauntletPlateFrom"/> より前: 拳頭・指・甲の前半）が鉄の板、後ろ（掌の付け根・手首の側）が革。手首に鉄の輪。
    /// </summary>
    private static byte GauntletAt(Vector3 p, byte leather)
    {
        var r = p.Length();
        if (r <= FistRadius)
        {
            return FistAt(p);
        }

        if (r <= GauntletPadding)
        {
            return BoxerAnatomy.Padding;
        }

        if (r <= GauntletOuter)
        {
            return p.X >= GauntletPlateFrom ? BoxerAnatomy.Iron : leather;
        }

        var ring = MathF.Sqrt((p.Y * p.Y) + (p.Z * p.Z));
        return p.X >= GauntletCuffFrom && p.X <= GauntletCuffTo && ring >= GauntletCuffInner && ring <= GauntletCuffOuter ? BoxerAnatomy.Iron : (byte)0;
    }
}
