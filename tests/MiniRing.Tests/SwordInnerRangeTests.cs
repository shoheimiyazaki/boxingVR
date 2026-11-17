using MiniRing.Bots;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>
/// 間合いの内側の技（柄頭打ち・前蹴り・押し斬り）: 歩いて詰める計画の候補は剣の間合い（MinReach）以上の技だけ。
/// すでに近いときだけ、全部の技から今の距離に間合いが合う技を選ぶ（距離という連続の量と教科書の重みだけで決まり、技の種類の分岐は無い）。
/// </summary>
[TestClass]
public sealed class SwordInnerRangeTests
{
    private const float ArmReach = 0.188f;

    private static SwordsmanBot Bot(WeaponKind weapon, int seed)
    {
        var style = WeaponCatalog.Textbook(weapon);
        var bot = FormSwordsman.Bot(weapon, seed, ArmReach, FighterTuning.Default, style);
        return (SwordsmanBot)bot.Armed;
    }

    [TestMethod]
    public void Katana_FarAway_PlansOnlyFromSwordReachForms_AndClose_PicksAFormThatFitsTheDistance()
    {
        var style = WeaponCatalog.Textbook(WeaponKind.Katana);
        var all = FormSwordsman.Techniques(WeaponKind.Katana, ArmReach, style.InnerMinReach!.Value);
        Assert.IsTrue(all.Any(t => t.Range < style.MinReach), "内側の技が技の集まりに入っている");

        for (var seed = 1; seed <= 40; seed++)
        {
            var far = Bot(WeaponKind.Katana, seed).PlanAt(1.2f);
            Assert.IsGreaterThanOrEqualTo(style.MinReach, far.Range, $"遠間では剣の間合いの技だけ（seed {seed}: {far.Name}）");
        }

        // 近い距離では、重みが 0 でない技のうち、間合いが今の距離の窓（SwordsmanBot.CloseWindow）に入る技（1 つも無ければいちばん近い技）から選ぶ。
        var chosenNames = new HashSet<string>();
        foreach (var distance in new[] { 0.12f, 0.2f })
        {
            var live = all.Where(t => style.TechniqueWeights!.GetValueOrDefault(t.Name, 1f) > 0f).ToList();
            var fitting = live.Where(t => MathF.Abs(t.Range - distance) <= SwordsmanBot.CloseWindow).Select(t => t.Name).ToHashSet();
            if (fitting.Count == 0)
            {
                fitting.Add(live.MinBy(t => MathF.Abs(t.Range - distance))!.Name);
            }

            for (var seed = 1; seed <= 40; seed++)
            {
                var chosen = Bot(WeaponKind.Katana, seed).PlanAt(distance);
                Assert.Contains(chosen.Name, fitting, $"距離 {distance} seed {seed}");
                chosenNames.Add(chosen.Name);
            }
        }

        Assert.IsGreaterThan(1, chosenNames.Count, "重みの付いた技がばらけて選ばれる");

        // 同じ種・同じ距離なら同じ技（決定的）。
        Assert.AreEqual(Bot(WeaponKind.Katana, 5).PlanAt(0.15f).Name, Bot(WeaponKind.Katana, 5).PlanAt(0.15f).Name);
    }

    [TestMethod]
    public void TechniquesOfTheSameReach_AreChosenByWeight_NotByListOrder()
    {
        var forms = FormSwordsman.Techniques(WeaponKind.Katana, ArmReach, 0f).Take(3).Select(t => t with { Range = 0.34f }).ToList();
        var weights = new Dictionary<string, float> { [forms[0].Name] = 1f, [forms[1].Name] = 1f, [forms[2].Name] = 0.0001f };
        var counts = new Dictionary<string, int>();
        for (var seed = 1; seed <= 200; seed++)
        {
            var bot = new SwordsmanBot(forms, FormSwordsman.Guard(WeaponKind.Katana), seed, SwordStance.Rate) { Weights = weights };
            var name = bot.NearestAt(0.34f).Name;
            counts[name] = counts.GetValueOrDefault(name) + 1;
        }

        Assert.IsGreaterThan(40, counts.GetValueOrDefault(forms[0].Name), "先頭の技");
        Assert.IsGreaterThan(40, counts.GetValueOrDefault(forms[1].Name), "先頭でない技も同じくらい選ばれる（並びの先頭へ偏らない）");
        Assert.IsLessThan(5, counts.GetValueOrDefault(forms[2].Name), "重みの小さい技はほとんど選ばれない");
    }

    [TestMethod]
    public void Longsword_InnerFormsAreWeightedOut_SoCloseOrFarItPlansTheSameForms()
    {
        // ロングソードの柄頭打ち・前蹴りは、自傷が大きく（柄頭）・当たらない（前蹴り）ので教科書で重み 0。内側の技が選べないときは、近くても計画の技のまま（今までと同じ）。
        var style = WeaponCatalog.Textbook(WeaponKind.Longsword);
        for (var seed = 1; seed <= 40; seed++)
        {
            Assert.IsGreaterThanOrEqualTo(style.MinReach, Bot(WeaponKind.Longsword, seed).PlanAt(0.15f).Range, $"seed {seed}");
        }
    }
}
