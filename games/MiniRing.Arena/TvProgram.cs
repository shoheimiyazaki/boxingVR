using System.Text.Json;
using System.Text.Json.Serialization;

namespace MiniRing.Arena;

/// <summary>
/// 大会のプログラム（その日の試合の順と、済んだ試合の結果）。テレビ中継の冒頭に「本日のプログラム」の表として出し、
/// 実況の下書き（<see cref="TvDraft"/>）が済んだ試合の結果とこの試合に懸かるもの（「勝てば決勝」）を言う。
/// 描画と台本だけに使い、試合には響かない。トーナメントの勝ち上がり（どの試合の勝者が次に誰と戦うか）は
/// 札（tools/tvshow/cards/*.json）から tvshow.ps1 が決めて、試合ごとにこのファイルを書く（docs/TV_BROADCAST.md §2）。
/// </summary>
/// <param name="Title">表の見出し（「WVC ボクシング王座トーナメント」）。</param>
/// <param name="Bouts">その日の試合（順に）。</param>
internal sealed record TvProgram(string Title, IReadOnlyList<TvProgramBout> Bouts)
{
    /// <summary>今の試合（<see cref="TvProgramBout.Current"/>）。無ければ null。</summary>
    [JsonIgnore]
    public TvProgramBout? Current => Bouts.FirstOrDefault(b => b.Current);

    /// <summary>済んだ試合（勝者が決まった順）。</summary>
    [JsonIgnore]
    public IEnumerable<TvProgramBout> Finished => Bouts.Where(b => b.Winner is not null);

    /// <summary>
    /// 表を出す長さ [s]: 見出しの台詞（約 2 秒）と、済んだ試合の結果・この試合に懸かるものを実況が 1 行ずつ言える分
    /// （1 行 4 秒。OneCore の声の実測 2026-11-16: 「準決勝第1試合はザ・ハンマーのKO勝ち。」が 3.8 秒）。
    /// 最短 6 秒（表が滑り込んで読める長さ）。
    /// </summary>
    [JsonIgnore]
    public float Seconds => MathF.Max(6f, 2.6f + (4f * (Finished.Count() + (Current is { Stake.Length: > 0 } ? 1 : 0))));

    /// <summary>
    /// 実況・解説の資料（<see cref="TvDigest"/> と、プロスペクティブ制の選手の資料 fighters.md）に書く「本日のプログラムとこの試合に懸かるもの」（Markdown の節）。
    /// この試合に懸かるもの・王座・勝者が得るもの、済んだ試合の結果、この後の試合の顔ぶれ。この後の試合の結果は表にまだ無いので書かない。
    /// 懸かるものを渡さないと、王座決定戦のメインで実況が一度も王座に触れなかった（2026-11-19 の WVC の番組）。
    /// </summary>
    public string Briefing()
    {
        var lines = new List<string> { "## 本日のプログラムとこの試合に懸かるもの", "" };
        if (Title.Length > 0)
        {
            lines.Add($"- 大会: {Title}");
        }

        if (Current is { } current)
        {
            lines.Add($"- この試合: {current.Bout}（赤 {current.Red} 対 青 {current.Blue}）");
            if (current.Stake.Length > 0)
            {
                lines.Add($"- この試合に懸かるもの: {current.Stake}");
            }

            if (current.Belt)
            {
                lines.Add("- 王座（チャンピオンベルト）が懸かった試合。勝った方がベルトを巻く");
            }

            if (current.Next.Length > 0)
            {
                lines.Add($"- 勝った方が得るもの: {current.Next}");
            }
        }

        var index = Current is { } now ? Bouts.ToList().IndexOf(now) : Bouts.Count;
        foreach (var done in Finished)
        {
            lines.Add($"- 済んだ試合: {done.Bout}（{done.Red} 対 {done.Blue}）は{done.WinnerName}の勝ち（{(done.Cause is { Length: > 0 } cause ? $"{cause}、" : "")}{done.Method}）{(done.Next.Length > 0 ? $"。{done.WinnerName}は{done.Next}" : "")}");
        }

        foreach (var later in Bouts.Skip(index + 1).Where(b => b.Winner is null))
        {
            lines.Add($"- この後の試合: {later.Bout}（{later.Red} 対 {later.Blue}）{(later.Stake.Length > 0 ? $"。懸かるもの: {later.Stake}" : "")}");
        }

        return string.Join('\n', lines) + '\n';
    }

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// プログラムの JSON を読む。<c>red</c>・<c>blue</c> が名簿のキャラクターの id なら画面の短い名前（<c>hudName</c>）に、
    /// そうでなければ書いたまま（「準決勝第1試合の勝者」）にする。
    /// </summary>
    public static TvProgram FromJson(string json, Func<string, string?> nameOf)
    {
        ArgumentNullException.ThrowIfNull(nameOf);
        var dto = JsonSerializer.Deserialize<ProgramDto>(json, Options) ?? throw new InvalidDataException("プログラムが空です。");
        if (dto.Bouts is not { Count: > 0 })
        {
            throw new InvalidDataException("プログラムに bouts がありません。");
        }

        var bouts = dto.Bouts.Select((b, i) =>
        {
            var label = string.IsNullOrWhiteSpace(b.Bout) ? $"第{i + 1}試合" : b.Bout;
            var red = Required(b.Red, label, "red");
            var blue = Required(b.Blue, label, "blue");
            var winner = b.Winner switch
            {
                null or "" => null,
                "red" or "blue" => b.Winner,
                _ => throw new InvalidDataException($"{label} の winner は red か blue です（{b.Winner}）。"),
            };
            return new TvProgramBout(label, nameOf(red) ?? red, nameOf(blue) ?? blue, winner, b.Method ?? "", b.Current, b.Stake ?? "", b.Next ?? "", string.IsNullOrEmpty(b.Cause) ? null : b.Cause, b.Belt);
        }).ToList();
        if (bouts.Count(b => b.Current) > 1)
        {
            throw new InvalidDataException("プログラムの current は 1 試合だけです。");
        }

        return new TvProgram(dto.Title ?? "", bouts);
    }

    /// <summary>名簿のキャラクターの短い名前（id でなければ null）。</summary>
    public static string? RosterName(string id) =>
        MiniRing.Characters.CharacterRoster.All.FirstOrDefault(c => c.Id == id)?.HudName
        ?? (id.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && File.Exists(id) ? MiniRing.Characters.CharacterRoster.LoadFile(id).HudName : null); // 名簿の外のファイル（新人リーグの新人）

    private static string Required(string? value, string label, string field) =>
        string.IsNullOrWhiteSpace(value) ? throw new InvalidDataException($"{label} の {field} がありません。") : value;

    private sealed class ProgramDto
    {
        public string? Title { get; init; }

        public List<BoutDto>? Bouts { get; init; }
    }

    private sealed class BoutDto
    {
        public string? Bout { get; init; }

        public string? Red { get; init; }

        public string? Blue { get; init; }

        public string? Winner { get; init; }

        public string? Method { get; init; }

        public bool Current { get; init; }

        public string? Stake { get; init; }

        public string? Next { get; init; }

        public string? Cause { get; init; }

        public bool Belt { get; init; }
    }
}

/// <summary>プログラムの 1 試合。</summary>
/// <param name="Bout">何試合目か（「準決勝第1試合」「決勝」）。</param>
/// <param name="Red">赤コーナーの名前（決まっていなければ「準決勝第1試合の勝者」）。</param>
/// <param name="Blue">青コーナーの名前。</param>
/// <param name="Winner">済んだ試合の勝者（"red" / "blue"）。まだなら null。</param>
/// <param name="Method">決まり手（「K.O.」「判定」）。</param>
/// <param name="Current">これから撮る試合。</param>
/// <param name="Stake">この試合に懸かるもの（「勝者は決勝へ」「優勝決定戦」）。</param>
/// <param name="Next">勝った人が得るもの（「決勝進出」「優勝」）。決着の後に実況が「〇〇、決勝進出！」と言う。</param>
/// <param name="Cause">決着の言葉（「脳を壊されて」。死で決着する団体の番組が済んだ試合の行に出す）。無ければ空。</param>
/// <param name="Belt">王座（チャンピオンベルト）が懸かった試合か（札の <c>belt</c>）。実況・解説の資料（<see cref="TvDigest"/>・プロスペクティブ制の選手の資料）に書く。</param>
internal sealed record TvProgramBout(string Bout, string Red, string Blue, string? Winner, string Method, bool Current, string Stake, string Next = "",
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Cause = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Belt = false)
{
    /// <summary>勝者の名前（まだなら null）。</summary>
    [JsonIgnore]
    public string? WinnerName => Winner switch { "red" => Red, "blue" => Blue, _ => null };
}
