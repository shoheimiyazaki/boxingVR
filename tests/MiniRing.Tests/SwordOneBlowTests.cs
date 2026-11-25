using MiniRing.Arena;
using MiniRing.Bots;
using MiniRing.Characters;
using MiniRing.Motion;

namespace MiniRing.Tests;

/// <summary>
/// 剣士 3 人（迅・コンラート・エドガー）の教育の型（<see cref="WeaponStyle.Forms"/>、swordpractice で練習した経路の差分）は、無防備に立つ相手を
/// 教科書の型より多く 1 振りで無力化し、刃先は当たる直前に速い（docs/arena/swordpractice_2026-11-26_round*.md）。
/// 同じビルドの中の比べ（教育 vs 教科書）だけで、決まった数は持たない。強さは変えていない（§1.7）: 違うのは目標の経路と時計の速さだけ。
/// </summary>
[TestClass]
[TestCategory("slow")]
public sealed class SwordOneBlowTests
{
    /// <summary>体格の違う相手（基準・大きい・小さい・中くらい・いちばん太い）。</summary>
    private static readonly string[] Victims = ["standard", "konrad", "ramon", "joao", "dmitri"];

    [TestMethod]
    [DataRow("jin")]
    [DataRow("konrad")]
    [DataRow("edgar")]
    public void TheEducatedForm_IncapacitatesMoreDefenselessOpponents_WithAFasterBlade(string id)
    {
        var who = CharacterRoster.Get(id);
        var educated = who.WeaponStyle!;
        var textbook = WeaponCatalog.Textbook(who.Weapon!.Value);
        Assert.IsNotNull(educated.Forms, $"{id} has form tunes in the education");
        foreach (var (form, tune) in educated.Forms)
        {
            var before = Victims.Select(v => SwordPractice.Swing(who, textbook, SwordPractice.Technique(who, form, FormTune.Identity), 11, SwordPractice.Victim(v))).ToList();
            var after = Victims.Select(v => SwordPractice.Swing(who, educated, SwordPractice.Technique(who, form, tune), 11, SwordPractice.Victim(v))).ToList();
            var detail = $"{id} {form}: textbook {before.Count(o => o.Incapacitated)}/{Victims.Length} at {before.Average(o => o.ImpactSpeed):0.0} m/s, "
                + $"educated {after.Count(o => o.Incapacitated)}/{Victims.Length} at {after.Average(o => o.ImpactSpeed):0.0} m/s";
            Console.WriteLine(detail);
            Assert.IsTrue(after.All(o => o.Swung), $"the educated form is swung ({detail})");
            Assert.IsGreaterThan(before.Count(o => o.Incapacitated), after.Count(o => o.Incapacitated), $"more one-blow incapacitations ({detail})");
            Assert.IsGreaterThan(before.Average(o => o.ImpactSpeed), after.Average(o => o.ImpactSpeed), $"the blade is faster just before it lands ({detail})");
        }
    }
}
