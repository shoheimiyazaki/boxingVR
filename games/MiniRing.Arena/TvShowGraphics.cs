using System.Globalization;
using System.Numerics;
using Aigis.Render;
using MiniRing.Weapons;

namespace MiniRing.Arena;

/// <summary>テレビ中継の 1 人の紹介（リングネーム・流派の見出し・その下の説明）。</summary>
/// <param name="Name">リングネーム。</param>
/// <param name="Discipline">流派・武器の見出し（「ボクシング」「空手」「日本刀」）。</param>
/// <param name="Detail">見出しの下の説明（スタイル・手にはめる物）。</param>
/// <param name="FullName">紹介の板の上の行に出す本名（名簿のキャラクター）。空なら出さない。</param>
internal sealed record TvCorner(string Name, string Discipline, string Detail, string FullName = "")
{
    /// <summary>名簿のキャラクターの紹介: 試合の名前・武器（はめる物）・肩書きと出身・本名。</summary>
    public static TvCorner Of(MiniRing.Characters.FighterCharacter character)
    {
        ArgumentNullException.ThrowIfNull(character);
        // 流派の見出しはスタイル・はめる物・武器から（「グローブ」でなく「ボクシング」）。下の行は肩書きと出身。
        var discipline = Of(character.HudName, character.StyleName, character.Wear, character.Weapon).Discipline;
        return new TvCorner(character.HudName, discipline, $"{character.Tagline}・{character.Origin}", character.Name);
    }

    /// <summary>手にはめる物・握る武器・スタイルから、紹介の見出しを作る。</summary>
    public static TvCorner Of(string ringName, string styleName, HandWear wear, WeaponKind? weapon)
    {
        var (discipline, detail) = weapon switch
        {
            WeaponKind.Katana => ("日本刀", "両手持ち・刀の型"),
            WeaponKind.Longsword => ("ロングソード", "両手持ち・西洋剣の型"),
            WeaponKind.Staff => ("棒術", "両手持ち・六尺棒の型"),
            WeaponKind.WoodenClub => ("こんぼう", $"片手持ち・{StyleLabel(styleName)}"),
            WeaponKind.IronSword when wear == HandWear.Shield => ("剣と盾", "右手に剣・左手に盾"),
            WeaponKind.IronSword => ("鉄の剣", $"片手持ち・{StyleLabel(styleName)}"),
            _ => wear switch
            {
                HandWear.None when styleName == "karate" => ("空手", "素手・正拳と蹴り"),
                HandWear.None => ("ベアナックル", StyleLabel(styleName)),
                HandWear.KnuckleDuster => ("メリケンサック", StyleLabel(styleName)),
                HandWear.Katar => ("カタール", StyleLabel(styleName)),
                HandWear.Gauntlet => ("籠手", StyleLabel(styleName)),
                HandWear.Tonfa => ("トンファー", "両手に木の棒・前腕で受ける"),
                HandWear.Shield => ("盾", "盾で受ける"),
                _ => ("ボクシング", StyleLabel(styleName)),
            },
        };
        return new TvCorner(ringName, discipline, detail);
    }

    private static string StyleLabel(string styleName) => styleName switch
    {
        "champion" => "王者型・読みとカウンター",
        "swarmer" => "インファイター",
        "slugger" => "強打者",
        "outboxer" => "アウトボクサー",
        "counter" => "カウンター使い",
        "jabber" => "ジャブ使い",
        "karate" => "空手家",
        _ => styleName,
    };
}

/// <summary>テレビ中継の試合の札（大会名・何試合目か・決まり）。</summary>
internal sealed record TvCard(string EventTitle, string Bout, TvCorner Red, TvCorner Blue, MatchRules Rules);

/// <summary>試合の結果（勝敗画面に出すもの）。</summary>
/// <param name="Winner">1 = 赤、−1 = 青、0 = 引き分け。</param>
/// <param name="Finish">ゲームの決着の文（"K.O. — BRAIN DESTROYED"・"10 COUNT K.O."・"... WIN BY DECISION"・"DRAW"）。</param>
/// <param name="Round">決着したラウンド。</param>
/// <param name="RoundTime">決着したラウンドの経過時間 [s]。</param>
/// <param name="RedScores">赤がラウンドごとに与えたダメージ（判定の得点）。</param>
/// <param name="BlueScores">青がラウンドごとに与えたダメージ。</param>
internal sealed record TvResult(int Winner, string? Finish, int Round, float RoundTime, IReadOnlyList<float> RedScores, IReadOnlyList<float> BlueScores)
{
    /// <summary>判定で決まった（K.O. でない）。</summary>
    public bool ByDecision => Finish is null || Finish.Contains("DECISION", StringComparison.Ordinal) || Finish == "DRAW";

    /// <summary>決まり手の大きな文字（K.O. / 判定 / 引き分け）。</summary>
    public string Method => Winner == 0 ? "引き分け" : ByDecision ? "判定" : Finish?.StartsWith("RING OUT", StringComparison.Ordinal) == true ? "リングアウト" : "K.O.";

    /// <summary>決まり手の説明（「脳の破壊」「失神」「10 カウント」）。</summary>
    public string Cause => Finish?.Split("  ")[0] switch // ゲームの文の後ろの "  BLUE WINS" は落とす
    {
        null => "",
        "RING OUT" => "リングの外へ",
        "10 COUNT K.O." => "10 カウント",
        "K.O. — OUT COLD" => "失神",
        { } ko when ko.StartsWith("K.O. — ", StringComparison.Ordinal) => Organ(ko["K.O. — ".Length..].Replace(" DESTROYED", "", StringComparison.Ordinal)) + "の破壊",
        _ => "",
    };

    private static string Organ(string cause) => cause switch
    {
        "BRAIN" => "脳",
        "HEART" => "心臓",
        "LUNG" => "肺",
        "LIVER" => "肝臓",
        "GUT" => "内臓",
        _ => cause,
    };
}

/// <summary>
/// テレビ中継の冒頭のタイトルと最後の勝敗の画面（描画だけ。試合には響かない）。背景はリングの 3D の絵のまま、
/// 帯と文字を時間でスライド・拡大させて重ねる。文字は影を付けて背景の上でも読めるようにする。
/// </summary>
internal sealed class TvShowGraphics(int width, int height)
{
    /// <summary>冒頭のタイトルの長さ [s]。</summary>
    public const float OpeningSeconds = 7f;

    /// <summary>勝敗の画面の長さ [s]。</summary>
    public const float ResultSeconds = 9f;

    private static readonly Vector4 White = new(1f, 1f, 1f, 1f);
    private static readonly Vector4 Gold = new(1f, 0.82f, 0.28f, 1f);
    private static readonly Vector4 Grey = new(0.78f, 0.8f, 0.86f, 1f);
    private static readonly Vector4 RedDeep = new(0.62f, 0.06f, 0.07f, 0.94f);
    private static readonly Vector4 RedBright = new(0.92f, 0.2f, 0.16f, 0.94f);
    private static readonly Vector4 BlueDeep = new(0.05f, 0.14f, 0.52f, 0.94f);
    private static readonly Vector4 BlueBright = new(0.2f, 0.42f, 0.95f, 0.94f);
    private static readonly Vector4 Ink = new(0.04f, 0.05f, 0.08f, 0.9f);

    /// <summary>冒頭のタイトル: 大会名 → 両コーナーの紹介が左右から → VS → 決まりの帯。最後の 0.5 秒で消える。</summary>
    public (IReadOnlyList<ScreenTextDraw> Text, IReadOnlyList<UiShapeDraw> Shapes) Opening(TvCard card, float t)
    {
        ArgumentNullException.ThrowIfNull(card);
        var w = (float)width;
        var h = (float)height;
        var text = new List<ScreenTextDraw>();
        var shapes = new List<UiShapeDraw>();
        var fade = 1f - Clamp01((t - (OpeningSeconds - 0.5f)) / 0.5f);
        shapes.Add(new UiShapeDraw(new Vector2(w * 0.5f, h * 0.5f), new Vector2(w, h), new Vector4(0f, 0f, 0f, 0.5f * Clamp01(t / 0.4f) * fade)));

        // 大会名: 中央に出て、紹介が入る前に上へ上がる。
        var logoIn = EaseOut(Clamp01(t / 0.7f));
        var logoUp = EaseInOut(Clamp01((t - 1.1f) / 0.6f));
        var logoY = Lerp(h * 0.42f, h * 0.13f, logoUp);
        var logoSize = Lerp(1.25f, 0.8f, logoUp);
        var bandWidth = w * logoIn;
        shapes.Add(new UiShapeDraw(new Vector2(w * 0.5f, logoY + (6f * logoSize)), new Vector2(bandWidth, 176f * logoSize), Ink with { W = 0.88f * fade },
            FillColorEnd: Ink with { W = 0.15f * fade }, GradientAxis: Vector2.UnitY));
        // ボクセルを積んだ金の WVC、その下に字間を空けた団体の名前と、金の細い罫、大会名。
        VoxelWord(shapes, LogoText, new Vector2(w * 0.5f, logoY - (40f * logoSize)), 10f * logoSize, logoIn * fade);
        text.Add(new ScreenTextDraw(Spaced(OrganizationName.ToUpperInvariant()), new Vector2(w * 0.5f, logoY + (6f * logoSize)), 20f * logoSize, White with { W = 0.9f * logoIn * fade }, TextAnchor.Center));
        shapes.Add(new UiShapeDraw(new Vector2(w * 0.5f, logoY + (36f * logoSize)), new Vector2(bandWidth * 0.36f, 1.5f), Gold with { W = fade }));
        Shadowed(text, card.EventTitle, new Vector2(w * 0.5f, logoY + (44f * logoSize)), 30f * logoSize, Gold with { W = logoIn * fade }, TextAnchor.Center);

        // 何試合目か。
        var boutIn = Clamp01((t - 1.5f) / 0.4f);
        Shadowed(text, card.Bout, new Vector2(w * 0.5f, h * 0.28f), 30f, White with { W = boutIn * fade }, TextAnchor.Center);

        // 両コーナーの紹介の板: 左右の画面の外から滑り込む。
        var slide = EaseOut(Clamp01((t - 1.6f) / 0.7f));
        var panelSize = new Vector2(w * 0.4f, 210f);
        var panelY = h * 0.53f;
        var redX = Lerp(-panelSize.X * 0.5f, w * 0.25f, slide);
        var blueX = Lerp(w + (panelSize.X * 0.5f), w * 0.75f, slide);
        CornerPanel(text, shapes, card.Red, new Vector2(redX, panelY), panelSize, RedBright, RedDeep, "赤コーナー", fade);
        CornerPanel(text, shapes, card.Blue, new Vector2(blueX, panelY), panelSize, BlueBright, BlueDeep, "青コーナー", fade);

        // VS: 少し大きく出て落ち着く。
        var vs = Clamp01((t - 2.3f) / 0.45f);
        if (vs > 0f)
        {
            var pop = BackOut(vs);
            shapes.Add(UiShapeDraw.Rounded(new Vector2(w * 0.5f, panelY), new Vector2(124f, 124f) * pop, 62f * pop, Ink with { W = 0.92f * fade }) with
            {
                BorderColor = Gold with { W = fade },
                BorderPixels = 3f,
            });
            Shadowed(text, "VS", new Vector2(w * 0.5f, panelY - (34f * pop)), 64f * pop, Gold with { W = fade }, TextAnchor.Center);
        }

        // 決まりの帯（下）。
        var rules = Clamp01((t - 3f) / 0.5f);
        var ribbonY = h * 0.85f;
        shapes.Add(new UiShapeDraw(new Vector2(w * 0.5f, ribbonY + 14f), new Vector2(w * 0.86f * EaseOut(rules), 46f), Ink with { W = 0.85f * fade }, UiShapeDraw.Uniform(6f)));
        var rule = string.Create(CultureInfo.InvariantCulture,
            $"{card.Rules.RoundSeconds:0}秒 × {card.Rules.Rounds}ラウンド　・　K.O. か判定で決着");
        Shadowed(text, rule, new Vector2(w * 0.5f, ribbonY), 24f, Grey with { W = rules * fade }, TextAnchor.Center);
        return (text, shapes);
    }

    /// <summary>
    /// 勝敗の画面: 「試合終了」→ 決まり手（K.O. / 判定）が弾む → 勝者の帯が勝った側から → 決着の時刻 → ラウンドごとの採点表。
    /// </summary>
    public (IReadOnlyList<ScreenTextDraw> Text, IReadOnlyList<UiShapeDraw> Shapes) Result(TvCard card, TvResult result, float t)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(result);
        var w = (float)width;
        var h = (float)height;
        var text = new List<ScreenTextDraw>();
        var shapes = new List<UiShapeDraw>();
        shapes.Add(new UiShapeDraw(new Vector2(w * 0.5f, h * 0.5f), new Vector2(w, h), new Vector4(0f, 0f, 0f, 0.55f * Clamp01(t / 0.5f))));
        Logo(text, shapes, new Vector2(70f, 34f), 1f);

        // 試合終了の札。
        var tagIn = Clamp01(t / 0.4f);
        shapes.Add(UiShapeDraw.Rounded(new Vector2(w * 0.5f, h * 0.09f + 16f), new Vector2(200f * EaseOut(tagIn), 40f), 6f, Gold with { W = 0.95f * tagIn }));
        text.Add(new ScreenTextDraw("試合終了", new Vector2(w * 0.5f, h * 0.09f), 26f, Ink with { W = tagIn }, TextAnchor.Center));

        // 決まり手。
        var method = Clamp01((t - 0.4f) / 0.5f);
        if (method > 0f)
        {
            var pop = BackOut(method);
            var size = (result.Method.Length > 3 ? 84f : 120f) * pop;
            Shadowed(text, result.Method, new Vector2(w * 0.5f, h * 0.17f + ((1f - pop) * 20f)), size, Gold with { W = method }, TextAnchor.Center, 4f);
        }

        // 勝者の帯: 勝った側から全幅へ伸びる。
        var band = EaseOut(Clamp01((t - 1.2f) / 0.6f));
        var bandY = h * 0.5f;
        var (bright, deep) = result.Winner > 0 ? (RedBright, RedDeep) : result.Winner < 0 ? (BlueBright, BlueDeep) : (Gold with { W = 0.9f }, new Vector4(0.45f, 0.33f, 0.05f, 0.9f));
        var bandWidth = w * band;
        var bandX = result.Winner < 0 ? w - (bandWidth * 0.5f) : result.Winner > 0 ? bandWidth * 0.5f : w * 0.5f;
        shapes.Add(new UiShapeDraw(new Vector2(bandX, bandY), new Vector2(bandWidth, 128f), bright, FillColorEnd: deep, GradientAxis: Vector2.UnitY));
        shapes.Add(new UiShapeDraw(new Vector2(bandX, bandY - 64f), new Vector2(bandWidth, 3f), Gold));
        shapes.Add(new UiShapeDraw(new Vector2(bandX, bandY + 64f), new Vector2(bandWidth, 3f), Gold));
        var nameIn = Clamp01((t - 1.6f) / 0.4f);
        if (result.Winner == 0)
        {
            Shadowed(text, $"{card.Red.Name}  ×  {card.Blue.Name}", new Vector2(w * 0.5f, bandY - 34f), 58f, White with { W = nameIn }, TextAnchor.Center);
        }
        else
        {
            var winner = result.Winner > 0 ? card.Red : card.Blue;
            var corner = result.Winner > 0 ? "赤コーナー" : "青コーナー";
            Shadowed(text, "勝者", new Vector2(w * 0.5f - 250f, bandY - 22f), 38f, Gold with { W = nameIn }, TextAnchor.Right);
            Shadowed(text, winner.Name, new Vector2(w * 0.5f - 220f, bandY - 54f), 64f, White with { W = nameIn });
            Shadowed(text, $"{corner}　{winner.Discipline}", new Vector2(w * 0.5f - 216f, bandY + 20f), 26f, Gold with { W = nameIn });
        }

        // 決着の時刻と決まり手の説明。
        var detail = Clamp01((t - 2.1f) / 0.4f);
        var timeText = string.Create(CultureInfo.InvariantCulture, $"第{result.Round}ラウンド {(int)result.RoundTime / 60}:{(int)result.RoundTime % 60:00}");
        var detailText = result.ByDecision
            ? $"{card.Rules.Rounds}ラウンド終了　・　与えたダメージの判定"
            : $"{timeText}　・　{result.Method}（{result.Cause}）";
        Shadowed(text, detailText, new Vector2(w * 0.5f, bandY + 82f), 28f, White with { W = detail }, TextAnchor.Center);

        // 採点表（ラウンドごとに与えたダメージ）。
        var table = Clamp01((t - 2.7f) / 0.5f);
        if (table > 0f)
        {
            ScoreTable(text, shapes, card, result, new Vector2(w * 0.5f, h * 0.83f), table);
        }

        return (text, shapes);
    }

    private static void ScoreTable(List<ScreenTextDraw> text, List<UiShapeDraw> shapes, TvCard card, TvResult result, Vector2 center, float alpha)
    {
        var rounds = card.Rules.Rounds;
        const float NameColumn = 230f;
        const float Column = 82f;
        const float Row = 34f;
        var tableWidth = NameColumn + (Column * (rounds + 1)) + 24f;
        var left = center.X - (tableWidth * 0.5f);
        var top = center.Y - (Row * 1.5f);
        shapes.Add(UiShapeDraw.Rounded(center, new Vector2(tableWidth, Row * 3f + 14f), 8f, Ink with { W = 0.88f * alpha }));
        float ColumnX(int i) => left + NameColumn + (Column * (i + 0.5f));
        text.Add(new ScreenTextDraw("与えたダメージ", new Vector2(left + 16f, top + 6f), 20f, Grey with { W = alpha }));
        for (var r = 0; r <= rounds; r++)
        {
            text.Add(new ScreenTextDraw(r < rounds ? $"R{r + 1}" : "計", new Vector2(ColumnX(r), top + 6f), 20f, (r < rounds ? Grey : Gold) with { W = alpha }, TextAnchor.Center));
        }

        foreach (var (row, corner, scores, color) in new[] { (1, card.Red, result.RedScores, RedBright), (2, card.Blue, result.BlueScores, BlueBright) })
        {
            var y = top + (Row * row) + 4f;
            shapes.Add(UiShapeDraw.Rounded(new Vector2(left + 12f, y + 13f), new Vector2(8f, 24f), 2f, color with { W = alpha }));
            text.Add(new ScreenTextDraw(corner.Name, new Vector2(left + 24f, y), 24f, White with { W = alpha }));
            var total = 0f;
            for (var r = 0; r < rounds; r++)
            {
                var played = r < scores.Count;
                var value = played ? scores[r] : 0f;
                total += value;
                text.Add(new ScreenTextDraw(played ? string.Create(CultureInfo.InvariantCulture, $"{value:0}") : "—", new Vector2(ColumnX(r), y), 24f, (played ? White : Grey) with { W = alpha }, TextAnchor.Center));
            }

            text.Add(new ScreenTextDraw(string.Create(CultureInfo.InvariantCulture, $"{total:0}"), new Vector2(ColumnX(rounds), y), 24f, Gold with { W = alpha }, TextAnchor.Center));
        }
    }

    private static void CornerPanel(List<ScreenTextDraw> text, List<UiShapeDraw> shapes, TvCorner corner, Vector2 center, Vector2 size, Vector4 bright, Vector4 deep, string label, float fade)
    {
        shapes.Add(new UiShapeDraw(center, size, bright with { W = bright.W * fade }, UiShapeDraw.Uniform(10f), Gold with { W = 0.8f * fade }, 2f,
            FillColorEnd: deep with { W = deep.W * fade }, GradientAxis: Vector2.UnitY));
        var top = center.Y - (size.Y * 0.5f);
        text.Add(new ScreenTextDraw(corner.FullName.Length > 0 ? $"{label}　{corner.FullName}" : label, new Vector2(center.X, top + 14f), 20f, White with { W = 0.85f * fade }, TextAnchor.Center));
        Shadowed(text, corner.Name, new Vector2(center.X, top + 46f), 62f, White with { W = fade }, TextAnchor.Center);
        shapes.Add(new UiShapeDraw(new Vector2(center.X, top + 132f), new Vector2(size.X * 0.7f, 2f), Gold with { W = 0.7f * fade }));
        Shadowed(text, corner.Discipline, new Vector2(center.X, top + 142f), 34f, Gold with { W = fade }, TextAnchor.Center);
        text.Add(new ScreenTextDraw(corner.Detail, new Vector2(center.X, top + 182f), 20f, White with { W = 0.85f * fade }, TextAnchor.Center));
    }

    /// <summary>
    /// 試合中の表示（顔の窓を左下・右下に置くとき）: 上の真ん中にラウンドと時計、右上に LIVE、各コーナーの顔の窓の下に
    /// コーナーの色の名札とスタミナのメーター。顔の窓そのものと縁は <see cref="FaceCam"/> が描く（窓の中に文字を置かない。動画では窓で上書きされる）。
    /// </summary>
    public (IReadOnlyList<ScreenTextDraw> Text, IReadOnlyList<UiShapeDraw> Shapes) MatchHud(BoxingGame game, RenderViewport redPanel, RenderViewport bluePanel, string red, string blue)
    {
        ArgumentNullException.ThrowIfNull(game);
        var w = (float)width;
        var text = new List<ScreenTextDraw>();
        var shapes = new List<UiShapeDraw>();

        // ラウンドと時計（ラウンド中は残り、休みは BREAK と残り、試合の後は FINAL）。
        var rounds = game.Rules?.Rounds ?? 0;
        var (label, clock) = game.MatchOver
            ? ("FINAL", rounds > 0 ? $"R{game.MatchRound}" : "")
            : game.InBreak ? ("BREAK", Clock(game.BreakLeft)) : ($"ROUND {game.MatchRound}/{rounds}", Clock(game.RoundLeft));
        var clockCenter = new Vector2(w * 0.5f, 46f);
        shapes.Add(UiShapeDraw.Rounded(clockCenter, new Vector2(150f, 66f), 8f, new Vector4(0.93f, 0.93f, 0.95f, 0.95f)));
        shapes.Add(UiShapeDraw.Rounded(clockCenter - new Vector2(0f, 23f), new Vector2(150f, 20f), 8f, Ink with { W = 0.92f }));
        text.Add(new ScreenTextDraw(label, clockCenter - new Vector2(0f, 33f), 17f, Gold, TextAnchor.Center));
        text.Add(new ScreenTextDraw(clock, clockCenter - new Vector2(0f, 12f), 36f, new Vector4(0.06f, 0.06f, 0.08f, 1f), TextAnchor.Center));
        shapes.Add(UiShapeDraw.Rounded(new Vector2(w - 62f, 30f), new Vector2(72f, 28f), 5f, new Vector4(0.85f, 0.1f, 0.1f, 0.95f)));
        text.Add(new ScreenTextDraw("LIVE", new Vector2(w - 62f, 20f), 20f, White, TextAnchor.Center));
        Logo(text, shapes, new Vector2(70f, 34f), 1f);

        foreach (var (panel, name, fighter, bright, deep) in new[] { (redPanel, red, game.Player, RedBright, RedDeep), (bluePanel, blue, game.Cpu, BlueBright, BlueDeep) })
        {
            var centerX = panel.XPx + (panel.WidthPx * 0.5f);
            var plateWidth = panel.WidthPx + 6f;
            var plateTop = panel.YPx + panel.HeightPx + 3f;
            shapes.Add(new UiShapeDraw(new Vector2(centerX, plateTop + 15f), new Vector2(plateWidth, 30f), bright, FillColorEnd: deep, GradientAxis: Vector2.UnitY));
            Shadowed(text, name, new Vector2(centerX, plateTop + 3f), 23f, White, TextAnchor.Center);
            var barTop = plateTop + 30f;
            shapes.Add(new UiShapeDraw(new Vector2(centerX, barTop + 8f), new Vector2(plateWidth, 16f), Ink));
            shapes.AddRange(TvHud.StaminaBar(new Vector2(centerX - (plateWidth * 0.5f) + 6f, barTop + 8f), plateWidth - 12f, 8f, fighter));
        }

        return (text, shapes);
    }

    /// <summary>
    /// リプレイ中の表示: 上下の黒い帯、左上の REPLAY の札とラウンド、下の帯に場面の説明（誰の何がどこへ・どれだけ・ダウン/骨折/切断）。
    /// </summary>
    public (IReadOnlyList<ScreenTextDraw> Text, IReadOnlyList<UiShapeDraw> Shapes) ReplayOverlay(int round, string description, float videoTime)
    {
        var w = (float)width;
        var h = (float)height;
        var text = new List<ScreenTextDraw>();
        var shapes = new List<UiShapeDraw>
        {
            new(new Vector2(w * 0.5f, 26f), new Vector2(w, 52f), new Vector4(0f, 0f, 0f, 0.9f)),
            new(new Vector2(w * 0.5f, h - 34f), new Vector2(w, 68f), new Vector4(0f, 0f, 0f, 0.9f)),
            UiShapeDraw.Rounded(new Vector2(92f, 26f), new Vector2(124f, 32f), 6f, new Vector4(0.85f, 0.1f, 0.1f, 0.95f)),
        };
        // 録画中の印のように点滅する丸（見た目だけ）。
        var blink = 0.5f + (0.5f * MathF.Sin(videoTime * 6f));
        shapes.Add(UiShapeDraw.Rounded(new Vector2(44f, 26f), new Vector2(12f, 12f), 6f, White with { W = 0.4f + (0.6f * blink) }));
        text.Add(new ScreenTextDraw("REPLAY", new Vector2(100f, 13f), 24f, White, TextAnchor.Center));
        text.Add(new ScreenTextDraw($"第{round}ラウンド　スロー", new Vector2(170f, 14f), 22f, Gold));
        Shadowed(text, description, new Vector2(w * 0.5f, h - 50f), 24f, White, TextAnchor.Center);
        Logo(text, shapes, new Vector2(w - 70f, 26f), 0.8f);
        return (text, shapes);
    }

    /// <summary>
    /// 中継の主催団体（2026-11-16、ユーザーの指定）。中継は作中の世界のテレビ番組なので、ゲームの名前（VGV）は出さない。
    /// 競技の名前は Voxing（<see cref="SportName"/>）。
    /// </summary>
    public const string OrganizationName = "Wonderful Voxing Council";

    /// <summary>主催団体の略称（ロゴ）。</summary>
    public const string LogoText = "WVC";

    /// <summary>競技の名前（角の札の下の行）。</summary>
    public const string SportName = "Voxing";

    /// <summary>
    /// 中継のロゴの札: 黒から焦げ茶へのグラデーションの板に金の二重の縁、ボクセルを積んだ金の「WVC」、その下に字間を空けた競技の名前。
    /// <paramref name="center"/> は札の中心、<paramref name="scale"/> は大きさ。
    /// </summary>
    public static void Logo(List<ScreenTextDraw> text, List<UiShapeDraw> shapes, Vector2 center, float scale, float alpha = 1f)
    {
        var size = new Vector2(124f, 58f) * scale;
        shapes.Add(new UiShapeDraw(center, size, new Vector4(0.03f, 0.03f, 0.04f, 0.94f * alpha), UiShapeDraw.Uniform(4f * scale), Gold with { W = alpha }, 2f * scale,
            FillColorEnd: new Vector4(0.13f, 0.1f, 0.05f, 0.94f * alpha), GradientAxis: Vector2.UnitY));
        shapes.Add(new UiShapeDraw(center, size - new Vector2(8f * scale), default, UiShapeDraw.Uniform(2f * scale), Gold with { W = 0.55f * alpha }, 1f));
        VoxelWord(shapes, LogoText, center - new Vector2(0f, 8f * scale), 3.4f * scale, alpha);
        text.Add(new ScreenTextDraw(Spaced(SportName.ToUpperInvariant()), center + new Vector2(0f, 11.5f * scale), 9f * scale, new Vector4(0.95f, 0.9f, 0.75f, 0.9f * alpha), TextAnchor.Center));
    }

    /// <summary>文字の間に細い空白を入れる（高級感のある字間の広い組み方）。</summary>
    private static string Spaced(string value) => string.Join('\u2009', value.ToCharArray());

    /// <summary>
    /// 5 × 7 のマスの字形（ロゴに使う文字だけ）。'#' が積むボクセル。
    /// </summary>
    private static readonly Dictionary<char, string[]> VoxelGlyphs = new()
    {
        ['V'] = ["#...#", "#...#", "#...#", "#...#", ".#.#.", ".#.#.", "..#.."],
        ['G'] = [".###.", "#...#", "#....", "#.###", "#...#", "#...#", ".###."],
        ['W'] = ["#...#", "#...#", "#...#", "#.#.#", "#.#.#", "##.##", "#...#"],
        ['C'] = [".###.", "#...#", "#....", "#....", "#....", "#...#", ".###."],
    };

    /// <summary>
    /// ボクセルを積んだ金の文字: マスごとに、右下へずらした暗い銅色の側面（奥行き）を置き、その上に上が明るく下が深い金の面を置く（立方体の斜め見え）。
    /// <paramref name="center"/> は文字列の中心、<paramref name="cell"/> は 1 マスの大きさ [px]。
    /// </summary>
    public static void VoxelWord(List<UiShapeDraw> shapes, string word, Vector2 center, float cell, float alpha = 1f)
    {
        const int GlyphWidth = 5;
        const int GlyphHeight = 7;
        const float Gap = 1.5f; // 文字の間（マス）
        var columns = (word.Length * GlyphWidth) + ((word.Length - 1) * Gap);
        var origin = center - new Vector2(columns * cell * 0.5f, GlyphHeight * cell * 0.5f);
        var depth = new Vector2(cell * 0.4f);
        var face = new Vector2(cell * 0.9f);
        var side = new Vector4(0.36f, 0.22f, 0.07f, alpha);
        var top = new Vector4(1f, 0.93f, 0.62f, alpha);
        var bottom = new Vector4(0.8f, 0.55f, 0.16f, alpha);
        var cells = new List<Vector2>();
        for (var i = 0; i < word.Length; i++)
        {
            if (!VoxelGlyphs.TryGetValue(word[i], out var glyph))
            {
                continue;
            }

            var left = origin.X + (i * (GlyphWidth + Gap) * cell);
            for (var row = 0; row < GlyphHeight; row++)
            {
                for (var col = 0; col < GlyphWidth; col++)
                {
                    if (glyph[row][col] == '#')
                    {
                        cells.Add(new Vector2(left + ((col + 0.5f) * cell), origin.Y + ((row + 0.5f) * cell)));
                    }
                }
            }
        }

        // 側面を先に全部、その上に面（隣のマスの面が側面を隠す）。
        foreach (var c in cells)
        {
            shapes.Add(new UiShapeDraw(c + depth, face, side));
        }

        foreach (var c in cells)
        {
            shapes.Add(new UiShapeDraw(c, face, top, FillColorEnd: bottom, GradientAxis: Vector2.UnitY));
            shapes.Add(new UiShapeDraw(c - new Vector2(0f, face.Y * 0.4f), new Vector2(face.X, face.Y * 0.12f), new Vector4(1f, 1f, 0.9f, 0.35f * alpha)));
        }
    }

    /// <summary>リプレイへ入る・出るときの、金の帯が横切るワイプ（<paramref name="progress"/> 0..1）。</summary>
    public IReadOnlyList<UiShapeDraw> Wipe(float progress)
    {
        var w = (float)width;
        var h = (float)height;
        var x = (-0.3f * w) + (1.6f * w * Math.Clamp(progress, 0f, 1f));
        return
        [
            new(new Vector2(x - (0.16f * w), h * 0.5f), new Vector2(0.2f * w, h), Ink with { W = 0.95f }),
            new(new Vector2(x, h * 0.5f), new Vector2(0.12f * w, h), Gold with { W = 0.95f }, FillColorEnd: new Vector4(1f, 0.95f, 0.7f, 0.95f), GradientAxis: Vector2.UnitX),
        ];
    }

    /// <summary>顔の窓の下に名札とメーターを置くための、窓の下の余白 [px]（縁 3 + 名札 30 + メーター 16 + すき間）。</summary>
    public const float CornerHudBelowPanel = 58f;

    private static string Clock(float seconds)
    {
        var whole = (int)MathF.Ceiling(MathF.Max(0f, seconds));
        return string.Create(CultureInfo.InvariantCulture, $"{whole / 60}:{whole % 60:00}");
    }

    /// <summary>影を付けた文字（背景の 3D の絵の上でも読める）。</summary>
    private static void Shadowed(List<ScreenTextDraw> text, string value, Vector2 at, float size, Vector4 color, TextAnchor anchor = TextAnchor.Left, float offset = 2f)
    {
        text.Add(new ScreenTextDraw(value, at + new Vector2(offset, offset), size, new Vector4(0f, 0f, 0f, 0.7f * color.W), anchor));
        text.Add(new ScreenTextDraw(value, at, size, color, anchor));
    }

    private static float Clamp01(float x) => Math.Clamp(x, 0f, 1f);

    private static float Lerp(float a, float b, float t) => a + ((b - a) * t);

    private static float EaseOut(float t) => 1f - MathF.Pow(1f - t, 3f);

    private static float EaseInOut(float t) => t < 0.5f ? 4f * t * t * t : 1f - (MathF.Pow((-2f * t) + 2f, 3f) / 2f);

    /// <summary>少し行き過ぎて戻る（弾む）。</summary>
    private static float BackOut(float t)
    {
        const float C = 1.70158f;
        var u = t - 1f;
        return 1f + ((C + 1f) * u * u * u) + (C * u * u);
    }
}
