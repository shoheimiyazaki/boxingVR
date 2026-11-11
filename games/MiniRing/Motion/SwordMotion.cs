using System.Numerics;

namespace MiniRing.Motion;

/// <summary>
/// 両手で剣を振る動きの 1 フレーム（体フレーム: 肩の中心が原点、向きのヨーだけ回した座標。+Z が前、+X が左、+Y が上）。長さはすべて
/// 腕の長さ（肩から手首）を 1 とした値なので、どの大きさの体にも当てはめられる。
/// </summary>
/// <param name="Right">右手（上の手、鍔の側）の位置。</param>
/// <param name="Left">左手（下の手、柄頭の側）の位置。</param>
/// <param name="Blade">剣の向き（柄頭から切っ先への単位ベクトル）。</param>
/// <param name="Edge">刃の向き（<see cref="Blade"/> に垂直な単位ベクトル。日本刀は片刃でこちらが刃、両刃の剣は片方の刃）。</param>
/// <param name="Head">頭の、中立の構えからのずれ（前へ出せば前傾、下げれば沈む）。</param>
/// <param name="Twist">胴のひねり [rad]（胸の向き − 腰の向き。正で右肩が前）。</param>
/// <param name="Velocity">腰の水平の速さ（体フレーム、腕の長さ / s。録った速さのまま）。</param>
public readonly record struct MotionFrame(Vector3 Right, Vector3 Left, Vector3 Blade, Vector3 Edge, Vector3 Head, float Twist, Vector3 Velocity)
{
    /// <summary>剣の向き（武器のローカル → 体フレーム）: ローカル X が <see cref="Blade"/>、ローカル −Y が <see cref="Edge"/>。</summary>
    public Quaternion Orientation
    {
        get
        {
            var x = Blade;
            var y = -Edge;
            var z = Vector3.Cross(x, y);
            return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(new Matrix4x4(x.X, x.Y, x.Z, 0f, y.X, y.Y, y.Z, 0f, z.X, z.Y, z.Z, 0f, 0f, 0f, 0f, 1f)));
        }
    }

    /// <summary>2 つのフレームの間（向きは正規化し直す）。</summary>
    public static MotionFrame Lerp(MotionFrame a, MotionFrame b, float t)
    {
        var blade = Vector3.Normalize(Vector3.Lerp(a.Blade, b.Blade, t));
        var edge = Vector3.Lerp(a.Edge, b.Edge, t);
        edge = Vector3.Normalize(edge - (Vector3.Dot(edge, blade) * blade) + new Vector3(1e-6f));
        return new MotionFrame(Vector3.Lerp(a.Right, b.Right, t), Vector3.Lerp(a.Left, b.Left, t), blade, edge,
            Vector3.Lerp(a.Head, b.Head, t), a.Twist + ((b.Twist - a.Twist) * t), Vector3.Lerp(a.Velocity, b.Velocity, t));
    }
}

/// <summary>両手で剣を振る動き 1 本（等間隔のフレームの列）。モーションキャプチャから（<see cref="MotionRetarget"/>）か、型の手作りのキー（<see cref="SwordForms"/>）から作る。</summary>
/// <param name="Name">名前。</param>
/// <param name="FrameTime">フレームの間隔 [s]（録った速さ）。</param>
/// <param name="Frames">フレーム。</param>
public sealed record SwordMotion(string Name, float FrameTime, IReadOnlyList<MotionFrame> Frames)
{
    /// <summary>長さ [s]（録った速さ）。</summary>
    public float Duration => (Frames.Count - 1) * FrameTime;

    /// <summary>時刻 <paramref name="time"/> [s]（録った速さ）のフレーム（前後のフレームの間を補う。範囲の外は端）。</summary>
    public MotionFrame Sample(float time)
    {
        var x = Math.Clamp(time / FrameTime, 0f, Frames.Count - 1);
        var i = Math.Min((int)x, Frames.Count - 2);
        return Frames.Count == 1 ? Frames[0] : MotionFrame.Lerp(Frames[i], Frames[i + 1], x - i);
    }

    /// <summary>
    /// 刃筋: 剣の先の動き（剣に垂直な分）が速い間は、刃の向きをその動きの面へ回す。向きは前のフレームから 180° は返さない
    /// （振りかぶりでは峰が先、斬り下ろしでは刃が先になる）。動きが遅い間は前の向きを保つ。<paramref name="edges"/> を書き換える。
    /// </summary>
    /// <param name="blades">剣の向き（フレームごと）。</param>
    /// <param name="tipVelocities">剣の先の速さ（フレームごと、任意の単位）。</param>
    /// <param name="edges">刃の向き（最初の値が始まりの向き。ほかは上書き）。</param>
    /// <param name="moving">これより速い先の動きで、刃筋を動きの面へ合わせる。</param>
    /// <param name="follow">1 フレームで合わせる割合（0..1）。</param>
    public static void FollowCut(IReadOnlyList<Vector3> blades, IReadOnlyList<Vector3> tipVelocities, Vector3[] edges, float moving, float follow)
    {
        ArgumentNullException.ThrowIfNull(blades);
        ArgumentNullException.ThrowIfNull(tipVelocities);
        ArgumentNullException.ThrowIfNull(edges);
        var edge = Perpendicular(edges[0], blades[0]);
        for (var i = 0; i < blades.Count; i++)
        {
            var blade = blades[i];
            edge = Perpendicular(edge, blade);
            var v = tipVelocities[i];
            var across = v - (Vector3.Dot(v, blade) * blade);
            if (across.Length() > moving)
            {
                var want = Vector3.Normalize(across);
                if (Vector3.Dot(want, edge) < 0f)
                {
                    want = -want; // 刃筋の面に合わせるだけで、裏返さない
                }

                edge = Perpendicular(Vector3.Lerp(edge, want, follow), blade);
            }

            edges[i] = edge;
        }
    }

    /// <summary><paramref name="v"/> の、<paramref name="axis"/> に垂直な向き（単位ベクトル。垂直な分が無ければ、下向きか前向きから作る）。</summary>
    public static Vector3 Perpendicular(Vector3 v, Vector3 axis)
    {
        var p = v - (Vector3.Dot(v, axis) * axis);
        if (p.LengthSquared() > 1e-8f)
        {
            return Vector3.Normalize(p);
        }

        var down = -Vector3.UnitY - (Vector3.Dot(-Vector3.UnitY, axis) * axis);
        return down.LengthSquared() > 1e-8f ? Vector3.Normalize(down) : Vector3.UnitZ;
    }
}
