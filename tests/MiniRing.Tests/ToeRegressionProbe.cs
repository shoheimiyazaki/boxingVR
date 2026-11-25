using System.Globalization;
using MiniRing.Weapons;
using MiniRing.Bots;

namespace MiniRing.Tests;

/// <summary>
/// 調査用（2026-11-26、つま先の蝶番 10.72 → 45 Hz で動いたテスト）: 同じ場面を前のつま先と固いつま先、いくつもの種で比べる。
/// 種 1 つの上限すれすれ（たまたま）か、体の動きが一方へずれた（系統）かを分ける。
/// </summary>
internal static class ToeVariants
{
    internal const float Dt = 1f / 240f;

    internal static readonly (string Name, FighterTuning Tuning)[] Toes =
    [
        ("toe 10.72", FighterTuning.Default with { ToeHingeHz = 10.72f }),
        ("toe 45", FighterTuning.Default with { ToeHingeHz = 45f }),
    ];

    internal static string F(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);
}
[TestClass]
[TestCategory("Probe")]
public sealed class ToeStandingFallsProbe
{
    [TestMethod]
    public void StandingFalls()
    {
        // 立つだけの 2 人（15 秒）: 骨盤の流れ・倒れたか。
        foreach (var (name, tuning) in ToeVariants.Toes)
        {
            foreach (var seed in new[] { 1, 2, 3, 4, 5, 6 })
            {
                using var game = new BoxingGame(tuning, seed: seed, corners: new BoxingCorners("still", _ => new StandGuard(), _ => new StandGuard()));
                var start = game.Player.PelvisPosition with { Y = 0f };
                var falls = 0;
                for (var t = 0f; t < 15f; t += ToeVariants.Dt)
                {
                    var up = game.Player.State == FighterState.Standing;
                    game.Step(default, ToeVariants.Dt);
                    falls += up && game.Player.State != FighterState.Standing ? 1 : 0;
                }

                Console.WriteLine(ToeVariants.F($"{name} seed {seed}: pelvis drift {((game.Player.PelvisPosition with { Y = 0f }) - start).Length() * 100f:0.0} cm, falls {falls}, steps {game.Player.Footwork.Steps}"));
            }
        }
    }
}

[TestClass]
[TestCategory("Probe")]
public sealed class ToeThinLegsAgainstAGuardProbe
{
    [TestMethod]
    public void ThinLegsAgainstAGuard()
    {
        // CharacterTests.LegStrength の場面（細い脚の CPU と、構えて立つだけの相手）を種で。
        foreach (var (name, tuning) in ToeVariants.Toes)
        {
            foreach (var seed in new[] { 13, 14, 15, 16, 17, 18 })
            {
                using var thin = new BoxingGame(tuning, seed: seed, corners: BoxingCorners.Default with { CpuBody = new BodyBuild(Muscle: 0.7f), Player = _ => new StandGuard() });
                var downAt = -1f;
                var hitsTaken = 0;
                for (var i = 0; i < 1611; i++)
                {
                    thin.Step(default, ToeVariants.Dt);
                    hitsTaken += thin.RecentHits.Count(h => h.Victim == thin.Cpu);
                    if (downAt < 0f && thin.Cpu.IsKnockedDown)
                    {
                        downAt = i * ToeVariants.Dt;
                        Console.WriteLine(ToeVariants.F($"  {name} seed {seed}: knocked down at {downAt:0.00} s after {hitsTaken} hits taken, tone {thin.Cpu.Tone:0.00}, consciousness {thin.Cpu.Consciousness:0.00}"));
                    }
                }

                Console.WriteLine(ToeVariants.F($"{name} seed {seed}: down at {downAt:0.00} s, hits taken {hitsTaken}"));
            }
        }
    }
}

[TestClass]
[TestCategory("Probe")]
public sealed class ToeSwordBotFirstCutProbe
{
    [TestMethod]
    public void SwordBotFirstCut()
    {
        // SwordTests.TheSwordBot の場面（構えて立つ相手に剣のボット）を種で。
        foreach (var (name, tuning) in ToeVariants.Toes)
        {
            foreach (var seed in new[] { 1, 2, 3, 4, 5, 6 })
            {
                SwordBot? bot = null;
                using var game = new BoxingGame(tuning with { OxygenStorePerKg = 304_200f }, seed: seed, corners: new BoxingCorners("stand", _ => new StandGuard(), s => bot = SwordBot.Basic(s), PlayerWeapon: WeaponKind.IronSword));
                var start = game.Cpu.Voxels!.Parts.Sum(p => p.Body.Grid.FilledCount);
                var downAt = -1f;
                var hitsBefore = 0;
                for (var f = 0; f < 806 && !game.Cpu.IsDead; f++)
                {
                    game.Step(default, 1f / 60f);
                    if (downAt < 0f)
                    {
                        hitsBefore += game.RecentHits.Count(h => h.Victim == game.Cpu) + game.RecentCuts.Count;
                    }

                    if (downAt < 0f && game.Cpu.State != FighterState.Standing)
                    {
                        downAt = f / 60f;
                    }
                }

                Console.WriteLine(ToeVariants.F($"{name} seed {seed}: techniques {string.Join(",", bot!.Used)}, down at {downAt:0.00} s (contacts before {hitsBefore}), cells cut {start - game.Cpu.Voxels!.Parts.Sum(p => p.Body.Grid.FilledCount)}"));
            }
        }
    }
}

[TestClass]
[TestCategory("Probe")]
public sealed class ToeRoundhouseShinContactProbe
{
    private static readonly int[] Settles = [360, 480, 604, 720, 840];

    [TestMethod]
    public void RoundhouseShinContact()
    {
        foreach (var (name, tuning) in ToeVariants.Toes)
        {
            foreach (var (settle, side) in Settles.SelectMany(w => new[] { (w, Hand.Right), (w, Hand.Left) }))
            {
                var r = KneeControlTests.RoundhouseAtStandoff(tuning, 1, side, settle);
                Console.WriteLine(ToeVariants.F($"{name} wait {settle * ToeVariants.Dt:0.00} s {side}: {r.Hits} hits, main at {r.Main?.Fraction:0.00} of the shin, {r.Main?.Energy:0} J, chests {r.Distance:0.000} m apart at the start, standing {r.Standing}"));
            }
        }
    }
}

[TestClass]
[TestCategory("Probe")]
public sealed class ToeCrossEnergyProbe
{
    [TestMethod]
    public void CrossEnergy()
    {
        foreach (var (name, tuning) in ToeVariants.Toes)
        {
            var weak = tuning with { PelvisRotatorTorque = 60f };
            foreach (var range in new[] { 0.95f, 1.0f, 1.05f, 1.1f, 1.15f })
            {
                Console.WriteLine(ToeVariants.F($"{name} range {range:0.00}: quick {PunchWeightTests.Cross(tuning, 0.06f, 1, range):0} (weak {PunchWeightTests.Cross(weak, 0.06f, 1, range):0}), loaded {PunchWeightTests.Cross(tuning, 0.25f, 1, range):0} (weak {PunchWeightTests.Cross(weak, 0.25f, 1, range):0}) J"));
            }

            foreach (var seed in new[] { 1 })
            {
                Console.WriteLine(ToeVariants.F($"{name} seed {seed}: quick {PunchWeightTests.Cross(tuning, 0.06f, seed):0} (weak {PunchWeightTests.Cross(weak, 0.06f, seed):0}), loaded {PunchWeightTests.Cross(tuning, 0.25f, seed):0} (weak {PunchWeightTests.Cross(weak, 0.25f, seed):0}) J"));
            }
        }
    }
}

[TestClass]
[TestCategory("Probe")]
public sealed class ToeElbowGapProbe
{
    [TestMethod]
    public void ElbowGap()
    {
        foreach (var (name, tuning) in ToeVariants.Toes)
        {
            using (var game = new BoxingGame(tuning))
            {
                foreach (var part in game.Player.Voxels!.Parts.Where(p => p.Kind is BodyPartKind.UpperArm or BodyPartKind.Forearm))
                {
                    Console.WriteLine(ToeVariants.F($"{part.Kind}: cell {part.Body.Grid.CellSize.X * 1000f:0.0} x {part.Body.Grid.CellSize.Y * 1000f:0.0} x {part.Body.Grid.CellSize.Z * 1000f:0.0} mm"));
                }
            }

            foreach (var seed in new[] { 1 })
            {
                var g = ElbowGapTests.MaxGap(tuning, seed);
                Console.WriteLine(ToeVariants.F($"{name} seed {seed}: elbow gap {g.Max * 1000f:0.0} mm at {g.MaxT:0.000} s, first hit at {g.HitT:0.000} s, max before it {g.BeforeHit * 1000f:0.0} mm"));
            }
        }
    }
}

[TestClass]
[TestCategory("Probe")]
public sealed class ToeCpuSwingsProbe
{
    [TestMethod]
    public void CpuSwings()
    {
        foreach (var (name, tuning) in ToeVariants.Toes)
        {
            foreach (var seed in new[] { 1, 2, 3, 4, 5, 6 })
            {
                using var game = new BoxingGame(tuning with { OxygenStorePerKg = 304_225f }, seed: seed);
                var swings = 0;
                var cpuDown = 0;
                for (var i = 0; i < 2014; i++)
                {
                    var up = game.Cpu.State == FighterState.Standing;
                    game.Step(default, 1f / 60f);
                    swings += game.RecentSwings.Count;
                    cpuDown += up && game.Cpu.State != FighterState.Standing ? 1 : 0;
                }

                Console.WriteLine(ToeVariants.F($"{name} seed {seed}: swings {swings}, cpu falls {cpuDown}, player state {game.Player.State}"));
            }
        }
    }
}

[TestClass]
[TestCategory("Probe")]
public sealed class ToeStillHmdProbe
{
    private static readonly float[] Hinges = [10.72f, 20f, 30f, 45f];

    [TestMethod]
    public void StillHmd()
    {
        foreach (var (name, tuning) in Hinges.Select(hz => ($"toe {hz}", FighterTuning.Default with { ToeHingeHz = hz })))
        {
            var (pelvis, head, later) = VrFirstPersonPhysicsTests.StillHmdDrift(tuning);
            Console.WriteLine(ToeVariants.F($"{name}: still HMD pelvis drift {pelvis * 100f:0.0} cm (later half {later * 100f:0.0} cm), head {head * 100f:0.0} cm"));
        }
    }
}

[TestClass]
[TestCategory("Probe")]
public sealed class ToeDeepKnockdownRiseProbe
{
    [TestMethod]
    public void DeepKnockdownRise()
    {
        foreach (var (name, tuning) in ToeVariants.Toes)
        {
            foreach (var seed in new[] { 1, 2, 3, 4 })
            {
                using var game = DownRecoveryTests.StillGame(tuning, seed);
                var (ground, stand) = DownRecoveryTests.Settle(game);
                game.Cpu.Concussion = 0.95f;
                float? kneelAt = null, uprightAt = null, standingAt = null;
                float cKneel = 0f, cUpright = 0f;
                for (var t = 0f; t < 20.14f && standingAt is null; t += ToeVariants.Dt)
                {
                    game.Step(default, ToeVariants.Dt);
                    var f = game.Cpu;
                    var ratio = (f.PelvisPosition.Y - ground) / stand;
                    if (kneelAt is null && t > 0.8392f && ratio > 0.4f) { kneelAt = t; cKneel = f.Consciousness; }
                    if (uprightAt is null && kneelAt is not null && ratio > 0.8f) { uprightAt = t; cUpright = f.Consciousness; }
                    if (t > 0.8392f && f.State == FighterState.Standing) { standingAt = t; }
                }

                Console.WriteLine(ToeVariants.F($"{name} seed {seed}: 40% at {kneelAt:0.00} s (consciousness {cKneel:0.00}), 80% at {uprightAt:0.00} s ({cUpright:0.00}), standing at {standingAt:0.00} s"));
            }
        }
    }
}

[TestClass]
[TestCategory("Probe")]
public sealed class ToeRushFadeProbe
{
    [TestMethod]
    public void RushFade()
    {
        foreach (var (name, tuning) in ToeVariants.Toes)
        {
            foreach (var seed in new[] { 3, 4, 5 })
            {
                var m = StaminaRushTests.RushWindows(20f, tuning: tuning, seed: seed);
                Console.WriteLine(ToeVariants.F($"{name} seed {seed}: rush {m[0]:0.00} {m[1]:0.00} {m[2]:0.00} {m[3]:0.00} m/s"));
            }
        }
    }
}

[TestClass]
[TestCategory("Probe")]
public sealed class ToeHeelStompProbe
{
    [TestMethod]
    public void HeelStomp()
    {
        foreach (var (name, tuning) in ToeVariants.Toes)
        {
            foreach (var seed in new[] { 1, 2, 3, 4, 5, 6 })
            {
                var sole = HeelStompTests.Drill(heel: false, seed, tuning: tuning);
                var heel = HeelStompTests.Drill(heel: true, seed, tuning: tuning);
                Console.WriteLine(ToeVariants.F($"{name} seed {seed}: sole hits {sole.FootHits} head {sole.HeadHits}; heel hits {heel.FootHits} head {heel.HeadHits} falls {heel.AttackerFalls}"));
            }
        }
    }
}

[TestClass]
[TestCategory("Probe")]
public sealed class ToeHeelSwitchProbe
{
    [TestMethod]
    public void HeelSwitch()
    {
        foreach (var (name, tuning) in ToeVariants.Toes)
        {
            foreach (var seed in new[] { 1, 2, 3, 4, 5, 6 })
            {
                var r = HeelStompTests.SwitchRun(seed, tuning);
                Console.WriteLine(ToeVariants.F($"{name} seed {seed}: first {r.First} after {r.After} hits after {r.HitsAfter} falls {r.Falls}"));
            }
        }
    }
}

[TestClass]
[TestCategory("Probe")]
public sealed class ToeCreepProbe
{
    [TestMethod]
    public void Creep()
    {
        // 立つだけの体の流れ: つま先の蝶番の固さと、つま先のサーボの力の上限を替える。
        var variants = new (string Name, FighterTuning Tuning)[]
        {
            ("toe 10.72, servo 20", FighterTuning.Default with { ToeHingeHz = 10.72f }),
            ("toe 45, servo 20", FighterTuning.Default with { ToeHingeHz = 45f }),
            ("toe 45, servo 10", FighterTuning.Default with { ToeHingeHz = 45f, ToeMaxTorque = 10f }),
            ("toe 45, servo 5", FighterTuning.Default with { ToeHingeHz = 45f, ToeMaxTorque = 5f }),
            ("toe 10.72, servo 5", FighterTuning.Default with { ToeHingeHz = 10.72f, ToeMaxTorque = 5f }),
        };
        foreach (var (name, tuning) in variants)
        {
            using var game = new BoxingGame(tuning, seed: 1, corners: new BoxingCorners("still", _ => new StandGuard(), _ => new StandGuard()));
            var start = game.Player.PelvisPosition with { Y = 0f };
            var feet = new[] { game.World.GetState(game.Player.HeelId(0)).Position, game.World.GetState(game.Player.HeelId(1)).Position };
            for (var t = 0f; t < 15f; t += ToeVariants.Dt)
            {
                game.Step(default, ToeVariants.Dt);
            }

            var (pelvis, head, _) = VrFirstPersonPhysicsTests.StillHmdDrift(tuning);
            var footMoves = Enumerable.Range(0, 2).Select(i => ((game.World.GetState(game.Player.HeelId(i)).Position - feet[i]) with { Y = 0f }).Length() * 100f);
            Console.WriteLine(ToeVariants.F($"{name}: stand 15 s pelvis {((game.Player.PelvisPosition with { Y = 0f }) - start).Length() * 100f:0.0} cm, steps {game.Player.Footwork.Steps}, heels moved {string.Join(" / ", footMoves.Select(m => m.ToString("0.0", CultureInfo.InvariantCulture)))} cm; still HMD pelvis {pelvis * 100f:0.0} cm head {head * 100f:0.0} cm"));
        }
    }
}

[TestClass]
[TestCategory("Probe")]
public sealed class ToeKickDistanceProbe
{
    private static readonly float[] BackOffs = [-0.02f, -0.01f, 0f, 0.01f, 0.02f, 0.03f];

    [TestMethod]
    public void KickDistance()
    {
        foreach (var (name, tuning) in ToeVariants.Toes)
        {
            foreach (var back in BackOffs)
            {
                var l = KneeControlTests.RoundhouseAtStandoff(tuning, 1, Hand.Left, backOff: back);
                var r = KneeControlTests.RoundhouseAtStandoff(tuning, 1, Hand.Right, backOff: back);
                var turning = KickContactTests.Run(KickTechnique.Roundhouse, closeIn: -back, tuning: tuning);
                Console.WriteLine(ToeVariants.F($"{name} back off {back * 100f:0} cm: kneecontrol L {l.Main?.Fraction:0.00} ({l.Distance:0.000} m) R {r.Main?.Fraction:0.00} ({r.Distance:0.000} m); kickcontact roundhouse parts [{string.Join(",", turning.Parts.Zip(turning.Energies!, (p, e) => $"{p} {e:0}J"))}] main {turning.MainPart} {turning.AttackerState}"));
            }
        }
    }
}
