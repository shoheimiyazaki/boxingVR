using System.Numerics;
using Aigis.Render;

namespace MiniRing.Desktop;

/// <summary>結果の画面（<see cref="MenuPage.Result"/>）の描画: 勝者・決まり手・ラウンドと時刻と、次へのボタン（クリックできる大きさ）。</summary>
internal sealed partial class MenuView
{
    private static string ChoiceLabel(ResultChoice choice) => choice switch
    {
        ResultChoice.Home => "本拠地へ",
        ResultChoice.Again => "もう一度",
        ResultChoice.Select => "キャラ選択へ",
        _ => "メニューへ",
    };

    private void DrawResult(PlateFn plate, FrameFn frame, LabelFn label)
    {
        if (state.Result is not { } result) { return; }

        // 勝者の側の色の枠（引き分けは落ち着いた色）。
        var (glow, deep) = result.Winner switch
        {
            0 => (RedSide, new Vector4(0.30f, 0.06f, 0.05f, 1)),
            1 => (BlueSide, new Vector4(0.05f, 0.10f, 0.32f, 1)),
            _ => (Pale, new Vector4(0.10f, 0.16f, 0.22f, 1)),
        };
        plate(323, 204, 540, 190, Shadow, Shadow, 10);
        plate(320, 200, 540, 190, deep * new Vector4(1.5f, 1.5f, 1.5f, 1f), deep, 10);
        frame(320, 200, 540, 190, glow, 2.5f, 10);
        var headline = result.Winner switch
        {
            0 => $"{result.Red} の勝ち",
            1 => $"{result.Blue} の勝ち",
            _ => "引き分け",
        };
        label(headline, 320, 124, 34, result.Winner is null ? Pale : Gold, TextAnchor.Center, maxWidth: 500f);
        label(result.Method, 320, 176, 20, Pale, TextAnchor.Center, maxWidth: 500f);
        var when = result.Round > 0
            ? result.Clock.Length > 0 ? $"第 {result.Round} ラウンド  {result.Clock}" : $"第 {result.Round} ラウンド"
            : string.Empty;
        if (when.Length > 0) { label(when, 320, 208, 18, Dim, TextAnchor.Center); }
        label($"{result.Red}  対  {result.Blue}", 320, 246, 16, Dim, TextAnchor.Center, maxWidth: 500f);

        var choices = result.Choices;
        var size = MenuLayout.ResultButtonSize;
        for (var i = 0; i < choices.Count; i++)
        {
            var c = MenuLayout.ResultButtonCenter(i, choices.Count);
            var selected = i == state.ResultIndex;
            plate(c.X + 3, c.Y + 4, size.X, size.Y, Shadow, Shadow, 6);
            plate(c.X, c.Y, size.X, size.Y, selected ? new(0.18f, 0.54f, 0.78f, 1) : new(0.10f, 0.28f, 0.40f, 1), new(0.04f, 0.13f, 0.26f, 1), 6);
            frame(c.X, c.Y, size.X, size.Y, selected ? Cyan : new(0.19f, 0.38f, 0.51f, 1), 2, 6);
            label(ChoiceLabel(choices[i]), c.X, c.Y - 11, 20, selected ? Gold : Pale, TextAnchor.Center, maxWidth: size.X - 16);
        }
    }
}
