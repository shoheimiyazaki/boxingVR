using System.Globalization;
using System.Numerics;
using Aigis.Render;

namespace MiniRing;

/// <summary>
/// テレビ中継風の画面表示（描画だけ。試合には響かない）。左上に中継の得点表（両者の名前・コーナーの色・スタミナのメーター）と、
/// その右にラウンドと時計（ラウンド中は残り時間、休みは BREAK と残り秒、試合の後は FINAL）。右上に LIVE の札。
/// スタミナのメーター（<see cref="StaminaBar(Vector2, float, float, Fighter)"/>）は、テレビ中継でない画面でも名前の下に出す。
/// </summary>
public static class TvHud
{
    private const float Left = 24f;
    private const float Top = 20f;
    private const float RowHeight = 34f;
    private const float NameWidth = 190f;
    private const float BarWidth = 150f;
    private const float ClockWidth = 118f;
    private static readonly Vector4 Panel = new(0.05f, 0.06f, 0.09f, 0.82f);
    private static readonly Vector4 ClockPanel = new(0.92f, 0.92f, 0.94f, 0.95f);
    private static readonly Vector4 Ink = new(0.08f, 0.08f, 0.1f, 1f);
    private static readonly Vector4 White = new(1f, 1f, 1f, 1f);
    private static readonly Vector4 Red = new(0.9f, 0.18f, 0.16f, 1f);
    private static readonly Vector4 Blue = new(0.2f, 0.42f, 0.95f, 1f);
    private static readonly Vector4 FineZone = new(0.3f, 0.9f, 0.4f, 1f);
    private static readonly Vector4 SluggishZone = new(0.98f, 0.8f, 0.15f, 1f);
    private static readonly Vector4 DangerZone = new(0.95f, 0.2f, 0.18f, 1f);
    private static readonly Vector4 TickColor = new(1f, 1f, 1f, 0.9f);

    /// <summary>空の溝に敷く区域の色の濃さ（帯より十分薄く、どこが空かは読める）。</summary>
    private const float ZoneTint = 0.28f;

    /// <summary>
    /// <paramref name="fighter"/> のスタミナのメーター。区切りは体の決まりそのもの: 拳が鈍り始める <see cref="Fighter.SluggishBelow"/>
    /// と、息切れの警告（点滅）が始まる <see cref="TiredLook.DangerBelow"/>。残りが少なければ息の拍子で点滅（<see cref="TiredLook.MeterFlash"/>）。
    /// </summary>
    public static IEnumerable<UiShapeDraw> StaminaBar(Vector2 leftCenter, float width, float height, Fighter fighter)
    {
        ArgumentNullException.ThrowIfNull(fighter);
        return StaminaBar(leftCenter, width, height, fighter.Stamina, fighter.SluggishBelow, TiredLook.DangerBelow, TiredLook.MeterFlash(fighter));
    }

    /// <summary>
    /// スタミナのメーター。溝を 3 つの区域に分けて薄く色を敷く（右から: 問題ない = 緑、<paramref name="sluggishBelow"/> より下の
    /// 動きが鈍る = 黄、<paramref name="dangerBelow"/> より下のヤバい = 赤）。区切りに白い目盛り。帯は今いる区域の色で塗る
    /// （どの区域かが色だけで読める）。<paramref name="flash"/>（0..1）だけ帯を白へ寄せ、溝の縁を赤く光らせる（尽きかけの警告）。
    /// </summary>
    public static IEnumerable<UiShapeDraw> StaminaBar(Vector2 leftCenter, float width, float height, float stamina, float sluggishBelow, float dangerBelow, float flash = 0f)
    {
        var value = Math.Clamp(stamina, 0f, 1f);
        var warn = Math.Clamp(flash, 0f, 1f);
        var danger = Math.Clamp(dangerBelow, 0f, 1f);
        var sluggish = Math.Clamp(sluggishBelow, danger, 1f);
        if (warn > 0.01f)
        {
            var glow = new Vector2(6f, 6f) * warn;
            yield return UiShapeDraw.Rounded(leftCenter + new Vector2(width * 0.5f, 0f), new Vector2(width, height) + glow, (height + glow.Y) * 0.5f, new Vector4(1f, 0.15f, 0.1f, 0.85f * warn));
        }

        yield return UiShapeDraw.Rounded(leftCenter + new Vector2(width * 0.5f, 0f), new Vector2(width, height), height * 0.5f, new Vector4(0f, 0f, 0f, 0.6f));
        foreach (var (from, to, color) in new[] { (0f, danger, DangerZone), (danger, sluggish, SluggishZone), (sluggish, 1f, FineZone) })
        {
            if (to - from > 1e-3f)
            {
                yield return Span(leftCenter, width, height, from, to, color with { W = ZoneTint });
            }
        }

        if (value > 0.005f)
        {
            var fill = value < danger ? DangerZone : value < sluggish ? SluggishZone : FineZone;
            fill = Vector4.Lerp(fill, Vector4.One, 0.7f * warn);
            yield return Span(leftCenter, width, height, 0f, MathF.Max(value, height / MathF.Max(1f, width)), fill);
        }

        foreach (var tick in new[] { danger, sluggish })
        {
            if (tick is > 0f and < 1f)
            {
                yield return UiShapeDraw.Rounded(leftCenter + new Vector2(width * tick, 0f), new Vector2(2f, height + 4f), 1f, TickColor);
            }
        }
    }

    /// <summary>メーターの <paramref name="from"/>..<paramref name="to"/>（0..1）の区間の帯（両端は丸く: 溝の丸みに合わせる）。</summary>
    private static UiShapeDraw Span(Vector2 leftCenter, float width, float height, float from, float to, Vector4 color)
    {
        var w = width * (to - from);
        return UiShapeDraw.Rounded(leftCenter + new Vector2(width * (from + to) * 0.5f, 0f), new Vector2(w, height), MathF.Min(height, w) * 0.5f, color);
    }

    /// <summary>得点表と時計の図形（文字の下に描かれる）。</summary>
    public static IEnumerable<UiShapeDraw> Shapes(BoxingGame game, Vector2 screen)
    {
        ArgumentNullException.ThrowIfNull(game);
        var width = NameWidth + BarWidth + 24f;
        var height = RowHeight * 2f;
        yield return UiShapeDraw.Rounded(new Vector2(Left + (width * 0.5f), Top + (height * 0.5f)), new Vector2(width, height), 6f, Panel);
        foreach (var (fighter, row, color) in new[] { (game.Player, 0, Red), (game.Cpu, 1, Blue) })
        {
            var y = Top + (RowHeight * (row + 0.5f));
            yield return UiShapeDraw.Rounded(new Vector2(Left + 4f, y), new Vector2(8f, RowHeight - 6f), 2f, color);
            foreach (var bar in StaminaBar(new Vector2(Left + NameWidth, y), BarWidth, 10f, fighter))
            {
                yield return bar;
            }
        }

        var clockCenter = new Vector2(Left + width + 6f + (ClockWidth * 0.5f), Top + (height * 0.5f));
        yield return UiShapeDraw.Rounded(clockCenter, new Vector2(ClockWidth, height), 6f, ClockPanel);
        yield return UiShapeDraw.Rounded(new Vector2(screen.X - 60f, Top + 14f), new Vector2(72f, 26f), 4f, Red);
    }

    /// <summary>得点表と時計の文字。<paramref name="red"/> / <paramref name="blue"/> は両者の名前。</summary>
    public static IEnumerable<ScreenTextDraw> Text(BoxingGame game, Vector2 screen, string red, string blue)
    {
        ArgumentNullException.ThrowIfNull(game);
        var width = NameWidth + BarWidth + 24f;
        yield return new ScreenTextDraw(red.ToUpperInvariant(), new Vector2(Left + 18f, Top + 6f), 22f, White);
        yield return new ScreenTextDraw(blue.ToUpperInvariant(), new Vector2(Left + 18f, Top + RowHeight + 6f), 22f, White);
        var clockX = Left + width + 6f + (ClockWidth * 0.5f);
        var rounds = game.Rules?.Rounds ?? 0;
        var (label, clock) = game.MatchOver
            ? ("FINAL", rounds > 0 ? $"R{game.MatchRound}" : "")
            : game.InBreak
                ? ("BREAK", Seconds(game.BreakLeft))
                : ($"ROUND {game.MatchRound}/{rounds}", Seconds(game.RoundLeft));
        yield return new ScreenTextDraw(label, new Vector2(clockX, Top + 4f), 18f, Ink, TextAnchor.Center);
        yield return new ScreenTextDraw(clock, new Vector2(clockX, Top + 26f), 32f, Ink, TextAnchor.Center);
        yield return new ScreenTextDraw("LIVE", new Vector2(screen.X - 60f, Top + 3f), 20f, White, TextAnchor.Center);
    }

    /// <summary>残り秒を 0:23 の形に（切り上げ）。</summary>
    private static string Seconds(float seconds)
    {
        var whole = (int)MathF.Ceiling(MathF.Max(0f, seconds));
        return string.Create(CultureInfo.InvariantCulture, $"{whole / 60}:{whole % 60:00}");
    }
}
