using System.Numerics;

namespace MiniRing.Break;

/// <summary>
/// 目標の経路（3 次元）: 時刻（0..1）に並べた点を、なめらかに（smoothstep で）つないだ値と、その上にかぶせる周期の揺れ（<see cref="Wave"/>）。
/// 姿勢を書き込まない: 出てくるのは手・頭の「目標」で、体がバネで追った結果が見た目になる（CLAUDE.md §1.2）。
/// </summary>
public sealed record Path3(IReadOnlyList<(float T, Vector3 P)> Keys, Wave? Wave = null)
{
    public static Path3 Still { get; } = new([(0f, Vector3.Zero)]);

    public static Path3 Hold(Vector3 p) => new([(0f, p)]);

    public Vector3 At(float t01)
    {
        var t = Math.Clamp(t01, 0f, 1f);
        var value = Keys[^1].P;
        if (t <= Keys[0].T)
        {
            value = Keys[0].P;
        }
        else
        {
            for (var i = 1; i < Keys.Count; i++)
            {
                if (t <= Keys[i].T)
                {
                    var (t0, p0) = Keys[i - 1];
                    var (t1, p1) = Keys[i];
                    var u = t1 > t0 ? (t - t0) / (t1 - t0) : 1f;
                    value = Vector3.Lerp(p0, p1, u * u * (3f - (2f * u)));
                    break;
                }
            }
        }

        return Wave is { } wave ? value + wave.At(t) : value;
    }
}

/// <summary>
/// 周期の揺れ: 動作の間に <paramref name="Cycles"/> 回、<paramref name="Amplitude"/> の大きさで往復する。始めと終わりは 0（包み込み）。
/// <paramref name="Quadrature"/> を渡すと、その軸へ 90 度ずれて動く（円を描く: 肩・首を回す）。
/// </summary>
public sealed record Wave(Vector3 Amplitude, float Cycles, Vector3? Quadrature = null)
{
    public Vector3 At(float t01)
    {
        var envelope = MathF.Sin(MathF.PI * Math.Clamp(t01, 0f, 1f));
        var phase = 2f * MathF.PI * Cycles * t01;
        var value = Amplitude * MathF.Sin(phase);
        if (Quadrature is { } q)
        {
            value += q * MathF.Cos(phase);
        }

        return value * envelope;
    }
}

/// <summary>手の目標の基準: 体フレーム（肩の中心）か、頭の位置か（口・額・耳へ運ぶ動作は頭を基準に目標を置く。頭が揺れても追う）。</summary>
public enum HandAnchor
{
    Body,
    Head,
}

/// <summary>
/// 片手の目標の経路。値は腕の長さ（<see cref="FighterSense.ArmReach"/>）を 1 とした体フレームのローカル（X は<b>その手の外側</b>が +、
/// Y 上、Z 前）。<paramref name="Anchor"/> が <see cref="HandAnchor.Head"/> なら頭の位置からのずれ。
/// </summary>
public sealed record HandPath(HandAnchor Anchor, Path3 Path);

/// <summary>
/// 休みの動作 1 つ: 手・頭・胴のひねり・体の向きの「目標の経路」のデータ。動作の最初と最後は、休みの構え（<see cref="BoxingGame"/> の休みの指令）から
/// <see cref="Lead"/> 秒かけて目標へ移り、<see cref="Release"/> 秒かけて構えへ戻る（目標の混ぜ合わせ。姿勢の書き込みではない）。
/// 経路の時刻（0..1）は、動作の中身の区間（<see cref="Seconds"/> から行きと帰りを除いた間）の中の割合。
/// <para>左右: 動作は「主の手」（小物を持つ手・動作の主役）で書き、動作ごとに左右どちらの手かを決める。<paramref name="Head"/>・<paramref name="Twist"/>・<paramref name="FaceYaw"/> の X・ひねりも主の手の側が +。</para>
/// </summary>
/// <param name="Name">動作の名前。</param>
/// <param name="Seconds">動作の長さ（行き・帰りを含む）[s]。</param>
/// <param name="Prop">持つ小物。null は素手。</param>
/// <param name="Main">主の手の目標の経路。</param>
/// <param name="Other">もう一方の手の経路。null は休みの構えのまま。</param>
/// <param name="Head">頭のずれの経路（腕の長さ比。主の手の側が +X）。</param>
/// <param name="Twist">胴のひねり [rad]（主の手の肩が前へ出るのが +）。</param>
/// <param name="FaceYaw">体の向きを相手の方からずらす角 [rad]（主の手の側へ向く方が +）。</param>
/// <param name="Weights">動作の選ばれやすさの重み（スタミナの少なさ・意識の薄れ・傷から連続に出す: <see cref="BreakWeights"/>）。</param>
/// <param name="Lead">目標へ移るまでの時間 [s]。</param>
/// <param name="Release">構えへ戻るまでの時間 [s]。</param>
/// <param name="Hold">小物の持ち方（<paramref name="Prop"/> があるとき）。</param>
public sealed record BreakAction(
    string Name, float Seconds, PropKind? Prop, HandPath Main, HandPath? Other, Path3 Head, Path3 Twist, Path3 FaceYaw, BreakWeights Weights,
    float Lead = 0.9f, float Release = 0.8f, PropHold Hold = default);

/// <summary>
/// 動作の選ばれやすさ: <c>Base + Tired × (1 − スタミナ) + Hurt × (意識の薄れ)</c>（連続の重み。種類での分岐ではない）。キャラの好み（<c>BreakRoutine</c> の
/// 個性）は、動作の名前で引く倍率として別に掛ける。
/// </summary>
public readonly record struct BreakWeights(float Base, float Tired = 0f, float Hurt = 0f)
{
    public float Of(float stamina, float faintness) => MathF.Max(0f, Base + (Tired * (1f - Math.Clamp(stamina, 0f, 1f))) + (Hurt * Math.Clamp(faintness, 0f, 1f)));
}
