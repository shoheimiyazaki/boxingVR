using System.Numerics;
using Aigis.Foundation.Voxels;
using Aigis.Render.Voxels;

namespace MiniRing.Break;

/// <summary>休みの間に手へ持つ小物の種類（セコンドが渡す物）。</summary>
public enum PropKind
{
    /// <summary>水のボトル（プラスチック）。</summary>
    Bottle,

    /// <summary>タオル（布）。</summary>
    Towel,

    /// <summary>スマホ（画面が光る板）。</summary>
    Phone,
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

    private static Vector4 Rgb(float r, float g, float b) => new(r, g, b, 1f);

    public static VoxelPalette Palette { get; } = new(
    [
        new VoxelMaterial("empty", VoxelTissue.Empty, 1f, false, Vector4.Zero, Vector4.Zero),
        new VoxelMaterial("plastic", VoxelTissue.Solid, 1f, false, Rgb(0.72f, 0.9f, 1f), Rgb(0.72f, 0.9f, 1f), Stainable: 0f),
        new VoxelMaterial("cap", VoxelTissue.Solid, 1f, false, Rgb(0.1f, 0.4f, 0.9f), Rgb(0.1f, 0.4f, 0.9f), Stainable: 0f),
        new VoxelMaterial("water", VoxelTissue.Solid, 1f, false, Rgb(0.35f, 0.65f, 1f), Rgb(0.35f, 0.65f, 1f), Stainable: 0f),
        new VoxelMaterial("cloth", VoxelTissue.Solid, 1f, false, Rgb(0.97f, 0.97f, 0.93f), Rgb(0.97f, 0.97f, 0.93f), Stainable: 0f),
        new VoxelMaterial("stripe", VoxelTissue.Solid, 1f, false, Rgb(0.85f, 0.12f, 0.12f), Rgb(0.85f, 0.12f, 0.12f), Stainable: 0f),
        new VoxelMaterial("phone body", VoxelTissue.Solid, 1f, false, Rgb(0.08f, 0.08f, 0.1f), Rgb(0.08f, 0.08f, 0.1f), Stainable: 0f),
        new VoxelMaterial("screen", VoxelTissue.Solid, 1f, false, Rgb(0.55f, 0.95f, 1f), Rgb(0.55f, 0.95f, 1f), Stainable: 0f),
    ]);
}

/// <summary>
/// 小物の持ち方: 小物の長い軸（<paramref name="Axis"/>、小物の局所 +X）と面の向き（<paramref name="Normal"/>、局所 +Z。スマホの画面・タオルの面）を、
/// 体フレームのローカルで（X は持っている手の外側が +。左右の手で鏡になる）。<paramref name="Grip"/> は握る所（小物の局所）。
/// </summary>
public readonly record struct PropHold(Vector3 Axis, Vector3 Normal, Vector3 Grip = default);

/// <summary>
/// 手に持つ小物（ボトル・タオル・スマホ）。ボクセルの物で、剛体ではない（グローブの中心に付いて描かれるだけ。体と打ち合わず、体を削らない）。
/// 形は小物の局所座標で +X が長い軸で、握る所（<see cref="PropHold.Grip"/>）がグローブの中心に来る。向きは動作ごとの持ち方（<see cref="PropHold"/>）で
/// 体フレームから決める近道（前腕のひねりは入力から制御できないので、手の形で持ち方が変わるのを書いている。CLAUDE.md §8「休みの動作」）。
/// </summary>
public sealed class BreakProp
{
    private const float Cell = 0.002f;

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

    /// <summary>小物を作って <paramref name="owner"/> の <paramref name="hand"/> に持たせる。</summary>
    public static BreakProp Create(PropKind kind, Fighter owner, Hand hand, PropHold hold)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return kind switch
        {
            PropKind.Bottle => new BreakProp(kind, owner, hand, Bottle(), hold),
            PropKind.Towel => new BreakProp(kind, owner, hand, Towel(), hold),
            PropKind.Phone => new BreakProp(kind, owner, hand, Phone(), hold),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "知らない小物です。"),
        };
    }

    /// <summary>小物の向き（局所 → ワールド）: 体フレームの軸 2 本（長い軸・面の向き）から正規直交の枠を作り、体の向き（ヨー）で回す。</summary>
    public Matrix4x4 Orientation()
    {
        var side = Hand.LocalSide();
        var frame = Owner.Frame;
        var x = Vector3.Normalize(frame.Rotate(new Vector3(Hold.Axis.X * side, Hold.Axis.Y, Hold.Axis.Z)));
        var n = frame.Rotate(new Vector3(Hold.Normal.X * side, Hold.Normal.Y, Hold.Normal.Z));
        var z = Vector3.Normalize(n - (Vector3.Dot(n, x) * x));
        var y = Vector3.Cross(z, x);
        return new Matrix4x4(x.X, x.Y, x.Z, 0f, y.X, y.Y, y.Z, 0f, z.X, z.Y, z.Z, 0f, 0f, 0f, 0f, 1f);
    }

    /// <summary>小物の格子（真ん中が原点、メートル）→ ワールドの変換: 握る所をグローブの中心へ。</summary>
    public Matrix4x4 GridToWorld() =>
        Matrix4x4.CreateTranslation(-Hold.Grip) * Orientation() * Matrix4x4.CreateTranslation(Owner.GloveCenter(Hand));

    /// <summary>この小物を描く指示。</summary>
    public VoxelVolumeDraw Draw()
    {
        _volume.Sync(Grid);
        return new VoxelVolumeDraw(_volume, VoxelVolume.CellToWorld(Grid, GridToWorld()));
    }

    /// <summary>ボトル: 本体（丸い筒）・肩・首・キャップ。+X が口の側。水が下の 2/3。寸法はグローブ（半径 2.4cm）から両端がはみ出して見えるよう大きめ（見た目の値）。</summary>
    private static VoxelGrid Bottle()
    {
        var half = new Vector3(0.055f, 0.018f, 0.018f);
        return new VoxelGrid(BreakPalette.Palette, half, VoxelGrid.CellsFor(half, Cell), (p, _) =>
        {
            var radial = MathF.Sqrt((p.Y * p.Y) + (p.Z * p.Z));
            var x = p.X;
            if (x > 0.044f)
            {
                return radial <= 0.0098f ? BreakPalette.Cap : (byte)0;
            }

            if (x > 0.034f)
            {
                return radial <= 0.0085f ? BreakPalette.Plastic : (byte)0;
            }

            if (x > 0.021f)
            {
                var shoulder = 0.0085f + ((0.034f - x) / 0.013f * 0.0085f);
                return radial <= shoulder ? BreakPalette.Plastic : (byte)0;
            }

            if (radial > 0.0175f)
            {
                return 0;
            }

            return x < 0.016f && radial < 0.0137f ? BreakPalette.Water : BreakPalette.Plastic;
        });
    }

    /// <summary>タオル: 握った手からはみ出す布の板（両端に赤い縞）。面の向きが +Z。</summary>
    private static VoxelGrid Towel()
    {
        var half = new Vector3(0.045f, 0.035f, 0.005f);
        return new VoxelGrid(BreakPalette.Palette, half, VoxelGrid.CellsFor(half, Cell), (p, _) =>
            MathF.Abs(p.X) is > 0.037f and < 0.043f ? BreakPalette.Stripe : BreakPalette.Cloth);
    }

    /// <summary>スマホ: 黒い板。+Z の面に光る画面。長い方が +X。</summary>
    private static VoxelGrid Phone()
    {
        var half = new Vector3(0.036f, 0.02f, 0.004f);
        return new VoxelGrid(BreakPalette.Palette, half, VoxelGrid.CellsFor(half, Cell), (p, _) =>
            p.Z > 0.0005f && MathF.Abs(p.X) < 0.032f && MathF.Abs(p.Y) < 0.016f ? BreakPalette.Screen : BreakPalette.PhoneBody);
    }
}
