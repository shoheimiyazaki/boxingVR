using Aigis.Foundation.Voxels;
using MiniRing.Bots;
using MiniRing.Characters;
using MiniRing.Desktop;

namespace MiniRing.Tests;

/// <summary>調査用: 脚を断たれた体と、その相手が、その後どう打ち合うか（状態・腰の高さ・当たった数）を 1 秒ごとに書く。</summary>
[TestClass]
public sealed class LegLossProbe
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow("shin")]
    [DataRow("thighs")]
    public void Probe_BoutAfterLegLoss(string cut)
    {
        using var game = new BoxingGame(seed: 3, corners: new BoxingCorners("bout", s => BotCatalog.Create("champion", s), s => BotCatalog.Create("swarmer", s)));
        const float dt = 1f / 60f;
        for (var t = 0; t < 20 * 60; t++)
        {
            if (t == 2 * 60)
            {
                if (cut == "shin")
                {
                    Cut(game, game.Cpu, game.Cpu.Parts.LeftShin, 0.4f);
                }
                else
                {
                    Cut(game, game.Cpu, game.Cpu.Parts.LeftThigh, 0.25f);
                    Cut(game, game.Cpu, game.Cpu.Parts.RightThigh, 0.25f);
                }
            }

            game.Step(default, dt);
            if (t % 60 == 59)
            {
                TestContext.WriteLine($"{(t + 1) / 60,2}s cpu {game.Cpu.State,-10} hips {game.Cpu.HoverHeight:F3} head {game.Cpu.HeadPosition.Y:F3} | player {game.Player.State,-10} hips {game.Player.HoverHeight:F3} head {game.Player.HeadPosition.Y:F3} | cons {game.Cpu.Consciousness:F2}/{game.Player.Consciousness:F2} msg {game.Message}");
            }
        }
    }

    /// <summary>
    /// 記録したチェックサムの試合（CharacterSamenessCases）で、脚・腕を失った最初の時刻を書く。PhysicsVersion 39 で値が変わった試合が、
    /// 脚を失った（または切り株の断面の測り方が効く腕を失った）試合だけかを確かめる。
    /// </summary>
    [TestMethod]
    public void Probe_WhichRecordedMatchesLoseALimb()
    {
        foreach (var c in CharacterSamenessCases.All.Concat(CharacterSamenessCases.Weapons))
        {
            foreach (var legacy in new[] { true, false })
            {
                var selection = new MatchSelection(c.Mode, CharacterRoster.Get(c.Red), CharacterRoster.Get(c.Blue), c.Referee);
                var corners = selection.Corners;
                if (legacy)
                {
                    corners = corners with { PlayerWeaponStyle = CharacterSamenessCases.LegacyWeaponStyle(corners.PlayerWeapon), CpuWeaponStyle = CharacterSamenessCases.LegacyWeaponStyle(corners.CpuWeapon) };
                }

                using var game = new BoxingGame(corners: corners);
                var steps = (int)(c.Seconds * 60f);
                float? leg = null, arm = null;
                var half = 0UL;
                for (var i = 1; i <= steps; i++)
                {
                    game.Step(default, 1f / 60f);
                    if (i == steps / 2)
                    {
                        half = game.StateChecksum();
                    }

                    leg ??= game.Cpu.LegSevered || game.Player.LegSevered ? i / 60f : null;
                    arm ??= game.Cpu.ArmSevered(Hand.Left) || game.Cpu.ArmSevered(Hand.Right) || game.Player.ArmSevered(Hand.Left) || game.Player.ArmSevered(Hand.Right) ? i / 60f : null;
                }

                TestContext.WriteLine($"{c.Name,-30} legacy={legacy,-5} half 0x{half:X16} end 0x{game.StateChecksum():X16} halfAt {steps / 2 / 60f:F1}s leg {(leg is { } l ? $"{l:F1}" : "-")} arm {(arm is { } a ? $"{a:F1}" : "-")}");
            }
        }
    }

    private static void Cut(BoxingGame game, Fighter fighter, int part, float fromNear)
    {
        var voxel = fighter.Voxels!.PartOf(part)!;
        var grid = voxel.Body.Grid;
        var axis = voxel.Body.BeamAxis ?? 1;
        var n = axis == 0 ? grid.Dimensions.X : axis == 1 ? grid.Dimensions.Y : grid.Dimensions.Z;
        var k = Math.Clamp((int)MathF.Round(fromNear * (n - 1)), 1, n - 2);
        var slice = voxel.DistalHigh ? k : n - 1 - k;
        var removed = new List<RemovedVoxel>();
        for (var i = 0; i < grid.CellCount; i++)
        {
            var (x, y, z) = grid.Coordinates(i);
            if ((axis == 0 ? x : axis == 1 ? y : z) == slice && grid.MaterialAt(i) != 0)
            {
                grid.Break(i, removed);
            }
        }

        if (fighter.Voxels.BreakIfSevered(part) is { } broken)
        {
            game.BreakBone(fighter, broken, 1f);
        }
    }
}
