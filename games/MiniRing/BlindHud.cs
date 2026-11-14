using System.Numerics;
using Aigis.Render;

namespace MiniRing;

/// <summary>
/// 目が潰れたプレイヤーの画面（HUD。描画だけで試合には響かない）: 潰れた目の側の画面の端が暗くなる（片目なら片側、両目なら両側から視野が閉じる）。
/// 暗さと幅は、その目の失われた割合（<see cref="VisionState"/>）に連続に従う。ボットが同じ損傷で相手の拳の情報を失う
/// （<see cref="VisionPerception"/>）のと同じ量から出す。画面は後ろから見るので、本人の左（+X）の目は画面の左。
/// </summary>
public static class BlindHud
{
    /// <summary>目を全部失った側の、画面の幅に対する暗い帯の割合（両目なら画面全体を覆う）。</summary>
    public const float FullBandFraction = 0.5f;

    /// <summary>目を全部失った側の、帯の端の暗さ（不透明度。完全には塞がない: 光は感じる）。</summary>
    public const float FullOpacity = 0.93f;

    private static readonly Vector4 Dark = new(0.01f, 0.01f, 0.02f, 1f);

    public static IReadOnlyList<UiShapeDraw> Shapes(BoxingGame game, Vector2 screenSizePixels)
    {
        ArgumentNullException.ThrowIfNull(game);
        var vision = game.Player.Voxels?.Vision ?? VisionState.Healthy;
        var shapes = new List<UiShapeDraw>();
        foreach (var (loss, onLeft) in new[] { (1f - vision.Left, true), (1f - vision.Right, false) })
        {
            if (loss < 0.02f)
            {
                continue;
            }

            var width = screenSizePixels.X * FullBandFraction * (0.4f + (0.6f * loss));
            var edge = Dark with { W = FullOpacity * loss };
            var clear = Dark with { W = 0f };
            var center = new Vector2(onLeft ? width * 0.5f : screenSizePixels.X - (width * 0.5f), screenSizePixels.Y * 0.5f);
            var size = new Vector2(width, screenSizePixels.Y);
            shapes.Add(new UiShapeDraw(center, size, onLeft ? edge : clear, FillColorEnd: onLeft ? clear : edge, GradientAxis: new Vector2(1f, 0f)));
        }

        return shapes;
    }
}
