using System.Globalization;
using System.Numerics;
using Aigis.Render;

namespace MiniRing.Arena.Shows.Wvc;

/// <summary>ラウンド 1 つぶんの集計（採点の札の入力）。ダメージは判定と同じ量（<c>Condition.TotalDamage</c> のラウンド内の差分）。</summary>
/// <param name="RedDamage">赤がそのラウンドで与えたダメージ。</param>
/// <param name="BlueDamage">青がそのラウンドで与えたダメージ。</param>
/// <param name="RedHits">赤がそのラウンドで当てた打撃の数。</param>
/// <param name="BlueHits">青がそのラウンドで当てた打撃の数。</param>
internal readonly record struct WvcRoundTally(float RedDamage, float BlueDamage, int RedHits, int BlueHits);

/// <summary>
/// WVC（格式の高いボクシング団体の地上波中継）にだけある番組の部品。どれも「入力 → 2D の重ね絵（文字と図形の並び）」の純粋な関数で、時刻 t [s] で動く。
/// 描画だけで試合には響かない。色は団体のファイル（金・白・濃紺）から、紋章と罫の飾りは既存の <see cref="WvcEmblem"/>・<see cref="TvShowGraphics"/> を呼ぶ。
/// 板を持つ部品（コール・採点の札）は、返す図形の並びの先頭から 2 つめまでが板（影と本体）。文字は板の中に収まる（長い名前は字を縮める）。
/// </summary>
internal static class WvcShowParts
{
    /// <summary>リングアナウンサーのコールの長さ [s]。</summary>
    public const float CallSeconds = 6f;

    /// <summary>ラウンドの採点の札の長さ [s]。</summary>
    public const float RoundCardSeconds = 7f;

    /// <summary>採点の札に出す行数の上限（それより多いラウンドは古い方を省く）。</summary>
    public const int MaxRows = 12;

    /// <summary>ベルトの全体の幅（マス）。</summary>
    public const int BeltColumns = 66;

    /// <summary>ベルトの全体の高さ（マス）。</summary>
    public const int BeltRows = 21;

    private const float FadeOut = 0.5f;

    private static readonly Vector4 RedDeep = new(0.62f, 0.06f, 0.07f, 0.94f);
    private static readonly Vector4 RedBright = new(0.92f, 0.2f, 0.16f, 0.94f);
    private static readonly Vector4 BlueDeep = new(0.05f, 0.14f, 0.52f, 0.94f);
    private static readonly Vector4 BlueBright = new(0.2f, 0.42f, 0.95f, 0.94f);

    private static readonly WvcEmblem.Palette BeltGold = new(new Vector3(1f, 0.9f, 0.45f), new Vector3(0.93f, 0.62f, 0.1f), new Vector3(0.45f, 0.26f, 0.04f));
    private static readonly WvcEmblem.Palette BeltDeepGold = new(new Vector3(0.82f, 0.58f, 0.14f), new Vector3(0.58f, 0.36f, 0.06f), new Vector3(0.3f, 0.17f, 0.03f));
    private static readonly WvcEmblem.Palette BeltLeather = new(new Vector3(0.16f, 0.07f, 0.06f), new Vector3(0.08f, 0.03f, 0.03f), new Vector3(0.03f, 0.01f, 0.01f));
    private static readonly WvcEmblem.Palette BeltJewel = new(new Vector3(1f, 0.35f, 0.3f), new Vector3(0.62f, 0.04f, 0.03f), new Vector3(0.3f, 0.01f, 0.01f));

    // ---------------------------------------------------------------------------------------------------------------------------
    // 1. リングアナウンサーのコール
    // ---------------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// リングアナウンサーのコールの板（画面の下 1/3、生の絵の上に重ねる。<see cref="CallSeconds"/> 秒）。上から「赤（青）コーナー」・肩書き・戦績・体重・本名・呼び名が
    /// 0.4 秒ほどずつ順に出て、最後の 0.5 秒で消える。入力は <paramref name="corner"/>（<see cref="TvCorner.Profile"/> があれば戦績・体重・拠点を出す）。
    /// </summary>
    /// <param name="corner">コーナーの紹介。</param>
    /// <param name="side">1 = 赤コーナー、−1 = 青コーナー。</param>
    /// <param name="t">コールの始まりからの時刻 [s]（0〜<see cref="CallSeconds"/>。外は何も出さない）。</param>
    /// <param name="width">画面の幅 [px]。</param>
    /// <param name="height">画面の高さ [px]。</param>
    /// <param name="org">団体（null は WVC）。</param>
    public static (IReadOnlyList<ScreenTextDraw> Text, IReadOnlyList<UiShapeDraw> Shapes) RingAnnouncerCall(TvCorner corner, int side, float t, int width, int height, Organization? org = null)
    {
        ArgumentNullException.ThrowIfNull(corner);
        var o = org ?? Organization.Default;
        var text = new List<ScreenTextDraw>();
        var shapes = new List<UiShapeDraw>();
        var k = height / 720f;
        var fade = Fade(t, CallSeconds);
        if (fade <= 0f)
        {
            return (text, shapes);
        }

        var red = side >= 0;
        var accent = red ? RedBright : BlueBright;
        var deep = red ? RedDeep : BlueDeep;
        var gold = o.Colors.Accent;
        var white = o.Colors.Text;
        var muted = o.Colors.Muted;
        var ink = o.Colors.Ink;

        // 板: 画面の下 1/3 の中ほど。下から立ち上がり、金の縁を持つ濃紺の板。
        var panelW = MathF.Min(width * 0.64f, 860f * k);
        var panelH = 268f * k;
        var rise = EaseOut(Clamp01(t / 0.45f));
        var cx = width * 0.5f;
        var cy = height - (panelH * 0.5f) - (34f * k) + ((1f - rise) * 40f * k);
        var top = cy - (panelH * 0.5f);
        shapes.Add(UiShapeDraw.Shadow(new Vector2(cx, cy), new Vector2(panelW, panelH), 10f * k, new Vector4(0f, 0f, 0f, 0.5f * fade), 18f * k, 6f * k));
        shapes.Add(new UiShapeDraw(new Vector2(cx, cy), new Vector2(panelW, panelH), ink with { W = 0.93f * fade }, UiShapeDraw.Uniform(10f * k), gold with { W = 0.85f * fade }, 2f * k,
            FillColorEnd: new Vector4(0.02f, 0.03f, 0.1f, 0.93f * fade), GradientAxis: Vector2.UnitY));

        // コーナーの色の帯（板の左右の縁の内側）。
        foreach (var dx in new[] { -1f, 1f })
        {
            shapes.Add(new UiShapeDraw(new Vector2(cx + (dx * ((panelW * 0.5f) - (12f * k))), cy), new Vector2(6f * k, panelH - (28f * k)), accent with { W = 0.95f * fade }, UiShapeDraw.Uniform(2f * k),
                FillColorEnd: deep with { W = 0.95f * fade }, GradientAxis: Vector2.UnitY));
        }

        var inner = panelW - (72f * k);
        float Reveal(float at) => Clamp01((t - at) / 0.3f) * fade;

        // 「赤コーナー」の札と両脇の罫。
        var label = red ? TvLocale.T("赤コーナー", "RED CORNER") : TvLocale.T("青コーナー", "BLUE CORNER");
        var a0 = Reveal(0.45f);
        var chipW = (ScreenText.MeasurePixelWidth(label, 24f * k) + (36f * k)) * EaseOut(Clamp01((t - 0.45f) / 0.3f));
        shapes.Add(UiShapeDraw.Rounded(new Vector2(cx, top + (26f * k)), new Vector2(chipW, 30f * k), 6f * k, accent with { W = 0.96f * a0 }));
        if (a0 > 0.2f)
        {
            text.Add(new ScreenTextDraw(label, new Vector2(cx, top + (12f * k)), 24f * k, white with { W = a0 }, TextAnchor.Center));
        }

        var ruleLength = (inner * 0.5f) - (chipW * 0.5f) - (20f * k);
        if (ruleLength > 10f)
        {
            foreach (var dx in new[] { -1f, 1f })
            {
                shapes.Add(new UiShapeDraw(new Vector2(cx + (dx * ((chipW * 0.5f) + (14f * k) + (ruleLength * 0.5f))), top + (26f * k)), new Vector2(ruleLength, 2f * k), gold with { W = 0.7f * a0 }));
            }
        }

        // 肩書き（流派・拠点）、戦績、体重。
        var title = Title(corner);
        var a1 = Reveal(0.85f);
        if (a1 > 0f && title.Length > 0)
        {
            text.Add(FitCentered(title, cx, top + (54f * k), 24f * k, inner, muted with { W = a1 }));
        }

        var a2 = Reveal(1.25f);
        var record = Record(corner);
        if (a2 > 0f && record.Length > 0)
        {
            text.Add(FitCentered(record, cx, top + (86f * k), 30f * k, inner, white with { W = a2 }));
        }

        var a3 = Reveal(1.65f);
        var weight = corner.Profile is { WeightKg: > 0 } p ? string.Create(CultureInfo.InvariantCulture, $"{TvLocale.T("体重　", "WEIGHT  ")}{p.WeightKg} kg") : "";
        if (a3 > 0f && weight.Length > 0)
        {
            text.Add(FitCentered(weight, cx, top + (122f * k), 26f * k, inner, gold with { W = a3 }));
        }

        // 金の罫と菱形。
        var a4 = Reveal(2.05f);
        if (a4 > 0f)
        {
            shapes.Add(new UiShapeDraw(new Vector2(cx, top + (166f * k)), new Vector2(inner * 0.86f, 2f * k), gold with { W = 0.75f * a4 }));
            new TvShowGraphics(width, height, o).RuleGems(shapes, new Vector2(cx, top + (166f * k)), inner * 0.43f, 2.6f * k, a4);
        }

        // 本名（大きく）と呼び名。本名が無ければ呼び名を大きく。
        var a5 = Reveal(2.4f);
        var a6 = Reveal(2.95f);
        if (corner.FullName.Length > 0)
        {
            if (a5 > 0f)
            {
                text.Add(FitCentered(corner.FullName, cx, top + (178f * k), 40f * k, inner, white with { W = a5 }));
            }

            if (a6 > 0f && corner.Name.Length > 0 && !corner.FullName.Contains(corner.Name, StringComparison.Ordinal))
            {
                text.Add(FitCentered($"“{corner.Name}”", cx, top + (226f * k), 26f * k, inner, gold with { W = a6 }));
            }
        }
        else if (a5 > 0f)
        {
            text.Add(FitCentered(corner.Name, cx, top + (190f * k), 48f * k, inner, gold with { W = a5 }));
        }

        return (text, shapes);
    }

    /// <summary>肩書き: 流派（<see cref="TvCorner.Discipline"/>）に、資料があれば拠点（<c>Profile.Hometown</c>）を添える。</summary>
    internal static string Title(TvCorner corner)
    {
        var hometown = corner.Profile?.Hometown ?? "";
        var discipline = corner.Discipline ?? "";
        return (discipline.Length, hometown.Length) switch
        {
            (0, 0) => "",
            (_, 0) => discipline,
            (0, _) => hometown,
            _ => $"{discipline}　・　{hometown}",
        };
    }

    /// <summary>戦績の言い方（WVC: 「17勝 12K.O. 2敗 1分」。引き分けが 0 なら出さない）。資料が無ければ空。</summary>
    internal static string Record(TvCorner corner)
    {
        if (corner.Profile is not { } p)
        {
            return "";
        }

        var r = p.FightRecord;
        if (TvLocale.IsEnglish)
        {
            return TvLocale.Record(r);
        }

        var s = string.Create(CultureInfo.InvariantCulture, $"{r.Wins}勝 {r.Kos}K.O. {r.Losses}敗");
        return r.Draws > 0 ? string.Create(CultureInfo.InvariantCulture, $"{s} {r.Draws}分") : s;
    }

    // ---------------------------------------------------------------------------------------------------------------------------
    // 2. ラウンドの採点の札
    // ---------------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// ラウンドの採点の札（休みの最初の 6〜8 秒。画面の右下。<see cref="RoundCardSeconds"/> 秒）。ラウンドごとに「与えたダメージ」（判定と同じ量）と、当てた打撃の数を、
    /// 採点表に似せた罫の表で出す（量は 1 つ。ジャッジごとの違う点は作らない）。最新のラウンドの行は金で強める。行は 0.25 秒ずつ順に出る。
    /// <paramref name="rounds"/> が空なら何も出さない。表は新しい方から最大 <see cref="MaxRows"/> 行（それより多いラウンドは古い方を省く。合計は全ラウンド）。
    /// <paramref name="right"/>・<paramref name="bottom"/> は札の右の縁・下の縁の位置 [px]（省略すると画面の右下の隅。顔の窓などを避けて置くとき）。
    /// </summary>
    public static (IReadOnlyList<ScreenTextDraw> Text, IReadOnlyList<UiShapeDraw> Shapes) RoundScoreCard(
        IReadOnlyList<WvcRoundTally> rounds, string redName, string blueName, float t, int width, int height, Organization? org = null,
        float? right = null, float? bottom = null)
    {
        ArgumentNullException.ThrowIfNull(rounds);
        var o = org ?? Organization.Default;
        var text = new List<ScreenTextDraw>();
        var shapes = new List<UiShapeDraw>();
        var fade = Fade(t, RoundCardSeconds);
        if (rounds.Count == 0 || fade <= 0f)
        {
            return (text, shapes);
        }

        var k = height / 720f;
        var gold = o.Colors.Accent;
        var white = o.Colors.Text;
        var muted = o.Colors.Muted;
        var ink = o.Colors.Ink;

        var first = Math.Max(0, rounds.Count - MaxRows);
        var shown = rounds.Count - first;
        var rowH = 30f * k;
        var headH = 76f * k;
        var footH = 44f * k;
        var panelW = 420f * k;
        var panelH = headH + (rowH * shown) + footH;
        var slide = EaseOut(Clamp01(t / 0.5f));
        var rightEdge = (right ?? (width - (28f * k))) + ((1f - slide) * 80f * k);
        var left = rightEdge - panelW;
        var top = (bottom ?? (height - (34f * k))) - panelH;
        var cx = left + (panelW * 0.5f);
        var cy = top + (panelH * 0.5f);
        shapes.Add(UiShapeDraw.Shadow(new Vector2(cx, cy), new Vector2(panelW, panelH), 8f * k, new Vector4(0f, 0f, 0f, 0.5f * fade), 16f * k, 5f * k));
        shapes.Add(new UiShapeDraw(new Vector2(cx, cy), new Vector2(panelW, panelH), ink with { W = 0.93f * fade }, UiShapeDraw.Uniform(8f * k), gold with { W = 0.85f * fade }, 2f * k,
            FillColorEnd: new Vector4(0.02f, 0.03f, 0.1f, 0.93f * fade), GradientAxis: Vector2.UnitY));

        // 見出し: ラウンド終了の札と、赤・青の名札（列）。
        text.Add(FitLeft(TvLocale.IsEnglish ? string.Create(CultureInfo.InvariantCulture, $"ROUND {rounds.Count} COMPLETE  ·  DAMAGE DEALT") : string.Create(CultureInfo.InvariantCulture, $"第{rounds.Count}ラウンド終了　与えたダメージ"), left + (16f * k), top + (8f * k), 22f * k, panelW - (32f * k), gold with { W = fade }));
        var labelW = 56f * k;
        var colW = (panelW - labelW - (16f * k)) * 0.5f;
        var redX = left + labelW + (colW * 0.5f);
        var blueX = redX + colW;
        var nameTop = top + (38f * k);
        foreach (var (x, name, color) in new[] { (redX, redName, RedBright), (blueX, blueName, BlueBright) })
        {
            shapes.Add(UiShapeDraw.Rounded(new Vector2(x, nameTop + (15f * k)), new Vector2(colW - (8f * k), 28f * k), 5f * k, color with { W = 0.9f * fade }));
            text.Add(FitCentered(name ?? "", x, nameTop + (3f * k), 20f * k, colW - (24f * k), white with { W = fade }));
        }

        shapes.Add(new UiShapeDraw(new Vector2(cx, top + headH - (2f * k)), new Vector2(panelW - (24f * k), 2f * k), gold with { W = 0.7f * fade }));

        // 行: R 番号・与えたダメージ（大）・当てた数（小）。
        float redTotal = 0f, blueTotal = 0f;
        var redHits = 0;
        var blueHits = 0;
        foreach (var r in rounds)
        {
            redTotal += r.RedDamage;
            blueTotal += r.BlueDamage;
            redHits += r.RedHits;
            blueHits += r.BlueHits;
        }

        for (var i = 0; i < shown; i++)
        {
            var r = rounds[first + i];
            var a = Clamp01((t - 0.5f - (0.25f * i)) / 0.25f) * fade;
            var y = top + headH + (rowH * i);
            var latest = first + i == rounds.Count - 1;
            if (a <= 0f)
            {
                continue;
            }

            if (latest)
            {
                shapes.Add(UiShapeDraw.Rounded(new Vector2(cx, y + (rowH * 0.5f)), new Vector2(panelW - (16f * k), rowH - (2f * k)), 4f * k, gold with { W = 0.16f * a }));
            }

            var c = latest ? gold : white;
            text.Add(new ScreenTextDraw(string.Create(CultureInfo.InvariantCulture, $"R{first + i + 1}"), new Vector2(left + (16f * k), y + (3f * k)), 22f * k, (latest ? gold : muted) with { W = a }));
            DamageCell(text, r.RedDamage, r.RedHits, redX, y, k, colW, c with { W = a }, muted with { W = a });
            DamageCell(text, r.BlueDamage, r.BlueHits, blueX, y, k, colW, c with { W = a }, muted with { W = a });
        }

        // 合計。
        var total = Clamp01((t - 0.5f - (0.25f * shown)) / 0.3f) * fade;
        var fy = top + headH + (rowH * shown);
        shapes.Add(new UiShapeDraw(new Vector2(cx, fy + (2f * k)), new Vector2(panelW - (24f * k), 2f * k), gold with { W = 0.7f * fade }));
        if (total > 0f)
        {
            text.Add(new ScreenTextDraw(TvLocale.T("計", "TOTAL"), new Vector2(left + (16f * k), fy + (11f * k)), 22f * k, gold with { W = total }));
            DamageCell(text, redTotal, redHits, redX, fy + (8f * k), k, colW, gold with { W = total }, muted with { W = total });
            DamageCell(text, blueTotal, blueHits, blueX, fy + (8f * k), k, colW, gold with { W = total }, muted with { W = total });
        }

        return (text, shapes);
    }

    private static void DamageCell(List<ScreenTextDraw> text, float damage, int hits, float x, float y, float k, float colW, Vector4 main, Vector4 sub)
    {
        var damageText = string.Create(CultureInfo.InvariantCulture, $"{damage:0}");
        var hitText = TvLocale.IsEnglish ? string.Create(CultureInfo.InvariantCulture, $"({hits} hits)") : string.Create(CultureInfo.InvariantCulture, $"（{hits}発）");
        var wDamage = ScreenText.MeasurePixelWidth(damageText, 24f * k);
        var wHit = ScreenText.MeasurePixelWidth(hitText, 16f * k);
        var gap = 6f * k;
        var half = (colW * 0.5f) - (8f * k); // ダメージは列の中心の左（右揃え）、当てた数は右（左揃え）。桁がはみ出すなら縮める
        var sd = wDamage > half ? half / wDamage : 1f;
        var sh = wHit > half - gap ? (half - gap) / wHit : 1f;
        text.Add(new ScreenTextDraw(damageText, new Vector2(x, y + (2f * k)), 24f * k * sd, main, TextAnchor.Right));
        text.Add(new ScreenTextDraw(hitText, new Vector2(x + gap, y + (8f * k)), 16f * k * sh, sub));
    }

    // ---------------------------------------------------------------------------------------------------------------------------
    // 3. 王座のベルト
    // ---------------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// 王座のベルト: ボクセルで積んだ金のチャンピオンベルト（黒い革の帯に金の側板、中央の大きな金の板に WVC の紋章）。決着の画面に重ねる、勝者の名前の上の飾り。
    /// 出すかどうか（<paramref name="show"/>）は呼ぶ側が決める（部品は判断しない）。出ると上から降りてきて、そのあとも光の帯が板の上を通り続ける。
    /// </summary>
    /// <param name="show">出すか（false なら何も返さない）。</param>
    /// <param name="t">ベルトが出てからの時刻 [s]。</param>
    /// <param name="center">ベルトの中心 [px]。</param>
    /// <param name="cell">1 マスの大きさ [px]。全体の幅は <see cref="BeltColumns"/> × cell、高さは <see cref="BeltRows"/> × cell。</param>
    public static (IReadOnlyList<ScreenTextDraw> Text, IReadOnlyList<UiShapeDraw> Shapes) ChampionBelt(bool show, float t, Vector2 center, float cell)
    {
        var text = new List<ScreenTextDraw>();
        var shapes = new List<UiShapeDraw>();
        if (!show || cell <= 0f || !float.IsFinite(cell) || !float.IsFinite(t))
        {
            return (text, shapes);
        }

        var alpha = Clamp01(t / 0.5f);
        if (alpha <= 0f)
        {
            return (text, shapes);
        }

        var drop = (1f - EaseOut(Clamp01(t / 0.7f))) * -(BeltRows * cell * 0.6f);
        var origin = center + new Vector2(-BeltColumns * cell * 0.5f, (-BeltRows * cell * 0.5f) + drop);
        var strap = new List<Vector2>();
        var rims = new List<Vector2>();
        var gold = new List<Vector2>();
        var deepGold = new List<Vector2>();
        var jewels = new List<Vector2>();
        Vector2 At(int col, int row) => origin + new Vector2((col + 0.5f) * cell, (row + 0.5f) * cell);
        const int MidRow = BeltRows / 2;
        const int MidCol = BeltColumns / 2;

        // 革の帯: 真ん中の 7 段。上下の縁は濃い金の線。
        for (var col = 0; col < BeltColumns; col++)
        {
            for (var row = MidRow - 3; row <= MidRow + 3; row++)
            {
                (row == MidRow - 3 || row == MidRow + 3 ? rims : strap).Add(At(col, row));
            }
        }

        // 側板: 左右に 2 枚ずつ（小さな金の板、真ん中に赤い宝石）。
        foreach (var sign in new[] { -1, 1 })
        {
            foreach (var offset in new[] { 17, 27 })
            {
                var c = MidCol + (sign * offset);
                for (var dy = -4; dy <= 4; dy++)
                {
                    var span = 4 - (Math.Abs(dy) / 2);
                    for (var dx = -span; dx <= span; dx++)
                    {
                        (Math.Abs(dx) + Math.Abs(dy) <= 1 ? jewels : gold).Add(At(c + dx, MidRow + dy));
                    }
                }
            }
        }

        // 中央の大きな板: 楕円（半径 横 12・縦 10 マス）の金に、細い濃い金の輪。
        const int PlateRx = 12;
        const int PlateRy = 10;
        for (var dy = -PlateRy; dy <= PlateRy; dy++)
        {
            for (var dx = -PlateRx; dx <= PlateRx; dx++)
            {
                var e = (dx * dx / (float)(PlateRx * PlateRx)) + (dy * dy / (float)(PlateRy * PlateRy));
                if (e > 1.02f)
                {
                    continue;
                }

                (e is > 0.64f and <= 0.8f ? deepGold : gold).Add(At(MidCol + dx, MidRow + dy));
            }
        }

        // 板の上下の宝石。
        jewels.Add(At(MidCol, MidRow - PlateRy + 1));
        jewels.Add(At(MidCol, MidRow + PlateRy - 1));

        // 光の帯は左から右へ、2.5 秒ごとに 1 回通る。
        var sweep = (t / 2.5f) % 1f;
        var shine = origin.X + ((-0.2f + (1.4f * sweep)) * BeltColumns * cell);
        WvcEmblem.Voxels(shapes, strap, cell, BeltLeather, alpha, float.NaN);
        WvcEmblem.Voxels(shapes, rims, cell, BeltDeepGold, alpha, shine);
        WvcEmblem.Voxels(shapes, gold, cell, BeltGold, alpha, shine);
        WvcEmblem.Voxels(shapes, deepGold, cell, BeltDeepGold, alpha, shine);
        WvcEmblem.Voxels(shapes, jewels, cell, BeltJewel, alpha, float.NaN);

        // 板の真ん中に WVC の紋章（板の内側の幅 15 マスぶん）。
        WvcEmblem.Draw(text, shapes, At(MidCol, MidRow), cell * 15f / WvcEmblem.Width, alpha, float.NaN);
        return (text, shapes);
    }

    // ---------------------------------------------------------------------------------------------------------------------------
    // 補助
    // ---------------------------------------------------------------------------------------------------------------------------

    /// <summary>終わりの 0.5 秒で 1 → 0 になる係数。範囲の外（t &lt; 0・<paramref name="length"/> 超え・非数）は 0。</summary>
    private static float Fade(float t, float length) => !float.IsFinite(t) || t < 0f || t > length ? 0f : Clamp01((length - t) / FadeOut);

    /// <summary>中央揃えの文字。幅が <paramref name="maxWidth"/> を超えたら、収まる大きさまで縮める。</summary>
    private static ScreenTextDraw FitCentered(string value, float centerX, float top, float size, float maxWidth, Vector4 color)
    {
        var w = ScreenText.MeasurePixelWidth(value, size);
        var fitted = w > maxWidth && w > 0f ? size * (maxWidth / w) : size;
        return new ScreenTextDraw(value, new Vector2(centerX, top), fitted, color, TextAnchor.Center);
    }

    /// <summary>左揃えの文字。幅が <paramref name="maxWidth"/> を超えたら縮める。</summary>
    private static ScreenTextDraw FitLeft(string value, float left, float top, float size, float maxWidth, Vector4 color)
    {
        var w = ScreenText.MeasurePixelWidth(value, size);
        var fitted = w > maxWidth && w > 0f ? size * (maxWidth / w) : size;
        return new ScreenTextDraw(value, new Vector2(left, top), fitted, color);
    }

    private static float Clamp01(float x) => Math.Clamp(x, 0f, 1f);

    private static float EaseOut(float t) => 1f - MathF.Pow(1f - t, 3f);
}
