using System.Globalization;
using System.Numerics;
using Aigis.Render;

namespace MiniRing;

/// <summary>
/// 審判の 10 カウントの表示（HUD。描画だけで試合には響かない）。倒れた方の名前と、画面の真ん中に大きな数字（1 秒ごとに増え、
/// 増えた瞬間に大きく打ち出して縮む）、その下に 10 個の目盛り。ゲームの HUD と動画（miniring-arena）で同じものを使う。
/// </summary>
public static class DownCountHud
{
    /// <summary>名前の札（「YOU DOWN」の YOU の部分）が出始める、倒れてからの時間 [s]。転んだだけの一瞬は出さない。</summary>
    public const float ShowAfter = 0.5f;

    private const float NumberSize = 96f;
    private const float PipSize = 16f;
    private const float PipGap = 8f;
    private static readonly Vector4 Gold = new(1f, 0.85f, 0.3f, 1f);
    private static readonly Vector4 PipOff = new(1f, 1f, 1f, 0.18f);
    private static readonly Vector4 Backdrop = new(0f, 0f, 0f, 0.38f);

    /// <summary>今の数え（0 = まだ 1 秒経っていない、最大 10）。</summary>
    public static int Count(Fighter fighter)
    {
        ArgumentNullException.ThrowIfNull(fighter);
        return (int)MathF.Min(BoxingGame.CountSeconds, MathF.Floor(fighter.DownTime));
    }

    /// <summary>数えている（倒れて <see cref="ShowAfter"/> を過ぎ、ラウンドが終わっていない）ファイターと、その表示の中心。</summary>
    private static IEnumerable<(Fighter Fighter, Vector2 Center)> Counted(BoxingGame game, Vector2 screen)
    {
        if (game.RoundOver)
        {
            yield break;
        }

        var down = new[] { game.Player, game.Cpu }.Where(f => f.DownTime > ShowAfter).ToArray();
        for (var i = 0; i < down.Length; i++)
        {
            // 両方倒れたら左右に分ける（赤が左）。
            var x = down.Length == 1 ? 0.5f : (down[i] == game.Player ? 0.36f : 0.64f);
            yield return (down[i], new Vector2(screen.X * x, screen.Y * 0.26f)); // 上寄せ: 倒れた体（もがく様子）を隠さない
        }
    }

    /// <summary>カウントの文字（名前と数字）。</summary>
    public static IEnumerable<ScreenTextDraw> Text(BoxingGame game, Vector2 screen, Func<Fighter, string> name, Func<Fighter, Vector4> color)
    {
        ArgumentNullException.ThrowIfNull(game);
        foreach (var (fighter, center) in Counted(game, screen))
        {
            // 文字の Y は上端。数字の縦の中心を center に置き、名前はその上、目盛りはその下。
            yield return new ScreenTextDraw($"{name(fighter)} DOWN", center - new Vector2(0f, (NumberSize * 0.5f) + 40f), 30f, color(fighter), TextAnchor.Center);
            var count = Count(fighter);
            if (count >= 1)
            {
                // 数が増えた瞬間に 1.4 倍で打ち出し、0.25 秒ほどで元の大きさへ。
                var since = fighter.DownTime - MathF.Floor(fighter.DownTime);
                var size = NumberSize * (1f + (0.4f * MathF.Exp(-since * 10f)));
                var alpha = 0.75f + (0.25f * MathF.Exp(-since * 4f));
                yield return new ScreenTextDraw(count.ToString(CultureInfo.InvariantCulture), center - new Vector2(0f, size * 0.5f), size, Gold with { W = alpha }, TextAnchor.Center);
            }
        }
    }

    /// <summary>カウントの目盛り（10 個。数えた分だけ金色）。</summary>
    public static IEnumerable<UiShapeDraw> Shapes(BoxingGame game, Vector2 screen)
    {
        ArgumentNullException.ThrowIfNull(game);
        foreach (var (fighter, center) in Counted(game, screen))
        {
            var count = Count(fighter);
            var width = (BoxingGame.CountSeconds * (PipSize + PipGap)) - PipGap;
            // 数字の字形は上端から大きさの 0.8 ほど下まで来る（数字の行は高めに取られる）ので、その下に置く。
            var y = center.Y + NumberSize;
            // 名前・数字・目盛りの下地（リングの白いマットの上でも読めるように）。図形は文字の下に描かれる。
            yield return UiShapeDraw.Rounded(new Vector2(center.X, center.Y + (NumberSize * 0.2f)), new Vector2(width + 80f, (NumberSize * 1.9f) + 20f), 18f, Backdrop);
            for (var i = 0; i < (int)BoxingGame.CountSeconds; i++)
            {
                var x = center.X - (width * 0.5f) + (PipSize * 0.5f) + (i * (PipSize + PipGap));
                yield return UiShapeDraw.Rounded(new Vector2(x, y), new Vector2(PipSize), 4f, i < count ? Gold : PipOff);
            }
        }
    }
}
