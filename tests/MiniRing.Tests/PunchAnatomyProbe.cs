using System.Globalization;
using System.Numerics;

namespace MiniRing.Tests;

/// <summary>
/// 1 発の拳を分解する（2026-11-18、PhysicsVersion 45 の作り直しの調べ）: 構えて立つ相手へ寄って止まり、1 発だけ打つ。
/// 240 Hz で、拳の（胸に対する・地面に対する）当たる向きの速さ、胸の鉛直軸まわりの回転、重心の前進、体を剛体と見た拳の所の速さ（体の点）を記録し、
/// 最大の時刻と当たった時刻の値を並べる。溜めた拳がなぜ当たる瞬間に速くならないかを見る。
/// </summary>
[TestClass]
[TestCategory("Probe")]
public sealed class PunchAnatomyProbe
{
    private const float Dt = 1f / 240f;

    [TestMethod]
    public void OnePunch()
    {
        foreach (var distance in new[] { 1.0f, 1.25f })
        {
            foreach (var (name, heavy, hold) in new[] { ("tap jab", false, 0f), ("tap cross", true, 0f), ("charged cross", true, 0.4196f) })
            {
                foreach (var seed in new[] { 1, 2 })
                {
                    Run(name, heavy, hold, distance, seed);
                }
            }
        }
    }

    private static void Run(string name, bool heavy, float hold, float distance, int seed)
    {
        using var game = new BoxingGame(FighterTuning.Default, seed: seed,
            corners: new BoxingCorners("still", _ => new StandGuard(), _ => new IntentControl()));
        var control = (IntentControl)game.PlayerControl!;
        var p = game.Player;
        var lead = Bots.BotCatalog.Boxer.Lead;
        var hand = heavy ? (lead == Hand.Left ? Hand.Right : Hand.Left) : lead;
        var clock = 0f;
        // 寄る
        while (clock < 10.07f && Flat(game.Cpu.ChestPosition - p.ChestPosition).Length() > distance * p.ArmReach)
        {
            control.Input = new FighterIntent(new Vector2(0f, 1f));
            game.Step(new BoxingInput(default), Dt);
            clock += Dt;
        }

        for (var i = 0; i < 201; i++)
        {
            control.Input = new FighterIntent(Vector2.Zero);
            game.Step(new BoxingInput(default), Dt);
        }

        var start = Flat(game.Cpu.ChestPosition - p.ChestPosition).Length() / p.ArmReach;
        var lastCom = p.CenterOfMass;
        var lastShoulder = p.ShoulderPosition(hand);
        float peakShoulder = 0f, peakShoulderT = 0f;
        var t = 0f;
        float peakArm = 0f, peakArmT = 0f, peakAbs = 0f, peakSpin = 0f, peakSpinT = 0f, peakBody = 0f, peakBodyT = 0f, peakCom = 0f;
        string? hitLine = null;
        var released = -1f;
        while (t < hold + 1.007f)
        {
            var down = t < MathF.Max(hold, Dt);
            control.Input = new FighterIntent(Vector2.Zero, Light: down && !heavy, Heavy: down && heavy);
            if (!down && released < 0f)
            {
                released = t;
            }

            game.Step(new BoxingInput(default), Dt);
            t += Dt;
            var to = Flat(game.Cpu.HeadPosition - p.ChestPosition);
            var dir = to.LengthSquared() > 1e-8f ? Vector3.Normalize(to) : Vector3.UnitZ;
            var glove = p.GloveVelocity(hand);
            var com = p.CenterOfMass;
            var comV = (com - lastCom) / Dt;
            lastCom = com;
            var bodyPoint = comV + Vector3.Cross(p.ChestAngularVelocity, p.GloveCenter(hand) - com);
            var arm = Vector3.Dot(glove - p.ChestVelocity, dir);
            var abs = Vector3.Dot(glove, dir);
            var spin = MathF.Abs(p.ChestAngularVelocity.Y);
            var body = Vector3.Dot(bodyPoint, dir);
            var shoulderNow = p.ShoulderPosition(hand);
            var shoulder = Vector3.Dot((shoulderNow - lastShoulder) / Dt, dir);
            lastShoulder = shoulderNow;
            if (shoulder > peakShoulder) { peakShoulder = shoulder; peakShoulderT = t; }
            if (arm > peakArm) { peakArm = arm; peakArmT = t; }
            peakAbs = MathF.Max(peakAbs, abs);
            if (spin > peakSpin) { peakSpin = spin; peakSpinT = t; }
            if (body > peakBody) { peakBody = body; peakBodyT = t; }
            peakCom = MathF.Max(peakCom, Vector3.Dot(comV, dir));
            foreach (var hit in game.RecentHits.Where(h => h.Attacker == p && hitLine is null))
            {
                hitLine = string.Create(CultureInfo.InvariantCulture,
                    $"HIT t {t:F3} {hit.Zone} speed {hit.Speed:F2} mass {hit.ImpactMass:F2} share {hit.BodyShare:P0} dmg {hit.Damage:F1} | arm {arm:F2} abs {abs:F2} spin {spin:F1} body {body:F2} shoulder {shoulder:F2}");
            }
        }

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{name,-14} d {distance:F2} (start {start:F2}) seed {seed}: released {released:F2}; peak arm {peakArm:F2}@{peakArmT:F3} abs {peakAbs:F2} spin {peakSpin:F1}@{peakSpinT:F3} body {peakBody:F2}@{peakBodyT:F3} com {peakCom:F2} shoulder {peakShoulder:F2}@{peakShoulderT:F3}; {hitLine ?? "no hit"}"));
    }

    private static Vector3 Flat(Vector3 v) => v with { Y = 0f };
}
