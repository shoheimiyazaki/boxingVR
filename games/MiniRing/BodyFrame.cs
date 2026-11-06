using System.Numerics;

namespace MiniRing;

/// <summary>
/// 「位置 + ヨー角だけ」の体基準フレーム。プレイヤー（HMD）とファイター（ラグドール）の間で
/// 手の位置を受け渡す共通座標系で、ピッチ/ロールを捨てるので、うつむいて人形を見下ろしても
/// 腕の写像がぶれない。前方は +Z、ヨーは +Y 回り（forward = (sin θ, 0, cos θ)）。
/// </summary>
public readonly record struct BodyFrame(Vector3 Origin, float YawRadians)
{
    public Vector3 Forward => new(MathF.Sin(YawRadians), 0f, MathF.Cos(YawRadians));

    /// <summary>右方向（右手系: 前 × 上）。ヨー 0（+Z 向き）で −X。</summary>
    public Vector3 Right => new(-MathF.Cos(YawRadians), 0f, MathF.Sin(YawRadians));

    public Quaternion Rotation => Quaternion.CreateFromAxisAngle(Vector3.UnitY, YawRadians);

    /// <summary>水平成分の向き。水平成分が無ければ <paramref name="fallback"/>。</summary>
    public static float YawOf(Vector3 direction, float fallback = 0f)
        => direction.X * direction.X + direction.Z * direction.Z < 1e-8f ? fallback : MathF.Atan2(direction.X, direction.Z);

    public Vector3 WorldToLocal(Vector3 world) => InverseRotate(world - Origin);

    public Vector3 LocalToWorld(Vector3 local) => Origin + Rotate(local);

    public Vector3 Rotate(Vector3 v)
    {
        var (s, c) = MathF.SinCos(YawRadians);
        return new Vector3((v.X * c) + (v.Z * s), v.Y, (-v.X * s) + (v.Z * c));
    }

    public Vector3 InverseRotate(Vector3 v)
    {
        var (s, c) = MathF.SinCos(YawRadians);
        return new Vector3((v.X * c) - (v.Z * s), v.Y, (v.X * s) + (v.Z * c));
    }

    /// <summary>2 角の最短差（-π..π）。</summary>
    public static float DeltaAngle(float from, float to)
    {
        var d = (to - from) % (2f * MathF.PI);
        if (d > MathF.PI)
        {
            d -= 2f * MathF.PI;
        }
        else if (d < -MathF.PI)
        {
            d += 2f * MathF.PI;
        }

        return d;
    }
}
