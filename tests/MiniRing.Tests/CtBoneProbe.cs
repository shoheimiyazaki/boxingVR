using System.Globalization;
using System.Numerics;
using MiniRing.Bots;
using MiniRing.Break;
using MiniRing.Voxels;

namespace MiniRing.Tests;

/// <summary>調査用: CT の体で、1 秒のラウンドのあいだに腕・脚の骨が折れるか（段 2 の骨の強さの較正）。</summary>
[TestClass]
[TestCategory("Probe")]
public sealed class CtBoneProbe
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void BonesAfterAShortRound()
    {
        using var game = new BoxingGame(seed: 3, rules: MatchRules.Tv with { Rounds = 2, RoundSeconds = 1f, BreakSeconds = 14f },
            corners: new BoxingCorners("cpu", s => BotCatalog.Create("champion", s), s => BotCatalog.Create("swarmer", s),
                BreakSource: (seed, _) => new BreakRoutine(seed, null, ["coffee:R"])));
        var reported = new HashSet<string>();
        for (var i = 0; i < 60 * 30; i++)
        {
            game.Step(default, 1f / 60f);
            foreach (var f in new[] { game.Player, game.Cpu })
            {
                var p = f.Parts;
                foreach (var (name, id) in new[] { ("LUpperArm", p.LeftUpperArm), ("RUpperArm", p.RightUpperArm), ("LForearm", p.LeftForearm), ("RForearm", p.RightForearm),
                             ("LThigh", p.LeftThigh), ("RThigh", p.RightThigh), ("LShin", p.LeftShin), ("RShin", p.RightShin) })
                {
                    var key = f.Name + name;
                    if (!f.Voxels!.BoneWhole(id) && reported.Add(key))
                    {
                        TestContext.WriteLine($"t={i / 60f:F2}s {f.Name} {name}: bone broken (fragments {f.Voxels.Fragments.Count}, inBreak {game.InBreak})");
                    }
                }
            }
        }

        TestContext.WriteLine($"done: broken {reported.Count}");
    }

    /// <summary>部位の真ん中を折る力（BoneBreakProbe.BreakingForce_ByPart と同じ）を、CT を入れる前の体（環境変数 CT_OLD_VOX）と今の体で。</summary>
    [TestMethod]
    public void BreakingForce_OldAndCt()
    {
        var tuning = FighterTuning.Default;
        var bodies = new List<(string, BoxerVoxelOverrides?)> { ("ct", BoxerVoxelOverrides.Shipped) };
        if (Environment.GetEnvironmentVariable("CT_OLD_VOX") is { } old)
        {
            bodies.Insert(0, ("old", BoxerVoxelOverrides.Load(old)));
        }

        foreach (var (label, overrides) in bodies)
        {
            foreach (var (kind, half) in new[]
            {
                (BodyPartKind.Forearm, tuning.HalfExtents.Forearm), (BodyPartKind.UpperArm, tuning.HalfExtents.UpperArm),
                (BodyPartKind.Shin, tuning.HalfExtents.Shin), (BodyPartKind.Thigh, tuning.HalfExtents.Thigh),
            })
            {
                var grid = BoxerAnatomy.Build(kind, half, tuning.VoxelSize, null, null, overrides);
                var axis = kind is BodyPartKind.Forearm or BodyPartKind.UpperArm ? 0 : 1;
                foreach (var at in new[] { -0.25f, 0f, 0.25f })
                {
                    var point = axis == 0 ? new Vector3(at * 2f * half.X, 0f, 0f) : new Vector3(0f, at * 2f * half.Y, 0f);
                    var force = 1f;
                    while (force < 1e5f && Aigis.Physics.Voxels.VoxelBending.FindBreak(grid, point, -Vector3.UnitZ * force, axis, loadBearingOnly: true) is null)
                    {
                        force *= 1.05f;
                    }

                    TestContext.WriteLine(string.Create(CultureInfo.InvariantCulture,
                        $"{label} {kind} at {at:+0.00;-0.00}: bone {grid.Remaining(BoxerAnatomy.Bone)} cells; breaks at {force:0} N"));
                }
            }
        }
    }
}
