using System.Globalization;
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

    /// <summary>ボクサーのページの行（0..大会の数−1 = 大会、次 = 目標体重、次 = 消す、最後 = 戻る）。</summary>
    public int BoxerIndex { get; private set; }

    /// <summary>ボクサーのページの見えている先頭の行（大会は階級ごとにあって画面に収まらないので送る）。</summary>
    public int BoxerFirst { get; private set; }

    /// <summary>ボクサーのページで一度に見せる行の数。</summary>
    public const int BoxerVisible = CampaignVisible + 2;

    public static int BoxerRows => CampaignEvents.All.Count + 3;
    public static int WeightRow => CampaignEvents.All.Count;
    public static int DeleteRow => CampaignEvents.All.Count + 1;

    /// <summary>目標体重 [kg]（ボクサーのページ。決定で減量・増量）。</summary>
    public int TargetKg { get; private set; }

    /// <summary>消すの確かめ（もう一度決定で消す）。</summary>
    public bool ConfirmingDelete { get; private set; }

    private readonly Dictionary<string, int> _weaponChoice = new(StringComparer.Ordinal);

    /// <summary>大会ごとに選んでいる武器（<see cref="CampaignEntry.Weapons"/> の添字。大会ごとに選び直せる）。</summary>
    public WeaponKind? WeaponFor(CampaignEntry entry) => entry.Weapons[Math.Clamp(_weaponChoice.GetValueOrDefault(entry.Event.Id), 0, entry.Weapons.Count - 1)];

    /// <summary>大会の並びと、出られるか（理由）・組み合わせ。</summary>
    public IReadOnlyList<(CampaignEvent Event, CampaignEntry? Entry, string? Reason)> Events { get; private set; } = [];

    // ── 作る
    public const int CreateRows = 5;
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

        RefreshEvents();
    }

    /// <summary>作ったボクサーのページを開く。</summary>
    public void OpenBoxer(string id)
    {
        _boxerId = id;
        CampaignIndex = Math.Max(0, Boxers.ToList().FindIndex(b => b.Id == id));
        Page = MenuPage.Boxer;
        ConfirmingDelete = false;
        RefreshEvents();
        // 初めは出られる最初の大会に合わせる（無ければ先頭）。
        BoxerIndex = Math.Max(0, Events.ToList().FindIndex(e => e.Entry is not null));
        BoxerFirst = 0;
        ScrollBoxer();
    }

    private void ScrollBoxer() => BoxerFirst = Math.Clamp(BoxerFirst, Math.Max(0, BoxerIndex - BoxerVisible + 1), BoxerIndex);

    private void EnterCampaign()
    {
        Page = MenuPage.Campaign;
        ConfirmingDelete = false;
    }

    private void RefreshEvents()
    {
        if (Boxer is not { } boxer)
        {
            Events = [];
            return;
        }

        TargetKg = boxer.WeightKg;
        Events = [.. CampaignEvents.All.Select(e =>
        {
            var (entry, reason) = CampaignLadder.Check(e, boxer, Roster);
            return (e, entry, reason);
        })];
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

    private MenuOutcome ApplyBoxer(MenuInput input)
    {
        if (input.Cancel || Boxer is not { } boxer)
        {
            Page = MenuPage.Campaign;
            ConfirmingDelete = false;
            return new(Cue: MenuCue.Confirm);
        }

        var (oldRow, oldKg) = (BoxerIndex, TargetKg);
        var oldWeapons = _weaponChoice.ToDictionary(p => p.Key, p => p.Value);
        var confirm = input.Confirm;
        if (input.Pointer is { } point && MenuLayout.HitCenters(point, Math.Min(BoxerRows - BoxerFirst, BoxerVisible), MenuLayout.CampaignRowCenter, MenuLayout.CampaignRowSize) is { } hit)
        {
            BoxerIndex = BoxerFirst + hit;
            confirm |= input.Click;
        }

        var step = input.Scroll != 0 ? Math.Sign(input.Scroll) : input.Y;
        BoxerIndex = Wrap(BoxerIndex + step, BoxerRows);
        ScrollBoxer();
        if (BoxerIndex != oldRow) { ConfirmingDelete = false; }
        if (input.X != 0)
        {
            if (BoxerIndex < Events.Count && Events[BoxerIndex].Entry is { } focused)
            {
                _weaponChoice[focused.Event.Id] = Wrap(_weaponChoice.GetValueOrDefault(focused.Event.Id) + input.X, focused.Weapons.Count);
            }
            else if (BoxerIndex == WeightRow)
            {
                var (lo, hi) = boxer.WeightRange;
                TargetKg = Math.Clamp(TargetKg + input.X, lo, hi);
            }
        }

        if (!confirm)
        {
            var moved = BoxerIndex != oldRow || TargetKg != oldKg || !oldWeapons.SequenceEqual(_weaponChoice);
            return new(Cue: moved ? MenuCue.Move : MenuCue.None);
        }

        if (BoxerIndex < Events.Count)
        {
            var (ev, entry, reason) = Events[BoxerIndex];
            if (entry is null)
            {
                return new(Cue: MenuCue.Move, Notice: reason ?? $"{ev.Name} には出られません。");
            }

            var weapon = WeaponFor(entry);
            var red = boxer.Character with { Weapon = weapon };
            var match = new MatchSelection(MatchMode.Single, red, entry.Opponent, Settings.Referee, Competition.Event(entry.Event.Organization))
            {
                Campaign = new CampaignTicket(boxer.Id, entry, weapon),
            };
            return new(MenuAction.StartMatch, match, Cue: MenuCue.Confirm);
        }

        if (BoxerIndex == WeightRow)
        {
            return TargetKg == boxer.WeightKg
                ? new(Cue: MenuCue.Move, Notice: "← → で目標の体重を決めてから決定します。")
                : new(MenuAction.Campaign, Cue: MenuCue.Confirm, Request: new CampaignRequest.ChangeWeight(boxer.Id, TargetKg));
        }

        if (BoxerIndex == DeleteRow)
        {
            if (!ConfirmingDelete)
            {
                ConfirmingDelete = true;
                return new(Cue: MenuCue.Move, Notice: $"{boxer.Character.HudName} を消します。もう一度決定で消します（元に戻せません）。");
            }

            ConfirmingDelete = false;
            _boxerId = null;
            Page = MenuPage.Campaign;
            return new(MenuAction.Campaign, Cue: MenuCue.Confirm, Request: new CampaignRequest.Delete(boxer.Id));
        }

        Page = MenuPage.Campaign;
        return new(Cue: MenuCue.Confirm);
    }

    /// <summary>目標体重にしたときの体（画面の見込み: 計量・階級・力の変わり方。決定するまでファイルは変わらない）。</summary>
    public BodyBuild? TargetBuild => Boxer is { } boxer && TargetKg != boxer.WeightKg ? BodySizing.WithWeight(boxer.Character.Build, TargetKg) : null;

    /// <summary>体重の行の言い方（「72 kg → 66 kg　力 −8%」）。</summary>
    public string WeightLine()
    {
        if (Boxer is not { } boxer) { return ""; }
        if (TargetBuild is not { } target) { return string.Create(CultureInfo.InvariantCulture, $"目標体重  ◀ {TargetKg} kg ▶"); }
        var power = (target.SectionRatio / boxer.Character.Build.SectionRatio) - 1f;
        return string.Create(CultureInfo.InvariantCulture, $"目標体重  ◀ {TargetKg} kg ▶  力 {power:+0%;-0%;0%}");
    }
}
