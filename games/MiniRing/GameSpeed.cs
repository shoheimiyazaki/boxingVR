using System.Globalization;

namespace MiniRing;

/// <summary>
/// 試合の再生の速さ（ゲームの秒 / 実時間の秒）。見ていてちょうどよい 0.75 倍を標準にし、0.5 倍・1.0 倍へ切り替えられる（ユーザーの指示: 2026-11-11）。
/// <para>ライブの Desktop / VR は専用スレッドで 1/240 秒ずつ進む。速度は歩を実行する実時間の間隔だけを変え、
/// 物理の刻みを変えない。記録は適用済みの入力と各歩の刻みを持つので、表示速度を変えても照合できる。
/// Arena など同期ホストでは従来どおり外側の刻みを調整できる。</para>
/// </summary>
public sealed class GameSpeed
{
    /// <summary>標準の速さ。</summary>
    public const float Standard = 0.75f;

    /// <summary>選べる表示・進行速度（遅い順）。</summary>
    public static IReadOnlyList<float> Choices { get; } = [0.5f, 0.75f, 1f];

    /// <param name="initial">最初の速さ（<see cref="Choices"/> のどれか）。</param>
    public GameSpeed(float initial = Standard) => Select(initial);

    /// <summary>今の速さ。</summary>
    public float Value { get; private set; }

    /// <summary>画面に出す表示（「0.75x」）。</summary>
    public string Label => Value.ToString("0.0#", CultureInfo.InvariantCulture) + "x";

    /// <summary><see cref="Choices"/> の <paramref name="index"/> 番目（0 始まり）にする。</summary>
    public void SelectIndex(int index) => Value = Choices[Math.Clamp(index, 0, Choices.Count - 1)];

    /// <summary>速さを選ぶ。<see cref="Choices"/> に無い値は受け付けない。</summary>
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
