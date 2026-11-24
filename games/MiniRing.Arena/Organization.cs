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
/// <param name="LiveTag">試合中の画面の隅の札の言葉（地上波は LIVE、深夜の有料配信は PPV）。番組の型が使う。</param>
/// <param name="Strapline">札の下の小さな 1 行（番組の性格。無ければ空）。</param>
/// <param name="IntervalSign">休みの看板の言葉（VFC「再生インターバル」。使わない番組の型は空）。</param>
/// <param name="ColdOpen">番組の冒頭に 1 行ずつ出す字幕（VFC「ノーレフェリー」…。使わない番組の型は空）。</param>
internal sealed record OrgText(string OpeningRule, string FinishWord, string DecisionLine, string LiveTag = "LIVE", string Strapline = "", string IntervalSign = "", IReadOnlyList<string>? ColdOpen = null)
{
    /// <summary>冒頭の字幕（無ければ空）。</summary>
    public IReadOnlyList<string> ColdOpenLines => ColdOpen ?? [];
}

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

/// <summary>放送席の声（<c>tools/commentary/Synthesize.ps1</c> の <c>speakers</c> の 1 話し手と同じ形）。</summary>
/// <param name="Voice">声の名前の断片（Windows の声の Id・表示名に含まれる文字。先に合った物を使う）。</param>
/// <param name="Rate">読む速さ（SSML の prosody。「+30%」）。</param>
/// <param name="Pitch">声の高さ（「+5%」）。</param>
/// <param name="Volume">音量（「+0%」）。</param>
/// <param name="Excited">声が高ぶったとき（台詞の <c>excited</c>）の速さ・高さ・音量。</param>
internal sealed record OrgVoice(IReadOnlyList<string> Voice, string Rate, string Pitch, string Volume, OrgVoiceProsody Excited);

/// <summary>声の速さ・高さ・音量（高ぶったとき）。</summary>
internal sealed record OrgVoiceProsody(string Rate, string Pitch, string Volume);

/// <summary>放送席の 1 席（実況・解説）: 名前・人物の説明のファイル・席の役・声。</summary>
/// <param name="Name">呼び名（相方が呼ぶ名前。中継の字幕・声の名簿）。</param>
/// <param name="Persona">人物の説明のファイル（リポジトリの根から。経歴・役割・場だけを書く: <c>docs/LLM_CONTENT.md</c> §1）。</param>
/// <param name="Role">席の役を 1 文で（LLM への指示の頭に置く。口ぐせ・言い回しは書かない）。</param>
/// <param name="Voice">声。</param>
internal sealed record OrgSeat(string Name, string Persona, string Role, OrgVoice Voice);

/// <summary>団体の放送席（実況と解説）。台本を作る道具（<c>tools/tvshow</c>）が団体のファイルからこれを読む。</summary>
internal sealed record OrgCast(OrgSeat Announcer, OrgSeat Commentator);

/// <summary>
/// 団体の試合の決まり（ルールの値。体・物理は団体によらず同じ）: 最終ラウンドの終わりに判定するか（<see cref="MatchRules.JudgesDecision"/>）・
/// 休みの間に削れた体が予算で治るか（<see cref="MatchRules.RegenVoxelsPerSecond"/>、WVC）・レフェリーがいるか（<see cref="BoxingCorners.Referee"/>）・場の形（<see cref="ArenaShape"/>）・
/// 傷の前線の再生（<see cref="Regrowth"/>、VFC。書かなければしない）・休みに失った武器を支給するか（<see cref="ResupplyWeapons"/>）。
/// ダウンを数えるかは <see cref="Organization.CountsDowns"/>（下書き・画面と共有）。
/// 1 ラウンドにこの数のダウンで TKO（<see cref="Organization"/> の <c>KnockdownLimit</c> = <see cref="MatchRules.KnockdownLimit"/>。0 なら無い、IVF は 3）。
/// </summary>
internal sealed record OrgMatch(bool JudgesDecision, bool Regenerates, bool Referee, ArenaShape Arena, OrgRegrowth? Regrowth = null, bool ResupplyWeapons = false,
    float ConcussionSpinScale = 1f, float LimbBendStrengthScale = 1f, int KnockdownLimit = 0);

/// <summary>
/// 傷の前線の再生の値（団体のファイルの <c>match.regrowth</c>）: 前線の速さ [m/s]（<see cref="MatchRules.RegrowthSpeed"/>）・抽選の間隔 [s]
/// （<see cref="MatchRules.RegrowthTickSeconds"/>）・両者ダウンで立てない間に倍になる時間 [s]（<see cref="MatchRules.StuckDoublingSeconds"/>）。
/// </summary>
internal sealed record OrgRegrowth(float Speed, float TickSeconds, float StuckDoublingSeconds);

/// <summary>
/// 出場の条件（団体のファイルの <c>entry</c>）。条件は選手の見える物（はめる物・武器・教科書）だけで決め、強さの値は見ない（CLAUDE.md §1.7）。
/// </summary>
/// <param name="Gear">団体が着けさせるはめる物（IVF はグローブ）。null なら選手のはめる物のまま。</param>
/// <param name="Weapons">武器を握った選手が出られるか。</param>
/// <param name="Textbooks">出られる教科書（スタイル）の名前。null なら何でも。</param>
internal sealed record OrgEntry(MiniRing.Weapons.HandWear? Gear, bool Weapons, IReadOnlyList<string>? Textbooks);

/// <summary>階級（団体のファイルの <c>divisions</c>。軽い順）。</summary>
/// <param name="Id">階級の id（王座の記録が引く）。</param>
/// <param name="Name">画面と実況の名前（「ミドル級」「無差別級」）。</param>
/// <param name="LimitKg">体重の上限 [kg]（その値ちょうどまで入る）。null なら上限なし（最も重い階級・無差別級）。</param>
internal sealed record OrgDivision(string Id, string Name, int? LimitKg);

/// <summary>王座と試合の呼び方（<c>{org}</c>・<c>{division}</c> を差し込む）。</summary>
/// <param name="Belt">王座の名前（「{org} {division}王座」）。</param>
/// <param name="TitleBout">王者が出る試合の見出し（「{division}タイトルマッチ」）。</param>
/// <param name="Bout">王者が出ない試合の見出し（「{division}」。空なら出さない）。</param>
internal sealed record OrgTitles(string Belt, string TitleBout, string Bout);

/// <summary>ランキングの付け方（団体のファイルの <c>ranking</c>）。</summary>
/// <param name="MembersOnly">true なら所属（キャラの <c>org</c>）の選手だけを順位に入れる。false なら出場の条件を満たす名簿の全員（計量で振り分ける）。</param>
/// <param name="Win">勝ちの点。</param>
/// <param name="Draw">引き分けの点。</param>
/// <param name="FinishBonus">判定でなく倒して勝ったときに足す点。</param>
internal sealed record OrgRanking(bool MembersOnly, float Win, float Draw, float FinishBonus);

/// <summary>
/// 中継の主催団体（<c>games/MiniRing.Arena/Orgs/&lt;id&gt;.json</c>、2026-11-18）。画面の色と名前・紋章・ラウンドの合図の音・会場の雰囲気・
/// 台本の資料と下書きの言い回し・下見の点数の重みを、コードの分岐ではなく値で切り替える（CLAUDE.md §1.3・§1.8 と同じ考え方）。
/// 体・物理・材料は団体によらず同じで、違うのは中継の見せ方（と、試合の決まり・場の形は別の仕組み）。
/// </summary>
internal sealed record Organization(string Id, string Name, string LogoText, string Sport, string Emblem, OrgColors Colors, ArenaMood ArenaMood, string Bell, OrgCrowd Crowd, bool CountsDowns,
    OrgText Text, IReadOnlyList<string> RulesSheet, OrgDraft Draft, IReadOnlyList<string> CallWords, DramaWeights Drama, OrgCommentary Commentary, OrgMatch Match, string Show, OrgCast Cast,
    OrgEntry Entry, IReadOnlyList<OrgDivision> Divisions, OrgTitles Titles, OrgRanking Ranking)
{
    /// <summary>階級制か（階級が 2 つ以上）。階級制なら、違う階級の選手どうしの試合は組めない。</summary>
    public bool HasWeightClasses => Divisions.Count > 1;

    /// <summary>体重 <paramref name="weightKg"/> の入る階級（上限がその体重以上の、いちばん軽い階級）。</summary>
    public OrgDivision DivisionOf(int weightKg) => Divisions.First(d => d.LimitKg is not { } limit || weightKg <= limit);

    /// <summary>階級を id で引く。</summary>
    public OrgDivision Division(string id)
        => Divisions.FirstOrDefault(d => d.Id == id) ?? throw new ArgumentException($"{LogoText} に階級 '{id}' はありません（{string.Join(", ", Divisions.Select(d => d.Id))}）。", nameof(id));

    /// <summary>王座の名前（「IVF ミドル級王座」）。</summary>
    public string BeltName(OrgDivision division) => Fill(Titles.Belt, division);

    /// <summary>試合の見出し（王者が出るなら「ミドル級タイトルマッチ」、出ないなら「ミドル級」。空もある）。</summary>
    public string BoutName(OrgDivision division, bool titleBout) => Fill(titleBout ? Titles.TitleBout : Titles.Bout, division);

    private string Fill(string format, OrgDivision division)
    {
        ArgumentNullException.ThrowIfNull(division);
        return format.Replace("{org}", LogoText, StringComparison.Ordinal).Replace("{division}", division.Name, StringComparison.Ordinal).Trim();
    }

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

    /// <summary>番組の型の id（<c>show</c>）の一覧。ファイルの検査に使う。</summary>
    public static IReadOnlyList<string> ShowIds => ShowFormats.Ids;

    /// <summary>ラウンドの合図の音の id（<c>bell</c>）の一覧。ファイルの検査に使う。</summary>
    public static readonly IReadOnlyList<string> BellIds = MiniRing.Audio.BellSound.Ids;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
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
        public required OrgMatch Match { get; init; }
        public required string Show { get; init; }
        public required CastDto Cast { get; init; }
        public required EntryDto Entry { get; init; }
        public required OrgDivision[] Divisions { get; init; }
        public required OrgTitles Titles { get; init; }
        public required OrgRanking Ranking { get; init; }

        public Organization ToOrganization()
        {
            Need(Id, "id");
            if (!Id.All(ch => ch is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '_'))
            {
                throw new InvalidDataException($"id '{Id}' は小文字・数字・-・_ だけにしてください。");
            }

            if (Match.Regrowth is { } regrowth && !(regrowth.Speed >= 0f && regrowth.TickSeconds > 0f && regrowth.StuckDoublingSeconds >= 0f))
            {
                throw new InvalidDataException("match.regrowth は speed ≥ 0・tickSeconds > 0・stuckDoublingSeconds ≥ 0 にしてください。");
            }

            Need(Name, "name");
            Need(LogoText, "logoText");
            Need(Sport, "sport");
            if (!EmblemIds.Contains(Emblem))
            {
                throw new InvalidDataException($"emblem '{Emblem}' は知りません（{string.Join(", ", EmblemIds)}）。");
            }

            if (!ShowIds.Contains(Show))
            {
                throw new InvalidDataException($"show '{Show}' は知りません（{string.Join(", ", ShowIds)}）。");
            }

            if (!BellIds.Contains(Bell))
            {
                throw new InvalidDataException($"bell '{Bell}' は知りません（{string.Join(", ", BellIds)}）。");
            }

            ArenaMood.Validate();
            if (Match.KnockdownLimit < 0)
            {
                throw new InvalidDataException("match.knockdownLimit は 0 以上にしてください（0 = 無し）。");
            }

            ValidateDivisions(Divisions);
            Need(Titles?.Belt, "titles.belt");
            Need(Titles?.TitleBout, "titles.titleBout");
            if (Titles!.Bout is null)
            {
                throw new InvalidDataException("titles.bout がありません（出さないなら空の文字にしてください）。");
            }

            if (Ranking is null || !float.IsFinite(Ranking.Win) || !float.IsFinite(Ranking.Draw) || !float.IsFinite(Ranking.FinishBonus)
                || Ranking.Win <= Ranking.Draw || Ranking.Draw < 0f || Ranking.FinishBonus < 0f)
            {
                throw new InvalidDataException("ranking は 勝ち > 引き分け ≥ 0、finishBonus ≥ 0 の有限値にしてください。");
            }
            if (RulesSheet.Length == 0 || RulesSheet.Any(string.IsNullOrWhiteSpace))
            {
                throw new InvalidDataException("rulesSheet は空でない文の並びにしてください。");
            }

            if (Lint.CallWords is not { Length: > 0 } || Lint.CallWords.Any(string.IsNullOrWhiteSpace))
            {
                throw new InvalidDataException("lint.callWords は空でない言葉の並びにしてください。");
            }

            return new Organization(Id, Name, LogoText, Sport, Emblem, Colors.ToColors(), ArenaMood, Bell, Crowd.ToCrowd(), CountsDowns, Text.ToText(), RulesSheet, Draft.ToDraft(), Lint.CallWords,
                Drama.ToWeights(), Commentary.ToCommentary(), Match, Show, Cast.ToCast(),
                Entry.ToEntry(), Divisions, Titles, Ranking);
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

    private sealed class EntryDto
    {
        public string? Gear { get; init; }
        public required bool Weapons { get; init; }
        public string[]? Textbooks { get; init; }

        public OrgEntry ToEntry()
        {
            MiniRing.Weapons.HandWear? gear;
            try
            {
                gear = Gear is null ? null : MiniRing.Weapons.HandWears.Parse(Gear);
            }
            catch (ArgumentException ex)
            {
                throw new InvalidDataException($"entry.gear '{Gear}' は知りません（{MiniRing.Weapons.HandWears.Names}）。", ex);
            }

            if (Textbooks is { } books && (books.Length == 0 || books.Any(b => !MiniRing.Bots.BotCatalog.Styles.Any(s => s.Name == b))))
            {
                throw new InvalidDataException($"entry.textbooks は BotCatalog のスタイルの名前の並びにしてください（{string.Join(", ", MiniRing.Bots.BotCatalog.Styles.Select(s => s.Name))}）。何でもよければ書かない。");
            }

            return new OrgEntry(gear, Weapons, Textbooks);
        }
    }

    /// <summary>階級は 1 つ以上・id が重ならない・軽い順・上限なしは最後の 1 つだけ。</summary>
    private static void ValidateDivisions(OrgDivision[]? divisions)
    {
        if (divisions is not { Length: > 0 })
        {
            throw new InvalidDataException("divisions は 1 つ以上の階級にしてください（階級の無い団体は上限なしの 1 つ）。");
        }

        for (var i = 0; i < divisions.Length; i++)
        {
            var d = divisions[i];
            Need(d.Id, $"divisions[{i}].id");
            Need(d.Name, $"divisions[{i}].name");
            if (divisions.Take(i).Any(o => o.Id == d.Id))
            {
                throw new InvalidDataException($"divisions の id '{d.Id}' が重複しています。");
            }

            var last = i == divisions.Length - 1;
            if (last != d.LimitKg is null)
            {
                throw new InvalidDataException("divisions は最後の 1 つだけを上限なし（limitKg: null）にしてください（どの体重もどこかの階級に入るように）。");
            }

            if (d.LimitKg is { } limit && (limit <= 0 || (i > 0 && divisions[i - 1].LimitKg >= limit)))
            {
                throw new InvalidDataException($"divisions[{i}].limitKg {limit} は正で、軽い順（前の階級より重い）にしてください。");
            }
        }
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
        public string? LiveTag { get; init; }
        public string? Strapline { get; init; }
        public string? IntervalSign { get; init; }
        public List<string>? ColdOpen { get; init; }

        public OrgText ToText() => new(Need(OpeningRule, "text.openingRule"), Need(FinishWord, "text.finishWord"), Need(DecisionLine, "text.decisionLine"),
            string.IsNullOrWhiteSpace(LiveTag) ? "LIVE" : LiveTag, Strapline ?? "", IntervalSign ?? "", ColdOpen?.Where(l => !string.IsNullOrWhiteSpace(l)).ToList() ?? []);
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

    private sealed class CastDto
    {
        public required SeatDto Announcer { get; init; }
        public required SeatDto Commentator { get; init; }

        public OrgCast ToCast() => new(Announcer.ToSeat("cast.announcer"), Commentator.ToSeat("cast.commentator"));
    }

    private sealed class SeatDto
    {
        public required string Name { get; init; }
        public required string Persona { get; init; }
        public required string Role { get; init; }
        public required VoiceDto Voice { get; init; }

        public OrgSeat ToSeat(string field) => new(Need(Name, $"{field}.name"), Need(Persona, $"{field}.persona"), Need(Role, $"{field}.role"), Voice.ToVoice($"{field}.voice"));
    }

    private sealed class VoiceDto
    {
        public required string[] Voice { get; init; }
        public required string Rate { get; init; }
        public required string Pitch { get; init; }
        public required string Volume { get; init; }
        public required ProsodyDto Excited { get; init; }

        public OrgVoice ToVoice(string field)
        {
            if (Voice is not { Length: > 0 } || Voice.Any(string.IsNullOrWhiteSpace))
            {
                throw new InvalidDataException($"{field}.voice は空でない声の名前の断片の並びにしてください。");
            }

            return new OrgVoice(Voice, Percent(Rate, $"{field}.rate"), Percent(Pitch, $"{field}.pitch"), Percent(Volume, $"{field}.volume"), Excited.ToProsody($"{field}.excited"));
        }
    }

    private sealed class ProsodyDto
    {
        public required string Rate { get; init; }
        public required string Pitch { get; init; }
        public required string Volume { get; init; }

        public OrgVoiceProsody ToProsody(string field) => new(Percent(Rate, $"{field}.rate"), Percent(Pitch, $"{field}.pitch"), Percent(Volume, $"{field}.volume"));
    }

    /// <summary>SSML の prosody の割合（「+30%」「-2%」）。</summary>
    private static string Percent(string? value, string field)
        => value is not null && System.Text.RegularExpressions.Regex.IsMatch(value, @"^[+-]\d{1,3}%$")
            ? value
            : throw new InvalidDataException($"{field} は「+30%」「-2%」の形で書いてください（{value}）。");

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
