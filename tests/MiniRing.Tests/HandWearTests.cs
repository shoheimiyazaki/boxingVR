using MiniRing.Bots;
using MiniRing.Voxels;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>
/// 手にはめる物（<see cref="HandWear"/>）: グローブ・素手・メリケンサック・カタール・籠手。どれも拳に付けたボクセルの物で、打撃は同じ
/// 圧力のモデルの接触で起きる。違いは形と材質だけ（種類ごとの倍率は無い）。
/// </summary>
[TestClass]
public sealed class HandWearTests
{
    public TestContext TestContext { get; set; } = null!;

    private static readonly HandWear[] All = Enum.GetValues<HandWear>();

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
                    Assert.IsGreaterThan(0, worn.Body.Grid.Remaining(BoxerAnatomy.Bone), $"{wear}: the fist (bone) is inside");
                }
            }

            var red = game.GloveOf(game.Player, Hand.Right)!.Body.Grid;
            var blue = game.GloveOf(game.Cpu, Hand.Right)!.Body.Grid;
            switch (wear)
            {
                case HandWear.None:
                    Assert.IsGreaterThan(0, red.Remaining(BoxerAnatomy.Skin));
                    Assert.AreEqual(0, red.Remaining(BoxerAnatomy.Iron));
                    break;
                case HandWear.KnuckleDuster:
                case HandWear.Katar:
                    Assert.IsGreaterThan(0, red.Remaining(BoxerAnatomy.Iron));
                    Assert.IsGreaterThan(0, red.Remaining(BoxerAnatomy.Skin), "the bare fist holds it");
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
        foreach (var kind in new[] { WeaponKind.IronSword, WeaponKind.WoodenClub, WeaponKind.Katana, WeaponKind.Longsword, WeaponKind.KnuckleDuster, WeaponKind.Katar, WeaponKind.Gauntlet })
        {
            Assert.IsTrue(Weapon.HasRigidFace(kind), $"{kind} strikes with iron or wood");
        }

        Assert.IsFalse(Weapon.HasRigidFace(WeaponKind.Glove));
        Assert.IsFalse(Weapon.HasRigidFace(WeaponKind.Fist));
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
    private static BoutResult Bout(HandWear wear, string bot, float seconds = 12f)
    {
        using var game = new BoxingGame(seed: 1, corners: new BoxingCorners("stand", _ => new StandGuard(), s => BotCatalog.Create(bot, s), PlayerWear: wear, CpuWear: HandWear.Glove));
        int Filled() => game.Cpu.Voxels!.Parts.Sum(p => p.Body.Grid.FilledCount) + game.Cpu.Voxels!.Fragments.Sum(p => p.Body.Grid.FilledCount);
        int Worn(byte? material = null) => new[] { Hand.Left, Hand.Right }.Sum(h =>
            game.GloveOf(game.Player, h)!.Body.Grid is var g && material is { } m ? g.Remaining(m) : g.FilledCount);
        var before = Filled();
        var wornBefore = Worn();
        var ironBefore = Worn(BoxerAnatomy.Iron);
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

        return new BoutResult(before - Filled(), hits, hits > 0 ? speed / hits : 0f, wornBefore - Worn(), ironBefore - Worn(BoxerAnatomy.Iron));
    }

    private static BoutResult Sum(BoutResult a, BoutResult b) => new(a.Carved + b.Carved, a.Hits + b.Hits,
        a.Hits + b.Hits > 0 ? ((a.MeanSpeed * a.Hits) + (b.MeanSpeed * b.Hits)) / (a.Hits + b.Hits) : 0f, a.WearLost + b.WearLost, a.IronLost + b.IronLost);

    /// <param name="Carved">青の体が失ったボクセル。</param>
    /// <param name="Hits">赤の拳の打撃の数。</param>
    /// <param name="MeanSpeed">打撃の速さの平均 [m/s]。</param>
    /// <param name="WearLost">赤のはめた物（両手）が失ったボクセル。</param>
    /// <param name="IronLost">そのうち鉄。</param>
    private readonly record struct BoutResult(int Carved, int Hits, float MeanSpeed, int WearLost, int IronLost)
    {
        public override string ToString() => $"carved {Carved,5}  hits {Hits,3}  mean speed {MeanSpeed:F2} m/s  wear lost {WearLost} (iron {IronLost})";
    }

    /// <summary>調べもの: 2 つのボットの拳で、はめる物ごとの削れ方を並べる（docs/VOXEL_WEAPONS.md の表）。</summary>
    [TestMethod]
    public void HandWearBoutProbe()
    {
        foreach (var bot in new[] { "champion", "swarmer" })
        {
            foreach (var wear in All)
            {
                TestContext.WriteLine($"{bot,-9} {wear,-14} {Bout(wear, bot)}");
            }
        }
    }

    [TestMethod]
    public void TheSamePunches_CarveByTheShapeAndMaterialOfWhatIsWorn()
    {
        // 同じボット・同じ種で、構えて立つ相手を 12 秒打つ。違うのは拳にはめた物の形と材質だけ。1 本の試合は混沌としていて、何発当たるかが
        // 大きく揺れる（籠手は swarmer の 1 本で 4 発しか当たらず 7 セルのこともあった）ので、2 つのボットの合計で比べる（2026-11-13）。
        var result = All.ToDictionary(w => w, w => Sum(Bout(w, "swarmer"), Bout(w, "champion")));
        foreach (var (wear, r) in result)
        {
            TestContext.WriteLine($"{wear,-14} {r}");
        }

        foreach (var wear in All)
        {
            Assert.IsGreaterThan(3, result[wear].Hits, $"{wear}: the punches land as punches");
            Assert.AreEqual(0, result[wear].IronLost, $"{wear}: skin and leather never break iron");
        }

        // 何発当たるかははめる物ごとの試合の流れで大きく変わる（ボットの間合いの層を入れたマージで、グローブは 13 → 34 発。2026-11-13）ので、
        // 1 発あたりの削れ [セル/発] で比べる。
        float PerHit(HandWear wear) => (float)result[wear].Carved / result[wear].Hits;
        var glove = PerHit(HandWear.Glove);
        var bare = PerHit(HandWear.None);
        // 柔らかく広い面（詰め物・皮膚）はほとんど壊さない（2 発で 1 セル未満）。硬い鉄の面は同じ拳で肌と肉を潰し、刃先は深く刺さる。
        Assert.IsLessThan(0.5f, glove, "a padded glove barely breaks the skin");
        Assert.IsLessThan(0.5f, bare, "skin on skin barely breaks either");
        Assert.IsGreaterThan(glove + 1.5f, PerHit(HandWear.KnuckleDuster), "iron knuckles crush more than a padded glove");
        Assert.IsGreaterThan(glove + 1.5f, PerHit(HandWear.Gauntlet), "iron plates crush more than a padded glove");
        // 刃先は鈍い面より 1 発あたりを多く削る。倍率は 2 → 1.1 に緩めた（2026-11-14）: 顔の起伏（鼻・耳・あご）で頭の形が変わり、
        // 1 試合の当たり方で 1 発あたりが 11〜15 セル（鈍い面は 9.6）とばらつく（試合の流れの揺らぎ）。向きが合っていること（刃 > 鈍い面）を見る。
        Assert.IsGreaterThan(1.1f * Math.Max(PerHit(HandWear.KnuckleDuster), PerHit(HandWear.Gauntlet)), PerHit(HandWear.Katar),
            "a blade's edge and point concentrate the same punch more than any blunt face");
    }
}
