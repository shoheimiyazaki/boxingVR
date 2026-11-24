using System.Text;

namespace MiniRing.Characters;

/// <summary>
/// キャラクターのプロフィール（作中の世界の経歴・人柄。キャラクターの軸を作り、テレビ中継の実況・解説が参照する）。
/// 強さは持たない（CLAUDE.md §1.7: 強さは見えている体から）。身長・体重は見えている体（<see cref="BodyBuild"/>）から決まる値と一致させる
/// （<see cref="HeightOf"/>・<see cref="WeightOf"/>。見た目と紙の上の数字が食い違わないように、読み込みで確かめる）。全員架空（docs/CHARACTERS.md）。
/// </summary>
public sealed record CharacterProfile
{
    /// <summary>基準体格の身長 [cm]（<see cref="BodyBuild.Height"/> = 1）。</summary>
    public const float BaseHeightCm = 175f;

    /// <summary>基準体格の体重 [kg]。体重 = これ × 身長の比² × 太さの比（身長の 2 乗と太さに比例: 見た目の体格指数）。</summary>
    public const float BaseWeightKg = 72f;

    public int Age { get; init; }

    public int HeightCm { get; init; }

    public int WeightKg { get; init; }

    public string Birthplace { get; init; } = "";

    public string Hometown { get; init; } = "";

    public string Stance { get; init; } = "";

    public string NicknameStory { get; init; } = "";

    public FightRecord FightRecord { get; init; } = new();

    public IReadOnlyList<string> Career { get; init; } = [];

    public string Trainer { get; init; } = "";

    public string FightingStyle { get; init; } = "";

    public IReadOnlyList<string> Strengths { get; init; } = [];

    public IReadOnlyList<string> Weaknesses { get; init; } = [];

    public string Personality { get; init; } = "";

    public IReadOnlyList<string> Hobbies { get; init; } = [];

    public string Family { get; init; } = "";

    public IReadOnlyList<string> Episodes { get; init; } = [];

    public string Quote { get; init; } = "";

    public IReadOnlyList<string> CommentaryNotes { get; init; } = [];

    /// <summary>見えている体の身長 [cm]。</summary>
    public static int HeightOf(BodyBuild build)
    {
        if (!build.FourLegged)
        {
            return Rounded(BaseHeightCm * build.Height);
        }

        var tuning = FighterTuning.Default with { Build = build };
        var h = tuning.HalfExtents;
        var height = (2f * (h.Foot.Y + h.Shin.Y + h.Thigh.Y + h.Pelvis.Y + h.Chest.Y + h.Head.Y)) - (5f * tuning.JointOverlap);
        return Rounded(height * 100f);
    }

    /// <summary>見えている体の体重 [kg]。</summary>
    public static int WeightOf(BodyBuild build)
    {
        if (!build.FourLegged)
        {
            return Rounded(BaseWeightKg * build.Height * build.Height * build.Muscle);
        }

        var m = (FighterTuning.Default with { Build = build }).Masses;
        return Rounded(m.Pelvis + m.Chest + m.Head + (2f * (m.UpperArm + m.Forearm))
            + (4f * (m.Thigh + m.Shin + (m.Foot / (1f - FootGeometry.ToeShare)))));
    }

    private static int Rounded(float value) => (int)MathF.Round(value, MidpointRounding.AwayFromZero);

    /// <summary>中身を確かめる（足りない項目・体と合わない身長と体重・ありえない戦績は例外）。</summary>
    public CharacterProfile Validate(BodyBuild build)
    {
        ArgumentNullException.ThrowIfNull(build);
        if (Age is < 16 or > 70)
        {
            throw new InvalidDataException($"profile.age {Age} は 16〜70 にしてください。");
        }

        if (HeightCm != HeightOf(build) || WeightKg != WeightOf(build))
        {
            throw new InvalidDataException(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"profile の身長・体重（{HeightCm} cm・{WeightKg} kg）が見えている体（{HeightOf(build)} cm・{WeightOf(build)} kg）と合いません。"));
        }

        if (FightRecord.Wins < 0 || FightRecord.Losses < 0 || FightRecord.Draws < 0 || FightRecord.Kos < 0 || FightRecord.Kos > FightRecord.Wins)
        {
            throw new InvalidDataException("profile.fightRecord は 0 以上で、K.O. は勝ちの数以下にしてください。");
        }

        foreach (var (value, field) in new[] { (Birthplace, "birthplace"), (Hometown, "hometown"), (Stance, "stance"), (NicknameStory, "nicknameStory"), (Trainer, "trainer"),
            (FightingStyle, "fightingStyle"), (Personality, "personality"), (Family, "family"), (Quote, "quote") })
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidDataException($"profile.{field} がありません。");
            }
        }

        foreach (var (list, field) in new[] { (Career, "career"), (Strengths, "strengths"), (Weaknesses, "weaknesses"), (Hobbies, "hobbies"), (Episodes, "episodes"), (CommentaryNotes, "commentaryNotes") })
        {
            if (list.Count == 0 || list.Any(string.IsNullOrWhiteSpace))
            {
                throw new InvalidDataException($"profile.{field} が空です。");
            }
        }

        return this;
    }

    /// <summary>実況・解説の資料（テレビ中継の記録と台本の資料に入れる、読みやすい形）。</summary>
    public string Briefing(string name, string hudName, string tagline)
    {
        var r = FightRecord;
        var sb = new StringBuilder();
        sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"{name}（{hudName}）— {tagline}");
        sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"- {Age}歳・{HeightCm} cm・{WeightKg} kg・{Stance}。出身 {Birthplace}、拠点 {Hometown}");
        sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"- 戦績 {r.Wins}勝（{r.Kos} K.O.）{r.Losses}敗{(r.Draws > 0 ? $"{r.Draws}分" : "")}。トレーナー: {Trainer}");
        sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"- リングネームの由来: {NicknameStory}");
        sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"- 戦い方: {FightingStyle}");
        sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"- 強み: {string.Join("／", Strengths)}。弱み: {string.Join("／", Weaknesses)}");
        sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"- 人柄: {Personality} 趣味: {string.Join("、", Hobbies)}");
        sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"- 家族・生い立ち: {Family}");
        sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"- 口癖: 「{Quote}」");
        sb.AppendLine("- 経歴:");
        foreach (var c in Career)
        {
            sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"  - {c}");
        }

        sb.AppendLine("- エピソード:");
        foreach (var e in Episodes)
        {
            sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"  - {e}");
        }

        sb.AppendLine("- 中継の小ネタ:");
        foreach (var n in CommentaryNotes)
        {
            sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"  - {n}");
        }

        return sb.ToString();
    }
}

/// <summary>プロの戦績（作中の記録）。</summary>
public sealed record FightRecord
{
    public int Wins { get; init; }

    public int Losses { get; init; }

    public int Draws { get; init; }

    public int Kos { get; init; }
}
