using System.Numerics;

namespace MiniRing.Bots;

/// <summary>
/// パンチの種類。ボクシングの番号で 1 = ジャブ、2 = ストレート（クロス）、3 = リードフック、4 = リアフック、
/// 5 = リードアッパー、6 = リアアッパー。「リード」は前の手（オーソドックスなら左）、「リア」は後ろの手。
/// </summary>
public enum Technique
{
    Jab,
    Cross,
    LeadHook,
    RearHook,
    LeadUppercut,
    RearUppercut,
    BodyJab,
    BodyCross,
    LeadBodyHook,
    RearBodyHook,
    Overhand,

    /// <summary>ジャブを途中で引く見せかけ（相手の守りを引き出す）。当てるつもりはない。</summary>
    Feint,
}

/// <summary>
/// 1 つのパンチの経路と体の使い方（長さは腕の長さ単位）。経路は <see cref="PunchPath"/>（構え → 溜め → 狙いの奥）で、
/// 溜めを横に取ればフック、下に取ればアッパー、上に取ればオーバーハンドになる。強さは決めない（物理が決める）。
/// </summary>
/// <param name="Lead">前の手で打つか。</param>
/// <param name="Lateral">溜めの横の大きさ（0 = まっすぐ … 1 = 大きく横から）。</param>
/// <param name="Low">溜めの低さ（0 = 構えの高さ、1 = 大きく下から、負 = 上から）。</param>
/// <param name="Overshoot">狙いの奥へ打ち抜く量。</param>
/// <param name="Duration">打ち出して構えに戻るまでの時間 [s]（速さ 1 のとき）。</param>
/// <param name="ToBody">胴を狙うか（偽なら頭）。</param>
/// <param name="HeadShift">打つ間の頭の動き（X = リード側へ、Y = 上、Z = 前）。体重移動・レベルチェンジ。</param>
/// <param name="StepIn">打つ間の踏み込み（0..1、移動指令の前向き成分）。</param>
/// <param name="Extend">経路のどこまで出すか（1 = 打ち抜く、フェイントは途中で引く）。</param>
/// <param name="Sweep">狙いを打つ手と反対の側へ振り抜く量（フックが相手の顔を横切る）。</param>
public readonly record struct PunchSpec(
    bool Lead, float Lateral, float Low, float Overshoot, float Duration, bool ToBody, Vector3 HeadShift, float StepIn, float Extend = 1f, float Sweep = 0f)
{
    /// <summary>腕の振り出しが全体に占める割合（残りで構えへ戻す）。</summary>
    public const float OutboundShare = 0.6f;

    /// <summary>
    /// 拳の目標が打ち抜く先へ届くまでの、振り出しに占める割合。目標は経路を先回りし（<see cref="PunchPath.MinimumJerk"/> の速さの山で）、
    /// 拳はそれを肩・肘の筋肉の力で追う: 拳の速さは予定の時間ではなく、筋肉の力・加速できる距離・体のバネのタメで決まる。
    /// 0.3（右ストレート 0.4 s で 0.072 s）は、既定の右ストレートの行程（構え → 打ち抜く先 約 1 腕）で目標の速さの山（平均の 1.875 倍）が目標の速度の上限
    /// <see cref="FighterTuning.MaxTargetSpeed"/>（6 m/s）を越えない短さ（時間の短い技・行程の長い技では山が上限を越え、目標の速度のフィードフォワードがそこで切れる）。これより短くしても拳は速くならない（punchlab、届く間合いの右ストレートの打ち出しの最高速:
    /// 0.1 / 0.15 / 0.2 / 0.3 で 3.6 / 3.6 / 3.6 / 3.4 m/s、当たった瞬間 3.3 / 3.3 / 3.2 / 3.3 m/s。拳のバネが追える速さで頭打ち）。0.5 は 2.9 m/s。
    /// 前の値（0.5、目標は等速）は 2.0 m/s だった（docs/BOXING.md 1-7）。
    /// </summary>
    public const float LaunchShare = 0.3f;

    /// <summary>打ち出しから、拳の目標が打ち抜く先へ届くまでの、技の時間に占める割合（<see cref="OutboundShare"/> × <see cref="LaunchShare"/>）。</summary>
    public const float ArrivalShare = OutboundShare * LaunchShare;

    /// <summary>経路の曲がり（0 = 構えから狙いへまっすぐ … 1 = 溜めの点を通る）。溜めの横・下の大きさで決める（0.5 以上で 1）。</summary>
    public float Curve => Math.Clamp(MathF.Max(Lateral, MathF.Abs(Low)) / 0.5f, 0f, 1f);

    /// <summary>既定の技表（<see cref="PunchBook.Default"/>）の値。スタイルごとの値は <see cref="BotStyle.Punches"/>。</summary>
    public static PunchSpec Of(Technique technique) => PunchBook.Default.Of(technique);

    /// <summary>
    /// 時刻 <paramref name="time"/> の拳の目標（体フレームのローカル）。前半で <see cref="PunchPath"/> を打ち出し、後半で構えへ戻す。
    /// 経路の上の進み具合は時間の最小躍度の曲線（<see cref="PunchPath.MinimumJerk"/>）: 目標の速さは打ち出しの途中で山になり、打ち抜く先・構えで 0 に戻る。
    /// 技の種類で形を分けず、どの技も同じ式で、違いは時間（<see cref="Duration"/>）と行程（溜め・打ち抜き）だけ。
    /// </summary>
    public Vector3 Evaluate(Hand hand, float time, float duration, Vector3 guard, Vector3 aim, float reach)
    {
        var t = Math.Clamp(time / duration, 0f, 1f);
        var through = PunchPath.Through(aim, Overshoot, reach) - new Vector3(hand.LocalSide() * Sweep * reach, 0f, 0f);
        // 溜めの小さい拳（ジャブ・ストレート）は、構えから狙いへまっすぐ出す。溜めの点をそのまま使うと、右ストレートが
        // 外側の下から弧を描いて来て、頭の横下へ外す回避に自分から当たりに行っていた（基本動作編 B9）。
        var straight = (guard + through) * 0.5f;
        var windup = Vector3.Lerp(straight, PunchPath.Windup(hand, Lateral, Low, reach), Curve);
        // フェイント（Extend < 1）は同じ曲線を途中までの行程に縮める（途中で切ると、速さの山の近くで目標が急に止まる。レビュー指摘）。
        var outbound = PunchPath.Point(guard, windup, through, Extend * PunchPath.MinimumJerk(t / ArrivalShare));
        return Vector3.Lerp(outbound, guard, PunchPath.MinimumJerk((t - OutboundShare) / (1f - OutboundShare)));
    }
}

/// <summary>守りの種類。</summary>
public enum Defense
{
    None,

    /// <summary>両グローブを顔（胴打ちなら脇腹）の前へ上げて受ける。</summary>
    Block,

    /// <summary>膝を沈めて頭を下げ、フックやストレートの下をくぐる。</summary>
    Duck,

    /// <summary>頭を U 字に沈めながら反対側へ抜ける（ウィービング）。フックの下をくぐって外へ出る。</summary>
    Weave,

    /// <summary>頭を横へずらして、まっすぐ来る拳の線から外す。</summary>
    Slip,

    /// <summary>上体を後ろへ反らして（スウェーバック）拳の届く先から頭を引く。</summary>
    Sway,

    /// <summary>足で下がって間合いを外す。</summary>
    StepBack,

    /// <summary>足で横へ出て、拳の線から外れて角度を取る。</summary>
    SideStep,

    /// <summary>インサイドスリップ: 頭を拳の線から外しながら半歩踏み込み、カウンターの届く距離に入る。</summary>
    SlipIn,

    /// <summary>ローリング: 拳の進む向きへ頭を沈めながら流し、当たっても衝撃を逃がす。</summary>
    Roll,
    Parry,
}

/// <summary>
/// コンビネーション（連打）。<see cref="Steps"/> の各パンチは、前のパンチを始めてから <see cref="Gap"/> × そのパンチの時間
/// だけ後に始める（同じ手が続くときはその手が空くまで待つ）。
/// </summary>
public sealed record Combo(string Name, IReadOnlyList<Technique> Steps, float Gap = 0.55f)
{
    public bool HasBodyShot => Steps.Any(t => PunchSpec.Of(t).ToBody);

    /// <summary>
    /// 型の一覧（名前はボクシングの番号: 1 ジャブ / 2 ストレート / 3 リードフック / 4 リアフック / 5,6 アッパー / b 胴 / f フェイント）。
    /// </summary>
    public static IReadOnlyList<Combo> All { get; } =
    [
        new("1", [Technique.Jab]),
        new("1-1", [Technique.Jab, Technique.Jab], 0.75f),
        new("1-2", [Technique.Jab, Technique.Cross]),
        new("1-1-2", [Technique.Jab, Technique.Jab, Technique.Cross], 0.7f),
        new("1-2-3", [Technique.Jab, Technique.Cross, Technique.LeadHook]),
        new("1-2-3-2", [Technique.Jab, Technique.Cross, Technique.LeadHook, Technique.Cross]),
        new("2-3-2", [Technique.Cross, Technique.LeadHook, Technique.Cross]),
        new("1b-2", [Technique.BodyJab, Technique.Cross]),
        new("1-2-3b", [Technique.Jab, Technique.Cross, Technique.LeadBodyHook]),
        new("3b-3", [Technique.LeadBodyHook, Technique.LeadHook], 0.9f),
        new("2b-3", [Technique.BodyCross, Technique.LeadHook]),
        new("6-3", [Technique.RearUppercut, Technique.LeadHook]),
        new("5-2", [Technique.LeadUppercut, Technique.Cross]),
        new("6-3-2", [Technique.RearUppercut, Technique.LeadHook, Technique.Cross]),
        new("3-4", [Technique.LeadHook, Technique.RearHook]),
        new("4b-3", [Technique.RearBodyHook, Technique.LeadHook]),
        new("1-oh", [Technique.Jab, Technique.Overhand]),
        new("f-2", [Technique.Feint, Technique.Cross], 0.6f),
        new("f-3", [Technique.Feint, Technique.LeadHook], 0.6f),
        new("f-1-2", [Technique.Feint, Technique.Jab, Technique.Cross], 0.6f),

        // 単発（防御ドリルで 1 種類の拳だけを打たせるため。スタイルにも使える）。
        new("2", [Technique.Cross]),
        new("3", [Technique.LeadHook]),
        new("4", [Technique.RearHook]),
        new("5", [Technique.LeadUppercut]),
        new("6", [Technique.RearUppercut]),
        new("1b", [Technique.BodyJab]),
        new("2b", [Technique.BodyCross]),
        new("3b", [Technique.LeadBodyHook]),
        new("4b", [Technique.RearBodyHook]),

        // 回避からの打ち返しの候補（カウンター研究）。
        new("2-3", [Technique.Cross, Technique.LeadHook]),
        new("3-2", [Technique.LeadHook, Technique.Cross]),
    ];

    public static Combo Named(string name) => All.FirstOrDefault(c => c.Name == name)
        ?? throw new ArgumentException($"コンビネーション '{name}' はありません。", nameof(name));
}
