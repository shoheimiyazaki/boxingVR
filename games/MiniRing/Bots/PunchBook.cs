using System.Numerics;
using System.Text.Json.Serialization;

namespace MiniRing.Bots;

/// <summary>調整してよい 1 つの値の名前と範囲。<c>Validated()</c> と、遺伝子・ゆらぎ（<c>Mutation</c>）が同じ表を参照する（範囲は 1 か所）。</summary>
public readonly record struct ValueRange(string Name, float Min, float Max)
{
    /// <summary>範囲へ収める。有限でない値は <paramref name="fallback"/>（既定の値）へ戻してから収める。</summary>
    public float Clamp(float value, float fallback) => Math.Clamp(float.IsFinite(value) ? value : fallback, Min, Max);
}

/// <summary>
/// 拳の技の値の範囲（<see cref="PunchMove"/> を <see cref="PunchBook.Validated"/> で収める幅）。物理として意味のある幅で、根拠を横に書く。
/// </summary>
public static class PunchRanges
{
    /// <summary>溜めの横。1.2 は肘を大きく外へ開いた大振り。それ以上は肩の可動域を越えて目標が体に届かない。</summary>
    public static ValueRange Lateral { get; } = new(nameof(PunchMove.Lateral), 0f, 1.2f);

    /// <summary>溜めの低さ（負 = 上から）。−1 は頭上からのオーバーハンド、1.2 は膝の高さからのアッパー。</summary>
    public static ValueRange Low { get; } = new(nameof(PunchMove.Low), -1f, 1.2f);

    /// <summary>狙いの奥へ打ち抜く量。0.6 腕長（腕を伸ばしきった先）を越える目標は体が追えない。</summary>
    public static ValueRange Overshoot { get; } = new(nameof(PunchMove.Overshoot), 0f, 0.6f);

    /// <summary>
    /// 打ち出して戻るまでの時間 [s]。0.8 s より長いと的になる。下限 0.18 s: 振り出しの目標は技の時間の <see cref="PunchSpec.ArrivalShare"/>（0.18）で打ち抜く先へ着くので、
    /// 0.18 s では 0.032 s。到着が 0.07 s 以下では拳の速さは拳のバネの追える速さで頭打ちで（右ストレートで到着 0.024〜0.072 s の最高速が 3.4〜3.7 m/s、docs/BOXING.md 1-7）、もっと短くしても拳は速くならず、
    /// 戻りと次の拳が早まるだけになる（連打の間隔は人並みの範囲に留める）。
    /// </summary>
    public static ValueRange Duration { get; } = new(nameof(PunchMove.Duration), 0.18f, 0.8f);

    /// <summary>打つ間の頭のずれ（腕長単位）。±0.4 は体幹を大きく傾けた量で、それ以上は首・腰が追えず倒れる。</summary>
    public static ValueRange HeadShiftX { get; } = new(nameof(PunchMove.HeadShiftX), -0.4f, 0.4f);

    public static ValueRange HeadShiftY { get; } = new(nameof(PunchMove.HeadShiftY), -0.4f, 0.4f);

    public static ValueRange HeadShiftZ { get; } = new(nameof(PunchMove.HeadShiftZ), -0.4f, 0.4f);

    /// <summary>打つ間の踏み込み（移動指令の前向き成分。0..1 は指令の大きさの範囲そのもの）。</summary>
    public static ValueRange StepIn { get; } = new(nameof(PunchMove.StepIn), 0f, 1f);

    /// <summary>反対の側へ振り抜く量。0.6 腕長は相手の顔を完全に横切る幅。</summary>
    public static ValueRange Sweep { get; } = new(nameof(PunchMove.Sweep), 0f, 0.6f);

    /// <summary>
    /// フェイントだけが途中で引く割合。ほかの技は 1（打ち抜く）に固定する: <c>TechniqueExecution.Spring</c> は Extend ≥ 1 のときだけ体のバネを拳へ移すので、
    /// 1 未満にすると「フェイント化」と「移送の切れ目」が混ざる。
    /// </summary>
    public static ValueRange FeintExtend { get; } = new(nameof(PunchMove.Extend), 0.2f, 0.6f);

    /// <summary>進化・ゆらぎの対象にする値の表（名前と範囲）。</summary>
    public static IReadOnlyList<ValueRange> Tunable { get; } =
        [Lateral, Low, Overshoot, Duration, HeadShiftX, HeadShiftY, HeadShiftZ, StepIn, Sweep];
}

/// <summary>
/// 1 つの拳の技の値（<see cref="PunchSpec"/> の JSON 用の写し）。<see cref="PunchSpec"/> は引数の既定値と <see cref="Vector3"/> があり、
/// そのままでは JSON に読み書きできない（<c>Vector3</c> のフィールドは出ず、引数の既定は欠けると 0 になる）ので、init プロパティの class で持つ。
/// </summary>
public sealed record PunchMove
{
    /// <summary>前の手で打つか。技の正体なので <see cref="PunchBook.Validated"/> が既定へ戻す。</summary>
    public bool Lead { get; init; }

    public float Lateral { get; init; }

    public float Low { get; init; }

    public float Overshoot { get; init; }

    public float Duration { get; init; } = 0.3f;

    /// <summary>胴を狙うか。技の正体なので <see cref="PunchBook.Validated"/> が既定へ戻す。</summary>
    public bool ToBody { get; init; }

    public float HeadShiftX { get; init; }

    public float HeadShiftY { get; init; }

    public float HeadShiftZ { get; init; }

    public float StepIn { get; init; }

    public float Extend { get; init; } = 1f;

    public float Sweep { get; init; }

    public static PunchMove From(PunchSpec spec) => new()
    {
        Lead = spec.Lead,
        Lateral = spec.Lateral,
        Low = spec.Low,
        Overshoot = spec.Overshoot,
        Duration = spec.Duration,
        ToBody = spec.ToBody,
        HeadShiftX = spec.HeadShift.X,
        HeadShiftY = spec.HeadShift.Y,
        HeadShiftZ = spec.HeadShift.Z,
        StepIn = spec.StepIn,
        Extend = spec.Extend,
        Sweep = spec.Sweep,
    };

    public PunchSpec ToSpec() => new(Lead, Lateral, Low, Overshoot, Duration, ToBody, new Vector3(HeadShiftX, HeadShiftY, HeadShiftZ), StepIn, Extend, Sweep);

    /// <summary>範囲へ収める。<paramref name="original"/> は既定の値で、技の正体（Lead・ToBody）と Extend（フェイント以外は 1）をそこへ戻す。</summary>
    public PunchMove Validated(PunchMove original, bool feint) => this with
    {
        Lead = original.Lead,
        ToBody = original.ToBody,
        Lateral = PunchRanges.Lateral.Clamp(Lateral, original.Lateral),
        Low = PunchRanges.Low.Clamp(Low, original.Low),
        Overshoot = PunchRanges.Overshoot.Clamp(Overshoot, original.Overshoot),
        Duration = PunchRanges.Duration.Clamp(Duration, original.Duration),
        HeadShiftX = PunchRanges.HeadShiftX.Clamp(HeadShiftX, original.HeadShiftX),
        HeadShiftY = PunchRanges.HeadShiftY.Clamp(HeadShiftY, original.HeadShiftY),
        HeadShiftZ = PunchRanges.HeadShiftZ.Clamp(HeadShiftZ, original.HeadShiftZ),
        StepIn = PunchRanges.StepIn.Clamp(StepIn, original.StepIn),
        Extend = feint ? PunchRanges.FeintExtend.Clamp(Extend, original.Extend) : 1f,
        Sweep = PunchRanges.Sweep.Clamp(Sweep, original.Sweep),
    };
}

/// <summary>
/// 拳の技の一覧（技 → 値）。<see cref="Default"/> が既定の値の唯一の出どころ（<see cref="PunchSpec.Of"/> もここを見る）。
/// スタイルは <see cref="BotStyle.Punches"/> で自分の本を持ち、JSON に読み書きできる。JSON に書いていない技・値は既定になる
/// （既存のスタイルの JSON は書き換えなくてよい）。
/// <para>
/// 変えてよいのは経路と体の使い方（溜め・時間・頭のずれ・踏み込み・振り抜き）。技の正体（前の手か・胴か）は <see cref="Validated"/> が既定へ戻す。
/// </para>
/// </summary>
[JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
public sealed record PunchBook
{
    public PunchMove Jab { get; init; } = Make(true, 0f, 0f, 0.25f, 0.30f, false, new(0.05f, 0f, 0.08f), 0.6f);

    public PunchMove Cross { get; init; } = Make(false, 0.1f, 0f, 0.35f, 0.40f, false, new(0.12f, -0.02f, 0.14f), 0.35f);

    // 頭へのフックは肘の高さを越える溜め（Low 負）から、相手の顔を横切って振り抜く（PDCA サイクル 2）。
    public PunchMove LeadHook { get; init; } = Make(true, 0.85f, -0.3f, 0.05f, 0.42f, false, new(-0.10f, -0.03f, 0.05f), 0.1f, sweep: 0.3f);

    public PunchMove RearHook { get; init; } = Make(false, 0.9f, -0.3f, 0.05f, 0.46f, false, new(0.12f, -0.03f, 0.06f), 0.1f, sweep: 0.3f);

    public PunchMove LeadUppercut { get; init; } = Make(true, 0.15f, 0.9f, 0.2f, 0.42f, false, new(-0.04f, -0.15f, 0.06f), 0.15f);

    public PunchMove RearUppercut { get; init; } = Make(false, 0.2f, 1.0f, 0.2f, 0.46f, false, new(0.06f, -0.15f, 0.06f), 0.15f);

    public PunchMove BodyJab { get; init; } = Make(true, 0f, 0.3f, 0.25f, 0.32f, true, new(0.04f, -0.35f, 0.12f), 0.7f);

    public PunchMove BodyCross { get; init; } = Make(false, 0.1f, 0.3f, 0.3f, 0.42f, true, new(0.10f, -0.35f, 0.14f), 0.4f);

    public PunchMove LeadBodyHook { get; init; } = Make(true, 0.8f, 0.35f, 0.15f, 0.44f, true, new(0.10f, -0.3f, 0.06f), 0.15f);

    public PunchMove RearBodyHook { get; init; } = Make(false, 0.85f, 0.35f, 0.15f, 0.48f, true, new(-0.08f, -0.3f, 0.06f), 0.15f);

    public PunchMove Overhand { get; init; } = Make(false, 0.55f, -0.7f, 0.25f, 0.50f, false, new(0.15f, -0.08f, 0.2f), 0.3f);

    public PunchMove Feint { get; init; } = Make(true, 0f, 0f, 0f, 0.20f, false, new(0.03f, -0.04f, 0.06f), 0.2f, extend: 0.35f);

    /// <summary>最初の手書きの値（以前は <c>PunchSpec.Of</c> に直に書いてあった）。</summary>
    public static PunchBook Default { get; } = new();

    private static PunchMove Make(bool lead, float lateral, float low, float overshoot, float duration, bool toBody, Vector3 headShift, float stepIn,
        float extend = 1f, float sweep = 0f)
        => PunchMove.From(new PunchSpec(lead, lateral, low, overshoot, duration, toBody, headShift, stepIn, extend, sweep));

    public PunchMove MoveOf(Technique technique) => technique switch
    {
        Technique.Jab => Jab,
        Technique.Cross => Cross,
        Technique.LeadHook => LeadHook,
        Technique.RearHook => RearHook,
        Technique.LeadUppercut => LeadUppercut,
        Technique.RearUppercut => RearUppercut,
        Technique.BodyJab => BodyJab,
        Technique.BodyCross => BodyCross,
        Technique.LeadBodyHook => LeadBodyHook,
        Technique.RearBodyHook => RearBodyHook,
        Technique.Overhand => Overhand,
        Technique.Feint => Feint,
        _ => throw new ArgumentOutOfRangeException(nameof(technique), technique, null),
    };

    public PunchSpec Of(Technique technique) => MoveOf(technique).ToSpec();

    /// <summary>1 つの技の値だけ差し替えた本。</summary>
    public PunchBook With(Technique technique, PunchMove move) => technique switch
    {
        Technique.Jab => this with { Jab = move },
        Technique.Cross => this with { Cross = move },
        Technique.LeadHook => this with { LeadHook = move },
        Technique.RearHook => this with { RearHook = move },
        Technique.LeadUppercut => this with { LeadUppercut = move },
        Technique.RearUppercut => this with { RearUppercut = move },
        Technique.BodyJab => this with { BodyJab = move },
        Technique.BodyCross => this with { BodyCross = move },
        Technique.LeadBodyHook => this with { LeadBodyHook = move },
        Technique.RearBodyHook => this with { RearBodyHook = move },
        Technique.Overhand => this with { Overhand = move },
        Technique.Feint => this with { Feint = move },
        _ => throw new ArgumentOutOfRangeException(nameof(technique), technique, null),
    };

    /// <summary>範囲へ収め、技の正体（Lead・ToBody）を既定へ戻し、Extend をフェイント以外 1 に固定した本。</summary>
    public PunchBook Validated()
    {
        var book = this;
        foreach (var technique in Enum.GetValues<Technique>())
        {
            var original = Default.MoveOf(technique);
            book = book.With(technique, (book.MoveOf(technique) ?? original).Validated(original, technique == Technique.Feint));
        }

        return book;
    }
}
