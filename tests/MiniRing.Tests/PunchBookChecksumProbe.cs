using System.Globalization;
using MiniRing.Bots;

namespace MiniRing.Tests;

/// <summary>調査用: 拳の試合 5 組（champion 対 各スタイル、30 秒）の最後の StateChecksum を出す。通常の実行から外す（Probe）。</summary>
[TestClass]
public sealed class PunchBookChecksumProbe
{
    [TestMethod]
    public void PrintFinalChecksums()
    {
        var lines = new List<string>();
        foreach (var blue in new[] { "swarmer", "jabber", "slugger", "counter", "outboxer" })
        {
            using var game = new BoxingGame(seed: 1, corners: new BoxingCorners("champion",
                s => BotCatalog.Create("champion", s), s => BotCatalog.Create(blue, s)));
            for (var step = 0; step < 1800; step++)
            {
                game.Step(default, 1f / 60f);
            }

            lines.Add(string.Create(CultureInfo.InvariantCulture, $"champion-vs-{blue}:seed1:1800:{game.StateChecksum():X16}"));
        }

        var path = Environment.GetEnvironmentVariable("MINIRING_CHECKSUM_OUT");
        if (!string.IsNullOrEmpty(path))
        {
            File.WriteAllLines(path, lines);
        }

        Console.WriteLine(string.Join(Environment.NewLine, lines));
    }
}
