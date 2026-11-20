namespace MiniRing.Weapons;

/// <summary>
/// 手の関節の動き: 関節ごとのバネ・ダンパ（サーボ）で、目標の曲げ角（手形の関節角）を追う。位置を直接書き換えない（体と同じ §1.1）ので、
/// 手形を変えるには時間がかかり（<see cref="HandSkeleton.FingerSettleSeconds"/>）、骨が欠けた指は力が入らず、力を抜いた形（<see cref="HandSkeleton.RestFlexion"/>）へ落ちる。
/// <para>
/// 関節 j のトルク = <c>固さ × 骨の強さ × (目標 − 角度) + 受け身の固さ × (力を抜いた角度 − 角度)</c> − 減衰 × 角速度。固さ（<see cref="HandSkeleton.Stiffness"/>）は
/// 関節より先のセルの慣性と、落ち着く時間から。骨の強さ（0..1）はその節の骨のセルのうち最も傷んだものの残り（骨は直列に力を伝えるので、1 か所の欠けが節全体の強さを決める。
/// 全部の骨のセルが残っていれば 1、1 つでも無くなれば 0 = 折れている）。減衰は臨界。陰解法で、歩幅によらず安定。
/// </para>
/// </summary>
internal sealed class HandMotion
{
    private readonly float[] _angle;
    private readonly float[] _speed = new float[HandPose.JointCount];

    public HandMotion(HandPose start) => _angle = start.ToArray();

    /// <summary>いまの関節の姿勢。</summary>
    public HandPose Pose => new(_angle);

    /// <summary>目標の角度を、関節の動く範囲（<see cref="HandSkeleton.Range"/>）に収める。</summary>
    private static float[] Clamped(HandPose pose)
    {
        var angles = pose.ToArray();
        for (var j = 0; j < angles.Length; j++)
        {
            var (min, max) = HandSkeleton.Range(j);
            angles[j] = Math.Clamp(angles[j], min, max);
        }

        return angles;
    }

    /// <summary>目標の姿勢へ一気に（速さ 0 で）置く。</summary>
    public void Snap(HandPose target)
    {
        Array.Copy(Clamped(target), _angle, _angle.Length);
        Array.Clear(_speed);
    }

    /// <summary>
    /// <paramref name="dt"/> 秒進める。<paramref name="strength"/> は関節ごとの骨の強さ（0..1。<see cref="HandPose"/> の順。手首は掌の骨）。
    /// </summary>
    /// <returns>全部の関節がつり合いの角度に落ち着いたか（目標のずれ・角速度がともに十分小さい関節は、つり合いの角度にぴったり置く）。</returns>
    public bool Step(HandPose target, float dt, IReadOnlyList<float> strength)
    {
        var goal = Clamped(target);
        var settled = true;
        for (var j = 0; j < _angle.Length; j++)
        {
            var rest = j < 2 ? 0f : HandSkeleton.RestFlexion[(j - 2) % 3];
            var k = HandSkeleton.Stiffness(j);
            var active = k * Math.Clamp(strength[j], 0f, 1f);
            var passive = k * HandSkeleton.PassiveShare;
            var total = active + passive;
            var equilibrium = ((active * goal[j]) + (passive * rest)) / total;
            var inertia = HandSkeleton.Inertia[j];
            var damping = 2f * MathF.Sqrt(total * inertia);
            _speed[j] = (_speed[j] + (dt * total / inertia * (equilibrium - _angle[j]))) / (1f + (dt * damping / inertia) + (dt * dt * total / inertia));
            _angle[j] += dt * _speed[j];
            if (MathF.Abs(_angle[j] - equilibrium) < SettledAngle && MathF.Abs(_speed[j]) < SettledSpeed)
            {
                // 落ち着いた関節は、つり合いの角度にぴったり置く（いつまでも 0.01 rad 手前を漂わない。置き場が姿勢だけで決まるように）。
                _angle[j] = equilibrium;
                _speed[j] = 0f;
            }
            else
            {
                settled = false;
            }
        }

        return settled;
    }

    /// <summary>落ち着いたとみなす角度のずれ [rad]（セルが 1 つ動くほどの角度の 1/10: 2 mm / 16 mm の指の長さ ÷ 10）。</summary>
    private const float SettledAngle = 0.0125f;

    /// <summary>落ち着いたとみなす角速度 [rad/s]（0.0125 rad を落ち着く時間 0.092 s で動く速さのおよそ 1/5）。</summary>
    private const float SettledSpeed = 0.02979f;
}
