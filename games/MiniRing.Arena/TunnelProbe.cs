using System.Globalization;
using System.Numerics;
using System.Text;
using Aigis.Physics;
using Aigis.Physics.Voxels;
using MiniRing.Bots;
using MiniRing.Weapons;

namespace MiniRing.Arena;

/// <summary>
/// 調査: 刃が体の部位のセルの中を通ったのに、接触が何も報告しなかった歩（すり抜け）を数える。練習試合を物理の 1 歩（1/240 秒）ずつ進め、
/// 握られた剣ごと・体の部位ごとに、歩の前の姿勢から後の姿勢まで（剣も部位も）を細かく刻み、剣のセルの中心が部位の埋まったセルに入る姿勢があるかを、
/// 接触の解き方（<see cref="VoxelContact"/> の振り分け・候補の絞り込み）を通さずに全部のセルで調べる。あって、その歩の接触が触れたと言っていなければ、すり抜け。
/// 歩の前から既に埋まっていた分（刺さったままの剣）と、歩の前は離れていて途中から入った分（本当にすり抜けた可能性）を分けて数える。
/// </summary>
internal static class TunnelProbe
{
    private const float Dt = 1f / 240f;

    /// <summary>接触を掃き直す回数（<see cref="BoxingGame.MaxContactPasses"/>）。<c>--passes</c> で変える。</summary>
    public static int Passes { get; set; } = 3;
    private const int SubSteps = 16;

    internal sealed record Miss(string Pair, float At, string Weapon, string Victim, BodyPartKind Part, bool AlreadyInside, int Cells, float Speed, string Materials);

    public static string Run(IReadOnlyList<Contender> contenders, string[] pairs, int bouts, float seconds, TextWriter log)
    {
        var all = new List<Miss>();
        var totals = new Dictionary<string, (int Steps, int Checks)>();
        foreach (var pair in pairs)
        {
            var names = pair.Split('-');
            var red = contenders.Single(c => c.Name == names[0]);
            var blue = contenders.Single(c => c.Name == names[1]);
            var results = new (List<Miss>, int, int)[bouts];
            Parallel.For(0, bouts, b => results[b] = Bout(b % 2 == 0 ? red : blue, b % 2 == 0 ? blue : red, 1 + (b / 2), seconds, pair));
            foreach (var (m, steps, checks) in results)
            {
                all.AddRange(m);
                var t = totals.GetValueOrDefault(pair);
                totals[pair] = (t.Steps + steps, t.Checks + checks);
            }

            log.WriteLine($"{pair}: misses {results.Sum(r => r.Item1.Count)}");
        }

        var report = new StringBuilder();
        report.AppendLine("| 組 | 物理の歩 | 調べた（剣 × 部位 × 歩） | すり抜け（途中から入った） | 刺さったまま（歩の前から埋まっていた） |");
        report.AppendLine("|---|---:|---:|---:|---:|");
        foreach (var (pair, t) in totals)
        {
            var mine = all.Where(m => m.Pair == pair).ToList();
            report.AppendLine(string.Create(CultureInfo.InvariantCulture, $"| {pair} | {t.Steps} | {t.Checks} | {mine.Count(m => !m.AlreadyInside)} | {mine.Count(m => m.AlreadyInside)} |"));
        }

        foreach (var (label, group) in new[] { ("すり抜け（途中から入った）", all.Where(m => !m.AlreadyInside).ToList()), ("刺さったまま接触が無い", all.Where(m => m.AlreadyInside).ToList()) })
        {
            report.AppendLine();
            report.AppendLine(string.Create(CultureInfo.InvariantCulture, $"{label}: {group.Count} 歩。部位別:"));
            report.AppendLine();
            report.AppendLine("| 部位 | 自分 / 相手 | 歩 | 重なったセル（最大） | 剣の速さ（最大 m/s） |");
            report.AppendLine("|---|---|---:|---:|---:|");
            foreach (var g in group.GroupBy(m => (m.Part, m.Victim)).OrderByDescending(g => g.Count()))
            {
                report.AppendLine(string.Create(CultureInfo.InvariantCulture, $"| {g.Key.Part} | {g.Key.Victim} | {g.Count()} | {g.Max(m => m.Cells)} | {g.Max(m => m.Speed):0.0} |"));
            }

            report.AppendLine();
            report.AppendLine("先の 20 件:");
            report.AppendLine();
            report.AppendLine("| 組 | 時刻 | 剣 | 部位 | 自分 / 相手 | 重なったセル | 剣の速さ | 材質 |");
            report.AppendLine("|---|---:|---|---|---|---:|---:|---|");
            foreach (var m in group.OrderByDescending(m => m.Cells).Take(20))
            {
                report.AppendLine(string.Create(CultureInfo.InvariantCulture, $"| {m.Pair} | {m.At:0.00} | {m.Weapon} | {m.Part} | {m.Victim} | {m.Cells} | {m.Speed:0.0} | {m.Materials} |"));
            }
        }

        return report.ToString();
    }

    private static (List<Miss>, int, int) Bout(Contender red, Contender blue, int seed, float seconds, string pair)
    {
        IFighterControl Make(Contender c, int s) => c.Weapon is null ? c.Create(s) : DisarmedFallback.WithFists(c.Create(s), s);
        using var game = new BoxingGame(VersusLab.Tuning, seed, new BoxingCorners(blue.Name, s => Make(blue, s), s => Make(red, s),
            CpuWeapon: blue.Weapon, PlayerWeapon: red.Weapon, CpuTwoHanded: blue.TwoHanded, PlayerTwoHanded: red.TwoHanded));
        game.MaxContactPasses = Passes;
        var misses = new List<Miss>();
        int steps = 0, checks = 0;
        var round = game.Round;
        var before = new Dictionary<int, BodyState>();
        for (var t = 0f; t < seconds && !game.Player.IsDead && !game.Cpu.IsDead; t += Dt)
        {
            before.Clear();
            var held = game.Weapons.Where(w => !Weapon.IsWorn(w.Kind) && Holder(game, w) is not null).ToList();
            foreach (var w in held)
            {
                before[w.Body.BodyId] = game.World.GetState(w.Body.BodyId);
            }

            foreach (var f in new[] { game.Player, game.Cpu })
            {
                foreach (var part in f.Voxels!.Parts)
                {
                    before[part.Body.BodyId] = game.World.GetState(part.Body.BodyId);
                }
            }

            // 歩の前の、部位の埋まったセルの数（歩の中で削れたら、そのぶん触れていたはず）。
            var filledBefore = new Dictionary<VoxelBody, int>();
            foreach (var f in new[] { game.Player, game.Cpu })
            {
                foreach (var part in f.Voxels!.Parts)
                {
                    filledBefore[part.Body] = part.Body.Grid.FilledCount;
                }
            }

            game.Step(default, Dt);
            steps++;
            if (game.Round != round)
            {
                round = game.Round;
                continue;
            }

            foreach (var w in held)
            {
                var holder = Holder(game, w);
                if (holder is null || !before.TryGetValue(w.Body.BodyId, out var wb))
                {
                    continue;
                }

                var wNow = game.World.GetState(w.Body.BodyId);
                foreach (var victim in new[] { game.Player, game.Cpu })
                {
                    foreach (var part in victim.Voxels!.Parts)
                    {
                        if (victim == holder && (part.Kind == BodyPartKind.Forearm || part.Kind == BodyPartKind.UpperArm))
                        {
                            continue; // 握っている腕は握りで触れている
                        }

                        var pb = before[part.Body.BodyId];
                        var pNow = game.World.GetState(part.Body.BodyId);
                        var reach = Radius(w.Body) + Radius(part.Body);
                        var moved = Vector3.Distance(wb.Position, wNow.Position) + Vector3.Distance(pb.Position, pNow.Position) + 0.02f;
                        if (MathF.Min(Vector3.Distance(wb.Position, pb.Position), Vector3.Distance(wNow.Position, pNow.Position)) - moved > reach)
                        {
                            continue;
                        }

                        checks++;
                        if (game.RecentCuts.Any(c => c.Touched && ((c.A == w.Body && c.B == part.Body) || (c.B == w.Body && c.A == part.Body))))
                        {
                            continue;
                        }

                        // 歩の中で、部位のセルが削れていたら、何かが触れた。
                        if (part.Body.Grid.FilledCount != filledBefore[part.Body])
                        {
                            continue;
                        }

                        var (cells, first, materials) = Brute(w.Body, wb, wNow, part.Body, pb, pNow);
                        if (cells > 0)
                        {
                            var speed = (wNow.LinearVelocity + Vector3.Cross(wNow.AngularVelocity, Weapon.TipOf(w.Kind))).Length();
                            // 同じ姿勢でもう一度解いたら触れるか（触れるなら、ここまでの経路の差。触れないなら解き方の穴）と、手前の振り分けが外したか。
                            var again = VoxelContact.Resolve(w.Body, wb, part.Body, pb);
                            var cullReach = w.Body.Grid.HalfExtents.Length() + part.Body.Grid.HalfExtents.Length() + w.Body.GridCenterLocal.Length() + part.Body.GridCenterLocal.Length();
                            var cullMoved = Vector3.Distance(wb.Position, wNow.Position) + Vector3.Distance(pb.Position, pNow.Position) + 0.01f;
                            var culled = MathF.Min(Vector3.Distance(wb.Position, pb.Position), Vector3.Distance(wNow.Position, pNow.Position)) - cullMoved > cullReach;
                            misses.Add(new Miss(pair, t, $"{(holder == game.Player ? "赤" : "青")}{w.Kind}", victim == holder ? "自分" : "相手", part.Kind, first == 0, cells, speed,
                                $"{materials} (再解:{(again.Touched ? "触れた" : "触れず")} 振り分け:{(culled ? "外した" : "通した")} 初めの刻み {first})"));
                        }
                    }
                }
            }
        }

        return (misses, steps, checks);
    }

    private static Fighter? Holder(BoxingGame game, Weapon weapon) => game.Player == null ? null : game.WeaponOf(game.Player) == weapon ? game.Player : game.WeaponOf(game.Cpu) == weapon ? game.Cpu : null;

    private static float Radius(VoxelBody body) => body.Grid.HalfExtents.Length() + body.GridCenterLocal.Length();

    private static BodyState Lerp(BodyState a, BodyState b, float t) =>
        a with { Position = Vector3.Lerp(a.Position, b.Position, t), Orientation = Quaternion.Normalize(Quaternion.Slerp(a.Orientation, b.Orientation, t)) };

    /// <summary>
    /// 歩の前から後まで <see cref="SubSteps"/> 刻みで姿勢を補い、剣の埋まったセルの中心が、部位の埋まったセルに入った姿勢の数の最大（重なったセル）と、
    /// 初めて入った刻み（0 = 歩の前から埋まっていた）、材質。全てのセルを調べる。
    /// </summary>
    private static (int Cells, int First, string Materials) Brute(VoxelBody weapon, BodyState wb, BodyState wNow, VoxelBody part, BodyState pb, BodyState pNow)
    {
        var wg = weapon.Grid;
        var pg = part.Grid;
        var cells = new List<Vector3>();
        var (nx, ny, nz) = wg.Dimensions;
        for (var z = 0; z < nz; z++)
        {
            for (var y = 0; y < ny; y++)
            {
                for (var x = 0; x < nx; x++)
                {
                    if (wg.At(x, y, z) != 0)
                    {
                        cells.Add(wg.CellCenter(x, y, z));
                    }
                }
            }
        }

        var best = 0;
        var first = -1;
        var materials = new SortedSet<string>(StringComparer.Ordinal);
        for (var k = 0; k <= SubSteps; k++)
        {
            var t = k / (float)SubSteps;
            var toWorld = weapon.GridToWorld(Lerp(wb, wNow, t));
            Matrix4x4.Invert(part.GridToWorld(Lerp(pb, pNow, t)), out var toPart);
            var m = toWorld * toPart;
            var inside = 0;
            foreach (var c in cells)
            {
                var i = pg.CellAt(Vector3.Transform(c, m));
                if (i >= 0 && pg.MaterialAt(i) != 0)
                {
                    inside++;
                    materials.Add(pg.Palette[pg.MaterialAt(i)].Name);
                }
            }

            if (inside > 0 && first < 0)
            {
                first = k;
            }

            best = Math.Max(best, inside);
        }

        return (best, first, string.Join("/", materials));
    }
}
