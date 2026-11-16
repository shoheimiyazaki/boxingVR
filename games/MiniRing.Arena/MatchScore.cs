using MiniRing.Bots;

namespace MiniRing.Arena;

/// <summary>
/// 試合の点の式を 1 か所に置く（<see cref="MatchTrainer{T}"/> と <see cref="MatchSampler"/> が同じ式を使う）。
/// </summary>
internal static class MatchScore
{
    /// <summary>
    /// 勝ち − 負け + 0.9 tanh((与えた − 受けた) / 400)。勝ち越しが主で、ダメージの差は同じ勝ち越しの間の順を決めるだけ（1 試合ぶんに届かない重さ）。
    /// </summary>
    public static float Of(int wins, int losses, float dealt, float taken)
        => wins - losses + (0.9f * MathF.Tanh((dealt - taken) / 400f));

    /// <summary>自分と同じ名前の相手と集計が混ざらないように、自分の名前へ付ける印。</summary>
    public const string MineMark = "（育てる）";

    /// <summary>名前に印を付けた自分。</summary>
    public static Contender Mark(Contender mine) => mine with { Name = mine.Name + MineMark };
}
