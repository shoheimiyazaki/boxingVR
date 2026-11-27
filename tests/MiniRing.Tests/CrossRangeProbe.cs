using System.Globalization;
using System.Numerics;
using MiniRing.Arena;
using MiniRing.Bots;

namespace MiniRing.Tests;

/// <summary>
/// 調査（2026-11-27、基準の体の移し替えの右ストレートの退行）: 右ストレート 1 本（守らない相手）を、間合い 5 本 × 種 3 で、強い腰（既定）と
/// 弱い腰（骨盤の回旋 0.4 倍）で打ち、当たりの瞬間の拳の速さ・乗る質量・エネルギー・腕の伸び（肩から拳 / 腕の長さ）・胸と骨盤のひねりの速さを並べる。
/// master と同じ式で比べられるよう、使うのは両方の版にある API だけ。
/// </summary>
[TestClass]
[TestCategory("Probe")]
public sealed class CrossRangeProbe
{
    private static readonly float[] Ranges = [0.95f, 1.0f, 1.05f, 1.1f, 1.15f];
    private static readonly int[] Seeds = [1, 2, 3];

    [TestMethod]
    public void Measure()
    {
        var strong = FighterTuning.Default;
        var weak = strong with { PelvisRotatorTorque = strong.PelvisRotatorTorque * 0.4f };
        foreach (var (name, tuning) in new[] { ("strong", strong), ("weak", weak) })
        {
            var rows = new List<Trial>();
            foreach (var range in Ranges)
            {
                foreach (var seed in Seeds)
                {
                    var trial = Cross(tuning, seed, range);
                    rows.Add(trial);
                    Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                        $"CROSS {name} r{range:0.00} s{seed}: dist {trial.Distance:0.000} hit {trial.Hit} v {trial.Speed:0.00} m {trial.Mass:0.00} E {trial.Energy:0.0} peak {trial.Peak:0.00} ext {trial.Extension:0.00} chestYaw {trial.ChestYaw:0.00} (max {trial.ChestYawMax:0.00}) pelvisYaw {trial.PelvisYaw:0.00} (max {trial.PelvisYawMax:0.00}) t {trial.Time:0.000} shoulderGap {trial.ShoulderGap:0.000}"));
                }
            }

            var hits = rows.Where(r => r.Hit).ToList();
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"CROSSSUM {name}: hits {hits.Count}/{rows.Count} E {Mean(hits, r => r.Energy):0.0} (sd {Sd(hits, r => r.Energy):0.0}) v {Mean(hits, r => r.Speed):0.00} m {Mean(hits, r => r.Mass):0.00} peak {Mean(rows, r => r.Peak):0.00} ext {Mean(hits, r => r.Extension):0.00} chestYaw {Mean(hits, r => r.ChestYaw):0.00} max {Mean(rows, r => r.ChestYawMax):0.00} pelvisMax {Mean(rows, r => r.PelvisYawMax):0.00}"));
        }
    }

    /// <summary>
    /// 当たりの時刻と拳の速さの山の時刻のずれ（BASE_BODY_PLAN §9）: 拳の速さ・目標の速さ（指令の目標の世界の位置の差）・拳と相手の頭の距離を刻みごとに見て、
    /// 速さの山の時刻・当たりの時刻・目標が打ち抜く先へ着く時刻（技の時間 × ArrivalShare）・目標の速さの最大を並べる。
    /// </summary>
    [TestMethod]
    public void Timing()
    {
        foreach (var range in new[] { 0.95f, 1.05f, 1.15f })
        {
            using var bout = new PunchLab.Bout(new(PunchLabPath.Standard, "still", "reach", range, false), 1, 0.06f, FighterTuning.Default);
            while (bout.PunchTime is null)
            {
                bout.Step();
            }

            var f = bout.Game.Player;
            var peak = 0f;
            var peakTime = 0f;
            var targetMax = 0f;
            Vector3? lastTarget = null;
            var hitTime = float.NaN;
            var hitSpeed = 0f;
            var line = new System.Text.StringBuilder();
            while (bout.PunchTime is { } t && t < 0.30f)
            {
                bout.Step();
                if (bout.TargetWorld is { } target)
                {
                    if (lastTarget is { } previous && t > 0f)
                    {
                        targetMax = MathF.Max(targetMax, Vector3.Distance(target, previous) / (1f / 240f));
                    }

                    lastTarget = target;
                }

                var speed = f.GloveVelocity(Hand.Right).Length();
                if (t >= 0f && speed > peak)
                {
                    peak = speed;
                    peakTime = t;
                }

                if (t >= 0f && MathF.Round(t * 240f) % 3 == 0)
                {
                    line.Append(string.Create(CultureInfo.InvariantCulture, $" {t:0.000}:{speed:0.0}/{Vector3.Distance(f.GloveCenter(Hand.Right), bout.Game.Cpu.HeadPosition):0.00}/{(bout.TargetWorld is { } tw ? Vector3.Distance(tw, f.ShoulderPosition(Hand.Right)) / f.ArmReach : 0f):0.00}/{Vector3.Distance(f.GloveCenter(Hand.Right), f.ShoulderPosition(Hand.Right)) / f.ArmReach:0.00}"));
                }

                if (float.IsNaN(hitTime) && bout.Game.RecentHits.Any(h => h.Attacker == f && h.Hand == Hand.Right && !h.Kick && !h.Elbow))
                {
                    hitTime = t;
                    hitSpeed = bout.Game.RecentHits.First(h => h.Attacker == f && h.Hand == Hand.Right).Speed;
                }
            }

            var spec = PunchBook.Default.Of(Technique.Cross);
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"TIMING r{range:0.00} reach {f.ArmReach:0.000}: peak {peak:0.00} at {peakTime:0.000}, hit {hitSpeed:0.00} at {hitTime:0.000}, target arrives {spec.Duration * PunchSpec.ArrivalShare:0.000}, target max {targetMax:0.0} m/s (cap {FighterTuning.Default.MaxTargetSpeed:0.0})"));
            Console.WriteLine("  t:speed/glove-head" + line);
        }
    }

    /// <summary>
    /// あごの間合い（腕を肩からあごへ伸ばしきったときにグローブの先がちょうどあごに触れる所、PunchLab の chin）で、溜めを 3 通りに変えて打つ。
    /// 胸と胸の間を腕の長さの単位で揃える比べ（<see cref="Measure"/>）は、頭・胸の深さが体ごとに違うと「届く」の意味がずれるので、届く所で揃えて比べ直す。
    /// </summary>
    [TestMethod]
    public void AtTheChin()
    {
        var strong = FighterTuning.Default;
        var weak = strong with { PelvisRotatorTorque = strong.PelvisRotatorTorque * 0.4f };
        foreach (var (name, tuning) in new[] { ("strong", strong), ("weak", weak) })
        {
            var rows = new List<Trial>();
            foreach (var telegraph in new[] { 0.03f, 0.06f, 0.09f, 0.12f, 0.15f })
            {
                using var bout = new PunchLab.Bout(new(PunchLabPath.Standard, "still", "chin", 0f, false), 1, telegraph, tuning);
                var trial = Run(bout);
                rows.Add(trial);
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"CHIN {name} tel{telegraph:0.00}: dist {trial.Distance:0.000} hit {trial.Hit} v {trial.Speed:0.00} m {trial.Mass:0.00} E {trial.Energy:0.0} peak {trial.Peak:0.00} ext {trial.Extension:0.00} chestYaw {trial.ChestYaw:0.00} pelvisMax {trial.PelvisYawMax:0.00} t {trial.Time:0.000}"));
            }

            var hits = rows.Where(r => r.Hit).ToList();
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"CHINSUM {name}: hits {hits.Count}/{rows.Count} E {Mean(hits, r => r.Energy):0.0} (sd {Sd(hits, r => r.Energy):0.0}) v {Mean(hits, r => r.Speed):0.00} m {Mean(hits, r => r.Mass):0.00} peak {Mean(rows, r => r.Peak):0.00} dist {Mean(rows, r => r.Distance):0.000}"));
        }
    }

    /// <summary>
    /// 「腰が効く」の物理の確かめ（BASE_BODY_PLAN §9.3）: あごの間合いで、骨盤の回旋筋のトルクの上限を 0.4〜1.6 倍に掃き、溜め 5 通りの平均を並べる。
    /// 見る量: 骨盤の鉛直軸まわりの角速度の最大・当たりの瞬間の胴（骨盤 + 胸）の鉛直軸まわりの角運動量・当たりの拳の速さ・計算した有効質量・
    /// 相手へ渡った運動量（当たりから 50 ms の間の相手の全部位の運動量の変化の、打つ向きの成分）。
    /// </summary>
    [TestMethod]
    public void HipSweep()
    {
        foreach (var k in new[] { 0.4f, 0.7f, 1.0f, 1.3f, 1.6f })
        {
            var tuning = FighterTuning.Default with { PelvisRotatorTorque = FighterTuning.Default.PelvisRotatorTorque * k };
            var rows = new List<(float Pelvis, float Trunk, float Speed, float Mass, float Energy, float Given)>();
            foreach (var telegraph in new[] { 0.03f, 0.06f, 0.09f, 0.12f, 0.15f })
            {
                using var bout = new PunchLab.Bout(new(PunchLabPath.Standard, "still", "chin", 0f, false), 1, telegraph, tuning);
                while (bout.PunchTime is null)
                {
                    bout.Step();
                }

                var f = bout.Game.Player;
                var v = bout.Game.Cpu;
                var world = bout.Game.World;
                var pelvisMax = 0f;
                HitEvent? hit = null;
                var trunk = 0f;
                var direction = Vector3.Zero;
                var before = Vector3.Zero;
                var after = Vector3.Zero;
                var since = 0;
                Vector3 Momentum() => v.AllParts.Aggregate(Vector3.Zero, (sum, id) => sum + (world.GetMass(id) * world.GetState(id).LinearVelocity));
                var previous = Momentum();
                while (bout.PunchTime is { } t && t < 0.5f && since < 12)
                {
                    bout.Step();
                    pelvisMax = MathF.Max(pelvisMax, MathF.Abs(world.GetState(f.Parts.Pelvis).AngularVelocity.Y));
                    if (hit is null)
                    {
                        foreach (var h in bout.Game.RecentHits.Where(h => h.Attacker == f && h.Hand == Hand.Right && !h.Kick && !h.Elbow))
                        {
                            hit = h;
                            // 胴の角運動量（鉛直成分）: 骨盤と胸の慣性の主軸を鉛直とみなした近似ではなく、部位ごとに ω·I を世界で数える。
                            trunk = new[] { f.Parts.Pelvis, f.Parts.Chest }.Sum(id => YawMomentum(world, id));
                            direction = Vector3.Normalize((v.HeadPosition - f.GloveCenter(Hand.Right)) with { Y = 0f });
                            before = previous;
                            break;
                        }
                    }
                    else
                    {
                        since++;
                        after = Momentum();
                    }

                    previous = Momentum();
                }

                if (hit is { } got)
                {
                    rows.Add((pelvisMax, trunk, got.Speed, got.ImpactMass, got.Energy, Vector3.Dot(after - before, direction)));
                }
            }

            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"HIP x{k:0.0}: hits {rows.Count}/5 pelvisMax {rows.Average(r => r.Pelvis):0.00} trunkL {rows.Average(r => r.Trunk):0.00} v {rows.Average(r => r.Speed):0.00} m {rows.Average(r => r.Mass):0.00} E {rows.Average(r => r.Energy):0.0} given {rows.Average(r => r.Given):0.00} (min {rows.Min(r => r.Given):0.00} max {rows.Max(r => r.Given):0.00})"));
        }
    }

    private static float YawMomentum(Aigis.Physics.PhysicsWorld world, int id)
    {
        var state = world.GetState(id);
        var inertia = world.GetLocalInertiaDiagonal(id);
        var local = Vector3.Transform(state.AngularVelocity, Quaternion.Conjugate(state.Orientation));
        var l = Vector3.Transform(local * inertia, state.Orientation);
        return l.Y;
    }

    private static float Mean(List<Trial> rows, Func<Trial, float> f) => rows.Count == 0 ? float.NaN : rows.Average(f);

    private static float Sd(List<Trial> rows, Func<Trial, float> f)
    {
        if (rows.Count < 2)
        {
            return float.NaN;
        }

        var m = rows.Average(f);
        return MathF.Sqrt(rows.Sum(r => (f(r) - m) * (f(r) - m)) / (rows.Count - 1));
    }

    internal readonly record struct Trial(float Distance, bool Hit, float Speed, float Mass, float Energy, float Peak, float Extension, float ChestYaw, float ChestYawMax,
        float PelvisYaw, float PelvisYawMax, float Time, float ShoulderGap);

    internal static Trial Cross(FighterTuning tuning, int seed, float range, float telegraph = 0.06f)
    {
        using var bout = new PunchLab.Bout(new(PunchLabPath.Standard, "still", "reach", range, false), seed, telegraph, tuning);
        return Run(bout);
    }

    internal static Trial Run(PunchLab.Bout bout)
    {
        while (bout.PunchTime is null)
        {
            bout.Step();
        }

        var f = bout.Game.Player;
        var distance = bout.Distance;
        var gap = (Vector3.Distance(f.ShoulderPosition(Hand.Right), bout.Game.Cpu.HeadPosition) / f.ArmReach) - 1f;
        var peak = 0f;
        var chestMax = 0f;
        var pelvisMax = 0f;
        while (bout.PunchTime is { } t && t < 0.45f)
        {
            bout.Step();
            var chest = MathF.Abs(f.ChestAngularVelocity.Y);
            var pelvis = MathF.Abs(bout.Game.World.GetState(f.Parts.Pelvis).AngularVelocity.Y);
            if (t >= 0f)
            {
                chestMax = MathF.Max(chestMax, chest);
                pelvisMax = MathF.Max(pelvisMax, pelvis);
            }

            foreach (var hit in bout.Game.RecentHits.Where(h => h.Attacker == f && h.Hand == Hand.Right && !h.Kick && !h.Elbow))
            {
                var extension = Vector3.Distance(f.ShoulderPosition(Hand.Right), f.GloveCenter(Hand.Right)) / f.ArmReach;
                return new(distance, true, hit.Speed, hit.ImpactMass, hit.Energy, peak, extension, chest, chestMax, pelvis, pelvisMax, t, gap);
            }

            peak = MathF.Max(peak, f.GloveVelocity(Hand.Right).Length());
        }

        return new(distance, false, 0f, 0f, 0f, peak, 0f, 0f, chestMax, 0f, pelvisMax, 0f, gap);
    }
}
