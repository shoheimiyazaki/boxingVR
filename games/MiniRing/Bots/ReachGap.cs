using System.Numerics;

namespace MiniRing.Bots;

/// <summary>
/// 届く間合い（2026-11-27、LogicVersion 仮）: ボットの拳の間合いの物差し。胸と胸の距離ではなく、<b>両肩それぞれから相手のあごまでの距離の平均</b>で測る。
/// 腕の長さで割った値が、スタイルの <see cref="BotStyle.AttackRange"/>・<see cref="BotStyle.NeutralRange"/>・<see cref="BotStyle.EngageRange"/> の単位。
/// 胸と胸の物差しは、頭と胸の深さ・頭の大きさ・肩の幅が違う体どうしで同じ「腕の倍」でもあごまでの遠さが変わる（基準の体を 175 cm に置き直したときの問い）。
/// 肩とあごで測れば、体の寸法がどう違っても同じ式で「拳がどこまで届くか」を見る。キャラ・技ごとに分けない。
/// <para>
/// 前の体（145 cm）で磨いた値は、前の体で構えて測った対応（<c>ReachRangeProbe</c>: 胸と胸 c [腕] → 届く間合い 0.9767 c − 0.0111、
/// 間合いの層の前の物差し「相手の胸の 0.25 腕上から自分の頭」 t → 0.947 t + 0.033）で移した。新しい体では胸と胸 0.98 c − 0.002 で、
/// 前の体とほとんど同じ所に立つ（差は 0.01 腕未満。docs/plans/BASE_BODY_PLAN.md §10）。
/// </para>
/// </summary>
public static class ReachGap
{
    /// <summary>自分の両肩それぞれから相手のあごまでの距離の平均 [m]。肩が見えない入力（古い <see cref="FighterSense"/>）は胸から測る。</summary>
    public static float ToOpponent(in FighterSense sense)
    {
        var body = sense.SelfBody;
        var left = body == default ? sense.Self.Origin : body.LeftShoulder;
        var right = body == default ? sense.Self.Origin : body.RightShoulder;
        var chin = sense.OpponentTargets?.Point(AimGoal.Chin, 0.5f * (left + right)) ?? sense.OpponentHead;
        return 0.5f * (Vector3.Distance(left, chin) + Vector3.Distance(right, chin));
    }

    /// <summary>相手の両肩それぞれから自分のあごまでの距離の平均 [m]（相手の拳が自分へ届くか）。肩が見えなければ相手の胸の 0.25 腕上、あごが分からなければ頭から測る。</summary>
    public static float FromOpponent(in FighterSense sense)
    {
        var body = sense.OpponentBody;
        var reach = sense.OpponentArmReach > 0f ? sense.OpponentArmReach : sense.ArmReach;
        var estimate = sense.OpponentChest + new Vector3(0f, 0.25f * reach, 0f);
        var left = body == default ? estimate : body.LeftShoulder;
        var right = body == default ? estimate : body.RightShoulder;
        var chin = sense.SelfChin != default ? sense.SelfChin
            : sense.SelfHead != default ? sense.SelfHead
            : sense.Self.Origin + new Vector3(0f, 0.3f * sense.ArmReach, 0f);
        return 0.5f * (Vector3.Distance(left, chin) + Vector3.Distance(right, chin));
    }
}
