using MiniRing.Bots;
using MiniRing.Voxels;

namespace MiniRing.Weapons;

/// <summary>剣の流儀: 持っている技と、その選ばれやすさ（<see cref="SwordBot"/> がこれで技を選ぶ）。</summary>
/// <param name="Techniques">技。</param>
/// <param name="Weights">技ごとの選ばれやすさ（技と同じ数。0 の技は出さない）。</param>
public sealed record SwordStyle(IReadOnlyList<SwordTechnique> Techniques, IReadOnlyList<float> Weights)
{
    /// <summary>どの技も同じ選ばれやすさの流儀。</summary>
    public static SwordStyle Even(IReadOnlyList<SwordTechnique> techniques) => new(techniques, [.. techniques.Select(_ => 1f)]);

    /// <summary>この流儀で戦うボット。</summary>
    public SwordBot Bot(int seed) => new(Techniques, seed, weights: Weights);
}

/// <summary>
/// 剣同士の稽古（1 試合）の結果。「斬った」は相手の体（胴につながっている所）から失わせたボクセルの数: 斬り取った分と、切り落とした先。
/// </summary>
/// <param name="DealtByRed">赤が青から失わせたボクセル。</param>
/// <param name="DealtByBlue">青が赤から失わせたボクセル。</param>
/// <param name="RedDied">赤が死んだ。</param>
/// <param name="BlueDied">青が死んだ。</param>
/// <param name="RedByTechnique">赤の技ごとの、相手から失わせたボクセル（技を出していない間の分は「構え」）。</param>
/// <param name="BlueByTechnique">青の同じもの。</param>
/// <param name="RedUsed">赤が出した技の名前（順に）。</param>
/// <param name="BlueUsed">青が出した技の名前（順に）。</param>
public sealed record SparResult(float DealtByRed, float DealtByBlue, bool RedDied, bool BlueDied,
    IReadOnlyDictionary<string, float> RedByTechnique, IReadOnlyDictionary<string, float> BlueByTechnique,
    IReadOnlyList<string> RedUsed, IReadOnlyList<string> BlueUsed)
{
    /// <summary>赤から見た差: 失わせたボクセルの差 / 100 + （殺した − 殺された）× 3。</summary>
    public float RedMargin => ((DealtByRed - DealtByBlue) / 100f) + (3f * ((BlueDied ? 1f : 0f) - (RedDied ? 1f : 0f)));
}

/// <summary>
/// 剣同士の稽古: 2 人とも右手に剣を握り（左手はグローブで顔を守る）、それぞれの流儀（<see cref="SwordStyle"/>）の <see cref="SwordBot"/> で、
/// どちらかが死ぬか時間まで戦う。剣と剣はボクセル同士で当たる（刃が刃に食い込む・弾かれる）。
/// </summary>
public static class SwordSparring
{
    /// <summary>1 試合。<paramref name="afterStep"/> は 1 歩ごとに、時刻と赤・青のボットを添えて呼ぶ（動画を撮るため）。</summary>
    public static SparResult Run(SwordStyle red, SwordStyle blue, int seed, float seconds, Action<BoxingGame, float, SwordBot?, SwordBot?>? afterStep = null)
    {
        ArgumentNullException.ThrowIfNull(red);
        ArgumentNullException.ThrowIfNull(blue);
        SwordBot? redBot = null, blueBot = null;
        using var game = new BoxingGame(seed: seed, corners: new BoxingCorners("sword",
            s => blueBot = blue.Bot(s), s => redBot = red.Bot(s), CpuWeapon: WeaponKind.IronSword, PlayerWeapon: WeaponKind.IronSword));
        var redCells = Attached(game.Player.Voxels!);
        var blueCells = Attached(game.Cpu.Voxels!);
        float dealtByRed = 0f, dealtByBlue = 0f;
        var redBy = new Dictionary<string, float>();
        var blueBy = new Dictionary<string, float>();
        const float dt = 1f / 60f;
        for (var t = 0f; t < seconds && !game.Player.IsDead && !game.Cpu.IsDead; t += dt)
        {
            game.Step(default, dt);
            var redNow = Attached(game.Player.Voxels!);
            var blueNow = Attached(game.Cpu.Voxels!);
            // 失ったボクセルは、そのとき相手が出していた技のもの（出していなければ「構え」: 構えた剣に触れた・引いた剣で斬れた）。
            if (blueNow < blueCells)
            {
                dealtByRed += blueCells - blueNow;
                Add(redBy, redBot?.Current?.Name ?? Guard, blueCells - blueNow);
            }

            if (redNow < redCells)
            {
                dealtByBlue += redCells - redNow;
                Add(blueBy, blueBot?.Current?.Name ?? Guard, redCells - redNow);
            }

            (redCells, blueCells) = (redNow, blueNow);
            afterStep?.Invoke(game, t, redBot, blueBot);
        }

        return new SparResult(dealtByRed, dealtByBlue, game.Player.IsDead, game.Cpu.IsDead, redBy, blueBy,
            redBot?.Used.ToList() ?? [], blueBot?.Used.ToList() ?? []);
    }

    /// <summary>技を出していない間に斬れた分の名前。</summary>
    public const string Guard = "guard";

    private static int Attached(BoxerBody body) => body.Parts.Sum(p => p.Body.Grid.FilledCount);

    private static void Add(Dictionary<string, float> to, string key, float value) => to[key] = to.GetValueOrDefault(key) + value;
}
