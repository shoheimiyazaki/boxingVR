using System.Numerics;
using Aigis.Render;

namespace MiniRing.Presentation;

/// <summary>
/// 貯めた 2 枚の場面の間を描くための姿勢の補間（描画だけ。試合には戻らない）。位置と大きさは線形、向きは球面線形。
/// 分解できない行列（潰れた・鏡に映した）は近い方をそのまま使う。
/// </summary>
public static class PoseBlend
{
    /// <summary>2 枚の間に 1 つの物が動ける距離の上限 [表示の m]（物理の約 1.4 m。拳でも 1/30 秒ではこの数分の 1）。越えたら別の物とみなす。</summary>
    public const float MaxJump = 0.5f;

    public static Matrix4x4 Matrix(Matrix4x4 from, Matrix4x4 to, float amount)
    {
        if (from == to) { return from; }
        if (!Matrix4x4.Decompose(from, out var sa, out var ra, out var ta) || !Matrix4x4.Decompose(to, out var sb, out var rb, out var tb))
        {
            return amount < 0.5f ? from : to;
        }

        return Matrix4x4.CreateScale(Vector3.Lerp(sa, sb, amount))
            * Matrix4x4.CreateFromQuaternion(Quaternion.Slerp(ra, rb, amount))
            * Matrix4x4.CreateTranslation(Vector3.Lerp(ta, tb, amount));
    }

    /// <summary>同じ並びの物どうしだけ補間する（数・種類・形が違えば近い方の場面）。</summary>
    public static PrimitiveInstance[] Instances(PrimitiveInstance[] from, PrimitiveInstance[] to, float amount)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        var nearer = amount < 0.5f ? from : to;
        if (ReferenceEquals(from, to) || from.Length != to.Length || amount <= 0f || amount >= 1f)
        {
            return amount >= 1f ? to : nearer;
        }

        var blended = new PrimitiveInstance[from.Length];
        for (var i = 0; i < from.Length; i++)
        {
            var a = from[i];
            var b = to[i];
            if (a.Kind != b.Kind || !ReferenceEquals(a.Mesh, b.Mesh)
                || Vector3.DistanceSquared(a.Model.Translation, b.Model.Translation) > MaxJump * MaxJump)
            {
                return nearer; // 並びが入れ替わった（破片が増えて減った）らしい: 別の物どうしを補間しない
            }

            blended[i] = a.Equals(b) ? a : nearer[i] with
            {
                Model = Matrix(a.Model, b.Model, amount),
                Dimensions = Vector3.Lerp(a.Dimensions, b.Dimensions, amount),
                Color = Vector4.Lerp(a.Color, b.Color, amount),
            };
        }

        return blended;
    }
}
