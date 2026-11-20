using System.Globalization;
using System.Numerics;

namespace MiniRing.Tests;

/// <summary>
/// 調査（2026-11-17、ユーザーの報告）: キーボードで前へ押しながら弱を高速で連打すると最適解になる。連打の速さ別に、
/// 当たった拳の速さ・乗った質量・体の割合・拳が戻る途中で当たった割合・相手に対する相対の速さ・腕が拳を当たる向きへ押していた力・
/// 肘の伸び（肩から拳の距離 / 腕の長さ）を測る。比べものとして、同じ試合の CPU（ボットの技の拳）の当たりも同じ物差しで出す。
/// 速度はどれも直前の歩の終わり（= この歩の始まり）の値（歩の後では接触で速度がそろってしまう）。
/// </summary>
[TestClass]
[TestCategory("Probe")]
public sealed class RushPhysicsProbe
{
    private const float Dt = 1f / 60f;

    [TestMethod]
    public void RushTapRates()
    {
        foreach (var forward in new[] { 1f, 0f })
        {
            foreach (var period in new[] { 0.1175f, 0.2014f, 1.007f })
            {
                Run(period, forward, seed: 3);
            }
        }
    }

    /// <summary>
    /// 勝負の結果で比べる: 前へ押しながら弱の高速連打（rush）と、間合いまで寄って溜めた強を 1 発ずつ（patient）を、既定の CPU に対して
    /// 種 4 つ × 50 秒（人形の単位の 30 秒）。与えた・受けたダメージ（得点の点）、奪ったダウン・奪われたダウン、CPU の意識の最小。
    /// </summary>
    [TestMethod]
    public void RushVersusPatientOutcome()
    {
        foreach (var mode in new[] { "rush", "patient" })
        {
            double dealt = 0, taken = 0, minAwake = 0;
            int downsGiven = 0, downsTaken = 0;
            foreach (var seed in new[] { 1, 2, 3, 4 })
            {
                using var game = new BoxingGame(FighterTuning.Default, seed: seed,
                    corners: BoxingCorners.Default with { Player = _ => new IntentControl() });
                var control = (IntentControl)game.PlayerControl!;
                var clock = 0f;
                bool cpuDown = false, playerDown = false;
                var awake = 1f;
                var lastPlayer = game.Player;
                double seedDealt = 0, seedTaken = 0;
                while (clock < 50.35f)
                {
                    var p = game.Player;
                    var close = Vector3.Distance(p.ChestPosition, game.Cpu.ChestPosition) < 1.1f * p.ArmReach;
                    if (mode == "rush")
                    {
                        var phase = clock / 0.1343f;
                        control.Input = new FighterIntent(new Vector2(0f, 1f), Light: phase - MathF.Floor(phase) < 0.5f);
                    }
                    else
                    {
                        var phase = clock % 1.511f;
                        control.Input = new FighterIntent(new Vector2(0f, close ? 0f : 1f), Heavy: close && phase < 0.4196f);
                    }

                    game.Step(new BoxingInput(default), Dt);
                    clock += Dt;
                    foreach (var hit in game.RecentHits)
                    {
                        if (hit.Attacker == game.Player)
                        {
                            seedDealt += hit.Damage;
                        }
                        else
                        {
                            seedTaken += hit.Damage;
                        }
                    }

                    downsGiven += game.Cpu.IsKnockedDown && !cpuDown ? 1 : 0;
                    downsTaken += game.Player.IsKnockedDown && !playerDown ? 1 : 0;
                    cpuDown = game.Cpu.IsKnockedDown;
                    playerDown = game.Player.IsKnockedDown;
                    awake = MathF.Min(awake, game.Cpu.Consciousness);
                    lastPlayer = game.Player;
                }

                dealt += seedDealt;
                taken += seedTaken;
                minAwake += awake;
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"  {mode} seed {seed}: dealt {seedDealt:F0}, taken {seedTaken:F0}, cpu awake min {awake:F2}, started {control.AttacksStarted}"));
            }

            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{mode}: dealt {dealt:F0}, taken {taken:F0}, downs given {downsGiven}, downs taken {downsTaken}, cpu awake min (mean) {minAwake / 4:F2}"));
        }
    }

    /// <summary>CmaTuneTests の打ち返しのダメージを、種ごとに、打ち返す受け手・打ち返さない受け手で出す。</summary>
    [TestMethod]
    public void CounterDamageBySeed()
    {
        foreach (var seed in new[] { 2, 3, 5, 6, 7 })
        {
            var with = Bots.DefenseDrill.Punch("2", Bots.Defense.Slip, Bots.DefenseDrill.Defender(Bots.BotCatalog.Champion.Defenses, defend: true, Bots.DefenseDrill.DefaultRange, true), seed: seed, seconds: 23.5f, cue: 0.0671f);
            var without = Bots.DefenseDrill.Punch("2", Bots.Defense.Slip, Bots.DefenseDrill.Defender(Bots.BotCatalog.Champion.Defenses, defend: true, Bots.DefenseDrill.DefaultRange, false), seed: seed, seconds: 23.5f, cue: 0.0671f);
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"seed {seed}: with counters {with.CounterDamage:F1}, without {without.CounterDamage:F1}"));
        }
    }

    /// <summary>
    /// 溜めた強（保持 <c>hold</c> 秒）と短押しの弱を、ガードを固めて立つ相手に、間合いで 1 発ずつ。種 1〜3 × 34 秒。
    /// 1 発あたりの当たる速さ・乗る質量・体の割合・運動量・ダメージと、拳を出した瞬間の胸の回転・重心の前進の速さ。
    /// </summary>
    [TestMethod]
    public void ChargedVersusTap()
    {
        foreach (var (name, heavy, hold, period) in new[] { ("tap jab", false, 0f, 1.007f), ("tap cross", true, 0f, 1.007f), ("charged cross", true, 0.4196f, 1.678f), ("charged + step", true, 0.4196f, 1.678f) })
        {
            int hits = 0;
            double speed = 0, mass = 0, share = 0, momentum = 0, damage = 0, spin = 0, advance = 0;
            foreach (var seed in new[] { 1, 2, 3 })
            {
                var corners = BoxingCorners.Default with { Player = _ => new IntentControl(), Cpu = _ => new IntentControl() };
                using var game = new BoxingGame(FighterTuning.Default, seed: seed, corners: corners);
                var control = (IntentControl)game.PlayerControl!;
                var cpu = (IntentControl)game.CpuControl;
                var clock = 0f;
                while (clock < 33.57f)
                {
                    var p = game.Player;
                    var close = Vector3.Distance(p.ChestPosition with { Y = 0f }, game.Cpu.ChestPosition with { Y = 0f }) < 1.15f * p.ArmReach;
                    var phase = clock % period;
                    var press = close && phase < MathF.Max(hold, 0.0839f) + Dt;
                    var step = name.EndsWith("step", StringComparison.Ordinal) && close && phase >= hold && phase < hold + 0.3357f;
                    control.Input = new FighterIntent(new Vector2(0f, close && !step ? 0f : 1f), Light: press && !heavy, Heavy: press && heavy);
                    cpu.Input = new FighterIntent(Vector2.Zero, Evade: true);
                    game.Step(new BoxingInput(default), Dt);
                    clock += Dt;
                    foreach (var hit in game.RecentHits.Where(h => h.Attacker == game.Player && !h.Kick))
                    {
                        hits++;
                        speed += hit.Speed;
                        mass += hit.ImpactMass;
                        share += hit.BodyShare;
                        momentum += hit.Momentum;
                        damage += hit.Damage;
                        spin += MathF.Abs(game.Player.ChestAngularVelocity.Y);
                        advance += Vector3.Dot(game.Player.ChestVelocity with { Y = 0f }, Vector3.Normalize((game.Cpu.ChestPosition - game.Player.ChestPosition) with { Y = 0f }));
                    }
                }
            }

            var n = Math.Max(1, hits);
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{name}: hits {hits}, speed {speed / n:F2} m/s, mass {mass / n:F2} kg, bodyShare {share / n:P0}, momentum {momentum / n:F2}, dmg {damage / n:F2}/hit, chest spin {spin / n:F1} rad/s, advance {advance / n:F2} m/s"));
        }
    }

    private sealed class Tally
    {
        public int Hits, Retracting, Slow, Pulling;
        public double Damage, Speed, Rel, Mass, Share, SlowDamage, Push, Straight, RetractDamage, BodyClose, BodyRatio;

        public string Line(string who) => Hits == 0 ? $"{who}: no hits" : string.Create(CultureInfo.InvariantCulture,
            $"{who}: hits {Hits}, dmg {Damage:F0} ({Damage / Hits:F2}/hit), speed {Speed / Hits:F2} vs rel {Rel / Hits:F2}, rel<half {Slow * 100 / Hits}% ({SlowDamage:F0}), " +
            $"mass {Mass / Hits:F1} kg, bodyShare {Share / Hits:P0}, retracting {Retracting * 100 / Hits}% ({RetractDamage:F0}), arm push {Push / Hits:F1} N (pulling {Pulling * 100 / Hits}%), straight {Straight / Hits:F2}, shoulder closing {BodyClose / Hits:F2} m/s (shoulder/fist {BodyRatio / Hits:F2})");
    }

    private static void Run(float period, float forward, int seed)
    {
        using var game = new BoxingGame(FighterTuning.Default, seed: seed,
            corners: BoxingCorners.Default with { Player = _ => new IntentControl() });
        var control = (IntentControl)game.PlayerControl!;
        var clock = 0f;
        var tallies = new[] { new Tally(), new Tally() };
        var gloveV = new Dictionary<(int, Hand), Vector3>();
        var chestV = new Vector3[2];
        var shoulderV = new Dictionary<(int, Hand), Vector3>();
        var lastShoulder = new Dictionary<(int, Hand), Vector3>();
        var headV = new Vector3[2];
        var lastHead = new[] { game.Player.HeadPosition, game.Cpu.HeadPosition };
        Remember();
        while (clock < 33.57f)
        {
            var phase = clock / period;
            var down = phase - MathF.Floor(phase) < 0.5f;
            control.Input = new FighterIntent(new Vector2(0f, forward), Light: down);
            game.Step(new BoxingInput(default), Dt);
            clock += Dt;
            foreach (var hit in game.RecentHits)
            {
                if (hit.Kick)
                {
                    continue;
                }

                var p = hit.Attacker;
                var v = hit.Victim;
                var pi = p == game.Player ? 0 : 1;
                var vi = 1 - pi;
                var t = tallies[pi];
                var shoulder = p.ShoulderPosition(hit.Hand);
                var dir = p.GloveCenter(hit.Hand) - shoulder;
                var straight = dir.Length() / p.ArmReach;
                dir = dir.LengthSquared() > 1e-8f ? Vector3.Normalize(dir) : Vector3.UnitZ;
                var g = gloveV[(pi, hit.Hand)];
                var strike = g.LengthSquared() > 1e-8f ? Vector3.Normalize(g) : dir;
                t.Hits++;
                if (Vector3.Dot(g - chestV[pi], dir) < 0f)
                {
                    t.Retracting++;
                    t.RetractDamage += hit.Damage;
                }

                var victimV = Vector3.Distance(hit.Point, v.HeadPosition) < Vector3.Distance(hit.Point, v.ChestPosition) ? headV[vi] : chestV[vi];
                var rel = Vector3.Dot(g - victimV, strike);
                if (rel < 0.5f * hit.Speed)
                {
                    t.Slow++;
                    t.SlowDamage += hit.Damage;
                }

                var push = Vector3.Dot(p.HandPush(hit.Hand), strike);
                if (push < 0f)
                {
                    t.Pulling++;
                }

                t.Damage += hit.Damage;
                t.Speed += hit.Speed;
                t.Mass += hit.ImpactMass;
                t.Share += hit.BodyShare;
                t.Rel += rel;
                t.Push += push;
                t.Straight += straight;
                var bodyClose = Vector3.Dot(shoulderV.GetValueOrDefault((pi, hit.Hand)) - victimV, strike);
                t.BodyClose += bodyClose;
                t.BodyRatio += Math.Clamp(bodyClose / MathF.Max(0.1678f, rel), 0f, 1f);
            }

            Remember();
        }

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"fwd {forward} tap {period:F2}s (started {control.AttacksStarted}, stamina {game.Player.Stamina:P0})"));
        Console.WriteLine("  " + tallies[0].Line("player"));
        Console.WriteLine("  " + tallies[1].Line("cpu   "));

        void Remember()
        {
            for (var i = 0; i < 2; i++)
            {
                var f = i == 0 ? game.Player : game.Cpu;
                foreach (var hand in new[] { Hand.Left, Hand.Right })
                {
                    gloveV[(i, hand)] = f.GloveVelocity(hand);
                    var sh = f.ShoulderPosition(hand);
                    shoulderV[(i, hand)] = lastShoulder.TryGetValue((i, hand), out var ls) ? (sh - ls) / Dt : Vector3.Zero;
                    lastShoulder[(i, hand)] = sh;
                }

                chestV[i] = f.ChestVelocity;
                headV[i] = (f.HeadPosition - lastHead[i]) / Dt;
                lastHead[i] = f.HeadPosition;
            }
        }
    }
}
