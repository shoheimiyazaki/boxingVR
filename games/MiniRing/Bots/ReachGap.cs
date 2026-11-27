using System.Numerics;

namespace MiniRing.Bots;

/// <summary>
/// 届く間合い（2026-11-27、LogicVersion 仮）: ボットの拳の間合いの物差し。胸と胸の距離ではなく、<b>両肩それぞれから相手のあごまでの床の上の（水平な）距離の平均</b>で測る。
/// 水平に測るのは、足運びが床の上の位置を決めるから: 高さを含めると、倒れた相手（あごが床の上）や倒れた自分（肩が床の上）で届かない高さの分を歩いて詰めようとする
/// （3 次元で測った版では、倒れた体が起き上がれず・倒れた相手を踏めなかった: DamageModelTests・HeelStompTests）。
/// 腕の長さで割った値が、スタイルの <see cref="BotStyle.AttackRange"/>・<see cref="BotStyle.NeutralRange"/>・<see cref="BotStyle.EngageRange"/> の単位。
/// 胸と胸の物差しは、頭と胸の深さ・頭の大きさ・肩の幅が違う体どうしで同じ「腕の倍」でもあごまでの遠さが変わる（基準の体を 175 cm に置き直したときの問い）。
/// 肩とあごで測れば、体の寸法がどう違っても同じ式で「拳がどこまで届くか」を見る。キャラ・技ごとに分けない。
/// <para>
/// 前の体（145 cm）で磨いた値は、前の体で構えて測った対応（<c>ReachRangeProbe</c>: 胸と胸 c [腕] → 届く間合い 0.9703 c − 0.0130、
/// 間合いの層の前の物差し「相手の胸の 0.25 腕上から自分の頭（3 次元）」 t → 0.9879 t − 0.0580）で移した。新しい体では胸と胸 0.9647 c − 0.0149 で、
/// 同じ値なら前の体より胸と胸で 0.01 腕ほど遠くに立つ（docs/plans/BASE_BODY_PLAN.md §10）。
/// </para>
/// </summary>
public static class ReachGap
{
    /// <summary>自分の両肩それぞれから相手のあごまでの水平な距離の平均 [m]。肩が見えない入力（古い <see cref="FighterSense"/>）は胸から測る。</summary>
    public static float ToOpponent(in FighterSense sense)
    {
        var body = sense.SelfBody;
        var left = body == default ? sense.Self.Origin : body.LeftShoulder;
        var right = body == default ? sense.Self.Origin : body.RightShoulder;
        var chin = sense.OpponentTargets?.Point(AimGoal.Chin, 0.5f * (left + right)) ?? sense.OpponentHead;
        return 0.5f * (Flat(left, chin) + Flat(right, chin));
    }

    /// <summary>相手の両肩それぞれから自分のあごまでの水平な距離の平均 [m]（相手の拳が自分へ届くか）。肩が見えなければ相手の胸の 0.25 腕上、あごが分からなければ頭から測る。</summary>
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
        return 0.5f * (Flat(left, chin) + Flat(right, chin));
    }

    private static float Flat(Vector3 a, Vector3 b) => Vector3.Distance(a with { Y = 0f }, b with { Y = 0f });
}
