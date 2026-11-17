using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MiniRing.Arena;

/// <summary>画面の色（RGBA、0〜1）。団体のファイルの <c>colors</c>。</summary>
/// <param name="Accent">題字・見出し・罫・縁の色（WVC は金、VFC はクリーム）。</param>
/// <param name="AccentLight">光る面の明るい側（ワイプの帯の端など）。</param>
/// <param name="AccentDeep">引き分けの帯の深い側。</param>
/// <param name="Text">本文の文字。</param>
/// <param name="Muted">控えめな文字（このあと・採点表の見出し）。</param>
/// <param name="Ink">帯・札の黒地。</param>
/// <param name="VoxelTop">罫の飾り（ボクセル）の面の上。</param>
/// <param name="VoxelBottom">罫の飾りの面の下。</param>
/// <param name="VoxelSide">罫の飾りの側面。</param>
internal sealed record OrgColors(Vector4 Accent, Vector4 AccentLight, Vector4 AccentDeep, Vector4 Text, Vector4 Muted, Vector4 Ink, Vector3 VoxelTop, Vector3 VoxelBottom, Vector3 VoxelSide);

/// <summary>会場の歓声の大きさ（団体の違い。1 が WVC の今の音）。</summary>
/// <param name="ReactionGain">出来事への反応（歓声・どよめき・悲鳴）にかける倍率。</param>
/// <param name="AmbienceGain">地のざわめきにかける倍率。</param>
internal sealed record OrgCrowd(float ReactionGain, float AmbienceGain);

/// <summary>画面に出す団体の言葉（冒頭の決まりの帯・決まり手の言い方）。</summary>
/// <param name="OpeningRule">冒頭の決まりの帯。<c>{seconds}</c>・<c>{rounds}</c> を差し込む。</param>
/// <param name="FinishWord">K.O. にあたる決着の大きな文字（WVC「K.O.」、VFC「決着」）。</param>
/// <param name="DecisionLine">判定・引き分けで終わったときの下の行。<c>{rounds}</c> を差し込む。</param>
internal sealed record OrgText(string OpeningRule, string FinishWord, string DecisionLine);

/// <summary>台本の下書き（<see cref="TvDraft"/>）の団体ごとの言い回し。<c>{round}</c>・<c>{victim}</c>・<c>{winner}</c> を差し込む。</summary>
/// <param name="BellWord">ラウンドの合図の呼び方（「ゴング」「ホーン」）。</param>
/// <param name="FinishCall">決着のコール（K.O.・死）の頭。</param>
/// <param name="WinLine">勝者を告げる行。</param>
/// <param name="DrawLine">引き分けで終わった試合の終了の行（空なら、記録の文をそのまま言う）。</param>
/// <param name="FallenCall">倒れたときのコール（ダウンを数えない団体）。</param>
internal sealed record OrgDraft(string BellWord, string FinishCall, string WinLine, string DrawLine, string FallenCall);

/// <summary>
/// 下見の点（<see cref="TvShowRules.Drama"/>）の重み。点は次の和（WVC の値が元の式そのまま）:
/// 長く続く = <see cref="PerRound"/> × 経過ラウンド数、ダウン = <see cref="PerDown"/> × min(数, <see cref="DownCap"/>)、
/// 両者のダウン = <see cref="BothDowns"/>、K.O. = <see cref="Ko"/>（最終ラウンドなら + <see cref="KoLastRound"/>）、
/// 死 = <see cref="Death"/> + <see cref="DeathPerRound"/> × (決着ラウンド − 1)、削られていた側の逆転勝ち = <see cref="Comeback"/>、
/// 両者が深く傷む = <see cref="BothHurt"/> × min(1, 与えたダメージの小さい方 ÷ <see cref="BothHurtDamage"/>)、
/// 第 1 ラウンドの <see cref="QuickSeconds"/> より前の決着 = −<see cref="QuickPenalty"/>、引き分け = −<see cref="DrawPenalty"/>。
/// </summary>
internal sealed record DramaWeights(float PerRound, float PerDown, int DownCap, float BothDowns, float Ko, float KoLastRound, float Death, float DeathPerRound, float Comeback,
    float BothHurt, float BothHurtDamage, float QuickSeconds, float QuickPenalty, float DrawPenalty);

/// <summary>台本を書く LLM に渡す団体の紹介と語り口（プロンプトの <c>{{org}}</c>・<c>{{orgIntro}}</c>・<c>{{announcerTone}}</c>・<c>{{analystTone}}</c>）。</summary>
/// <param name="Intro">団体の紹介の行（作中の世界での団体の名前と競技）。</param>
/// <param name="AnnouncerTone">実況の語り口。</param>
/// <param name="AnalystTone">解説の語り口。</param>
/// <param name="CallThing">実況が 1 秒以内にコールする出来事の言い方（WVC「ダウン・決着」、VFC「倒れたこと・決着」。プロンプトの <c>{{callThing}}</c>）。</param>
/// <param name="CallExamples">コールの言葉の例（プロンプトの <c>{{callExamples}}</c>）。</param>
internal sealed record OrgCommentary(string Intro, string AnnouncerTone, string AnalystTone, string CallThing, string CallExamples);

/// <summary>
/// 中継の主催団体（<c>games/MiniRing.Arena/Orgs/&lt;id&gt;.json</c>、2026-11-18）。画面の色と名前・紋章・ラウンドの合図の音・会場の雰囲気・
/// 台本の資料と下書きの言い回し・下見の点数の重みを、コードの分岐ではなく値で切り替える（CLAUDE.md §1.3・§1.8 と同じ考え方）。
/// 体・物理・材料は団体によらず同じで、違うのは中継の見せ方（と、試合の決まり・場の形は別の仕組み）。
/// </summary>
internal sealed record Organization(string Id, string Name, string LogoText, string Sport, string Emblem, OrgColors Colors, ArenaMood ArenaMood, string Bell, OrgCrowd Crowd, bool CountsDowns,
    OrgText Text, IReadOnlyList<string> RulesSheet, OrgDraft Draft, IReadOnlyList<string> CallWords, DramaWeights Drama, OrgCommentary Commentary)
{
    /// <summary>既定の団体の id。</summary>
    public const string DefaultId = "wvc";

    /// <summary>既定の団体（WVC。今の中継と同じ見た目・出力）。</summary>
    public static Organization Default => Organizations.Get(DefaultId);

    /// <summary>引数・記録の id から団体を引く。null・空なら <see cref="Default"/>。</summary>
    public static Organization Of(string? id) => string.IsNullOrWhiteSpace(id) ? Default : Organizations.Get(id);
}

/// <summary>団体の一覧（組み込みの <c>Orgs/*.json</c>）と、ファイルの読み込み・検査。</summary>
internal static class Organizations
{
    private const string Prefix = "MiniRing.Arena.Orgs.";

    private static readonly Lazy<IReadOnlyDictionary<string, Organization>> Loaded = new(() =>
    {
        var assembly = typeof(Organizations).Assembly;
        var files = new List<(string FileName, string Json)>();
        foreach (var resource in assembly.GetManifestResourceNames()
                     .Where(n => n.StartsWith(Prefix, StringComparison.Ordinal) && n.EndsWith(".json", StringComparison.Ordinal))
                     .Order(StringComparer.Ordinal))
        {
            using var stream = assembly.GetManifestResourceStream(resource)!;
            using var reader = new StreamReader(stream);
            files.Add((resource[Prefix.Length..], reader.ReadToEnd()));
        }

        return files.Count == 0
            ? throw new InvalidOperationException($"組み込みの団体のファイル {Prefix}*.json が 1 つもありません（MiniRing.Arena.csproj の EmbeddedResource）。")
            : FromFiles(files);
    });

    /// <summary>全団体（id → 団体）。</summary>
    public static IReadOnlyDictionary<string, Organization> All => Loaded.Value;

    /// <summary>id から探す。無ければ、ある id を書いた例外。</summary>
    public static Organization Get(string id)
        => All.TryGetValue(id, out var org) ? org : throw new ArgumentException($"団体 '{id}' はありません（{string.Join(", ", All.Keys.Order(StringComparer.Ordinal))}）。", nameof(id));

    /// <summary>紋章の id（<c>emblem</c>）の一覧。ファイルの検査に使う。</summary>
    public static IReadOnlyList<string> EmblemIds => Emblems.Ids;

    /// <summary>ラウンドの合図の音の id（<c>bell</c>）の一覧。ファイルの検査に使う。</summary>
    public static readonly IReadOnlyList<string> BellIds = MiniRing.Audio.BellSound.Ids;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>団体のファイル（ファイル名と JSON の組）から読んで検査する。足りない欄・型違い・範囲外・id とファイル名の食い違いは、どのファイルのどの欄かを書いた例外で止める。</summary>
    public static IReadOnlyDictionary<string, Organization> FromFiles(IEnumerable<(string FileName, string Json)> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        var result = new Dictionary<string, Organization>(StringComparer.Ordinal);
        foreach (var (fileName, json) in files)
        {
            ArgumentNullException.ThrowIfNull(json);
            Dto dto;
            try
            {
                dto = JsonSerializer.Deserialize<Dto>(json, Options) ?? throw new InvalidDataException("ファイルが空です。");
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException($"団体のファイル {fileName} が読めません: {ex.Message}", ex);
            }

            try
            {
                var org = dto.ToOrganization();
                if (Path.GetFileNameWithoutExtension(fileName) != org.Id)
                {
                    throw new InvalidDataException($"ファイル名 {fileName} と id '{org.Id}' が違います（<id>.json にしてください）。");
                }

                if (!result.TryAdd(org.Id, org))
                {
                    throw new InvalidDataException($"id '{org.Id}' が重複しています。");
                }
            }
            catch (InvalidDataException ex)
            {
                throw new InvalidDataException($"団体のファイル {fileName}: {ex.Message}", ex);
            }
        }

        return result.Count == 0 ? throw new InvalidDataException("団体のファイルがありません。") : result;
    }

    private sealed class Dto
    {
        public required string Id { get; init; }
        public required string Name { get; init; }
        public required string LogoText { get; init; }
        public required string Sport { get; init; }
        public required string Emblem { get; init; }
        public required ColorsDto Colors { get; init; }
        public required ArenaMood ArenaMood { get; init; }
        public required string Bell { get; init; }
        public required CrowdDto Crowd { get; init; }
        public required bool CountsDowns { get; init; }
        public required TextDto Text { get; init; }
        public required string[] RulesSheet { get; init; }
        public required DraftDto Draft { get; init; }
        public required LintDto Lint { get; init; }
        public required DramaDto Drama { get; init; }
        public required CommentaryDto Commentary { get; init; }

        public Organization ToOrganization()
        {
            Need(Id, "id");
            if (!Id.All(ch => ch is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '_'))
            {
                throw new InvalidDataException($"id '{Id}' は小文字・数字・-・_ だけにしてください。");
            }

            Need(Name, "name");
            Need(LogoText, "logoText");
            Need(Sport, "sport");
            if (!EmblemIds.Contains(Emblem))
            {
                throw new InvalidDataException($"emblem '{Emblem}' は知りません（{string.Join(", ", EmblemIds)}）。");
            }

            if (!BellIds.Contains(Bell))
            {
                throw new InvalidDataException($"bell '{Bell}' は知りません（{string.Join(", ", BellIds)}）。");
            }

            ArenaMood.Validate();
            if (RulesSheet.Length == 0 || RulesSheet.Any(string.IsNullOrWhiteSpace))
            {
                throw new InvalidDataException("rulesSheet は空でない文の並びにしてください。");
            }

            if (Lint.CallWords is not { Length: > 0 } || Lint.CallWords.Any(string.IsNullOrWhiteSpace))
            {
                throw new InvalidDataException("lint.callWords は空でない言葉の並びにしてください。");
            }

            return new Organization(Id, Name, LogoText, Sport, Emblem, Colors.ToColors(), ArenaMood, Bell, Crowd.ToCrowd(), CountsDowns, Text.ToText(), RulesSheet, Draft.ToDraft(), Lint.CallWords,
                Drama.ToWeights(), Commentary.ToCommentary());
        }
    }

    private sealed class ColorsDto
    {
        public required float[] Accent { get; init; }
        public required float[] AccentLight { get; init; }
        public required float[] AccentDeep { get; init; }
        public required float[] Text { get; init; }
        public required float[] Muted { get; init; }
        public required float[] Ink { get; init; }
        public required float[] VoxelTop { get; init; }
        public required float[] VoxelBottom { get; init; }
        public required float[] VoxelSide { get; init; }

        public OrgColors ToColors() => new(Rgba(Accent, "colors.accent"), Rgba(AccentLight, "colors.accentLight"), Rgba(AccentDeep, "colors.accentDeep"), Rgba(Text, "colors.text"),
            Rgba(Muted, "colors.muted"), Rgba(Ink, "colors.ink"), Rgb(VoxelTop, "colors.voxelTop"), Rgb(VoxelBottom, "colors.voxelBottom"), Rgb(VoxelSide, "colors.voxelSide"));
    }

    private sealed class CrowdDto
    {
        public required float ReactionGain { get; init; }
        public required float AmbienceGain { get; init; }

        public OrgCrowd ToCrowd()
        {
            Positive(ReactionGain, "crowd.reactionGain");
            Positive(AmbienceGain, "crowd.ambienceGain");
            return new OrgCrowd(ReactionGain, AmbienceGain);
        }
    }

    private sealed class TextDto
    {
        public required string OpeningRule { get; init; }
        public required string FinishWord { get; init; }
        public required string DecisionLine { get; init; }

        public OrgText ToText() => new(Need(OpeningRule, "text.openingRule"), Need(FinishWord, "text.finishWord"), Need(DecisionLine, "text.decisionLine"));
    }

    private sealed class DraftDto
    {
        public required string BellWord { get; init; }
        public required string FinishCall { get; init; }
        public required string WinLine { get; init; }
        public required string DrawLine { get; init; }
        public required string FallenCall { get; init; }

        public OrgDraft ToDraft() => new(Need(BellWord, "draft.bellWord"), Need(FinishCall, "draft.finishCall"), Need(WinLine, "draft.winLine"), DrawLine ?? "", Need(FallenCall, "draft.fallenCall"));
    }

    private sealed class LintDto
    {
        public required string[] CallWords { get; init; }
    }

    private sealed class DramaDto
    {
        public required float PerRound { get; init; }
        public required float PerDown { get; init; }
        public required int DownCap { get; init; }
        public required float BothDowns { get; init; }
        public required float Ko { get; init; }
        public required float KoLastRound { get; init; }
        public required float Death { get; init; }
        public required float DeathPerRound { get; init; }
        public required float Comeback { get; init; }
        public required float BothHurt { get; init; }
        public required float BothHurtDamage { get; init; }
        public required float QuickSeconds { get; init; }
        public required float QuickPenalty { get; init; }
        public required float DrawPenalty { get; init; }

        public DramaWeights ToWeights()
        {
            foreach (var (name, value) in new[]
            {
                ("perRound", PerRound), ("perDown", PerDown), ("bothDowns", BothDowns), ("ko", Ko), ("koLastRound", KoLastRound), ("death", Death), ("deathPerRound", DeathPerRound),
                ("comeback", Comeback), ("bothHurt", BothHurt), ("quickSeconds", QuickSeconds), ("quickPenalty", QuickPenalty), ("drawPenalty", DrawPenalty),
            })
            {
                if (!float.IsFinite(value) || value < 0f)
                {
                    throw new InvalidDataException($"drama.{name} は 0 以上の有限値にしてください（減点の重み quickPenalty・drawPenalty も正の値で書く）。");
                }
            }

            if (DownCap < 0)
            {
                throw new InvalidDataException("drama.downCap は 0 以上にしてください。");
            }

            Positive(BothHurtDamage, "drama.bothHurtDamage");
            return new DramaWeights(PerRound, PerDown, DownCap, BothDowns, Ko, KoLastRound, Death, DeathPerRound, Comeback, BothHurt, BothHurtDamage, QuickSeconds, QuickPenalty, DrawPenalty);
        }
    }

    private sealed class CommentaryDto
    {
        public required string Intro { get; init; }
        public required string AnnouncerTone { get; init; }
        public required string AnalystTone { get; init; }
        public required string CallThing { get; init; }
        public required string CallExamples { get; init; }

        public OrgCommentary ToCommentary() => new(Need(Intro, "commentary.intro"), Need(AnnouncerTone, "commentary.announcerTone"), Need(AnalystTone, "commentary.analystTone"),
            Need(CallThing, "commentary.callThing"), Need(CallExamples, "commentary.callExamples"));
    }

    private static string Need(string? value, string field)
        => string.IsNullOrWhiteSpace(value) ? throw new InvalidDataException($"{field} がありません（空です）。") : value;

    private static void Positive(float value, string field)
    {
        if (!float.IsFinite(value) || value <= 0f)
        {
            throw new InvalidDataException($"{field} は正の有限値にしてください。");
        }
    }

    private static Vector4 Rgba(float[] value, string field)
    {
        if (value is not { Length: 3 or 4 } || value.Any(v => !float.IsFinite(v) || v is < 0f or > 1f))
        {
            throw new InvalidDataException($"{field} は 0〜1 の有限値 3 つ（RGB）か 4 つ（RGBA）で指定してください。");
        }

        return new Vector4(value[0], value[1], value[2], value.Length == 4 ? value[3] : 1f);
    }

    private static Vector3 Rgb(float[] value, string field)
    {
        var rgba = Rgba(value, field);
        return new Vector3(rgba.X, rgba.Y, rgba.Z);
    }
}
