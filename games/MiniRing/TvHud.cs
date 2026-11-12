using System.Globalization;
using System.Numerics;
using Aigis.Render;

namespace MiniRing;

/// <summary>
/// テレビ中継風の画面表示（描画だけ。試合には響かない）。左上に中継の得点表（両者の名前・コーナーの色・スタミナのメーター）と、
/// その右にラウンドと時計（ラウンド中は残り時間、休みは BREAK と残り秒、試合の後は FINAL）。右上に LIVE の札。
/// スタミナのメーター（<see cref="StaminaBar"/>）は、テレビ中継でない画面でも名前の下に出す。
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

    /// <summary>スタミナのメーター: 暗い溝に、残りの分だけ色の帯（多いと緑、半分で黄、少ないと赤）。</summary>
    public static IEnumerable<UiShapeDraw> StaminaBar(Vector2 leftCenter, float width, float height, float stamina)
    {
        var value = Math.Clamp(stamina, 0f, 1f);
        yield return UiShapeDraw.Rounded(leftCenter + new Vector2(width * 0.5f, 0f), new Vector2(width, height), height * 0.5f, new Vector4(0f, 0f, 0f, 0.55f));
        if (value > 0.005f)
        {
            var fill = value > 0.5f
                ? Vector4.Lerp(new Vector4(0.98f, 0.85f, 0.2f, 1f), new Vector4(0.3f, 0.9f, 0.4f, 1f), (value - 0.5f) * 2f)
                : Vector4.Lerp(new Vector4(0.95f, 0.25f, 0.2f, 1f), new Vector4(0.98f, 0.85f, 0.2f, 1f), value * 2f);
            var w = MathF.Max(height, width * value);
            yield return UiShapeDraw.Rounded(leftCenter + new Vector2(w * 0.5f, 0f), new Vector2(w, height), height * 0.5f, fill);
        }
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
            foreach (var bar in StaminaBar(new Vector2(Left + NameWidth, y), BarWidth, 10f, fighter.Stamina))
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
