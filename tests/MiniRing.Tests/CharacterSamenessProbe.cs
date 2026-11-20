namespace MiniRing.Tests;

/// <summary>
/// 調査用（スイートの外）: 名簿の組の試合のチェックサムを、今の教科書（SAME）・武器の教科書を鍛える前（LEGACY）・さらに戦略の層を外した（NOPLAN-LEGACY）で書く。
/// ある変更で値の変わる組・変わらない組を調べるのに使う（変更の前後で回して比べる）。2026-11-20 から <see cref="CharacterSamenessTests"/> は金型を持たないので、版を上げても取り直しは要らない。
/// `bash tools/remote-test.sh probe:CharacterSamenessProbe`、または `--filter "FullyQualifiedName~CharacterSamenessProbe" --logger "console;verbosity=detailed"`。
/// </summary>
[TestClass]
[TestCategory("Probe")]
public sealed class CharacterSamenessProbe
{
    [TestMethod]
    public void PrintChecksums()
    {
        foreach (var c in CharacterSamenessCases.All.Concat(CharacterSamenessCases.Weapons))
        {
            var (half, end) = CharacterSamenessCases.Run(c);
            Console.WriteLine($"SAME {c.Name} half=0x{half:X16} end=0x{end:X16}");
            var (lHalf, lEnd) = CharacterSamenessCases.Run(c, legacyWeapons: true);
            Console.WriteLine($"LEGACY {c.Name} half=0x{lHalf:X16} end=0x{lEnd:X16}");
            var (nHalf, nEnd) = CharacterSamenessCases.Run(c, legacyWeapons: true, gamePlan: false);
            Console.WriteLine($"NOPLAN-LEGACY {c.Name} half=0x{nHalf:X16} end=0x{nEnd:X16}");
        }
    }
}
