using System.Globalization;
using System.Numerics;
using Aigis.Physics;

namespace MiniRing.Tests;

/// <summary>調べ物（BASE_BODY_PLAN §10.9）: 骨盤の傾きの保ちを担ぐ上体の慣性で効かせた直し（carried）が、2 歩周期の揺れ返しを起こさないか・床反力（CoP）の制限にどう掛かるか。</summary>
[TestClass]
[TestCategory("Probe")]
public sealed class CarriedTiltProbe
{
    private const float Dt = 1f / 240f;

    private static (PhysicsWorld World, Fighter Fighter) Alone(FighterTuning baseTuning, BodyBuild build)
    {
        var world = new PhysicsWorld();
        world.AddStaticBox(new Vector3(0f, -1.408f, 0f), new Vector3(14.08f, 1.408f, 14.08f));
        var f = Fighter.Spawn(world, baseTuning with { Build = build }, "f", Vector3.Zero, 0f);
        return (world, f);
    }

    private static FighterCommand Guard(Fighter f, Vector3 head = default, Vector3 step = default) =>
        new(Vector3.Zero, Vector3.UnitZ, new HandCommand(AiControl.Guard(Hand.Left, f.ArmReach), false),
            new HandCommand(AiControl.Guard(Hand.Right, f.ArmReach), false), HeadOffset: head, Step: step);

    private static float Pitch(PhysicsWorld w, int body)
    {
        var up = Vector3.Transform(Vector3.UnitY, w.GetState(body).Orientation);
        return MathF.Atan2(up.Z, up.Y) * 180f / MathF.PI;
    }

    private static float Tilt(PhysicsWorld w, int body)
    {
        var up = Vector3.Transform(Vector3.UnitY, w.GetState(body).Orientation);
        return MathF.Acos(Math.Clamp(up.Y, -1f, 1f)) * 180f / MathF.PI;
    }

    private sealed record Stats(float ChestMean, float ChestMax, float PelvisMax, float OscP2P, int Crossings, float TorqueMax, float TorqueMean, float ShortFrac, float ShortMean, float FellAt);

    private static string Tag => Environment.GetEnvironmentVariable("TAG") ?? "RUN";

    private static Stats Run(FighterTuning t, BodyBuild build, float seconds, Func<float, (Vector3 Head, Vector3 Step)> phase, List<string>? trace = null)
    {
        var (world, f) = Alone(t, build);
        using var _ = world;
        var n = (int)(seconds * 240);
        var pelvis = new float[n];
        var (cs, cmax, pmax, tmax, tsum, short_, shortSum, fell) = (0f, 0f, 0f, 0f, 0f, 0, 0f, -1f);
        for (var i = 0; i < n; i++)
        {
            var (head, step) = phase(i * Dt);
            f.ApplyControl(Guard(f, head, step), Dt);
            world.Step(Dt);
            var chest = Tilt(world, f.Parts.Chest);
            pelvis[i] = Pitch(world, f.Parts.Pelvis);
            cs += chest;
            cmax = MathF.Max(cmax, chest);
            pmax = MathF.Max(pmax, Tilt(world, f.Parts.Pelvis));
            var tq = new Vector2(f.SupportTorqueWanted.X, f.SupportTorqueWanted.Z).Length();
            tmax = MathF.Max(tmax, tq);
            tsum += tq;
            if (f.CopShortfall > 0f)
            {
                short_++;
                shortSum += f.CopShortfall;
            }

            if (fell < 0f && f.State != FighterState.Standing)
            {
                fell = i * Dt;
            }

            if (trace is not null && i % 12 == 0 && i * Dt >= 1f)
            {
                trace.Add(string.Create(CultureInfo.InvariantCulture, $"{i * Dt:0.0}:{Pitch(world, f.Parts.Chest):0.0}/{pelvis[i]:0.0}"));
            }
        }

        // 骨盤の前後の傾きの揺れ: 0.5 秒の移動平均からのずれ（後ろ 2/3 の区間）。符号の入れ替わり数と山谷の幅。
        const int W = 120;
        var dev = new List<float>();
        for (var i = n / 3; i < n - W; i++)
        {
            var avg = 0f;
            for (var k = 0; k < W; k++)
            {
                avg += pelvis[i + k];
            }

            dev.Add(pelvis[i + (W / 2)] - (avg / W));
        }

        var cross = 0;
        for (var i = 1; i < dev.Count; i++)
        {
            if (MathF.Sign(dev[i]) != MathF.Sign(dev[i - 1]) && MathF.Abs(dev[i]) > 0.05f)
            {
                cross++;
            }
        }

        return new Stats(cs / n, cmax, pmax, dev.Max() - dev.Min(), cross, tmax, tsum / n, short_ / (float)n, short_ > 0 ? shortSum / short_ : 0f, fell);
    }

    private static string Fmt(Stats s) => string.Create(CultureInfo.InvariantCulture,
        $"chestMean {s.ChestMean:0.0} chestMax {s.ChestMax:0.0} pelvisMax {s.PelvisMax:0.0} osc(p2p deg) {s.OscP2P:0.00} cross {s.Crossings} torqueMax {s.TorqueMax:0} torqueMean {s.TorqueMean:0.0} copShortFrac {s.ShortFrac:0.000} copShortMean {s.ShortMean:0.0000} fell {s.FellAt:0.00}");

    /// <summary>15 体の格子（太さ 0.6〜1.2 × 身長 0.9〜1.25）を 20 秒立たせる。</summary>
    [TestMethod]
    public void StandGrid()
    {
        foreach (var (name, tuning) in new[] { ("Default", FighterTuning.Default), ("Hybrid", FighterTuning.HybridPunch), ("HybridCop", FighterTuning.HybridPunch with { SupportModel = SupportModel.SoftCop }) })
        {
            foreach (var h in new[] { 0.9f, 1.0f, 1.25f })
            {
                foreach (var m in new[] { 0.6f, 0.75f, 0.9f, 1.0f, 1.2f })
                {
                    var trace = h == 1.0f && (m == 0.75f || m == 1.0f) ? new List<string>() : null;
                    var s = Run(tuning, new BodyBuild(h, 1f, 1f, m), 20f, _ => (Vector3.Zero, Vector3.Zero), trace);
                    Console.WriteLine($"GRID {Tag} {name} H{h:0.00} M{m:0.00} {Fmt(s)}");
                    if (trace is not null && name == "Default")
                    {
                        Console.WriteLine($"TRACE {Tag} H{h:0.00} M{m:0.00} chest/pelvis pitch: {string.Join(' ', trace.Take(60))}");
                    }
                }
            }
        }
    }

    /// <summary>ダッキング（頭を腕の -0.7 倍まで下げて 1.3 秒保つ）と前への 1 歩（片脚が宙）。</summary>
    [TestMethod]
    public void DuckAndStep()
    {
        foreach (var (name, tuning) in new[] { ("Default", FighterTuning.Default), ("Hybrid", FighterTuning.HybridPunch), ("HybridCop", FighterTuning.HybridPunch with { SupportModel = SupportModel.SoftCop }) })
        {
            foreach (var m in new[] { 0.75f, 1.0f })
            {
                var duck = Run(tuning, new BodyBuild(1f, 1f, 1f, m), 5f, t =>
                {
                    var d = t < 1f ? 0f : t < 1.3f ? (t - 1f) / 0.3f : t < 2.6f ? 1f : t < 2.9f ? 1f - ((t - 2.6f) / 0.3f) : 0f;
                    return (new Vector3(0f, -0.7f * d * 0.7f, 0.09f * d * 0.7f), Vector3.Zero);
                });
                Console.WriteLine($"DUCK {Tag} {name} M{m:0.00} {Fmt(duck)}");
                var step = Run(tuning, new BodyBuild(1f, 1f, 1f, m), 5f, t => (Vector3.Zero, t is >= 1f and < 1.1f ? Vector3.UnitZ : Vector3.Zero));
                Console.WriteLine($"STEP {Tag} {name} M{m:0.00} {Fmt(step)}");
            }
        }
    }
}
