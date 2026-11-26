using System.Globalization;
using System.Numerics;
using Aigis.Render;
using MiniRing.Highlights;
using MiniRing.Replay;

namespace MiniRing.Presentation;

/// <summary>シークのバーを描くのに要る、今の再生の様子。</summary>
/// <param name="Time">再生ヘッドの時刻 [s]。</param>
/// <param name="Duration">記録の長さ [s]。</param>
/// <param name="Scanned">走査が済んだ時刻 [s]（ここまでシークできる）。</param>
/// <param name="Rate">試合の秒 / 実時間の秒（止まっていれば 0）。</param>
/// <param name="Speed">速さの大きさ（倍）。</param>
/// <param name="LoopStart">A（無ければ null）。</param>
/// <param name="LoopEnd">B（無ければ null）。</param>
/// <param name="Camera">カメラの方式の短い説明（画面の右上に出す）。</param>
/// <param name="Verdict">記録との照合（一致 / ずれ）。</param>
/// <param name="Hover">マウスの位置（バーの上なら時刻と近くの印を出す）。</param>
/// <param name="RedLabel">赤の名前。</param>
/// <param name="BlueLabel">青の名前。</param>
public readonly record struct ReplayHudState(
    float Time, float Duration, float Scanned, float Rate, float Speed, float? LoopStart, float? LoopEnd,
    string Camera, string Verdict, Vector2? Hover, string RedLabel, string BlueLabel);

/// <summary>
/// リプレイの画面の下のパネル: 時刻・速さ・状態の行と、試合の展開が見えるシークのバー。
/// 上から「見込み（どちらが勝ちそうか）」「意識」「スタミナ」「体の残り」のグラフ、打撃と見どころの印、再生の位置のバー。
/// ラウンドの区切り・休み・ダウンの間は全段に帯で出す。描画だけ（試合には触れない）。
/// </summary>
public static class ReplayHud
{
    /// <summary>パネルの高さ [px]。</summary>
    public const float PanelHeight = 214f;

    private const float LabelWidth = 86f;
    private const float RightMargin = 24f;
    private const float HeaderHeight = 30f;
    private const float BeliefHeight = 22f;
    private const float GraphHeight = 26f;
    private const float LaneGap = 4f;
    private const float MarkHeight = 30f;
    private const float TrackHeight = 12f;
    private const float ColumnPixels = 3f;
    private const float HighlightPickPixels = 9f;

    public static readonly Vector4 Red = new(1f, 0.45f, 0.4f, 1f);
    public static readonly Vector4 Blue = new(0.5f, 0.65f, 1f, 1f);
    private static readonly Vector4 White = new(1f, 1f, 1f, 1f);
    private static readonly Vector4 Dim = new(0.72f, 0.74f, 0.78f, 1f);
    private static readonly Vector4 Gold = new(1f, 0.82f, 0.3f, 1f);
    private static readonly Vector4 PanelFill = new(0.04f, 0.05f, 0.07f, 0.92f);
    private static readonly Vector4 LaneFill = new(1f, 1f, 1f, 0.05f);

    /// <summary>パネルの中の段の位置。</summary>
    public readonly record struct Layout(float Left, float Right, float Top, float BeliefTop, float GraphTop, float MarkTop, float TrackTop, float Bottom)
    {
        public float Width => MathF.Max(1f, Right - Left);

        public float X(float time, float duration) => Left + (Width * Math.Clamp(duration > 0f ? time / duration : 0f, 0f, 1f));

        public float Time(float x, float duration) => Math.Clamp((x - Left) / Width, 0f, 1f) * duration;

        /// <summary>グラフの何段目（0 = 意識、1 = スタミナ、2 = 体）の上端。</summary>
        public float GraphLane(int lane) => GraphTop + (lane * (GraphHeight + LaneGap));
    }

    public static Layout Measure(Vector2 screen)
    {
        var top = screen.Y - PanelHeight;
        var beliefTop = top + HeaderHeight;
        var graphTop = beliefTop + BeliefHeight + LaneGap;
        var markTop = graphTop + (3 * (GraphHeight + LaneGap));
        var trackTop = markTop + MarkHeight + LaneGap;
        return new Layout(LabelWidth, screen.X - RightMargin, top, beliefTop, graphTop, markTop, trackTop, trackTop + TrackHeight);
    }

    /// <summary>マウスがバーの段（見込み〜位置のバー）の上なら、その時刻。</summary>
    public static float? TimeAt(Vector2 mouse, Vector2 screen, float duration)
    {
        var l = Measure(screen);
        return mouse.Y >= l.BeliefTop - 2f && mouse.Y <= l.Bottom + 6f && mouse.X >= l.Left - 6f && mouse.X <= l.Right + 6f
            ? l.Time(mouse.X, duration)
            : null;
    }

    /// <summary>マウスの近くの見どころの印（印の段の上で、横に数 px 以内）。</summary>
    public static ReplayMark? MarkAt(Vector2 mouse, Vector2 screen, float duration, IReadOnlyList<ReplayMark> marks)
    {
        var l = Measure(screen);
        if (mouse.Y < l.MarkTop - 4f || mouse.Y > l.TrackTop + 2f) { return null; }
        ReplayMark? best = null;
        var bestDistance = HighlightPickPixels;
        foreach (var m in marks)
        {
            if (m.Kind != ReplayMarkKind.Highlight) { continue; }
            var d = MathF.Abs(l.X(m.Time, duration) - mouse.X);
            if (d < bestDistance || (best is { } b && MathF.Abs(d - bestDistance) < 0.5f && ReplayTimeline.Weighted(m) > ReplayTimeline.Weighted(b)))
            {
                best = m;
                bestDistance = d;
            }
        }

        return best;
    }

    /// <summary>パネルの図形と文字。</summary>
    public static (List<UiShapeDraw> Shapes, List<ScreenTextDraw> Text) Draw(Vector2 screen, ReplayHudState s,
        IReadOnlyList<ReplaySample> samples, IReadOnlyList<ReplayMark> marks)
    {
        var shapes = new List<UiShapeDraw>(1600);
        var text = new List<ScreenTextDraw>(64);
        var l = Measure(screen);
        var duration = MathF.Max(1e-3f, s.Duration);
        shapes.Add(new UiShapeDraw(new Vector2(screen.X * 0.5f, l.Top + (PanelHeight * 0.5f)), new Vector2(screen.X, PanelHeight), PanelFill));

        Header(text, screen, l, s);
        Lanes(shapes, text, l);
        Bands(shapes, l, samples, duration);
        Graphs(shapes, l, samples, duration);
        Marks(shapes, text, l, marks, duration);
        Track(shapes, text, l, s, duration);
        Hover(shapes, text, screen, l, s, marks, duration);
        return (shapes, text);
    }

    private static void Header(List<ScreenTextDraw> text, Vector2 screen, Layout l, ReplayHudState s)
    {
        var y = l.Top + 6f;
        var play = s.Rate > 0f ? "再生" : s.Rate < 0f ? "巻き戻し" : "一時停止";
        var speed = s.Speed.ToString(s.Speed < 0.1f ? "0.00" : s.Speed < 1f ? "0.##" : "0", CultureInfo.InvariantCulture);
        var head = $"{play}  {Clock(s.Time)} / {Clock(s.Duration)}   {speed}x";
        text.Add(new ScreenTextDraw(head, new Vector2(16f, y), 20f, White));
        var info = new List<string>();
        if (s.LoopStart is { } a) { info.Add($"A {Clock(a)}"); }
        if (s.LoopEnd is { } b) { info.Add($"B {Clock(b)}"); }
        if (s.Scanned < s.Duration - 0.05f) { info.Add($"読み込み {100f * s.Scanned / MathF.Max(1e-3f, s.Duration):0}%"); }
        info.Add(s.Verdict);
        text.Add(new ScreenTextDraw(string.Join("   ", info), new Vector2(screen.X * 0.5f, y + 2f), 16f, Dim, TextAnchor.Center));
        text.Add(new ScreenTextDraw($"{s.Camera}   [H] 操作の一覧", new Vector2(screen.X - RightMargin, y + 2f), 16f, Dim, TextAnchor.Right));
    }

    private static void Lanes(List<UiShapeDraw> shapes, List<ScreenTextDraw> text, Layout l)
    {
        void Lane(float top, float height, string label)
        {
            shapes.Add(new UiShapeDraw(new Vector2((l.Left + l.Right) * 0.5f, top + (height * 0.5f)), new Vector2(l.Width, height), LaneFill));
            text.Add(new ScreenTextDraw(label, new Vector2(l.Left - 10f, top + (height * 0.5f) - 8f), 14f, Dim, TextAnchor.Right));
        }

        Lane(l.BeliefTop, BeliefHeight, "見込み");
        Lane(l.GraphLane(0), GraphHeight, "意識");
        Lane(l.GraphLane(1), GraphHeight, "スタミナ");
        Lane(l.GraphLane(2), GraphHeight, "体");
        Lane(l.MarkTop, MarkHeight, "出来事");
        // 見込みの中央線（五分五分）。
        shapes.Add(new UiShapeDraw(new Vector2((l.Left + l.Right) * 0.5f, l.BeliefTop + (BeliefHeight * 0.5f)), new Vector2(l.Width, 1f), new Vector4(1f, 1f, 1f, 0.25f)));
    }

    /// <summary>休み（灰）とダウン（その選手の色）の帯。グラフの段に重ねる。</summary>
    private static void Bands(List<UiShapeDraw> shapes, Layout l, IReadOnlyList<ReplaySample> samples, float duration)
    {
        var top = l.BeliefTop;
        var bottom = l.MarkTop + MarkHeight;
        void Band(float from, float to, Vector4 color)
        {
            var x0 = l.X(from, duration);
            var x1 = MathF.Max(x0 + 1f, l.X(to, duration));
            shapes.Add(new UiShapeDraw(new Vector2((x0 + x1) * 0.5f, (top + bottom) * 0.5f), new Vector2(x1 - x0, bottom - top), color));
        }

        void Runs(Func<ReplaySample, bool> on, Vector4 color)
        {
            float? start = null;
            for (var i = 0; i < samples.Count; i++)
            {
                if (on(samples[i])) { start ??= samples[i].Time; }
                else if (start is { } s0) { Band(s0, samples[i].Time, color); start = null; }
            }

            if (start is { } open && samples.Count > 0) { Band(open, samples[^1].Time, color); }
        }

        Runs(s => s.Break, new Vector4(0.6f, 0.6f, 0.6f, 0.16f));
        Runs(s => s.Red.Down, Red with { W = 0.22f });
        Runs(s => s.Blue.Down, Blue with { W = 0.22f });
    }

    private static void Graphs(List<UiShapeDraw> shapes, Layout l, IReadOnlyList<ReplaySample> samples, float duration)
    {
        if (samples.Count == 0) { return; }
        var columns = Math.Max(1, (int)(l.Width / ColumnPixels));
        var index = 0;
        for (var c = 0; c < columns; c++)
        {
            var t1 = duration * (c + 1) / columns;
            var first = index;
            while (index < samples.Count && samples[index].Time < t1) { index++; }
            if (index == first) { continue; } // まだ走査していない・記録の無い列
            var count = index - first;
            float Mean(Func<ReplaySample, float> f)
            {
                var sum = 0f;
                for (var i = first; i < index; i++) { sum += f(samples[i]); }
                return sum / count;
            }

            var x = l.Left + ((c + 0.5f) * l.Width / columns);
            var w = (l.Width / columns) + 0.5f;
            // 見込み: 中央から赤（上）か青（下）へ伸びる柱。
            var p = Mean(s => s.RedWinChance) - 0.5f;
            var mid = l.BeliefTop + (BeliefHeight * 0.5f);
            var h = MathF.Abs(p) * BeliefHeight;
            if (h >= 0.5f)
            {
                shapes.Add(new UiShapeDraw(new Vector2(x, p > 0f ? mid - (h * 0.5f) : mid + (h * 0.5f)), new Vector2(w, h), (p > 0f ? Red : Blue) with { W = 0.75f }));
            }

            Line(shapes, l, 0, x, w, Mean(s => s.Red.Consciousness), Mean(s => s.Blue.Consciousness));
            Line(shapes, l, 1, x, w, Mean(s => s.Red.Stamina), Mean(s => s.Blue.Stamina));
            Line(shapes, l, 2, x, w, Mean(s => s.Red.Body), Mean(s => s.Blue.Body));
        }
    }

    /// <summary>1 列ぶんの 2 本の線（値 0..1 を段の高さへ）。</summary>
    private static void Line(List<UiShapeDraw> shapes, Layout l, int lane, float x, float w, float red, float blue)
    {
        var top = l.GraphLane(lane) + 2f;
        var span = GraphHeight - 4f;
        shapes.Add(new UiShapeDraw(new Vector2(x, top + (span * (1f - Math.Clamp(blue, 0f, 1f)))), new Vector2(w, 2.5f), Blue));
        shapes.Add(new UiShapeDraw(new Vector2(x, top + (span * (1f - Math.Clamp(red, 0f, 1f)))), new Vector2(w, 2.5f), Red));
    }

    private static void Marks(List<UiShapeDraw> shapes, List<ScreenTextDraw> text, Layout l, IReadOnlyList<ReplayMark> marks, float duration)
    {
        var mid = l.MarkTop + (MarkHeight * 0.5f);
        var lastLabel = float.NegativeInfinity;
        foreach (var m in marks)
        {
            var x = l.X(m.Time, duration);
            switch (m.Kind)
            {
                case ReplayMarkKind.RoundStart:
                    shapes.Add(new UiShapeDraw(new Vector2(x, (l.BeliefTop + l.Bottom) * 0.5f), new Vector2(1.5f, l.Bottom - l.BeliefTop), new Vector4(1f, 1f, 1f, 0.45f)));
                    text.Add(new ScreenTextDraw(m.Note, new Vector2(x + 4f, l.MarkTop + 1f), 12f, Dim));
                    break;
                case ReplayMarkKind.Hit:
                    // 打撃: 打った側の色の細い線。上半分が赤、下半分が青。長さはエネルギー（強打 2 J で半分の段いっぱい）。
                    var h = MathF.Max(3f, MathF.Min(1f, MathF.Sqrt(MathF.Max(0f, m.Strength) / 2f)) * ((MarkHeight * 0.5f) - 2f));
                    shapes.Add(new UiShapeDraw(new Vector2(x, m.Red ? mid - (h * 0.5f) : mid + (h * 0.5f)), new Vector2(2f, h), (m.Red ? Red : Blue) with { W = 0.8f }));
                    break;
            }
        }

        // 見どころは打撃の上に、目立つ物ほど大きく描く。大きい物には短い名前を添える（重ならない分だけ）。
        foreach (var m in marks.Where(m => m.Kind == ReplayMarkKind.Highlight).OrderBy(ReplayTimeline.Weighted))
        {
            var x = l.X(m.Time, duration);
            var weight = ReplayTimeline.Weighted(m);
            var size = Math.Clamp(5f + (weight * 0.9f), 6f, 16f);
            var color = KindColor(m.Highlight);
            shapes.Add(new UiShapeDraw(new Vector2(x, mid), new Vector2(size), color, new Vector4(size * 0.3f), m.Red ? Red : Blue, 1.5f));
            if (weight >= 6f && x - lastLabel > 54f)
            {
                text.Add(new ScreenTextDraw(KindLabel(m.Highlight), new Vector2(x, l.MarkTop - 1f), 12f, color, TextAnchor.Center));
                lastLabel = x;
            }
        }
    }

    private static void Track(List<UiShapeDraw> shapes, List<ScreenTextDraw> text, Layout l, ReplayHudState s, float duration)
    {
        var y = l.TrackTop + (TrackHeight * 0.5f);
        shapes.Add(UiShapeDraw.Rounded(new Vector2((l.Left + l.Right) * 0.5f, y), new Vector2(l.Width, TrackHeight), 4f, new Vector4(1f, 1f, 1f, 0.08f)));
        var scanned = l.X(s.Scanned, duration);
        shapes.Add(UiShapeDraw.Rounded(new Vector2((l.Left + scanned) * 0.5f, y), new Vector2(MathF.Max(1f, scanned - l.Left), TrackHeight), 4f, new Vector4(1f, 1f, 1f, 0.16f)));
        var played = l.X(s.Time, duration);
        shapes.Add(UiShapeDraw.Rounded(new Vector2((l.Left + played) * 0.5f, y), new Vector2(MathF.Max(1f, played - l.Left), TrackHeight), 4f, new Vector4(0.95f, 0.8f, 0.35f, 0.55f)));
        if (s.LoopStart is { } a)
        {
            var xa = l.X(a, duration);
            var xb = s.LoopEnd is { } b ? l.X(b, duration) : xa + 2f;
            shapes.Add(new UiShapeDraw(new Vector2((xa + xb) * 0.5f, (l.BeliefTop + l.Bottom) * 0.5f), new Vector2(MathF.Max(2f, xb - xa), l.Bottom - l.BeliefTop), new Vector4(0.4f, 1f, 0.6f, 0.10f)));
            text.Add(new ScreenTextDraw("A", new Vector2(xa, l.Bottom + 1f), 13f, new Vector4(0.5f, 1f, 0.65f, 1f), TextAnchor.Center));
            if (s.LoopEnd is not null) { text.Add(new ScreenTextDraw("B", new Vector2(xb, l.Bottom + 1f), 13f, new Vector4(0.5f, 1f, 0.65f, 1f), TextAnchor.Center)); }
        }

        // 再生ヘッド: 全段を通る線と、位置のバーの上のつまみ。
        shapes.Add(new UiShapeDraw(new Vector2(played, (l.BeliefTop + l.Bottom) * 0.5f), new Vector2(2f, l.Bottom - l.BeliefTop + 6f), White));
        shapes.Add(UiShapeDraw.Rounded(new Vector2(played, y), new Vector2(14f, 18f), 5f, White));
    }

    private static void Hover(List<UiShapeDraw> shapes, List<ScreenTextDraw> text, Vector2 screen, Layout l, ReplayHudState s,
        IReadOnlyList<ReplayMark> marks, float duration)
    {
        if (s.Hover is not { } mouse || TimeAt(mouse, screen, duration) is not { } time) { return; }
        var x = l.X(time, duration);
        shapes.Add(new UiShapeDraw(new Vector2(x, (l.BeliefTop + l.Bottom) * 0.5f), new Vector2(1f, l.Bottom - l.BeliefTop), new Vector4(1f, 1f, 1f, 0.5f)));
        var label = Clock(time);
        if (MarkAt(mouse, screen, duration, marks) is { } mark)
        {
            label = $"{Clock(mark.Time)}  {KindLabel(mark.Highlight)}（{(mark.Red ? s.RedLabel : s.BlueLabel)}）  クリックで飛ぶ";
        }

        var width = 20f + label.Sum(c => c < 0x2E80 ? 8.5f : 15.5f); // 半角と全角の幅の目安（15px の文字）
        var cx = Math.Clamp(x, (width * 0.5f) + 4f, screen.X - (width * 0.5f) - 4f);
        var cy = l.Top - 62f; // 試合の HUD の最後の打撃の行（枠の下端から 44 px）より上。文字は図形の上に描かれるので、重ねずに避ける
        shapes.Add(UiShapeDraw.Rounded(new Vector2(cx, cy), new Vector2(width, 26f), 6f, new Vector4(0.03f, 0.03f, 0.04f, 1f)));
        text.Add(new ScreenTextDraw(label, new Vector2(cx, cy - 9f), 15f, White, TextAnchor.Center));
    }

    /// <summary>操作の一覧（[H] で出す）。</summary>
    public static (List<UiShapeDraw> Shapes, List<ScreenTextDraw> Text) Help(Vector2 screen, IReadOnlyList<(string Keys, string Action)> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var shapes = new List<UiShapeDraw>();
        var text = new List<ScreenTextDraw>();
        var half = (rows.Count + 1) / 2;
        const float rowHeight = 24f;
        const float columnWidth = 470f;
        var width = MathF.Min(screen.X - 40f, (2f * columnWidth) + 40f);
        var height = 70f + (half * rowHeight);
        var top = MathF.Max(16f, ((screen.Y - PanelHeight) * 0.5f) - (height * 0.5f));
        var left = (screen.X - width) * 0.5f;
        shapes.Add(UiShapeDraw.Rounded(new Vector2(screen.X * 0.5f, top + (height * 0.5f)), new Vector2(width, height), 12f, new Vector4(0.02f, 0.03f, 0.05f, 0.9f)));
        text.Add(new ScreenTextDraw("リプレイの操作  （H / F1 で閉じる）", new Vector2(screen.X * 0.5f, top + 14f), 22f, Gold, TextAnchor.Center));
        for (var i = 0; i < rows.Count; i++)
        {
            var column = i / half;
            var row = i % half;
            var x = left + 24f + (column * (width * 0.5f));
            var y = top + 54f + (row * rowHeight);
            text.Add(new ScreenTextDraw(rows[i].Keys, new Vector2(x, y), 16f, White));
            text.Add(new ScreenTextDraw(rows[i].Action, new Vector2(x + 170f, y), 16f, Dim));
        }

        return (shapes, text);
    }

    public static string Clock(float seconds)
    {
        seconds = MathF.Round(MathF.Max(0f, seconds) * 10f) / 10f; // 0.1 秒に丸めてから分を取る（59.96 → 1:00.0）
        var minutes = (int)(seconds / 60f);
        return string.Create(CultureInfo.InvariantCulture, $"{minutes}:{seconds - (minutes * 60f):00.0}");
    }

    public static string KindLabel(HighlightKind kind) => kind switch
    {
        HighlightKind.Exchange => "打ち合い",
        HighlightKind.NearMiss => "紙一重",
        HighlightKind.Graze => "皮一枚",
        HighlightKind.Counter => "カウンター",
        HighlightKind.Flurry => "連打",
        HighlightKind.Knockdown => "ダウン",
        HighlightKind.Rise => "立ち上がり",
        HighlightKind.KO => "KO",
        HighlightKind.OnePunch => "一撃",
        HighlightKind.BoneBreak => "骨折",
        HighlightKind.LimbLoss => "切断",
        HighlightKind.VoxelLoss => "大きな欠け",
        HighlightKind.Shatter => "砕け",
        HighlightKind.HeadSnap => "首が持っていかれる",
        _ => kind.ToString(),
    };

    /// <summary>見どころの色（種類のまとまりごと: 決着は金、壊れは橙、技は水色、手数は白、削れは紫）。</summary>
    public static Vector4 KindColor(HighlightKind kind) => kind switch
    {
        HighlightKind.Knockdown or HighlightKind.KO or HighlightKind.OnePunch or HighlightKind.Rise => Gold,
        HighlightKind.BoneBreak or HighlightKind.LimbLoss => new Vector4(1f, 0.45f, 0.15f, 1f),
        HighlightKind.Counter or HighlightKind.NearMiss or HighlightKind.Graze => new Vector4(0.35f, 0.95f, 1f, 1f),
        HighlightKind.Exchange or HighlightKind.Flurry => new Vector4(0.92f, 0.92f, 0.92f, 1f),
        _ => new Vector4(0.8f, 0.5f, 1f, 1f),
    };
}
