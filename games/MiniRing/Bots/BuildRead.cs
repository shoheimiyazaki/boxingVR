namespace MiniRing.Bots;

/// <summary>
/// 体格を読む層（docs/BOT_PDCA.md「体格に応じた戦い方」）: 自分と相手の見えている寸法の比から、戦い方の重みを連続に出す。
/// キャラの名前・体格の種類・スタイルでは分けない（CLAUDE.md §1.3）。体格が同じなら 4 つの比は 0（ln 1）で、どの倍率もちょうど 1 になり、
/// 従来と同じ振る舞いになる。ボットが見てよいのは見えている寸法だけ（§1.7）: 腕の長さ（<see cref="FighterSense.ArmReach"/>）・身長・
/// 太さ（<see cref="FighterSense.SelfBuild"/>）。重さは見た目の大きさから <see cref="BodyBuild.MassRatio"/> で見積もる（読み取れなければ 0 = 同じ体格）。
/// <list type="bullet">
/// <item><see cref="Arm"/> = ln(自分の腕 / 相手の腕)。</item>
/// <item><see cref="Height"/> = ln(自分の身長 / 相手の身長)。</item>
/// <item><see cref="Mass"/> = ln(自分の重さの見積もり / 相手の重さの見積もり)。</item>
/// </list>
/// </summary>
internal readonly record struct BuildRead(float Arm, float Height, float Mass)
{
    /// <summary>比の対数の絶対値の上限（体格は基準の 0.6〜1.5 倍なので、ln(1.5/0.6) ≈ 0.92 を超えないが、念のため）。</summary>
    public const float MaxLog = 0.95f;

    /// <summary>同じ体格（または読み取れない）。</summary>
    public static BuildRead Even => default;

    public static BuildRead From(in FighterSense sense)
    {
        var arm = Ratio(sense.ArmReach, sense.OpponentArmReach);
        if (sense.SelfBuild is not { } self || sense.OpponentBuild is not { } other)
        {
            return new BuildRead(arm, 0f, 0f);
        }

        return new BuildRead(arm, Ratio(self.Height, other.Height), Ratio(self.MassRatio, other.MassRatio));
    }

    /// <summary>立ち位置（普段・打ちに入る・休む間合い。腕の長さ倍）に掛ける倍率 ρ^(−BuildStandOff)。</summary>
    public float StandOff(BotStyle style) => MathF.Exp(-style.BuildStandOff * Arm);

    /// <summary>打つ間合い（<see cref="BotStyle.AttackRange"/>）に掛ける倍率 min(ρ, 1)^BuildInside（短い腕ほど深く、長い腕は変えない）。</summary>
    public float Inside(BotStyle style) => MathF.Exp(style.BuildInside * MathF.Min(0f, Arm));

    /// <summary>攻めの間隔の縮む速さに掛ける倍率 μ^BuildPush（重い方が速い）。</summary>
    public float Tempo(BotStyle style) => MathF.Exp(style.BuildPush * Mass);

    /// <summary>打ったあとに離れる確率に掛ける倍率 μ^(−BuildPush)。</summary>
    public float Leave(BotStyle style) => MathF.Exp(-style.BuildPush * Mass);

    /// <summary>頭を沈める守り（ダッキング・ウィービング・ロール）の重みに掛ける倍率 η^(−BuildDuck)（自分の頭が低いほど大きい）。</summary>
    public float Dive(BotStyle style) => MathF.Exp(-style.BuildDuck * Height);

    /// <summary>引く守り（スウェー・ステップバック）の重みに掛ける倍率 η^(BuildDuck)。</summary>
    public float Retreat(BotStyle style) => MathF.Exp(style.BuildDuck * Height);

    /// <summary>この守りの重みに掛ける倍率（沈む守り・引く守り以外は 1）。</summary>
    public float DefenseScale(BotStyle style, Defense defense) => defense switch
    {
        Defense.Duck or Defense.Weave or Defense.Roll => Dive(style),
        Defense.Sway or Defense.StepBack => Retreat(style),
        _ => 1f,
    };

    /// <summary>胴を狙う重みの合計に足す値 BuildAimBody × max(0, −ln η)（相手が高いほど大きい）。</summary>
    public float BodyAim(BotStyle style) => style.BuildAimBody * MathF.Max(0f, -Height);

    /// <summary>
    /// 狙いどころの重みに、胴の分を足したもの（肝臓・みぞおち・心臓へ 3 等分）。足す値が 0 なら <paramref name="weights"/> をそのまま返す
    /// （重みが無い（null）ままなら、従来どおり真ん中だけを狙い、乱数も使わない）。
    /// </summary>
    public AimWeights? WithBody(BotStyle style, AimWeights? weights)
    {
        var body = BodyAim(style);
        if (body <= 0f)
        {
            return weights;
        }

        var w = weights ?? AimWeights.None;
        var each = body / 3f;
        return w with { Liver = w.Liver + each, SolarPlexus = w.SolarPlexus + each, Heart = w.Heart + each };
    }

    private static float Ratio(float own, float other)
        => own > 1e-4f && other > 1e-4f && float.IsFinite(own) && float.IsFinite(other)
            ? Math.Clamp(MathF.Log(own / other), -MaxLog, MaxLog)
            : 0f;
}
