namespace MiniRing.Tests;

/// <summary>調査用: E0 の物差しのチェックサムを標準出力へ出す（`--filter "FullyQualifiedName~CharacterSamenessProbe" --logger "console;verbosity=detailed"`）。</summary>
[TestClass]
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
