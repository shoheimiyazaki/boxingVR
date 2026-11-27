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
            if (bots && i >= 180 && i <= 330 && i % 6 == 0)
            {
                var p = game.Player;
                var bot = game.PlayerControl as BoxerBot;
                Console.WriteLine(string.Create(inv,
                    $"  t {i / 60f:F2} red pelvis {(p.PelvisPosition.Y - Floor(p)) / stand[0]:F2} defense {bot?.CurrentDefense} headY {p.LastCommand.HeadOffset.Y / p.ArmReach:F3} arm move {p.LastCommand.ClampedMove.Length():F2} faint {p.Faintness:F2} tone {p.Tone:F2} state {p.State}"));
            }

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
    /// <summary>沈みの深さを体の物理の値で変えて比べる（ボット同士・コーナーから・種 4 と 5、始めの 8 秒の骨盤の最低と、骨盤の目標の最低）。</summary>
    [TestMethod]
    public void DropVariants()
    {
        var d = FighterTuning.Default;
        var variants = new (string Name, FighterTuning Tuning)[]
        {
            ("default", d),
            ("release x1.2", d with { DropReleaseDepth = d.DropReleaseDepth * 1.2f, DropReleaseDeadband = d.DropReleaseDeadband * 1.2f }),
            ("release x2", d with { DropReleaseDepth = d.DropReleaseDepth * 2f, DropReleaseDeadband = d.DropReleaseDeadband * 2f }),
            ("pull 0", d with { DropPull = 0f }),
            ("brake 0.5", d with { DropBrakeKeep = 0.5f }),
        };
        var inv = CultureInfo.InvariantCulture;
        foreach (var (name, tuning) in variants)
        {
            foreach (var seed in new[] { 4, 5, 6 })
            {
                var corners = new BoxingCorners("swarmer", s => BotCatalog.Create("swarmer", s), s => BotCatalog.Create("champion", s)) with { Ring = RingLayout.Boxing.AtCorners() };
                using var game = new BoxingGame(tuning, seed: seed, corners: corners);
                float Floor(Fighter f) => MathF.Min(f.AnklePosition(Hand.Left).Y, f.AnklePosition(Hand.Right).Y);
                var stand = new[] { game.Player.PelvisPosition.Y - Floor(game.Player), game.Cpu.PelvisPosition.Y - Floor(game.Cpu) };
                var low = new[] { 9f, 9f };
                var target = new[] { 9f, 9f };
                for (var i = 0; i < 480; i++)
                {
                    game.Step(default, 1f / 60f);
                    if (i < 120)
                    {
                        continue;
                    }

                    var fs = new[] { game.Player, game.Cpu };
                    for (var k = 0; k < 2; k++)
                    {
                        if (fs[k].State == FighterState.Standing)
                        {
                            low[k] = MathF.Min(low[k], (fs[k].PelvisPosition.Y - Floor(fs[k])) / stand[k]);
                            target[k] = MathF.Min(target[k], (fs[k].HoverHeight + (fs[k].PelvisPosition.Y - Floor(fs[k]) - fs[k].PelvisPosition.Y)) / stand[k]);
                        }
                    }
                }

                Console.WriteLine(string.Create(inv, $"DROP {name} seed {seed}: red low {low[0]:F2} blue low {low[1]:F2}"));
            }
        }
    }
}
