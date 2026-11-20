#pragma warning disable CA1305
using System.Globalization;
using System.Numerics;
using System.Text;
using Aigis.Physics;
using MiniRing.Bots;

namespace MiniRing.Tests;

/// <summary>
/// 調査用（名前に Probe）: スタミナのテンポの測定（docs/arena/gen13_stamina_tempo.md）。
/// 環境変数 STAMINA_TUNE="VesselPowerPerKg=15,HeartPower=12" で調整値を上書きして試せる。
/// </summary>
[TestClass]
[TestCategory("Probe")]
public sealed class StaminaTempoProbe
{
    private const float Dt = 1f / 240f;

    private static FighterTuning Tuning()
    {
        var tuning = FighterTuning.Default with { };
        var spec = Environment.GetEnvironmentVariable("STAMINA_TUNE");
        if (string.IsNullOrWhiteSpace(spec))
        {
            return tuning;
        }

        foreach (var pair in spec.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = pair.Split('=');
            typeof(FighterTuning).GetProperty(kv[0].Trim())!.SetValue(tuning, float.Parse(kv[1], CultureInfo.InvariantCulture));
        }

        return tuning;
    }

    private static string F(float v) => v.ToString("F3", CultureInfo.InvariantCulture);

    private sealed class Rig : IDisposable
    {
        public PhysicsWorld World { get; } = new();
        public Fighter Fighter { get; }
        public Vector3 Left { get; }
        public Vector3 Guard { get; }
        public float Time { get; private set; }

        public Rig(FighterTuning tuning)
        {
            World.AddStaticBox(new Vector3(0f, -1.408f, 0f), new Vector3(22.54f, 1.408f, 22.54f));
            Fighter = Fighter.Spawn(World, tuning, "tempo", Vector3.Zero, 0f);
            Left = AiControl.Guard(Hand.Left, Fighter.ArmReach);
            Guard = AiControl.Guard(Hand.Right, Fighter.ArmReach);
            for (var i = 0; i < 806; i++)
            {
                Step(Guard, 0f, Vector3.Zero);
            }

            Time = 0f;
        }

        public void Step(Vector3 right, float spring, Vector3 move)
        {
            Fighter.ApplyControl(new FighterCommand(move, Vector3.UnitZ, new HandCommand(Left, false), new HandCommand(right, false, spring)), Dt);
            World.Step(Dt);
            Time += Dt;
        }

        /// <summary>1 発（出す 0.34 s、戻す残り）。胸に対する拳の最大相対速度を返す。</summary>
        public float Punch(float cycleSeconds, Vector3 move, float reach = 0.75f, float ramp = 0.2014f, int extend = 81)
        {
            var steps = (int)(cycleSeconds / Dt);
            var peak = 0f;
            var aim = Guard + new Vector3(0f, 0.05f, reach) * Fighter.ArmReach;
            for (var phase = 0; phase < steps; phase++)
            {
                var extending = phase < extend;
                var target = extending ? Vector3.Lerp(Guard, aim, MathF.Min(1f, phase * Dt / ramp)) : Guard;
                Step(target, extending ? 1f : 0f, move);
                if (extending)
                {
                    peak = MathF.Max(peak, (Fighter.GloveVelocity(Hand.Right) - Fighter.ChestVelocity).Length());
                }
            }

            return peak;
        }

        public void Dispose() => World.Dispose();
    }

    [TestMethod]
    public void PunchCost_Probe()
    {
        var tuning = Tuning() with { VesselPowerPerKg = 0f };
        foreach (var (reach, ramp) in new[] { (0.75f, 0.2014f), (1.2f, 0.1343f), (1.6f, 0.1007f), (2.2f, 0.0839f), (3f, 0.0671f) })
        {
            using var rig = new Rig(tuning);
            var before = rig.Fighter.Stamina;
            var peak = rig.Punch(0.8392f, Vector3.Zero, reach, ramp);
            Console.WriteLine($"PC reach {reach} ramp {ramp}: peak {F(peak)} m/s, cost {F((before - rig.Fighter.Stamina) * tuning.OxygenStorePerKg * rig.Fighter.ArmMass(Hand.Right))} J");
        }
    }

    [TestMethod]
    public void Tempo_Probe()
    {
        var tuning = Tuning();
        var sb = new StringBuilder();

        // A: 空から構えて立つ
        using (var rig = new Rig(tuning))
        {
            rig.Fighter.Stamina = 0f;
            sb.Append("A rest(empty,guard):");
            for (var t = 1; t <= 10; t++)
            {
                for (var i = 0; i < 403; i++) { rig.Step(rig.Guard, 0f, Vector3.Zero); }
                sb.Append($" {t * 1.678f:0.0}s={F(rig.Fighter.Stamina)}");
            }

            sb.AppendLine($" P={F(rig.Fighter.RecentMusclePower)} W");
        }

        // B: 空から下がりながら（歩く）
        foreach (var speed in new[] { 0.5f, 1f })
        {
            using var rig = new Rig(tuning);
            rig.Fighter.Stamina = 0f;
            sb.Append($"B retreat move={speed}:");
            for (var t = 1; t <= 10; t++)
            {
                for (var i = 0; i < 403; i++) { rig.Step(rig.Guard, 0f, new Vector3(0f, 0f, -speed * 0.5f)); }
                sb.Append($" {t * 1.678f:0.0}s={F(rig.Fighter.Stamina)}");
            }

            sb.AppendLine($" P={F(rig.Fighter.RecentMusclePower)} W");
        }

        // C: 満タンの 1 発目の拳速と、空からの最初のまともな 1 発
        float reference;
        using (var rig = new Rig(tuning))
        {
            reference = rig.Punch(0.8392f, Vector3.Zero);
        }

        sb.AppendLine($"C reference peak (full, first punch) = {F(reference)} m/s");
        foreach (var wait in new[] { 0f, 0.8392f, 1.678f, 2.518f, 3.357f, 5.035f })
        {
            using var rig = new Rig(tuning);
            rig.Fighter.Stamina = 0f;
            for (var i = 0; i < (int)(wait / Dt); i++) { rig.Step(rig.Guard, 0f, Vector3.Zero); }
            var peak = rig.Punch(0.8392f, Vector3.Zero);
            sb.AppendLine($"C empty -> wait {wait:F1}s -> one punch: peak {F(peak)} ({F(peak / reference)} of full)");
        }

        // C2: 空から 0.84 秒ごとに打ち続けて、最初に 8 割へ届く時刻
        using (var rig = new Rig(tuning))
        {
            rig.Fighter.Stamina = 0f;
            var first = -1f;
            var peaks = new List<float>();
            for (var n = 0; n < 12; n++)
            {
                var start = rig.Time;
                var peak = rig.Punch(0.8392f, Vector3.Zero);
                peaks.Add(peak);
                if (first < 0f && peak >= 0.8f * reference) { first = start; }
            }

            sb.AppendLine($"C2 empty, try every 0.84s: first >=80% at t={(first < 0 ? "never" : F(first))}; peaks {string.Join(' ', peaks.Select(F))}; stamina {F(rig.Fighter.Stamina)}");
        }

        // D: 満タンから連打（0.84 秒周期と 0.5 秒周期）して空になるまで
        foreach (var cycle in new[] { 0.8392f, 0.5035f })
        {
            using var rig = new Rig(tuning);
            var emptyAt = -1f;
            var peaks = new List<float>();
            for (var n = 0; n < 60 && emptyAt < 0f; n++)
            {
                peaks.Add(rig.Punch(cycle, Vector3.Zero, 1.6f, 0.1007f, 49));
                if (rig.Fighter.Stamina < 0.1f) { emptyAt = rig.Time; }
            }

            sb.AppendLine($"D spam from full cycle {cycle}s: stamina<0.1 at {(emptyAt < 0 ? "never(60 punches)" : F(emptyAt))} s, {peaks.Count} punches; peaks {string.Join(' ', peaks.Take(14).Select(F))}");
        }

        // E: 空で連打（10 発目 / 1 発目）
        using (var rig = new Rig(tuning))
        {
            rig.Fighter.Stamina = 0f;
            var peaks = new List<float>();
            for (var n = 0; n < 20; n++) { peaks.Add(rig.Punch(0.5035f, Vector3.Zero, 1.6f, 0.1007f, 49)); }
            sb.AppendLine($"E spam from empty: 1st {F(peaks[0])}, 10th {F(peaks[9])} ratio {F(peaks[9] / peaks[0])}; stamina {F(rig.Fighter.Stamina)}");
        }

        Console.WriteLine(sb.ToString());
    }

    [TestMethod]
    public void Idle_Probe()
    {
        var tuning = Tuning();
        var styles = new (string, BotStyle)[]
        {
            ("still", BotCatalog.Boxer with { Attacks = false, DefenseRate = 0f, Prediction = 0f, Aggression = 0f }),
            ("noattack-moving", BotCatalog.Style("swarmer") with { Attacks = false }),
            ("noattack-moving-slugger", BotCatalog.Style("slugger") with { Attacks = false }),
        };
        foreach (var (name, style) in styles)
        {
            using var game = new BoxingGame(tuning, 3, new BoxingCorners("x", s => new BoxerBot(style, s), s => new BoxerBot(style, s)));
            var sum = 0f;
            var n = 0;
            for (var step = 0; step < 2014; step++)
            {
                game.Step(default, 1f / 60f);
                if (step > 302) { sum += game.Cpu.RecentMusclePower + game.Player.RecentMusclePower; n += 2; }
            }

            Console.WriteLine($"IDLE {name}: P {F(sum / n)} W, stamina {F(game.Cpu.Stamina)}");
        }
    }

    [TestMethod]
    public void Timeline_Probe()
    {
        var tuning = Tuning();
        var red = Environment.GetEnvironmentVariable("STAMINA_RED") ?? "slugger";
        var blue = Environment.GetEnvironmentVariable("STAMINA_BLUE") ?? "jabber";
        using var game = new BoxingGame(tuning, 11, new BoxingCorners(blue, s => BotCatalog.Create(blue, s), s => BotCatalog.Create(red, s)));
        var line = new StringBuilder();
        for (var step = 0; step < 6042; step++)
        {
            game.Step(default, 1f / 60f);
            if (step % 101 == 100)
            {
                line.AppendLine($"TL t={(step + 1) / 60f:0.0}s red st {F(game.Player.Stamina)} P {F(game.Player.RecentMusclePower)} R {F(game.Player.RecoveryPower)} | blue st {F(game.Cpu.Stamina)} P {F(game.Cpu.RecentMusclePower)} R {F(game.Cpu.RecoveryPower)}");
            }
        }

        Console.WriteLine(line.ToString());
    }

    [TestMethod]
    public void Bouts_Probe()
    {
        var tuning = Tuning();
        var pairs = new[] { ("swarmer", "champion"), ("slugger", "jabber"), ("boxer", "outboxer"), ("champion", "slugger"), ("swarmer", "slugger"), ("jabber", "outboxer") };
        var seconds = float.Parse(Environment.GetEnvironmentVariable("STAMINA_SECONDS") ?? "100.7", CultureInfo.InvariantCulture);
        var seedCount = int.Parse(Environment.GetEnvironmentVariable("STAMINA_SEEDS") ?? "2", CultureInfo.InvariantCulture);
        var rows = new string[pairs.Length * seedCount];
        Parallel.For(0, rows.Length, index =>
        {
            var (red, blue) = pairs[index / seedCount];
            var seed = 11 + (index % seedCount);
            var allOut = Environment.GetEnvironmentVariable("STAMINA_ALLOUT") == "1";
            var restOverride = Environment.GetEnvironmentVariable("STAMINA_REST");
            IFighterControl Make(string name, int s) => allOut ? new BoxerBot(BotCatalog.Style(name) with { RestBelow = 0.01f, PaceDrop = 0f, Aggression = 1f }, s)
                : restOverride is null ? BotCatalog.Create(name, s) : new BoxerBot(BotCatalog.Style(name) with { RestBelow = float.Parse(restOverride, CultureInfo.InvariantCulture) }, s);
            using var game = new BoxingGame(tuning, seed, new BoxingCorners(blue, s => Make(blue, s), s => Make(red, s)));
            var steps = (int)(seconds * 60f);
            var mean = 0f;
            var empty = 0;
            var waiting = 0;
            var episodes = new List<float>();
            var recoveries = new List<float>();
            var fighters = new[] { game.Player, game.Cpu };
            var phase = new int[2];
            var spendSum = 0f;
            var spendCount = 0; // 0: 満タン側を探す、1: 空へ向かう、2: 空から戻る
            var since = new float[2];
            var samples = 0;
            var recSum = 0f;
            var workSum = 0f;
            for (var step = 0; step < steps; step++)
            {
                game.Step(default, 1f / 60f);
                for (var k = 0; k < 2; k++)
                {
                    var s = fighters[k].Stamina;
                    mean += s;
                    recSum += fighters[k].RecoveryPower;
                    workSum += fighters[k].RecentMusclePower;
                    samples++;
                    if (s < 0.05f) { empty++; }
                    if (s < 0.25f) { waiting++; }
                    var time = step / 60f;
                    if (phase[k] == 1) { spendSum += fighters[k].RecentMusclePower; spendCount++; }
                    switch (phase[k])
                    {
                        case 0 when s >= 0.9f: phase[k] = 1; since[k] = time; break;
                        case 1 when s < 0.1f: episodes.Add(time - since[k]); phase[k] = 2; since[k] = time; break;
                        case 2 when s >= 0.5f: recoveries.Add(time - since[k]); phase[k] = 0; break;
                    }
                }
            }

            rows[index] = string.Create(CultureInfo.InvariantCulture,
                $"{red} vs {blue} seed {seed}: mean {mean / samples:F3}, R {recSum / samples:F1} W, P {workSum / samples:F1} W, P(full->empty) {(spendCount == 0 ? 0f : spendSum / spendCount):F1} W, empty(<5%) {100f * empty / samples:F1}%, waiting(<25%) {100f * waiting / samples:F1}%, full->empty {(episodes.Count == 0 ? "-" : episodes.Average().ToString("F1", CultureInfo.InvariantCulture))} s (n={episodes.Count}), empty->50% {(recoveries.Count == 0 ? "-" : recoveries.Average().ToString("F1", CultureInfo.InvariantCulture))} s (n={recoveries.Count}), hits {game.PlayerWins}/{game.CpuWins}");
        });
        Console.WriteLine(string.Join(Environment.NewLine, rows));
    }
}
