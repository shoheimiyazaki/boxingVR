using System.Globalization;
using System.Numerics;
using Aigis.Render;
using MiniRing.Arena.Shows.Wvc;
using MiniRing.Characters;

namespace MiniRing.Arena;

/// <summary>
/// VFC の番組の型: 何でもありの金網の団体の、深夜の有料配信のケージファイト。WVC の地上波の中継（金の細い罫・菱形の飾り・中央揃えの板・
/// 角丸・表）とは別の作りにする: 黒・クリーム・赤、角張ったボクセルの文字、斜めの切り口（平行四辺形の帯）、赤い斜線。
/// 試合の前は コールドオープン → ファイトポスター → 入場（赤・青）→ フェイスオフ、試合中は下の黒い帯に両者の体の図（見えている傷だけ）、
/// 休みは団体の言葉の看板（<c>text.intervalSign</c>）と傷の言葉、リプレイは赤い斜線のワイプと REWIND、決着は勝者の名前と死因の言葉と最後の体の図。
/// 色は団体のファイル（<see cref="OrgColors"/>）の値。描画と音の合図だけで、試合には響かない。
/// </summary>
internal sealed class VfcShow(int width, int height, Organization org) : ITvShow
{
    // ---- 場面の長さ（合計 25 秒。指示書: 試合の前は 22〜26 秒） ----

    /// <summary>コールドオープン [s]: 字幕 3 行（0.4・1.3・2.2 秒に 1 行ずつ）→ 3.3 秒で消え → 3.4 秒に紋章が打ち込まれ、1.6 秒の余韻。</summary>
    public const float ColdOpenSeconds = 5f;

    /// <summary>ファイトポスター [s]: 肖像が入る 0.8 秒、名前・武器・VS が出そろって 4 秒以上読める。</summary>
    public const float PosterSeconds = 6f;

    /// <summary>入場 1 人 [s]: 帯が滑り込み（0.5 秒）、5 行を 0.15 秒おきに出し、残りで読める。</summary>
    public const float WalkoutSeconds = 5f;

    /// <summary>フェイスオフ [s]: 2 人の顔が向き合うのを見せる。終わりは試合の始まりのホーン。</summary>
    public const float FaceOffSeconds = 4f;

    /// <summary>休みの看板を出す長さ [s]（休みの最初のこれだけ。カメラの切り替えは <see cref="TvDirector"/> のまま）。</summary>
    public const float IntervalShowSeconds = 8f;

    /// <summary>コールドオープンで紋章が打ち込まれる時刻 [s]（ホーンと同時）。</summary>
    public const float EmblemStrikeAt = 3.4f;

    private const float ResultLength = 9f;

    /// <summary>試合中の下の帯の高さ（画面の高さに対する割合）。</summary>
    private const float BandShare = 0.15f;

    /// <summary>顔の窓の一辺（画面の高さに対する割合）。</summary>
    private const float FaceSize = 0.18f;

    private readonly OrgColors _colors = org.Colors;
    private readonly Dictionary<string, CharacterPortrait> _portraits = [];
    private readonly VitalsMonitor _vitals = new();

    private Vector4 Cream => _colors.Accent;
    private Vector4 Muted => _colors.Muted;
    private Vector4 Black => _colors.Ink;
    private Vector4 RedTop => new(_colors.VoxelTop, 1f);
    private Vector4 RedDeep => new(_colors.VoxelBottom, 1f);
    private Vector4 RedDark => new(_colors.VoxelSide, 1f);

    private static readonly Vector4 BlueCorner = new(0.2f, 0.42f, 0.95f, 1f);

    public string Id => "vfc";

    public float ResultSeconds => ResultLength;

    public float IntervalBoardSeconds => IntervalShowSeconds;

    // ---- 試合の前の場面 ----

    public IReadOnlyList<ShowScene> PreFight(TvCard card, TvProgram? program, bool hasProfiles)
    {
        ArgumentNullException.ThrowIfNull(card);
        var scenes = new List<ShowScene>();
        if (program is not null)
        {
            scenes.Add(new ShowScene("fightcard", "fight_card", program.Seconds, $"ファイトカード（{program.Seconds:0} 秒）: {program.Title}。"
                + string.Join("。", program.Bouts.Select(b => $"{b.Bout} {b.Red} 対 {b.Blue}{(b.WinnerName is { } wn ? $"（{wn}の勝ち{(b.Cause is { Length: > 0 } cause ? $" {cause}" : "")}）" : b.Current ? "（この試合）" : "")}")), ShowBackdrop.Flat));
        }

        scenes.Add(new ShowScene("cold_open", "cold_open", ColdOpenSeconds,
            $"コールドオープン（{ColdOpenSeconds:0} 秒）: 暗い檻を外からゆっくり回って映し、字幕「{string.Join("」「", org.Text.ColdOpenLines)}」が 1 行ずつ。最後に VFC の紋章がホーンと一緒に打ち込まれる",
            ShowBackdrop.CageOrbit, EmblemStrikeAt));
        scenes.Add(new ShowScene("poster", "poster", PosterSeconds,
            $"ファイトポスター（{PosterSeconds:0} 秒）: 左に赤コーナーの{card.Red.Name}、右に青コーナーの{card.Blue.Name}の全身の絵が向き合い、間を赤い斜線が割る。{card.EventTitle} {card.Bout}、今夜のメインイベント。"
            + $"赤は{card.Red.Discipline}、青は{card.Blue.Discipline}", ShowBackdrop.Flat));
        scenes.Add(Walkout(card.Red, "walkout_red", ShowBackdrop.RedLow, "赤"));
        scenes.Add(Walkout(card.Blue, "walkout_blue", ShowBackdrop.BlueLow, "青"));
        scenes.Add(new ShowScene("faceoff", "faceoff", FaceOffSeconds,
            $"フェイスオフ（{FaceOffSeconds:0} 秒）: {card.Red.Name}と{card.Blue.Name}の顔が画面いっぱいに左右で向き合い、中央に FACE OFF。終わりにホーンで試合へ", ShowBackdrop.FaceOff));
        return scenes;
    }

    private static ShowScene Walkout(TvCorner corner, string id, ShowBackdrop backdrop, string side)
        => new(id, "walkout", WalkoutSeconds, $"入場（{side}コーナー、{WalkoutSeconds:0} 秒）: {corner.Name}が低いカメラで映り、左下の帯に{WalkoutFacts(corner)}", backdrop, null, corner.Name, StagePose.Corners);

    /// <summary>入場の帯に出す事実（記録にも同じ言葉で書く）。</summary>
    private static string WalkoutFacts(TvCorner corner)
    {
        var facts = new List<string>();
        if (corner.FullName.Length > 0)
        {
            facts.Add($"本名 {corner.FullName}");
        }

        facts.Add(corner.Discipline);
        if (corner.Profile is { } p)
        {
            facts.Add($"身長 {p.HeightCm} cm・体重 {p.WeightKg} kg");
            facts.Add($"戦績 {RecordText(p.FightRecord)}");
        }

        return string.Join("、", facts);
    }

    private static string RecordText(FightRecord r) => string.Create(CultureInfo.InvariantCulture, $"{r.Wins}勝{r.Losses}敗{(r.Draws > 0 ? $"{r.Draws}分" : "")}（{r.Kos} KO）");

    public ShowDraw Scene(ShowScene scene, TvCard card, TvProgram? program, float t)
    {
        ArgumentNullException.ThrowIfNull(scene);
        return scene.Id switch
        {
            "fightcard" => FightCard(program ?? throw new ArgumentNullException(nameof(program)), t),
            "cold_open" => ColdOpen(t),
            "poster" => Poster(card, t),
            "walkout_red" => WalkoutScene(card.Red, true, t),
            "walkout_blue" => WalkoutScene(card.Blue, false, t),
            "faceoff" => FaceOff(card, t),
            _ => throw new ArgumentException($"VFC の番組に場面 '{scene.Id}' はありません。", nameof(scene)),
        };
    }

    /// <summary>場面の頭に赤い斜線が画面を横切る（0.22 秒。場面の切れ目の合図）。</summary>
    private void Slash(List<UiShapeDraw> shapes, float t)
    {
        var w = (float)width;
        var h = (float)height;
        var p = Clamp01(t / 0.22f);
        if (p <= 0f || p >= 1f)
        {
            return;
        }

        var x = Lerp(-0.25f * w, 1.3f * w, p);
        BlockFont.Slab(shapes, x - (0.1f * w), x, 0f, h, 0.2f * w, Black with { W = 0.9f });
        BlockFont.Slab(shapes, x, x + (0.07f * w), 0f, h, 0.2f * w, RedTop);
    }

    private ShowDraw ColdOpen(float t)
    {
        var w = (float)width;
        var h = (float)height;
        var text = new List<ScreenTextDraw>();
        var shapes = new List<UiShapeDraw>();

        // 檻を暗く沈め、上下に黒い帯（シネマスコープ）。
        shapes.Add(Rect(0f, 0f, w, h, new Vector4(0f, 0f, 0f, 0.3f + (0.25f * Clamp01((t - (EmblemStrikeAt - 0.1f)) / 0.2f)))));
        var bar = h * 0.1f * EaseOut(Clamp01(t / 0.5f));
        shapes.Add(Rect(0f, 0f, w, bar, Black));
        shapes.Add(Rect(0f, h - bar, w, h, Black));
        shapes.Add(Rect(0f, bar, w, bar + 3f, RedTop));
        shapes.Add(Rect(0f, h - bar - 3f, w, h - bar, RedTop));

        // 字幕: 1 行ずつ叩きつけるように出て、紋章の前に一斉に消える。
        // 字幕は団体の言葉（text.coldOpen）。檻の絵を隠さないよう小さく（58 → 34 px。2026-11-19、ユーザー: 説明の UI が大きすぎて邪魔）。
        var lines = org.Text.ColdOpenLines;
        var vanish = 1f - Clamp01((t - 3.3f) / 0.12f);
        const float Size = 34f;
        for (var i = 0; i < lines.Count; i++)
        {
            var appear = EaseOut(Clamp01((t - (0.4f + (0.9f * i))) / 0.22f));
            var alpha = appear * vanish;
            if (alpha <= 0f)
            {
                continue;
            }

            var y = (h * 0.16f) + (i * (Size + 26f));
            var x0 = (w * 0.04f) - ((1f - appear) * 80f);
            var length = (lines[i].Length * Size) + 56f;
            BlockFont.Slab(shapes, x0, x0 + length, y - 7f, y + Size + 7f, 20f, Black with { W = 0.85f * alpha });
            BlockFont.Slab(shapes, x0 - 10f, x0, y - 7f, y + Size + 7f, 20f, RedTop with { W = alpha });
            Shadowed(text, lines[i], new Vector2(x0 + 24f, y), Size, Cream with { W = alpha });
        }

        // 紋章: ホーンと同時に大きく入って縮み、止まる（少し揺れる）。
        var e = t - EmblemStrikeAt;
        if (e >= 0f)
        {
            var scale = Lerp(1.7f, 1f, EaseOut(Clamp01(e / 0.3f)));
            var shake = 16f * MathF.Exp(-e * 9f);
            var center = new Vector2((w * 0.5f) + (shake * MathF.Sin(e * 60f)), (h * 0.5f) + (0.6f * shake * MathF.Cos(e * 52f)));
            Emblems.Draw(org, text, shapes, center, 0.6f * w / VfcEmblem.Width * scale);
            var flash = 0.55f * MathF.Exp(-e * 10f);
            if (flash > 0.01f)
            {
                shapes.Add(Rect(0f, 0f, w, h, new Vector4(1f, 1f, 1f, flash)));
            }
        }

        Slash(shapes, t);
        return new ShowDraw(text, shapes);
    }

    private ShowDraw Poster(TvCard card, float t)
    {
        var w = (float)width;
        var h = (float)height;
        var text = new List<ScreenTextDraw>();
        var shapes = new List<UiShapeDraw>();
        const float Lean = 0.2f; // 斜線の傾き（画面の幅に対する、上下の x のずれ）
        var skew = Lean * w;
        var cut = Clamp01(t / 0.4f);

        // 左は黒、右はごく暗い赤。間を赤い斜めの切り口が割る。切り口は上の端で skew/2 だけ右、下の端で skew/2 だけ左にあり、
        // 画面の高さの真ん中で横の真ん中を通る（以前は上の端が真ん中で、線全体が左へ寄っていた。2026-11-19 ユーザーの指摘）。
        var top = (w * 0.5f) + (skew * 0.5f);
        shapes.Add(Rect(0f, 0f, w, h, new Vector4(0.075f, 0.07f, 0.07f, 1f)));
        BlockFont.Slab(shapes, top, w + skew, 0f, h, skew, new Vector4(0.12f, 0.022f, 0.02f, 1f));
        BlockFont.Slab(shapes, top - 26f, top + 26f, 0f, h * EaseOut(cut), skew * EaseOut(cut), RedTop);
        BlockFont.Slab(shapes, top - 36f, top - 30f, 0f, h * EaseOut(cut), skew * EaseOut(cut), Cream);

        // 肖像（左は赤・右は青。全身の絵を試合の体のボクセルから写したもの）。
        var enter = EaseOut(Clamp01((t - 0.15f) / 0.7f));
        // 頭が左上の看板（MAIN EVENT。下端は h * 0.04 + 114 px）の下に収まる高さまでにする（背が高い選手の頭が看板に隠れていた）。
        var feet = h * 0.9f;
        var portraitHeight = MathF.Min(h * 0.78f, feet - ((h * 0.04f) + 114f + 22f));
        DrawPortrait(shapes, card.Red, new Vector2(Lerp(-0.3f * w, w * 0.25f, enter), feet), portraitHeight, blue: false);
        DrawPortrait(shapes, card.Blue, new Vector2(Lerp(1.3f * w, w * 0.75f, enter), feet), portraitHeight, blue: true);

        // 看板: 今夜のメインイベント・大会名と何試合目。
        var tag = Clamp01((t - 0.3f) / 0.3f);
        BlockFont.Slab(shapes, -30f, 440f * EaseOut(tag), h * 0.04f, h * 0.04f + 108f, 28f, Black with { W = 0.92f });
        BlockFont.Slab(shapes, -30f, 440f * EaseOut(tag), h * 0.04f + 108f, h * 0.04f + 114f, 28f, RedTop);
        if (tag > 0.4f)
        {
            BlockFont.Draw(shapes, "MAIN EVENT", new Vector2(34f, (h * 0.04f) + 14f), 5.5f, Cream, 0.2f);
            Shadowed(text, "今夜のメインイベント", new Vector2(34f, (h * 0.04f) + 64f), 30f, RedTop);
        }

        Shadowed(text, card.EventTitle, new Vector2(w - 32f, (h * 0.04f) + 6f), 30f, Cream with { W = tag }, TextAnchor.Right);
        Shadowed(text, card.Bout, new Vector2(w - 32f, (h * 0.04f) + 44f), 26f, Muted with { W = tag }, TextAnchor.Right);

        // 名前（弾む）と、武器・はめる物。
        foreach (var (corner, centerX, color, appearAt) in new[] { (card.Red, w * 0.25f, RedTop, 1.1f), (card.Blue, w * 0.75f, BlueCorner, 1.4f) })
        {
            var pop = BackOut(Clamp01((t - appearAt) / 0.35f));
            if (pop <= 0f)
            {
                continue;
            }

            var size = MathF.Min(88f, (w * 0.44f) / MathF.Max(1f, corner.Name.Length)) * pop;
            var y = h * 0.775f;
            BlockFont.Slab(shapes, centerX - (w * 0.22f), centerX + (w * 0.22f), y - 8f, y + 142f, 26f, Black with { W = 0.82f });
            BlockFont.Slab(shapes, centerX - (w * 0.22f), centerX + (w * 0.22f), y + 142f, y + 150f, 26f, color);
            Shadowed(text, corner.Name, new Vector2(centerX, y), size, Cream, TextAnchor.Center, 3f);
            var gear = Clamp01((t - appearAt - 0.25f) / 0.3f);
            Shadowed(text, corner.Discipline, new Vector2(centerX, y + 100f), 30f, Cream with { W = gear }, TextAnchor.Center);
        }

        // VS（中央の斜めの切り口の上）。
        var vs = Clamp01((t - 1.8f) / 0.3f);
        if (vs > 0f)
        {
            var cell = 15f * BackOut(vs);
            var plate = BlockFont.Width("VS", cell);
            BlockFont.Slab(shapes, (w * 0.5f) - (plate * 0.5f) - 22f, (w * 0.5f) + (plate * 0.5f) + 22f, (h * 0.46f) - 14f, (h * 0.46f) + BlockFont.Height(cell) + 14f, 24f, Black with { W = 0.95f });
            BlockFont.Draw(shapes, "VS", new Vector2((w * 0.5f) - (plate * 0.5f), h * 0.46f), cell, Cream, 0.2f);
        }

        Slash(shapes, t);
        return new ShowDraw(text, shapes);
    }

    private ShowDraw WalkoutScene(TvCorner corner, bool red, float t)
    {
        var w = (float)width;
        var h = (float)height;
        var text = new List<ScreenTextDraw>();
        var shapes = new List<UiShapeDraw>();
        var color = red ? RedTop : BlueCorner;

        // 上下に黒い帯。左下に斜めの帯が滑り込み、行が 1 つずつ出る。
        var bar = h * 0.06f * EaseOut(Clamp01(t / 0.4f));
        shapes.Add(Rect(0f, 0f, w, bar, Black));
        shapes.Add(Rect(0f, h - bar, w, h, Black));
        var slide = EaseOut(Clamp01(t / 0.5f));
        var left = Lerp(-0.7f * w, -40f, slide);
        var right = left + (0.66f * w);
        var top = h * 0.62f;
        var bottom = h * 0.925f;
        BlockFont.Slab(shapes, left, right, top, bottom, 70f, Black with { W = 0.88f });
        BlockFont.Slab(shapes, left, left + 40f, top, bottom, 70f, color);
        BlockFont.Slab(shapes, left, right, bottom - 5f, bottom, 70f, color);

        // 角の札（RED CORNER / BLUE CORNER）。
        var label = red ? "RED CORNER" : "BLUE CORNER";
        BlockFont.Slab(shapes, left + 60f, left + 60f + BlockFont.Width(label, 3.5f) + 40f, top - 34f, top, 70f * 0.1f, color);
        BlockFont.Draw(shapes, label, new Vector2(left + 82f, top - 29f), 3.5f, Cream);

        var x = left + 100f;
        var rows = Rows(corner);
        for (var i = 0; i < rows.Count; i++)
        {
            var a = Clamp01((t - 0.35f - (0.15f * i)) / 0.25f);
            if (a <= 0f)
            {
                continue;
            }

            var (value, size, rowColor, y) = rows[i];
            Shadowed(text, value, new Vector2(x - ((1f - a) * 60f), top + y), size, rowColor with { W = a });
        }

        Slash(shapes, t);
        return new ShowDraw(text, shapes);
    }

    /// <summary>入場の帯の行（文字・大きさ・色・帯の上端からの y）。</summary>
    private List<(string Value, float Size, Vector4 Color, float Y)> Rows(TvCorner corner)
    {
        var rows = new List<(string, float, Vector4, float)> { (corner.Name, 64f, Cream, 6f) };
        var sub = corner.FullName.Length > 0 ? $"{corner.FullName}　{corner.Detail.Split('・').LastOrDefault() ?? ""}" : corner.Detail;
        rows.Add((sub, 22f, Muted, 76f));
        rows.Add((corner.Discipline, 30f, RedTop, 104f));
        if (corner.Profile is { } p)
        {
            rows.Add((string.Create(CultureInfo.InvariantCulture, $"身長 {p.HeightCm} cm　体重 {p.WeightKg} kg　戦績 {RecordText(p.FightRecord)}"), 24f, Cream, 142f));
            if (p.Quote.Length > 0)
            {
                var quote = p.Quote.Length > 26 ? p.Quote[..25] + "…" : p.Quote;
                rows.Add(($"「{quote}」", 22f, Muted, 178f));
            }
        }

        return rows;
    }

    private ShowDraw FaceOff(TvCard card, float t)
    {
        var w = (float)width;
        var h = (float)height;
        var text = new List<ScreenTextDraw>();
        var shapes = new List<UiShapeDraw>();
        var top = h * 0.14f;
        var bottom = h * 0.86f;

        // 窓の上下は黒い帯。顔の窓（左が赤・右が青）は窓の置き場（FaceOffCam）が描く。
        shapes.Add(Rect(0f, 0f, w, top, Black));
        shapes.Add(Rect(0f, bottom, w, h, Black));
        shapes.Add(Rect(0f, top - 4f, w, top, RedTop));
        shapes.Add(Rect(0f, bottom, w, bottom + 4f, RedTop));

        // 窓の間の斜めの赤い切り口。
        var gap = Clamp01(t / 0.3f);
        BlockFont.Slab(shapes, (w * 0.5f) - 24f, (w * 0.5f) + 24f, top, top + ((bottom - top) * EaseOut(gap)), 46f, RedTop);

        var appear = EaseOut(Clamp01((t - 0.2f) / 0.35f));
        var margin = w * 0.047f;
        Shadowed(text, card.Red.Name, new Vector2(margin - ((1f - appear) * 200f), top * 0.18f), 62f, Cream with { W = appear });
        Shadowed(text, card.Blue.Name, new Vector2(w - margin + ((1f - appear) * 200f), top * 0.18f), 62f, Cream with { W = appear }, TextAnchor.Right);
        shapes.Add(Rect(margin, top - 12f, margin + (w * 0.2f * appear), top - 6f, RedTop));
        shapes.Add(Rect(w - margin - (w * 0.2f * appear), top - 12f, w - margin, top - 6f, BlueCorner));

        var banner = "FACE OFF";
        var cell = 9f * BackOut(Clamp01((t - 0.5f) / 0.3f));
        if (cell > 0.5f)
        {
            var span = BlockFont.Width(banner, cell);
            BlockFont.Slab(shapes, (w * 0.5f) - (span * 0.5f) - 50f, (w * 0.5f) + (span * 0.5f) + 50f, bottom + 8f, h - 8f, 30f, RedDeep);
            BlockFont.Draw(shapes, banner, new Vector2((w * 0.5f) - (span * 0.5f), bottom + ((h - bottom - BlockFont.Height(cell)) * 0.5f)), cell, Cream, 0.2f);
        }

        return new ShowDraw(text, shapes);
    }

    /// <summary>フェイスオフの顔の窓: 画面の高さいっぱいに近い大きさで左右に（窓の上下に黒い帯が残る）。</summary>
    public FaceCam ArrangeFaceOff(FaceCam cam, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(cam);
        return cam with
        {
            SizeOfScreenHeight = 0.72f,
            MarginPixels = width * 0.047f,
            CenterOfScreenHeight = 0.5f,
            FramePixels = 6f,
            RedFrameColor = new Vector4(0.93f, 0.14f, 0.11f, 1f),
            BlueFrameColor = BlueCorner,
        };
    }

    private ShowDraw FightCard(TvProgram program, float t)
    {
        var w = (float)width;
        var h = (float)height;
        var text = new List<ScreenTextDraw>();
        var shapes = new List<UiShapeDraw>();
        var fade = 1f - Clamp01((t - (program.Seconds - 0.4f)) / 0.4f);
        shapes.Add(Rect(0f, 0f, w, h, new Vector4(0.015f, 0.012f, 0.012f, 0.94f * Clamp01(t / 0.2f))));
        BlockFont.Slab(shapes, w * 0.62f, w + 60f, 0f, h, 0.16f * w, new Vector4(0.1f, 0.018f, 0.016f, 1f * fade));
        BlockFont.Draw(shapes, "FIGHT CARD", new Vector2(w * 0.06f, h * 0.05f), 9f, Cream with { W = fade }, 0.2f);
        Shadowed(text, program.Title, new Vector2(w * 0.06f, (h * 0.05f) + 74f), 28f, RedTop with { W = fade });

        var count = program.Bouts.Count;
        var rowHeight = MathF.Min(88f, (h * 0.6f) / count);
        var top = h * 0.27f;
        for (var i = 0; i < count; i++)
        {
            var bout = program.Bouts[i];
            var slide = EaseOut(Clamp01((t - 0.3f - (0.25f * i)) / 0.4f));
            if (slide <= 0f)
            {
                continue;
            }

            var a = slide * fade;
            var y = top + (i * rowHeight);
            var x0 = (w * 0.06f) - ((1f - slide) * w * 0.5f);
            var x1 = w * 0.94f;
            BlockFont.Slab(shapes, x0, x1, y, y + rowHeight - 12f, 36f, (bout.Current ? RedDeep : Black) with { W = 0.92f * a });
            BlockFont.Slab(shapes, x0, x0 + 18f, y, y + rowHeight - 12f, 36f, (bout.Current ? Cream : RedTop) with { W = a });
            var textY = y + ((rowHeight - 12f - 28f) * 0.5f) - 4f;
            text.Add(new ScreenTextDraw(bout.Bout, new Vector2(x0 + 50f, textY + 2f), 22f, (bout.Current ? Cream : Muted) with { W = a }));
            var redColor = bout.Winner == "blue" ? Muted : Cream;
            var blueColor = bout.Winner == "red" ? Muted : Cream;
            var midX = x0 + ((x1 - x0) * 0.46f);
            text.Add(new ScreenTextDraw(bout.Red, new Vector2(midX - 50f, textY), 28f, redColor with { W = a }, TextAnchor.Right));
            BlockFont.Draw(shapes, "VS", new Vector2(midX - 22f, textY + 2f), 4f, RedTop with { W = a }, 0.2f);
            text.Add(new ScreenTextDraw(bout.Blue, new Vector2(midX + 50f, textY), 28f, blueColor with { W = a }));
            var rightEdge = x1 - 50f;
            if (bout.WinnerName is { } winner)
            {
                var how = bout.Cause is { Length: > 0 } ? bout.Cause : bout.Method;
                text.Add(new ScreenTextDraw($"{winner} 勝ち{(how.Length > 0 ? $"　{how}" : "")}", new Vector2(rightEdge, textY + 2f), 22f, RedTop with { W = a }, TextAnchor.Right));
            }
            else if (bout.Current)
            {
                BlockFont.Draw(shapes, "NEXT", new Vector2(rightEdge - BlockFont.Width("NEXT", 4f), textY + 2f), 4f, Cream with { W = a }, 0.2f);
            }
        }

        if (program.Current is { Stake.Length: > 0 } current)
        {
            var stake = Clamp01((t - 0.6f - (count * 0.25f)) / 0.4f);
            var y = h * 0.9f;
            BlockFont.Slab(shapes, w * 0.06f, (w * 0.06f) + ((w * 0.62f) * EaseOut(stake)), y - 6f, y + 44f, 26f, RedDeep with { W = 0.95f * fade });
            Shadowed(text, $"{current.Bout}　{current.Stake}", new Vector2((w * 0.06f) + 36f, y), 28f, Cream with { W = stake * fade });
        }

        return new ShowDraw(text, shapes);
    }

    // ---- 肖像 ----

    /// <summary>肖像を足元（<paramref name="feet"/> は足の中心）から描く。キャラクターの全身の絵が無ければ、名前の頭文字を大きく薄く。</summary>
    private void DrawPortrait(List<UiShapeDraw> shapes, TvCorner corner, Vector2 feet, float tall, bool blue)
    {
        if (corner.Character is not { } character)
        {
            return;
        }

        if (!_portraits.TryGetValue(character.Id, out var portrait))
        {
            portrait = CharacterPortraits.Build(character);
            _portraits[character.Id] = portrait;
        }

        var image = portrait.Body;
        var pixel = tall / image.Height;
        var origin = new Vector2(feet.X - (image.Width * pixel * 0.5f), feet.Y - tall);
        var overlap = 0.7f; // 隣の四角との継ぎ目を消す重なり [px]
        for (var y = 0; y < image.Height; y++)
        {
            var x = 0;
            while (x < image.Width)
            {
                var color = image.ColorAt(x, y, blue);
                if (color.W == 0f)
                {
                    x++;
                    continue;
                }

                var end = x + 1;
                while (end < image.Width && image.ColorAt(end, y, blue) == color)
                {
                    end++;
                }

                var size = new Vector2(((end - x) * pixel) + overlap, pixel + overlap);
                shapes.Add(new UiShapeDraw(origin + new Vector2((x * pixel) + (((end - x) * pixel) * 0.5f), (y * pixel) + (pixel * 0.5f)), size, color));
                x = end;
            }
        }
    }

    // ---- 試合中 ----

    public FaceCam Arrange(FaceCam cam, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(cam);
        return cam with
        {
            SizeOfScreenHeight = FaceSize,
            MarginPixels = 16f,
            FramePixels = 5f,
            RedFrameColor = new Vector4(0.93f, 0.14f, 0.11f, 1f),
            BlueFrameColor = BlueCorner,
            // 窓は下の黒い帯のすぐ上（帯の上に赤・青の札）。
            CenterOfScreenHeight = (height - (height * BandShare) - 14f - (FaceSize * height * 0.5f)) / height,
        };
    }

    public float CaptionY(int height) => height - (height * BandShare) - 56f;

    public ShowDraw MatchHud(BoxingGame game, RenderViewport redPanel, RenderViewport bluePanel, string red, string blue, ShowBodies bodies)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(bodies);
        var w = (float)width;
        var h = (float)height;
        var text = new List<ScreenTextDraw>();
        var shapes = new List<UiShapeDraw>();

        // 左上: 斜めの黒い板に赤い縁。ラウンドと残り時間（角張った字。真ん中の大きな時計は使わない）。
        var rounds = game.Rules?.Rounds ?? 0;
        var (label, clock) = game.MatchOver ? ("FIN", "")
            : game.InBreak ? ("INT", Clock(game.BreakLeft))
            : (string.Create(CultureInfo.InvariantCulture, $"R{game.MatchRound}/{rounds}"), Clock(game.RoundLeft));
        const float TopHeight = 62f;
        BlockFont.Slab(shapes, -40f, 340f, 0f, TopHeight, 26f, Black with { W = 0.92f });
        BlockFont.Slab(shapes, -40f, 340f, TopHeight - 5f, TopHeight, 26f, RedTop);
        BlockFont.Draw(shapes, label, new Vector2(22f, 12f), 5f, RedTop, 0.15f);
        BlockFont.Draw(shapes, clock, new Vector2(22f + BlockFont.Width(label, 5f) + 26f, 12f), 5f, Cream, 0.15f);

        // 右上: 小さな紋章と、深夜配信の札。
        BlockFont.Slab(shapes, w - 330f, w + 40f, 0f, TopHeight, 26f, Black with { W = 0.92f });
        BlockFont.Slab(shapes, w - 330f, w + 40f, TopHeight - 5f, TopHeight, 26f, RedTop);
        Emblems.Draw(org, text, shapes, new Vector2(w - 276f, 31f), 1.25f);
        var tag = org.Text.LiveTag;
        BlockFont.Draw(shapes, tag, new Vector2(w - 200f, 8f), 4f, RedTop, 0.15f);
        if (org.Text.Strapline.Length > 0)
        {
            text.Add(new ScreenTextDraw(org.Text.Strapline, new Vector2(w - 200f, 40f), 15f, Muted));
        }

        // 下: 画面いっぱいの黒い帯。両者の名前と、その内側に体の図（見えている傷だけ）。
        var bandHeight = h * BandShare;
        var bandTop = h - bandHeight;
        shapes.Add(Rect(0f, bandTop, w, h, Black with { W = 0.94f }));
        shapes.Add(Rect(0f, bandTop, w, bandTop + 3f, Cream with { W = 0.85f }));
        BlockFont.Slab(shapes, -30f, 400f, bandTop - 10f, bandTop, 22f, RedTop);
        BlockFont.Slab(shapes, w - 400f, w + 30f, bandTop - 10f, bandTop, -22f, BlueCorner);
        BlockFont.Draw(shapes, "VS", new Vector2((w * 0.5f) - (BlockFont.Width("VS", 4f) * 0.5f), bandTop + ((bandHeight - BlockFont.Height(4f)) * 0.5f)), 4f, RedTop, 0.15f);

        var unit = bandHeight * 0.78f / FigureHeightUnits;
        var figureWidth = FigureWidthUnits * unit;
        var figureTop = bandTop + (bandHeight * 0.11f);
        Shadowed(text, red, new Vector2(24f, bandTop + (bandHeight * 0.16f)), 38f, Cream);
        Shadowed(text, blue, new Vector2(w - 24f, bandTop + (bandHeight * 0.16f)), 38f, Cream, TextAnchor.Right);
        DrawFigure(shapes, bodies.Of(game.Player), new Vector2(w * 0.5f - 90f - figureWidth, figureTop), unit, 1f, sunk: false);
        DrawFigure(shapes, bodies.Of(game.Cpu), new Vector2(w * 0.5f + 90f, figureTop), unit, 1f, sunk: false);

        // 顔の窓の下の帯、名前の横に小さなバイタルモニター: 波形と数字だけ（説明の文字は出さない。2026-11-19、ユーザーの指定）。
        // 帯の高さいっぱいを使う（名前の下に置くと 1 本の波が 20 px ほどになり、拍が見えなかった）。
        _vitals.Sample(game.Player);
        _vitals.Sample(game.Cpu);
        var monitorTop = bandTop + 14f;
        var monitorHeight = h - 10f - monitorTop;
        var monitorLeft = w * 0.15f;
        var monitorWidth = (w * 0.5f) - 90f - figureWidth - 24f - monitorLeft;
        _vitals.Draw(game.Player, new Vector2(monitorLeft, monitorTop), monitorWidth, monitorHeight, text, shapes);
        _vitals.Draw(game.Cpu, new Vector2(w - monitorLeft - monitorWidth, monitorTop), monitorWidth, monitorHeight, text, shapes);
        return new ShowDraw(text, shapes);
    }

    // ---- 体の図 ----

    /// <summary>図の幅（単位）。</summary>
    public const float FigureWidthUnits = 14f;

    /// <summary>図の高さ（単位）。</summary>
    public const float FigureHeightUnits = 31f;

    /// <summary>
    /// 体の図（頭・胴・腰・両腕・両脚の小さなボクセルの人形）。無傷はクリーム、削れた割合で赤へ、折れた部位はひびの入った暗い赤、
    /// 失った部位は輪郭だけ。<paramref name="sunk"/> は敗者・死者: 灰色に沈める。<paramref name="topLeft"/> は図の左上、<paramref name="unit"/> は 1 単位 [px]。
    /// 画面の左の腕・脚が本人の右。
    /// </summary>
    public void DrawFigure(List<UiShapeDraw> shapes, BodyFigure figure, Vector2 topLeft, float unit, float alpha, bool sunk)
    {
        void Part(FigurePart part, float x, float y, float partWidth, float partHeight)
        {
            var s = figure[part];
            var size = new Vector2((partWidth * unit) - MathF.Max(1f, unit * 0.18f), (partHeight * unit) - MathF.Max(1f, unit * 0.18f));
            var center = topLeft + new Vector2((x + (partWidth * 0.5f)) * unit, (y + (partHeight * 0.5f)) * unit);
            if (s.Gone)
            {
                shapes.Add(UiShapeDraw.Outline(center, size, 0f, Cream with { W = 0.4f * alpha }, MathF.Max(1f, unit * 0.18f)));
                return;
            }

            Vector4 color;
            if (sunk)
            {
                color = new Vector4(0.34f, 0.32f, 0.3f, 0.85f * alpha);
            }
            else if (s.Broken)
            {
                color = new Vector4(0.36f, 0.05f, 0.05f, alpha);
            }
            else
            {
                color = Vector4.Lerp(Cream, RedTop, Clamp01(s.Lost * 1.3f)) with { W = alpha };
            }

            shapes.Add(new UiShapeDraw(center, size, color));
            if (s.Broken && !sunk)
            {
                // ひび: ずれた短い線を 3 本。
                var thick = MathF.Max(1f, unit * 0.2f);
                for (var k = -1; k <= 1; k++)
                {
                    shapes.Add(new UiShapeDraw(center + new Vector2(k * size.X * 0.16f, k * size.Y * 0.22f), new Vector2(size.X * 0.55f, thick), Cream with { W = 0.9f * alpha }));
                }
            }
        }

        // 画面の左の腕・脚が本人の右。
        Part(FigurePart.Head, 5f, 0f, 4f, 4.2f);
        Part(FigurePart.Chest, 3f, 5f, 8f, 8f);
        Part(FigurePart.Waist, 4f, 13.4f, 6f, 3.6f);
        Part(FigurePart.RightUpperArm, 0.4f, 5f, 2.4f, 6f);
        Part(FigurePart.RightForearm, 0.4f, 11.2f, 2.4f, 5.8f);
        Part(FigurePart.LeftUpperArm, 11.2f, 5f, 2.4f, 6f);
        Part(FigurePart.LeftForearm, 11.2f, 11.2f, 2.4f, 5.8f);
        Part(FigurePart.RightThigh, 4f, 17.4f, 2.8f, 7f);
        Part(FigurePart.RightShin, 4f, 24.6f, 2.8f, 6.4f);
        Part(FigurePart.LeftThigh, 7.2f, 17.4f, 2.8f, 7f);
        Part(FigurePart.LeftShin, 7.2f, 24.6f, 2.8f, 6.4f);

        // 目: 潰れた側だけ赤い点（顔の画面の左が本人の右目）。
        if (!figure[FigurePart.Head].Gone && !sunk)
        {
            foreach (var (sight, x) in new[] { (figure.RightSight, 5.7f), (figure.LeftSight, 7.7f) })
            {
                if (sight < 0.5f)
                {
                    shapes.Add(new UiShapeDraw(topLeft + new Vector2((x + 0.3f) * unit, 1.9f * unit), new Vector2(MathF.Max(1.5f, unit * 0.9f)), RedDeep with { W = alpha }));
                }
            }
        }
    }

    // ---- 休み ----

    public string IntervalNote(BoxingGame game, string red, string blue, ShowBodies bodies, IReadOnlyList<WvcRoundTally> rounds)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(bodies);
        string Wounds(Fighter f) => bodies.Of(f).Wounds() is { Count: > 0 } w ? string.Join("・", w) : "目立つ傷なし";
        return $"休みの看板（{IntervalShowSeconds:0} 秒、画面の上の真ん中。体の図は赤が左の端・青が右の端）: 「{org.Text.IntervalSign}」。両者の体の図と見えている傷。{red}: {Wounds(game.Player)}。{blue}: {Wounds(game.Cpu)}";
    }

    public ShowDraw Interval(BoxingGame game, string red, string blue, ShowBodies bodies, float breakTime, IReadOnlyList<WvcRoundTally> rounds)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(bodies);
        var text = new List<ScreenTextDraw>();
        var shapes = new List<UiShapeDraw>();
        var a = Clamp01(breakTime / 0.3f) * (1f - Clamp01((breakTime - (IntervalShowSeconds - 0.5f)) / 0.5f));
        if (a <= 0f)
        {
            return ShowDraw.Empty;
        }

        var w = (float)width;
        var h = (float)height;
        var slide = 1f - EaseOut(Clamp01(breakTime / 0.4f));

        // 上の真ん中に小さな看板（INTERVAL と団体の言葉）。画面の大半は休む選手の絵に残す（2026-11-19、ユーザー: 説明の UI が大きすぎて邪魔）。
        var signY = (h * 0.035f) - (slide * 80f);
        var signCell = 3.2f;
        var signWidth = MathF.Max(BlockFont.Width("INTERVAL", signCell), org.Text.IntervalSign.Length * 22f) + 48f;
        BlockFont.Slab(shapes, (w - signWidth) * 0.5f, (w + signWidth) * 0.5f, signY, signY + 66f, 14f, Black with { W = 0.8f * a });
        BlockFont.Slab(shapes, (w - signWidth) * 0.5f, (w + signWidth) * 0.5f, signY + 66f, signY + 70f, 14f, RedTop with { W = a });
        BlockFont.Draw(shapes, "INTERVAL", new Vector2((w - BlockFont.Width("INTERVAL", signCell)) * 0.5f, signY + 8f), signCell, Cream with { W = a }, 0.15f);
        Shadowed(text, org.Text.IntervalSign, new Vector2(w * 0.5f, signY + 36f), 22f, Cream with { W = a }, TextAnchor.Center);

        // 両者の体の図と、見えている傷: 赤は左の端、青は右の端（下の左右の顔の窓の真上。顔と図の側をそろえる。2026-11-19、ユーザーの指定）。
        var unit = h * 0.22f / FigureHeightUnits;
        var figureWidth = FigureWidthUnits * unit;
        var figureTop = h * 0.3f;
        var margin = w * 0.025f;
        var columnWidth = w * 0.24f; // 傷の言葉 2 行（17 px）が収まる幅
        var columns = new[]
        {
            (red, game.Player, margin - (slide * columnWidth), RedTop, TextAnchor.Left),
            (blue, game.Cpu, w - margin + (slide * columnWidth), BlueCorner, TextAnchor.Right),
        };
        foreach (var (name, fighter, edge, color, anchor) in columns)
        {
            var figure = bodies.Of(fighter);
            var wounds = figure.Wounds();
            var lines = wounds.Count == 0 ? ["目立つ傷はない"] : wounds.Take(2).ToList();
            var left = anchor == TextAnchor.Left ? edge : edge - columnWidth;
            var bottom = figureTop + (FigureHeightUnits * unit) + 14f + (lines.Count * 22f);
            shapes.Add(Rect(left - 10f, figureTop - 48f, left + columnWidth + 10f, bottom, Black with { W = 0.55f * a }));
            shapes.Add(Rect(left - 10f, figureTop - 48f, left + columnWidth + 10f, figureTop - 44f, color with { W = a }));
            Shadowed(text, name, new Vector2(edge, figureTop - 40f), 24f, color with { W = a }, anchor);
            var figureLeft = anchor == TextAnchor.Left ? edge : edge - figureWidth;
            DrawFigure(shapes, figure, new Vector2(figureLeft, figureTop), unit, a, sunk: false);
            for (var i = 0; i < lines.Count; i++)
            {
                text.Add(new ScreenTextDraw(lines[i], new Vector2(edge, figureTop + (FigureHeightUnits * unit) + 10f + (i * 22f)), 17f, (wounds.Count == 0 ? Muted : Cream) with { W = a }, anchor));
            }
        }

        return new ShowDraw(text, shapes);
    }

    // ---- リプレイ ----

    public ShowDraw ReplayOverlay(int round, string description, float videoTime)
    {
        var w = (float)width;
        var h = (float)height;
        var text = new List<ScreenTextDraw>();
        var shapes = new List<UiShapeDraw>();

        // 画面の上下に細い赤い帯。左上に REWIND と巻き戻しの記号。
        shapes.Add(Rect(0f, 0f, w, 8f, RedTop));
        shapes.Add(Rect(0f, h - 8f, w, h, RedTop));
        BlockFont.Slab(shapes, -30f, 330f, 20f, 82f, 24f, Black with { W = 0.92f });
        BlockFont.Slab(shapes, -30f, 330f, 76f, 82f, 24f, RedTop);
        var blink = 0.5f + (0.5f * MathF.Sin(videoTime * 9f));
        BlockFont.Draw(shapes, "<<", new Vector2(22f, 34f), 4f, RedTop with { W = 0.45f + (0.55f * blink) });
        BlockFont.Draw(shapes, "REWIND", new Vector2(100f, 30f), 5f, Cream, 0.15f);
        text.Add(new ScreenTextDraw($"第{round}ラウンド　スロー", new Vector2(24f, 92f), 22f, Muted));

        // 下: 場面の説明。
        BlockFont.Slab(shapes, w * 0.06f, w * 0.94f, h - 84f, h - 24f, 28f, Black with { W = 0.88f });
        BlockFont.Slab(shapes, w * 0.06f, w * 0.94f, h - 88f, h - 84f, 28f, RedTop);
        Shadowed(text, description, new Vector2(w * 0.5f, h - 70f), 24f, Cream, TextAnchor.Center);
        return new ShowDraw(text, shapes);
    }

    public IReadOnlyList<UiShapeDraw> Wipe(float progress)
    {
        var w = (float)width;
        var h = (float)height;
        var p = Math.Clamp(progress, 0f, 1f);
        var x = Lerp(-0.3f * w, 1.35f * w, p);
        var skew = 0.2f * w;
        var shapes = new List<UiShapeDraw>();
        BlockFont.Slab(shapes, x - (0.3f * w), x - (0.08f * w), 0f, h, skew, Black with { W = 0.95f });
        BlockFont.Slab(shapes, x - (0.08f * w), x + (0.03f * w), 0f, h, skew, RedTop);
        BlockFont.Slab(shapes, x + (0.03f * w), x + (0.045f * w), 0f, h, skew, Cream);
        return shapes;
    }

    // ---- 決着 ----

    public ShowDraw Result(TvCard card, TvResult result, float t, BoxingGame game, ShowBodies bodies)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(bodies);
        var w = (float)width;
        var h = (float)height;
        var text = new List<ScreenTextDraw>();
        var shapes = new List<UiShapeDraw>();
        shapes.Add(Rect(0f, 0f, w, h, new Vector4(0.012f, 0.01f, 0.01f, 0.93f * Clamp01(t / 0.3f))));
        BlockFont.Slab(shapes, w * 0.5f, w + 0.2f * w, 0f, h, 0.2f * w, new Vector4(0.09f, 0.016f, 0.014f, Clamp01(t / 0.4f)));

        // 見出し。
        var head = Clamp01((t - 0.3f) / 0.3f);
        BlockFont.Slab(shapes, -30f, (result.Winner == 0 ? 440f : 330f) * EaseOut(head), h * 0.06f, (h * 0.06f) + 70f, 24f, RedDeep);
        BlockFont.Draw(shapes, result.Winner == 0 ? "FULL TIME" : "FINISH", new Vector2(w * 0.04f, (h * 0.06f) + 10f), 7f, Cream with { W = head }, 0.15f);
        if (result.Winner != 0)
        {
            Shadowed(text, org.Text.FinishWord, new Vector2((w * 0.04f) + 330f, (h * 0.06f) + 6f), 54f, Cream with { W = head });
        }

        var (redFigure, redSunk) = ResultFigure(bodies.Of(game.Player), result.Winner < 0, result);
        var (blueFigure, blueSunk) = ResultFigure(bodies.Of(game.Cpu), result.Winner > 0, result);
        var name = Clamp01((t - 0.7f) / 0.3f);
        if (result.Winner == 0)
        {
            var pop = BackOut(name);
            Shadowed(text, "両者生還", new Vector2(w * 0.04f, h * 0.22f), 128f * MathF.Max(0.01f, pop), Cream with { W = name });
            var line = org.Text.DecisionLine.Replace("{rounds}", card.Rules.Rounds.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
            var sub = Clamp01((t - 1.3f) / 0.4f);
            BlockFont.Slab(shapes, w * 0.04f - 20f, w * 0.04f + (w * 0.46f * EaseOut(sub)), (h * 0.22f) + 170f, (h * 0.22f) + 234f, 26f, Black with { W = 0.9f });
            Shadowed(text, line, new Vector2(w * 0.04f + 14f, (h * 0.22f) + 184f), 26f, Cream with { W = sub });
        }
        else
        {
            var winner = result.Winner > 0 ? card.Red : card.Blue;
            var color = result.Winner > 0 ? RedTop : BlueCorner;
            var corner = result.Winner > 0 ? "RED CORNER" : "BLUE CORNER";
            BlockFont.Draw(shapes, corner, new Vector2(w * 0.04f, h * 0.2f), 4f, color with { W = name }, 0.15f);
            var size = MathF.Min(128f, (w * 0.5f) / MathF.Max(1f, winner.Name.Length)) * MathF.Max(0.01f, BackOut(name));
            Shadowed(text, winner.Name, new Vector2(w * 0.04f, (h * 0.2f) + 38f), size, Cream with { W = name });

            var cause = Clamp01((t - 1.3f) / 0.4f);
            var phrase = result.DeathPhrase;
            if (phrase.Length > 0)
            {
                BlockFont.Slab(shapes, w * 0.04f - 20f, (w * 0.04f) + (phrase.Length * 56f) + 70f, (h * 0.2f) + 190f, (h * 0.2f) + 262f, 26f, RedDeep with { W = cause });
                Shadowed(text, phrase, new Vector2(w * 0.04f + 14f, (h * 0.2f) + 198f), 56f, Cream with { W = cause });
            }

            var minutes = (int)result.RoundTime / 60;
            var seconds = (int)result.RoundTime % 60;
            var timeText = string.Create(CultureInfo.InvariantCulture, $"R{result.Round} {minutes}:{seconds:00}");
            var time = Clamp01((t - 1.8f) / 0.4f);
            BlockFont.Draw(shapes, timeText, new Vector2(w * 0.04f, (h * 0.2f) + 300f), 8f, Cream with { W = time }, 0.15f);
        }

        // 両者の最後の体（敗者は灰色に沈める）。
        var unit = h * 0.5f / FigureHeightUnits;
        var figures = new[] { (card.Red.Name, redFigure, w * 0.585f, RedTop, redSunk), (card.Blue.Name, blueFigure, w * 0.79f, BlueCorner, blueSunk) };
        var figureIn = EaseOut(Clamp01((t - 1f) / 0.5f));
        foreach (var (who, figure, left, color, sunk) in figures)
        {
            var top = (h * 0.2f) + ((1f - figureIn) * 60f) + (sunk ? unit * 1.2f : 0f);
            DrawFigure(shapes, figure, new Vector2(left, top), unit, figureIn, sunk);
            Shadowed(text, who, new Vector2(left, (h * 0.2f) + (FigureHeightUnits * unit) + 22f), 28f, (sunk ? Muted : color) with { W = figureIn });
            var wounds = figure.Wounds();
            for (var i = 0; i < Math.Min(3, wounds.Count); i++)
            {
                text.Add(new ScreenTextDraw(wounds[i], new Vector2(left, (h * 0.2f) + (FigureHeightUnits * unit) + 60f + (i * 24f)), 18f, Muted with { W = figureIn }));
            }
        }

        return new ShowDraw(text, shapes);
    }

    /// <summary>
    /// 決着の画面の体の図。死で決まった試合（<see cref="TvResult.DeathPhrase"/>）の負けた側は、体の状態（試合の体が覚えている欠損）に
    /// 加えて決着の結果からも「死んだ側」と分かるので、全体を灰色に沈める。死因が首（頭が離れた）なら頭は輪郭だけ。
    /// 体の図の読み手が頭の離断を知らなくても（記録に無くても）、決着の文が言う死因と図がずれない。
    /// </summary>
    internal static (BodyFigure Figure, bool Sunk) ResultFigure(BodyFigure figure, bool loser, TvResult result)
    {
        ArgumentNullException.ThrowIfNull(figure);
        ArgumentNullException.ThrowIfNull(result);
        var diedHere = loser && result.Winner != 0 && result.DeathPhrase.Length > 0;
        if (diedHere && result.DeathOrgan == "首")
        {
            figure = figure.With(FigurePart.Head, figure[FigurePart.Head] with { Gone = true });
        }

        return (diedHere ? figure.WithDead(true) : figure, result.Winner != 0 && (diedHere || figure.Dead));
    }

    // ---- 小さな道具 ----

    private static UiShapeDraw Rect(float left, float top, float right, float bottom, Vector4 color)
        => new(new Vector2((left + right) * 0.5f, (top + bottom) * 0.5f), new Vector2(right - left, bottom - top), color);

    private static string Clock(float seconds)
    {
        var whole = (int)MathF.Ceiling(MathF.Max(0f, seconds));
        return string.Create(CultureInfo.InvariantCulture, $"{whole / 60}:{whole % 60:00}");
    }

    private static void Shadowed(List<ScreenTextDraw> text, string value, Vector2 at, float size, Vector4 color, TextAnchor anchor = TextAnchor.Left, float offset = 2f)
    {
        text.Add(new ScreenTextDraw(value, at + new Vector2(offset, offset), size, new Vector4(0f, 0f, 0f, 0.7f * color.W), anchor));
        text.Add(new ScreenTextDraw(value, at, size, color, anchor));
    }

    private static float Clamp01(float x) => Math.Clamp(x, 0f, 1f);

    private static float Lerp(float a, float b, float t) => a + ((b - a) * t);

    private static float EaseOut(float t) => 1f - MathF.Pow(1f - t, 3f);

    private static float BackOut(float t)
    {
        const float C = 1.70158f;
        var u = t - 1f;
        return 1f + ((C + 1f) * u * u * u) + (C * u * u);
    }
}
