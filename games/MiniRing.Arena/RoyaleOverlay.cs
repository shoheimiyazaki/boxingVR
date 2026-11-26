using System.Globalization;
using System.Numerics;
using Aigis.Render;

namespace MiniRing.Arena;

/// <summary>
/// バトルロイヤルの中継の画面（見せ方だけ。試合の計算に入らない）。誰がどの選手かを、戦いの絵の邪魔をしない所・時に出す:
/// <list type="bullet">
/// <item>左の名簿: 全体の絵では名前・流派・体の残り・状態の札。寄った絵では細い札に縮み、映っている 2 人を光らせる。死んだ選手は暗い札に赤い「死亡」と死因・倒した人。</item>
/// <item>頭の上の名札: 全体の絵だけ（試合の始めと、全体へ引いた直後の 2 秒は流派も）。倒れた体には灰色の「死」の札。</item>
/// <item>下の帯（ロワーサード）: 戦いへ寄った瞬間に 2.2 秒だけ、映っている 2 人の名前・流派・体の残り。</item>
/// <item>脱落の帯: 脱落の瞬間に 4 秒、誰が・何で・誰に。決着では勝者の札。</item>
/// </list>
/// </summary>
internal sealed class RoyaleOverlay(int width, int height, IReadOnlyList<TvCorner> corners, IReadOnlyList<string>? teamNames)
{
    private static readonly Vector4 Ink = new(0.97f, 0.95f, 0.9f, 1f);
    private static readonly Vector4 Muted = new(0.72f, 0.7f, 0.66f, 1f);
    private static readonly Vector4 Dim = new(0.45f, 0.44f, 0.42f, 1f);
    private static readonly Vector4 Panel = new(0.05f, 0.05f, 0.06f, 0.78f);
    private static readonly Vector4 PanelDead = new(0.12f, 0.03f, 0.03f, 0.82f);
    private static readonly Vector4 Blood = new(0.86f, 0.12f, 0.1f, 1f);
    private static readonly Vector4 Amber = new(0.98f, 0.68f, 0.12f, 1f);
    private static readonly Vector4 Gold = new(0.98f, 0.82f, 0.36f, 1f);
    private static readonly Vector4 RedKit = new(0.9f, 0.2f, 0.16f, 1f);
    private static readonly Vector4 BlueKit = new(0.24f, 0.46f, 0.96f, 1f);

    /// <summary>下の帯を出す長さ [s]（寄った瞬間から）。</summary>
    public const float LowerThirdSeconds = 2.2f;

    /// <summary>脱落の帯を出す長さ [s]。</summary>
    public const float EliminationSeconds = 4f;

    private static readonly Organization Vfc = Organizations.Get("vfc");

    private float U => height / 540f; // 960×540 を基準にした大きさ

    /// <summary>今の画面の文字と札。</summary>
    public (ScreenTextDraw[] Text, UiShapeDraw[] Shapes) Draw(BoxingGame game, Matrix4x4 vp, float t, int[] startCells, Shot shot, float shotStart)
    {
        var text = new List<ScreenTextDraw>();
        var shapes = new List<UiShapeDraw>();
        var since = t - shotStart;
        Header(game, text, shapes, shot);
        if (!game.MatchOver)
        {
            Tags(game, vp, text, shapes, t, startCells, shot, since);
        }

        Roster(game, text, shapes, startCells, shot);
        var banner = game.Eliminations.LastOrDefault(e => t - e.Time < EliminationSeconds && t >= e.Time);
        if (game.MatchOver)
        {
            Winner(game, text, shapes);
        }
        else if (banner is not null)
        {
            EliminationBanner(game, text, shapes, banner, t - banner.Time);
        }
        else if (!shot.IsWide && since < LowerThirdSeconds)
        {
            LowerThird(game, text, shapes, startCells, shot, since);
        }

        return ([.. text], [.. shapes]);
    }

    private void Header(BoxingGame game, List<ScreenTextDraw> text, List<UiShapeDraw> shapes, Shot shot)
    {
        var u = U;
        Emblems.Draw(Vfc, text, shapes, new Vector2(70f * u, 30f * u), 0.95f * u);
        var left = game.AllFighters.Count(game.InPlay);
        var title = $"バトルロイヤル  残り {left} / {game.AllFighters.Count} 人";
        Pill(text, shapes, title, new Vector2(150f * u, 30f * u), 17f * u, Panel, Ink, TextAnchor.Left);
        var clock = game.InIntro ? "まもなく開始"
            : game.InBreak ? string.Create(CultureInfo.InvariantCulture, $"休み  R{game.MatchRound}  {game.BreakLeft:0}s")
            : string.Create(CultureInfo.InvariantCulture, $"R{game.MatchRound}   {Clock(game.RoundLeft)}");
        Pill(text, shapes, clock, new Vector2(width - (16f * u), 30f * u), 19f * u, Panel, Ink, TextAnchor.Right);
        if (!shot.IsWide)
        {
            Pill(text, shapes, $"注目  {game.LabelOf(game.AllFighters[shot.A])} × {game.LabelOf(game.AllFighters[shot.B])}",
                new Vector2(width - (16f * u), 60f * u), 15f * u, new Vector4(0.45f, 0.06f, 0.05f, 0.85f), Ink, TextAnchor.Right);
        }
    }

    /// <summary>左の名簿。全体の絵では札を広く（流派まで）、寄った絵では細く（名前と体の残りだけ）。</summary>
    private void Roster(BoxingGame game, List<ScreenTextDraw> text, List<UiShapeDraw> shapes, int[] startCells, Shot shot)
    {
        var u = U;
        var wide = shot.IsWide || game.MatchOver;
        var rowH = (wide ? 34f : 22f) * u;
        var rowW = (wide ? 214f : 150f) * u;
        var x0 = 14f * u;
        var y0 = 58f * u;
        var deaths = game.Eliminations.ToDictionary(e => e.Fighter);
        for (var i = 0; i < game.AllFighters.Count; i++)
        {
            var f = game.AllFighters[i];
            var dead = deaths.TryGetValue(f, out var death);
            var focused = !shot.IsWide && (i == shot.A || i == shot.B);
            var top = y0 + (i * (rowH + (3f * u)));
            var center = new Vector2(x0 + (rowW * 0.5f), top + (rowH * 0.5f));
            var alpha = wide || focused || dead ? 1f : 0.7f;
            shapes.Add(UiShapeDraw.Rounded(center, new Vector2(rowW, rowH), 4f * u, (dead ? PanelDead : Panel) with { W = (dead ? PanelDead : Panel).W * alpha }));
            if (focused)
            {
                shapes.Add(UiShapeDraw.Outline(center, new Vector2(rowW, rowH), 4f * u, Gold, 1.5f * u));
            }

            // 色の帯: チーム戦はチームの色、そうでなければ赤・青の装い。死んだ選手は灰色。
            var kit = dead ? Dim : KitColor(game, i);
            shapes.Add(UiShapeDraw.Rounded(new Vector2(x0 + (3f * u), center.Y), new Vector2(4f * u, rowH - (6f * u)), 2f * u, kit));
            var name = game.LabelOf(f);
            var nameSize = (wide ? 15f : 13f) * u;
            var nameColor = dead ? Dim : Ink;
            var nameAt = new Vector2(x0 + (11f * u), top + (wide ? 3f * u : 3.5f * u));
            text.Add(new ScreenTextDraw(name, nameAt, nameSize, nameColor));
            if (dead)
            {
                // 取り消し線と、赤い「死亡」の札。全体の絵では死因と倒した人も。
                var nameWidth = ScreenText.MeasurePixelWidth(name, nameSize);
                shapes.Add(UiShapeDraw.Rounded(nameAt + new Vector2(nameWidth * 0.5f, nameSize * 0.55f), new Vector2(nameWidth + (4f * u), 1.6f * u), 0.8f * u, Blood));
                Chip(text, shapes, "死亡", new Vector2(x0 + rowW - (6f * u), top + (rowH * (wide ? 0.3f : 0.5f))), 11f * u, Blood, Ink);
                if (wide && death is not null)
                {
                    var by = death.By is { } killer ? $" ← {game.LabelOf(killer)}" : "";
                    text.Add(new ScreenTextDraw($"{RoyaleVideo.Cause(death.Cause)}{by}", new Vector2(x0 + (11f * u), top + (19f * u)), 11f * u, new Vector4(0.9f, 0.45f, 0.4f, 1f)));
                }

                continue;
            }

            var body = Math.Clamp((float)Cells(f) / startCells[i], 0f, 1f);
            if (wide)
            {
                text.Add(new ScreenTextDraw(corners[i].Discipline, new Vector2(x0 + (11f * u), top + (19.5f * u)), 10.5f * u, Muted));
                Bar(shapes, new Vector2(x0 + rowW - (66f * u), top + (24f * u)), 58f * u, 4f * u, body);
            }
            else
            {
                Bar(shapes, new Vector2(x0 + rowW - (50f * u), center.Y), 42f * u, 4f * u, body);
            }

            if (f.State != FighterState.Standing)
            {
                Chip(text, shapes, "ダウン", new Vector2(x0 + rowW - (6f * u), top + (rowH * (wide ? 0.3f : 0.5f)) - (wide ? 0f : 0f)), 10.5f * u, Amber, new Vector4(0.1f, 0.06f, 0f, 1f),
                    shiftLeft: wide ? 0f : 48f * u);
            }
        }
    }

    /// <summary>
    /// 頭の上の名札。全体の絵だけ（寄った絵は 2 人が大きく映り、名前は下の帯と名簿で読める）。試合の始めの 8 秒と、全体へ引いた直後の 2 秒は流派も添える。
    /// 倒れた（死んだ）体には胸の上に灰色の「死」の札。
    /// </summary>
    private void Tags(BoxingGame game, Matrix4x4 vp, List<ScreenTextDraw> text, List<UiShapeDraw> shapes, float t, int[] startCells, Shot shot, float since)
    {
        if (!shot.IsWide)
        {
            return;
        }

        var u = U;
        var screen = new Vector2(width, height);
        var detail = t < 8f || since < 2f;
        var placed = new List<(Vector2 Center, Vector2 Size)>(); // 重ならないよう、先に置いた札の上へずらす
        Vector2 Free(Vector2 at, Vector2 size)
        {
            for (var tries = 0; tries < 6; tries++)
            {
                var hit = placed.FirstOrDefault(p => MathF.Abs(p.Center.X - at.X) < (p.Size.X + size.X) * 0.5f && MathF.Abs(p.Center.Y - at.Y) < (p.Size.Y + size.Y) * 0.5f);
                if (hit.Size == Vector2.Zero)
                {
                    break;
                }

                at = at with { Y = hit.Center.Y - ((hit.Size.Y + size.Y) * 0.5f) - (2f * u) };
            }

            placed.Add((at, size));
            return at;
        }

        // 手前（画面の下）の選手の札から置く: 奥の札が上へよける。
        var order = Enumerable.Range(0, game.AllFighters.Count)
            .OrderByDescending(i => Camera.TryWorldToScreen(vp, game.Display.Point(game.AllFighters[i].HeadPosition), screen, out var p) ? p.Y : float.MinValue);
        foreach (var i in order)
        {
            var f = game.AllFighters[i];
            if (!game.InPlay(f))
            {
                if (Camera.TryWorldToScreen(vp, game.Display.Point(f.ChestPosition + new Vector3(0f, 0.25f, 0f)), screen, out var corpse))
                {
                    var deadLabel = $"死  {game.LabelOf(f)}";
                    var size0 = 11f * u;
                    var box = new Vector2(ScreenText.MeasurePixelWidth(deadLabel, size0) + (16f * u), size0 + (10f * u));
                    var at0 = Free(corpse, box);
                    shapes.Add(UiShapeDraw.Rounded(at0, box, box.Y * 0.5f, new Vector4(0.1f, 0.02f, 0.02f, 0.78f)));
                    shapes.Add(UiShapeDraw.Outline(at0, box, box.Y * 0.5f, Blood with { W = 0.8f }, 1f * u));
                    text.Add(new ScreenTextDraw(deadLabel, new Vector2(at0.X, at0.Y - (size0 * 0.62f)), size0, Muted, TextAnchor.Center));
                }

                continue;
            }

            if (!Camera.TryWorldToScreen(vp, game.Display.Point(f.HeadPosition + new Vector3(0f, 0.42f, 0f)), screen, out var pixel))
            {
                continue;
            }

            var label = detail ? $"{game.LabelOf(f)}  {corners[i].Discipline}" : game.LabelOf(f);
            var size = 12.5f * u;
            var w = ScreenText.MeasurePixelWidth(label, size) + (12f * u);
            var h = size + (9f * u);
            var at = Free(pixel - new Vector2(0f, h), new Vector2(w, h + (6f * u)));
            shapes.Add(UiShapeDraw.Rounded(at, new Vector2(w, h), 3f * u, new Vector4(0.04f, 0.04f, 0.05f, 0.72f)));
            shapes.Add(UiShapeDraw.Rounded(at + new Vector2(0f, (h * 0.5f) - (1f * u)), new Vector2(w - (4f * u), 2f * u), 1f * u, KitColor(game, i)));
            text.Add(new ScreenTextDraw(label, at - new Vector2(0f, size * 0.62f), size, Ink, TextAnchor.Center));
            var body = Math.Clamp((float)Cells(f) / startCells[i], 0f, 1f);
            Bar(shapes, at + new Vector2(-(w * 0.5f) + (2f * u), (h * 0.5f) + (3f * u)), w - (4f * u), 2.5f * u, body);
            if (f.State != FighterState.Standing)
            {
                Chip(text, shapes, "ダウン", at + new Vector2((w * 0.5f) + (2f * u) + ScreenText.MeasurePixelWidth("ダウン", 10f * u) + (8f * u), -(5f * u)), 10f * u, Amber, new Vector4(0.1f, 0.06f, 0f, 1f));
            }
        }
    }

    /// <summary>寄った瞬間の下の帯: 映っている 2 人の名前・流派・体の残り（左右の札と中央の VS）。出入りは横に滑る。</summary>
    private void LowerThird(BoxingGame game, List<ScreenTextDraw> text, List<UiShapeDraw> shapes, int[] startCells, Shot shot, float since)
    {
        var u = U;
        var appear = Math.Clamp(since / 0.25f, 0f, 1f);
        var leave = Math.Clamp((LowerThirdSeconds - since) / 0.35f, 0f, 1f);
        var k = MathF.Min(appear, leave);
        var ease = 1f - MathF.Pow(1f - k, 3f);
        var y = height - (62f * u);
        var cardW = 250f * u;
        var cardH = 54f * u;
        var gap = 34f * u;
        Card(shot.A, new Vector2((width * 0.5f) - gap - (cardW * 0.5f) - ((1f - ease) * 120f * u), y), TextAnchor.Right);
        Card(shot.B, new Vector2((width * 0.5f) + gap + (cardW * 0.5f) + ((1f - ease) * 120f * u), y), TextAnchor.Left);
        shapes.Add(UiShapeDraw.Rounded(new Vector2(width * 0.5f, y), new Vector2(46f * u, 30f * u), 15f * u, Blood with { W = ease }));
        text.Add(new ScreenTextDraw("VS", new Vector2(width * 0.5f, y - (10f * u)), 17f * u, Ink with { W = ease }, TextAnchor.Center));

        void Card(int seat, Vector2 center, TextAnchor side)
        {
            var f = game.AllFighters[seat];
            shapes.Add(UiShapeDraw.Shadow(center, new Vector2(cardW, cardH), 6f * u, new Vector4(0f, 0f, 0f, 0.4f * ease), 10f * u, 3f * u));
            shapes.Add(UiShapeDraw.VerticalGradient(center, new Vector2(cardW, cardH), 6f * u, new Vector4(0.1f, 0.1f, 0.12f, 0.9f * ease), new Vector4(0.03f, 0.03f, 0.04f, 0.9f * ease)));
            var edge = side == TextAnchor.Right ? center.X + (cardW * 0.5f) - (4f * u) : center.X - (cardW * 0.5f) + (4f * u);
            shapes.Add(UiShapeDraw.Rounded(new Vector2(edge, center.Y), new Vector2(5f * u, cardH - (10f * u)), 2.5f * u, KitColor(game, seat) with { W = ease }));
            var inner = side == TextAnchor.Right ? edge - (12f * u) : edge + (12f * u);
            text.Add(new ScreenTextDraw(game.LabelOf(f), new Vector2(inner, center.Y - (23f * u)), 22f * u, Ink with { W = ease }, side));
            text.Add(new ScreenTextDraw($"{corners[seat].Discipline}・{corners[seat].Detail}", new Vector2(inner, center.Y + (4f * u)), 11f * u, Muted with { W = ease }, side));
            var body = Math.Clamp((float)Cells(f) / startCells[seat], 0f, 1f);
            var barW = 90f * u;
            var barLeft = side == TextAnchor.Right ? inner - barW : inner;
            Bar(shapes, new Vector2(barLeft, center.Y + (21f * u)), barW, 3.5f * u, body, ease);
        }
    }

    /// <summary>脱落の帯: 赤い帯に「脱落」と、誰が・何で・誰に。出るときは帯が横へ伸びる。</summary>
    private void EliminationBanner(BoxingGame game, List<ScreenTextDraw> text, List<UiShapeDraw> shapes, Elimination e, float age)
    {
        var u = U;
        var grow = Math.Clamp(age / 0.3f, 0f, 1f);
        var fade = Math.Clamp((EliminationSeconds - age) / 0.5f, 0f, 1f);
        var y = height - (64f * u);
        var w = (520f * u) * (1f - MathF.Pow(1f - grow, 3f));
        shapes.Add(UiShapeDraw.Shadow(new Vector2(width * 0.5f, y), new Vector2(w, 58f * u), 4f * u, new Vector4(0f, 0f, 0f, 0.5f * fade), 12f * u, 3f * u));
        shapes.Add(UiShapeDraw.VerticalGradient(new Vector2(width * 0.5f, y), new Vector2(w, 58f * u), 4f * u, new Vector4(0.62f, 0.05f, 0.05f, 0.94f * fade), new Vector4(0.32f, 0.02f, 0.02f, 0.94f * fade)));
        if (grow < 0.6f)
        {
            return;
        }

        var by = e.By is { } killer ? $"  ／  {game.LabelOf(killer)} が倒した" : "";
        text.Add(new ScreenTextDraw($"脱落  {game.LabelOf(e.Fighter)}", new Vector2(width * 0.5f, y - (25f * u)), 24f * u, Ink with { W = fade }, TextAnchor.Center));
        text.Add(new ScreenTextDraw($"死因  {RoyaleVideo.Cause(e.Cause)}{by}", new Vector2(width * 0.5f, y + (6f * u)), 13f * u, new Vector4(1f, 0.85f, 0.8f, fade), TextAnchor.Center));
    }

    /// <summary>決着の札: 勝者（チーム戦はチームと生き残り）、引き分けなら生き残った人。</summary>
    private void Winner(BoxingGame game, List<ScreenTextDraw> text, List<UiShapeDraw> shapes)
    {
        var u = U;
        var alive = game.AllFighters.Where(game.InPlay).Select(game.LabelOf).ToList();
        var (head, sub) = game.WinningTeam is { } team
            ? (game.InitialCorners.Teams is null ? $"勝者  {string.Join("・", alive)}" : $"勝者  {TeamName(team)}", game.InitialCorners.Teams is null ? "最後の 1 人" : $"{string.Join("・", alive)} が生き残った")
            : ("引き分け", $"{string.Join("・", alive)} が生き残った");
        var y = height - (70f * u);
        shapes.Add(UiShapeDraw.Shadow(new Vector2(width * 0.5f, y), new Vector2(560f * u, 70f * u), 6f * u, new Vector4(0f, 0f, 0f, 0.5f), 14f * u, 3f * u));
        shapes.Add(UiShapeDraw.VerticalGradient(new Vector2(width * 0.5f, y), new Vector2(560f * u, 70f * u), 6f * u, new Vector4(0.16f, 0.13f, 0.06f, 0.94f), new Vector4(0.06f, 0.05f, 0.02f, 0.94f)));
        shapes.Add(UiShapeDraw.Outline(new Vector2(width * 0.5f, y), new Vector2(560f * u, 70f * u), 6f * u, Gold, 1.5f * u));
        text.Add(new ScreenTextDraw(head, new Vector2(width * 0.5f, y - (30f * u)), 28f * u, Gold, TextAnchor.Center));
        text.Add(new ScreenTextDraw(sub, new Vector2(width * 0.5f, y + (8f * u)), 14f * u, Ink, TextAnchor.Center));
    }

    private string TeamName(int team) => teamNames is { } names && team < names.Count ? names[team] : $"チーム {team + 1}";

    private static readonly Vector4[] TeamColors = [new(0.95f, 0.42f, 0.12f, 1f), new(0.2f, 0.72f, 0.95f, 1f), new(0.55f, 0.85f, 0.3f, 1f), new(0.8f, 0.4f, 0.9f, 1f)];

    private static Vector4 KitColor(BoxingGame game, int seat) => game.InitialCorners.Teams is not null
        ? TeamColors[game.InitialCorners.TeamOf(seat) % TeamColors.Length]
        : game.InitialCorners.RedKit(seat) ? RedKit : BlueKit;

    private static int Cells(Fighter fighter) => fighter.Voxels?.Parts.Sum(p => p.Body.Grid.FilledCount) ?? 1;

    private static string Clock(float seconds)
    {
        var s = Math.Max(0, (int)MathF.Ceiling(seconds));
        return string.Create(CultureInfo.InvariantCulture, $"{s / 60}:{s % 60:00}");
    }

    /// <summary>体の残りの細い棒（緑 → 黄 → 赤）。<paramref name="left"/> は棒の左端の中心の高さ。</summary>
    private static void Bar(List<UiShapeDraw> shapes, Vector2 left, float w, float h, float value, float alpha = 1f)
    {
        shapes.Add(UiShapeDraw.Rounded(left + new Vector2(w * 0.5f, 0f), new Vector2(w, h), h * 0.5f, new Vector4(1f, 1f, 1f, 0.16f * alpha)));
        var color = value > 0.6f ? Vector4.Lerp(new Vector4(0.95f, 0.85f, 0.2f, 1f), new Vector4(0.35f, 0.85f, 0.35f, 1f), (value - 0.6f) / 0.4f)
            : Vector4.Lerp(new Vector4(0.9f, 0.18f, 0.12f, 1f), new Vector4(0.95f, 0.85f, 0.2f, 1f), value / 0.6f);
        var fill = MathF.Max(h, w * value);
        shapes.Add(UiShapeDraw.Rounded(left + new Vector2(fill * 0.5f, 0f), new Vector2(fill, h), h * 0.5f, color with { W = alpha }));
    }

    /// <summary>文字に合わせた丸い札（<paramref name="anchor"/> の点が札の左端・中央・右端）。</summary>
    private void Pill(List<ScreenTextDraw> text, List<UiShapeDraw> shapes, string label, Vector2 at, float size, Vector4 fill, Vector4 ink, TextAnchor anchor)
    {
        var u = U;
        var w = ScreenText.MeasurePixelWidth(label, size) + (16f * u);
        var h = size + (10f * u);
        var cx = anchor switch { TextAnchor.Left => at.X + (w * 0.5f), TextAnchor.Right => at.X - (w * 0.5f), _ => at.X };
        shapes.Add(UiShapeDraw.Rounded(new Vector2(cx, at.Y), new Vector2(w, h), h * 0.5f, fill));
        text.Add(new ScreenTextDraw(label, new Vector2(cx, at.Y - (size * 0.62f)), size, ink, TextAnchor.Center));
    }

    /// <summary>右端を <paramref name="right"/> に揃えた小さな札（状態: 死亡・ダウン）。</summary>
    private void Chip(List<ScreenTextDraw> text, List<UiShapeDraw> shapes, string label, Vector2 right, float size, Vector4 fill, Vector4 ink, float shiftLeft = 0f)
    {
        var u = U;
        var w = ScreenText.MeasurePixelWidth(label, size) + (10f * u);
        var h = size + (6f * u);
        var center = new Vector2(right.X - shiftLeft - (w * 0.5f), right.Y);
        shapes.Add(UiShapeDraw.Rounded(center, new Vector2(w, h), 3f * u, fill));
        text.Add(new ScreenTextDraw(label, new Vector2(center.X, center.Y - (size * 0.62f)), size, ink, TextAnchor.Center));
    }
}
