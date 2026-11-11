using System.Globalization;

namespace MiniRing;

/// <summary>
/// 試合の再生の速さ（ゲームの秒 / 実時間の秒）。見ていてちょうどよい 0.75 倍を標準にし、0.5 倍・1.0 倍へ切り替えられる（ユーザーの指示: 2026-11-11）。
/// <para>試合の計算そのもの（240 Hz の固定のサブステップ）は変えない: ホストが 1 歩に渡す時間が短くなるだけで、物理は同じ刻みで進む
/// （1 歩 = 1/60 × 速さ = サブステップ 2 / 3 / 4 個）。記録は 1 歩ごとの時間を持つので、どの速さで遊んだ試合も再生でき、
/// 再生の速さも同じように切り替えられる。</para>
/// </summary>
public sealed class GameSpeed
{
    /// <summary>標準の速さ。</summary>
    public const float Standard = 0.75f;

    /// <summary>選べる速さ（遅い順）。どれも 1 歩（1/60 s × 速さ）が 240 Hz のサブステップのちょうど整数個になる。</summary>
    public static IReadOnlyList<float> Choices { get; } = [0.5f, 0.75f, 1f];

    /// <param name="initial">最初の速さ（<see cref="Choices"/> のどれか）。</param>
    public GameSpeed(float initial = Standard) => Select(initial);

    /// <summary>今の速さ。</summary>
    public float Value { get; private set; }

    /// <summary>画面に出す表示（「0.75x」）。</summary>
    public string Label => Value.ToString("0.0#", CultureInfo.InvariantCulture) + "x";

    /// <summary><see cref="Choices"/> の <paramref name="index"/> 番目（0 始まり）にする。</summary>
    public void SelectIndex(int index) => Value = Choices[Math.Clamp(index, 0, Choices.Count - 1)];

    /// <summary>速さを選ぶ。<see cref="Choices"/> に無い値は受け付けない（サブステップが整数個にならない）。</summary>
    /// <exception cref="ArgumentOutOfRangeException">選べる速さでない。</exception>
    public void Select(float value)
    {
        if (!Choices.Contains(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, $"速さは {string.Join(" / ", Choices.Select(c => c.ToString(CultureInfo.InvariantCulture)))} のどれかです。");
        }

        Value = value;
    }

    /// <summary>コマンドラインの値（"0.5" / "0.75" / "1" など）を読む。読めない・選べない値は null。</summary>
    public static float? Parse(string? text)
        => float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && Choices.Contains(value) ? value : null;
}
