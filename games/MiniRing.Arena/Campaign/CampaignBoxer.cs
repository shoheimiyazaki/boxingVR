using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MiniRing.Characters;
using MiniRing.Weapons;

namespace MiniRing.Arena.Campaign;

/// <summary>試合の結果（ボクサーの側から）。</summary>
internal enum BoutResult { Win, Loss, Draw }

/// <summary>終わった試合の結果（赤 = ボクサーの側から）。<paramref name="Finish"/> は倒して決まったか（判定・引き分けでない）。</summary>
internal readonly record struct BoutOutcome(BoutResult Result, bool Finish)
{
    /// <summary>試合の勝ちの数と判定の言葉から（終わっていなければ null）。K.O. は判定の言葉が無い。</summary>
    public static BoutOutcome? Of(bool matchOver, int redWins, int blueWins, string? decision) => matchOver
        ? new BoutOutcome(redWins > blueWins ? BoutResult.Win : blueWins > redWins ? BoutResult.Loss : BoutResult.Draw, decision is null && redWins != blueWins)
        : null;
}

/// <summary>キャンペーンの 1 試合の記録。</summary>
/// <param name="Event">大会の id。</param>
/// <param name="Org">団体の id。</param>
/// <param name="Division">階級の id（計量の結果）。</param>
/// <param name="Opponent">相手（名簿のキャラの id）。</param>
/// <param name="Result">結果。</param>
/// <param name="Finish">倒して決まったか（判定・引き分けでなく）。</param>
/// <param name="WeightKg">計量の体重 [kg]。</param>
/// <param name="Weapon">持った武器（素手なら null。ファイルの名前: <see cref="CharacterFile.WeaponName"/>）。</param>
/// <param name="SeatAfter">試合の後の席（<see cref="CampaignLadder.SeatName"/>）。</param>
internal sealed record CampaignBout(string Event, string Org, string Division, string Opponent, BoutResult Result, bool Finish, int WeightKg, string? Weapon, int? SeatAfter);

/// <summary>団体の階級ごとの席（0 = 王者、1.. = ランキング）。</summary>
internal sealed record CampaignSeat(string Org, string Division, int Seat);

/// <summary>体重の変更（減量・増量）の記録: 何試合目の後に、何 kg から何 kg へ。</summary>
internal sealed record WeightChange(int AfterBouts, int FromKg, int ToKg);

/// <summary>戦績（試合の記録・席・体重の変更）。試合の中では変わらず、試合と試合の間にだけ書き足す（CLAUDE.md §1.6）。</summary>
internal sealed record CampaignCareer(IReadOnlyList<CampaignBout> Bouts, IReadOnlyList<CampaignSeat> Seats, IReadOnlyList<WeightChange> WeightLog)
{
    public static CampaignCareer Empty { get; } = new([], [], []);

    public int Wins => Bouts.Count(b => b.Result == BoutResult.Win);
    public int Losses => Bouts.Count(b => b.Result == BoutResult.Loss);
    public int Draws => Bouts.Count(b => b.Result == BoutResult.Draw);

    /// <summary>「3勝1敗0分」。</summary>
    public string RecordText => string.Create(CultureInfo.InvariantCulture, $"{Wins}勝{Losses}敗{Draws}分");

    /// <summary>この団体・階級の席（無ければ null = ランキング外）。</summary>
    public int? SeatIn(string org, string division) => Seats.FirstOrDefault(s => s.Org == org && s.Division == division)?.Seat;

    /// <summary>席を置き換えた戦績（null なら外す）。</summary>
    public CampaignCareer WithSeat(string org, string division, int? seat)
    {
        var rest = Seats.Where(s => !(s.Org == org && s.Division == division));
        return this with { Seats = seat is { } v ? [.. rest, new CampaignSeat(org, division, v)] : [.. rest] };
    }
}

/// <summary>
/// キャンペーンのボクサー 1 人: 名簿と同じ形のキャラクター（体 ＋ 教科書 ＋ 教育、§1.8）と戦績。強さは体からだけ出る（§1.7）。
/// 変わるのは試合と試合の間だけ（<see cref="AfterBout"/>・<see cref="ChangeWeight"/>、どちらも新しい値を返す）。
/// </summary>
internal sealed record CampaignBoxer(FighterCharacter Character, CampaignCareer Career)
{
    public string Id => Character.Id;

    /// <summary>計量の体重 [kg]（見えている体から）。</summary>
    public int WeightKg => OrgBouts.WeightOf(Character);

    /// <summary>この骨格で目標にできる体重の範囲 [kg]。</summary>
    public (int Min, int Max) WeightRange => BodySizing.WeightRange(Character.Build);

    /// <summary>
    /// 試合を記録した新しいボクサー（席も動かす: <see cref="CampaignLadder.SeatAfter"/>）。<paramref name="rankedCount"/> はその階級の記録の順位の人数。
    /// </summary>
    public CampaignBoxer AfterBout(CampaignEntry entry, BoutResult result, bool finish, WeaponKind? weapon, int rankedCount)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var org = entry.Event.Org;
        var division = entry.Division.Id;
        var seat = CampaignLadder.SeatAfter(entry.Event.Tier, Career.SeatIn(org, division), result, rankedCount);
        var bout = new CampaignBout(entry.Event.Id, org, division, entry.Opponent.Id, result, finish && result != BoutResult.Draw, WeightKg,
            weapon is { } w ? CharacterFile.WeaponName(w) : null, seat);
        return this with { Career = Career.WithSeat(org, division, seat) with { Bouts = [.. Career.Bouts, bout] } };
    }

    /// <summary>
    /// 減量・増量（試合と試合の間）: 骨格はそのまま、筋肉の太さだけを変えて計量の体重を <paramref name="targetKg"/> にする（<see cref="BodySizing.WithWeight"/>）。
    /// 力は太さの断面から同じ式で出るので、減量すれば力も落ちる。作れない体重は理由を書いた例外。
    /// </summary>
    public CampaignBoxer ChangeWeight(int targetKg)
    {
        var from = WeightKg;
        if (targetKg == from) { return this; }
        var build = BodySizing.WithWeight(Character.Build, targetKg);
        var profile = Character.Profile is { } p ? p with { WeightKg = CharacterProfile.WeightOf(build) } : null;
        return new CampaignBoxer(Character with { Build = build, Profile = profile },
            Career with { WeightLog = [.. Career.WeightLog, new WeightChange(Career.Bouts.Count, from, CharacterProfile.WeightOf(build))] });
    }
}

/// <summary>
/// ボクサーの置き場（1 人 1 ファイル <c>&lt;id&gt;.json</c>）。中身は名簿のキャラのファイル（<see cref="CharacterFile"/>）と同じ形に、
/// 戦績の欄 <c>"career"</c> を 1 つ足したもの。読むときは <c>"career"</c> を外して名簿の読み込み（<see cref="CharacterRoster.FromFiles"/>）で確かめる。
/// 名簿の架空のキャラ（組み込み）とは別の置き場で、id は名簿と重ならない。
/// </summary>
internal sealed class CampaignStore
{
    private const string CareerKey = "career";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    private readonly IReadOnlyCollection<string> _rosterIds;

    /// <param name="directory">置き場（無ければ保存のときに作る）。</param>
    /// <param name="rosterIds">名簿の id（重ならないように・教育の相手として認める）。null なら組み込みの名簿。</param>
    public CampaignStore(string directory, IReadOnlyCollection<string>? rosterIds = null)
    {
        Directory = directory ?? throw new ArgumentNullException(nameof(directory));
        _rosterIds = rosterIds ?? [.. CharacterRoster.All.Select(c => c.Id)];
    }

    public string Directory { get; }

    /// <summary>ゲームのフォルダー名（%LOCALAPPDATA% の下。キャラメイクの道具 <c>MakerServer</c> の <c>MiniRing\characters</c> と同じ根）。</summary>
    public const string AppFolderName = "MiniRing";

    /// <summary>
    /// 既定の置き場: <c>&lt;LocalApplicationData&gt;/MiniRing/campaign</c>（Windows は <c>%LOCALAPPDATA%\MiniRing\campaign</c>、
    /// Linux は <c>~/.local/share/MiniRing/campaign</c>、macOS は <c>~/Library/Application Support/MiniRing/campaign</c>）。
    /// Roaming（%APPDATA%）でなく Local にしたのは、セーブがこの PC のゲームの記録で、ドメインのプロファイルで他の PC へ同期させる物ではないから
    /// （キャラメイクの保存と同じ根に揃える）。OS が場所を返さない（空）ときは <paramref name="fallback"/>。
    /// </summary>
    public static string DefaultDirectory(string fallback)
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify);
        return string.IsNullOrEmpty(root) ? fallback : Path.Combine(root, AppFolderName, "campaign");
    }

    /// <summary>
    /// 古い置き場（作業フォルダーの <c>campaign/</c>）のボクサーを新しい置き場へ写す（古い方は消さない）。新しい方に同じ名前があれば写さない
    /// （新しい方が正。何度呼んでも同じ）。写した数を返す。読めない古いファイルもそのまま写す（読むときにファイル名つきで知らせる）。
    /// </summary>
    public static int CopyLegacy(string legacyDirectory, string directory)
    {
        if (string.Equals(Path.GetFullPath(legacyDirectory).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase) || !System.IO.Directory.Exists(legacyDirectory))
        {
            return 0;
        }

        var copied = 0;
        foreach (var source in System.IO.Directory.GetFiles(legacyDirectory, "*.json"))
        {
            var target = Path.Combine(directory, Path.GetFileName(source));
            if (File.Exists(target)) { continue; }
            System.IO.Directory.CreateDirectory(directory);
            File.Copy(source, target, overwrite: false);
            copied++;
        }

        return copied;
    }

    /// <summary>全員（id の順）。壊れたファイルは飛ばさず、ファイル名を書いた例外で止める（黙って消えない）。</summary>
    public IReadOnlyList<CampaignBoxer> LoadAll()
    {
        if (!System.IO.Directory.Exists(Directory)) { return []; }
        return [.. System.IO.Directory.GetFiles(Directory, "*.json").Order(StringComparer.Ordinal).Select(Load)];
    }

    /// <summary>1 人を読む。</summary>
    public CampaignBoxer Load(string path)
    {
        var name = Path.GetFileName(path);
        return Parse(name, File.ReadAllText(path), _rosterIds);
    }

    /// <summary>ファイルの中身から読む（キャラクターは名簿の読み込みで確かめる）。</summary>
    public static CampaignBoxer Parse(string fileName, string json, IReadOnlyCollection<string> rosterIds)
    {
        JsonObject node;
        try
        {
            node = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })?.AsObject()
                ?? throw new InvalidDataException($"ボクサーのファイル {fileName} が空です。");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"ボクサーのファイル {fileName} の JSON が読めません: {ex.Message}", ex);
        }

        var careerNode = node[CareerKey];
        node.Remove(CareerKey);
        var character = CharacterRoster.FromFiles([(fileName, node.ToJsonString())], rosterIds).Single();
        CampaignCareer career;
        try
        {
            career = careerNode?.Deserialize<CareerDto>(Options)?.ToCareer() ?? CampaignCareer.Empty;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"ボクサーのファイル {fileName} の career が読めません: {ex.Message}", ex);
        }

        return new CampaignBoxer(character, career);
    }

    /// <summary>ファイルの中身（キャラクターのファイル ＋ <c>"career"</c>）。</summary>
    public static string Format(CampaignBoxer boxer)
    {
        ArgumentNullException.ThrowIfNull(boxer);
        var node = CharacterFile.ToNode(boxer.Character);
        node[CareerKey] = JsonSerializer.SerializeToNode(CareerDto.From(boxer.Career), Options);
        return CharacterFile.Format(node);
    }

    /// <summary>書く（同じ id は上書き）。</summary>
    public void Save(CampaignBoxer boxer)
    {
        ArgumentNullException.ThrowIfNull(boxer);
        System.IO.Directory.CreateDirectory(Directory);
        var path = PathOf(boxer.Id);
        var temp = path + ".tmp";
        File.WriteAllText(temp, Format(boxer));
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>消す（ファイルを消す。無ければ何もしない）。</summary>
    public void Delete(string id) => File.Delete(PathOf(id));

    /// <summary>
    /// 新しいボクサーを作る（キャラメイクの下書き <paramref name="draft"/> から。id・名前が空なら「新人 N」と空いた id を振る）。書いてから返す。
    /// </summary>
    public CampaignBoxer Create(CharacterDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var taken = new HashSet<string>(_rosterIds, StringComparer.Ordinal);
        foreach (var b in LoadAll()) { taken.Add(b.Id); }
        var n = 1;
        while (taken.Contains(IdOf(n))) { n++; }
        var id = string.IsNullOrWhiteSpace(draft.Id) ? IdOf(n) : draft.Id.Trim();
        if (taken.Contains(id)) { throw new InvalidDataException($"id '{id}' は名簿か置き場にもうあります。"); }
        var name = string.IsNullOrWhiteSpace(draft.Name) ? string.Create(CultureInfo.InvariantCulture, $"新人 {n}") : draft.Name;
        var (character, _) = CharacterMaker.ToFile(draft with { Id = id, Name = name, Country = string.IsNullOrWhiteSpace(draft.Country) ? "japan" : draft.Country, Order = 1 }, _rosterIds);
        var boxer = new CampaignBoxer(character, CampaignCareer.Empty);
        Save(boxer);
        return boxer;
    }

    private static string IdOf(int n) => string.Create(CultureInfo.InvariantCulture, $"my-boxer-{n}");

    private string PathOf(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || id.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException($"ボクサーの id '{id}' はファイル名に使えません。", nameof(id));
        }

        return Path.Combine(Directory, id + ".json");
    }

    private sealed class CareerDto
    {
        public CampaignBout[] Bouts { get; init; } = [];
        public CampaignSeat[] Seats { get; init; } = [];
        public WeightChange[] WeightLog { get; init; } = [];

        public CampaignCareer ToCareer() => new(Bouts, Seats, WeightLog);

        public static CareerDto From(CampaignCareer c) => new() { Bouts = [.. c.Bouts], Seats = [.. c.Seats], WeightLog = [.. c.WeightLog] };
    }
}
