using System.Numerics;
using MiniRing.Characters;
using MiniRing.Desktop;

namespace MiniRing.Tests;

/// <summary>調査用: 武器を握った手の、前腕（肘 → 握り）の向きを武器のローカルで平均する（握る手の向きを決めるため）。</summary>
[TestClass]
[TestCategory("Probe")]
public sealed class GripHandProbe
{
    [TestMethod]
    [TestCategory("Probe")]
    public void ForearmInWeaponFrame()
    {
        foreach (var (red, blue) in new[] { ("edgar", "jin"), ("jin", "edgar"), ("konrad", "edgar"), ("daichi", "edgar"), ("sean", "edgar") })
        {
            var corners = new MatchSelection(MatchMode.Spectate, CharacterRoster.Get(red), CharacterRoster.Get(blue)).Corners with { UseWeaponBots = true };
            using var game = new BoxingGame(seed: 1, corners: corners);
            var sum = new Vector3[2];
            var n = new int[2];
            for (var i = 0; i < 900 && !game.RoundOver; i++)
            {
                game.Step(default, 1f / 60f);
                if (i < 120)
                {
                    continue;
                }

                foreach (var hand in new[] { Hand.Left, Hand.Right })
                {
                    if (game.Player.WieldedBody(hand) is not { } body || game.WeaponOf(game.Player) is not { } w || w.Body.BodyId != body)
                    {
                        continue;
                    }

                    var s = game.World.GetState(body);
                    var back = Vector3.Normalize(game.Player.ElbowPosition(hand) - game.Player.GloveCenter(hand));
                    sum[(int)hand] += Vector3.Transform(back, Quaternion.Conjugate(s.Orientation));
                    n[(int)hand]++;
                }
            }

            for (var h = 0; h < 2; h++)
            {
                if (n[h] > 0)
                {
                    var m = sum[h] / n[h];
                    Console.WriteLine($"{red} {(Hand)h}: elbow dir in weapon frame {m.X:0.00} {m.Y:0.00} {m.Z:0.00} (|m| {m.Length():0.00}, n {n[h]})");
                }
            }
        }
    }
}

[TestClass]
[TestCategory("Probe")]
public sealed class GripHandDimsProbe
{
    [TestMethod]
    [TestCategory("Probe")]
    public void FistBounds()
    {
        foreach (var pose in new[] { ("seiken", MiniRing.Weapons.HandSkeleton.PoseOf(HandShape.Seiken)), ("grip", MiniRing.Weapons.HandSkeleton.GripPose) })
        {
            _ = new MiniRing.Weapons.BareHandState().Placed(pose.Item2, out var grid);
            var b = grid.OccupiedBounds()!.Value;
            Console.WriteLine($"{pose.Item1}: min {b.Center - b.HalfExtents} max {b.Center + b.HalfExtents} wrist {MiniRing.Weapons.HandSkeleton.Wrist} gripC {MiniRing.Weapons.HandSkeleton.GripCenter} cells {grid.FilledCount} dims {grid.Dimensions}");
            // 断面（YZ）を X ごとに
            for (var x = 0; x < grid.Dimensions.X; x++)
            {
                var line = new System.Text.StringBuilder();
                for (var y = grid.Dimensions.Y - 1; y >= 0; y--)
                {
                    for (var z = 0; z < grid.Dimensions.Z; z++)
                    {
                        var m = grid.MaterialAt(grid.Index(x, y, z));
                        line.Append(m == 0 ? '.' : m == MiniRing.Voxels.BoxerAnatomy.HandBone ? 'B' : m == MiniRing.Voxels.BoxerAnatomy.HandSkin ? 's' : 'm');
                    }
                    line.Append('|');
                }
                Console.WriteLine($"x{x} ({grid.CellCenter(x, 0, 0).X * 1000:0}mm): {line}");
            }
        }
    }
}
