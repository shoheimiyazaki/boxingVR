using MiniRing.Bots;

namespace MiniRing.Tests;

/// <summary>調べもの: 構えて止まった受け手に右ストレートが頭へ数えられる割合（EvasionBasicsTests の対照）を種ごとに。</summary>
[TestClass]
[TestCategory("Probe")]
public sealed class AimedPunchProbe
{
    [TestMethod]
    public void OpenHeadRate_BySeed()
    {
        var inside = new DefenseMove { HeadX = 0.5f, HeadY = -0.5f, HeadZ = -0.2f, Step = 1f, Duration = 0.5035f, Rate = 35.75f, HandsUp = 0.3f, VsStraight = 1f };
        var book = DefenseBook.Default.With(Defense.Duck, inside);
        static void KeepFresh(BoxingGame game, BoxerBot _)
        {
            game.Player.Stamina = 1f;
            game.Cpu.Stamina = 1f;
        }

        for (var seed = 1; seed <= 8; seed++)
        {
            var r = DefenseDrill.Punch("2", Defense.Duck, DefenseDrill.Defender(book, defend: false, counters: false), seed: seed, seconds: 13.43f, telegraph: 0.1343f, observe: KeepFresh);
            Console.WriteLine($"seed {seed}: {r}");
        }
    }
}
