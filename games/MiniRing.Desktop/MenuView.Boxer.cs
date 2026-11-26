using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Aigis.Render;
using MiniRing.Arena;
using MiniRing.Arena.Campaign;
using MiniRing.Characters;

namespace MiniRing.Desktop;

/// <summary>
/// キャンペーンのボクサーの画面の描画（2026-11-26 UI の作り直し）: 本拠地・団体を選ぶ・団体の道・減量・増量と、試合の後の札（解放・席・王座・勝敗）。
/// 文字は美咲フォントの 2 倍以上（設計の高さ 13 以上）。札の動きは描画の時計だけで、試合の計算に入らない。
/// </summary>
internal sealed partial class MenuView
{
    private static readonly Vector4 Green = new(0.45f, 0.95f, 0.55f, 1f);
    private static readonly Vector4 Locked = new(0.40f, 0.48f, 0.54f, 1f);
    private static readonly Vector4 CardBlue = new(0.10f, 0.28f, 0.40f, 1);
    private static readonly Vector4 CardDeep = new(0.04f, 0.13f, 0.26f, 1);
    private static readonly Vector4 Edge = new(0.19f, 0.38f, 0.51f, 1);
    private static readonly Vector4 SelectedTop = new(0.16f, 0.50f, 0.72f, 1);

    private static readonly Stopwatch Watch = Stopwatch.StartNew();

    /// <summary>描画の時計 [s]（札の動きだけに使う）。</summary>
    public Func<double> Clock { get; set; } = () => Watch.Elapsed.TotalSeconds;

    private CampaignCard? _shownCard;
    private double _shownAt;

    /// <summary>今の札を見せ始めてからの時間 [s]。</summary>
    private float CardAge(CampaignCard card)
    {
        var now = Clock();
        if (!ReferenceEquals(card, _shownCard))
        {
            _shownCard = card;
            _shownAt = now;
        }

        return (float)Math.Max(0, now - _shownAt);
    }

    private static void Box(PlateFn plate, FrameFn frame, Vector2 c, Vector2 size, bool selected, Vector4? edge = null, bool dim = false)
    {
        plate(c.X + 3, c.Y + 3, size.X, size.Y, Shadow, Shadow, 6);
        var top = selected ? SelectedTop : dim ? new(0.07f, 0.15f, 0.21f, 1) : CardBlue;
        plate(c.X, c.Y, size.X, size.Y, top, CardDeep, 6);
        frame(c.X, c.Y, size.X, size.Y, selected ? Cyan : edge ?? Edge, selected ? 2.5f : 1.5f, 6);
        if (selected) { plate(c.X - (size.X / 2) + 6, c.Y, 5, size.Y - 12, Gold, Gold, 2); }
    }

    private static string Seat(int? seat) => seat == 0 ? "王者" : CampaignLadder.SeatName(seat);

    // ── 本拠地
    private void DrawHome(PlateFn plate, FrameFn frame, LabelFn label)
    {
        if (state.Boxer is not { } boxer) { return; }
        var c = boxer.Character;

        // 左: 名前・体・戦績。
        plate(170, 160, 284, 120, new(0.08f, 0.22f, 0.32f, 1), new(0.03f, 0.10f, 0.20f, 1), 6);
        frame(170, 160, 284, 120, Edge, 1.5f, 6);
        label(c.HudName, 40, 106, 21, Gold, maxWidth: 260);
        label(string.Create(CultureInfo.InvariantCulture, $"{boxer.WeightKg} kg・身長 {CharacterProfile.HeightOf(c.Build)} cm・{c.StyleName}"), 40, 136, 14, Pale, maxWidth: 260);
        var career = boxer.Career;
        label(string.Create(CultureInfo.InvariantCulture, $"{career.Wins}勝 {career.Losses}敗 {career.Draws}分"), 40, 160, 21, Pale);
        var recent = string.Concat(career.Bouts.TakeLast(8).Select(b => b.Result switch { BoutResult.Win => "○", BoutResult.Loss => "●", _ => "△" }));
        label(recent.Length > 0 ? $"直近 {recent}" : "まだ試合をしていません", 40, 192, 14, Dim);

        // 右: 団体ごとの今の席（IVF は自分の階級）。
        plate(462, 160, 300, 120, new(0.08f, 0.22f, 0.32f, 1), new(0.03f, 0.10f, 0.20f, 1), 6);
        frame(462, 160, 300, 120, Edge, 1.5f, 6);
        label("団体の席", 326, 106, 14, Dim);
        var y = 130f;
        foreach (var org in CampaignPlan.Orgs)
        {
            var division = CampaignPlan.DivisionFor(org, boxer);
            var seat = career.SeatIn(org.Id, division.Id);
            label(org.LogoText, 326, y, 14, org.Colors.Accent with { W = 1f });
            label(org.HasWeightClasses ? division.Name : "無差別", 376, y, 14, Pale, maxWidth: 130);
            label(Seat(seat), 600, y, 14, seat is null ? Dim : Gold, TextAnchor.Right);
            y += 26f;
        }

        // 次の試合（大きく）。
        var selectedNext = state.HomeIndex == MenuState.HomeNext;
        Box(plate, frame, CampaignLayout.NextCenter, CampaignLayout.NextSize, selectedNext);
        var top = CampaignLayout.NextCenter.Y - (CampaignLayout.NextSize.Y / 2);
        if (state.Next is { Entry: { } entry } next)
        {
            label("次の試合", 44, top + 8, 14, Cyan);
            label(next.Event.Name, 44, top + 30, 21, selectedNext ? Gold : Pale, maxWidth: 340);
            var opponent = entry.Opponent;
            label(string.Create(CultureInfo.InvariantCulture, $"相手 {opponent.HudName}（{OrgBouts.WeightOf(opponent)} kg）"), 600, top + 8, 14, Pale, TextAnchor.Right, maxWidth: 220);
            var weapon = state.WeaponFor(entry) is { } w ? FighterCharacter.WeaponLabel(w) : "素手";
            label(entry.Weapons.Count > 1 ? $"◀ {weapon} ▶" : weapon, 600, top + 34, 14, selectedNext ? Gold : Dim, TextAnchor.Right);
        }
        else
        {
            label("出られる大会がありません", 44, top + 22, 21, Dim);
        }

        string[] names = ["団体と大会", "体重を変える", state.ConfirmingDelete ? "もう一度で消す" : "消す", "戻る"];
        for (var i = 0; i < names.Length; i++)
        {
            var bc = CampaignLayout.HomeButtonCenter(i);
            var selected = state.HomeIndex == i + 1;
            Box(plate, frame, bc, CampaignLayout.HomeButtonSize, selected);
            label(names[i], bc.X + 3, bc.Y - 8, 14, selected ? Gold : Pale, TextAnchor.Center);
        }
    }

    // ── 団体を選ぶ
    private void DrawOrgs(PlateFn plate, FrameFn frame, LabelFn label)
    {
        if (state.Boxer is not { } boxer) { return; }
        var orgs = CampaignPlan.Orgs;
        for (var i = 0; i < orgs.Count; i++)
        {
            var org = orgs[i];
            var c = CampaignLayout.OrgCenter(i);
            var selected = state.OrgIndex == i;
            var accent = org.Colors.Accent with { W = 1f };
            Box(plate, frame, c, CampaignLayout.OrgSize, selected, accent with { W = 0.6f });
            label(org.LogoText, 44, c.Y - 16, 28, selected ? Gold : accent);
            var division = CampaignPlan.DivisionFor(org, boxer);
            label(org.Name, 140, c.Y - 22, 14, Dim, maxWidth: 290);
            label($"{(org.HasWeightClasses ? division.Name : "無差別")}・{Seat(boxer.Career.SeatIn(org.Id, division.Id))}", 140, c.Y + 2, 14, selected ? Gold : Pale, maxWidth: 290);
            // 3 段の開き具合（緑 = 出られる）。
            var path = CampaignPlan.Path(state.Stops, org, boxer);
            string[] pips = ["新人", "順位", "王座"];
            for (var k = 0; k < path.Count && k < pips.Length; k++)
            {
                var x = 466f + (k * 52f);
                var open = path[k].Open;
                plate(x, c.Y, 46, 26, open ? new(0.20f, 0.55f, 0.30f, 1) : new(0.10f, 0.16f, 0.22f, 1), open ? new(0.08f, 0.30f, 0.14f, 1) : new(0.05f, 0.09f, 0.14f, 1), 4);
                frame(x, c.Y, 46, 26, open ? Green : Locked, 1.2f, 4);
                label(pips[k], x, c.Y - 8, 14, open ? Pale : Locked, TextAnchor.Center, shadow: open);
            }
        }

        var back = state.OrgIndex == orgs.Count;
        Box(plate, frame, CampaignLayout.OrgBackCenter, CampaignLayout.BackSize, back);
        label("戻る", CampaignLayout.OrgBackCenter.X + 3, CampaignLayout.OrgBackCenter.Y - 8, 14, back ? Gold : Pale, TextAnchor.Center);
    }

    // ── 団体の道
    private void DrawPath(MenuCanvas canvas, PlateFn plate, FrameFn frame, LabelFn label)
    {
        if (state.Boxer is not { } boxer) { return; }
        var org = state.PathOrg;
        var division = CampaignPlan.DivisionFor(org, boxer);
        var path = state.PathStops;
        var seat = boxer.Career.SeatIn(org.Id, division.Id);
        label($"{(org.HasWeightClasses ? division.Name : "無差別級")}（{(org.Entry.WeighIn ? "計量" : "体重")} {boxer.WeightKg.ToString(CultureInfo.InvariantCulture)} kg）", 30, 94, 14, Pale, maxWidth: 380);
        label($"今の席  {Seat(seat)}", 610, 94, 14, seat is null ? Dim : Gold, TextAnchor.Right);

        // いまここ = 出られる一番先の段、次の目標 = その先で数で近づける段。
        var here = path.Select((s, i) => (s, i)).Where(p => p.s.Open).Select(p => p.i).DefaultIfEmpty(-1).Max();
        var goal = path.Select((s, i) => (s, i)).FirstOrDefault(p => p.i > here && p.s.Gate.Kind is CampaignGateKind.Wins or CampaignGateKind.Seat).i;
        if (goal <= here) { goal = -1; }
        for (var i = 0; i < path.Count; i++)
        {
            var stop = path[i];
            var c = CampaignLayout.NodeCenter(i);
            var selected = state.PathIndex == i;
            var open = stop.Open;
            var title = stop.Event.Tier == CampaignTier.Title;
            Box(plate, frame, c, CampaignLayout.NodeSize, selected, open ? (title ? Gold : Green) : Locked, dim: !open);
            if (i < path.Count - 1)
            {
                label("▶", c.X + (CampaignLayout.NodeSize.X / 2) + 16, c.Y - 12, 21, path[i + 1].Open ? Green : Locked, TextAnchor.Center);
            }

            var top = c.Y - (CampaignLayout.NodeSize.Y / 2);
            if (i == here || i == goal)
            {
                var badge = i == here ? "いまここ" : "次の目標";
                var ink = i == here ? Green : Cyan;
                plate(c.X, top, 82, 20, ink with { W = 1f }, ink * new Vector4(0.6f, 0.6f, 0.6f, 1f), 4);
                label(badge, c.X, top - 8, 14, Shadow, TextAnchor.Center, shadow: false);
            }

            label(CampaignPlan.TierName(stop.Event.Tier), c.X, top + 20, 21, open ? (selected ? Gold : Pale) : Locked, TextAnchor.Center);
            label(CampaignPlan.GateText(stop), c.X, top + 52, 14, open ? Green : Pale, TextAnchor.Center, maxWidth: CampaignLayout.NodeSize.X - 16);
            if (CampaignPlan.WinProgress(stop) is { } progress)
            {
                const float BarW = 132f;
                plate(c.X, top + 82, BarW, 8, new(0.03f, 0.08f, 0.14f, 1), new(0.03f, 0.08f, 0.14f, 1), 3);
                if (progress > 0f)
                {
                    var w = BarW * progress;
                    plate(c.X - (BarW / 2) + (w / 2), top + 82, w, 8, open ? Green : Cyan, open ? Green : Cyan, 3);
                }

                label(string.Create(CultureInfo.InvariantCulture, $"{Math.Min(stop.Gate.Have, stop.Event.Record.MinWins)} / {stop.Event.Record.MinWins} 勝"), c.X, top + 90, 14, Dim, TextAnchor.Center);
            }
            else if (stop.Gate.Kind == CampaignGateKind.Seat || (title && open))
            {
                label($"席 {Seat(stop.Seat)}", c.X, top + 84, 14, Dim, TextAnchor.Center);
            }
        }

        // 下: 選んでいる段の中身。
        plate(240, 300, 424, 96, new(0.08f, 0.22f, 0.32f, 1), new(0.03f, 0.10f, 0.20f, 1), 6);
        frame(240, 300, 424, 96, Edge, 1.5f, 6);
        if (state.PathIndex < path.Count)
        {
            var stop = path[state.PathIndex];
            label(stop.Event.Name, 40, 258, 14, Gold, maxWidth: 400);
            if (stop.Entry is { } entry)
            {
                var o = entry.Opponent;
                label(string.Create(CultureInfo.InvariantCulture, $"相手 {o.HudName}（{OrgBouts.WeightOf(o)} kg・{o.StyleName}）"), 40, 280, 14, Pale, maxWidth: 400);
                label(stop.Event.Note, 40, 302, 14, Dim, maxWidth: 400);
                label(state.Next?.Event.Id == stop.Event.Id ? "これが次の試合です" : "Enter で次の試合にする", 40, 324, 14, Cyan);
            }
            else
            {
                var ny = 280f;
                foreach (var line in MenuText.Wrap(stop.Gate.Reason ?? "", 400 * canvas.Scale, 14 * canvas.Scale).Take(3))
                {
                    label(line, 40, ny, 14, Pale);
                    ny += 22f;
                }
            }
        }
        else
        {
            label("団体を選び直します", 40, 258, 14, Pale);
        }

        var back = state.PathIndex == MenuState.PathBack;
        Box(plate, frame, CampaignLayout.PathBackCenter, CampaignLayout.BackSize, back);
        label("戻る", CampaignLayout.PathBackCenter.X + 3, CampaignLayout.PathBackCenter.Y - 8, 14, back ? Gold : Pale, TextAnchor.Center);
    }

    // ── 減量・増量
    private void DrawWeight(MenuCanvas canvas, PlateFn plate, FrameFn frame, LabelFn label)
    {
        if (state.Boxer is not { } boxer) { return; }
        string[] rows = [state.WeightLine(), "この体重に決める", "戻る"];
        for (var i = 0; i < rows.Length; i++)
        {
            var c = CampaignLayout.WeightRowCenter(i);
            var selected = state.WeightIndex == i;
            Box(plate, frame, c, CampaignLayout.WeightRowSize, selected);
            label(rows[i], c.X - (CampaignLayout.WeightRowSize.X / 2) + 18, c.Y - 8, 14, selected ? Gold : Pale);
        }

        // 左の下: IVF の階級の表（今の体重と目標の体重がどこに入るか）。
        var target = state.TargetBuild is { } build ? CharacterProfile.WeightOf(build) : boxer.WeightKg;
        var y = 262f;
        foreach (var org in CampaignPlan.Orgs.Where(o => o.HasWeightClasses))
        {
            label($"{org.LogoText} の階級", 40, y, 14, Dim);
            y += 20f;
            var lower = 0;
            foreach (var division in org.Divisions)
            {
                var now = org.DivisionOf(boxer.WeightKg) == division;
                var then = org.DivisionOf(target) == division;
                var range = division.LimitKg is { } limit
                    ? string.Create(CultureInfo.InvariantCulture, $"〜{limit} kg")
                    : string.Create(CultureInfo.InvariantCulture, $"{lower} kg 超");
                label(then ? "▶" : now ? "・" : "", 40, y, 14, then ? Gold : Dim);
                label(division.Name, 56, y, 14, then ? Gold : now ? Pale : Dim);
                label(range, 300, y, 14, then ? Gold : Dim, TextAnchor.Right);
                lower = division.LimitKg ?? lower;
                y += 18f;
            }
        }

        Panel(plate, frame);
        label("減量・増量", 326, 106, 21, Gold);
        var ny = 136f;
        foreach (var line in MenuText.Wrap("骨格はそのまま、筋肉の太さが変わります。力は筋肉の断面から出るので、減量すれば力も落ちます。", (PanelRight - 326) * canvas.Scale, 14 * canvas.Scale))
        {
            label(line, 326, ny, 14, Pale);
            ny += 20f;
        }

        var (lo, hi) = boxer.WeightRange;
        label(string.Create(CultureInfo.InvariantCulture, $"作れる体重 {lo}〜{hi} kg"), 326, ny + 6, 14, Dim);
        ny += 34f;
        label(string.Create(CultureInfo.InvariantCulture, $"{boxer.WeightKg} kg ▶ {target} kg"), 326, ny, 21, state.TargetBuild is null ? Pale : Gold);
        label(string.Create(CultureInfo.InvariantCulture, $"力 {state.TargetPower:+0%;-0%;±0%}"), 326, ny + 30, 21, state.TargetPower < 0 ? new(1f, 0.6f, 0.5f, 1) : Green);
        ny += 66f;
        foreach (var org in CampaignPlan.Orgs.Where(o => o.HasWeightClasses))
        {
            var then = org.DivisionOf(target);
            var now = org.DivisionOf(boxer.WeightKg);
            var text = then == now ? $"{org.LogoText}: {now.Name}のまま" : $"{org.LogoText}: この体重なら{then.Name}";
            label(text, 326, ny, 14, then == now ? Pale : Gold, maxWidth: PanelRight - 326);
            ny += 20f;
        }

        label("WVC・VFC は無差別級", 326, ny, 14, Dim);
    }

    // ── 試合の後の札
    private void DrawCard(PlateFn plate, FrameFn frame, LabelFn label)
    {
        if (state.Cards.Count == 0) { return; }
        var card = state.Cards[0];
        var t = CardAge(card);
        var appear = Math.Clamp(t / 0.25f, 0f, 1f);
        plate(320, 232, 640, 464, new(0f, 0.02f, 0.06f, 0.72f * appear), new(0f, 0.02f, 0.06f, 0.72f * appear));

        var (glow, deep) = card switch
        {
            CampaignCard.Result { Outcome: not BoutResult.Win } => (new Vector4(0.30f, 0.42f, 0.55f, 1), new Vector4(0.08f, 0.13f, 0.20f, 1)),
            CampaignCard.Title => (Gold, new Vector4(0.45f, 0.25f, 0.02f, 1)),
            _ => (Cyan, new Vector4(0.03f, 0.12f, 0.26f, 1)),
        };
        var calm = card is CampaignCard.Result { Outcome: not BoutResult.Win };
        const float W = 540f, H = 270f, Cx = 320f, Cy = 226f;
        plate(Cx + 4, Cy + 5, W, H, Shadow, Shadow, 10);
        plate(Cx, Cy, W, H, deep with { W = 1f } * new Vector4(1.4f, 1.4f, 1.4f, 1f), deep, 10);
        if (!calm)
        {
            // 光る帯（左から右へ流れる）と、脈打つ縁。
            var pulse = 0.5f + (0.5f * MathF.Sin(t * 5f));
            frame(Cx, Cy, W + 8 + (pulse * 6), H + 8 + (pulse * 6), glow with { W = 0.35f + (0.3f * pulse) }, 3f, 12);
            plate(Cx, Cy - 72, W - 24, 46, glow with { W = 0.20f }, glow with { W = 0.05f }, 6);
            // 光は 3 秒ごとに 1 回、左から右へ走る（枠の外では描かない）。
            var bandX = Cx - (W / 2) + 30 + (t % 3f * 600f);
            if (bandX < Cx + (W / 2) - 30) { plate(bandX, Cy, 40, H - 12, new(1f, 1f, 1f, 0.10f), new(1f, 1f, 1f, 0.02f), 4); }
        }

        frame(Cx, Cy, W, H, glow, 2.5f, 10);
        var pop = 1f + (0.25f * MathF.Max(0f, 1f - (t / 0.3f))); // 出だしに少し大きく
        switch (card)
        {
            case CampaignCard.Result r: DrawResult(r, t, pop, label); break;
            case CampaignCard.RankUp up: DrawRankUp(up, t, pop, plate, label); break;
            case CampaignCard.Title title: DrawTitle(title, t, pop, plate, frame, label); break;
            case CampaignCard.Unlocked unlocked: DrawUnlocked(unlocked, t, pop, plate, label); break;
        }

        var more = state.Cards.Count - 1;
        label(more > 0 ? $"Enter で次へ（あと {more}）" : "Enter で閉じる", Cx, Cy + (H / 2) - 26, 14, Dim, TextAnchor.Center);
    }

    private static void DrawResult(CampaignCard.Result r, float t, float pop, LabelFn label)
    {
        var (word, ink) = r.Outcome switch
        {
            BoutResult.Win => ("勝利！", Gold),
            BoutResult.Loss => ("敗戦", new Vector4(0.70f, 0.80f, 0.90f, 1)),
            _ => ("引き分け", Pale),
        };
        var how = r.Outcome switch
        {
            BoutResult.Win => r.Finish ? "倒して勝ち" : "判定で勝ち",
            BoutResult.Loss => r.Finish ? "倒されて負け" : "判定で負け",
            _ => "決着つかず",
        };
        label(word, 320, 112, r.Outcome == BoutResult.Win ? 41 * pop : 34, ink, TextAnchor.Center);
        label($"{r.EventName}・{how}", 320, 170, 14, Pale, TextAnchor.Center, maxWidth: 500);
        label($"相手 {r.Opponent}", 320, 192, 14, Dim, TextAnchor.Center, maxWidth: 500);
        static string Rec((int W, int L, int D) x) => string.Create(CultureInfo.InvariantCulture, $"{x.W}勝{x.L}敗{x.D}分");
        label(Rec(r.Before), 300, 222, 21, Dim, TextAnchor.Right);
        label("▶", 320, 222, 21, Dim, TextAnchor.Center);
        var lit = t > 0.4f && r.Outcome == BoutResult.Win && (int)(t * 4) % 2 == 0;
        label(Rec(r.After), 340, 222, 21, lit ? Gold : Pale);
        label($"次の目標  {r.NextGoal}", 320, 268, 14, Cyan, TextAnchor.Center, maxWidth: 500);
    }

    private static void DrawRankUp(CampaignCard.RankUp up, float t, float pop, PlateFn plate, LabelFn label)
    {
        label("ランキング上昇！", 320, 112, 34 * pop, Gold, TextAnchor.Center);
        // 数字が前の席から今の席へ動く（0.9 秒）。
        var from = up.From ?? (up.To + 3);
        var k = Math.Clamp((t - 0.3f) / 0.9f, 0f, 1f);
        var shown = (int)MathF.Round(from + ((up.To - from) * k));
        var start = up.From is null ? "圏外" : string.Create(CultureInfo.InvariantCulture, $"{up.From} 位");
        label(up.Where, 320, 162, 14, Pale, TextAnchor.Center);
        label(start, 250, 196, 28, Dim, TextAnchor.Right);
        label("▶", 290, 196, 28, Dim, TextAnchor.Center);
        label(k >= 1f || up.From is not null ? string.Create(CultureInfo.InvariantCulture, $"{shown} 位") : "…", 330, 196, 41, k >= 1f ? Gold : Pale);
        var rise = MathF.Sin(t * 6f) * 4f;
        label("▲", 470, 192 - rise, 41, Green, TextAnchor.Center);
        plate(320, 262, 360, 26, new(0.20f, 0.55f, 0.30f, 0.6f), new(0.08f, 0.30f, 0.14f, 0.6f), 4);
        label($"{up.Passed} を追い越した", 320, 254, 14, Pale, TextAnchor.Center, maxWidth: 340);
    }

    private static void DrawTitle(CampaignCard.Title title, float t, float pop, PlateFn plate, FrameFn frame, LabelFn label)
    {
        label(title.Defense ? "防衛成功！" : "新王者！", 320, 104, 41 * pop, Gold, TextAnchor.Center);
        // ベルト: 帯・左右の飾り板・中央の金の板（光が走る）。
        var shine = 0.5f + (0.5f * MathF.Sin(t * 4f));
        plate(320, 196, 420, 34, new(0.55f, 0.12f, 0.10f, 1), new(0.30f, 0.05f, 0.05f, 1), 6);
        frame(320, 196, 420, 34, Gold, 2f, 6);
        foreach (var side in new[] { -1f, 1f })
        {
            plate(320 + (side * 120), 196, 54, 46, new(0.95f, 0.80f, 0.35f, 1), new(0.60f, 0.40f, 0.08f, 1), 6);
            frame(320 + (side * 120), 196, 54, 46, new(1f, 0.95f, 0.70f, 1), 1.5f, 6);
        }

        plate(320, 196, 140, 80, new Vector4(1f, 0.88f, 0.45f, 1) * new Vector4(0.85f + (0.15f * shine), 0.85f + (0.15f * shine), 0.85f + (0.15f * shine), 1f), new(0.70f, 0.45f, 0.05f, 1), 10);
        frame(320, 196, 140, 80, new(1f, 0.97f, 0.80f, 1), 2.5f, 10);
        frame(320, 196, 120, 62, new(0.55f, 0.32f, 0.02f, 1), 1.5f, 8);
        label("★", 320, 170, 21, new(0.55f, 0.20f, 0.05f, 1), TextAnchor.Center, shadow: false);
        label("CHAMP", 320, 196, 14, new(0.40f, 0.18f, 0.02f, 1), TextAnchor.Center, shadow: false);
        label($"{title.Where} 王者", 320, 248, 21, Gold, TextAnchor.Center, maxWidth: 500);
        label(title.Defense ? $"挑戦者 {title.Defeated} を退けた" : $"王者 {title.Defeated} を破った", 320, 278, 14, Pale, TextAnchor.Center, maxWidth: 500);
    }

    private static void DrawUnlocked(CampaignCard.Unlocked unlocked, float t, float pop, PlateFn plate, LabelFn label)
    {
        var blink = (int)(t * 3) % 2 == 0;
        plate(320, 112, 120 * pop, 40 * pop, blink ? new(1f, 0.30f, 0.25f, 1) : new(0.85f, 0.20f, 0.18f, 1), new(0.55f, 0.08f, 0.08f, 1), 8);
        label("NEW!", 320, 98, 28 * pop, new(1f, 0.97f, 0.85f, 1), TextAnchor.Center);
        label("新しい大会に出られる！", 320, 146, 21, Gold, TextAnchor.Center, maxWidth: 500);
        var y = 180f;
        foreach (var ev in unlocked.Events.Take(3))
        {
            label($"{CampaignPlan.TierName(ev.Tier)}に挑戦できる！", 320, y, 21, Green, TextAnchor.Center, maxWidth: 500);
            label(ev.Name, 320, y + 26, 14, Pale, TextAnchor.Center, maxWidth: 500);
            y += 52f;
        }

        if (unlocked.Events.Count > 3) { label($"ほか {unlocked.Events.Count - 3} 大会", 320, y, 14, Dim, TextAnchor.Center); }
    }
}
