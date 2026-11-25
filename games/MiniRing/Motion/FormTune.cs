using System.Numerics;

namespace MiniRing.Motion;

/// <summary>
/// 型の経路への教育の差分（CLAUDE.md §1.8: 教育は技の経路・タイミング・間合いだけ）。教科書の型（<see cref="SwordMotion"/>）の目標の時間変化を、
/// 型ごとに少しだけ変える: 時計の速さ・胴のひねりの大きさ・両手の高さ・頭の前への出し・間合い。筋力・質量・材料・威力の倍率は無い（§1.7）。
/// 振りが速くなるのは目標が速く動いた結果を体がバネで追うからで、追い切れなければ目標から遅れる（§1.1）。
/// 型の種類で式を分けない（§1.3）: どの型にも同じ変換を掛ける。既定値（<see cref="Identity"/>）では型はそのまま。
/// </summary>
/// <param name="Tempo">型の時計の速さの倍率（1 で教科書の速さ。2 なら同じ経路を半分の時間で）。腰の歩く速さも同じ倍率にして、同じ距離を踏み込む。</param>
/// <param name="Twist">胴のひねりの目標の倍率（1 で教科書のまま。大きいほど、ひねって貯めて戻すバネが大きい）。</param>
/// <param name="Lift">両手の目標の高さのずれ [腕の長さ]（型の全部の間。上へ正。刃が通る高さが変わる）。</param>
/// <param name="Lean">頭の目標の前へのずれ [腕の長さ]（型の全部の間。前へ正。体を打ちへ乗せる）。</param>
/// <param name="Reach">間合いのずれ [m]（型の動きから出す間合い <see cref="StrikeDrill.ReachOf"/> に足す。負なら近くから打ち込み、深く斬る）。</param>
public sealed record FormTune(float Tempo = 1f, float Twist = 1f, float Lift = 0f, float Lean = 0f, float Reach = 0f)
{
    /// <summary>何も変えない差分。</summary>
    public static FormTune Identity { get; } = new();

    /// <summary>範囲の検査（どれも型の形が保たれる幅。外は <see cref="ArgumentOutOfRangeException"/>）。</summary>
    public FormTune Validated(string name)
    {
        Bots.WeaponStyle.InRange(Tempo, 0.5f, 2.5f, $"Forms.{name}.Tempo");
        Bots.WeaponStyle.InRange(Twist, 0f, 2.5f, $"Forms.{name}.Twist");
        Bots.WeaponStyle.InRange(Lift, -0.5f, 0.5f, $"Forms.{name}.Lift");
        Bots.WeaponStyle.InRange(Lean, -0.3f, 0.3f, $"Forms.{name}.Lean");
        Bots.WeaponStyle.InRange(Reach, -0.4f, 0.4f, $"Forms.{name}.Reach");
        return this;
    }

    /// <summary>型 <paramref name="motion"/> に差分を掛けた型（<see cref="Identity"/> なら同じ物を返す）。</summary>
    public SwordMotion Apply(SwordMotion motion)
    {
        ArgumentNullException.ThrowIfNull(motion);
        if (this == Identity)
        {
            return motion;
        }

        var lift = new Vector3(0f, Lift, 0f);
        var lean = new Vector3(0f, 0f, Lean);
        var frames = motion.Frames.Select(f => f with
        {
            Right = f.Right + lift,
            Left = f.Left + lift,
            Head = f.Head + lean,
            Twist = f.Twist * Twist,
            Velocity = f.Velocity * Tempo,
        }).ToList();
        return motion with
        {
            FrameTime = motion.FrameTime / Tempo,
            Frames = frames,
            StrikeUntil = motion.StrikeUntil / Tempo,
        };
    }
}
