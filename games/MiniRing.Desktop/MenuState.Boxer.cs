using System.Globalization;
using System.Numerics;
using MiniRing.Arena;
using MiniRing.Arena.Campaign;
using MiniRing.Characters;
using MiniRing.Weapons;

namespace MiniRing.Desktop;

/// <summary>
/// キャンペーンのボクサーの画面の配置（設計の画面 640 × 464）。描画と指す位置の判定が同じ値を使う。
/// 1 画面の選べる項目は 6 つ以下（2026-11-26 UI の作り直し: 21 の大会を 1 つの一覧に並べていたのが分かりにくかった）。
/// </summary>
internal static class CampaignLayout
{
    /// <summary>本拠地: 「次の試合」の大きな札。</summary>
    public static readonly Vector2 NextCenter = new(320f, 268f);
    public static readonly Vector2 NextSize = new(584f, 64f);

    /// <summary>本拠地: 下の小さな札（団体と大会・体重・消す・戻る）。</summary>
    public static readonly Vector2 HomeButtonSize = new(138f, 32f);
    public static Vector2 HomeButtonCenter(int index) => new(98f + (index * 148f), 330f);

    /// <summary>本拠地の札の中心（0 = 次の試合、1.. = 下の札）。</summary>
    public static Vector2 HomeCenter(int index) => index == 0 ? NextCenter : HomeButtonCenter(index - 1);
    public static Vector2 HomeSize(int index) => index == 0 ? NextSize : HomeButtonSize;

    /// <summary>団体を選ぶ: 団体の大きな札（3 つ）と戻る。</summary>
    public static readonly Vector2 OrgSize = new(584f, 66f);
    public static Vector2 OrgCenter(int index) => new(320f, 134f + (index * 76f));
    public static readonly Vector2 BackSize = new(138f, 30f);
    public static readonly Vector2 OrgBackCenter = new(320f, 356f);

    /// <summary>団体の道: 3 つの段（左から右）と戻る。</summary>
    public static readonly Vector2 NodeSize = new(172f, 112f);
    public static Vector2 NodeCenter(int index) => new(116f + (index * 204f), 176f);
    public static readonly Vector2 PathBackCenter = new(542f, 356f);

    /// <summary>
    /// 減量・増量（2026-11-26 マウス向け）: 左上に今の体と目標の体の全身の絵、その下に目標体重のスライダー（行 0）、−5 / −1 / +1 / +5 kg、
    /// 決める（行 1）・戻る（行 2）。右の板に数字と、押すとその階級へ合わせる階級の表。
    /// </summary>
    public static readonly Vector2 PreviewSize = new(132f, 150f);
    public static Vector2 PreviewCenter(int index) => new(96f + (index * 140f), 170f);

    /// <summary>スライダー（行 0）の当たりの範囲と、溝の左右の端・高さ。</summary>
    public static readonly Vector2 SliderCenter = new(166f, 270f);
    public static readonly Vector2 SliderSize = new(276f, 40f);
    public const float SliderLeft = 50f;
    public const float SliderRight = 282f;
    public const float SliderY = 280f;

    /// <summary>スライダーの位置 <paramref name="x"/> の体重（作れる範囲 [<paramref name="lo"/>, <paramref name="hi"/>] の整数 kg）。</summary>
    public static int SliderKg(float x, int lo, int hi) =>
        lo + (int)MathF.Round(Math.Clamp((x - SliderLeft) / (SliderRight - SliderLeft), 0f, 1f) * (hi - lo));

    /// <summary>体重 <paramref name="kg"/> のスライダーの位置。</summary>
    public static float SliderX(int kg, int lo, int hi) => hi > lo ? SliderLeft + ((SliderRight - SliderLeft) * (kg - lo) / (hi - lo)) : SliderLeft;

    /// <summary>−5 / −1 / +1 / +5 kg のボタン。</summary>
    public static readonly IReadOnlyList<int> WeightSteps = [-5, -1, 1, 5];
    public static readonly Vector2 StepSize = new(58f, 26f);
    public static Vector2 StepCenter(int index) => new(67f + (index * 66f), 314f);

    /// <summary>行 0 = スライダー、1 = 決める、2 = 戻る。</summary>
    public static Vector2 WeightRowCenter(int index) => index == 0 ? SliderCenter : new(100f + ((index - 1) * 134f), 354f);
    public static Vector2 WeightRowSize(int index) => index == 0 ? SliderSize : new(128f, 32f);

    /// <summary>右の板の階級の表の行（押すとその階級へ）。</summary>
    public static readonly Vector2 DivisionRowSize = new(284f, 20f);
    public static Vector2 DivisionRowCenter(int index) => new(462f, 254f + (index * 21f));
}

/// <summary>
/// キャンペーンのボクサーの画面（2026-11-26 UI の作り直し）。段に分ける:
/// <list type="number">
/// <item>本拠地（<see cref="MenuPage.Boxer"/>）: 名前・体重と階級・戦績・団体ごとの席を大きく、「次の試合」を 1 つ大きく推す（← → で武器）。下に 団体と大会・体重・消す・戻る。</item>
/// <item>団体を選ぶ（<see cref="MenuPage.Orgs"/>）: IVF・WVC・VFC と戻る。</item>
/// <item>団体の道（<see cref="MenuPage.Path"/>）: 自分の階級の 新人戦 → ランキング戦 → タイトル を左から右へ。開いた所・閉じた所（あと何で開くか）が一目で分かる。決定で「次の試合」にして本拠地へ戻る。</item>
/// <item>減量・増量（<see cref="MenuPage.Weight"/>）: 目標体重と、その体重で入る階級。</item>
/// </list>
/// 試合の後は、前後の差から作った札（<see cref="CampaignCard"/>）を本拠地の上に 1 枚ずつ見せ、Enter で次へ・最後で消える。
/// </summary>
internal sealed partial class MenuState
{
    // 本拠地の札の番号。
    public const int HomeNext = 0;
    public const int HomeOrgs = 1;
    public const int HomeWeight = 2;
    public const int HomeDelete = 3;
    public const int HomeBack = 4;
    public const int HomeRows = 5;

    /// <summary>本拠地のカーソル（0 = 次の試合、1 = 団体と大会、2 = 体重、3 = 消す、4 = 戻る）。</summary>
    public int HomeIndex { get; private set; }

    /// <summary>団体を選ぶのカーソル（0..団体の数−1、最後 = 戻る）。</summary>
    public int OrgIndex { get; private set; }

    /// <summary>団体の道のカーソル（0..2 = 段、3 = 戻る）。</summary>
    public int PathIndex { get; private set; }
    public const int PathBack = 3;

    /// <summary>減量・増量のカーソル（0 = 目標体重、1 = 決める、2 = 戻る）。</summary>
    public int WeightIndex { get; private set; }
    public const int WeightRows = 3;

    /// <summary>目標体重 [kg]（決めるで減量・増量）。</summary>
    public int TargetKg { get; private set; }

    /// <summary>消すの確かめ（もう一度決定で消す）。</summary>
    public bool ConfirmingDelete { get; private set; }

    private readonly Dictionary<string, int> _weaponChoice = new(StringComparer.Ordinal);

    /// <summary>大会ごとに選んでいる武器（<see cref="CampaignEntry.Weapons"/> の添字。大会ごとに選び直せる）。</summary>
    public WeaponKind? WeaponFor(CampaignEntry entry) => entry.Weapons[Math.Clamp(_weaponChoice.GetValueOrDefault(entry.Event.Id), 0, entry.Weapons.Count - 1)];

    /// <summary>全大会の見え方（出られるか・あと何で開くか）。</summary>
    public IReadOnlyList<CampaignStop> Stops { get; private set; } = [];

    /// <summary>道で選んだ「次の試合」（null = 自動: <see cref="CampaignPlan.AutoNext"/>）。出られなくなれば自動へ戻る。</summary>
    private string? _chosenNext;

    /// <summary>次の試合の武器を選び直せるか（武器が 2 つ以上: 札に ◀ ▶ が出る）。</summary>
    public bool WeaponChoosable => Next?.Entry is { Weapons.Count: > 1 };

    /// <summary>本拠地で推す「次の試合」。</summary>
    public CampaignStop? Next =>Stops.FirstOrDefault(s => s.Open && s.Event.Id == _chosenNext) ?? CampaignPlan.AutoNext(Stops);

    /// <summary>道を見ている団体。</summary>
    public Organization PathOrg => CampaignPlan.Orgs[Math.Clamp(OrgIndex, 0, CampaignPlan.Orgs.Count - 1)];

    /// <summary>道の 3 段（自分の階級の 新人戦 → ランキング戦 → タイトル）。</summary>
    public IReadOnlyList<CampaignStop> PathStops => Boxer is { } boxer ? CampaignPlan.Path(Stops, PathOrg, boxer) : [];

    /// <summary>試合の後に見せる札（先頭が今の札。Enter で次へ）。</summary>
    public IReadOnlyList<CampaignCard> Cards { get; private set; } = [];

    /// <summary>試合の後の札を並べる（本拠地の上に出る）。「次の試合」は自動へ戻す（新しく開いた大会へ移る）。</summary>
    public void Celebrate(IReadOnlyList<CampaignCard> cards)
    {
        ArgumentNullException.ThrowIfNull(cards);
        Cards = Boxer is null ? [] : cards;
        _chosenNext = null;
        HomeIndex = HomeNext;
        if (Boxer is not null) { Page = MenuPage.Boxer; }
    }

    /// <summary>ボクサーの本拠地を開く。</summary>
    public void OpenBoxer(string id)
    {
        _boxerId = id;
        CampaignIndex = Math.Max(0, Boxers.ToList().FindIndex(b => b.Id == id));
        Page = MenuPage.Boxer;
        ConfirmingDelete = false;
        _chosenNext = null;
        _weaponChoice.Clear(); // 武器の選びはボクサーごと
        Cards = [];
        HomeIndex = HomeNext;
        RefreshStops();
    }

    private void RefreshStops()
    {
        if (Boxer is not { } boxer)
        {
            Stops = [];
            return;
        }

        TargetKg = boxer.WeightKg;
        Stops = CampaignPlan.Stops(boxer, Roster);
    }

    private static int? Hit(MenuInput input, int count, Func<int, Vector2> center, Func<int, Vector2> size)
    {
        if (input.Pointer is not { } point) { return null; }
        for (var i = 0; i < count; i++)
        {
            var delta = Vector2.Abs(point - center(i));
            var s = size(i);
            if (delta.X <= s.X / 2 && delta.Y <= s.Y / 2) { return i; }
        }

        return null;
    }

    // ── 試合の後の札
    private MenuOutcome ApplyCard(MenuInput input)
    {
        if (!(input.Confirm || input.Cancel || input.Click)) { return new(); }
        Cards = [.. Cards.Skip(1)];
        return new(Cue: MenuCue.Confirm);
    }

    // ── 本拠地
    private MenuOutcome ApplyBoxer(MenuInput input)
    {
        if (input.Cancel || Boxer is not { } boxer)
        {
            Page = MenuPage.Campaign;
            ConfirmingDelete = false;
            return new(Cue: MenuCue.Confirm);
        }

        if (WeaponChoosable) { input = Arrows(input, MouseLayout.WeaponArrow, "weapon"); }
        var oldIndex = HomeIndex;
        var oldWeapons = _weaponChoice.ToDictionary(p => p.Key, p => p.Value);
        var confirm = input.Confirm;
        if (Hit(input, HomeRows, CampaignLayout.HomeCenter, CampaignLayout.HomeSize) is { } hit)
        {
            HomeIndex = hit;
            confirm |= input.Click;
        }

        // 上下: 次の試合 ⇔ 下の札の列。左右: 次の試合では武器、下の列では札を送る。
        var y = input.Scroll != 0 ? Math.Sign(input.Scroll) : input.Y;
        if (y != 0) { HomeIndex = HomeIndex == HomeNext ? HomeOrgs : HomeNext; }
        if (input.X != 0)
        {
            if (HomeIndex == HomeNext && Next?.Entry is { } entry)
            {
                _weaponChoice[entry.Event.Id] = Wrap(_weaponChoice.GetValueOrDefault(entry.Event.Id) + input.X, entry.Weapons.Count);
            }
            else if (HomeIndex != HomeNext)
            {
                HomeIndex = 1 + Wrap(HomeIndex - 1 + input.X, HomeRows - 1);
            }
        }

        if (HomeIndex != oldIndex) { ConfirmingDelete = false; }
        if (!confirm)
        {
            var moved = HomeIndex != oldIndex || !oldWeapons.SequenceEqual(_weaponChoice);
            return new(Cue: moved ? MenuCue.Move : MenuCue.None);
        }

        switch (HomeIndex)
        {
            case HomeNext:
                if (Next?.Entry is not { } next)
                {
                    return new(Cue: MenuCue.Move, Notice: "出られる大会がありません。体重を変えるか、団体と大会で条件を見てください。");
                }

                var weapon = WeaponFor(next);
                var red = boxer.Character with { Weapon = weapon };
                var match = new MatchSelection(MatchMode.Single, red, next.Opponent, Settings.Referee, Competition.Event(next.Event.Organization))
                {
                    Campaign = new CampaignTicket(boxer.Id, next, weapon),
                };
                return new(MenuAction.StartMatch, match, Cue: MenuCue.Confirm);
            case HomeOrgs:
                Page = MenuPage.Orgs;
                OrgIndex = Next is { } n ? Math.Max(0, CampaignPlan.Orgs.ToList().FindIndex(o => o.Id == n.Event.Org)) : 0;
                return new(Cue: MenuCue.Confirm);
            case HomeWeight:
                Page = MenuPage.Weight;
                WeightIndex = 0;
                TargetKg = boxer.WeightKg;
                return new(Cue: MenuCue.Confirm);
            case HomeDelete:
                if (!ConfirmingDelete)
                {
                    ConfirmingDelete = true;
                    return new(Cue: MenuCue.Move, Notice: $"{boxer.Character.HudName} を消します。もう一度決定で消します（元に戻せません）。");
                }

                ConfirmingDelete = false;
                _boxerId = null;
                Page = MenuPage.Campaign;
                return new(MenuAction.Campaign, Cue: MenuCue.Confirm, Request: new CampaignRequest.Delete(boxer.Id));
            default:
                Page = MenuPage.Campaign;
                return new(Cue: MenuCue.Confirm);
        }
    }

    // ── 団体を選ぶ
    public static int OrgRows => CampaignPlan.Orgs.Count + 1;

    private MenuOutcome ApplyOrgs(MenuInput input)
    {
        if (input.Cancel || Boxer is null)
        {
            Page = Boxer is null ? MenuPage.Campaign : MenuPage.Boxer;
            return new(Cue: MenuCue.Confirm);
        }

        var old = OrgIndex;
        var confirm = input.Confirm;
        var orgs = CampaignPlan.Orgs.Count;
        if (Hit(input, OrgRows, i => i < orgs ? CampaignLayout.OrgCenter(i) : CampaignLayout.OrgBackCenter, i => i < orgs ? CampaignLayout.OrgSize : CampaignLayout.BackSize) is { } hit)
        {
            OrgIndex = hit;
            confirm |= input.Click;
        }

        OrgIndex = Wrap(OrgIndex + (input.Scroll != 0 ? Math.Sign(input.Scroll) : input.Y), OrgRows);
        if (!confirm) { return new(Cue: OrgIndex != old ? MenuCue.Move : MenuCue.None); }
        if (OrgIndex == orgs)
        {
            Page = MenuPage.Boxer;
            return new(Cue: MenuCue.Confirm);
        }

        Page = MenuPage.Path;
        // 道のカーソルは、出られる一番先の段（無ければ新人戦）。
        var path = PathStops;
        PathIndex = Math.Max(0, path.Select((s, i) => (s, i)).Where(p => p.s.Open).Select(p => p.i).DefaultIfEmpty(0).Max());
        return new(Cue: MenuCue.Confirm);
    }

    // ── 団体の道
    private MenuOutcome ApplyPath(MenuInput input)
    {
        if (input.Cancel || Boxer is null)
        {
            Page = Boxer is null ? MenuPage.Campaign : MenuPage.Orgs;
            return new(Cue: MenuCue.Confirm);
        }

        var path = PathStops;
        var old = PathIndex;
        var confirm = input.Confirm;
        if (Hit(input, path.Count + 1, i => i < path.Count ? CampaignLayout.NodeCenter(i) : CampaignLayout.PathBackCenter,
            i => i < path.Count ? CampaignLayout.NodeSize : CampaignLayout.BackSize) is { } hit)
        {
            PathIndex = hit < path.Count ? hit : PathBack;
            confirm |= input.Click;
        }

        if (input.X != 0 && PathIndex != PathBack && path.Count > 0) { PathIndex = Wrap(PathIndex + input.X, path.Count); }
        if (input.Y > 0 || input.Scroll > 0) { PathIndex = PathBack; }
        if ((input.Y < 0 || input.Scroll < 0) && PathIndex == PathBack) { PathIndex = Math.Max(0, path.Count - 1); }
        if (!confirm) { return new(Cue: PathIndex != old ? MenuCue.Move : MenuCue.None); }
        if (PathIndex == PathBack || PathIndex >= path.Count)
        {
            Page = MenuPage.Orgs;
            return new(Cue: MenuCue.Confirm);
        }

        var stop = path[PathIndex];
        if (!stop.Open)
        {
            return new(Cue: MenuCue.Move, Notice: stop.Gate.Reason ?? $"{stop.Event.Name} には出られません。");
        }

        _chosenNext = stop.Event.Id;
        Page = MenuPage.Boxer;
        HomeIndex = HomeNext;
        return new(Cue: MenuCue.Confirm, Notice: $"次の試合を {stop.Event.Name} にしました。");
    }

    // ── 減量・増量
    private MenuOutcome ApplyWeight(MenuInput input)
    {
        if (input.Cancel || Boxer is not { } boxer)
        {
            Page = Boxer is null ? MenuPage.Campaign : MenuPage.Boxer;
            return new(Cue: MenuCue.Confirm);
        }

        var (oldIndex, oldKg) = (WeightIndex, TargetKg);
        var (lo, hi) = boxer.WeightRange;
        var confirm = input.Confirm;
        string? notice = null;
        if (input.Pointer is { } point)
        {
            // スライダー: 押した所から、放すまでドラッグで目標体重（作れる範囲の中）。
            if (input.Click && MouseLayout.Inside(point, CampaignLayout.SliderCenter, CampaignLayout.SliderSize)) { _dragging = true; }
            if (_dragging)
            {
                WeightIndex = 0;
                TargetKg = CampaignLayout.SliderKg(point.X, lo, hi);
            }

            // −5 / −1 / +1 / +5 kg（押しっぱなしで連続）。
            if (!_dragging && Hit(input, CampaignLayout.WeightSteps.Count, CampaignLayout.StepCenter, _ => CampaignLayout.StepSize) is { } step)
            {
                WeightIndex = 0;
                TargetKg = Math.Clamp(TargetKg + (CampaignLayout.WeightSteps[step] * Fire(input, $"kg{step}")), lo, hi);
            }

            // 階級の表の行: その階級へ入る一番近い体重（重い側からなら上限、軽い側からなら下限）。
            if (input.Click && Hit(input, WeightOrg.Divisions.Count, CampaignLayout.DivisionRowCenter, _ => CampaignLayout.DivisionRowSize) is { } row)
            {
                WeightIndex = 0;
                (TargetKg, notice) = IntoDivision(row, lo, hi);
            }

            if (Hit(input, WeightRows, CampaignLayout.WeightRowCenter, CampaignLayout.WeightRowSize) is { } hit)
            {
                WeightIndex = hit;
                confirm |= input.Click && hit != 0; // スライダーの行は押しても決めない（決めるのボタンで決める）
            }
        }

        WeightIndex = Wrap(WeightIndex + (input.Scroll != 0 ? Math.Sign(input.Scroll) : input.Y), WeightRows);
        if (WeightIndex == 0 && input.X != 0)
        {
            TargetKg = Math.Clamp(TargetKg + input.X, lo, hi);
        }

        if (!confirm) { return new(Cue: notice is not null || (WeightIndex, TargetKg) != (oldIndex, oldKg) ? MenuCue.Move : MenuCue.None, Notice: notice); }
        if (WeightIndex == 2)
        {
            Page = MenuPage.Boxer;
            return new(Cue: MenuCue.Confirm);
        }

        if (TargetKg == boxer.WeightKg)
        {
            return new(Cue: MenuCue.Move, Notice: "スライダー・ボタン・← → で目標の体重を決めてから決めます。");
        }

        Page = MenuPage.Boxer;
        _chosenNext = null;
        return new(MenuAction.Campaign, Cue: MenuCue.Confirm, Request: new CampaignRequest.ChangeWeight(boxer.Id, TargetKg));
    }

    /// <summary>スライダーをドラッグしている間（押した所から放すまで）。</summary>
    private bool _dragging;

    /// <summary>減量・増量の画面の階級の表の団体（階級制の最初の団体 = IVF）。</summary>
    public static MiniRing.Arena.Organization WeightOrg => CampaignPlan.Orgs.First(o => o.HasWeightClasses);

    /// <summary>
    /// 階級の表の行 <paramref name="row"/> を押したときの目標体重: 今の目標より軽い階級ならその上限、重い階級ならその下限（一つ下の上限 + 1）。
    /// 作れる範囲 [<paramref name="lo"/>, <paramref name="hi"/>] で届かなければ、届く端と、届かないことの知らせ。
    /// </summary>
    private (int Kg, string? Notice) IntoDivision(int row, int lo, int hi)
    {
        var divisions = WeightOrg.Divisions;
        var division = divisions[row];
        var floor = row == 0 ? 0 : (divisions[row - 1].MaxWholeKg ?? 0) + 1;
        var ceiling = division.MaxWholeKg ?? int.MaxValue;
        var wanted = TargetKg > ceiling ? ceiling : TargetKg < floor ? floor : TargetKg;
        var kg = Math.Clamp(wanted, lo, hi);
        return WeightOrg.DivisionOf(kg) == division
            ? (kg, null)
            : (kg, string.Create(CultureInfo.InvariantCulture, $"この骨格では {division.Name}に届きません（作れる体重 {lo}〜{hi} kg）。"));
    }

    /// <summary>目標体重にしたときの体（画面の見込み: 計量・階級・力の変わり方。決めるまでファイルは変わらない）。</summary>
    public BodyBuild? TargetBuild => Boxer is { } boxer && TargetKg != boxer.WeightKg ? BodySizing.WithWeight(boxer.Character.Build, TargetKg) : null;

    /// <summary>目標体重での力の変わり方（筋肉の断面の比 − 1。変えなければ 0）。</summary>
    public float TargetPower => Boxer is { } boxer && TargetBuild is { } target ? (target.SectionRatio / boxer.Character.Build.SectionRatio) - 1f : 0f;
}
