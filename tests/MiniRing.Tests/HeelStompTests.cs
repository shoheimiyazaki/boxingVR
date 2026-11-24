using System.Globalization;
using System.Numerics;
using MiniRing.Bots;

namespace MiniRing.Tests;

/// <summary>
/// かかとの制御（<see cref="LegCommand.Heel"/>: 足の点の目標を足首からかかとへ一般化した形）と、倒れた相手へのかかとの踏み下ろしの連打
/// （<see cref="HeelBarrage"/>）。どれも同じビルドの中の比べ（足首を引く ↔ かかとを引く、足裏の踏みつけ ↔ かかとの連打）で見る。
/// </summary>
[TestClass]
[TestCategory("slow")]
public sealed class HeelStompTests
{
    private const float Dt = 1f / 240f;

    private sealed class Scripted(Func<FighterSense, FighterCommand> read) : IFighterControl
    {
        public FighterCommand Read(in FighterSense sense, float dt) => read(sense);
    }

    /// <summary>右脚に足の点の目標 <paramref name="leg"/> を出し続けて構えて立つ。</summary>
    private static BoxingGame Holding(Func<FighterSense, LegCommand> leg) =>
        new(seed: 1, corners: new BoxingCorners("heel", _ => new Scripted(s =>
        {
            var face = s.OpponentChest - s.Self.Origin;
            face.Y = 0f;
            var reach = s.ArmReach;
            return new FighterCommand(Vector3.Zero, Vector3.Normalize(face), new HandCommand(AiControl.Guard(Hand.Left, reach), false),
                new HandCommand(AiControl.Guard(Hand.Right, reach), false), RightLeg: leg(s));
        }), _ => new StandGuard(), Referee: false));

    /// <summary>右のかかとを体の前・下（膝を上げた形）に置く目標（体フレーム）。</summary>
    private static Vector3 HeelGoal(float reach) => new(0.2f * reach, -1.6f * reach, 0.3f * reach);

    private static float HeelError(Func<Vector3, float, LegCommand> make)
    {
        using var game = Holding(s => make(HeelGoal(s.ArmReach), s.ArmReach));
        for (var t = 0f; t < 1.5f; t += Dt)
        {
            game.Step(default, Dt);
        }

        var goal = game.Cpu.Frame.LocalToWorld(HeelGoal(game.Cpu.ArmReach));
        return Vector3.Distance(game.Cpu.HeelPosition(Hand.Right), goal);
    }

    [TestMethod]
    public void HeelControl_PutsTheHeelOnTheTarget_CloserThanPullingTheAnkleThere()
    {
        var tilt = Quaternion.CreateFromAxisAngle(Vector3.UnitX, -0.35f);
        var heel = HeelError((p, reach) => LegCommand.Heel(p, tilt, p + new Vector3(0f, 0.3f * reach, 0.2f * reach)));
        var ankle = HeelError((p, reach) => new LegCommand(p, p + new Vector3(0f, 0.3f * reach, 0.2f * reach), tilt));
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"heel error: heel control {heel * 100f:0.0} cm, ankle target {ankle * 100f:0.0} cm"));
        Assert.IsLessThan(0.05f, heel, "the heel settles within 5 cm of its target");
        Assert.IsLessThan(ankle, heel, "pulling the heel point puts the heel closer than pulling the ankle to the same point");
    }

    /// <summary>倒れた相手への足の攻めを 1 種類だけ選ぶ攻め手（足裏の踏みつけか、かかとの連打）。</summary>
    internal static BotStyle Attacker(bool heel) => (BotCatalog.Boxer with { GroundDrive = 1f, GroundStomp = 1f, GroundHeel = heel ? 1f : 0f }).Validated();

    internal sealed record StompRun(int FootHits, int HeadHits, float MeanSpeed, float PeakSpeed, float Energy, int AttackerFalls, float Seconds, float Lift = 0f);

    /// <summary>
    /// 受け手を気絶させて倒したままにし、攻め手が足で攻める <paramref name="seconds"/> 秒（打ち始めてから）の、足（かかと側の足・つま先）の当たり・頭と首への当たり・
    /// 当たった速さ・渡したエネルギーと、攻め手が倒れた回数。
    /// </summary>
    internal static StompRun Drill(bool heel, int seed, float seconds = 8f)
    {
        var attacker = Attacker(heel);
        using var game = new BoxingGame(seed: seed, corners: new BoxingCorners("attacker", s => new BoxerBot(attacker, s),
            s => new BoxerBot(BotCatalog.Boxer with { Attacks = false, DefenseRate = 0f, Prediction = 0f, Aggression = 0f, FinishTempo = 0f, FinishPress = 0f, FinishSpend = 0f }, s),
            CpuStyleJson: attacker.ToJson(), Referee: false));
        var victim = game.Player;
        float? started = null;
        var (hits, head, falls) = (0, 0, 0);
        var (speed, peak, energy) = (0f, 0f, 0f);
        var standing = true;
        var lift = 0f;
        for (var t = 0f; t < 40f; t += Dt)
        {
            if (t > 2.518f)
            {
                victim.Concussion = 0.97f;
            }

            game.Step(default, Dt);
            var ground = (game.CpuControl as GroundStriker)?.Ground;
            if (ground is { Started: > 0 } && victim.State != FighterState.Standing)
            {
                started ??= t;
            }

            if (started is null)
            {
                continue;
            }

            if (t - started > seconds)
            {
                break;
            }

            var up = game.Cpu.State == FighterState.Standing;
            if (standing && !up)
            {
                falls++;
            }

            standing = up;
            if (ground is { Striking: true, CurrentName: "heel-stomp" })
            {
                // かかとが倒れた相手の頭（狙いの範囲の上の端）より上がった高さ。
                lift = MathF.Max(lift, MathF.Max(game.Cpu.HeelPosition(Hand.Left).Y, game.Cpu.HeelPosition(Hand.Right).Y) - victim.HeadPosition.Y);
            }

            foreach (var hit in game.RecentHits.Where(h => h.Attacker == game.Cpu && h.Victim == victim && h.Kick && h.KickerPart is KickPart.Foot or KickPart.Toe))
            {
                hits++;
                speed += hit.Speed;
                peak = MathF.Max(peak, hit.Speed);
                energy += hit.Energy;
                head += hit.Zone is HitZone.Head or HitZone.Temple or HitZone.Chin or HitZone.Neck ? 1 : 0;
            }
        }

        return new StompRun(hits, head, hits > 0 ? speed / hits : 0f, peak, energy, falls, started is null ? 0f : seconds, lift);
    }

    internal static string Line(string label, StompRun r) => string.Create(CultureInfo.InvariantCulture,
        $"{label}: foot hits={r.FootHits} head/neck={r.HeadHits} mean speed={r.MeanSpeed:0.00} m/s peak={r.PeakSpeed:0.00} m/s energy={r.Energy:0} J attacker falls={r.AttackerFalls} heel lift above the head={r.Lift * 100f:0} cm");

    [TestMethod]
    public void HeelBarrage_StompsTheDownedHeadFasterAndMoreOften_ThanTheSoleStomp_WithoutFalling()
    {
        var seeds = new[] { 1, 2, 3 };
        var sole = seeds.Select(s => Drill(heel: false, s)).ToArray();
        var heel = seeds.Select(s => Drill(heel: true, s)).ToArray();
        foreach (var (s, h) in sole.Zip(heel))
        {
            Console.WriteLine(Line("sole stomp", s));
            Console.WriteLine(Line("heel barrage", h));
        }

        Assert.IsTrue(heel.All(r => r.Seconds > 0f), "the heel barrage starts on the downed opponent");
        Assert.IsGreaterThan(sole.Sum(r => r.FootHits), heel.Sum(r => r.FootHits), "more foot strikes land in the same time");
        Assert.IsGreaterThan(sole.Sum(r => r.HeadHits), heel.Sum(r => r.HeadHits), "more of them land on the head or neck");
        Assert.IsGreaterThan(sole.Max(r => r.PeakSpeed), heel.Max(r => r.PeakSpeed), "the heel comes down faster than the sole stomp");
        Assert.AreEqual(0, heel.Sum(r => r.AttackerFalls), "the attacker keeps its balance on the standing leg");
        Assert.IsTrue(heel.All(r => r.Lift is > 0.15f and < 0.5f), "the heel is raised about 30 cm above the head between strokes");
    }

    [TestMethod]
    public void HeelBarrage_PathLiftsTheHeelAboveTheAimAndDrivesItBelow_CountTimes()
    {
        var m = GroundTuning.Default.HeelStomp;
        var b = GroundTuning.Default.HeelBarrage;
        var aim = new Vector3(0.1f, -1.2f, 0.3f);
        var reach = 0.62f;
        var lows = 0;
        var wasLow = false;
        var top = float.NegativeInfinity;
        for (var t = 0f; t < b.Length(m); t += 0.002f)
        {
            var leg = b.Leg(m, t, aim, new Vector3(0.12f, -0.87f, 0f), Vector3.Zero, reach);
            Assert.AreEqual(FootPoint.Heel, leg.Point);
            var low = leg.TargetLocal.Y < aim.Y;
            lows += low && !wasLow ? 1 : 0;
            wasLow = low;
            top = b.Stroke(m, t) >= 0 ? MathF.Max(top, leg.TargetLocal.Y - aim.Y) : top;
        }

        Assert.AreEqual(b.Count, lows, "one drive below the aim per stroke");
        Assert.AreEqual(m.Rise * reach, top, 1e-3f, "the heel is lifted the rise above the aim between strokes");
    }
}
