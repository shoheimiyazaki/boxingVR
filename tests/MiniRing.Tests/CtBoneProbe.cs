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
        using var game = new BoxingGame(seed: 3, rules: MatchRules.Tv with { Rounds = 2, RoundSeconds = 1.678f, BreakSeconds = 23.5f },
            corners: new BoxingCorners("cpu", s => BotCatalog.Create("champion", s), s => BotCatalog.Create("swarmer", s),
                BreakSource: (seed, _) => new BreakRoutine(seed, null, ["coffee:R"])));
        var reported = new HashSet<string>();
        for (var i = 0; i < 3021; i++)
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

    /// <summary>
    /// 打たれた腕（前腕・上腕）の骨が、拳（グローブ・素手）の速さと拳の後ろの質量のどこから折れるか（BoneBreakTests.AGloveBlow… と同じ組み立て:
    /// 無重力の自由な部位の真ん中へ、拳を真横から当てる）。環境変数 CT_OLD_VOX があれば CT を入れる前の体でも。
    /// </summary>
    [TestMethod]
    public void ArmBreak_SpeedAndMassBehind()
    {
        var tuning = FighterTuning.Default;
        var bodies = new List<(string, BoxerVoxelOverrides?)> { ("ct", BoxerVoxelOverrides.Shipped) };
        if (Environment.GetEnvironmentVariable("CT_OLD_VOX") is { } old)
        {
            bodies.Insert(0, ("old", BoxerVoxelOverrides.Load(old)));
        }

        foreach (var (label, overrides) in bodies)
        {
            foreach (var (kind, half, mass) in new[]
            {
                (BodyPartKind.Forearm, tuning.HalfExtents.Forearm, tuning.Masses.Forearm),
                (BodyPartKind.UpperArm, tuning.HalfExtents.UpperArm, tuning.Masses.UpperArm),
            })
            {
                foreach (var wear in new[] { Weapons.WeaponKind.Glove, Weapons.WeaponKind.Fist })
                {
                    foreach (var behind in new[] { 0f, 10f, 20f, 40f, 80f })
                    {
                        var at = "none up to 20 m/s";
                        for (var speed = 1.678f; speed <= 20.15f; speed += 0.8392f)
                        {
                            if (Bends(kind, half, mass, wear, speed, behind, overrides))
                            {
                                at = string.Create(CultureInfo.InvariantCulture, $"{speed:0.0} m/s");
                                break;
                            }
                        }

                        TestContext.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{label} {kind} {wear} behind {behind:0} kg: breaks from {at}"));
                    }
                }
            }
        }

        static bool Bends(BodyPartKind kind, Vector3 half, float mass, Weapons.WeaponKind wear, float speed, float behind, BoxerVoxelOverrides? overrides)
        {
            var tuning = FighterTuning.Default;
            using var world = new Aigis.Physics.PhysicsWorld(gravity: Vector3.Zero);
            var id = world.AddDynamicBox(Vector3.Zero, Quaternion.Identity, half, mass);
            var limb = new Aigis.Physics.Voxels.VoxelBody(world, id, BoxerAnatomy.Build(kind, half, tuning.VoxelSize, null, null, overrides)) { BeamAxis = 0 };
            // 素手は拳頭（正拳の先）を下（-Z）へ向け、拳頭が部位の真ん中の真上に来るように置く（グローブは丸いので向きによらない）。
            var tip = Weapons.HandSkeleton.SeikenTip;
            var (place, turn) = wear == Weapons.WeaponKind.Fist
                ? (new Vector3(-tip.Z, -tip.Y, half.Z + 0.01408f + tip.X), Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f))
                : (new Vector3(0f, 0f, half.Z + 0.0845f), Quaternion.Identity);
            var fist = Weapons.Weapon.CreateWorn(world, wear, BoxerAnatomy.RedGlove, place, turn);
            world.SetLinearVelocity(fist.Body.BodyId, new Vector3(0f, 0f, -speed));
            world.SetPairCollisionEnabled(id, fist.Body.BodyId, false);
            for (var i = 0; i < 101; i++)
            {
                var sg = world.GetState(fist.Body.BodyId);
                var sl = world.GetState(id);
                world.Step(1f / 240f);
                var hit = Aigis.Physics.Voxels.VoxelContact.Resolve(fist.Body, sg, limb, sl, massBehindA: behind);

                if (hit.BentFor(limb) is not null)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// 試合の中で、拳が腕（肘・ガード）に当たった速さ・拳の後ろの質量の分布と、腕の骨が折れた数（グローブと素手、ボット同士の 100 秒 × 数試合）。
    /// </summary>
    [TestMethod]
    public void ArmHitsAndBreaks_InBouts()
    {
        foreach (var wear in new[] { Weapons.HandWear.Glove, Weapons.HandWear.None })
        {
            var armHits = new List<(float Speed, float Mass)>();
            var allSpeeds = new List<float>();
            var breaks = new List<string>();
            foreach (var (red, blue, seed) in new[] { ("champion", "swarmer", 1), ("slugger", "champion", 2), ("swarmer", "slugger", 3), ("champion", "counter", 4) })
            {
                using var game = new BoxingGame(seed: seed, rules: MatchRules.Tv with { Rounds = 1, RoundSeconds = 100.7f },
                    corners: new BoxingCorners("cpu", s => BotCatalog.Create(red, s), s => BotCatalog.Create(blue, s), PlayerWear: wear, CpuWear: wear));
                for (var i = 0; i < 6042; i++)
                {
                    game.Step(default, 1f / 60f);
                    foreach (var h in game.RecentHits.Where(h => !h.Kick && !h.Elbow))
                    {
                        allSpeeds.Add(h.Speed);
                        if (h.Zone is HitZone.Guard or HitZone.Elbow or HitZone.Shoulder)
                        {
                            armHits.Add((h.Speed, h.ImpactMass));
                        }
                    }

                    foreach (var b in game.BrokenBones.Where(b => b.Part is BodyPartKind.Forearm or BodyPartKind.UpperArm))
                    {
                        breaks.Add(string.Create(CultureInfo.InvariantCulture, $"{red}-{blue} t={i / 60f:F1}s {b.Victim.Name} {b.Part} bent={b.Bent}"));
                    }
                }
            }

            static string Q(IEnumerable<float> xs)
            {
                var s = xs.OrderBy(x => x).ToArray();
                return s.Length == 0 ? "none" : string.Create(CultureInfo.InvariantCulture,
                    $"n={s.Length} median {s[s.Length / 2]:0.00} p90 {s[(int)(s.Length * 0.9)]:0.00} max {s[^1]:0.00}");
            }

            TestContext.WriteLine($"{wear}: all punch speeds {Q(allSpeeds)}");
            TestContext.WriteLine($"{wear}: arm-hit speeds {Q(armHits.Select(a => a.Speed))}; impact mass {Q(armHits.Select(a => a.Mass))}");
            TestContext.WriteLine($"{wear}: arm bones broken {breaks.Count}");
            foreach (var b in breaks)
            {
                TestContext.WriteLine("  " + b);
            }
        }
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
                    while (force < 1e6f && Aigis.Physics.Voxels.VoxelBending.FindBreak(grid, point, -Vector3.UnitZ * force, axis, loadBearingOnly: true) is null)
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
