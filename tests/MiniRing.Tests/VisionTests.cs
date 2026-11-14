using System.Numerics;
using MiniRing.Bots;
using MiniRing.Voxels;

namespace MiniRing.Tests;

[TestClass]
public sealed class VisionTests
{
    [TestMethod]
    public void EyeLoss_IsIndependentContinuousAndRegenerates()
    {
        using var game = new BoxingGame(seed: 13);
        var body = game.Player.Voxels!;
        Assert.AreEqual(1f, body.Vision.Left, 0.0001f);
        var grid = body.PartOf(game.Player.Parts.Head)!.Body.Grid;
        var cells = Enumerable.Range(0, grid.CellCount).Where(i =>
            grid.MaterialAt(i) is BoxerAnatomy.Eye or BoxerAnatomy.Pupil &&
            grid.CellCenter(grid.Coordinates(i).X, grid.Coordinates(i).Y, grid.Coordinates(i).Z).X > 0f).ToArray();
        Assert.IsGreaterThan(0, cells.Length);
        foreach (var i in cells.Take(cells.Length / 2))
        {
            grid.SetCellState(i, (0, 0f, false, 0f));
        }
        Assert.IsTrue(body.Vision.Left is > 0f and < 1f);
        Assert.AreEqual(1f, body.Vision.Right, 0.0001f);
        foreach (var i in cells)
        {
            grid.SetCellState(i, (0, 0f, false, 0f));
        }
        Assert.AreEqual(0f, body.Vision.Left, 0.0001f);
        body.Regenerate(int.MaxValue);
        Assert.AreEqual(VisionState.Healthy, body.Vision);
    }

    [TestMethod]
    public void BlindSide_HasSlowerPunchEvidence_AndCannotAccessLiveTargets()
    {
        var frame = new BodyFrame(Vector3.Zero, 0f);
        var raw = new FighterSense(frame, 0.2f, new(0, 0, 1), new(0, -0.1f, 1), true,
            OpponentLeft: new(new(0.2f, 0, 1), new(0, 0, -4)),
            OpponentRight: new(new(-0.2f, 0, 1), new(0, 0, -4)));
        var filter = new VisionPerception(13);
        filter.Update(raw, VisionState.Healthy, 1f / 240f);
        var moved = raw with {
            OpponentLeft = raw.OpponentLeft with { Position = new(0.2f, 0, 0.5f) },
            OpponentRight = raw.OpponentRight with { Position = new(-0.2f, 0, 0.5f) }
        };
        var seen = filter.Update(moved, new VisionState(0f, 1f), 1f / 240f);
        Assert.IsGreaterThan(Vector3.Distance(seen.OpponentRight.Position, moved.OpponentRight.Position),
            Vector3.Distance(seen.OpponentLeft.Position, moved.OpponentLeft.Position));
        Assert.IsNull(seen.OpponentTargets);
        var again = new VisionPerception(13);
        again.Update(raw, VisionState.Healthy, 1f / 240f);
        Assert.AreEqual(seen, again.Update(moved, new VisionState(0f, 1f), 1f / 240f));
    }

    [TestMethod]
    public void HealthyVision_PreservesEverySenseField()
    {
        using var game = new BoxingGame(seed: 2);
        var raw = BoxingGame.Sense(game.Player, game.Cpu);
        Assert.AreEqual(raw, new VisionPerception(2).Update(raw, VisionState.Healthy, 1f / 240f));
    }

    [TestMethod]
    public void BlindHud_DarkensOnlyTheLostSide()
    {
        using var game = new BoxingGame(seed: 13);
        var screen = new Vector2(1280f, 720f);
        Assert.IsEmpty(BlindHud.Shapes(game, screen));
        game.Player.Voxels!.DestroyEye(left: true);
        var one = BlindHud.Shapes(game, screen);
        Assert.HasCount(1, one);
        Assert.IsLessThan(screen.X * 0.5f, one[0].CenterPixels.X, "the player's left eye is on the left of the screen");
        game.Player.Voxels.DestroyEye(left: false);
        var both = BlindHud.Shapes(game, screen);
        Assert.HasCount(2, both);
        // 操作を渡されるのも、遅れて・ぶれた見え方（ボットと同じ情報の損傷）。
        Assert.AreEqual(VisionState.Healthy, game.CpuSense.SelfVision ?? VisionState.Healthy);
        Assert.AreEqual(0f, game.PlayerSense.SelfVision!.Value.Binocular, 1e-4f);
    }

    /// <summary>
    /// 受け手の目の状態ごとに、同じ拳（相手の右ストレート）が来るのを受け手が見て、守りを始めるまでの時間 [s]。
    /// 試合の初期配置の見え方（<see cref="BoxingGame.Sense"/>）から、相手の右の拳を受け手の頭へ一定の速さで動かす。
    /// 受け手のボット（守る率 100 %・反応 0.18 秒）には、見える度合いで遅れ・ぶれた写し（<see cref="VisionPerception"/>）を渡す。
    /// 守りを始めなければ <see cref="float.PositiveInfinity"/>。
    /// </summary>
    private static float DefenseStart(VisionState eyes, int seed, float speedInReaches = 12f)
    {
        using var game = new BoxingGame(seed: 2);
        var sense = BoxingGame.Sense(game.Player, game.Cpu);
        // 相手を、拳の届く間合い（胸が自分の頭の 0.9 腕の長さ前）へ寄せる。
        var shift = sense.SelfHead + (sense.Self.Forward * 0.9f * sense.ArmReach) - sense.OpponentChest;
        var raw = sense with
        {
            OpponentHead = sense.OpponentHead + shift, OpponentChest = sense.OpponentChest + shift,
            OpponentLeft = sense.OpponentLeft with { Position = sense.OpponentLeft.Position + shift },
            OpponentRight = sense.OpponentRight with { Position = sense.OpponentRight.Position + shift },
            OpponentBody = new(sense.OpponentBody.LeftShoulder + shift, sense.OpponentBody.RightShoulder + shift, sense.OpponentBody.Pelvis + shift),
        };
        var speed = speedInReaches * raw.ArmReach;
        var start = raw.OpponentRight.Position;
        var direction = Vector3.Normalize(raw.SelfHead - start);
        var bot = new BoxerBot((DefenseDrill.Defender(DefenseBook.Default, defend: true) with { ReactionTime = 0.18f }).Validated(), seed, Defense.Slip);
        var filter = new VisionPerception(seed);
        const float dt = 1f / 240f;
        // 構え（見えている状態）で少し待ってから拳を出す。
        for (var step = 0; step < 120; step++)
        {
            var seen = filter.Update(raw, eyes, dt);
            bot.Read(seen, dt);
        }

        for (var step = 0; step < 240; step++)
        {
            var t = step * dt;
            var glove = new GloveSense(start + (direction * speed * t), direction * speed);
            var moved = raw with { OpponentRight = glove };
            bot.Read(filter.Update(moved, eyes, dt), dt);
            if (bot.CurrentDefense != Defense.None)
            {
                return t;
            }
        }

        return float.PositiveInfinity;
    }

    [TestMethod]
    public void OneDestroyedEye_DelaysTheDefenseAgainstPunchesFromThatSide()
    {
        // 右ストレート（相手の右手）は、受け手から見て左（+X）から来る: 左目が潰れると見えにくい側。
        var seeds = Enumerable.Range(1, 6).ToArray();
        var healthy = seeds.Select(s => DefenseStart(VisionState.Healthy, s)).ToArray();
        var leftBlind = seeds.Select(s => DefenseStart(new VisionState(0f, 1f), s)).ToArray();
        var rightBlind = seeds.Select(s => DefenseStart(new VisionState(1f, 0f), s)).ToArray();
        var both = seeds.Select(s => DefenseStart(new VisionState(0f, 0f), s)).ToArray();
        var half = seeds.Select(s => DefenseStart(new VisionState(0.5f, 1f), s)).ToArray();
        static int Defended(IEnumerable<float> times) => times.Count(float.IsFinite);
        static string Show(IEnumerable<float> times) => string.Join(" ", times.Select(v => v.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)));
        Console.WriteLine($"healthy {Show(healthy)}");
        Console.WriteLine($"left eye lost {Show(leftBlind)}");
        Console.WriteLine($"left eye half {Show(half)}");
        Console.WriteLine($"right eye lost {Show(rightBlind)}");
        Console.WriteLine($"both lost {Show(both)}");
        // 守る率 100 % でも、気づかなければ守れない。見える側と同じ拳には、健康なときと同じ反応で守る。
        Assert.IsGreaterThanOrEqualTo(4, Defended(healthy), "healthy: the defense starts at the reaction time");
        Assert.AreEqual(Defended(healthy), Defended(rightBlind), "the eye on the far side does not matter for a punch from the left");
        Assert.AreEqual(0, Defended(leftBlind), "the lost eye's side: the punch is not seen in time");
        Assert.AreEqual(0, Defended(both));
        // 半分の目は、その間（連続）: 守れた数が健康以下で、守れたとしても健康より遅い。
        Assert.IsLessThanOrEqualTo(Defended(healthy), Defended(half));
        for (var i = 0; i < seeds.Length; i++)
        {
            Assert.IsGreaterThanOrEqualTo(healthy[i], half[i], $"seed {seeds[i]}: a half-lost eye never reacts earlier");
        }
    }
}
