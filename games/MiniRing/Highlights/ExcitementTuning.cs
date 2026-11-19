namespace MiniRing.Highlights;

/// <summary>
/// 試合の面白さ（見どころ）の測り方の値のすべて。検出の閾値（何を「出来事」と数えるか）と、総合点の重み。
/// 試合の計算には一切使わない（読むだけの観測。<see cref="MatchHighlights"/>）。
/// <para>
/// 長さ・速さは「人の大きさ」に直した値（選手の身長を 1.75 m とみなす。リングは人の 5.3 m）。試合の中の箱は小さいので、
/// 観測側が身長で割って直す（<see cref="MatchHighlights"/> の <c>Human</c>）。
/// </para>
/// <para>
/// 重みの方針（ユーザーの指定 2026-11-19）: 見た目の派手さ・技・暴力・逆転のドラマを、数字のダメージより重く数える。
/// 生のダメージは <see cref="RawDamageWeight"/> でごく軽く足すだけ。
/// </para>
/// </summary>
public sealed record ExcitementTuning
{
    public static ExcitementTuning Default { get; } = new();

    // ---- 検出: 打ち出し ----

    /// <summary>拳が「打ち出された」とみなす、自分の胸に対する拳の速さの相手の頭の方向の成分 [人の m/s]。構えの揺れ（〜1.5 m/s）より十分大きく、
    /// 実際のパンチの手の速さ（6〜10 m/s）より小さい値。</summary>
    public float LaunchSpeed { get; init; } = 3.5f;

    /// <summary>打ち出した拳が飛んでいるとみなす最長の時間 [s]。ジャブ〜フックの往復はこの中に収まる。</summary>
    public float FlightSeconds { get; init; } = 0.6f;

    // ---- 打ち合い ----

    /// <summary>打ち合いの間合い: 胸と胸の水平距離 [人の m]。腕の届く範囲（腕の長さ 0.75 m × 2 程度）。</summary>
    public float ExchangeRange { get; init; } = 1.4f;

    /// <summary>打ち出しと打ち出しの間がこれ以内なら同じ打ち合いの続き [s]。</summary>
    public float ExchangeGap { get; init; } = 1.0f;

    // ---- 紙一重 ----

    /// <summary>拳の中心と頭の中心の距離がこれより小さいのに当たらなければ「かすめた」[人の m]。頭の半径 0.1 + 拳 0.05 + すき間 0.12。</summary>
    public float NearMissDistance { get; init; } = 0.27f;

    /// <summary>当たったとみなす距離 [人の m]（頭の半径 + 拳の半径）。これと <see cref="NearMissDistance"/> の間が紙一重の帯。</summary>
    public float ContactDistance { get; init; } = 0.15f;

    /// <summary>紙一重の強さの基準の拳の速さ [人の m/s]。</summary>
    public float NearMissSpeedRef { get; init; } = 6f;

    /// <summary>よけた速さの基準: 拳の通り道から頭が逃げる速さ [人の m/s]。スリップ・スウェーの頭の速さ（1〜2 m/s）。</summary>
    public float EvadeSpeedRef { get; init; } = 1.5f;

    // ---- 打撃の強さ ----

    /// <summary>打撃のエネルギーの基準 [J、試合の値]。<see cref="HitEvent.Energy"/> の典型的な強打（試合で測った上位の値の目安）。</summary>
    public float EnergyRef { get; init; } = 2f;

    /// <summary>皮一枚: 当たりのエネルギーがこれ × <see cref="EnergyRef"/> 以上なのに、削れたのが皮膚・髪だけ。</summary>
    public float GrazeEnergyShare { get; init; } = 0.5f;

    // ---- ダウン・KO ----

    /// <summary>一撃: 決め手の打撃の直前の意識がこれ以上（ほぼ満タン）。</summary>
    public float OnePunchConsciousness { get; init; } = 0.8f;

    /// <summary>一撃: ダウン・KO の前のこの時間に、打撃（ガード以外）が 1 つだけ [s]。</summary>
    public float OnePunchWindow { get; init; } = 2f;

    /// <summary>打たれずに崩れたダウンの強さ（直前の打撃のエネルギーが 0 のとき）。倒れる見た目の驚きはあるが攻防の結果ではないので、
    /// 打撃で倒れたダウン（強さ 1）の 1/4。</summary>
    public float UnforcedKnockdown { get; init; } = 0.25f;

    /// <summary>立ち上がりの強さの基準: 倒れていた時間 [s]。長く倒れてから立つほど劇的。</summary>
    public float RiseRefSeconds { get; init; } = 5f;

    // ---- 体の破壊 ----

    /// <summary>部位の大きな欠け: 部位の始めのセルのこの割合を失うごとに 1 回。</summary>
    public float VoxelLossShare { get; init; } = 0.1f;

    /// <summary>派手な砕け: 1 回の砕けで飛んだ粒がこれ以上。</summary>
    public int ShatterGrains { get; init; } = 12;

    /// <summary>頭のねじれの基準の角速度 [rad/s、<see cref="Fighter.LastHeadTwist"/>]。意識は ω² で落ちるので強さも (ω / 基準)²。</summary>
    public float HeadSnapTwist { get; init; } = 20f;

    // ---- 連打 ----

    /// <summary>連打: この時間の中に同じ側のクリーンヒット（ガード以外）が <see cref="FlurryHits"/> 以上 [s]。</summary>
    public float FlurrySeconds { get; init; } = 1f;

    public int FlurryHits { get; init; } = 3;

    // ---- ドラマ（見込み p_red の動き、§A） ----

    /// <summary>与えたダメージの割合の差 = (赤 − 青) / (合計 + この値)。序盤の 1 発で ±1 に振れないように（強打 1 発のダメージ 〜20）。</summary>
    public float DamageShareSoftening { get; init; } = 20f;

    /// <summary>見込みを記録する刻み [s]。</summary>
    public float BeliefSample { get; init; } = 0.25f;

    /// <summary>見込みの揺れやすさ（|Δp|/秒 の移動平均）の半減期 [s]。打ち合い 1〜2 回の長さ: いま試合が動いているかの記憶。</summary>
    public float VolatilityHalfLife { get; init; } = 3f;

    /// <summary>「1 回の揺れ」を測る時間 [s]。ダウン 1 回（倒れて数えられる）が入る長さ。</summary>
    public float SwingSeconds { get; init; } = 5f;

    /// <summary>見込みの入れ替わりのヒステリシス（0.5 ± この値を越えて初めて入れ替わり）。小さな揺れで数えない。</summary>
    public float LeadHysteresis { get; init; } = 0.1f;

    /// <summary>驚き（Σ|Δp|）1 /分 あたりの点。</summary>
    public float SurpriseWeight { get; init; } = 20f;

    /// <summary>はらはらの平均 1（|Δp|/秒）あたりの点。</summary>
    public float SuspenseWeight { get; init; } = 100f;

    /// <summary>一番大きな揺れ 1（p が 0 → 1）あたりの点。</summary>
    public float SwingWeight { get; init; } = 20f;

    /// <summary>見込みの入れ替わり 1 回あたりの点。</summary>
    public float LeadChangeWeight { get; init; } = 10f;

    // ---- 緩急（慣れのある興奮の曲線、§B） ----

    /// <summary>興奮の曲線の刻み [s]。</summary>
    public float ArousalSample { get; init; } = 0.25f;

    /// <summary>興奮が基準へ戻る半減期 [s]。強打の後の「おおっ」が静まる長さ（数秒）。短いと出来事ごとの針、長いと平らになる。</summary>
    public float ArousalHalfLife { get; init; } = 3f;

    /// <summary>目新しさの記憶の半減期 [s]。同じ種類が 6 秒に何度も来ると慣れる（打ち合いの間隔 1〜2 秒の 3〜6 倍）。</summary>
    public float NoveltyHalfLife { get; init; } = 6f;

    /// <summary>同じ選手の出来事の記憶が目新しさを下げる割合（同じ種類の半分）。一方的な展開にも慣れる。</summary>
    public float SideNoveltyShare { get; init; } = 0.5f;

    /// <summary>見込みの揺れ |Δp| = 1 が興奮をどれだけ上げるか。ダウン 1 回（重み 6）と同じぐらいに、p が 0.3 動くと 〜6。</summary>
    public float SurpriseArousal { get; init; } = 20f;

    /// <summary>退屈の水準（興奮の値）。小さな打ち合い 1 回（1）が半減期 1 回分しぼんだ値。</summary>
    public float BoredLevel { get; init; } = 0.5f;

    /// <summary>山と数える高さ（直前の谷から上がり、その後に下がる量）。紙一重 1 回（〜2）より上 = 見どころの重なり。</summary>
    public float PeakProminence { get; init; } = 3f;

    /// <summary>山 1 つ/分 あたりの点。</summary>
    public float PeakWeight { get; init; } = 3f;

    /// <summary>山の高さの平均 1 あたりの点（上限 <see cref="PeakHeightCap"/>）。</summary>
    public float PeakHeightWeight { get; init; } = 1f;

    public float PeakHeightCap { get; init; } = 20f;

    /// <summary>後半の山が前半より高い（育つ）ことの点（−1..1 に）。</summary>
    public float GrowthWeight { get; init; } = 5f;

    /// <summary>退屈の割合 1（全部退屈）の罰。</summary>
    public float BoredPenalty { get; init; } = 20f;

    /// <summary>最長の退屈が試合の全部だったときの罰。</summary>
    public float LongBoredPenalty { get; init; } = 20f;

    // ---- 総合（§C） ----

    /// <summary>総合 = 派手さ × この重み + ドラマ × <see cref="DramaWeight"/> + 緩急 × <see cref="PacingWeight"/>。3 つがおおむね同じ桁になるように。</summary>
    public float SpectacleWeight { get; init; } = 0.5f;

    public float DramaWeight { get; init; } = 1f;

    public float PacingWeight { get; init; } = 1f;

    // ---- 派手さ（出来事の点） ----

    /// <summary>1 つの出来事の強さの上限（1 つの異常値で点が決まらないように）。</summary>
    public float IntensityCap { get; init; } = 3f;

    /// <summary>出来事のない時間がこれより長いと「退屈な時間」[s]。</summary>
    public float DeadGapSeconds { get; init; } = 5f;

    /// <summary>退屈な時間の割合 1 につき、点をこの割合だけ下げる。</summary>
    public float DeadTimePenalty { get; init; } = 0.5f;

    /// <summary>生のダメージ 1 点あたりの重み（ユーザーの方針でごく軽い）。</summary>
    public float RawDamageWeight { get; init; } = 0.01f;

    /// <summary>出来事の種類ごとの重み（強さ 1 あたり）。派手さ・技・ドラマを重く。</summary>
    public IReadOnlyDictionary<HighlightKind, float> Weights { get; init; } = new Dictionary<HighlightKind, float>
    {
        [HighlightKind.Exchange] = 1f, // 強さ = 行き来の回数。3 往復で 3
        [HighlightKind.NearMiss] = 3f, // 技（よけ）
        [HighlightKind.Graze] = 3f, // 技（皮一枚）
        [HighlightKind.Counter] = 3f, // 技（相打ちの中で先に当てる）
        [HighlightKind.Flurry] = 1.5f,
        [HighlightKind.Knockdown] = 6f, // ドラマ
        [HighlightKind.Rise] = 4f, // ドラマ
        [HighlightKind.KO] = 8f,
        [HighlightKind.OnePunch] = 10f, // 派手さ＋ドラマ
        [HighlightKind.BoneBreak] = 6f, // 暴力
        [HighlightKind.LimbLoss] = 8f, // 暴力
        [HighlightKind.VoxelLoss] = 2f, // 見た目の欠け
        [HighlightKind.Shatter] = 1.5f, // 見た目の派手さ
        [HighlightKind.HeadSnap] = 2f, // 見た目の派手さ（首が持っていかれる）
    };
}
