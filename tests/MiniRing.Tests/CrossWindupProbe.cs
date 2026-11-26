using System.Globalization;
using System.Numerics;
using Aigis.Physics;
using MiniRing.Arena;

namespace MiniRing.Tests;

/// <summary>
/// 調査用（2026-11-26）: 溜め（予備動作 0.25 s）の右ストレートが、溜めなし（0.06 s）より重くならない原因。
/// <see cref="PunchWeightTests.Cross"/> と同じ場面（守らない相手・種 1）で、打ち出しの前から 0.30 s まで、体の動き・拳の速さ・当たりを時系列で出す。
/// (1) 溜めで体に貯まった量（<see cref="Fighter.BodySpringEnergy"/> と、胴の回る・進む運動エネルギー）と、拳・腕へ移った量。
/// (2) 当たる瞬間が拳の速さの山の前か後か（届かない間合い 1.40 の空振りで、拳の速さの山を別に取る）。
/// (3) 最初の当たりだけを数えることが溜めの差を消していないか（当たり全部・最大・当たる直前の腕の運動エネルギー）。
/// </summary>
[TestClass]
[TestCategory("Probe")]
public sealed class CrossWindupProbe
{
    private const float Dt = 1f / 240f;
    private const float Quick = 0.06f;
    private const float Loaded = 0.25f;
    private static readonly float[] Ranges = [0.95f, 1.0f, 1.05f, 1.1f, 1.15f];

    private static string F(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);

    private static readonly (string Name, FighterTuning Tuning)[] Bodies =
    [
        ("standard", FighterTuning.Default),
        ("weak hips", FighterTuning.Default with { PelvisRotatorTorque = 60f }),
        ("toe 10.72", FighterTuning.Default with { ToeHingeHz = 10.72f }),
    ];

    /// <summary>1 歩の記録。</summary>
    private readonly record struct Sample(
        float T, float GloveSpeed, float GloveToward, float ArmKe, float TrunkSpinKe, float TrunkLinKe,
        float ChestYaw, float PelvisYaw, float PelvisDrop, float SpringStore, float PelvisForward, float M4 = 0f, float M6 = 0f, float M8 = 0f);

    private readonly record struct Hit(float T, float Speed, float Mass, float Energy, HitZone Zone);

    private sealed record Run(string Body, float Telegraph, float Range, List<Sample> Samples, List<Hit> Hits, float Distance)
    {
        public Hit? First => Hits.Count > 0 ? Hits[0] : null;

        public Sample Peak => Samples.Where(s => s.T >= 0f).MaxBy(s => s.GloveToward);

        public Sample At(float t) => Samples.MinBy(s => MathF.Abs(s.T - t));
    }

    private static float Yaw(Quaternion q)
    {
        var f = Vector3.Transform(Vector3.UnitZ, q);
        return MathF.Atan2(f.X, f.Z);
    }

    private static float Wrap(float a) => MathF.IEEERemainder(a, 2f * MathF.PI);

    private static float SpinKe(PhysicsWorld world, int body)
    {
        var s = world.GetState(body);
        var local = Vector3.Transform(s.AngularVelocity, Quaternion.Conjugate(s.Orientation));
        return 0.5f * Vector3.Dot(local, world.GetLocalInertiaDiagonal(body) * local);
    }

    private static float LinKe(PhysicsWorld world, int body)
    {
        var s = world.GetState(body);
        return 0.5f * world.GetMass(body) * s.LinearVelocity.LengthSquared();
    }

    private static Run Measure(string name, FighterTuning tuning, float telegraph, float range, float until = 0.30f, int seed = 1, bool masses = false)
    {
        using var bout = new PunchLab.Bout(new(PunchLabPath.Standard, "still", "reach", range, false), seed, telegraph, tuning);
        while (bout.PunchTime is null)
        {
            bout.Step();
        }

        var f = bout.Game.Player;
        var w = f.World;
        var p = f.Parts;
        var trunk = new List<int> { p.Pelvis, p.Chest };
        if (p.Abdomen is { } abdomen)
        {
            trunk.Add(abdomen);
        }

        var arm = new HashSet<int> { f.UpperArmId(Hand.Right), f.ForearmId(Hand.Right), f.GloveBody(Hand.Right) };
        var chestYaw0 = Yaw(w.GetState(p.Chest).Orientation);
        var pelvisYaw0 = Yaw(w.GetState(p.Pelvis).Orientation);
        var pelvisY0 = f.PelvisPosition.Y;
        var pelvis0 = f.PelvisPosition;
        var distance = bout.Distance;
        var samples = new List<Sample>();
        var hits = new List<Hit>();
        while (bout.PunchTime is { } t && t < until)
        {
            var toward = Vector3.Normalize(bout.Game.Cpu.HeadPosition - f.GloveCenter(Hand.Right));
            var fwd = Vector3.Normalize((bout.Game.Cpu.ChestPosition - f.ChestPosition) with { Y = 0f });
            var v = f.GloveVelocity(Hand.Right);
            var chest = w.GetState(p.Chest);
            var trunkMass = trunk.Sum(w.GetMass);
            var trunkVel = trunk.Aggregate(Vector3.Zero, (a, b) => a + (w.GetState(b).LinearVelocity * w.GetMass(b))) / trunkMass;
            samples.Add(new Sample(
                t,
                v.Length(),
                Vector3.Dot(v, toward),
                arm.Sum(b => LinKe(w, b) + SpinKe(w, b)),
                trunk.Sum(b => SpinKe(w, b)),
                0.5f * trunkMass * MathF.Pow(MathF.Max(0f, Vector3.Dot(trunkVel, fwd)), 2f),
                Wrap(Yaw(chest.Orientation) - chestYaw0) * 180f / MathF.PI,
                Wrap(Yaw(w.GetState(p.Pelvis).Orientation) - pelvisYaw0) * 180f / MathF.PI,
                pelvisY0 - f.PelvisPosition.Y,
                f.BodySpringEnergy(),
                Vector3.Dot(f.PelvisPosition - pelvis0, fwd),
                masses ? f.ArticulatedMass(f.GloveBody(Hand.Right), f.GloveCenter(Hand.Right), toward, 4f) : 0f,
                masses ? f.ArticulatedMass(f.GloveBody(Hand.Right), f.GloveCenter(Hand.Right), toward, 6f) : 0f,
                masses ? f.ArticulatedMass(f.GloveBody(Hand.Right), f.GloveCenter(Hand.Right), toward, 8f) : 0f));
            bout.Step();
            foreach (var h in bout.Game.RecentHits.Where(h => h.Attacker == f && h.Hand == Hand.Right && !h.Kick && !h.Elbow))
            {
                hits.Add(new Hit(bout.PunchTime ?? t, h.Speed, h.ImpactMass, h.Energy, h.Zone));
            }
        }

        return new Run(name, telegraph, range, samples, hits, distance);
    }

    private static void Timeline(Run r)
    {
        Console.WriteLine(F($"--- timeline {r.Body} telegraph {r.Telegraph:0.00} range {r.Range:0.00} (distance at start {r.Distance:0.000} arm) ---"));
        Console.WriteLine("   t[s]  |v|  v->head  armKE  trunkSpinKE trunkFwdKE  chestYaw pelvisYaw  drop[cm] fwd[cm] springStore");
        for (var i = 0; i < r.Samples.Count; i += 3)
        {
            var s = r.Samples[i];
            var mark = r.Hits.Any(h => MathF.Abs(h.T - s.T) < 1.5f * 3 * Dt) ? "  <hit" : string.Empty;
            Console.WriteLine(F($"{s.T,7:0.000} {s.GloveSpeed,5:0.00} {s.GloveToward,6:0.00} {s.ArmKe,7:0.0} {s.TrunkSpinKe,9:0.0} {s.TrunkLinKe,10:0.0} {s.ChestYaw,9:0.0} {s.PelvisYaw,8:0.0} {s.PelvisDrop * 100f,8:0.0} {s.PelvisForward * 100f,7:0.0} {s.SpringStore,8:0.0}{mark}"));
        }

        foreach (var h in r.Hits)
        {
            Console.WriteLine(F($"   hit t {h.T:0.000} speed {h.Speed:0.00} m/s mass {h.Mass:0.00} kg energy {h.Energy:0.0} J zone {h.Zone}"));
        }
    }

    private static string Summary(Run r)
    {
        var peak = r.Peak;
        var first = r.First;
        var before = first is { } h ? r.Samples.Where(s => s.T < h.T - (0.5f * Dt)).LastOrDefault() : default;
        var windupStart = r.Samples[0];
        var release = r.At(0f);
        var maxSpin = r.Samples.Where(s => s.T >= 0f && (first is null || s.T <= first.Value.T)).Max(s => s.TrunkSpinKe);
        return F($"{r.Body,-10} tel {r.Telegraph:0.00} range {r.Range:0.00}: ")
            + F($"store@release {release.SpringStore:0.0} J (start {windupStart.SpringStore:0.0}), chestYaw@release {release.ChestYaw:0.0}°, pelvisYaw@release {release.PelvisYaw:0.0}°, drop {release.PelvisDrop * 100f:0.0} cm | ")
            + F($"peak v->head {peak.GloveToward:0.00} m/s @ {peak.T:0.000} s (armKE {peak.ArmKe:0.0}), maxTrunkSpinKE {maxSpin:0.0} | ")
            + (first is { } fh
                ? F($"first hit @ {fh.T:0.000} s ({(fh.T < peak.T ? "BEFORE" : "after")} peak) speed {fh.Speed:0.00} mass {fh.Mass:0.00} E {fh.Energy:0.0} J, armKE just before {before.ArmKe:0.0} J, v->head before {before.GloveToward:0.00}; hits {r.Hits.Count} sum {r.Hits.Sum(x => x.Energy):0.0} max {r.Hits.Max(x => x.Energy):0.0} J")
                : "no hit");
    }

    /// <summary>
    /// 間合い 0.95〜1.15 を 0.025 刻み（9 本）× 種 1・2 で、最初の当たりの中身を 1 行ずつ: 当たる直前の腕の運動エネルギー・拳の中心の速さ・
    /// 近づく速さ（当たった点）・乗った質量（関節で連なった有効質量）・エネルギー、同じ姿勢で近づく速さだけ 4・6・8 m/s に替えた有効質量。
    /// </summary>
    [TestMethod]
    public void CrossWindupScatter()
    {
        var free = new Dictionary<float, Run>();
        foreach (var tel in new[] { Quick, Loaded })
        {
            free[tel] = Measure("free", FighterTuning.Default, tel, 1.40f);
        }

        Console.WriteLine("tel seed range | tHit armKE vCenter vClose mass E zone | freeArmKE@tHit freePeakArmKE | m@4 m@6 m@8 (pose just before hit)");
        foreach (var seed in new[] { 1, 2 })
        {
            foreach (var tel in new[] { Quick, Loaded })
            {
                for (var range = 0.95f; range < 1.1501f; range += 0.025f)
                {
                    var r = Measure("standard", FighterTuning.Default, tel, range, 0.25f, seed, masses: true);
                    if (r.First is not { } h)
                    {
                        Console.WriteLine(F($"{tel:0.00} {seed} {range:0.000} | no hit"));
                        continue;
                    }

                    var before = r.Samples.Where(s => s.T < h.T - (0.5f * Dt)).Last();
                    var fr = free[tel];
                    Console.WriteLine(F($"{tel:0.00} {seed} {range:0.000} | {h.T:0.000} {before.ArmKe:0.0} {before.GloveToward:0.00} {h.Speed:0.00} {h.Mass:0.00} {h.Energy:0.0} {h.Zone} | {fr.At(h.T).ArmKe:0.0} {fr.Samples.Max(s => s.ArmKe):0.0} | {before.M4:0.00} {before.M6:0.00} {before.M8:0.00}"));
                }
            }
        }
    }

    [TestMethod]
    public void CrossWindup()
    {
        // 空振り（届かない間合い）: 拳の速さの山とその時刻、体のバネ・胴の回りの時系列。
        foreach (var tel in new[] { Quick, Loaded })
        {
            Timeline(Measure("standard", FighterTuning.Default, tel, 1.40f));
        }

        // 当たる間合い 1.05 の時系列（PunchWeightTests が前に見ていた 1 本）と 1.15。
        foreach (var range in new[] { 1.05f, 1.15f })
        {
            foreach (var tel in new[] { Quick, Loaded })
            {
                Timeline(Measure("standard", FighterTuning.Default, tel, range));
            }
        }

        // 5 つの間合い × 体 × 溜めの要約。
        foreach (var (name, tuning) in Bodies)
        {
            foreach (var tel in new[] { Quick, Loaded })
            {
                var runs = Ranges.Select(range => Measure(name, tuning, tel, range)).ToList();
                foreach (var r in runs)
                {
                    Console.WriteLine(Summary(r));
                }

                var firsts = runs.Sum(r => r.First?.Energy ?? 0f);
                var maxes = runs.Sum(r => r.Hits.Count > 0 ? r.Hits.Max(h => h.Energy) : 0f);
                var sums = runs.Sum(r => r.Hits.Sum(h => h.Energy));
                var armBefore = runs.Sum(r => r.First is { } h ? r.Samples.Where(s => s.T < h.T - (0.5f * Dt)).LastOrDefault().ArmKe : 0f);
                var peakKe = runs.Sum(r => r.Peak.ArmKe);
                var beforePeak = runs.Count(r => r.First is { } h && h.T < r.Peak.T);
                Console.WriteLine(F($"== {name} tel {tel:0.00}: first-hit sum {firsts:0} J, max-hit sum {maxes:0} J, all-hits sum {sums:0} J, armKE-before-first sum {armBefore:0} J, armKE-at-own-peak sum {peakKe:0} J, hits before peak {beforePeak}/{runs.Count}"));
            }
        }
    }
}
