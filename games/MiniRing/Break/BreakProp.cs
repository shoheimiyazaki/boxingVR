using System.Numerics;
using Aigis.Foundation.Voxels;
using Aigis.Render.Voxels;

namespace MiniRing.Break;

/// <summary>動作（エモート）の間に持つ・付ける小物の種類（セコンドが渡す物）。口へ運ぶ物は、口の側の端が局所 +X。</summary>
public enum PropKind
{
    /// <summary>水のボトル（プラスチック）。</summary>
    Bottle,

    /// <summary>タオル（布）。</summary>
    Towel,

    /// <summary>スマホ（画面が光る板）。</summary>
    Phone,

    /// <summary>スポーツドリンクのボトル（橙の液体）。</summary>
    SportsBottle,

    /// <summary>コーヒー・紅茶の紙コップ（蓋と袖付き）。</summary>
    Cup,

    /// <summary>エナジードリンクの缶。</summary>
    Can,

    /// <summary>牛乳パック。</summary>
    Carton,

    /// <summary>ストロー付きの紙パックのジュース。</summary>
    JuiceBox,

    /// <summary>ゼリー飲料のパウチ。</summary>
    JellyPouch,

    /// <summary>バナナ。</summary>
    Banana,

    /// <summary>おにぎり。</summary>
    Onigiri,

    /// <summary>サンドイッチ（三角に切った半分）。</summary>
    Sandwich,

    /// <summary>チョコバー（包み紙を半分むいた物）。</summary>
    Chocolate,

    /// <summary>りんご（かじった跡）。</summary>
    Apple,

    /// <summary>ドーナツ（グレーズと飾り）。</summary>
    Donut,

    /// <summary>たばこ（火のついた先と、フィルター）。</summary>
    Cigarette,

    /// <summary>マウスピース。</summary>
    MouthGuard,

    /// <summary>イヤホン（片耳）。</summary>
    Earbud,

    /// <summary>ヘッドホン（頭に付けて描く。手には持たない）。</summary>
    Headphones,
}

/// <summary>小物が付く所: 持っている手のグローブの中心か、頭。</summary>
public enum PropAttach
{
    Hand,
    Head,
}

/// <summary>
/// 小物の材質の表（見た目の色だけ。<see cref="VoxelMaterial"/> の強さ・密度は使わない: 小物は物理の剛体ではなく、
/// 体に当たらず削れない）。体の表（<c>BoxerAnatomy.Palette</c>）とは別に持つので、体の材質の番号・.vox の色番号に響かない。
/// </summary>
public static class BreakPalette
{
    public const byte Plastic = 1;
    public const byte Cap = 2;
    public const byte Water = 3;
    public const byte Cloth = 4;
    public const byte Stripe = 5;
    public const byte PhoneBody = 6;
    public const byte Screen = 7;
    public const byte Orange = 8;
    public const byte CapOrange = 9;
    public const byte PaperCup = 10;
    public const byte CupLid = 11;
    public const byte CupSleeve = 12;
    public const byte CanSilver = 13;
    public const byte CanGreen = 14;
    public const byte CanBand = 15;
    public const byte Carton = 16;
    public const byte CartonBlue = 17;
    public const byte Straw = 18;
    public const byte Pouch = 19;
    public const byte Banana = 20;
    public const byte BananaTip = 21;
    public const byte Rice = 22;
    public const byte Nori = 23;
    public const byte Bread = 24;
    public const byte Lettuce = 25;
    public const byte Ham = 26;
    public const byte Cheese = 27;
    public const byte Chocolate = 28;
    public const byte Wrapper = 29;
    public const byte Foil = 30;
    public const byte AppleRed = 31;
    public const byte AppleFlesh = 32;
    public const byte Stem = 33;
    public const byte Dough = 34;
    public const byte Glaze = 35;
    public const byte Sprinkle = 36;
    public const byte CigarettePaper = 37;
    public const byte Filter = 38;
    public const byte Ember = 39;
    public const byte MouthGuard = 40;
    public const byte Cushion = 41;

    private static Vector4 Rgb(float r, float g, float b) => new(r, g, b, 1f);

    private static VoxelMaterial Paint(string name, Vector4 color) => new(name, VoxelTissue.Solid, 1f, false, color, color, Stainable: 0f);

    public static VoxelPalette Palette { get; } = new(
    [
        new VoxelMaterial("empty", VoxelTissue.Empty, 1f, false, Vector4.Zero, Vector4.Zero),
        Paint("plastic", Rgb(0.72f, 0.9f, 1f)),
        Paint("cap", Rgb(0.1f, 0.4f, 0.9f)),
        Paint("water", Rgb(0.35f, 0.65f, 1f)),
        Paint("cloth", Rgb(0.97f, 0.97f, 0.93f)),
        Paint("stripe", Rgb(0.85f, 0.12f, 0.12f)),
        Paint("phone body", Rgb(0.08f, 0.08f, 0.1f)),
        Paint("screen", Rgb(0.55f, 0.95f, 1f)),
        Paint("orange drink", Rgb(1f, 0.55f, 0.1f)),
        Paint("orange cap", Rgb(0.95f, 0.35f, 0.05f)),
        Paint("paper cup", Rgb(0.95f, 0.92f, 0.85f)),
        Paint("cup lid", Rgb(0.18f, 0.12f, 0.09f)),
        Paint("cup sleeve", Rgb(0.62f, 0.4f, 0.22f)),
        Paint("can silver", Rgb(0.82f, 0.84f, 0.88f)),
        Paint("can green", Rgb(0.2f, 0.85f, 0.25f)),
        Paint("can band", Rgb(0.07f, 0.08f, 0.08f)),
        Paint("carton", Rgb(0.96f, 0.96f, 0.98f)),
        Paint("carton blue", Rgb(0.2f, 0.45f, 0.9f)),
        Paint("straw", Rgb(0.92f, 0.2f, 0.25f)),
        Paint("pouch", Rgb(0.75f, 0.78f, 0.82f)),
        Paint("banana", Rgb(1f, 0.85f, 0.15f)),
        Paint("banana tip", Rgb(0.35f, 0.22f, 0.08f)),
        Paint("rice", Rgb(1f, 1f, 0.97f)),
        Paint("nori", Rgb(0.05f, 0.1f, 0.07f)),
        Paint("bread", Rgb(0.86f, 0.68f, 0.38f)),
        Paint("lettuce", Rgb(0.45f, 0.8f, 0.25f)),
        Paint("ham", Rgb(0.95f, 0.55f, 0.55f)),
        Paint("cheese", Rgb(1f, 0.8f, 0.2f)),
        Paint("chocolate", Rgb(0.32f, 0.17f, 0.08f)),
        Paint("wrapper", Rgb(0.8f, 0.1f, 0.12f)),
        Paint("foil", Rgb(0.95f, 0.8f, 0.35f)),
        Paint("apple red", Rgb(0.82f, 0.1f, 0.12f)),
        Paint("apple flesh", Rgb(0.98f, 0.95f, 0.75f)),
        Paint("stem", Rgb(0.3f, 0.2f, 0.1f)),
        Paint("dough", Rgb(0.85f, 0.6f, 0.32f)),
        Paint("glaze", Rgb(1f, 0.55f, 0.75f)),
        Paint("sprinkle", Rgb(1f, 1f, 1f)),
        Paint("cigarette paper", Rgb(0.98f, 0.98f, 0.95f)),
        Paint("filter", Rgb(0.95f, 0.6f, 0.2f)),
        Paint("ember", Rgb(1f, 0.35f, 0.05f)),
        Paint("mouth guard", Rgb(0.45f, 0.8f, 1f)),
        Paint("cushion", Rgb(0.25f, 0.25f, 0.28f)),
    ]);
}

/// <summary>
/// 小物の持ち方: 小物の長い軸（<paramref name="Axis"/>、小物の局所 +X）と面の向き（<paramref name="Normal"/>、局所 +Z。スマホの画面・タオルの面）を、
/// 体フレームのローカルで（X は持っている手の外側が +。左右の手で鏡になる）。<paramref name="Grip"/> は握る所（小物の局所）。
/// </summary>
public readonly record struct PropHold(Vector3 Axis, Vector3 Normal, Vector3 Grip = default);

/// <summary>
/// 手に持つ・頭に付ける小物（ボトル・缶・食べ物・たばこ・スマホ・ヘッドホン…）。ボクセルの物で、剛体ではない（グローブの中心か頭に付いて描かれるだけ。体と打ち合わず、体を削らない）。
/// 形は小物の局所座標で +X が長い軸（口へ運ぶ物は口の側の端）で、握る所（<see cref="PropHold.Grip"/>）がグローブの中心に来る。向きは動作ごとの持ち方（<see cref="PropHold"/>）で
/// 体フレームから決める近道（前腕のひねりは入力から制御できないので、手の形で持ち方が変わるのを書いている。CLAUDE.md §8「休みの動作」）。
/// ヘッドホンは頭に付く（頭の向きに従う）。
/// </summary>
public sealed class BreakProp
{
    private const float Cell = 0.005634f;

    private readonly VoxelVolume _volume;

    private BreakProp(PropKind kind, Fighter owner, Hand hand, VoxelGrid grid, PropHold hold)
    {
        Kind = kind;
        Owner = owner;
        Hand = hand;
        Grid = grid;
        Hold = hold;
        _volume = VoxelVolume.For(grid);
    }

    public PropKind Kind { get; }

    /// <summary>持っている人。</summary>
    public Fighter Owner { get; }

    public Hand Hand { get; }

    public VoxelGrid Grid { get; }

    /// <summary>持ち方。</summary>
    public PropHold Hold { get; }

    /// <summary>この小物が付く所（手か頭）。</summary>
    public PropAttach Attach => AttachOf(Kind);

    /// <summary>小物の種類ごとの付く所。</summary>
    public static PropAttach AttachOf(PropKind kind) => kind == PropKind.Headphones ? PropAttach.Head : PropAttach.Hand;

    /// <summary>小物を作って <paramref name="owner"/> の <paramref name="hand"/> に持たせる（ヘッドホンは頭に付ける）。</summary>
    public static BreakProp Create(PropKind kind, Fighter owner, Hand hand, PropHold hold)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return new BreakProp(kind, owner, hand, kind == PropKind.Headphones ? Headphones(owner) : Build(kind), hold);
    }

    private static VoxelGrid Build(PropKind kind) => kind switch
    {
        PropKind.Bottle => Bottle(BreakPalette.Water, BreakPalette.Cap),
        PropKind.SportsBottle => Bottle(BreakPalette.Orange, BreakPalette.CapOrange),
        PropKind.Towel => Towel(),
        PropKind.Phone => Phone(),
        PropKind.Cup => Cup(),
        PropKind.Can => Can(),
        PropKind.Carton => Carton(straw: false),
        PropKind.JuiceBox => Carton(straw: true),
        PropKind.JellyPouch => JellyPouch(),
        PropKind.Banana => Banana(),
        PropKind.Onigiri => Onigiri(),
        PropKind.Sandwich => Sandwich(),
        PropKind.Chocolate => Chocolate(),
        PropKind.Apple => Apple(),
        PropKind.Donut => Donut(),
        PropKind.Cigarette => Cigarette(),
        PropKind.MouthGuard => MouthGuard(),
        PropKind.Earbud => Earbud(),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "知らない小物です。"),
    };

    /// <summary>小物の向き（局所 → ワールド）: 体フレームの軸 2 本（長い軸・面の向き）から正規直交の枠を作り、体の向き（ヨー）で回す。ヘッドホンは頭の向き。</summary>
    public Matrix4x4 Orientation()
    {
        if (Attach == PropAttach.Head)
        {
            return Matrix4x4.CreateFromQuaternion(Owner.HeadOrientation);
        }

        var side = Hand.LocalSide();
        var frame = Owner.Frame;
        var x = Vector3.Normalize(frame.Rotate(new Vector3(Hold.Axis.X * side, Hold.Axis.Y, Hold.Axis.Z)));
        var n = frame.Rotate(new Vector3(Hold.Normal.X * side, Hold.Normal.Y, Hold.Normal.Z));
        var z = Vector3.Normalize(n - (Vector3.Dot(n, x) * x));
        var y = Vector3.Cross(z, x);
        return new Matrix4x4(x.X, x.Y, x.Z, 0f, y.X, y.Y, y.Z, 0f, z.X, z.Y, z.Z, 0f, 0f, 0f, 0f, 1f);
    }

    /// <summary>小物の格子（真ん中が原点、メートル）→ ワールドの変換: 握る所をグローブの中心へ（ヘッドホンは頭の中心へ）。</summary>
    public Matrix4x4 GridToWorld() => Attach == PropAttach.Head
        ? Orientation() * Matrix4x4.CreateTranslation(Owner.HeadPosition)
        : Matrix4x4.CreateTranslation(-Hold.Grip) * Orientation() * Matrix4x4.CreateTranslation(Owner.GloveCenter(Hand));

    /// <summary>この小物を描く指示。</summary>
    public VoxelVolumeDraw Draw()
    {
        _volume.Sync(Grid);
        return new VoxelVolumeDraw(_volume, VoxelVolume.CellToWorld(Grid, GridToWorld()));
    }

    private static VoxelGrid Make(Vector3 half, Func<Vector3, byte> fill) => new(BreakPalette.Palette, half, VoxelGrid.CellsFor(half, Cell), (p, _) => fill(p));

    private static float Radial(Vector3 p) => MathF.Sqrt((p.Y * p.Y) + (p.Z * p.Z));

    /// <summary>ボトル: 本体（丸い筒）・肩・首・キャップ。+X が口の側。液体が下の 2/3。寸法はグローブ（半径 6.8cm）から両端がはみ出して見えるよう大きめ（見た目の値）。</summary>
    private static VoxelGrid Bottle(byte liquid, byte cap)
    {
        return Make(new Vector3(0.1549f, 0.0507f, 0.0507f), p =>
        {
            var radial = Radial(p);
            var x = p.X;
            if (x > 0.1239f)
            {
                return radial <= 0.02761f ? cap : (byte)0;
            }

            if (x > 0.09577f)
            {
                return radial <= 0.02394f ? BreakPalette.Plastic : (byte)0;
            }

            if (x > 0.05915f)
            {
                var shoulder = 0.02394f + ((0.09577f - x) / 0.03662f * 0.02394f);
                return radial <= shoulder ? BreakPalette.Plastic : (byte)0;
            }

            if (radial > 0.0493f)
            {
                return 0;
            }

            return x < 0.04507f && radial < 0.03859f ? liquid : BreakPalette.Plastic;
        });
    }

    /// <summary>タオル: 握った手からはみ出す布の板（両端に赤い縞）。面の向きが +Z。</summary>
    private static VoxelGrid Towel() => Make(new Vector3(0.1268f, 0.09859f, 0.01408f), p =>
        MathF.Abs(p.X) is > 0.1042f and < 0.1211f ? BreakPalette.Stripe : BreakPalette.Cloth);

    /// <summary>スマホ: 黒い板。+Z の面に光る画面。長い方が +X。</summary>
    private static VoxelGrid Phone() => Make(new Vector3(0.1014f, 0.05634f, 0.01127f), p =>
        p.Z > 0.001408f && MathF.Abs(p.X) < 0.09014f && MathF.Abs(p.Y) < 0.04507f ? BreakPalette.Screen : BreakPalette.PhoneBody);

    /// <summary>紙コップ: 底（−X）から口（+X）へ少し広がる筒、茶の袖、黒い蓋（+X）。</summary>
    private static VoxelGrid Cup() => Make(new Vector3(0.1239f, 0.05634f, 0.05634f), p =>
    {
        var radial = Radial(p);
        var x = p.X;
        if (x > 0.07324f)
        {
            // 蓋: 縁が張り出し、真ん中の飲み口が少し盛り上がる。
            var top = x < 0.09577f ? 0.0493f : x < 0.1127f ? 0.02113f : 0f;
            return radial <= top ? BreakPalette.CupLid : (byte)0;
        }

        if (x < -0.1014f)
        {
            return 0;
        }

        var body = 0.02958f + ((x + 0.1014f) / 0.1746f * 0.01831f);
        if (radial > body)
        {
            return 0;
        }

        return x is > -0.03944f and < 0.0338f ? BreakPalette.CupSleeve : BreakPalette.PaperCup;
    });

    /// <summary>缶: 緑の筒に黒い帯、上下に銀の縁。+X が飲み口。</summary>
    private static VoxelGrid Can() => Make(new Vector3(0.1014f, 0.04225f, 0.04225f), p =>
    {
        var radial = Radial(p);
        var x = p.X;
        if (MathF.Abs(x) > 0.09577f)
        {
            return 0;
        }

        if (x > 0.08169f)
        {
            return radial <= 0.02676f ? BreakPalette.CanSilver : (byte)0;
        }

        if (x < -0.08169f)
        {
            return radial <= 0.02676f ? BreakPalette.CanSilver : (byte)0;
        }

        if (radial > 0.03239f)
        {
            return 0;
        }

        return x is > -0.0338f and < 0.01127f ? BreakPalette.CanBand : BreakPalette.CanGreen;
    });

    /// <summary>紙パック: 四角い胴（青い帯）と、+X の屋根（切妻）。ストロー付きなら +X から上へ赤いストローが出る。</summary>
    private static VoxelGrid Carton(bool straw) => Make(new Vector3(0.1014f, 0.04507f, 0.03944f), p =>
    {
        var x = p.X;
        if (straw && x is > 0.0169f and < 0.1014f && MathF.Abs(p.Z) < 0.005634f && MathF.Abs(p.Y - 0.005634f) < 0.005634f)
        {
            return BreakPalette.Straw;
        }

        if (x is < -0.06761f or > 0.08451f)
        {
            return 0;
        }

        if (x > 0.06197f)
        {
            // 屋根: 先へ行くほど幅（Z）が細くなる。
            var width = 0.02676f * (1f - ((x - 0.06197f) / 0.02254f));
            return MathF.Abs(p.Z) <= width && MathF.Abs(p.Y) <= 0.03521f ? BreakPalette.Carton : (byte)0;
        }

        if (MathF.Abs(p.Y) > 0.03521f || MathF.Abs(p.Z) > 0.02676f)
        {
            return 0;
        }

        return x is > -0.03944f and < 0.02254f ? BreakPalette.CartonBlue : BreakPalette.Carton;
    });

    /// <summary>ゼリー飲料のパウチ: 銀の平らな袋に赤い帯、+X の細い飲み口（橙のキャップ）。</summary>
    private static VoxelGrid JellyPouch() => Make(new Vector3(0.107f, 0.04507f, 0.0169f), p =>
    {
        var x = p.X;
        if (x > 0.1014f || x < -0.08451f)
        {
            return 0;
        }

        if (x > 0.07324f)
        {
            return Radial(p) <= 0.01268f ? BreakPalette.CapOrange : (byte)0;
        }

        var half = 0.04085f * (1f - (0.25f * MathF.Max(0f, x / 0.07324f)));
        if (MathF.Abs(p.Y) > half || MathF.Abs(p.Z) > 0.01268f)
        {
            return 0;
        }

        return x is > -0.03944f and < 0.01127f ? BreakPalette.Stripe : BreakPalette.Pouch;
    });

    /// <summary>バナナ: 弓なりの黄色い棒、両端が茶色。+X が口へ向ける端。</summary>
    private static VoxelGrid Banana() => Make(new Vector3(0.1408f, 0.06197f, 0.0338f), p =>
    {
        var u = p.X / 0.1239f;
        if (MathF.Abs(u) > 1f)
        {
            return 0;
        }

        var center = (0.0338f * u * u) - 0.01127f;
        var radius = 0.02113f * (1f - (0.35f * u * u));
        var dy = p.Y - center;
        if (MathF.Sqrt((dy * dy) + (p.Z * p.Z)) > radius)
        {
            return 0;
        }

        return MathF.Abs(p.X) > 0.1042f ? BreakPalette.BananaTip : BreakPalette.Banana;
    });

    /// <summary>おにぎり: 三角形（頂点が +X）、下の部分に海苔。</summary>
    private static VoxelGrid Onigiri() => Make(new Vector3(0.07887f, 0.06761f, 0.03944f), p =>
    {
        if (p.X > 0.06761f || p.X < -0.06197f)
        {
            return 0;
        }

        var spread = 0.06197f * (0.06761f - p.X) / 0.1296f;
        if (MathF.Abs(p.Y) > spread)
        {
            return 0;
        }

        var thick = 0.03239f * (1f - (0.4f * (MathF.Abs(p.Y) / 0.06197f)));
        if (MathF.Abs(p.Z) > thick)
        {
            return 0;
        }

        return p.X < -0.0169f && p.Z > 0.005634f ? BreakPalette.Nori : BreakPalette.Rice;
    });

    /// <summary>サンドイッチ（三角に切った半分）: 頂点が +X、パン・レタス・ハム・チーズの層（Y の方向に重なる）。</summary>
    private static VoxelGrid Sandwich() => Make(new Vector3(0.09014f, 0.04507f, 0.07324f), p =>
    {
        if (p.X > 0.08451f || p.X < -0.08451f)
        {
            return 0;
        }

        var spread = 0.06761f * (0.08451f - p.X) / 0.169f;
        if (MathF.Abs(p.Z) > spread)
        {
            return 0;
        }

        var y = p.Y;
        if (y is < -0.03944f or > 0.0338f)
        {
            return 0;
        }

        return y < -0.02254f ? BreakPalette.Bread
            : y < -0.01127f ? BreakPalette.Lettuce
            : y < 0.005634f ? BreakPalette.Ham
            : y < 0.0169f ? BreakPalette.Cheese
            : BreakPalette.Bread;
    });

    /// <summary>チョコバー: 手前（−X）は赤い包み紙と金の銀紙、先（+X）はむいたチョコ。</summary>
    private static VoxelGrid Chocolate() => Make(new Vector3(0.1183f, 0.02535f, 0.03944f), p =>
    {
        if (MathF.Abs(p.X) > 0.1127f || MathF.Abs(p.Y) > 0.01831f || MathF.Abs(p.Z) > 0.03099f)
        {
            return 0;
        }

        return p.X < 0.01127f ? BreakPalette.Wrapper : p.X < 0.03944f ? BreakPalette.Foil : BreakPalette.Chocolate;
    });

    /// <summary>りんご: 赤い玉に茎、+X 側にかじった跡（果肉の色）。</summary>
    private static VoxelGrid Apple() => Make(new Vector3(0.05634f, 0.06761f, 0.05634f), p =>
    {
        var bite = new Vector3(0.04085f, 0.008451f, 0f);
        var fromBite = Vector3.Distance(p, bite);
        if (fromBite < 0.02113f)
        {
            return 0;
        }

        var apple = (p.X * p.X) + ((p.Y - 0.0f) * (p.Y - 0.0f) * 1.1f) + (p.Z * p.Z) <= 0.04366f * 0.04366f;
        if (apple)
        {
            return fromBite < 0.02958f ? BreakPalette.AppleFlesh : BreakPalette.AppleRed;
        }

        return MathF.Abs(p.X) < 0.004225f && MathF.Abs(p.Z) < 0.004225f && p.Y is > 0.0338f and < 0.05915f ? BreakPalette.Stem : (byte)0;
    });

    /// <summary>ドーナツ: 輪（軸は Z）。+Z 側にピンクのグレーズと白い飾り、+X 側にかじった跡。</summary>
    private static VoxelGrid Donut() => Make(new Vector3(0.06197f, 0.06197f, 0.0338f), p =>
    {
        if (Vector3.Distance(p, new Vector3(0.05493f, 0f, 0f)) < 0.0169f)
        {
            return 0;
        }

        var ring = MathF.Sqrt((p.X * p.X) + (p.Y * p.Y));
        var minor = MathF.Sqrt(((ring - 0.03521f) * (ring - 0.03521f)) + (p.Z * p.Z));
        if (minor > 0.01915f)
        {
            return 0;
        }

        if (p.Z > 0.004225f)
        {
            // 飾り: セルの位置で決まる白い点（乱数は使わない）。
            var cell = (int)MathF.Floor(p.X / Cell) + (7 * (int)MathF.Floor(p.Y / Cell));
            return cell % 5 == 0 && p.Z > 0.01127f ? BreakPalette.Sprinkle : BreakPalette.Glaze;
        }

        return BreakPalette.Dough;
    });

    /// <summary>たばこ: 白い紙巻き、+X がオレンジのフィルター（口へ）、−X が火のついた先。</summary>
    private static VoxelGrid Cigarette() => Make(new Vector3(0.1268f, 0.01408f, 0.01408f), p =>
    {
        if (MathF.Abs(p.X) > 0.1183f || Radial(p) > 0.009014f)
        {
            return 0;
        }

        return p.X > 0.06197f ? BreakPalette.Filter : p.X < -0.1042f ? BreakPalette.Ember : BreakPalette.CigarettePaper;
    });

    /// <summary>マウスピース: U の字に曲がった薄い板（水色）。U の丸い側が +X（口の前）。</summary>
    private static VoxelGrid MouthGuard() => Make(new Vector3(0.06197f, 0.01972f, 0.0507f), p =>
    {
        if (p.X < -0.008451f || MathF.Abs(p.Y) > 0.01183f)
        {
            return 0;
        }

        var u = p.X / 0.04507f;
        var w = p.Z / 0.03944f;
        var ring = (u * u) + (w * w);
        return ring is <= 1f and >= 0.4f ? BreakPalette.MouthGuard : (byte)0;
    });

    /// <summary>イヤホン: 白い小さな本体（+X が耳へ入る側）と、−X へ伸びる細いコード。</summary>
    private static VoxelGrid Earbud() => Make(new Vector3(0.04507f, 0.01972f, 0.01972f), p =>
    {
        var bud = Vector3.Distance(p, new Vector3(0.02254f, 0f, 0f)) <= 0.01549f;
        if (bud)
        {
            return BreakPalette.Cloth;
        }

        return p.X < 0.01127f && Radial(p) <= 0.004507f ? BreakPalette.PhoneBody : (byte)0;
    });

    /// <summary>
    /// ヘッドホン: 頭の箱（<c>HalfExtentsOf(頭)</c>）に合わせた寸法。頭の上を回る黒いバンドと、両耳の当て（クッション）・ハウジング。
    /// 頭の向きに付く（局所 +Z が顔、+Y が上、±X が耳）。
    /// </summary>
    private static VoxelGrid Headphones(Fighter owner)
    {
        var head = owner.HalfExtentsOf(owner.Parts.Head);
        var rx = head.X + 0.0169f;
        var ry = head.Y + 0.01127f;
        var half = new Vector3(head.X + 0.04507f, head.Y + 0.02817f, head.Z + 0.02817f);
        return Make(half, p =>
        {
            // 耳の当て: 耳の外側に円盤（ハウジング）、内側にクッション。
            var earRadius = MathF.Sqrt(((p.Y + 0.01127f) * (p.Y + 0.01127f)) + (p.Z * p.Z));
            var side = MathF.Abs(p.X);
            if (earRadius <= 0.04648f)
            {
                if (side >= head.X - 0.002817f && side < head.X + 0.008451f)
                {
                    return BreakPalette.Cushion;
                }

                if (side >= head.X + 0.008451f && side <= head.X + 0.03521f)
                {
                    return BreakPalette.PhoneBody;
                }
            }

            // バンド: 頭の上を通る半楕円の薄い帯（耳の上から頭頂を回る）。
            var e = ((p.X * p.X) / (rx * rx)) + ((p.Y * p.Y) / (ry * ry));
            return p.Y > -0.01127f && e is >= 0.86f and <= 1.1f && MathF.Abs(p.Z) <= 0.01549f ? BreakPalette.PhoneBody : (byte)0;
        });
    }
}
