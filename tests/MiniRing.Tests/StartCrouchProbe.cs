using System.Globalization;
using MiniRing.Bots;

namespace MiniRing.Tests;

/// <summary>
/// 調べ物（2026-11-27、基準の体の移行）: 試合の始めに深くしゃがむか。構えて立つだけの 2 人と、champion 対 swarmer のボットを、コーナーから・中央から始めて、
/// 骨盤と頭の高さ（立った高さに対する割合）を時間で出す。
/// </summary>
[TestClass]
[TestCategory("Probe")]
public sealed class StartCrouchProbe
{
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void PelvisHeight_FromTheStart(bool fromCorners, bool bots)
    {
        var corners = bots
            ? new BoxingCorners("swarmer", s => BotCatalog.Create("swarmer", s), s => BotCatalog.Create("champion", s))
            : new BoxingCorners("guard", _ => new StandGuard(), _ => new StandGuard());
        if (fromCorners)
        {
            corners = corners with { Ring = RingLayout.Boxing.AtCorners() };
        }

        using var game = new BoxingGame(seed: 4, corners: corners);
        var inv = CultureInfo.InvariantCulture;
        float Floor(Fighter f) => MathF.Min(f.AnklePosition(Hand.Left).Y, f.AnklePosition(Hand.Right).Y);
        var stand = new[] { game.Player.PelvisPosition.Y - Floor(game.Player), game.Cpu.PelvisPosition.Y - Floor(game.Cpu) };
        Console.WriteLine(string.Create(inv, $"corners {fromCorners} bots {bots}: spawn pelvis above ankle red {stand[0]:F3} blue {stand[1]:F3}"));
        for (var i = 0; i <= 480; i++)
        {
            if (i % 30 == 0)
            {
                var p = game.Player;
                var c = game.Cpu;
                Console.WriteLine(string.Create(inv,
                    $"t {i / 60f:F2} red pelvis {(p.PelvisPosition.Y - Floor(p)) / stand[0]:F2} head {p.HeadPosition.Y:F2} stamina {p.Stamina:F2} | blue pelvis {(c.PelvisPosition.Y - Floor(c)) / stand[1]:F2} head {c.HeadPosition.Y:F2} stamina {c.Stamina:F2}"));
            }

            game.Step(default, 1f / 60f);
        }
    }
}
