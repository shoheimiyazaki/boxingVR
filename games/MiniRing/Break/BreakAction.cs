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
/// <param name="Scenes">この動作を選べる場面（試合の始め・休み・試合の終わり。複数可）。場面ごとの動作の表は、この値で引く。選び方の式は場面に依らず同じ。</param>
/// <param name="Vapor">口から出る煙・飛沫（見た目だけ）。経路の時刻（0..1）の窓。null は出さない。</param>
/// <param name="Stride">相手の方へ歩く指令の大きさ（0..1。握手・歩み寄り）。0 は歩かない。歩く向きは相手の胸の方、<paramref name="Standoff"/> の手前で止まる。</param>
/// <param name="Standoff">相手へ歩み寄るとき、止まる距離 [m]（胸と胸）。</param>
/// <param name="NeedsPartner">相手が立っているときだけ選べる動作（握手・拍手など、相手がいて成り立つ動作）。</param>
public sealed record BreakAction(
    string Name, float Seconds, PropKind? Prop, HandPath Main, HandPath? Other, Path3 Head, Path3 Twist, Path3 FaceYaw, BreakWeights Weights,
    float Lead = 1.511f, float Release = 1.343f, PropHold Hold = default,
    BreakScene Scenes = BreakScene.Break, BreakVapor? Vapor = null, float Stride = 0f, float Standoff = 2.535f, bool NeedsPartner = false);

/// <summary>動作を行う場面: 試合の始め（入場してから最初のゴングまで）・ラウンドの間の休み・試合の終わり（決着の後）。複数の場面で選べる動作は和で持つ。</summary>
[Flags]
public enum BreakScene
{
    Start = 1,
    Break = 2,
    End = 4,
}

/// <summary>口から出る物の種類（見た目だけ）。</summary>
public enum VaporKind
{
    /// <summary>たばこの煙（灰色で漂って上がる）。</summary>
    Smoke,

    /// <summary>口に含んだ水を吐く飛沫（前・斜め下へ飛ぶ水色の玉）。</summary>
    Spit,
}

/// <summary>動作の中で口から物を出す窓（経路の時刻 0..1 の <paramref name="From"/> から <paramref name="To"/>）。</summary>
public readonly record struct BreakVapor(VaporKind Kind, float From, float To);

/// <summary>いま出ている煙・飛沫: 種類と、出始めてからの経過 [s]。</summary>
public readonly record struct VaporState(VaporKind Kind, float Age);

/// <summary>
/// 動作の選ばれやすさ: <c>Base + Tired × (1 − スタミナ) + Hurt × (意識の薄れ) + Joy × 気分</c>（連続の重み。種類での分岐ではない）。
/// 気分は試合の終わりの勝ち負けの量（勝者 +1・敗者 −1・引き分けと始め・休みは 0）で、Joy が正なら勝った側が、負なら負けた側が選びやすい。
/// キャラの好み（<c>BreakRoutine</c> の個性）は、動作の名前で引く倍率として別に掛ける。
/// </summary>
public readonly record struct BreakWeights(float Base, float Tired = 0f, float Hurt = 0f, float Joy = 0f)
{
    public float Of(float stamina, float faintness, float mood = 0f)
        => MathF.Max(0f, Base + (Tired * (1f - Math.Clamp(stamina, 0f, 1f))) + (Hurt * Math.Clamp(faintness, 0f, 1f)) + (Joy * Math.Clamp(mood, -1f, 1f)));
}
