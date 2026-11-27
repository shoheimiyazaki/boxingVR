using MiniRing.Arena;
using MiniRing.Bots;
using MiniRing.Characters;

namespace MiniRing.Tests;

/// <summary>
/// 刃の通り道（<see cref="BladeCutChooser"/>）と今の型を、剣士の 1 振り（<see cref="SwordOneBlowTests"/> と同じ測り方）で比べる調査。
/// 環境変数 BLADECUT_SEEDS（既定 11,12）・BLADECUT_VICTIMS（既定 standard,konrad,ramon,joao,dmitri）で種と相手を替えて比べ直す。
/// </summary>
[TestClass]
[TestCategory("Probe")]
public sealed class BladeCutBotProbe
{
    [TestMethod]
    public void ReachOfThePlan()
    {
        Assert.IsTrue(MiniRing.Motion.BladeCutGeometry.TryFromWeapon(MiniRing.Weapons.WeaponKind.Katana, Hand.Right, true, out var geometry, contactFraction: 0.85f));
        Console.WriteLine($"chord {geometry.Chord} contact {geometry.ContactOffset} support {geometry.SupportOffset}");
        var arm = new MiniRing.Motion.BladeArm(new System.Numerics.Vector3(-0.19f, 0f, 0f), 0.071f, 0.581f);
        var start = new MiniRing.Motion.BladeKeyPose(new System.Numerics.Vector3(-0.14f, -0.19f, 0.43f), System.Numerics.Quaternion.CreateFromYawPitchRoll(-MathF.PI / 2, 0.6f, 0f));
        foreach (var y in new[] { -0.2f, 0f, 0.2f, 0.4f })
        {
            foreach (var z in new[] { 0.6f, 0.8f, 1.0f, 1.2f, 1.4f })
            {
                var travel = System.Numerics.Vector3.Normalize(new System.Numerics.Vector3(0f, -0.8f, 0.6f));
                var vertical = MiniRing.Motion.BladeCutPlan.TryCreate(new MiniRing.Motion.BladeCutRequest(new(0f, y, z), travel, System.Numerics.Vector3.UnitX), geometry, start, arm, arm with { Shoulder = new(0.19f, 0f, 0f) }, out _);
                var flat = MiniRing.Motion.BladeCutPlan.TryCreate(new MiniRing.Motion.BladeCutRequest(new(0f, y, z), System.Numerics.Vector3.UnitX, System.Numerics.Vector3.UnitY), geometry, start, arm, arm with { Shoulder = new(0.19f, 0f, 0f) }, out _);
                Console.WriteLine($"y {y} z {z}: vertical {vertical} flat {flat}");
            }
        }
    }

    [TestMethod]
    [DataRow("jin")]
    [DataRow("edgar")]
    [DataRow("konrad")]
    public void CompareOneBlow(string id)
    {
        var seeds = (Environment.GetEnvironmentVariable("BLADECUT_SEEDS") ?? "11,12").Split(',').Select(int.Parse).ToList();
        var victims = (Environment.GetEnvironmentVariable("BLADECUT_VICTIMS") ?? "standard,konrad,ramon,joao,dmitri").Split(',');
        var who = CharacterRoster.Get(id);
        var educated = who.WeaponStyle!;
        static float Env(string name, float fallback) => float.TryParse(Environment.GetEnvironmentVariable(name), System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback;
        var baseline = new BladeCutStyle();
        var cut = educated with { BladeCut = baseline with { Swing = Env("BLADECUT_SWING", baseline.Swing), Runup = Env("BLADECUT_RUNUP", baseline.Runup), Follow = Env("BLADECUT_FOLLOW", baseline.Follow), Preparation = Env("BLADECUT_PREP", baseline.Preparation) } };
        foreach (var form in educated.Forms!.Keys)
        {
            foreach (var (label, style) in new[] { ("form", educated), ("cut", cut) })
            {
                var technique = SwordPractice.Technique(who, form, educated.Forms[form]);
                var outcomes = seeds.SelectMany(s => victims.Select(v => SwordPractice.Swing(who, style, technique, s, SwordPractice.Victim(v)))).ToList();
                Console.WriteLine($"{id} {form} {label}: swung {outcomes.Count(o => o.Swung)}/{outcomes.Count} incap {outcomes.Count(o => o.Incapacitated)}/{outcomes.Count} "
                    + $"impact {outcomes.Average(o => o.ImpactSpeed):0.0} m/s tip {outcomes.Average(o => o.TipPeak):0.0} cells {outcomes.Average(o => o.Cells):0} selfCells {outcomes.Average(o => o.SelfCells):0.0}");
            }
        }
    }
}
