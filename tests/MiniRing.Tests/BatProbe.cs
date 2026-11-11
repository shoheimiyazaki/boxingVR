using MiniRing.Bots;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>
/// 調査用: 拳の 1 発と、棍棒（片手・両手のバット）の 1 振りが、相手の体のボクセルをどれだけ壊すかを並べる
/// （--logger "console;verbosity=detailed"）。
/// </summary>
[TestClass]
public sealed class BatProbe
{
    private static int Cells(Fighter f) => f.Voxels!.Parts.Sum(p => p.Body.Grid.FilledCount);

    [TestMethod]
    [TestCategory("Probe")]
    public void CellsPerBlow()
    {
        // 拳: 最強のボット同士の 30 秒。打たれた歩の前後で、打たれた側のセルの減りを数える。
        var punches = new List<(float Damage, int Cells)>();
        using (var game = new BoxingGame(seed: 1, corners: new BoxingCorners("champion", s => BotCatalog.Create("champion", s), s => BotCatalog.Create("slugger", s))))
        {
            for (var i = 0; i < 240 * 30 && !game.RoundOver; i++)
            {
                var before = (Cells(game.Player), Cells(game.Cpu));
                game.Step(default, 1f / 240f);
                if (game.LastHit is { } hit && game.RecentHits.Count > 0)
                {
                    foreach (var h in game.RecentHits)
                    {
                        var lost = h.Victim == game.Player ? before.Item1 - Cells(game.Player) : before.Item2 - Cells(game.Cpu);
                        punches.Add((h.Damage, lost));
                    }
                }
            }
        }

        Console.WriteLine($"拳: {punches.Count} 発、壊したセル 平均 {punches.Average(p => p.Cells):0.0}、最大 {punches.Max(p => p.Cells)}、ダメージ 平均 {punches.Average(p => p.Damage):0.0}");
        var heavy = punches.OrderByDescending(p => p.Cells).Take(5).Select(p => $"{p.Cells}({p.Damage:0})");
        Console.WriteLine($"拳の重い 5 発: {string.Join(", ", heavy)}");

        foreach (var family in ClubBot.OneHanded.Append(SwingFamily.Bat))
        {
            var r = SwingTest.Run(WeaponSwing.Default(family), 0.38f);
            Console.WriteLine($"{family} 出発点: power {r.Power:0.000} part {r.Part} cells {r.Cells:0} momentum {r.Impulse:0.00} hitSpeed {r.HitTipSpeed:0.0} tip {r.TipSpeed:0.0} miss {r.HeadMiss * 100f:0.0}cm");
        }

        var path = Path.Combine(AppContext.BaseDirectory, "../../../../../docs/arena/club_swings.json");
        if (File.Exists(path))
        {
            foreach (var (swing, range) in System.Text.Json.JsonSerializer.Deserialize<List<Saved>>(File.ReadAllText(path), Json)!.Select(s => (s.Swing, s.Range)))
            {
                var r = SwingTest.Run(swing, range);
                Console.WriteLine($"{swing.Family} 研究した振り: power {r.Power:0.000} part {r.Part} cells {r.Cells:0} momentum {r.Impulse:0.00} hitSpeed {r.HitTipSpeed:0.0}");
            }
        }
    }

    private static readonly System.Text.Json.JsonSerializerOptions Json = new() { IncludeFields = true };

    private sealed record Saved(WeaponSwing Swing, float Range);

    [TestMethod]
    [TestCategory("Probe")]
    public void TheBatStance()
    {
        using var game = new BoxingGame(seed: 1, corners: new BoxingCorners("stand", _ => new StandGuard(), _ => new ClubBot([WeaponSwing.Default(SwingFamily.Bat)], 1, rest: 1000f, range: 0.1f), PlayerWeapon: WeaponKind.WoodenClub, PlayerTwoHanded: true));
        for (var t = 0f; t < 2f; t += 1f / 240f)
        {
            game.Step(default, 1f / 240f);
            if (MathF.Abs(t % 0.25f) < 1f / 240f)
            {
                var club = game.WeaponOf(game.Player)!;
                var s = game.World.GetState(club.Body.BodyId);
                var (top, bottom) = Weapon.TwoHandGripsOf(club.Kind);
                var bottomWorld = s.Position + System.Numerics.Vector3.Transform(bottom, s.Orientation);
                var topWorld = s.Position + System.Numerics.Vector3.Transform(top, s.Orientation);
                Console.WriteLine($"t {t:0.00} support {game.Player.SupportOf(Hand.Right)} left-to-grip {System.Numerics.Vector3.Distance(game.Player.GloveCenter(Hand.Left), bottomWorld) * 100f:0.0}cm right-to-grip {System.Numerics.Vector3.Distance(game.Player.GloveCenter(Hand.Right), topWorld) * 100f:0.0}cm axis {System.Numerics.Vector3.Transform(System.Numerics.Vector3.UnitX, s.Orientation)} standing {game.Player.State}");
            }
        }
    }

    [TestMethod]
    [TestCategory("Probe")]
    public void OneHandVersusTwo()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "../../../../../docs/arena/bat_swings.json");
        var swings = new List<(WeaponSwing Swing, float Range)> { (WeaponSwing.Default(SwingFamily.Bat), 0.38f) };
        if (File.Exists(path))
        {
            swings.AddRange(System.Text.Json.JsonSerializer.Deserialize<List<Saved>>(File.ReadAllText(path), Json)!.Select(s => (s.Swing, s.Range)));
        }

        foreach (var (swing, range) in swings)
        {
            foreach (var two in new[] { false, true })
            {
                var lag = 0f;
                var fist = 0f;
                var spin = 0f;
                var r = SwingTest.Run(swing, range, (game, t, swinging) =>
                {
                    if (swinging)
                    {
                        var torso = game.World.GetState(game.Player.Parts.Chest);
                        fist = MathF.Max(fist, (game.Player.GloveVelocity(Hand.Right) - torso.LinearVelocity).Length());
                        spin = MathF.Max(spin, (game.World.GetState(game.WeaponOf(game.Player)!.Body.BodyId).AngularVelocity - torso.AngularVelocity).Length());
                    }

                    if (swinging && two)
                    {
                        lag = MathF.Max(lag, System.Numerics.Vector3.Distance(game.Player.GloveCenter(Hand.Left), game.Player.HandTargetWorld(Hand.Left)));
                    }
                }, twoHanded: two);
                Console.WriteLine($"two {two}: power {r.Power:0.000} tip {r.TipSpeed:0.00} hit {r.HitTipSpeed:0.00} part {r.Part} cells {r.Cells:0} left-lag {lag * 100f:0.0}cm fist {fist:0.00} spin {spin:0.0}");
            }
        }
    }

    [TestMethod]
    [TestCategory("Probe")]
    public void SelfHits()
    {
        var list = new List<(WeaponSwing Swing, float Range)>();
        foreach (var file in new[] { "club_swings.json", "bat_swings.json" })
        {
            var path = Path.Combine(AppContext.BaseDirectory, "../../../../../docs/arena/" + file);
            list.AddRange(System.Text.Json.JsonSerializer.Deserialize<List<Saved>>(File.ReadAllText(path), Json)!.Select(s => (s.Swing, s.Range)));
        }

        list.AddRange(ClubBot.OneHanded.Append(SwingFamily.Bat).Select(f => (WeaponSwing.Default(f), 0.38f)));
        foreach (var (swing, range) in list)
        {
            var hits = new List<WeaponHitEvent>();
            var startCells = 0;
            var endCells = 0;
            var r = SwingTest.Run(swing, range, (game, t, swinging) =>
            {
                startCells = startCells == 0 ? Cells(game.Player) : startCells;
                endCells = Cells(game.Player);
                hits.AddRange(game.SelfWeaponHits);
            });
            Console.WriteLine($"{swing.Family} power {r.Power:0.000} self-hits {hits.Count} [{string.Join(", ", hits.Select(h => $"{h.Part}@{h.Speed:0.0}m/s cells {h.Cells} twist {h.HeadTwist:0}"))}] own cells lost {startCells - endCells}");
        }
    }
}
