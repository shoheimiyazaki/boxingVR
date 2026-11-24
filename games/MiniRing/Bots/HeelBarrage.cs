using System.Numerics;

namespace MiniRing.Bots;

/// <summary>
/// 倒れた相手へのかかとの踏み下ろしの連打（2026-11-24、ユーザーの指定「30cm ぐらいかかとを上げて、頭などの目標に向かって蹴り下ろす運動を出来る限り早く繰り返す」）。
/// かかとの制御（<see cref="LegCommand.Heel"/>）の目標の経路だけを書く: 体重を軸足へ寄せながら、かかとを狙いの真上（<see cref="GroundMove.Rise"/>、
/// 基準の体格で約 35 cm。ユーザーの指定は 30 cm ほど）へ上げ、つま先を起こした足（<see cref="Pitch"/>）のかかとで狙いの少し奥（<see cref="GroundMove.Overshoot"/>）まで踏み下ろし、
/// また真上へ戻す。これを <see cref="Count"/> 回、1 回 <see cref="GroundMove.Duration"/> 秒で繰り返す。
/// <para>
/// 速さと重さは、脚の筋肉が経路をどれだけ追えるか（PD の力の上限 = 脚の筋肉の断面 × 酸素 × 張り）と、体重を乗せた結果として物理から出る。
/// 威力の倍率は持たない。狙い（<c>aimLocal</c>）は毎歩、相手の見える頭・首の位置から出し直す（頭が動けば追う）。技の種類では分けない。
/// 長さは腕の長さ = 1（<see cref="GroundMove"/> と同じ）、時間は秒。値は教科書・教育（<see cref="GroundTuning.HeelBarrage"/>）。
/// </para>
/// </summary>
/// <param name="Count">踏み下ろす回数。</param>
/// <param name="DownShare">1 回のうち、踏み下ろす（真上 → 狙い）区間の割合。残りで真上へ戻す。</param>
/// <param name="Chamber">構えからかかとを最初の真上へ上げる時間 [s]（体重を軸足へ寄せる間）。</param>
/// <param name="Recover">最後の踏み下ろしの後、足を構えへ戻す時間 [s]。</param>
/// <param name="Pitch">足の向き: つま先を起こす角 [rad]（かかとが先に当たる。0 = 足裏を水平）。</param>
/// <param name="Drop">踏み下ろす間に腰（頭の目標）を落とす量 [腕の長さ]。上げる間に戻す。落ちる体の重さが踏む脚に乗る（物理の結果）。</param>
/// <param name="SwitchMargin">
/// 踏み替えの差: 踏んでいる脚の酸素の蓄え（0..1、自分が感じる脚の疲れ）が、軸足の蓄えよりこれだけ少なくなったら、足を上げきった所で反対の足に替える。
/// 替えた後は新しい足がこれだけ減るまで替えない（行ったり来たりしない）。
/// </param>
public sealed record HeelBarrage(int Count = 6, float DownShare = 0.10f, float Chamber = 0.25f, float Recover = 0.30f, float Pitch = 0.339f, float Drop = 0f,
    float SwitchMargin = 0.10f)
{
    public static HeelBarrage Default { get; } = new();

    /// <summary>教科書（JSON）から来た値を、動かせる範囲へ丸める（数でない値は既定へ）。</summary>
    public HeelBarrage Validated()
    {
        var d = Default;
        static float R(float v, float min, float max, float f) => Math.Clamp(float.IsFinite(v) ? v : f, min, max);
        return this with
        {
            Count = Math.Clamp(Count, 1, 30),
            DownShare = R(DownShare, 0.1f, 0.9f, d.DownShare),
            Chamber = R(Chamber, 0.05f, 2f, d.Chamber),
            Recover = R(Recover, 0.05f, 2f, d.Recover),
            Pitch = R(Pitch, -0.6f, 0.8f, d.Pitch),
            Drop = R(Drop, 0f, 0.5f, d.Drop),
            SwitchMargin = R(SwitchMargin, 0f, 1f, d.SwitchMargin),
        };
    }

    /// <summary>連打の全体の時間 [s]（上げる + 回数 × 1 回の時間 + 戻す）。</summary>
    public float Length(GroundMove move) => Chamber + (Count * move.Duration) + Recover;

    /// <summary>何回目の踏み下ろしの中か（0 から。上げる前・戻した後は -1）。</summary>
    public int Stroke(GroundMove move, float time)
    {
        var k = (int)MathF.Floor((time - Chamber) / move.Duration);
        return time < Chamber || k >= Count ? -1 : k;
    }

    /// <summary>
    /// 踏む足 <paramref name="foot"/> と反対の足のうち、踏むのに使う足: 踏む脚の蓄えが反対の脚より <see cref="SwitchMargin"/> 以上少なければ反対の足、そうでなければそのまま。
    /// </summary>
    public Hand Fresher(in FighterSense sense, Hand foot)
    {
        var other = foot == Hand.Left ? Hand.Right : Hand.Left;
        return sense.SelfLegOxygen(foot) + SwitchMargin < sense.SelfLegOxygen(other) ? other : foot;
    }

    /// <summary>腰を落とす割合 0..1: 踏み下ろす間にかかとと同じ形（u²）で落とし、上げる間に戻す。連打の外は 0。</summary>
    public float DropAt(GroundMove move, float time)
    {
        if (Stroke(move, time) < 0)
        {
            return 0f;
        }

        var u = ((time - Chamber) / move.Duration) % 1f;
        return u < DownShare ? (u / DownShare) * (u / DownShare) : 1f - Smooth((u - DownShare) / (1f - DownShare));
    }

    /// <summary>体重を軸足へ寄せる割合 0..1: 上げる間に満ち、踏み続ける間は 1、戻す間に抜ける。</summary>
    public float Weight(GroundMove move, float time)
    {
        var end = Chamber + (Count * move.Duration);
        return time < Chamber ? Smooth(Math.Clamp(time / Chamber, 0f, 1f))
            : time < end ? 1f
            : 1f - Smooth(Math.Clamp((time - end) / Recover, 0f, 1f));
    }

    /// <summary>
    /// <paramref name="time"/>（連打を始めてから [s]。負なら溜めの間で構えのまま）での、かかとの制御の指令（体フレーム）。
    /// <paramref name="rest"/> は構えの足の位置、<paramref name="kneeLift"/> は膝を向けたい点をかかとの真上の点からずらす量。
    /// </summary>
    public LegCommand Leg(GroundMove move, float time, Vector3 aimLocal, Vector3 rest, Vector3 kneeLift, float reach)
    {
        var contact = aimLocal - new Vector3(0f, move.Overshoot * reach, 0f);
        var top = aimLocal + new Vector3(0f, move.Rise * reach, 0f);
        var end = Chamber + (Count * move.Duration);
        Vector3 heel;
        if (time < Chamber)
        {
            heel = Vector3.Lerp(rest, top, Smooth(Math.Clamp(time / Chamber, 0f, 1f)));
        }
        else if (time < end)
        {
            var u = ((time - Chamber) / move.Duration) % 1f;
            if (u < DownShare)
            {
                var s = u / DownShare;
                heel = Vector3.Lerp(top, contact, s * s); // 初めはゆっくり、最後に速く（落とす）
            }
            else
            {
                heel = Vector3.Lerp(contact, top, Smooth((u - DownShare) / (1f - DownShare)));
            }
        }
        else
        {
            heel = Vector3.Lerp(top, rest, Smooth(Math.Clamp((time - end) / Recover, 0f, 1f)));
        }

        // つま先を起こす（足の X 軸まわりに負: 正はつま先が下がる）。上げる間に起こし、戻す間に戻す。
        var foot = Quaternion.CreateFromAxisAngle(Vector3.UnitX, -Pitch * Weight(move, time));
        return LegCommand.Heel(heel, foot, top + kneeLift);
    }

    private static float Smooth(float t) => t * t * (3f - (2f * t));
}
