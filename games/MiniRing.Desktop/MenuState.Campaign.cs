using MiniRing.Arena.Campaign;
using MiniRing.Characters;
using MiniRing.Weapons;

namespace MiniRing.Desktop;

/// <summary>キャンペーンのボクサーの置き場への頼み（メニューの殻 <see cref="MatchMenu"/> が実行して、一覧を読み直して <see cref="MenuState.SetBoxers"/> で戻す）。</summary>
internal abstract record CampaignRequest
{
    /// <summary>新しいボクサーを作る。</summary>
    public sealed record Create(CharacterDraft Draft) : CampaignRequest;

    /// <summary>ボクサーを消す。</summary>
    public sealed record Delete(string BoxerId) : CampaignRequest;

    /// <summary>減量・増量（試合と試合の間）。</summary>
    public sealed record ChangeWeight(string BoxerId, int TargetKg) : CampaignRequest;
}

/// <summary>キャンペーンの試合の札: どのボクサーが、どの大会に、どの武器で出たか（試合の後に戦績へ書く）。</summary>
internal sealed record CampaignTicket(string BoxerId, CampaignEntry Entry, WeaponKind? Weapon);

/// <summary>
/// キャンペーン（2026-11-26、docs/plans/CAMPAIGN_PLAN.md）のページ: ボクサーの一覧（何人でも）→ 作る（体型・身長・教科書）/ ボクサー（大会の並び・武器・目標体重・消す）。
/// 出られる大会・理由・相手は <see cref="CampaignLadder.Check"/>（体と団体のファイルと戦績から）。状態は純粋で、ファイルは殻が扱う。
/// </summary>
internal sealed partial class MenuState
{

    /// <summary>ボクサーの一覧（置き場から読んだ物。殻が <see cref="SetBoxers"/> で入れる）。</summary>
    public IReadOnlyList<CampaignBoxer> Boxers { get; private set; } = [];

    /// <summary>一覧の行（0..ボクサーの数−1 = ボクサー、次 = 新しく作る、最後 = 戻る）。</summary>
    public int CampaignIndex { get; private set; }

    /// <summary>一覧の見えている先頭の行。</summary>
    public int CampaignFirst { get; private set; }

    /// <summary>一覧で一度に見せる行の数。</summary>
    public const int CampaignVisible = 10;

    public int CampaignRows => Boxers.Count + 2;

    /// <summary>選んでいるボクサー（ボクサーのページ）。</summary>
    public CampaignBoxer? Boxer => _boxerId is { } id ? Boxers.FirstOrDefault(b => b.Id == id) : null;

    private string? _boxerId;

    // ── 作る
    public const int CreateRows = 5;

    /// <summary>作るの、← → で変える行の数（体型・身長・教科書。◀ ▶ のボタンがある）。</summary>
    public const int CreateValueRows = 3;
    public int CreateIndex { get; private set; }
    public int BodyTypeIndex { get; private set; }
    public int HeightCm { get; private set; } = CharacterProfile.StandardHeightCm;
    public int TextbookIndex { get; private set; }

    /// <summary>作るときに選べる教科書（名簿にある流派、出てくる順）。</summary>
    public IReadOnlyList<string> Textbooks => _textbooks ??= [.. Roster.Select(c => c.StyleName).Prepend(Bots.BotCatalog.Champion.Name).Distinct(StringComparer.Ordinal)];

    private IReadOnlyList<string>? _textbooks;

    /// <summary>作る下書き（体型・身長・教科書。名前と id は置き場が振る）。</summary>
    public CharacterDraft Draft()
    {
        var type = CharacterMaker.BodyTypes[BodyTypeIndex];
        return CharacterMaker.NewDraft(type.Id, heightCm: HeightCm) with { Textbook = Textbooks[TextbookIndex] };
    }

    /// <summary>
    /// 作る下書きの体の見込み（全身の絵のため。ファイルには書かない）。体は置き場が作るのと同じ道（<see cref="CharacterMaker.ToFile"/>）で、名前・id・国は仮。
    /// </summary>
    public FighterCharacter DraftPreview() =>
        CharacterMaker.ToFile(Draft() with { Id = "preview", Name = "preview", Country = "japan", Order = 1 }).Character;

    /// <summary>作る下書きの体の見込みを区別する鍵（体型と身長で体が決まる。教科書は体を変えない）。</summary>
    public string DraftPreviewKey => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"create:{BodyTypeIndex}:{HeightCm}");

    /// <summary>作れる身長の範囲 [cm]（体格の比 0.6〜1.5 の内側）。</summary>
    public static (int Min, int Max) HeightRange(float legs)
    {
        int Cm(float h) => CharacterProfile.HeightOf(new BodyBuild(h, 1f, legs, 1f));
        return (Cm(BodySizing.MinRatio) + 1, Cm(BodySizing.MaxRatio) - 1);
    }

    /// <summary>置き場から読み直した一覧を入れる（作った・消した・試合の後）。選んでいたボクサーが消えていれば一覧へ戻る。</summary>
    public void SetBoxers(IReadOnlyList<CampaignBoxer> boxers)
    {
        Boxers = boxers ?? throw new ArgumentNullException(nameof(boxers));
        CampaignIndex = Math.Clamp(CampaignIndex, 0, CampaignRows - 1);
        if (Page == MenuPage.Boxer && Boxer is null)
        {
            Page = MenuPage.Campaign;
        }

        if (Page is MenuPage.Orgs or MenuPage.Path or MenuPage.Weight && Boxer is null)
        {
            Page = MenuPage.Campaign;
        }

        RefreshStops();
    }

    private void EnterCampaign()
    {
        Page = MenuPage.Campaign;
        ConfirmingDelete = false;
    }

    private static int ListMove(int index, int rows, MenuInput input, Func<int, System.Numerics.Vector2> center, System.Numerics.Vector2 size, int first, ref bool confirm)
    {
        if (input.Pointer is { } point && MenuLayout.HitCenters(point, Math.Min(rows - first, CampaignVisible + 2), center, size) is { } hit)
        {
            index = first + hit;
            confirm |= input.Click;
        }

        var y = input.Scroll != 0 ? Math.Sign(input.Scroll) : input.Y;
        return Wrap(index + y, rows);
    }

    private MenuOutcome ApplyCampaign(MenuInput input)
    {
        if (input.Cancel)
        {
            Page = MenuPage.Top;
            return new(Cue: MenuCue.Confirm);
        }

        var old = CampaignIndex;
        var confirm = input.Confirm;
        CampaignIndex = ListMove(CampaignIndex, CampaignRows, input, MenuLayout.CampaignRowCenter, MenuLayout.CampaignRowSize, CampaignFirst, ref confirm);
        CampaignFirst = Math.Clamp(CampaignFirst, Math.Max(0, CampaignIndex - CampaignVisible + 1), CampaignIndex);
        if (!confirm)
        {
            return new(Cue: CampaignIndex != old ? MenuCue.Move : MenuCue.None);
        }

        if (CampaignIndex < Boxers.Count)
        {
            OpenBoxer(Boxers[CampaignIndex].Id);
        }
        else if (CampaignIndex == Boxers.Count)
        {
            Page = MenuPage.Create;
            CreateIndex = 0;
        }
        else
        {
            Page = MenuPage.Top;
        }

        return new(Cue: MenuCue.Confirm);
    }

    private MenuOutcome ApplyCreate(MenuInput input)
    {
        if (input.Cancel)
        {
            Page = MenuPage.Campaign;
            return new(Cue: MenuCue.Confirm);
        }

        for (var row = 0; row < CreateValueRows; row++)
        {
            var r = row;
            input = Arrows(input, side => MouseLayout.CreateArrow(r, side), $"create{r}");
        }

        var (oldRow, oldType, oldHeight, oldBook) = (CreateIndex, BodyTypeIndex, HeightCm, TextbookIndex);
        var confirm = input.Confirm;
        if (input.Pointer is { } point && MenuLayout.HitCenters(point, CreateRows, MenuLayout.CampaignRowCenter, MenuLayout.CampaignRowSize) is { } hit)
        {
            CreateIndex = hit;
            confirm |= input.Click;
        }

        CreateIndex = Wrap(CreateIndex + input.Y, CreateRows);
        switch (CreateIndex)
        {
            case 0: BodyTypeIndex = Wrap(BodyTypeIndex + input.X, CharacterMaker.BodyTypes.Count); break;
            case 1:
                var (lo, hi) = HeightRange(CharacterMaker.BodyTypes[BodyTypeIndex].Legs);
                HeightCm = Math.Clamp(HeightCm + input.X, lo, hi);
                break;
            case 2: TextbookIndex = Wrap(TextbookIndex + input.X, Textbooks.Count); break;
        }

        var (min, max) = HeightRange(CharacterMaker.BodyTypes[BodyTypeIndex].Legs);
        HeightCm = Math.Clamp(HeightCm, min, max);
        var changed = (CreateIndex, BodyTypeIndex, HeightCm, TextbookIndex) != (oldRow, oldType, oldHeight, oldBook);
        if (!confirm)
        {
            return new(Cue: changed ? MenuCue.Move : MenuCue.None);
        }

        if (CreateIndex == 3)
        {
            return new(MenuAction.Campaign, Cue: MenuCue.Confirm, Request: new CampaignRequest.Create(Draft()));
        }

        if (CreateIndex == 4)
        {
            Page = MenuPage.Campaign;
        }

        return new(Cue: MenuCue.Confirm);
    }
}
