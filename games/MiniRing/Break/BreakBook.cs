using System.Numerics;

namespace MiniRing.Break;

/// <summary>
/// 休みの動作の一覧（データ）。どの動作も「手・頭・胴・体の向きの目標の経路」で、姿勢の書き込みでも再生でもない（CLAUDE.md §1.2）。
/// 値は腕の長さ（約 53 cm）を 1 とした体フレームのローカルで、頭の基準の目標は頭の中心からのずれ（頭の中心は肩の中心の約 0.28 上。口は頭の中心の約 0.15 下・0.15 前）。
/// 数値は <c>BreakActionProbe</c> で撮った連続のコマを見て決めた（docs/BREAK_ROUTINE.md）。
/// </summary>
public static class BreakBook
{
    private static Vector3 V(float x, float y, float z) => new(x, y, z);

    private static Path3 Keys(params (float T, Vector3 P)[] keys) => new(keys);

    /// <summary>動作の名前。<c>BotStyle.BreakLikes</c> の鍵。</summary>
    public const string DeepBreath = "deepbreath";
    public const string Drink = "drink";
    public const string Wipe = "wipe";
    public const string Phone = "phone";
    public const string Call = "call";
    public const string ShoulderRoll = "shoulderroll";
    public const string NeckRoll = "neckroll";
    public const string SlapFace = "slapface";
    public const string Ropes = "ropes";
    public const string Glare = "glare";
    public const string ShakeArms = "shakearms";

    /// <summary>休みの動作のうち、始め・終わりでも選べる動作の場面（無い名前は休みだけ）。</summary>
    private static readonly Dictionary<string, BreakScene> OriginalScenes = new()
    {
        [DeepBreath] = BreakScene.Break | BreakScene.End,
        [Drink] = BreakScene.Break | BreakScene.Start,
        [Wipe] = BreakScene.Break | BreakScene.End,
        [Phone] = BreakScene.Break | BreakScene.Start,
        [ShoulderRoll] = BreakScene.Break | BreakScene.Start,
        [NeckRoll] = BreakScene.Break | BreakScene.Start,
        [SlapFace] = BreakScene.Break | BreakScene.Start,
        [Glare] = BreakScene.Break | BreakScene.Start,
        [ShakeArms] = BreakScene.Break | BreakScene.Start,
    };

    /// <summary>
    /// 全動作（すべての場面）。<paramref name="tired"/>（0 = 元気 … 1 = 息が上がっている）は深呼吸の深さ・速さ（息が上がるほど深く・速く）だけに効く。
    /// </summary>
    public static IReadOnlyList<BreakAction> All(float tired)
        => [.. Rests(tired).Select(a => a with { Scenes = OriginalScenes.GetValueOrDefault(a.Name, BreakScene.Break) }), .. BreakEmotes.All()];

    /// <summary>その場面で選べる動作（場面ごとの動作の表）。</summary>
    public static IReadOnlyList<BreakAction> For(BreakScene scene, float tired) => [.. All(tired).Where(a => (a.Scenes & scene) != 0)];

    private static IReadOnlyList<BreakAction> Rests(float tired)
    {
        var rest = Path3.Still;
        var breath = Math.Clamp(tired, 0f, 1f);
        return
        [
            // 深呼吸: 手を腰へ。息を吸うと頭と胸が上がって後ろへ、吐くと戻る（頭のずれの周期）。息が上がっているほど深く・速い。
            new BreakAction(DeepBreath, 9.231f, null,
                new HandPath(HandAnchor.Body, Path3.Hold(V(0.62f, -0.58f, 0.02f))),
                new HandPath(HandAnchor.Body, Path3.Hold(V(0.62f, -0.58f, 0.02f))),
                new Path3([(0f, Vector3.Zero)], new Wave(V(0f, 0.06f + (0.1f * breath), -0.04f - (0.06f * breath)), 2f + (3f * breath), Quadrature: null)),
                new Path3([(0f, Vector3.Zero)], new Wave(V(0.05f + (0.1f * breath), 0f, 0f), 2f + (3f * breath))),
                rest, new BreakWeights(1.2f, Tired: 3.5f, Hurt: 2f), Lead: 1.846f),

            // 水を飲む: ボトルを持つ手を口へ運び（頭の基準で口の前）、頭を後ろへ反らせてから、口を離して下ろす。
            new BreakAction(Drink, 10.07f, PropKind.Bottle,
                new HandPath(HandAnchor.Head, Keys((0f, V(-0.1f, -0.2f, 0.5f)), (0.25f, V(-0.1f, -0.1f, 0.5f)), (0.4f, V(-0.05f, 0.1f, 0.45f)), (0.8f, V(-0.05f, 0.1f, 0.45f)), (1f, V(-0.1f, -0.2f, 0.5f)))),
                null,
                Keys((0f, Vector3.Zero), (0.3f, Vector3.Zero), (0.5f, V(0f, 0.08f, -0.14f)), (0.8f, V(0f, 0.08f, -0.14f)), (0.95f, Vector3.Zero)),
                Keys((0f, Vector3.Zero)), rest, new BreakWeights(1f, Tired: 3f, Hurt: 0.5f), Hold: new PropHold(V(-0.15f, -0.45f, -0.88f), V(0f, 1f, 0f))),

            // 汗を拭く: タオルの手を額へ上げ、横になでながら頬・首へ下ろす。頭は少し下を向く。
            new BreakAction(Wipe, 9.231f, PropKind.Towel,
                new HandPath(HandAnchor.Head, new Path3(
                    [(0f, V(-0.1f, 0.12f, 0.4f)), (0.35f, V(-0.1f, 0.0f, 0.4f)), (0.7f, V(0f, -0.3f, 0.35f)), (1f, V(0.2f, -0.45f, 0.3f))],
                    new Wave(V(0.32f, 0f, 0f), 4f))),
                null,
                Keys((0f, Vector3.Zero), (0.3f, V(0f, -0.05f, 0.08f)), (1f, V(0.05f, -0.05f, 0.08f))),
                Keys((0f, Vector3.Zero)), rest, new BreakWeights(1f, Tired: 1.5f, Hurt: 1f), Hold: new PropHold(V(1f, 0.1f, 0f), V(0f, 0f, -1f))),

            // スマホを見る: 胸の前でスマホを持ち上げ、顔を下へ向ける（頭を前へ・下へ）。ときどき指で画面をなでる（手の小さな上下）。
            new BreakAction(Phone, 11.75f, PropKind.Phone,
                new HandPath(HandAnchor.Head, new Path3([(0f, V(-0.15f, -0.3f, 0.55f)), (1f, V(-0.15f, -0.3f, 0.55f))], new Wave(V(0f, 0.03f, 0f), 6f))),
                new HandPath(HandAnchor.Head, new Path3([(0f, V(0.35f, -0.65f, 0.45f)), (1f, V(0.35f, -0.65f, 0.45f))], new Wave(V(0f, 0.0f, 0.05f), 5f))),
                Keys((0f, Vector3.Zero), (0.2f, V(0f, -0.1f, 0.2f)), (0.9f, V(0f, -0.1f, 0.2f))),
                Keys((0f, Vector3.Zero)), rest, new BreakWeights(0.9f, Tired: 0.5f), Hold: new PropHold(V(0f, 0.85f, 0.5f), V(0f, 0.5f, -0.85f), V(-0.02f, 0f, 0f))),

            // 電話をする: スマホを耳へ当て、体の向きを相手の方から少しそらし、頭を耳の側へ傾ける。話す間に頭が小さく揺れる。
            new BreakAction(Call, 11.75f, PropKind.Phone,
                new HandPath(HandAnchor.Head, Keys((0f, V(0.35f, -0.35f, 0.3f)), (0.2f, V(0.3f, -0.05f, 0.05f)), (0.85f, V(0.3f, -0.05f, 0.05f)), (1f, V(0.35f, -0.35f, 0.3f)))),
                null,
                new Path3([(0f, Vector3.Zero), (0.25f, V(0.08f, 0f, 0f)), (0.9f, V(0.08f, 0f, 0f))], new Wave(V(0f, 0.02f, 0.02f), 4f)),
                Keys((0f, Vector3.Zero)),
                Keys((0f, Vector3.Zero), (0.25f, V(0.5f, 0f, 0f)), (0.9f, V(0.5f, 0f, 0f))),
                new BreakWeights(0.7f), Hold: new PropHold(V(0f, 0.95f, -0.15f), V(-1f, 0.1f, 0f), V(-0.02f, 0f, 0f))),

            // 肩を回す: 手は下げたまま、肩が上・後ろ・下・前と円を描く（両手の目標が肩と一緒に円）。胴は小さくひねる。
            new BreakAction(ShoulderRoll, 7.553f, null,
                new HandPath(HandAnchor.Body, new Path3([(0f, V(0.45f, -0.62f, 0.05f))], new Wave(V(0f, 0.18f, 0f), 2.5f, Quadrature: V(0f, 0f, 0.18f)))),
                new HandPath(HandAnchor.Body, new Path3([(0f, V(0.45f, -0.62f, 0.05f))], new Wave(V(0f, 0.18f, 0f), 2.5f, Quadrature: V(0f, 0f, 0.18f)))),
                Keys((0f, Vector3.Zero)),
                new Path3([(0f, Vector3.Zero)], new Wave(V(0.2f, 0f, 0f), 2.5f)),
                rest, new BreakWeights(0.9f, Tired: 0.6f)),

            // 首を回す: 頭が前・横・後ろ・横と円を描く。手は構えのまま。
            new BreakAction(NeckRoll, 8.392f, null,
                new HandPath(HandAnchor.Body, Path3.Hold(V(0.62f, -0.58f, 0.02f))),
                new HandPath(HandAnchor.Body, Path3.Hold(V(0.62f, -0.58f, 0.02f))),
                new Path3([(0f, Vector3.Zero)], new Wave(V(0.18f, 0f, 0f), 2f, Quadrature: V(0f, 0.05f, 0.18f))),
                Keys((0f, Vector3.Zero)), rest, new BreakWeights(0.9f, Hurt: 1.5f)),

            // グローブで顔を叩く: 両手を頬の外へ引き、頬へ打ちつける（外側から頬へ。2 回）。顔は少し前へ。
            new BreakAction(SlapFace, 6.714f, null,
                new HandPath(HandAnchor.Head, Keys((0f, V(0.8f, -0.1f, 0.3f)), (0.12f, V(0.27f, -0.1f, 0.1f)), (0.3f, V(0.8f, -0.1f, 0.3f)), (0.42f, V(0.27f, -0.1f, 0.1f)), (0.6f, V(0.8f, -0.1f, 0.3f)), (1f, V(0.8f, -0.1f, 0.3f)))),
                new HandPath(HandAnchor.Head, Keys((0f, V(0.8f, -0.1f, 0.3f)), (0.12f, V(0.27f, -0.1f, 0.1f)), (0.3f, V(0.8f, -0.1f, 0.3f)), (0.42f, V(0.27f, -0.1f, 0.1f)), (0.6f, V(0.8f, -0.1f, 0.3f)), (1f, V(0.8f, -0.1f, 0.3f)))),
                Keys((0f, Vector3.Zero), (0.3f, V(0f, -0.05f, 0.1f)), (0.9f, V(0f, -0.05f, 0.1f))),
                Keys((0f, Vector3.Zero)), rest, new BreakWeights(0.5f, Tired: 1f)),

            // コーナーにもたれる: 両腕を横へ広げて後ろのロープに掛け、体を後ろへあずける（頭が後ろへ・下がる）。
            new BreakAction(Ropes, 10.91f, null,
                new HandPath(HandAnchor.Body, Path3.Hold(V(0.95f, -0.15f, -0.45f))),
                new HandPath(HandAnchor.Body, Path3.Hold(V(0.95f, -0.15f, -0.45f))),
                Keys((0f, Vector3.Zero), (0.3f, V(0f, -0.04f, -0.18f)), (0.9f, V(0f, -0.04f, -0.18f))),
                Keys((0f, Vector3.Zero)), rest, new BreakWeights(1f, Tired: 3f)),

            // 相手のコーナーをにらむ: 頭を少し前へ・下げ、ゆっくり左右へ。手は下げたまま握って構える。
            new BreakAction(Glare, 7.553f, null,
                new HandPath(HandAnchor.Body, Path3.Hold(V(0.35f, -0.4f, 0.3f))),
                new HandPath(HandAnchor.Body, Path3.Hold(V(0.35f, -0.4f, 0.3f))),
                new Path3([(0f, Vector3.Zero), (0.25f, V(0f, -0.08f, 0.2f)), (0.9f, V(0f, -0.08f, 0.2f))], new Wave(V(0.05f, 0f, 0f), 1f)),
                Keys((0f, Vector3.Zero)), rest, new BreakWeights(0.7f, Hurt: -0.5f)),

            // 腕を振ってほぐす: 両手を下げたまま、手首から先を小刻みに上下へ振る。
            new BreakAction(ShakeArms, 5.874f, null,
                new HandPath(HandAnchor.Body, new Path3([(0f, V(0.55f, -0.6f, 0.1f))], new Wave(V(0f, 0.1f, 0.05f), 7f))),
                new HandPath(HandAnchor.Body, new Path3([(0f, V(0.55f, -0.6f, 0.1f))], new Wave(V(0f, -0.1f, 0.05f), 7f))),
                Keys((0f, Vector3.Zero)), Keys((0f, Vector3.Zero)), rest, new BreakWeights(0.6f)),
        ];
    }
}
