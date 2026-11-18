namespace MiniRing.Arena;

/// <summary>下見した 1 試合の結果。</summary>
/// <param name="Seed">試合の種。</param>
/// <param name="Finish">決着の文（ゲームの <see cref="BoxingGame.Decision"/> か <see cref="BoxingGame.Message"/>）。</param>
/// <param name="Ko">K.O. で決まった（判定でない）。</param>
/// <param name="Round">決着したラウンド。</param>
/// <param name="RoundTime">決着したラウンドの経過時間 [s]。</param>
/// <param name="RedDealt">赤が与えたダメージ。</param>
/// <param name="BlueDealt">青が与えたダメージ。</param>
/// <param name="RedDowns">赤が取られたダウンの数。</param>
/// <param name="BlueDowns">青が取られたダウンの数。</param>
/// <param name="Winner">1 = 赤、−1 = 青、0 = 引き分け。</param>
/// <param name="Death">臓器・首が壊れて死んだ（K.O. のうち、気絶や 10 カウントでない。VFC の決着）。</param>
internal sealed record TvScoutResult(int Seed, string Finish, bool Ko, int Round, float RoundTime, float RedDealt, float BlueDealt, int RedDowns, int BlueDowns, int Winner, bool Death = false)
{
    /// <summary>ゲームの決着の文（"K.O. — BRAIN DESTROYED"）が死（臓器・首の破壊）か。</summary>
    public static bool IsDeath(string finish) => finish.StartsWith("K.O. — ", StringComparison.Ordinal) && finish.Contains("DESTROYED", StringComparison.Ordinal);

    /// <summary>ラウンド中に両者が同時に倒れていた（立っていない）時間の合計 [s]。</summary>
    public float BothDownSeconds { get; init; }

    /// <summary>両者が同時に倒れていた時間の、いちばん長く続いた 1 回 [s]。</summary>
    public float LongestBothDown { get; init; }
}

/// <summary>
/// テレビ中継の決まりと下見の点数（描画にも試合にも触れない計算だけ。テストから直に確かめる）。
/// </summary>
internal static class TvShowRules
{
    /// <summary>
    /// 試合の始め（入場から最初のゴングまで）の長さ [s]（ゲームの時間）。選手が始めのエモート（観客へ手を振る・準備運動…）をする。人形の 0.75 倍速の動画で 9 秒ほど。
    /// ゲームの決まりの値（<see cref="MatchRules.IntroSeconds"/>）。撮影と下見（tvscout）が同じ値を使うので、同じ種なら下見と撮影の試合は一致する。
    /// </summary>
    public const float IntroSeconds = 7f;

    /// <summary>
    /// 試合の終わり（決着の後）に、立っている選手が終わりのエモートをする長さ [s]（ゲームの時間。<see cref="MatchRules.EndSeconds"/>）。
    /// 人形の 0.75 倍速の動画で 9.3 秒: 勝者・敗者のカット（<see cref="TvDirector.PlanEnd"/>）を 8〜10 秒に収める。
    /// </summary>
    public const float EndSeconds = 7f;

    /// <summary>世界を作った直後の助走 [s]（映像の始めに、生成直後の T 字の姿勢が映らないよう構えへ追いつかせる）。<see cref="BoxingCorners.SettleSeconds"/>。</summary>
    public const float SettleSeconds = 1f;

    /// <summary>テレビ中継の決まり（ラウンドの数・休みの長さ。休みの間に治る量は標準と同じ）。撮影と下見（tvscout）が同じものを使う。</summary>
    public static MatchRules Rules(int rounds, float breakSeconds)
        => MatchRules.Tv with { Rounds = rounds, BreakSeconds = breakSeconds, RegenVoxelsPerSecond = MatchRules.Tv.RegenVoxelsPerSecond * MatchRules.Tv.BreakSeconds / breakSeconds, IntroSeconds = IntroSeconds, EndSeconds = EndSeconds };

    /// <summary>
    /// 団体の決まり（<see cref="Organization.CountsDowns"/>・<see cref="OrgMatch"/>）を重ねたテレビ中継の決まり。WVC は <see cref="Rules(int, float)"/> と同じ値、
    /// VFC はダウンを数えず・判定せず・休みに傷が前線の再生で少しずつ戻り、失った武器を支給する（<see cref="MatchRules.Vfc"/> と同じ決まり）。撮影と下見（tvscout）が同じものを使う。
    /// </summary>
    public static MatchRules Rules(int rounds, float breakSeconds, Organization org)
    {
        ArgumentNullException.ThrowIfNull(org);
        var rules = Rules(rounds, breakSeconds);
        var regrowth = org.Match.Regrowth;
        return rules with
        {
            CountsDowns = org.CountsDowns,
            JudgesDecision = org.Match.JudgesDecision,
            RegenVoxelsPerSecond = org.Match.Regenerates ? rules.RegenVoxelsPerSecond : 0f,
            // 皮膚のあざは、体が休みに治る団体（予算でも前線でも）では休みに引く。
            BruiseHalfLifeSeconds = org.Match.Regenerates || regrowth is { Speed: > 0f } ? rules.BruiseHalfLifeSeconds : 0f,
            RegrowthSpeed = regrowth?.Speed ?? 0f,
            RegrowthTickSeconds = regrowth?.TickSeconds ?? rules.RegrowthTickSeconds,
            StuckDoublingSeconds = regrowth?.StuckDoublingSeconds ?? 0f,
            ResupplyWeapons = org.Match.ResupplyWeapons,
        };
    }

    /// <summary>
    /// 団体の場（<see cref="OrgMatch"/>）を両コーナーに重ねる: レフェリーがいない団体ならいない（<paramref name="corners"/> がいないと言えばどちらでもいない）、
    /// 檻の団体なら六角形の檻（両方が武器を握れば広い <see cref="RingLayout.WeaponsCage"/>、ほかは <see cref="RingLayout.Cage"/>。四角いリングの選び方と同じ）。
    /// </summary>
    public static BoxingCorners Stage(BoxingCorners corners, Organization org)
    {
        ArgumentNullException.ThrowIfNull(corners);
        ArgumentNullException.ThrowIfNull(org);
        var staged = corners with { Referee = corners.Referee && org.Match.Referee, SettleSeconds = SettleSeconds };
        // 檻の団体: リングが無いか、四角いリング（動画・中継の両コーナーはコーナーから始める四角いリングを持って来る: StartingAtCorners）なら檻にする。
        // コーナーから始めるかは渡されたリングのまま（以前は「リングが無いとき」だけ檻にしていたので、コーナーから始める動画・下見は四角いリングだった）。
        return org.Match.Arena == ArenaShape.HexCage && corners.Ring is not { Shape: ArenaShape.HexCage }
            ? staged with
            {
                Ring = (corners.CpuWeapon is not null && corners.PlayerWeapon is not null ? RingLayout.WeaponsCage : RingLayout.Cage) with { FromCorners = corners.Ring?.FromCorners ?? false },
            }
            : staged;
    }

    /// <summary>
    /// 見ごたえの点（中継に向く試合ほど高い）。団体の重み（<see cref="DramaWeights"/>、団体のファイルの <c>drama</c>）の和。<paramref name="weights"/> が null なら WVC の重み。
    /// WVC の重み: 長く続く（最終ラウンドの終わりまでで最大 3 × ラウンド数）、ダウン（1 回 1.5、5 回まで）、両者がダウンを奪い合う（+3）、
    /// K.O. で決着（+2、最終ラウンドなら +1）、ダメージで負けていた側の逆転勝ち（+4）、第 1 ラウンド 15 秒より前に終わる（−6）、引き分け（−1）。
    /// VFC の重み: 長く続く・死で決着（後半ほど高い）・逆転・両者が深く傷んでいるで上がり、序盤の決着と両者生還（引き分け）で下がる。ダウンは数えない（重み 0）。
    /// </summary>
    public static float Drama(TvScoutResult r, int rounds, float roundSeconds, DramaWeights? weights = null)
    {
        ArgumentNullException.ThrowIfNull(r);
        var w = weights ?? Organization.Default.Drama;
        var score = w.PerRound * ((r.Round - 1) + Math.Clamp(r.RoundTime / roundSeconds, 0f, 1f));
        var downs = r.RedDowns + r.BlueDowns;
        score += w.PerDown * Math.Min(downs, w.DownCap);
        score += r.RedDowns > 0 && r.BlueDowns > 0 ? w.BothDowns : 0f;
        score += r.Ko ? w.Ko + (r.Round >= rounds ? w.KoLastRound : 0f) : 0f;
        score += r.Death ? w.Death + (w.DeathPerRound * (r.Round - 1)) : 0f;
        var winnerDealt = r.Winner > 0 ? r.RedDealt : r.BlueDealt;
        var loserDealt = r.Winner > 0 ? r.BlueDealt : r.RedDealt;
        score += r.Winner != 0 && winnerDealt < loserDealt ? w.Comeback : 0f;
        score += w.BothHurt * Math.Min(1f, Math.Min(r.RedDealt, r.BlueDealt) / w.BothHurtDamage);
        score -= r.Round == 1 && r.RoundTime < w.QuickSeconds ? w.QuickPenalty : 0f;
        score -= r.Winner == 0 ? w.DrawPenalty : 0f;
        return score;
    }

    /// <summary>一番見ごたえのある種（同点なら小さい種）。</summary>
    public static TvScoutResult Best(IReadOnlyList<TvScoutResult> results, int rounds, float roundSeconds, DramaWeights? weights = null)
    {
        ArgumentNullException.ThrowIfNull(results);
        return results.OrderByDescending(r => Drama(r, rounds, roundSeconds, weights)).ThenBy(r => r.Seed).First();
    }
}
