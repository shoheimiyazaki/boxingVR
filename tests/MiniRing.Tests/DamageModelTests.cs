using System.Numerics;
using MiniRing.Bots;

namespace MiniRing.Tests;

/// <summary>
/// 非線形のダメージ: 削る量は拳の運動エネルギーに比例し、急所（回転の中心から外れた所）を体重を乗せて振り抜くと頭が首の上でねじられて
/// 脳しんとうになり、意識が飛ぶと体の力が抜けて崩れ落ちる。部位の名前で場合分けせず、物理（ねじれ）とボクセル（削れ）だけで決まる。
/// </summary>
[TestClass]
public sealed class DamageModelTests
{
    private const float Dt = 1f / 240f;

    [TestMethod]
    public void HeavyBlowSwungThroughTheChin_KnocksDown_ALightOneDoesNot()
    {
        Assert.IsTrue(Blow(Chin, mass: 4.5f, speed: 2.2f).WentDown, "a heavy, fast blow to the chin drops him");
        Assert.IsFalse(Blow(Chin, mass: 1.2f, speed: 0.9f).WentDown, "an arm punch to the chin does not");
    }

    [TestMethod]
    public void TheSameBlowThroughTheMiddleOfTheHead_TwistsItFarLess()
    {
        var chin = Blow(Chin, mass: 4.5f, speed: 2.2f);
        var centre = Blow(Centre, mass: 4.5f, speed: 2.2f);
        Console.WriteLine($"twist: chin {chin.Twist:0.0} rad/s, through the centre {centre.Twist:0.0} rad/s");

        Assert.IsGreaterThan(5f * centre.Twist, chin.Twist, "off the centre of rotation, the head is twisted on the neck");
        Assert.IsFalse(centre.WentDown);
    }

    [TestMethod]
    public void CarvingGrowsWithTheSquareOfTheSpeed()
    {
        // 削る量が数ボクセルだと、当たった所が骨か肉かで比が大きく揺れるので、数十ボクセル削れる速さで比べる。
        var slow = Carved(speed: 1.5f);
        var fast = Carved(speed: 3f);
        Console.WriteLine($"carved: speed 1.5 → {slow} voxels, speed 3 → {fast}");

        Assert.IsGreaterThan(0, slow);
        // 削る力は運動エネルギー（v²）で 4 倍。深く掘るほど硬い骨（肉の 8 倍）に当たるので、ボクセルの数は 4 倍より少ない。
        Assert.IsGreaterThan(2.2f * slow, (float)fast, "twice as fast carves well over twice as much (energy ∝ v²; bone deeper in is harder)");
    }

    [TestMethod]
    public void AKnockedDownFighter_GetsUpOnceHisHeadClears()
    {
        var (game, cpu) = NewGame();
        using (game)
        {
            Hit(game, cpu, Chin(cpu), mass: 4.5f, speed: 2.2f);
            var downAt = -1f;
            var upAt = -1f;
            for (var t = 0f; t < 16f; t += Dt)
            {
                game.Step(default, Dt);
                if (downAt < 0f && cpu.State == FighterState.Down)
                {
                    downAt = t;
                }

                if (downAt >= 0f && upAt < 0f && cpu.State == FighterState.Standing)
                {
                    upAt = t;
                }
            }

            Console.WriteLine($"down at {downAt:0.00}s, standing again at {upAt:0.00}s, consciousness {cpu.Consciousness:0.00}");
            Assert.IsGreaterThanOrEqualTo(0f, downAt, "knocked down");
            Assert.IsGreaterThan(downAt + 1.5f, upAt, "stays down for a while");
            Assert.IsTrue(upAt > 0f, "and gets back up once conscious again");
        }
    }

    [TestMethod]
    public void TheBlowsMomentum_IsSplitBetweenTheStruckPartAndTheBody_WithoutAppearingFromNowhere()
    {
        var (game, cpu) = NewGame();
        using (game)
        {
            // 削られた分の質量は破片になって飛ぶので、打つ前の質量で数える。
            var mass = cpu.AllParts.Concat(game.Player.AllParts).ToDictionary(id => id, game.World.GetMass);
            Vector3 Momentum(IEnumerable<int> parts) => parts.Aggregate(Vector3.Zero, (p, id) => p + (game.World.GetState(id).LinearVelocity * mass[id]));
            var victimBefore = Momentum(cpu.AllParts);
            var attackerBefore = Momentum(game.Player.AllParts);
            var headBefore = Momentum([cpu.Parts.Head]);
            Hit(game, cpu, Chin(cpu), mass: 4.5f, speed: 2.2f);
            var victimGain = Momentum(cpu.AllParts) - victimBefore;
            var attackerGain = Momentum(game.Player.AllParts) - attackerBefore;
            var headShare = (Momentum([cpu.Parts.Head]) - headBefore).Length() / victimGain.Length();
            Console.WriteLine($"victim gained {victimGain.Length():0.000} N·s, attacker {attackerGain.Length():0.000} N·s; the head took {headShare:P0}");

            Assert.IsLessThan(1e-3f, (victimGain + attackerGain).Length(), "what the victim gets, the attacker loses");
            Assert.IsGreaterThan(0.4f, headShare, "a fast blow leaves much of its momentum in the head it struck");
        }
    }

    private static (bool WentDown, float Twist) Blow(Func<Fighter, Vector3> where, float mass, float speed)
    {
        var (game, cpu) = NewGame();
        using (game)
        {
            Hit(game, cpu, where(cpu), mass, speed);
            var twist = cpu.LastHeadTwist;
            var down = false;
            for (var t = 0f; t < 2f; t += Dt)
            {
                game.Step(default, Dt);
                down |= cpu.State != FighterState.Standing;
            }

            return (down, twist);
        }
    }

    private static int Carved(float speed)
    {
        var (game, cpu) = NewGame();
        using (game)
        {
            var before = Voxels(cpu);
            var chest = cpu.ChestPosition;
            Hit(game, cpu, chest + (cpu.Frame.Forward * cpu.HalfExtents.Chest.Z), mass: 1.5f, speed, cpu.Parts.Chest, zone: HitZone.Body);
            return before - Voxels(cpu);
        }
    }

    private static int Voxels(Fighter f)
    {
        var total = 0;
        foreach (var part in f.Voxels!.Parts)
        {
            for (var m = 1; m < MiniRing.Voxels.BoxerAnatomy.Palette.Count; m++)
            {
                total += part.Body.Grid.Remaining((byte)m);
            }
        }

        return total;
    }

    /// <summary>あご: 頭の前・下の面（回転の中心から下へ外れている）。</summary>
    private static Vector3 Chin(Fighter f) => f.HeadPosition + (f.Frame.Forward * f.HalfExtents.Head.Z) - new Vector3(0f, 0.8f * f.HalfExtents.Head.Y, 0f);

    /// <summary>頭の正面の真ん中（拳の向きが回転の中心を通る）。</summary>
    private static Vector3 Centre(Fighter f) => f.HeadPosition + (f.Frame.Forward * f.HalfExtents.Head.Z);

    /// <summary>正面から水平に、拳に <paramref name="mass"/> kg が乗った速さ <paramref name="speed"/> の 1 発を当てる。</summary>
    private static void Hit(BoxingGame game, Fighter victim, Vector3 point, float mass, float speed, int? part = null, HitZone zone = HitZone.Chin)
    {
        var velocity = -victim.Frame.Forward * speed;
        var damage = PunchMechanics.Damage(mass * speed) * PunchMechanics.Sharpness(speed);
        game.ApplyHit(new HitEvent(game.Player, Hand.Right, victim, zone, point, speed, mass, 0.7f, damage), part ?? victim.Parts.Head, velocity);
    }

    /// <summary>打たず守らない 2 人のボットで、立って落ち着いた試合。</summary>
    private static (BoxingGame Game, Fighter Cpu) NewGame()
    {
        var still = BotCatalog.Boxer with { Attacks = false, DefenseRate = 0f, Prediction = 0f, Aggression = 0f };
        var corners = new BoxingCorners("still", s => new BoxerBot(still, s), s => new BoxerBot(still, s));
        var game = new BoxingGame(seed: 1, corners: corners);
        for (var t = 0f; t < 1.5f; t += Dt)
        {
            game.Step(default, Dt);
        }

        return (game, game.Cpu);
    }
}
