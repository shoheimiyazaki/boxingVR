using System.Globalization;
using System.Numerics;
using Aigis.Physics;
using Aigis.Physics.Voxels;
using MiniRing.Bots;
using MiniRing.Voxels;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>
/// 調べもの: グローブの打撃が腕・脚の骨を曲げで折るか（<see cref="VoxelContact"/> の BentB）。実物の前腕・すね（<see cref="BoxerAnatomy"/>）を
/// 浮かせ、ボクセルのグローブを真横から当てる。速さと拳の後ろの質量（massBehind）を振って、折れる所と曲げ応力 / 引張の比を見る。
/// あわせて、ボット同士のグローブの試合で骨が折れる回数を数える。
/// </summary>
[TestClass]
public sealed class BoneBreakProbe
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void GloveAgainstLimb_SpeedAndMassBehind()
    {
        var tuning = FighterTuning.Default;
        foreach (var (kind, half, mass) in new[]
        {
            (BodyPartKind.Forearm, tuning.HalfExtents.Forearm, tuning.Masses.Forearm),
            (BodyPartKind.Shin, tuning.HalfExtents.Shin, tuning.Masses.Shin),
            (BodyPartKind.UpperArm, tuning.HalfExtents.UpperArm, tuning.Masses.UpperArm),
        })
        {
            TestContext.WriteLine($"{kind}: half {half} mass {mass:0.000} kg");
            foreach (var speed in new[] { 1f, 2f, 3f, 4f, 6f })
            {
                var line = $"  v {speed:0.0} m/s:";
                foreach (var behind in new[] { 0f, 0.5f, 1f, 2f, 4f })
                {
                    var (bent, ratio, force) = Hit(kind, half, mass, speed, behind);
                    line += string.Create(CultureInfo.InvariantCulture, $"  M{behind:0.0}: {(bent ? "BREAK" : "ok")} F {force:0} N");
                }

                TestContext.WriteLine(line);
            }
        }
    }

    [TestMethod]
    public void BreakingForce_ByPart()
    {
        var tuning = FighterTuning.Default;
        foreach (var (kind, half) in new[]
        {
            (BodyPartKind.Forearm, tuning.HalfExtents.Forearm), (BodyPartKind.UpperArm, tuning.HalfExtents.UpperArm),
            (BodyPartKind.Shin, tuning.HalfExtents.Shin), (BodyPartKind.Thigh, tuning.HalfExtents.Thigh),
        })
        {
            var grid = BoxerAnatomy.Build(kind, half, tuning.VoxelSize);
            var axis = kind is BodyPartKind.Forearm or BodyPartKind.UpperArm ? 0 : 1;
            var across = axis == 0 ? Vector3.UnitZ : Vector3.UnitZ;
            var force = 1f;
            while (force < 1e5f && Aigis.Physics.Voxels.VoxelBending.FindBreak(grid, Vector3.Zero, -across * force, axis, loadBearingOnly: true) is null)
            {
                force *= 1.1f;
            }

            var k = Aigis.Physics.Voxels.VoxelBending.BeamStiffness(grid, across, axis, loadBearingOnly: true) ?? 0f;
            // 折る力 F を出すのに要るエネルギー（梁のたわみに貯まる分）: F² / (2k)。
            TestContext.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{kind}: dims {grid.Dimensions} bone {grid.Remaining(BoxerAnatomy.Bone)} cells; breaks at {force:0} N in the middle; k {k:0} N/m; energy to break {force * force / (2f * k):0.000} J"));
        }
    }

    private static (bool Bent, float Ratio, float Force) Hit(BodyPartKind kind, Vector3 half, float mass, float speed, float behind)
    {
        using var world = new PhysicsWorld(gravity: Vector3.Zero);
        var grid = BoxerAnatomy.Build(kind, half, FighterTuning.Default.VoxelSize);
        var id = world.AddDynamicBox(Vector3.Zero, Quaternion.Identity, half, mass);
        var axis = kind is BodyPartKind.Forearm or BodyPartKind.UpperArm ? 0 : 1;
        var limb = new VoxelBody(world, id, grid) { BeamAxis = axis };
        // 骨の軸に直角（Z）から、部位の真ん中へ。
        var glove = Weapon.CreateWorn(world, WeaponKind.Glove, BoxerAnatomy.RedGlove, new Vector3(0f, 0f, half.Z + 0.03f), Quaternion.Identity);
        world.SetLinearVelocity(glove.Body.BodyId, new Vector3(0f, 0f, -speed));
        world.SetPairCollisionEnabled(id, glove.Body.BodyId, false);
        var bent = false;
        var ratio = 0f;
        var force = 0f;
        for (var i = 0; i < 60 && !bent; i++)
        {
            var sg = world.GetState(glove.Body.BodyId);
            var sl = world.GetState(id);
            world.Step(1f / 240f);
            var r = VoxelContact.Resolve(glove.Body, sg, limb, sl, massBehindA: behind);
            force = MathF.Max(force, r.PassedForce);
            if (r.BentFor(limb) is { } b)
            {
                bent = true;
                ratio = b.Ratio;
            }
        }

        return (bent, ratio, force);
    }

    [TestMethod]
    public void GloveBouts_BreakRate()
    {
        int bouts = 0, breaks = 0, severs = 0;
        var byPart = new Dictionary<BodyPartKind, int>();
        foreach (var (red, blue) in new[] { ("champion", "slugger"), ("swarmer", "champion"), ("slugger", "swarmer"), ("champion", "champion") })
        {
            foreach (var seed in new[] { 1, 2, 3 })
            {
                using var game = new BoxingGame(seed: seed, corners: new BoxingCorners(blue, s => BotCatalog.Create(blue, s), s => BotCatalog.Create(red, s)));
                var lines = new List<string>();
                for (var t = 0f; t < 60f && !game.RoundOver; t += 1f / 60f)
                {
                    game.Step(default, 1f / 60f);
                    foreach (var b in game.BrokenBones)
                    {
                        breaks++;
                        byPart[b.Part] = byPart.GetValueOrDefault(b.Part) + 1;
                        lines.Add(string.Create(CultureInfo.InvariantCulture, $"{t:0.0}s {(b.Victim == game.Player ? "red" : "blue")} {b.Part}"));
                    }

                    severs += game.RecentSevers.Count;
                }

                bouts++;
                TestContext.WriteLine($"{red} vs {blue} seed {seed}: {string.Join(" | ", lines)}");
            }
        }

        TestContext.WriteLine($"{bouts} bouts x 60 s: {breaks} broken bones ({string.Join(", ", byPart.Select(kv => $"{kv.Key} {kv.Value}"))}), {severs} severed");
    }

    [TestMethod]
    public void GloveBouts_CountBrokenBones()
    {
        foreach (var seed in new[] { 1, 2, 3 })
        {
            using var game = new BoxingGame(seed: seed, corners: new BoxingCorners("slugger", s => BotCatalog.Create("slugger", s), s => BotCatalog.Create("champion", s)));
            var breaks = new List<string>();
            var maxMassBehind = 0f;
            for (var t = 0f; t < 60f && !game.RoundOver; t += 1f / 60f)
            {
                game.Step(default, 1f / 60f);
                breaks.AddRange(game.BrokenBones.Select(b => string.Create(CultureInfo.InvariantCulture, $"{t:0.0}s {b.Victim.Name} {b.Part}")));
                breaks.AddRange(game.RecentSevers.Select(s => string.Create(CultureInfo.InvariantCulture, $"{t:0.0}s sever {s.Strength:0.00}")));
                maxMassBehind = MathF.Max(maxMassBehind, game.LastHit?.ImpactMass ?? 0f);
            }

            TestContext.WriteLine($"seed {seed}: {breaks.Count} breaks; max impact mass {maxMassBehind:0.00} kg; {string.Join(" | ", breaks)}");
        }
    }
}
