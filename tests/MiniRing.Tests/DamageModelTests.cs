using System.Numerics;
using MiniRing.Bots;

namespace MiniRing.Tests;

/// <summary>
/// 非線形のダメージ: 削る量は拳の運動エネルギーに比例し、急所（回転の中心から外れた所）を体重を乗せて振り抜くと頭が首の上でねじられて
/// 脳しんとうになり、意識が飛ぶと体の力が抜けて崩れ落ちる。部位の名前で場合分けせず、物理（ねじれ）とボクセル（削れ）だけで決まる。
/// </summary>
[TestClass]
[TestCategory("slow")]
public sealed class DamageModelTests
{
    private const float Dt = 1f / 240f;

    [TestMethod]
    public void HeavyBlowSwungThroughTheChin_KnocksDown_ALightOneDoesNot()
    {
        Assert.IsTrue(Blow(Chin, mass: 45f, speed: 3.692f).WentDown, "a heavy, fast blow to the chin drops him");
        Assert.IsFalse(Blow(Chin, mass: 12f, speed: 1.511f).WentDown, "an arm punch to the chin does not");
    }

    [TestMethod]
    public void TheSameBlowThroughTheMiddleOfTheHead_TwistsItFarLess()
    {
        var chin = Blow(Chin, mass: 45f, speed: 3.692f);
        var centre = Blow(Centre, mass: 45f, speed: 3.692f);
        Console.WriteLine($"twist: chin {chin.Twist:0.0} rad/s, through the centre {centre.Twist:0.0} rad/s");

        Assert.IsGreaterThan(5f * centre.Twist, chin.Twist, "off the centre of rotation, the head is twisted on the neck");
        // 真ん中への同じ 1 発は頭をほとんどねじらない。倒れるかは材料（顔の下に何があるか）で決まる。
        // 2026-11-11: 生成の頭は目の高さで皮膚のすぐ下が脳だったので、顔を貫いて脳を 2 割削り（働き 0.23）、崩れていた。
        // 2026-11-15（PhysicsVersion 31）: 直した頭（boxer.vox）は鼻・眉・頬の皮膚の下に骨があり、拳はそこで止まって脳は 4% しか削れず
        // （働き 0.77）立っている。ねじれでなく材料で決まることは同じ。
        Assert.IsFalse(centre.WentDown, "the facial bone in front of the brain stops the fist, and the head barely turns");
    }

    [TestMethod]
    public void CarvingGrowsWithTheSquareOfTheSpeed()
    {
        // 削る量が数ボクセルだと、当たった所が骨か肉かで比が大きく揺れるので、数十ボクセル削れる速さで比べる。
        var slow = Carved(speed: 2.518f);
        var fast = Carved(speed: 5.035f);
        Console.WriteLine($"carved: speed 2.52 → {slow} voxels, speed 5.04 → {fast}");

        Assert.IsGreaterThan(0, slow);
        // 削る力は運動エネルギー（v²）で 4 倍。深く掘るほど硬い骨（肉の 8 倍）に当たるので、ボクセルの数は 4 倍より少ない。
        Assert.IsGreaterThan(2.2f * slow, (float)fast, "twice as fast carves well over twice as much (energy ∝ v²; bone deeper in is harder)");
    }

    [TestMethod]
    public void AKnockedDownFighter_GetsUpOnceHisHeadClears()
    {
        // 意識が 0 のまま StillKoSeconds 動かないほど深い 1 発はその場で K.O. になるので、倒れても意識が少し残る 1 発で、
        // もがきながら起き上がるまでを見る（2026-11-20 実物大へ: 質量 ×10・速さ ×1.678・時間 ×1.678。下の経緯の数字は人形の単位）。
        // 2026-11-18（PhysicsVersion 45）: 脳しんとうの係数 ConcussionGain を 100 倍にした（体が運ぶ運動量を体自身の動きの分だけにしたので、
        // 試合の拳で頭をねじる運動量が約 1/7〜1/25 になった）。ここで当てる 1 発はテストが直接決める乗る質量なので、同じ脳しんとうの深さに
        // 合わせて質量を 4.5 kg → 0.7 kg にした（脳しんとうは運動量の 2 乗にほぼ比例）。測った数字（GetUpScanProbe。0.7 kg × 2.1 m/s は脳しんとう 0.90・4.2 秒で起き上がり、0.65 kg × 2.1 は 0.68 で倒れない）:
        // 版 43: 4.5 kg × 2.1 m/s で脳しんとう 1.15・0.20 秒でダウン・6.75 秒で起き上がり・意識 0.97。
        // いま: 4.5 kg × 2.1 では脳しんとう 114.7 で意識 0 のまま（起き上がらない）。0.7 kg × 2.2 m/s で脳しんとう 1.09・0.23 秒でダウン・5.94 秒で起き上がり。
        // （いまの試合の拳に乗る質量は腕 0.3〜0.4 kg ＋ 体の分でせいぜい 1 kg なので、0.7 kg は強い 1 発の質量）。
        // 2026-11-19（PhysicsVersion 60）: 脳しんとうを 1 つの値（頭の回転の速さ ÷ ConcussionSpin 40 rad/s の 2 乗）にした。前の拳の係数 0.0035 の約 1/5.6 なので、
        // 同じ深さの脳しんとうに合わせて体の分の質量（0.7 − 腕 0.33 kg）を √5.6 倍にし、1.2 kg にした（脳しんとうは体の分の運動量の 2 乗に比例）。
        // 2026-11-25（PhysicsVersion 103、つま先の蝶番を固くした）: 同じ 1 発でも、倒れるときに頭が床を打つ強さで脳しんとうが 1.3〜5.6 まで変わり、深いと
        // 10 カウントまでに起き上がれない（種 1〜6 で、前のつま先では 3 つ、固いつま先では 2 つが起き上がらない。ToeGapProbe.KnockedDownRecovery）。
        // 種 1 の 1 回は前のつま先でたまたま起き上がっていた。倒れ方は種で変わるので、6 つの種のうち半分以上が「しばらく倒れていて、意識が戻ると起き上がる」ことを見る。
        var risen = 0;
        var detail = new List<string>();
        foreach (var seed in new[] { 1, 2, 3, 4, 5, 6 })
        {
            var (game, cpu) = NewGame(seed);
            using (game)
            {
                Hit(game, cpu, Chin(cpu), mass: 12f, speed: 3.692f);
                var downAt = -1f;
                var upAt = -1f;
                for (var t = 0f; t < 26.85f; t += Dt)
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

                detail.Add($"seed {seed}: down at {downAt:0.00}s, standing again at {upAt:0.00}s, consciousness {cpu.Consciousness:0.00}");
                Assert.IsGreaterThanOrEqualTo(0f, downAt, $"knocked down (seed {seed})");
                if (upAt > 0f)
                {
                    Assert.IsGreaterThan(downAt + 2.518f, upAt, $"stays down for a while (seed {seed})");
                    risen++;
                }
            }
        }

        Console.WriteLine(string.Join(Environment.NewLine, detail));
        Assert.IsGreaterThanOrEqualTo(3, risen, $"and gets back up once conscious again, in at least half of the falls ({string.Join("; ", detail)})");
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
            Hit(game, cpu, Chin(cpu), mass: 45f, speed: 3.692f);
            var victimGain = Momentum(cpu.AllParts) - victimBefore;
            var attackerGain = Momentum(game.Player.AllParts) - attackerBefore;
            var headShare = (Momentum([cpu.Parts.Head]) - headBefore).Length() / victimGain.Length();
            Console.WriteLine($"victim gained {victimGain.Length():0.000} N·s, attacker {attackerGain.Length():0.000} N·s; the head took {headShare:P0}");

            Assert.IsLessThan(0.01678f, (victimGain + attackerGain).Length(), "what the victim gets, the attacker loses");
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
            for (var t = 0f; t < 3.357f; t += Dt)
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
            Hit(game, cpu, chest + (cpu.Frame.Forward * cpu.HalfExtents.Chest.Z), mass: 15f, speed, cpu.Parts.Chest, zone: HitZone.Body);
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
    internal static Vector3 Chin(Fighter f) => f.HeadPosition + (f.Frame.Forward * f.HalfExtents.Head.Z) - new Vector3(0f, 0.8f * f.HalfExtents.Head.Y, 0f);

    /// <summary>頭の正面の真ん中（拳の向きが回転の中心を通る）。</summary>
    internal static Vector3 Centre(Fighter f) => f.HeadPosition + (f.Frame.Forward * f.HalfExtents.Head.Z);

    /// <summary>正面から水平に、拳に <paramref name="mass"/> kg が乗った速さ <paramref name="speed"/> の 1 発を当てる。</summary>
    internal static void Hit(BoxingGame game, Fighter victim, Vector3 point, float mass, float speed, int? part = null, HitZone zone = HitZone.Chin)
    {
        var velocity = -victim.Frame.Forward * speed;
        var damage = PunchMechanics.Damage(mass * speed) * PunchMechanics.Sharpness(speed);
        game.ApplyHit(new HitEvent(game.Player, Hand.Right, victim, zone, point, speed, mass, 0.7f, damage), part ?? victim.Parts.Head, velocity);
    }

    /// <summary>打たず守らない 2 人のボットで、立って落ち着いた試合。</summary>
    internal static (BoxingGame Game, Fighter Cpu) NewGame(int seed = 1)
    {
        var still = BotCatalog.Boxer with { Attacks = false, DefenseRate = 0f, Prediction = 0f, Aggression = 0f };
        var corners = new BoxingCorners("still", s => new BoxerBot(still, s), s => new BoxerBot(still, s));
        var game = new BoxingGame(seed: seed, corners: corners);
        for (var t = 0f; t < 2.518f; t += Dt)
        {
            game.Step(default, Dt);
        }

        return (game, game.Cpu);
    }
}
