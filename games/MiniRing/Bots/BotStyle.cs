using System.Text.Json;

namespace MiniRing.Bots;

/// <summary>
/// ボットの戦い方（調整できる値の全部）。値は不変で、変えるときは <c>with</c> で新しいスタイルを作る。
/// <para>
/// <b>人並みの制約</b>: 反応時間・守りの成功率・手の速さには上限・下限がある（<see cref="Validated"/>）。
/// 改善（PDCA）は「反射神経を超人にする」ではなく、技の選び方・間合い・守りの選び方で強くならなければならない。
/// </para>
/// </summary>
public sealed record BotStyle
{
    /// <summary>反応時間の下限 [s]（人の目と手の反応のおおよその下限）。</summary>
    public const float MinReactionTime = 0.12f;

    /// <summary>飛んでくる拳に守りを出せる確率の上限（読み違い・見落としがある）。</summary>
    public const float MaxDefenseRate = 0.85f;

    /// <summary>手の速さの倍率の上限（経路の時間を縮める。物理の目標速度の上限もある）。</summary>
    public const float MaxPunchSpeed = 1.15f;

    public string Name { get; init; } = "boxer";

    /// <summary>右が前（サウスポー）。</summary>
    public bool Southpaw { get; init; }

    // ── 攻め ──

    /// <summary>手数（0 = 待つ … 1 = 休まず攻める）。攻撃の間隔を決める。</summary>
    public float Aggression { get; init; } = 0.5f;

    /// <summary>打つときに踏み込む胸と胸の距離（腕の長さ倍）。</summary>
    public float AttackRange { get; init; } = 0.95f;

    /// <summary>普段の胸と胸の距離（腕の長さ倍）。</summary>
    public float NeutralRange { get; init; } = 1.3f;

    /// <summary>攻撃を始めてよい距離（腕の長さ倍）。これより遠ければまず詰める。</summary>
    public float EngageRange { get; init; } = 1.6f;

    /// <summary>手の速さの倍率（経路の時間の逆数）。</summary>
    public float PunchSpeed { get; init; } = 1f;

    /// <summary>コンビネーションの重み（<see cref="Combo.Name"/> → 重み）。載っていない型は使わない。</summary>
    public IReadOnlyDictionary<string, float> ComboWeights { get; init; } = new Dictionary<string, float>
    {
        ["1"] = 2f, ["1-2"] = 3f, ["1-1-2"] = 1f, ["1-2-3"] = 2f, ["2-3-2"] = 1f, ["1b-2"] = 1f, ["3b-3"] = 1f, ["6-3"] = 1f, ["f-2"] = 0.5f,
    };

    /// <summary>
    /// 相手の構えを読む度合い（0..1）。両拳が顔の前にあればストレートを胴へ切り替え、頭を打つときは拳の無い側へ狙いをずらす。
    /// </summary>
    public float ReadGuard { get; init; }

    /// <summary>自分から攻めるか（偽なら守りとカウンターだけ。防御ドリルの受け手に使う）。</summary>
    public bool Attacks { get; init; } = true;

    /// <summary>コンビネーションを打ち終えたあと、間合いの外へ下がる確率。</summary>
    public float ExitAfterCombo { get; init; } = 0.4f;

    /// <summary>コンビネーションを打ち終えたあと、横へ回り込んで角度を取る（ピボット）確率。</summary>
    public float PivotAfterCombo { get; init; }

    /// <summary>相手が踏み込んできたとき、ジャブで止めて間合いを保つ（ストップジャブ）確率。</summary>
    public float StopJab { get; init; }

    /// <summary>掴んで投げに行く確率（近距離の攻撃のうち）。</summary>
    public float ThrowRate { get; init; } = 0.05f;

    // ── 足と頭 ──

    /// <summary>
    /// ロープを背負わない度合い（0..1）。背中側のロープが近いとき、下がる動きをこの割合だけ空いている横への動きに替える。
    /// </summary>
    public float RopeEscape { get; init; }

    /// <summary>相手の周りを回る強さ（0..1）。</summary>
    public float Circling { get; init; } = 0.4f;

    /// <summary>何もしていないときの頭の振り（ボブ・ウィーブ）の大きさ（0..1）。</summary>
    public float HeadMovement { get; init; } = 0.4f;

    /// <summary>構えの高さ（0 = 低い構え（胸）… 1 = あご … <see cref="MaxGuardHeight"/> = 目の高さで顔を覆う）。</summary>
    public float GuardHeight { get; init; } = 0.5f;

    /// <summary>構えの高さの上限（あごの高さを 1 として、拳が目の高さに来る）。</summary>
    public const float MaxGuardHeight = 1.5f;

    /// <summary>溜めの上限 [s]（ボットはこの時間で引ききる）。</summary>
    public const float MaxTelegraph = 0.2f;

    /// <summary>
    /// 打つ前の溜め（予備動作）[s]（0 = 溜めなし、上限 <see cref="MaxTelegraph"/>）。連打の最初の拳の前に、打つ手の肩を後ろへ引き、頭を引いて膝を沈め
    /// （体のバネを縮める）、そのあと拳が出る。打ち出すと縮めたバネの反発と体の動きが拳に移る（場合分けせず物理で: 長く溜めるほど深く引けて、拳が速く重い）。
    /// 体は拳より先に動くので、相手はそれを読んで避ける。狙いは溜めの始めに決まる。長い溜めは威力が出る代わりに読まれやすく、時間もかかる。
    /// </summary>
    public float Telegraph { get; init; }

    /// <summary>構えの拳の左右の開き（1 = 標準 … 小さいほど両拳を顔の前で寄せる）。</summary>
    public float GuardWidth { get; init; } = 1f;

    /// <summary>構えの拳の前への出し（1 = 標準 … 小さいほど拳を顔に近づける）。</summary>
    public float GuardReach { get; init; } = 1f;

    // ── 守り ──

    /// <summary>拳が向かってくるのに気づいてから守りを出すまで [s]。</summary>
    public float ReactionTime { get; init; } = 0.18f;

    /// <summary>
    /// 動き出しを読む度合い（0..1）。0 は拳が速く向かってきてから気づく（近い間合いでは反応時間が間に合わない）。
    /// 1 に近いほど、拳が相手の体に対して動き出した早い段階で気づく。代わりにフェイントや構えの揺れにも反応しやすい。
    /// 反応時間（<see cref="ReactionTime"/>）は気づいてから数える。
    /// </summary>
    public float Anticipation { get; init; }

    /// <summary>
    /// 予測で避ける度合い（0..1、0 = 予測しない）。試合の中で相手の間合い・リズム・打つ前の体の動き・癖を覚え
    /// （<see cref="OpponentRead"/>）、拳が出る前に「まもなく来る」と見積もった時に守りを始める。大きいほど低い確からしさでも
    /// 動き出す（早く避けられるが、フェイントや頭の振りにも釣られ、空振りの守りで自分の攻めを止める）。
    /// </summary>
    public float Prediction { get; init; }

    /// <summary>
    /// 予測で動き出すのに要る、体の手がかり（ひねり・前傾）の証拠の強さ（尤度比の対数、0..3）。0 なら体が「打たない」側を示していなければ動く。
    /// 大きいほど、間合いやリズムだけでは動かず、打つ前の体の動きを見てから動く（早すぎる予測で短い回避が拳より先に終わるのを防ぐ）。
    /// </summary>
    public float BodyCue { get; init; }

    /// <summary>
    /// 相手の届く間合いのすぐ外に立つ度合い（0..1）。覚えた相手の打ち始めの間合いの少し外を普段の間合いにし、
    /// 相手に踏み込ませる（踏み込みは読める手がかりになり、その間に避ける）。
    /// </summary>
    public float EdgeRange { get; init; }

    /// <summary>
    /// 避ける拳の届く範囲（腕の長さ倍、0 = 見ない）。相手の肩から自分の頭までがこれより遠い拳は、届かないとみて避けない。
    /// </summary>
    public float EvadeReach { get; init; }

    /// <summary>気づいた拳に守りを出す確率。</summary>
    public float DefenseRate { get; init; } = 0.55f;

    public float BlockWeight { get; init; } = 1f;
    public float DuckWeight { get; init; } = 0.5f;
    public float SlipWeight { get; init; } = 0.5f;
    public float SwayWeight { get; init; } = 0.5f;
    public float StepBackWeight { get; init; } = 0.5f;
    public float WeaveWeight { get; init; }
    public float SideStepWeight { get; init; }
    public float SlipInWeight { get; init; }
    public float RollWeight { get; init; }

    /// <summary>防御技それぞれの動き（頭・足・拳）。防御ドリルで最適化した値を持つ。</summary>
    public DefenseBook Defenses { get; init; } = DefenseBook.Default;

    /// <summary>守り切ったあとにすぐ打ち返す（カウンター）確率。</summary>
    public float CounterRate { get; init; } = 0.5f;

    /// <summary>続けて打たれたときに組み付いて（クリンチ）止める確率。</summary>
    public float ClinchRate { get; init; } = 0.2f;

    [System.Text.Json.Serialization.JsonIgnore]
    public Hand Lead => Southpaw ? Hand.Right : Hand.Left;

    public float Weight(Defense defense) => defense switch
    {
        Defense.Block => BlockWeight,
        Defense.Duck => DuckWeight,
        Defense.Slip => SlipWeight,
        Defense.Sway => SwayWeight,
        Defense.StepBack => StepBackWeight,
        Defense.Weave => WeaveWeight,
        Defense.SideStep => SideStepWeight,
        Defense.SlipIn => SlipInWeight,
        Defense.Roll => RollWeight,
        _ => 0f,
    };

    /// <summary>人並みの制約と値域に収めたスタイル。未知のコンビネーション名があれば例外。</summary>
    public BotStyle Validated()
    {
        // JSON で null を書かれても落ちないよう、既定へ戻す（壊れたリプレイの見出しは InvalidDataException として報告される）。
        var comboWeights = ComboWeights ?? new Dictionary<string, float>();
        var name = string.IsNullOrWhiteSpace(Name) ? "boxer" : Name;
        foreach (var combo in comboWeights.Keys)
        {
            _ = Combo.Named(combo);
        }

        static float Unit(float v) => float.IsFinite(v) ? Math.Clamp(v, 0f, 1f) : 0f;
        static float NonNegative(float v) => float.IsFinite(v) ? MathF.Max(0f, v) : 0f;
        static float Range(float v, float min, float max, float fallback) => Math.Clamp(float.IsFinite(v) ? v : fallback, min, max);
        var attack = Range(AttackRange, 0.7f, 1.3f, 0.95f);
        var neutral = Range(NeutralRange, attack, 1.9f, 1.3f);
        return this with
        {
            Aggression = Unit(Aggression),
            AttackRange = attack,
            NeutralRange = neutral,
            EngageRange = Range(EngageRange, neutral, 2.2f, 1.6f),
            PunchSpeed = Range(PunchSpeed, 0.8f, MaxPunchSpeed, 1f),
            Name = name,
            ComboWeights = comboWeights.ToDictionary(p => p.Key, p => NonNegative(p.Value)),
            ReadGuard = Unit(ReadGuard),
            ExitAfterCombo = Unit(ExitAfterCombo),
            PivotAfterCombo = Unit(PivotAfterCombo),
            StopJab = Unit(StopJab),
            ThrowRate = Unit(ThrowRate),
            Circling = Unit(Circling),
            RopeEscape = Unit(RopeEscape),
            HeadMovement = Unit(HeadMovement),
            GuardHeight = Range(GuardHeight, 0f, MaxGuardHeight, 0.5f),
            GuardWidth = Range(GuardWidth, 0.2f, 1.5f, 1f),
            Telegraph = Range(Telegraph, 0f, MaxTelegraph, 0f),
            GuardReach = Range(GuardReach, 0.3f, 1.5f, 1f),
            ReactionTime = Range(ReactionTime, MinReactionTime, 0.5f, 0.5f),
            DefenseRate = Range(DefenseRate, 0f, MaxDefenseRate, 0f),
            BlockWeight = NonNegative(BlockWeight),
            DuckWeight = NonNegative(DuckWeight),
            SlipWeight = NonNegative(SlipWeight),
            SwayWeight = NonNegative(SwayWeight),
            StepBackWeight = NonNegative(StepBackWeight),
            WeaveWeight = NonNegative(WeaveWeight),
            SideStepWeight = NonNegative(SideStepWeight),
            SlipInWeight = NonNegative(SlipInWeight),
            RollWeight = NonNegative(RollWeight),
            Defenses = (Defenses ?? DefenseBook.Default).Validated(),
            CounterRate = Unit(CounterRate),
            Anticipation = Unit(Anticipation),
            Prediction = Unit(Prediction),
            BodyCue = Range(BodyCue, 0f, 3f, 0f),
            EdgeRange = Unit(EdgeRange),
            EvadeReach = float.IsFinite(EvadeReach) && EvadeReach > 0f ? Math.Clamp(EvadeReach, 0.8f, 2f) : 0f,
            ClinchRate = Unit(ClinchRate),
        };
    }

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static BotStyle FromJson(string json)
        => (JsonSerializer.Deserialize<BotStyle>(json, JsonOptions) ?? throw new JsonException("空のスタイルです。")).Validated();

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
}
