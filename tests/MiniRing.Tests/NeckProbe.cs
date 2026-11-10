using System.Numerics;
using MiniRing.Bots;

namespace MiniRing.Tests;

/// <summary>
/// 首の調査用: 打たれていないときの首の揺れ、打たれたときに頭が持っていかれる量（拳の重さ・速さ別）、スウェーで上体と首のどちらが傾くか。
/// </summary>
[TestClass]
public sealed class NeckProbe
{
    private const float Dt = 1f / 240f;

    [TestMethod]
    public void IdleWobbleAndHitSnap_InABout()
    {
        var corners = new BoxingCorners("champion", s => BotCatalog.Create("champion", s), s => BotCatalog.Create("swarmer", s));
        using var game = new BoxingGame(seed: 3, corners: corners);
        var idle = new List<float>();
        var lastHeadHit = new Dictionary<Fighter, float> { [game.Cpu] = -9f, [game.Player] = -9f };
        var watch = new List<(Fighter F, float Energy, float Until, float Peak)>();
        var snaps = new List<(float Energy, float Peak)>();
        for (var t = 0f; t < 60f; t += Dt)
        {
            game.Step(default, Dt);
            foreach (var hit in game.RecentHits.Where(h => h.Zone is HitZone.Head or HitZone.Temple or HitZone.Chin))
            {
                lastHeadHit[hit.Victim] = t;
                watch.Add((hit.Victim, 0.5f * hit.ImpactMass * hit.Speed * hit.Speed, t + 0.3f, 0f));
            }

            foreach (var f in new[] { game.Cpu, game.Player })
            {
                if (f.State == FighterState.Standing && t - lastHeadHit.GetValueOrDefault(f, -9f) > 1f && t > 2f)
                {
                    idle.Add(Tilt(game, f));
                }
            }

            for (var i = watch.Count - 1; i >= 0; i--)
            {
                var w = watch[i];
                if (t > w.Until)
                {
                    snaps.Add((w.Energy, w.Peak));
                    watch.RemoveAt(i);
                }
                else
                {
                    watch[i] = w with { Peak = MathF.Max(w.Peak, Tilt(game, w.F)) };
                }
            }
        }

        var q = Quaternion.Concatenate(game.World.GetState(game.Cpu.Parts.Head).Orientation, Quaternion.Inverse(game.World.GetState(game.Cpu.Parts.Chest).Orientation));
        var chestQ = game.World.GetState(game.Cpu.Parts.Chest).Orientation;
        var axisWorld = Vector3.Normalize(new Vector3(q.X, q.Y, q.Z));
        var axisChest = Vector3.Transform(axisWorld, Quaternion.Inverse(chestQ));
        Console.WriteLine($"end: relative axis in chest frame {axisChest} angle {Deg(2f * MathF.Acos(Math.Clamp(MathF.Abs(q.W), 0f, 1f))):0.0}°, cpu state {game.Cpu.State}");
        idle.Sort();
        Console.WriteLine($"idle head-to-chest tilt: median {Deg(idle[idle.Count / 2]):0.0}°, 90th {Deg(idle[(int)(idle.Count * 0.9f)]):0.0}°, 99th {Deg(idle[(int)(idle.Count * 0.99f)]):0.0}°");
        foreach (var (lo, hi) in new[] { (0f, 2f), (2f, 5f), (5f, 10f), (10f, 99f) })
        {
            var band = snaps.Where(s => s.Energy >= lo && s.Energy < hi).Select(s => s.Peak).OrderBy(p => p).ToList();
            if (band.Count > 0)
            {
                Console.WriteLine($"head hits {lo}-{hi} J: {band.Count}, snap peak median {Deg(band[band.Count / 2]):0.0}°, max {Deg(band[^1]):0.0}°");
            }
        }
    }

    [TestMethod]
    public void Sway_LeansTheUpperBody()
    {
        var still = BotCatalog.Boxer with { Attacks = false, DefenseRate = 0f, Prediction = 0f, Aggression = 0f };
        Swayer? swayer = null;
        var corners = new BoxingCorners("still", s => swayer = new Swayer(new BoxerBot(still, s)), s => new BoxerBot(still, s));
        using var game = new BoxingGame(seed: 1, corners: corners);
        var f = game.Cpu;
        float headBack0 = 0f, chest0 = 0f, pelvis0 = 0f;
        float maxBack = 0f, chestAt = 0f, pelvisAt = 0f, neckAt = 0f;
        for (var t = 0f; t < 4f; t += Dt)
        {
            swayer!.Time = t;
            if (MathF.Abs((t * 4f) - MathF.Round(t * 4f)) < Dt * 2f)
            {
                Console.WriteLine($"t {t:0.00} tilt {Deg(Tilt(game, f)):0.0}° neck pitch {Deg(Pitch(game, f, f.Parts.Head) - Pitch(game, f, f.Parts.Chest)):0.0}° chest {Deg(Pitch(game, f, f.Parts.Chest)):0.0}°");
            }

            game.Step(default, Dt);
            var back = -Vector3.Dot(f.HeadPosition - f.Frame.Origin, f.Frame.Forward);
            if (t < 1.99f)
            {
                headBack0 = back;
                chest0 = Pitch(game, f, f.Parts.Chest);
                pelvis0 = Pitch(game, f, f.Parts.Pelvis);
            }
            else if (back - headBack0 > maxBack)
            {
                maxBack = back - headBack0;
                chestAt = Pitch(game, f, f.Parts.Chest) - chest0;
                pelvisAt = Pitch(game, f, f.Parts.Pelvis) - pelvis0;
                neckAt = Pitch(game, f, f.Parts.Head) - Pitch(game, f, f.Parts.Chest);
            }
        }

        Console.WriteLine($"sway: head back {maxBack:0.000} m; chest leaned back {Deg(chestAt):0.0}°, pelvis {Deg(pelvisAt):0.0}°, head on the neck {Deg(neckAt):0.0}° (positive = back)");
    }

    /// <summary>2 秒目から 0.35 秒かけて頭を後ろへ（スウェーと同じ量）、0.4 秒保って戻す。</summary>
    private sealed class Swayer(IFighterControl inner) : IFighterControl
    {
        public float Time { get; set; }

        public FighterCommand Read(in FighterSense sense, float dt)
        {
            var c = inner.Read(sense, dt);
            var a = Math.Clamp((Time - 2f) / 0.35f, 0f, 1f) * (Time < 2.75f ? 1f : Math.Clamp(1f - ((Time - 2.75f) / 0.35f), 0f, 1f));
            return c with { HeadOffset = c.HeadOffset + new Vector3(0f, 0.02f, -0.38f) * (a * sense.ArmReach) };
        }
    }

    /// <summary>部位の後ろへの傾き [rad]（向きの座標系で、上向きの軸が後ろへ倒れた角度。正 = 後ろ）。</summary>
    private static float Pitch(BoxingGame game, Fighter f, int part)
    {
        var up = Vector3.Transform(Vector3.UnitY, game.World.GetState(part).Orientation);
        return MathF.Atan2(-Vector3.Dot(up, f.Frame.Forward), up.Y);
    }

    private static float Tilt(BoxingGame game, Fighter f)
    {
        var relative = Quaternion.Concatenate(game.World.GetState(f.Parts.Head).Orientation, Quaternion.Inverse(game.World.GetState(f.Parts.Chest).Orientation));
        return 2f * MathF.Acos(Math.Clamp(MathF.Abs(relative.W), 0f, 1f));
    }

    private static float Deg(float r) => r * 180f / MathF.PI;
}
