using System.Globalization;
using MiniRing.Arena;
using MiniRing.Characters;

namespace MiniRing.Desktop;

/// <summary>
/// ゲームモード（試合の場、2026-11-26）: 練習試合か、団体（WVC・VFC・IVF）の大会。団体の違いは団体のファイル（<c>games/MiniRing.Arena/Orgs/&lt;id&gt;.json</c>）の値だけで、
/// 読み込み・出場の条件・計量・試合の決まり・場の形は中継と同じ 1 か所（<see cref="Organization"/>・<see cref="OrgBouts"/>・<see cref="TvShowRules"/>）を通る（CLAUDE.md §1.5）。
/// 体・物理は団体によらず同じ。
/// </summary>
/// <param name="Org">試合の決まりを借りる団体。練習試合で「標準」なら null（今までのデスクトップの試合と同じ）。</param>
/// <param name="Tournament">大会か（出場の条件・団体の着けさせる物・階級を守る）。練習試合は誰とでも組め、手の物は選手のまま。</param>
internal sealed record Competition(Organization? Org, bool Tournament)
{
    /// <summary>練習試合（<paramref name="rules"/> の団体の決まりで。null なら標準）。</summary>
    public static Competition Practice(Organization? rules) => new(rules, false);

    /// <summary>団体の大会。</summary>
    public static Competition Event(Organization org) => new(org ?? throw new ArgumentNullException(nameof(org)), true);

    /// <summary>選べる団体（既定の団体が先、あとは id の順）。</summary>
    public static IReadOnlyList<Organization> Organizations()
        => [.. Arena.Organizations.All.Values.OrderBy(o => o.Id == Organization.DefaultId ? 0 : 1).ThenBy(o => o.Id, StringComparer.Ordinal)];

    /// <summary>画面の見出し（「練習試合」「IVF の大会」）。</summary>
    public string Title => Tournament ? $"{Org!.LogoText} の大会" : "練習試合";

    /// <summary>決まりの名前（「標準」「VFC」）。</summary>
    public string RulesName => Org?.LogoText ?? "標準";

    /// <summary>出られない理由（出られるなら null）。練習試合は誰でも出られる。</summary>
    public string? Ineligible(FighterCharacter character)
    {
        ArgumentNullException.ThrowIfNull(character);
        return Tournament ? OrgBouts.Ineligible(Org!, character) : null;
    }

    /// <summary>この選手の階級（階級制の団体の大会のときだけ。計量 = 見えている体の重さ）。</summary>
    public OrgDivision? DivisionOf(FighterCharacter character)
    {
        ArgumentNullException.ThrowIfNull(character);
        return Tournament && Org!.HasWeightClasses ? Org.DivisionOf(OrgBouts.WeightOf(character)) : null;
    }

    /// <summary>赤（<paramref name="red"/>）の相手に <paramref name="blue"/> を組めない理由（組めるなら null）: 出られない・階級が違う。</summary>
    public string? CannotFace(FighterCharacter red, FighterCharacter blue)
    {
        ArgumentNullException.ThrowIfNull(red);
        ArgumentNullException.ThrowIfNull(blue);
        if (Ineligible(blue) is { } why) { return why; }
        return DivisionOf(red) is { } redDivision && DivisionOf(blue) is { } blueDivision && redDivision != blueDivision
            ? string.Create(CultureInfo.InvariantCulture, $"{Org!.LogoText} は同じ階級どうしだけ: {red.HudName} は{redDivision.Name}、{blue.HudName} は{blueDivision.Name}")
            : null;
    }

    /// <summary>試合に出る両者（大会なら計量して、団体の着けさせる物を着ける: <see cref="OrgBouts.Enter"/>。組めなければ例外）。</summary>
    public (FighterCharacter Red, FighterCharacter Blue) Enter(FighterCharacter red, FighterCharacter blue)
    {
        if (!Tournament) { return (red, blue); }
        var (r, b, _) = OrgBouts.Enter(Org!, red, blue);
        return (r, b);
    }

    /// <summary>
    /// 試合の決まり: 団体があれば中継と同じ決まり（<see cref="TvShowRules.Rules(int, float, Organization)"/>。ラウンドの数・休みは中継の標準）、無ければ <paramref name="standard"/>。
    /// 練習試合は始めの入場（エモート）を省いてすぐゴング、大会は中継と同じく入場から。
    /// </summary>
    public MatchRules? Rules(MatchRules? standard)
        => Org is { } org
            ? (Tournament ? OrgRules(org) : OrgRules(org) with { IntroSeconds = 0f })
            : standard;

    private static MatchRules OrgRules(Organization org) => TvShowRules.Rules(MatchRules.Tv.Rounds, MatchRules.Tv.BreakSeconds, org);

    /// <summary>団体の場（レフェリーの有無・檻）を両コーナーに重ねる（<see cref="TvShowRules.Stage"/>）。</summary>
    public BoxingCorners Stage(BoxingCorners corners) => Org is { } org ? TvShowRules.Stage(corners, org) : corners;

    /// <summary>
    /// 決まりの要約（ファイルの値から作る 1 行ずつ）: 階級・ダウン・決着・レフェリー・場・手の物。
    /// </summary>
    public static IReadOnlyList<string> Summary(Organization? org, bool tournament)
    {
        if (org is null)
        {
            return ["今までの試合（レフェリーは設定のまま）", "誰とでも組める・手の物は選手のまま"];
        }

        var match = org.Match;
        var rules = TvShowRules.Rules(MatchRules.Tv.Rounds, MatchRules.Tv.BreakSeconds, org);
        var lines = new List<string>
        {
            string.Create(CultureInfo.InvariantCulture, $"{rules.Rounds} ラウンド × {rules.RoundSeconds:0} 秒・休み {rules.BreakSeconds:0} 秒"),
            string.Join("・", new[]
            {
                org.HasWeightClasses ? $"階級制（計量・{org.Divisions.Count} 階級）" : "無差別級",
                org.CountsDowns ? (match.KnockdownLimit > 0 ? $"{match.KnockdownLimit} ノックダウン制" : "ダウンを数える") : "ダウンを数えない",
                match.JudgesDecision ? "判定あり" : "判定なし（決着か両者生還）",
            }),
            string.Join("・", new[]
            {
                match.Referee ? "レフェリーあり" : "レフェリーなし",
                match.Arena == ArenaShape.HexCage ? "六角形の檻" : "四角いリング",
                match.Regrowth is not null ? "休みに傷が前線から再生" : match.RegenVoxelsPerSecond > 0f ? "休みに浅い傷が治る" : "休みに傷は治らない",
            }),
        };
        if (tournament)
        {
            var entry = org.Entry;
            var gear = entry.Gear is { } g ? $"{FighterCharacter.WearLabel(g)}を着ける" : "手の物は選手のまま";
            lines.Add($"{(entry.Weapons ? "武器あり" : "武器なし")}・{gear}{(entry.Textbooks is null ? "" : "・流派の制限あり")}");
        }
        else
        {
            lines.Add("誰とでも組める・手の物は選手のまま");
        }

        return lines;
    }
}
