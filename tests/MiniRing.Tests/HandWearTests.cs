using System.Numerics;
using Aigis.Foundation.Voxels;
using Aigis.Physics;
using Aigis.Physics.Voxels;
using MiniRing.Bots;
using MiniRing.Voxels;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>
/// 手にはめる物（<see cref="HandWear"/>）: グローブ・素手・メリケンサック・カタール・籠手。どれも拳に付けたボクセルの物で、打撃は同じ
/// 圧力のモデルの接触で起きる。違いは形と材質だけ（種類ごとの倍率は無い）。
/// </summary>
[TestClass]
[TestCategory("slow")]
public sealed class HandWearTests
{
    public TestContext TestContext { get; set; } = null!;

    internal static readonly HandWear[] All = Enum.GetValues<HandWear>();

    [TestMethod]
    public void EveryWear_IsBuilt_AndWornOnBothHands_InTheCornerColour()
    {
        foreach (var wear in All)
        {
            using var game = new BoxingGame(seed: 1, corners: BoxingCorners.Default with { PlayerWear = wear, CpuWear = wear });
            foreach (var fighter in new[] { game.Player, game.Cpu })
            {
                foreach (var hand in new[] { Hand.Left, Hand.Right })
                {
                    Assert.IsTrue(fighter.WearsGlove(hand), $"{wear}: worn on the {hand} hand");
                    Assert.IsFalse(fighter.Wields(hand), $"{wear}: worn, not wielded (bots punch with it like a fist)");
                    var worn = game.GloveOf(fighter, hand);
                    Assert.IsNotNull(worn);
                    Assert.AreEqual(wear.Kind(), worn.Kind);
                    Assert.IsNull(game.WeaponOf(fighter), "a worn thing is not a held weapon");
                    // 中の拳: 手にはめる物は物の格子に重ねた手の格子（PhysicsVersion 53。素手の手のモデル）、素手・グローブは自分の格子の中。
                    var inside = worn.Hands.Count > 0 ? worn.Hands[0].Body.Grid : worn.Body.Grid;
                    Assert.IsGreaterThan(0, inside.Remaining(BoxerAnatomy.Bone) + inside.Remaining(BoxerAnatomy.HandBone), $"{wear}: the fist (bone) is inside");
                }
            }

            var red = game.GloveOf(game.Player, Hand.Right)!.Body.Grid;
            var blue = game.GloveOf(game.Cpu, Hand.Right)!.Body.Grid;
            switch (wear)
            {
                case HandWear.None:
                    Assert.IsGreaterThan(0, red.Remaining(BoxerAnatomy.HandSkin), "the bare fist is hand skin");
                    Assert.AreEqual(0, red.Remaining(BoxerAnatomy.Iron));
                    break;
                case HandWear.KnuckleDuster:
                case HandWear.Katar:
                    Assert.IsGreaterThan(0, red.Remaining(BoxerAnatomy.Iron));
                    Assert.IsGreaterThan(0, game.GloveOf(game.Player, Hand.Right)!.Hands[0].Body.Grid.Remaining(BoxerAnatomy.HandSkin), "the bare fist (its own grid) holds it");
                    break;
                case HandWear.Gauntlet:
                    Assert.IsGreaterThan(0, red.Remaining(BoxerAnatomy.Iron));
                    Assert.IsGreaterThan(0, red.Remaining(BoxerAnatomy.Padding));
                    Assert.IsGreaterThan(0, red.Remaining(BoxerAnatomy.RedGlove));
                    Assert.IsGreaterThan(0, blue.Remaining(BoxerAnatomy.BlueGlove));
                    break;
                case HandWear.Glove:
                    Assert.IsGreaterThan(0, blue.Remaining(BoxerAnatomy.BlueGlove));
                    break;
            }
        }
    }

    [TestMethod]
    public void Mass_FollowsTheVoxels_IronIsHeavierThanTheBareFist()
    {
        var mass = new Dictionary<HandWear, float>();
        foreach (var wear in All)
        {
            using var game = new BoxingGame(seed: 1, corners: BoxingCorners.Default with { PlayerWear = wear });
            mass[wear] = game.World.GetMass(game.GloveOf(game.Player, Hand.Right)!.Body.BodyId);
        }

        TestContext.WriteLine(string.Join(", ", mass.Select(kv => $"{kv.Key} {kv.Value * 1000f:F1} g")));
        Assert.IsLessThan(mass[HandWear.KnuckleDuster], mass[HandWear.None]);
        Assert.IsLessThan(mass[HandWear.Katar], mass[HandWear.None]);
        Assert.IsLessThan(mass[HandWear.Gauntlet], mass[HandWear.None]);
        Assert.IsLessThan(mass[HandWear.Gauntlet], mass[HandWear.Glove], "iron plates weigh more than leather and foam");
    }

    [TestMethod]
    public void TheArmsPush_PassesThroughRigidFacesOnly()
    {
        // 打つ面の材質から: 鉄・木は腕の押す力を伝え、革・皮膚はその下の詰め物・肉が散らす。握る武器はこれまでどおり伝える。
        foreach (var kind in new[] { WeaponKind.IronSword, WeaponKind.WoodenClub, WeaponKind.Katana, WeaponKind.Longsword, WeaponKind.KnuckleDuster, WeaponKind.Katar, WeaponKind.Gauntlet, WeaponKind.Shield })
        {
            Assert.IsTrue(Weapon.HasRigidFace(kind), $"{kind} strikes with iron or wood");
        }

        Assert.IsFalse(Weapon.HasRigidFace(WeaponKind.Glove));
        // 素手の拳の組織は木の物性（ヤング率を持つ。PhysicsVersion 51、docs/DEBT.md「手の物性を木の値で代用」）なので、材質から決まる硬い面になる。
        Assert.IsTrue(Weapon.HasRigidFace(WeaponKind.Fist), "the bare fist has wood-valued tissue");
    }

    [TestMethod]
    public void HostNames_Parse()
    {
        Assert.AreEqual(HandWear.Glove, HandWears.Parse("glove"));
        Assert.AreEqual(HandWear.None, HandWears.Parse("none"));
        Assert.AreEqual(HandWear.KnuckleDuster, HandWears.Parse("Knuckles"));
        Assert.AreEqual(HandWear.Katar, HandWears.Parse("katar"));
        Assert.AreEqual(HandWear.Gauntlet, HandWears.Parse("gauntlet"));
        Assert.ThrowsExactly<ArgumentException>(() => HandWears.Parse("chainsaw"));
    }

    /// <summary>
    /// 赤（<paramref name="wear"/> をはめたボット <paramref name="bot"/>）が、構えて立つだけの青（グローブ）を <paramref name="seconds"/> 秒打つ。
    /// どのはめる物でも、ボットは同じ拳の技（同じ種）で打つ。
    /// </summary>
    internal static BoutResult Bout(HandWear wear, string bot, float seconds = 20.14f, int seed = 1)
    {
        using var game = new BoxingGame(seed: seed, corners: new BoxingCorners("stand", _ => new StandGuard(), s => BotCatalog.Create(bot, s), PlayerWear: wear, CpuWear: HandWear.Glove));
        // 2026-11-24（PhysicsVersion 93）: 接触はセルを丸ごとでなく、奥行きを押し抜けた分だけ壊す（途中で止まれば刻み目 = 丈夫さの減りが残る）。
        // 失ったセルに、残ったセルの丈夫さの減りをセルの数に換算して足す（拳頭で割れかけた皮膚も「削った量」）。
        static float Intact(Aigis.Foundation.Voxels.VoxelGrid g)
        {
            var sum = 0f;
            for (var i = 0; i < g.CellCount; i++)
            {
                var m = g.MaterialAt(i);
                if (m != 0)
                {
                    sum += Math.Clamp(g.HealthAt(i) / MathF.Max(1e-6f, g.Palette[m].Toughness), 0f, 1f);
                }
            }

            return sum;
        }

        int Filled() => (int)MathF.Round(game.Cpu.Voxels!.Parts.Sum(p => Intact(p.Body.Grid)) + game.Cpu.Voxels!.Fragments.Sum(p => Intact(p.Body.Grid)));
        int Worn(byte? material = null) => new[] { Hand.Left, Hand.Right }.Sum(h =>
            game.GloveOf(game.Player, h)!.Body.Grid is var g && material is { } m ? g.Remaining(m) : g.FilledCount);
        var before = Filled();
        var wornBefore = Worn();
        // 鉄の「壊れた」数: 折れて分かれた先（細い刃先が曲げで折れた: VoxelContact.Snap）は鉄のまま別の物として残る（game.Weapons の破片）ので失っていない。
        // 数えるのは、潰れた・削られて無くなった鉄だけ（2026-11-19、PhysicsVersion 59: champion 種 3 の 2.2 秒に、カタールの先の鉄 2 セルが折れて分かれた。
        // 胸の皮膚に当たった曲げで、打撃の記録は無い。削れて消えた鉄は 0）。
        int Iron() => game.Weapons.Sum(w => w.Body.Grid.Remaining(BoxerAnatomy.Iron));
        var ironBefore = Iron();
        var hits = 0;
        var speed = 0f;
        for (var t = 0f; t < seconds; t += 1f / 60f)
        {
            game.Step(default, 1f / 60f);
            foreach (var hit in game.RecentHits.Where(h => h.Attacker == game.Player))
            {
                hits++;
                speed += hit.Speed;
            }
        }

        return new BoutResult(before - Filled(), hits, hits > 0 ? speed / hits : 0f, wornBefore - Worn(), ironBefore - Iron());
    }

    private static BoutResult Sum(BoutResult a, BoutResult b) => new(a.Carved + b.Carved, a.Hits + b.Hits,
        a.Hits + b.Hits > 0 ? ((a.MeanSpeed * a.Hits) + (b.MeanSpeed * b.Hits)) / (a.Hits + b.Hits) : 0f, a.WearLost + b.WearLost, a.IronLost + b.IronLost);

    /// <param name="Carved">青の体が失ったボクセル。</param>
    /// <param name="Hits">赤の拳の打撃の数。</param>
    /// <param name="MeanSpeed">打撃の速さの平均 [m/s]。</param>
    /// <param name="WearLost">赤のはめた物（両手）が失ったボクセル。</param>
    /// <param name="IronLost">そのうち鉄。</param>
    internal readonly record struct BoutResult(int Carved, int Hits, float MeanSpeed, int WearLost, int IronLost)
    {
        public override string ToString() => $"carved {Carved,5}  hits {Hits,3}  mean speed {MeanSpeed:F2} m/s  wear lost {WearLost} (iron {IronLost})";
    }

    [TestMethod]
    [DataRow(3f, 0f)]
    [DataRow(3f, 25f)]
    [DataRow(5f, 0f)]
    [DataRow(5f, 25f)]
    [DataRow(7f, 0f)]
    [DataRow(7f, 25f)]
    public void PaddedGlove_TransmitsLessPressureThanAnEquallyShapedRigidFace(float speed, float angle)
    {
        var padded = Contact(WeaponKind.Glove, speed, angle);
        var rigid = Contact(WeaponKind.Glove, speed, angle, rigid: true);
        Assert.AreEqual(speed * MathF.Cos(angle * MathF.PI / 180f), padded.ApproachSpeed, 0.001f);
        Assert.AreEqual(padded.ApproachSpeed, rigid.ApproachSpeed, 0.001f);
        Assert.IsLessThan(rigid.Pressure, padded.Pressure, "the padded face reduces peak pressure");
    }

    [TestMethod]
    [DataRow(5f)]
    [DataRow(7f)]
    public void PaddedGlove_ReducesPressureAndKeepsTheSurfaceIntact_OnAGlancingBlow(float speed)
    {
        const float angle = 25f;
        var padded = Contact(WeaponKind.Glove, speed, angle);
        var bare = Contact(WeaponKind.Fist, speed, angle);
        Assert.AreEqual(bare.ApproachSpeed, padded.ApproachSpeed, 0.001f);
        // 拳の実寸と当たる面積によって皮膚が破れる速さは変わる。同じ速度での接触圧を比べる。
        Assert.IsLessThan(bare.Pressure, padded.Pressure, "padding lowers the peak pressure compared to the bare fist");
        Assert.IsEmpty(padded.RemovedB, "the padded glove leaves the surface intact");
    }

    [TestMethod]
    public void PaddedGlove_ReducesSubsurfaceCrushing_InTheModerateHeadOnBlow()
    {
        const float speed = 5f;
        var padded = Contact(WeaponKind.Glove, speed, 0f);
        var rigid = Contact(WeaponKind.Glove, speed, 0f, rigid: true);
        Assert.IsEmpty(padded.RemovedB);
        Assert.IsLessThan(rigid.CrushedB?.Count ?? 0, padded.CrushedB?.Count ?? 0);
    }

    private static VoxelContactResult Contact(WeaponKind kind, float speed, float angle, bool rigid = false)
    {
        // Both strikers get the same mass and motion. Remapping the glove allows a materials-only comparison.
        using var world = new PhysicsWorld(gravity: Vector3.Zero);
        var glove = Weapon.Build(kind);
        if (rigid)
        {
            glove.Remap((_, material, _) => material == 0 ? material : BoxerAnatomy.Iron);
        }

        var target = new VoxelGrid(BoxerAnatomy.Palette, new Vector3(0.07042f), 0.01408f,
            (p, _) => p.X < -0.05634f ? BoxerAnatomy.Skin : BoxerAnatomy.Muscle);
        var radians = angle * MathF.PI / 180f;
        var direction = new Vector3(MathF.Cos(radians), MathF.Sin(radians), 0f);
        var start = new Vector3(-0.14f, 0f, 0f) - (direction * 0.12f);
        var strikerId = world.AddDynamicBox(start, glove.HalfExtents, 0.8f);
        var targetId = world.AddDynamicBox(Vector3.Zero, target.HalfExtents, 10f);
        var striker = new VoxelBody(world, strikerId, glove);
        var flesh = new VoxelBody(world, targetId, target);
        world.SetPairCollisionEnabled(strikerId, targetId, false);
        world.SetLinearVelocity(strikerId, direction * speed);
        for (var step = 0; step < 80; step++)
        {
            var strikerBefore = world.GetState(strikerId);
            var targetBefore = world.GetState(targetId);
            world.Step(1f / 240f);
            var hit = VoxelContact.Resolve(striker, strikerBefore, flesh, targetBefore, massBehindA: 3f);
            if (hit.Touched)
            {
                return hit;
            }
        }

        throw new AssertFailedException("the controlled blow did not reach the target");
    }

    [TestMethod]
    public void WornMaterials_DifferInTotalBoutDamage()
    {
        // 同じボット・種でも道具によって立ち回りと接触の頻度が変わるため、ここでは試合全体の差を見る。
        var seeds = new[] { 1, 2, 3, 4, 5, 6 };
        var bots = new[] { "swarmer", "champion" };
        var jobs = All.SelectMany(w => seeds.SelectMany(seed => bots.Select(bot => (w, seed, bot)))).ToArray();
        var bouts = new BoutResult[jobs.Length];
        Parallel.For(0, jobs.Length, i => bouts[i] = Bout(jobs[i].w, jobs[i].bot, 20.14f, jobs[i].seed));
        var result = All.ToDictionary(w => w, w => Enumerable.Range(0, jobs.Length).Where(i => jobs[i].w == w).Select(i => bouts[i]).Aggregate(Sum));
        foreach (var (wear, r) in result)
        {
            TestContext.WriteLine($"{wear,-14} {r}");
        }

        foreach (var wear in All)
        {
            Assert.IsGreaterThan(3, result[wear].Hits, $"{wear}: the punches land as punches");
            Assert.AreEqual(0, result[wear].IronLost, $"{wear}: skin and leather never break iron");
        }

        // 1 発あたりで比べる: 道具によって当たる数が違う（素手は 69 発、グローブは 111 発）。2026-11-24（PhysicsVersion 93、セルは押し抜いた分だけ壊れる）から、
        // 合計では素手 617 対グローブ 618 とほぼ並んだが、1 発あたりでは素手 8.9 対グローブ 5.6 セル。
        static float PerHit(BoutResult r) => r.Hits > 0 ? (float)r.Carved / r.Hits : 0f;
        var glove = PerHit(result[HandWear.Glove]);
        Assert.IsGreaterThan(glove, PerHit(result[HandWear.None]), "bare fists remove more cells per punch than gloves");
        Assert.IsGreaterThan(glove, PerHit(result[HandWear.KnuckleDuster]), "iron knuckles remove more cells per punch than gloves");
        Assert.IsGreaterThan(glove, PerHit(result[HandWear.Gauntlet]), "iron plates remove more cells per punch than gloves");
        Assert.IsGreaterThan(glove, PerHit(result[HandWear.Katar]), "the blade removes more cells per punch than gloves");
    }
}
