using System.Globalization;
using System.Numerics;
using System.Text;
using MiniRing.Weapons;

namespace MiniRing.Tests;

/// <summary>
/// 調べもの: 肩と胴のつなぎ目を通る刃が、体のどこかに当たるか。立って構えた相手（動かない）の肩の関節を通る面を、日本刀が決まった向きに、
/// 決まった速さで、関節からの横のずれを振って（−12 mm から 12 mm）通る。当たったか（<see cref="BoxingGame.RecentCuts"/> に触れた接触があるか）を、
/// ずれと速さの表にする。
/// </summary>
[TestClass]
public sealed class ShoulderSeamProbe
{
    public TestContext TestContext { get; set; } = null!;

    internal static readonly (string Name, Func<Vector3, Vector3, Vector3, (Vector3 Heading, Vector3 Across, Vector3 Scan)> Axes)[] Cuts =
    [
        ("下へ・刃は前後", (inward, up, forward) => (-up, forward, inward)),
        ("下へ・刃は左右", (inward, up, forward) => (-up, inward, forward)),
        ("体の中心へ・刃は前後", (inward, up, forward) => (inward, forward, up)),
        ("体の中心へ・刃は上下", (inward, up, forward) => (inward, up, forward)),
        ("斜め下の内へ・刃は前後", (inward, up, forward) => (Vector3.Normalize(inward - up), forward, Vector3.Normalize(inward + up))),
        ("後ろへ・刃は上下", (inward, up, forward) => (-forward, up, inward)),
        ("後ろへ・刃は左右", (inward, up, forward) => (-forward, inward, up)),
    ];

    [TestMethod]
    public void SweepTheShoulderSeam()
    {
        var offsets = Enumerable.Range(-8, 17).Select(i => i * 0.0015f).ToArray();
        var speeds = new[] { 0.4f, 3f, 8f, 16f };
        var rows = new List<(string Cut, string Hand, float Offset, float Speed, bool Hit)>();
        var jobs = new List<(string Cut, int CutIndex, Hand Hand, float Offset, float Speed)>();
        for (var c = 0; c < Cuts.Length; c++)
        {
            foreach (var hand in new[] { Hand.Left, Hand.Right })
            {
                foreach (var offset in offsets)
                {
                    foreach (var speed in speeds)
                    {
                        jobs.Add((Cuts[c].Name, c, hand, offset, speed));
                    }
                }
            }
        }

        var results = new bool[jobs.Count];
        Parallel.For(0, jobs.Count, i => results[i] = Sweep(jobs[i].CutIndex, jobs[i].Hand, jobs[i].Offset, jobs[i].Speed));
        for (var i = 0; i < jobs.Count; i++)
        {
            rows.Add((jobs[i].Cut, jobs[i].Hand.ToString(), jobs[i].Offset, jobs[i].Speed, results[i]));
        }

        var sb = new StringBuilder();
        foreach (var cut in rows.GroupBy(r => (r.Cut, r.Hand)))
        {
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"{cut.Key.Cut} / {cut.Key.Hand}  (横のずれ -12..+12 mm; # = 当たった, . = 当たらず)"));
            foreach (var speed in speeds)
            {
                var line = string.Concat(cut.Where(r => r.Speed == speed).OrderBy(r => r.Offset).Select(r => r.Hit ? '#' : '.'));
                sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"  {speed,5:0.0} m/s  {line}"));
            }
        }

        TestContext.WriteLine(sb.ToString());
        TestContext.WriteLine($"当たらなかった: {rows.Count(r => !r.Hit)} / {rows.Count}");
    }

    /// <summary>
    /// 刃を肩の関節へ押し込み続けて（速さを毎歩与え直す: 腕が押し込む刃と同じ）、歩の終わりに刃のセルの中心が体の部位の埋まったセルの中にあるのに、
    /// その歩にその部位との接触が何も言わなかった歩（刃が体の中で何も起きない 1 歩）の数。
    /// </summary>
    internal static int EmbeddedSteps(int cutIndex, Hand hand, float offset, float speed, int passes)
    {
        using var game = new BoxingGame(FighterTuning.Default, 1, new BoxingCorners("stand", _ => new StandGuard(), _ => new StandGuard()));
        game.MaxContactPasses = passes;
        for (var i = 0; i < 90; i++)
        {
            game.Step(default, 1f / 60f);
        }

        var fighter = game.Cpu;
        var (joint, inward, up, forward) = ShoulderAxes(game, fighter, hand);
        var (heading, across, scan) = Cuts[cutIndex].Axes(inward, up, forward);
        var orientation = WeaponDrills.Edge(heading, across);
        var kind = WeaponKind.Katana;
        var middle = (Weapon.TipOf(kind).X + Weapon.TwoHandGripsOf(kind).Bottom.X) * 0.5f;
        var start = joint + (scan * offset) - (heading * 0.10f) - Vector3.Transform(new Vector3(middle, 0f, 0f), orientation);
        var weapon = game.AddWeapon(kind, start, orientation);
        var cells = new List<Vector3>();
        var grid = weapon.Body.Grid;
        for (var i = 0; i < grid.CellCount; i++)
        {
            if (grid.MaterialAt(i) != 0)
            {
                var (x, y, z) = grid.Coordinates(i);
                if ((i % 5) == 0 && (grid.CellCenter(x, y, z) + weapon.Body.GridCenterLocal).X > Weapon.TipOf(kind).X - 0.12f)
                {
                    cells.Add(grid.CellCenter(x, y, z));
                }
            }
        }

        var embedded = 0;
        var steps = (int)MathF.Ceiling(0.25f / speed * 240f) + 24;
        for (var i = 0; i < steps; i++)
        {
            game.World.SetLinearVelocity(weapon.Body.BodyId, heading * speed);
            game.World.SetAngularVelocity(weapon.Body.BodyId, Vector3.Zero);
            game.Step(default, 1f / 240f);
            var toWorld = weapon.Body.GridToWorld();
            foreach (var part in fighter.Voxels!.Parts.Where(p => p.Kind == BodyPartKind.Chest || p.Body.BodyId == fighter.UpperArmId(hand)))
            {
                if (game.RecentCuts.Any(c => c.Touched && (c.A == part.Body || c.B == part.Body)))
                {
                    continue;
                }

                Matrix4x4.Invert(part.Body.GridToWorld(), out var toPart);
                var m = toWorld * toPart;
                var inside = cells.Any(c =>
                {
                    var at = part.Body.Grid.CellAt(Vector3.Transform(c, m));
                    return at >= 0 && part.Body.Grid.MaterialAt(at) != 0;
                });
                embedded += inside ? 1 : 0;
            }
        }

        return embedded;
    }

    /// <summary>刃を肩の関節へ押し込んで、刃が体の中にあるのに何も起きない歩を数える。接触の掃き直し 1 回（以前）と 3 回で比べる。</summary>
    [TestMethod]
    public void EmbeddedBladesAtTheShoulder()
    {
        var offsets = Enumerable.Range(-4, 9).Select(i => i * 0.003f).ToArray();
        var speeds = new[] { 2f };
        var jobs = new List<(int Cut, Hand Hand, float Offset, float Speed)>();
        for (var c = 0; c < Cuts.Length; c++)
        {
            foreach (var hand in new[] { Hand.Left, Hand.Right })
            {
                foreach (var offset in offsets)
                {
                    foreach (var speed in speeds)
                    {
                        jobs.Add((c, hand, offset, speed));
                    }
                }
            }
        }

        foreach (var passes in new[] { 1, 3 })
        {
            var counts = new int[jobs.Count];
            Parallel.For(0, jobs.Count, k => counts[k] = EmbeddedSteps(jobs[k].Cut, jobs[k].Hand, jobs[k].Offset, jobs[k].Speed, passes));
            TestContext.WriteLine($"掃き直し {passes} 回: 刃が体の中にあるのに接触が無かった歩 計 {counts.Sum()}、そうなった押し込み {counts.Count(n => n > 0)} / {jobs.Count}、最大 {counts.Max()} 歩");
            foreach (var g in jobs.Select((j, k) => (j, n: counts[k])).Where(x => x.n > 0).GroupBy(x => (x.j.Cut, x.j.Hand)))
            {
                TestContext.WriteLine($"  {Cuts[g.Key.Cut].Name} (番号 {g.Key.Cut}) / {g.Key.Hand}: {g.Count()} 件、計 {g.Sum(x => x.n)} 歩: " + string.Join(", ", g.Select(x => string.Create(CultureInfo.InvariantCulture, $"[ずれ {x.j.Offset * 1000f:0.0} mm, {x.j.Speed:0.0} m/s, {x.n} 歩]"))));
            }
        }
    }

    /// <summary>1 回: 肩の関節から <paramref name="offset"/> 横へずらした面を、向き <paramref name="cutIndex"/> の刃が <paramref name="speed"/> で通る。当たったら true。</summary>
    internal static bool Sweep(int cutIndex, Hand hand, float offset, float speed)
    {
        using var game = new BoxingGame(FighterTuning.Default, 1, new BoxingCorners("stand", _ => new StandGuard(), _ => new StandGuard()));
        for (var i = 0; i < 90; i++)
        {
            game.Step(default, 1f / 60f);
        }

        var fighter = game.Cpu;
        var (joint, inward, up, forward) = ShoulderAxes(game, fighter, hand);
        var (heading, across, scan) = Cuts[cutIndex].Axes(inward, up, forward);
        var orientation = WeaponDrills.Edge(heading, across);
        var kind = WeaponKind.Katana;
        // 刃の真ん中（刀身の中央）が通る線を、関節 + 横のずれ の面に置く。始めは関節の手前 15 cm。
        var middle = (Weapon.TipOf(kind).X + Weapon.TwoHandGripsOf(kind).Bottom.X) * 0.5f;
        var local = new Vector3(middle, 0f, 0f);
        var target = joint + (scan * offset);
        var start = target - (heading * 0.15f) - Vector3.Transform(local, orientation);
        var weapon = game.AddWeapon(kind, start, orientation);
        var steps = (int)MathF.Ceiling(0.30f / speed * 240f) + 2;
        for (var i = 0; i < steps; i++)
        {
            game.World.SetLinearVelocity(weapon.Body.BodyId, heading * speed);
            game.World.SetAngularVelocity(weapon.Body.BodyId, Vector3.Zero);
            game.Step(default, 1f / 240f);
            if (game.RecentCuts.Any(c => c.Touched))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>切っ先を向けて（刺す向きに刀身を向け）肩の関節の周りの 2 次元の格子の点を通る。点ごとに、体が動かないとして線の上に体のセルがあるか（地図）と比べる。</summary>
    [TestMethod]
    public void StabThroughTheShoulderSeam()
    {
        var offsets = Enumerable.Range(-8, 17).Select(i => i * 0.0015f).ToArray();
        var speeds = new[] { 3f, 14f };
        var dirs = new[] { "下", "体の中心へ", "後ろへ", "斜め下の内へ" };
        var jobs = new List<(int Dir, Hand Hand, float Speed, int I, int J)>();
        foreach (var d in Enumerable.Range(0, dirs.Length))
        {
            foreach (var hand in new[] { Hand.Left, Hand.Right })
            {
                foreach (var speed in speeds)
                {
                    for (var i = 0; i < offsets.Length; i++)
                    {
                        for (var j = 0; j < offsets.Length; j++)
                        {
                            jobs.Add((d, hand, speed, i, j));
                        }
                    }
                }
            }
        }

        var results = new (bool Hit, bool Expected)[jobs.Count];
        Parallel.For(0, jobs.Count, k => results[k] = Stab(jobs[k].Dir, jobs[k].Hand, jobs[k].Speed, offsets[jobs[k].I], offsets[jobs[k].J]));
        var sb = new StringBuilder();
        var bugs = 0;
        var air = 0;
        foreach (var g in jobs.Select((j, k) => (j, r: results[k])).GroupBy(x => (x.j.Dir, x.j.Hand, x.j.Speed)))
        {
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"{dirs[g.Key.Dir]} / {g.Key.Hand} / {g.Key.Speed:0} m/s   (# 当たった, . 空気を通った, X 体のセルがあるのに当たらず, o 線に体は無いが当たった)"));
            foreach (var row in g.GroupBy(x => x.j.J).OrderBy(r => r.Key))
            {
                sb.AppendLine("  " + string.Concat(row.OrderBy(x => x.j.I).Select(x => x.r.Hit ? (x.r.Expected ? '#' : 'o') : x.r.Expected ? 'X' : '.')));
            }

            bugs += g.Count(x => !x.r.Hit && x.r.Expected);
            air += g.Count(x => !x.r.Hit && !x.r.Expected);
        }

        TestContext.WriteLine(sb.ToString());
        TestContext.WriteLine($"体のセルがあるのに当たらなかった: {bugs} / {jobs.Count}。空気を通った: {air}");
    }

    private static (bool Hit, bool Expected) Stab(int dir, Hand hand, float speed, float a, float b)
    {
        using var game = new BoxingGame(FighterTuning.Default, 1, new BoxingCorners("stand", _ => new StandGuard(), _ => new StandGuard()));
        for (var i = 0; i < 90; i++)
        {
            game.Step(default, 1f / 60f);
        }

        var fighter = game.Cpu;
        var (joint, inward, up, forward) = ShoulderAxes(game, fighter, hand);
        var (heading, u, v) = dir switch
        {
            0 => (-up, inward, forward),
            1 => (inward, up, forward),
            2 => (-forward, inward, up),
            _ => (Vector3.Normalize(inward - up), Vector3.Normalize(inward + up), forward),
        };
        var target = joint + (u * a) + (v * b);
        // 体のセルが線の上にあるか（動かない体として）。
        var expected = false;
        foreach (var part in fighter.Voxels!.Parts)
        {
            for (var s = -0.15f; s <= 0.15f && !expected; s += 0.001f)
            {
                var cell = part.Body.Grid.CellAt(part.Body.WorldToGrid(target + (heading * s)));
                expected |= cell >= 0 && part.Body.Grid.MaterialAt(cell) != 0;
            }
        }

        var kind = WeaponKind.Katana;
        var orientation = WeaponDrills.Point(heading);
        var tip = Weapon.TipOf(kind);
        var start = target - (heading * 0.15f) - Vector3.Transform(tip, orientation);
        var weapon = game.AddWeapon(kind, start, orientation);
        var steps = (int)MathF.Ceiling(0.30f / speed * 240f) + 2;
        for (var i = 0; i < steps; i++)
        {
            game.World.SetLinearVelocity(weapon.Body.BodyId, heading * speed);
            game.World.SetAngularVelocity(weapon.Body.BodyId, Vector3.Zero);
            game.Step(default, 1f / 240f);
            if (game.RecentCuts.Any(c => c.Touched))
            {
                return (true, expected);
            }
        }

        return (false, expected);
    }

    internal static (Vector3 Joint, Vector3 Inward, Vector3 Up, Vector3 Forward) ShoulderAxes(BoxingGame game, Fighter fighter, Hand hand)
    {
        var tuning = FighterTuning.Default;
        var u = game.World.GetState(fighter.UpperArmId(hand));
        var xAxis = Vector3.Transform(Vector3.UnitX, u.Orientation);
        var joint = u.Position - (xAxis * hand.LocalSide() * (tuning.HalfExtents.UpperArm.X - tuning.JointOverlap));
        var frame = fighter.Frame;
        var inward = frame.Right * hand.LocalSide();
        return (joint, inward, Vector3.UnitY, frame.Forward);
    }
}
