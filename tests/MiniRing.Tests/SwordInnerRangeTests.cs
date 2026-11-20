using MiniRing.Bots;
using MiniRing.Motion;
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

    /// <summary>剣先が欠けても（握った剣が短くなっても）、柄頭打ちと前蹴りの間合いは縮まない（2026-11-20、SwordExpansionTests から移した）。</summary>
    [TestMethod]
    [DataRow(WeaponKind.Katana, "tsuka-ate", "mae-geri")]
    [DataRow(WeaponKind.Longsword, "knaufschlag", "tritt")]
    public void LosingTheBladeTip_DoesNotShortenThePommelOrFoot(WeaponKind weapon, string pommel, string kick)
    {
        using var game = new BoxingGame();
        var close = FormSwordsman.Techniques(weapon, game.Player.ArmReach, 0f).Where(t => t.Name == pommel || t.Name == kick).ToList();
        Assert.HasCount(2, close, "both close techniques exist");
        foreach (var technique in close)
        {
            var bot = new SwordsmanBot([technique], FormSwordsman.Guard(weapon), 1, SwordStance.Rate, exact: true);
            var sense = game.PlayerSense with
            {
                OpponentChest = game.PlayerSense.Self.Origin + (game.PlayerSense.Self.Forward * technique.Range),
                SelfWeapon = new WeaponSense(true, 0.2f, 0.3f),
            };
            bot.Read(sense, 1f / 60f);
            Assert.IsNotNull(bot.Current, $"{technique.Name}: 剣先が 10 cm 欠けても脚・柄頭の間合いで出せる");
        }
    }

    /// <summary>
    /// 柄頭打ちは剣の握りより手前・腕の 1/3 未満の間合いで、前蹴りは蹴りの教科書の間合い（KickSpec）をそのまま使い、刃の技のどれより遠くから出す。
    /// 蹴る間、剣は構えより下へ下がらない（蹴り足の通り道から上へ外す）。2026-11-20、SwordExpansionTests から移した。
    /// </summary>
    [TestMethod]
    [DataRow(WeaponKind.Katana, "tsuka-ate", "mae-geri")]
    [DataRow(WeaponKind.Longsword, "knaufschlag", "tritt")]
    public void CloseTechniques_UseThePommelAndTheExistingFrontKick(WeaponKind weapon, string pommel, string kick)
    {
        using var game = new BoxingGame();
        var reach = game.Player.ArmReach;
        var techniques = FormSwordsman.Techniques(weapon, reach, 0f);
        var hit = techniques.Single(t => t.Name == pommel);
        Assert.IsLessThan(Weapon.GripOf(weapon).X, MotionAim.StrikingEndX(hit, reach));
        Assert.IsLessThan(0.33f, hit.Range);
        Assert.AreEqual(hit.Range / reach, MotionAim.Landing(hit, reach).Point.Z, 0.0001f);
        var front = techniques.Single(t => t.Name == kick);
        Assert.AreEqual(KickSpec.Of(KickTechnique.Front).Standoff * reach, front.Range, 0.0001f);
        var time = front.Motion.Duration * KickSpec.ContactShare;
        var command = MotionImitation.Command(front.Motion, time, game.PlayerSense, System.Numerics.Vector3.UnitZ, SwordStance.Rate, FighterTuning.Default.WalkSpeed, FighterTuning.Default.MaxTorsoTwistRadians);
        Assert.IsNotNull(command.RightLeg);
        Assert.IsGreaterThanOrEqualTo((front.Motion.Frames[0].Right * reach).Y - 0.0001f, command.Right.TargetLocal.Y, "蹴る間、剣は構えより下へ下がらない（蹴り足の通り道から上へ外す）");
        Assert.IsLessThan(techniques.Where(t => t.Motion.Kick is null).Max(t => t.Range), front.Range);
    }

    /// <summary>刀・ロングソードの型は、aimlab の実測で手・肩・顔・胴・脚のどこかを打てる型を持ち、全部の型が同じ 7 部位で測ってある（2026-11-20、SwordExpansionTests から移した）。</summary>
    [TestMethod]
    public void TheMeasuredSkills_CoverTheHandsShouldersFaceBodyAndLegs()
    {
        using var game = new BoxingGame();
        var techniques = new[] { WeaponKind.Katana, WeaponKind.Longsword }.SelectMany(w => FormSwordsman.Techniques(w, game.Player.ArmReach, 0f)).ToList();
        foreach (var target in new[] { BodyPartKind.Forearm, BodyPartKind.UpperArm, BodyPartKind.Head, BodyPartKind.Chest, BodyPartKind.Thigh })
        {
            Assert.IsTrue(techniques.Any(t => t.Skill?.GetValueOrDefault(target) > 0f), $"{target}: aimlab の実測で打てる型を少なくとも一つ持つ");
        }

        Assert.IsTrue(techniques.All(t => t.Skill is { Count: 7 }), "全型を同じ 7 部位・5 構えで測る");
    }
}
