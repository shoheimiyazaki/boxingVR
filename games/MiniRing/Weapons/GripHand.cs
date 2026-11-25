using System.Numerics;
using Aigis.Foundation.Voxels;
using MiniRing.Voxels;

namespace MiniRing.Weapons;

/// <summary>
/// 手に持つ物（剣・刀・ロングソード・棍棒・バット・棒・杖）を握る拳: 素手の拳（<see cref="WeaponKind.Fist"/>）と同じ骨格・同じ材質・同じ台帳
/// （<see cref="BareHandState"/>）の手を、握っている間だけ物の剛体に重ねる（<see cref="Weapon.GripHandFor"/>）。
/// <list type="bullet">
/// <item>形は正拳（<see cref="HandShape.Seiken"/>）の握り拳。手の向きは物のローカルのまま（物の X = 手の X = 前腕の向き: 手に持つ物は前腕に沿わせて
/// 握り（<see cref="Fighter.Wield"/>）、試合の間もほぼ前腕に沿う（調べた: 肘の向きは物の −X から 10〜40°））。素手の拳を前腕に付けたのと同じ向き。</item>
/// <item>柄は手首の点（<see cref="HandSkeleton.Wrist"/>: 前腕の先が手を握る点）の高さで拳を前後に貫く。柄の入る所は先に取ってあり（<see cref="Held"/>）、
/// 押しのけられた組織は柄の外へ回る。前腕の軸と柄の軸が同じ線に乗るので、拳は前腕の先にまっすぐ続く。</item>
/// <item>握りの関節（前腕の先と物をつなぐ玉継手）は今まで通り柄の上の握る所（<see cref="Weapon.GripOf"/>）。拳は柄の上に、手首をその点に置き、
/// 鍔・もう一方の拳に食い込むときだけ柄頭の側へずらす（<c>Wrists</c>）。</item>
/// <item>質量は手の組織の密度 × 体積（<see cref="MassPerCell"/>）。物の質量に足され、腕が引く重さ・振りの慣性に入る（素手の拳を外した分が戻る）。</item>
/// </list>
/// 近道: docs/DEBT.md「握る拳の形と向き」。
/// </summary>
internal static class GripHand
{
    /// <summary>握る拳の関節の姿勢（正拳）。</summary>
    public static HandPose Pose { get; } = HandSkeleton.PoseOf(HandShape.Seiken);

    /// <summary>拳の長さ [m]: 手首の点から拳頭の前の面まで（柄の上で 2 つの拳が重ならない間隔・鍔の手前に収める長さ）。</summary>
    public static float Length { get; } = MeasureLength();

    /// <summary>手の 1 セルの質量 [kg]（手の組織の密度 × セルの体積。素手の拳と同じ値）。</summary>
    public static float MassPerCell { get; } = BoxerAnatomy.HandMechanics.Density * HandSkeleton.Cell * HandSkeleton.Cell * HandSkeleton.Cell;

    /// <summary>空の握り手の格子（置いたときと同じ大きさ。握っていない間の物の剛体に重ねておく）。</summary>
    public static VoxelGrid EmptyGrid()
    {
        _ = new BareHandState().Placed(Pose, out var grid, Held(0f));
        return new VoxelGrid(grid.Palette, grid.HalfExtents, grid.Dimensions, (_, _) => 0);
    }

    /// <summary>半径 <paramref name="radius"/> [m] の柄が占める所（手の座標: 手首の点の高さを X に沿って通る）。</summary>
    public static Func<Vector3, bool> Held(float radius)
        => p => ((p.Y - HandSkeleton.Wrist.Y) * (p.Y - HandSkeleton.Wrist.Y)) + (p.Z * p.Z) <= radius * radius;

    /// <summary>手首の点が物のローカルの (<paramref name="wristX"/>, 0, 0)（柄の軸の上）に来るときの、手の格子の中心（物のローカル）。</summary>
    public static Vector3 CenterLocal(float wristX) => new(wristX - HandSkeleton.Wrist.X, -HandSkeleton.Wrist.Y, -HandSkeleton.Wrist.Z);

    /// <summary>
    /// 柄の上の拳の手首の位置（物のローカル X）。握る所 <paramref name="grips"/>（拳ごと、昇順でなくてよい）に手首を置き、拳（長さ <see cref="Length"/>）が
    /// 柄の範囲 <paramref name="span"/> を出る・もう一方の拳と重なるときだけずらす。どの拳も握る所を包む（手首 ≤ 握る所 ≤ 拳頭）範囲で動かす。
    /// </summary>
    public static float[] Wrists((float Min, float Max) span, IReadOnlyList<float> grips)
    {
        var g = grips.ToArray();
        var wrists = new float[g.Length];
        Wrists(span, g, wrists);
        return wrists;
    }

    /// <summary>上の手首の位置の、割り当てをしない形（拳は 1 つか 2 つ。答えは <paramref name="wrists"/> へ）。</summary>
    public static void Wrists((float Min, float Max) span, ReadOnlySpan<float> grips, Span<float> wrists)
    {
        var length = Length;
        for (var i = 0; i < grips.Length; i++)
        {
            wrists[i] = Fit(grips[i], span, length);
        }

        if (grips.Length == 2)
        {
            var (low, high) = grips[0] <= grips[1] ? (0, 1) : (1, 0);
            var overlap = wrists[low] + length - wrists[high];
            if (overlap > 0f)
            {
                // 下の拳を柄頭の側へ、残りを上の拳の鍔の側へ（どちらも握る所を包む範囲・柄の範囲の中で）。
                var down = MathF.Min(overlap, wrists[low] - MathF.Max(span.Min, grips[low] - length));
                wrists[low] -= MathF.Max(0f, down);
                overlap = wrists[low] + length - wrists[high];
                var up = MathF.Min(MathF.Max(0f, overlap), MathF.Min(span.Max - length, grips[high]) - wrists[high]);
                wrists[high] += MathF.Max(0f, up);
            }
        }
    }

    private static float Fit(float grip, (float Min, float Max) span, float length)
    {
        var wrist = MathF.Min(grip, span.Max - length);
        return MathF.Max(wrist, MathF.Max(span.Min, grip - length));
    }

    private static float MeasureLength()
    {
        _ = new BareHandState().Placed(Pose, out var grid);
        var front = float.MinValue;
        for (var i = 0; i < grid.CellCount; i++)
        {
            if (grid.MaterialAt(i) != 0)
            {
                var (x, y, z) = grid.Coordinates(i);
                front = MathF.Max(front, grid.CellCenter(x, y, z).X + (0.5f * HandSkeleton.Cell));
            }
        }

        return front - HandSkeleton.Wrist.X;
    }
}
