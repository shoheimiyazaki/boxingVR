using System.Globalization;
using System.Numerics;
using Aigis.Render;

namespace MiniRing;

/// <summary>
/// 審判の 10 カウントの表示（HUD。描画だけで試合には響かない）。倒れた本人の上に（体の真上の点を画面へ写した所）、名前と大きな数字
/// （1 秒ごとに増え、増えた瞬間に大きく打ち出して縮む）、その下に 10 個の目盛り。下地の窓は置かず（ユーザーの指定: 体を隠さない）、
/// 文字には影を付けてリングの白いマットの上でも読めるようにする。ゲームの HUD と動画（miniring-arena）で同じものを使う。
/// </summary>
public static class DownCountHud
{
    /// <summary>名前の札（「YOU DOWN」の YOU の部分）が出始める、倒れてからの時間 [s]。転んだだけの一瞬は出さない。</summary>
    public const float ShowAfter = 0.8392f;

    /// <summary>数字の大きさ [px]。体の上に浮かべるので、画面の真ん中に出していた頃（96）より小さく。</summary>
    private const float NumberSize = 72f;

    private const float NameSize = 24f;
    private const float PipSize = 11f;
    private const float PipGap = 5f;

    /// <summary>表示を浮かべる高さ [m]（物理の空間）: 倒れた体（胸と腰の間）の真上、これだけ上（寝た体の厚みの 2 倍ほど）。</summary>
    internal const float AboveBody = 0.4507f;

    /// <summary>体の上の点と表示の下端の間 [px]（目盛りが体に重ならない）。</summary>
    private const float Gap = 6f;

    /// <summary>文字の影のずれ [px]（下地の窓の代わりに、白いマットの上でも文字を読めるように）。</summary>
    private const float ShadowOffset = 3f;

    private static readonly Vector4 Gold = new(1f, 0.85f, 0.3f, 1f);
    private static readonly Vector4 PipOff = new(0.15f, 0.15f, 0.18f, 0.55f);
    private static readonly Vector4 Shadow = new(0f, 0f, 0f, 0.7f);

    /// <summary>今の数え（0 = まだ 1 秒経っていない、最大 10）。</summary>
    public static int Count(Fighter fighter)
    {
        ArgumentNullException.ThrowIfNull(fighter);
        return (int)MathF.Min(BoxingGame.CountNumbers, MathF.Floor(fighter.DownTime / BoxingGame.CountInterval));
    }

    /// <summary>
    /// 数えている（倒れて <see cref="ShowAfter"/> を過ぎ、ラウンドが終わっていない）ファイターと、表示の下端の中心（体の真上の点を画面へ写した所）。
    /// 写せない（カメラの後ろ）ときは画面の上の方に置く。
    /// </summary>
    private static IEnumerable<(Fighter Fighter, Vector2 Bottom)> Counted(BoxingGame game, Matrix4x4 viewProjection, Vector2 screen)
    {
        if (game.RoundOver || !game.CountsDowns)
        {
            yield break; // ダウンを数えない団体は 10 カウントを出さない
        }

        var down = new[] { game.Player, game.Cpu }.Where(f => f.DownTime > ShowAfter).ToArray();
        for (var i = 0; i < down.Length; i++)
        {
            var fighter = down[i];
            var above = ((fighter.ChestPosition + fighter.PelvisPosition) * 0.5f) + (Vector3.UnitY * AboveBody);
            if (Camera.TryWorldToScreen(viewProjection, game.Display.Point(above), screen, out var pixel)) // 描画の行列は表示の空間
            {
                // 体が画面の上や端に寄っても、表示全体（打ち出した数字と名前まで）が画面に収まるように寄せる。
                var bottom = pixel - new Vector2(0f, Gap);
                var halfWidth = 0.5f * ((BoxingGame.CountNumbers * (PipSize + PipGap)) + 40f);
                yield return (fighter, new Vector2(Math.Clamp(bottom.X, halfWidth, MathF.Max(halfWidth, screen.X - halfWidth)),
                    Math.Clamp(bottom.Y, BlockHeight, MathF.Max(BlockHeight, screen.Y - BottomMargin))));
            }
            else
            {
                var x = down.Length == 1 ? 0.5f : (fighter == game.Player ? 0.36f : 0.64f);
                yield return (fighter, new Vector2(screen.X * x, screen.Y * 0.35f));
            }
        }
    }

    /// <summary>体が画面の下の外にいても、表示の下端をこれだけ画面の内側に置く [px]。</summary>
    private const float BottomMargin = 24f;

    /// <summary>表示全体の高さ [px]（目盛り・打ち出して 1.4 倍の数字・名前と、上の余白）: 下端をこれより上へは寄せない。</summary>
    private const float BlockHeight = PipSize + 6f + (NumberSize * 0.85f * 1.4f) + NameSize + 4f + 12f;

    /// <summary>目盛りの行の縦の中心（表示の下端から）。</summary>
    private static float PipRow(Vector2 bottom) => bottom.Y - (PipSize * 0.5f);

    /// <summary>カウントの文字（名前と数字。どちらも影付き）。<paramref name="font"/> はデスクトップの試合の HUD が DotGothic16（<see cref="UiFont"/>）、テレビ中継は OS のフォント（既定）。</summary>
    public static IEnumerable<ScreenTextDraw> Text(BoxingGame game, Matrix4x4 viewProjection, Vector2 screen, Func<Fighter, string> name, Func<Fighter, Vector4> color,
        TextFont font = TextFont.System)
    {
        if (font == TextFont.Pixel) { UiFont.Ensure(); }

        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(color);
        // ドットのフォントは高さを 16 px の整数倍へ寄せるので、並べるときは実際に描かれる高さで測る（OS のフォントでは頼んだ高さのまま）。
        var numberSize = ScreenText.RenderedHeight(NumberSize, font);
        var nameSize = ScreenText.RenderedHeight(NameSize, font);
        foreach (var (fighter, bottom) in Counted(game, viewProjection, screen))
        {
            // 文字の Y は上端。下から: 目盛り → 数字 → 名前。
            var numberTop = PipRow(bottom) - (PipSize * 0.5f) - 6f - (numberSize * 0.85f);
            var count = Count(fighter);
            var nameTop = numberTop - nameSize - 4f;
            if (count >= 1)
            {
                // 数が増えた瞬間に 1.4 倍で打ち出し、カウント 1 つの間の 1/4 ほどで元の大きさへ（下端を揃えて大きくする）。
                var since = (fighter.DownTime / BoxingGame.CountInterval) - MathF.Floor(fighter.DownTime / BoxingGame.CountInterval); // カウントの間の中の位置 [0..1)
                var size = numberSize * (1f + (0.4f * MathF.Exp(-since * 10f)));
                var alpha = 0.8f + (0.2f * MathF.Exp(-since * 4f));
                var top = numberTop - ((ScreenText.RenderedHeight(size, font) - numberSize) * 0.85f);
                var text = count.ToString(CultureInfo.InvariantCulture);
                yield return new ScreenTextDraw(text, new Vector2(bottom.X + ShadowOffset, top + ShadowOffset), size, Shadow with { W = Shadow.W * alpha }, TextAnchor.Center, font);
                yield return new ScreenTextDraw(text, new Vector2(bottom.X, top), size, Gold with { W = alpha }, TextAnchor.Center, font);
                nameTop = top - nameSize - 4f;
            }

            var label = $"{name(fighter)} DOWN";
            yield return new ScreenTextDraw(label, new Vector2(bottom.X + 2f, nameTop + 2f), NameSize, Shadow, TextAnchor.Center, font);
            yield return new ScreenTextDraw(label, new Vector2(bottom.X, nameTop), NameSize, color(fighter), TextAnchor.Center, font);
        }
    }

    /// <summary>カウントの目盛り（10 個。数えた分だけ金色）。下地の窓は無い。</summary>
    public static IEnumerable<UiShapeDraw> Shapes(BoxingGame game, Matrix4x4 viewProjection, Vector2 screen)
    {
        ArgumentNullException.ThrowIfNull(game);
        foreach (var (fighter, bottom) in Counted(game, viewProjection, screen))
        {
            var count = Count(fighter);
            var width = (BoxingGame.CountNumbers * (PipSize + PipGap)) - PipGap;
            var y = PipRow(bottom);
            for (var i = 0; i < BoxingGame.CountNumbers; i++)
            {
                var x = bottom.X - (width * 0.5f) + (PipSize * 0.5f) + (i * (PipSize + PipGap));
                yield return UiShapeDraw.Rounded(new Vector2(x, y), new Vector2(PipSize), 3f, i < count ? Gold : PipOff);
            }
        }
    }
}
