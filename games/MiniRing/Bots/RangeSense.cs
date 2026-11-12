using System.Numerics;

namespace MiniRing.Bots;

/// <summary>
/// 間合いの層（docs/LEARNING_PLAN.md の R）: 相手の拳が自分の頭へ届きうる所にいるか（危険度、0..1）と、そこに居続けた時間（さらされ）を見る。
/// 打たれてから守るのでは間に合わない（ストレートは構えから当たるまで約 0.05 秒、反応は 0.12 秒以上。D1）ので、危ない所にいること自体を
/// 手がかりにして、構え・頭の動き・当てずっぽうの回避・足運びを変える。どの層もここが出す連続の値を使い、技の名前で分けない。
/// <para>
/// 危険度は位置関係と速さだけから出す: 相手の肩（胸の少し上）から自分の頭までの距離が、相手の届く長さ（腕の長さ × <see cref="BotStyle.RangeThreat"/>）と、
/// 相手が詰めてくる速さ × 自分の反応時間（反応する間に詰められる分）の和より近いほど高い。境目は <see cref="Width"/> の幅でなめらかにつなぐ。
/// 乱数は使わない（決定的）。
/// </para>
/// </summary>
internal sealed class RangeSense
{
    /// <summary>危険度が 0 から 1 へ変わる幅 [腕の長さ]。</summary>
    public const float Width = 0.15f;

    /// <summary>さらされの時定数 [s]: 危ない所を出ると、この速さで抜ける。</summary>
    public const float ExposureDecay = 1.5f;

    /// <summary>いまの危険度（0 = 届かない … 1 = 届く所の内側）。</summary>
    public float Danger { get; private set; }

    /// <summary>さらされ [s]: 自分から打っていない間に危険度を積んだもの（危ない所を出ると <see cref="ExposureDecay"/> で抜ける）。</summary>
    public float Exposure { get; private set; }

    /// <summary>相手の肩から自分の頭までの距離 [m]（調査用）。</summary>
    public float Distance { get; private set; }

    /// <summary>
    /// 1 歩ぶん見る。<paramref name="head"/> は自分の頭（ワールド）、<paramref name="attacking"/> は自分から打っている最中か
    /// （打ちに入っている間は危ない所にいるのが仕事なので、さらされを積まない）。
    /// </summary>
    public void Update(in FighterSense sense, Vector3 head, bool attacking, BotStyle style, float dt)
    {
        var reach = sense.ArmReach;
        var shoulder = sense.OpponentChest + new Vector3(0f, 0.25f * reach, 0f);
        var toMe = head - shoulder;
        var distance = toMe.Length();
        Distance = distance;
        if (!sense.OpponentAlive || distance < 1e-5f || reach <= 0f)
        {
            Danger = 0f;
            Exposure *= MathF.Exp(-dt / ExposureDecay);
            return;
        }

        // 相手が自分の方へ詰めてくる速さ（離れていく分は数えない）。反応する間に詰められる分だけ、届く所が遠くまで伸びる。
        var closing = MathF.Max(0f, Vector3.Dot(sense.OpponentVelocity, toMe / distance));
        var threat = (style.RangeThreat * reach) + (closing * style.ReactionTime);
        var width = Width * reach;
        var t = Math.Clamp((threat + width - distance) / (2f * width), 0f, 1f);
        Danger = t * t * (3f - (2f * t));
        Exposure = attacking
            ? Exposure * MathF.Exp(-dt / ExposureDecay)
            : (Exposure * MathF.Exp(-dt / ExposureDecay)) + (Danger * dt);
    }
}
